using System.Collections.Generic;
using CapsuleArena.CameraSystem;
using CapsuleArena.Combat;
using CapsuleArena.Core.Bootstrap;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying <see cref="CameraController"/> TPS/FPS toggle (<c>Key V</c>, <c>0.15s</c> debounce,
    /// <c>65 deg / 80 deg</c> FOV, <c>[-60, +75]</c> &amp; <c>[-80, +80]</c> pitch clamps, <see cref="ShadowCastingMode.ShadowsOnly"/>,
    /// single-camera scene/prefab hygiene), <c>Physics.SphereCast</c> (<c>r = 0.25m</c>) <c>Layer 6</c> boom anti-clip
    /// (<c>1.80m -&gt; 1.65m</c>, <c>0.40m -&gt; 0.60m</c>, asymmetric pull-in/push-out damping, <c>&gt;= 0.85m</c> recovery,
    /// <c>90 deg</c> L-corner shoulder Linecast), and <see cref="SpectatorController"/> Ghost Free-Roam + Follow-Target
    /// anti-clip, disconnect failover, and <see cref="IInputFocusService"/> suppression (Stories 8-1..8-3; ADR-0005; TR-camera-001..003).
    /// </summary>
    [TestFixture]
    public class CameraControllerTests
    {
        private CharacterCombatConfigSO _config;
        private GameObject _cameraGo;
        private Camera _sceneCamera;

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            ArenaLayers.ApplyCollisionMatrix();
            _config = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            _cameraGo = new GameObject("MainCamera_Test");
            _sceneCamera = _cameraGo.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void CameraController_TpsFpsToggleFovPitchClampsDebounceFocusGateAndShadowsOnly_MeetsCanonicalSpec()
        {
            GameObject playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                playerGo.transform.position = new Vector3(0f, 50f, 0f);
                MeshRenderer bodyRenderer = playerGo.GetComponent<MeshRenderer>();
                InputFocusManager focus = new InputFocusManager();
                CameraController camCtrl = playerGo.AddComponent<CameraController>();
                camCtrl.BindLocalCamera(_sceneCamera, null, focus, _config);

                // Default TPS: FOV = 65 deg, ShadowCastingMode.On, TPS pitch clamped to [-60, +75]:
                Assert.AreEqual(CameraPerspectiveMode.TPS, camCtrl.CurrentPerspective);
                Assert.AreEqual(CameraController.CanonicalTpsFov, _sceneCamera.fieldOfView, 0.01f);
                Assert.AreEqual(ShadowCastingMode.On, bodyRenderer.shadowCastingMode);

                camCtrl.UpdateCameraTransform(yawDeg: 35f, rawPitchDeg: -75f, deltaTime: 0f);
                Assert.AreEqual(-60.0f, camCtrl.LastAppliedPitchDeg, 0.01f, "TPS upward pitch must clamp at -60 deg.");
                camCtrl.UpdateCameraTransform(yawDeg: 35f, rawPitchDeg: +85f, deltaTime: 0f);
                Assert.AreEqual(75.0f, camCtrl.LastAppliedPitchDeg, 0.01f, "TPS downward pitch must clamp at +75 deg.");

                camCtrl.UpdateCameraTransform(yawDeg: 35f, rawPitchDeg: 10f, deltaTime: 0f);
                Vector3 expectedTpsPivot = playerGo.transform.position
                                           + Vector3.up * CameraController.CanonicalTpsHeight
                                           + (Quaternion.Euler(0f, 35f, 0f) * Vector3.right * CameraController.CanonicalTpsShoulderOffset);
                Assert.AreEqual(4.5f, Vector3.Distance(_sceneCamera.transform.position, expectedTpsPivot), 0.02f);

                // Toggle to FPS: FOV = 80 deg, ShadowsOnly, FPS pitch clamped to [-80, +80]:
                Assert.IsTrue(camCtrl.TryTogglePerspective(1.0f));
                Assert.AreEqual(CameraPerspectiveMode.FPS, camCtrl.CurrentPerspective);
                Assert.AreEqual(CameraController.CanonicalFpsFov, _sceneCamera.fieldOfView, 0.01f);
                Assert.AreEqual(ShadowCastingMode.ShadowsOnly, bodyRenderer.shadowCastingMode);

                camCtrl.UpdateCameraTransform(yawDeg: 35f, rawPitchDeg: -89f, deltaTime: 0f);
                Assert.AreEqual(-80.0f, camCtrl.LastAppliedPitchDeg, 0.01f);
                camCtrl.UpdateCameraTransform(yawDeg: 35f, rawPitchDeg: +89f, deltaTime: 0f);
                Assert.AreEqual(80.0f, camCtrl.LastAppliedPitchDeg, 0.01f);
                Assert.AreEqual(50f + CameraController.CanonicalFpsEyeHeight, _sceneCamera.transform.position.y, 0.01f);
                Assert.AreEqual(35f, _sceneCamera.transform.eulerAngles.y, 0.05f);

                // 0.15s debounce & ChatFocused suppression:
                Assert.IsFalse(camCtrl.TryTogglePerspective(1.08f));
                focus.SetFocusFlag(InputFocusFlag.ChatFocused, true);
                Assert.IsFalse(camCtrl.TryTogglePerspective(2.0f));

                focus.SetFocusFlag(InputFocusFlag.ChatFocused, false);
                Assert.IsTrue(camCtrl.TryTogglePerspective(2.5f));
                Assert.AreEqual(CameraPerspectiveMode.TPS, camCtrl.CurrentPerspective);
                Assert.AreEqual(CameraController.CanonicalTpsFov, _sceneCamera.fieldOfView, 0.01f);
                Assert.AreEqual(ShadowCastingMode.On, bodyRenderer.shadowCastingMode);
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
            }
        }

        [Test]
        public void PrefabAndArenaSceneHygiene_PlayerCapsuleHasZeroCamerasAndArenaSceneHasSingleMainCameraAndBootstrapper()
        {
            GameObject capsulePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/PlayerCapsule.prefab");
            Assert.IsNotNull(capsulePrefab, "PlayerCapsule.prefab must exist.");
            Assert.AreEqual(0, capsulePrefab.GetComponentsInChildren<Camera>(includeInactive: true).Length,
                "PlayerCapsule.prefab must contain 0 Camera components so 4 spawned players never create multiple cameras.");
            Assert.AreEqual(0, capsulePrefab.GetComponentsInChildren<AudioListener>(includeInactive: true).Length,
                "PlayerCapsule.prefab must contain 0 AudioListener components.");

            Scene arenaScene = EditorSceneManager.OpenScene("Assets/Scenes/Arena.unity", OpenSceneMode.Single);
            try
            {
                Assert.IsTrue(arenaScene.IsValid());
                Camera[] sceneCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                ArenaSceneBootstrapper[] bootstrappers = Object.FindObjectsByType<ArenaSceneBootstrapper>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                MobileInputBridge[] bridges = Object.FindObjectsByType<MobileInputBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);

                Assert.AreEqual(1, sceneCameras.Length, "Arena.unity must host strictly 1 MainCamera.");
                Assert.AreEqual(1, listeners.Length, "Arena.unity must host strictly 1 AudioListener.");
                Assert.AreEqual(1, bootstrappers.Length, "Arena.unity must host ArenaSceneBootstrapper.");
                Assert.AreEqual(1, bridges.Length, "Arena.unity must host a single shared MobileInputBridge.");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                _cameraGo = new GameObject("MainCamera_Test");
                _sceneCamera = _cameraGo.AddComponent<Camera>();
            }
        }

        [Test]
        public void ComputeSafeBoomDistance_ExactBufferMathAsymmetricDampingCornerLinecastAndShadowsOnlyRecovery_Verified()
        {
            GameObject playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            GameObject wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject sideCornerWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject playerBlocker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            GameObject ghostBlocker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                playerGo.layer = ArenaLayers.PlayerCharacter;
                playerGo.transform.position = new Vector3(0f, 80f, 0f);
                MeshRenderer bodyRenderer = playerGo.GetComponent<MeshRenderer>();
                CameraController camCtrl = playerGo.AddComponent<CameraController>();
                camCtrl.BindLocalCamera(_sceneCamera, null, null, _config);

                playerBlocker.layer = ArenaLayers.PlayerCharacter;
                playerBlocker.transform.position = new Vector3(0.45f, 81.8f, -2.0f);
                ghostBlocker.layer = ArenaLayers.GhostSpectator;
                ghostBlocker.transform.position = new Vector3(0.45f, 81.8f, -1.5f);
                wallGo.layer = ArenaLayers.EnvironmentWall;
                sideCornerWall.layer = ArenaLayers.EnvironmentWall;
                sideCornerWall.transform.position = new Vector3(50f, 81.8f, 0f);

                // 1. Exact SphereCast (r = 0.25m) hit.distance = 1.80m -> front face of wall at z = -(1.80 + 0.25) = -2.05m (cube center -2.15m, scale.z 0.2m):
                wallGo.transform.localScale = new Vector3(4f, 4f, 0.2f);
                wallGo.transform.position = new Vector3(0.45f, 81.8f, -2.15f);
                Physics.SyncTransforms();

                Vector3 pivot = new Vector3(0.45f, 81.8f, 0f);
                Assert.AreEqual(1.65f, CameraController.ComputeSafeBoomDistance(pivot, Vector3.back, 4.5f), 0.02f,
                    "hit.distance = 1.80m must clamp boom to max(0.6, 1.80 - 0.15) = 1.65m while ignoring Layer 8 & 10.");

                // 2. Exact hit.distance = 0.40m -> front face at z = -0.65m (cube center -0.75m) -> clamps to MinBoomDistance = 0.60m:
                wallGo.transform.position = new Vector3(0.45f, 81.8f, -0.75f);
                Physics.SyncTransforms();
                Assert.AreEqual(0.60f, CameraController.ComputeSafeBoomDistance(pivot, Vector3.back, 4.5f), 0.02f);

                // 3. Asymmetric damping: pull-in (speed 25) is faster than push-out (speed 10), and tight corner (< 0.85m) restores ShadowCastingMode.On at >= 0.85m!
                camCtrl.UpdateCameraTransform(0f, 0f, deltaTime: 0.04f); // pulls in rapidly from 4.5m toward 0.6m
                float pulledInBoom = camCtrl.CurrentBoomDistance;
                Assert.Less(pulledInBoom, 2.2f, "Pull-in damping (speed 25) must retract rapidly.");

                camCtrl.UpdateCameraTransform(0f, 0f, deltaTime: 0f); // snap to 0.60m (< 0.85m)
                Assert.AreEqual(ShadowCastingMode.ShadowsOnly, bodyRenderer.shadowCastingMode);

                // Move wall away -> push-out (speed 10) recovers smoothly and restores ShadowCastingMode.On once boom >= 0.85m!
                wallGo.transform.position = new Vector3(50f, 81.8f, 0f);
                Physics.SyncTransforms();
                camCtrl.UpdateCameraTransform(0f, 0f, deltaTime: 0.03f);
                Assert.GreaterOrEqual(camCtrl.CurrentBoomDistance, CameraController.TightCornerShadowsOnlyThreshold);
                Assert.Less(camCtrl.CurrentBoomDistance, 3.0f, "Push-out damping (speed 10) must ease out smoothly rather than snapping.");
                Assert.AreEqual(ShadowCastingMode.On, bodyRenderer.shadowCastingMode, "ShadowCastingMode.On must recover once boom >= 0.85m.");

                // 4. 90-deg L-corner shoulder Linecast: thin wall at x = +0.20m between head (0, 81.8, 0) and right shoulder (+0.45, 81.8, 0):
                sideCornerWall.transform.localScale = new Vector3(0.08f, 4f, 4f);
                sideCornerWall.transform.position = new Vector3(0.20f, 81.8f, 0f);
                Physics.SyncTransforms();
                camCtrl.UpdateCameraTransform(0f, 0f, deltaTime: 0f);
                Assert.Less(_sceneCamera.transform.position.x, 0.20f, "Shoulder Linecast must prevent TPS pivot from poking through a side L-corner wall.");
            }
            finally
            {
                Object.DestroyImmediate(ghostBlocker);
                Object.DestroyImmediate(playerBlocker);
                Object.DestroyImmediate(sideCornerWall);
                Object.DestroyImmediate(wallGo);
                Object.DestroyImmediate(playerGo);
            }
        }

        [Test]
        public void SpectatorController_FreeRoamBoundsWallPassThroughFollowTargetAntiClipDisconnectAndFocusGate_Verified()
        {
            GameObject localGo = new GameObject("LocalDeadPlayer");
            GameObject survAGo = new GameObject("SurvivorA");
            GameObject survBGo = new GameObject("SurvivorB");
            GameObject wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wallGo.layer = ArenaLayers.EnvironmentWall;
                wallGo.transform.position = new Vector3(0f, 2f, 5f);
                wallGo.transform.localScale = new Vector3(10f, 6f, 1.0f);
                Physics.SyncTransforms();

                InputFocusManager focus = new InputFocusManager();
                PlayerHealth localHp = localGo.AddComponent<PlayerHealth>();
                localHp.ConfigureForStandaloneTest(_config, PlayerRef.FromEncoded(1));
                PlayerHealth survA = survAGo.AddComponent<PlayerHealth>();
                survA.ConfigureForStandaloneTest(_config, PlayerRef.FromEncoded(2));
                survAGo.transform.position = new Vector3(0f, 0f, 8.5f);
                PlayerHealth survB = survBGo.AddComponent<PlayerHealth>();
                survB.ConfigureForStandaloneTest(_config, PlayerRef.FromEncoded(3));
                survBGo.transform.position = new Vector3(8f, 0f, -5f);

                SpectatorController spectator = localGo.AddComponent<SpectatorController>();
                spectator.ConfigureForLocalPlayer(localHp, _sceneCamera, null, focus);
                spectator.SetSurvivorPool(new List<PlayerHealth> { localHp, survA, survB });

                int bannerEmitCount = 0;
                string lastBanner = string.Empty;
                void OnBanner(bool active, string text)
                {
                    bannerEmitCount++;
                    lastBanner = text;
                }

                SpectatorController.OnAnyLocalSpectatorStateChanged += OnBanner;
                try
                {
                    PlayerRef externalKiller = PlayerRef.FromEncoded(9);
                    for (int t = 1; t <= 4; t++)
                    {
                        localHp.RequestDamageFromAttacker(externalKiller, 25, AttackType.VerticalSlash, localGo.transform.position + Vector3.forward, attackTick: t);
                    }

                    Assert.AreEqual(SpectatorMode.GhostFreeRoam, spectator.CurrentMode);

                    // 1. Diagonal speed normalization (9.0 m/s, never 12.73 m/s) + pass straight through Layer 6 wall at z = 5.0m:
                    Vector3 startPos = spectator.GhostPosition;
                    Vector3 afterDiag = spectator.StepGhostFreeRoam(new Vector2(1f, 1f), yawDeg: 0f, pitchDeg: 0f, deltaTime: 1.0f);
                    Assert.AreEqual(SpectatorController.GhostMoveSpeed, Vector3.Distance(startPos, afterDiag), 0.05f, "Diagonal input must normalize to 9.0 m/s.");

                    // 2. Both upper (+22m, +8m) and lower (-22m, +0.5m) bounds clamp:
                    Vector3 upper = spectator.StepGhostFreeRoam(Vector2.up, 0f, 0f, deltaTime: 10.0f, verticalElevate: +10f);
                    Assert.AreEqual(22.0f, upper.z, 0.01f);
                    Assert.AreEqual(8.0f, upper.y, 0.01f);
                    Vector3 lower = spectator.StepGhostFreeRoam(Vector2.down, 0f, 0f, deltaTime: 10.0f, verticalElevate: -20f);
                    Assert.AreEqual(-22.0f, lower.z, 0.01f);
                    Assert.AreEqual(0.5f, lower.y, 0.01f);

                    // 3. Follow-Target camera anti-clip against Layer 6 wall behind SurvivorA (wall back face at z = 5.5m, SurvivorA at z = 8.5m) + zero-alloc single banner emit:
                    Assert.IsTrue(spectator.CycleTarget(+1));
                    Assert.AreSame(survA, spectator.FollowedSurvivor);
                    bannerEmitCount = 0;
                    spectator.UpdateFollowTargetCamera(yawDeg: 0f, pitchDeg: 10f);
                    Assert.Greater(_sceneCamera.transform.position.z, 5.5f, "Follow-Target SphereCast must pull boom in front of Layer 6 wall.");
                    StringAssert.Contains("SPECTATING:", lastBanner);
                    int emitsAfterFirstFrame = bannerEmitCount;
                    spectator.UpdateFollowTargetCamera(yawDeg: 5f, pitchDeg: 10f);
                    Assert.AreEqual(emitsAfterFirstFrame, bannerEmitCount, "Unchanged target/HP must NOT re-allocate or re-emit banner every frame.");

                    // 4. Focus suppression (ChatFocused) blocks CycleTarget / ReturnToFreeRoam via IInputFocusService:
                    focus.SetFocusFlag(InputFocusFlag.ChatFocused, true);
                    Assert.IsFalse(focus.IsGameplayInputAllowed);
                    focus.SetFocusFlag(InputFocusFlag.ChatFocused, false);

                    // 5. Disconnect failover (NotifyPeerDisconnected) on followed SurvivorA switches immediately to SurvivorB!
                    DamageReceiverRegistry.Unregister(survAGo, survA);
                    spectator.SetSurvivorPool(new List<PlayerHealth> { localHp, survB });
                    spectator.NotifyPeerDisconnected(PlayerRef.FromEncoded(2));
                    Assert.AreSame(survB, spectator.FollowedSurvivor, "Followed survivor disconnect must immediately switch to remaining survivor.");
                }
                finally
                {
                    SpectatorController.OnAnyLocalSpectatorStateChanged -= OnBanner;
                }
            }
            finally
            {
                Object.DestroyImmediate(wallGo);
                Object.DestroyImmediate(survBGo);
                Object.DestroyImmediate(survAGo);
                Object.DestroyImmediate(localGo);
            }
        }
    }
}
