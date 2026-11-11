using System.Reflection;
using CapsuleArena.CameraSystem;
using CapsuleArena.Combat;
using CapsuleArena.Core.Bootstrap;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Networking;
using CapsuleArena.Player;
using CapsuleArena.UI.Lobby;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// Regression tests for asymmetric Shared Mode joining-client bugs (<c>TASK-NET-CLIENT-SYNC</c>):
    /// 1. Camera stuck at overview <c>(0, 22, -26)</c> + unresponsive <c>Key V</c> when <see cref="MatchManager"/> is unspawned at join time.
    /// 2. Multi-poll <c>OnInput</c> latch loss vs deterministic <see cref="LocalInputPoller.SampleSimulationTickInput"/> across Mobility &amp; Combat.
    /// 3. Self-healing horizontal mouse yaw unlock after Horizontal Sweep completion or mid-sweep interruption.
    /// 4. Late-join 60s active combatant window preservation and queued <c>DisplayName</c> registration.
    /// </summary>
    [TestFixture]
    public class JoiningClientSyncAndControlsTests
    {
        private CharacterCombatConfigSO _combatConfig;
        private GameObject _cameraGo;
        private Camera _sceneCamera;

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            ArenaLayers.ApplyCollisionMatrix();
            _combatConfig = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            _cameraGo = new GameObject("JoiningClient_MainCamera");
            _sceneCamera = _cameraGo.AddComponent<Camera>();
            // Reproduce exact overview pose from user bug screenshot (0, 22, -26).
            _cameraGo.transform.position = new Vector3(0f, 22f, -26f);
            _cameraGo.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
            if (_combatConfig != null) Object.DestroyImmediate(_combatConfig);
        }

        [Test]
        public void JoiningClientBootstrap_WhenMatchManagerNotYetSpawned_BindsCameraLeavesOverviewPoseAndEnablesKeyV()
        {
            GameObject systemsGo = new GameObject("JoiningClient_Systems");
            GameObject matchGo = new GameObject("MatchSystems_Unspawned");
            GameObject lobbyGo = new GameObject("LobbyRoot");
            GameObject avatarGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            try
            {
                avatarGo.transform.position = new Vector3(0f, 1.0f, 0f);
                LocalInputPoller poller = systemsGo.AddComponent<LocalInputPoller>();
                FusionSessionManager sessionMgr = systemsGo.AddComponent<FusionSessionManager>();
                MatchManager matchMgr = matchGo.AddComponent<MatchManager>();
                LobbyViewController lobbyView = lobbyGo.AddComponent<LobbyViewController>();

                NetworkObject netObj = avatarGo.AddComponent<NetworkObject>();
                CharacterController cc = avatarGo.AddComponent<CharacterController>();
                PlayerMobilityController mobility = avatarGo.AddComponent<PlayerMobilityController>();
                PlayerHealth health = avatarGo.AddComponent<PlayerHealth>();
                PlayerCombatController combat = avatarGo.AddComponent<PlayerCombatController>();
                CameraController camCtrl = avatarGo.AddComponent<CameraController>();
                SpectatorController specCtrl = avatarGo.AddComponent<SpectatorController>();

                PlayerRef joiningClient = PlayerRef.FromEncoded(2);
                mobility.ConfigureReferences(_combatConfig, cc, avatarGo.transform, avatarGo.transform);
                health.ConfigureForStandaloneTest(_combatConfig, joiningClient, hasStateAuthority: true, isMatchPlaying: true);
                combat.ConfigureForTest(_combatConfig, joiningClient, health, mobility, poller);

                ArenaSceneBootstrapper bootstrapper = systemsGo.AddComponent<ArenaSceneBootstrapper>();
                SetPrivateField(bootstrapper, "_isBootstrapped", false);
                SetPrivateField(bootstrapper, "_combatConfig", _combatConfig);
                SetPrivateField(bootstrapper, "_mainCamera", _sceneCamera);
                SetPrivateField(bootstrapper, "_inputPoller", poller);
                SetPrivateField(bootstrapper, "_sessionManager", sessionMgr);
                SetPrivateField(bootstrapper, "_matchManager", matchMgr);
                SetPrivateField(bootstrapper, "_lobbyView", lobbyView);
                bootstrapper.BootstrapSceneServices();

                matchMgr.SubmitPlayerDisplayName(joiningClient, "ClientJoiner02");
                Assert.AreEqual("ClientJoiner02", matchMgr.PendingDisplayName,
                    "DisplayName submitted before MatchManager.Spawned() must be queued for automatic RPC flush.");

                bootstrapper.HandleLocalPlayerSpawned(joiningClient, netObj);

                Assert.That(Vector3.Distance(_sceneCamera.transform.position, new Vector3(0f, 22f, -26f)), Is.GreaterThan(15f),
                    "MainCamera must immediately snap away from the sky overview (0, 22, -26) onto the local capsule TPS rig.");
                Assert.AreEqual(CameraPerspectiveMode.TPS, camCtrl.CurrentPerspective);
                Assert.AreEqual(CameraController.CanonicalTpsFov, _sceneCamera.fieldOfView, 0.01f);

                bool toggledToFps = camCtrl.TryTogglePerspective(10f);
                Assert.IsTrue(toggledToFps, "Key V perspective toggle must work on joining clients once spawned.");
                Assert.AreEqual(CameraPerspectiveMode.FPS, camCtrl.CurrentPerspective);
                Assert.AreEqual(CameraController.CanonicalFpsFov, _sceneCamera.fieldOfView, 0.01f);

                camCtrl.SetPerspective(CameraPerspectiveMode.TPS, forceRefresh: true);
            }
            finally
            {
                Object.DestroyImmediate(avatarGo);
                Object.DestroyImmediate(lobbyGo);
                Object.DestroyImmediate(matchGo);
                Object.DestroyImmediate(systemsGo);
            }
        }

        [Test]
        public void SampleSimulationTickInput_SurvivesMultiPollOnInputAndDeliversIdenticalSnapshotToMobilityAndCombat()
        {
            GameObject go = new GameObject("PollerHost");
            try
            {
                InputFocusManager focus = new InputFocusManager();
                KeybindingManager keys = new KeybindingManager();
                LocalInputPoller poller = go.AddComponent<LocalInputPoller>();
                poller.Initialize(keys, focus);
                poller.SetLookAngles(0f, 0f);

                // Player moves mouse +42 deg yaw and taps LMB VerticalSlash + Key 1 SweepLeft + Space Jump.
                poller.ApplyLookDeltaDegrees(new Vector2(42f, -10f));
                poller.LatchButton(NetworkInputButtons.VerticalSlash);
                poller.LatchButton(NetworkInputButtons.HorizontalSweepLeft);
                poller.LatchButton(NetworkInputButtons.Jump);

                // Simulate Photon Fusion Simulation+Client polling OnInput multiple ticks in the same frame before FixedUpdateNetwork.
                poller.CollectNetworkInput();
                poller.CollectNetworkInput();
                poller.CollectNetworkInput();

                // Both Mobility and Combat sample tick 1024 on StateAuthority in the same simulation step:
                NetworkInputData mobilityInput = poller.SampleSimulationTickInput(1024);
                NetworkInputData combatInput = poller.SampleSimulationTickInput(1024);

                Assert.AreEqual(42f, mobilityInput.LookYaw, 0.01f, "Mobility must receive exact local mouse LookYaw.");
                Assert.AreEqual(42f, combatInput.LookYaw, 0.01f, "Combat must receive exact local mouse LookYaw.");
                Assert.IsTrue(mobilityInput.Buttons.IsSet((int)NetworkInputButtons.Jump), "Jump latch must not be wiped by OnInput.");
                Assert.IsTrue(combatInput.Buttons.IsSet((int)NetworkInputButtons.VerticalSlash), "VerticalSlash latch must reach Combat.");
                Assert.IsTrue(combatInput.Buttons.IsSet((int)NetworkInputButtons.HorizontalSweepLeft), "SweepLeft latch must reach Combat.");

                // Next simulation tick 1025 must have cleared the consumed one-shot latches cleanly:
                NetworkInputData nextTickInput = poller.SampleSimulationTickInput(1025);
                Assert.IsFalse(nextTickInput.Buttons.IsSet((int)NetworkInputButtons.VerticalSlash));
                Assert.IsFalse(nextTickInput.Buttons.IsSet((int)NetworkInputButtons.HorizontalSweepLeft));
                Assert.IsFalse(nextTickInput.Buttons.IsSet((int)NetworkInputButtons.Jump));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HorizontalSweepYawLock_ReleasesCleanlyOnCompletionAndSelfHealsIfInterruptedByDeath()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                InputFocusManager focus = new InputFocusManager();
                KeybindingManager keys = new KeybindingManager();
                LocalInputPoller poller = go.AddComponent<LocalInputPoller>();
                poller.Initialize(keys, focus);
                poller.SetLookAngles(0f, 0f);

                CharacterController cc = go.AddComponent<CharacterController>();
                PlayerMobilityController mobility = go.AddComponent<PlayerMobilityController>();
                PlayerHealth health = go.AddComponent<PlayerHealth>();
                PlayerCombatController combat = go.AddComponent<PlayerCombatController>();

                PlayerRef clientRef = PlayerRef.FromEncoded(3);
                mobility.ConfigureReferences(_combatConfig, cc, go.transform, go.transform);
                health.ConfigureForStandaloneTest(_combatConfig, clientRef, hasStateAuthority: true, isMatchPlaying: true);
                combat.ConfigureForTest(_combatConfig, clientRef, health, mobility, poller);

                NetworkButtons sweepButtons = default;
                sweepButtons.Set((int)NetworkInputButtons.HorizontalSweepLeft, true);
                NetworkInputData startInput = LocalInputPoller.MergeInputs(Vector2.zero, sweepButtons, Vector2.zero, default, 0f, 0f, true);
                combat.SimulateCombatTick(in startInput, _combatConfig);
                Assert.IsTrue(poller.IsHorizontalYawLocked, "Horizontal yaw must lock at the start of Horizontal Sweep.");

                // Complete all 11 ticks + visual duration:
                NetworkInputData idleInput = LocalInputPoller.MergeInputs(Vector2.zero, default, Vector2.zero, default, -90f, 0f, true);
                for (int i = 0; i < _combatConfig.HorizontalTurnTicks + 2; i++)
                {
                    combat.SimulateCombatTick(in idleInput, _combatConfig);
                    combat.StepVisualHorizontalSweepRender(1f / 60f);
                }

                Assert.IsFalse(poller.IsHorizontalYawLocked, "Yaw lock must release after Horizontal Sweep finishes.");
                poller.ApplyLookDeltaDegrees(new Vector2(30f, 0f));
                Assert.AreEqual(-60f, poller.CurrentYaw, 0.5f, "Mouse horizontal rotation must work freely after sweep completion.");

                // Now simulate mid-sweep elimination and verify yaw lock is immediately released (never stuck):
                health.ConfigureForStandaloneTest(_combatConfig, clientRef, hasStateAuthority: true, isMatchPlaying: true);
                for (int cd = 0; cd < 95; cd++)
                {
                    combat.SimulateCombatTick(in idleInput, _combatConfig);
                }

                combat.SimulateCombatTick(in startInput, _combatConfig);
                Assert.IsTrue(poller.IsHorizontalYawLocked);
                health.EnterLateJoinGhostSpectator();
                combat.StepVisualHorizontalSweepRender(1f / 60f);
                Assert.IsFalse(poller.IsHorizontalYawLocked, "Yaw lock must self-heal immediately if player dies mid-sweep.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private serialized field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
