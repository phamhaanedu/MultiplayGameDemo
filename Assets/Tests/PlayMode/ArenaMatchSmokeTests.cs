using System.Collections;
using System.Threading.Tasks;
using CapsuleArena.Arena;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Networking;
using CapsuleArena.Networking.Pooling;
using CapsuleArena.Player;
using Fusion;
using Fusion.Photon.Realtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration and smoke tests verifying FusionSessionManager Shared Mode bootstrap arguments,
    /// PhotonAppSettings AppIdFusion wiring, Room Code regex validation, watchdog ConnectionTimeout &amp;
    /// GameIsFull disconnect handling without destroying FusionSessionManager, Max-Min live opponent spawn
    /// selection, and pooled NetworkObject lifecycle across frames (Stories 4-1, 4-2, 4-3; ADR-0003; TR-session-001..003).
    /// </summary>
    [TestFixture]
    public class ArenaMatchSmokeTests
    {
        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
        }

        [Test]
        public void PhotonAppSettings_LoadedFromResources_HasConfiguredFusionAppId()
        {
            PhotonAppSettings settings = Resources.Load<PhotonAppSettings>("PhotonAppSettings");
            Assert.IsNotNull(settings, "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset must exist.");
            Assert.IsNotNull(settings.AppSettings);
            Assert.AreEqual(
                "fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9",
                settings.AppSettings.AppIdFusion,
                "Photon Fusion App ID must match configured project credentials.");
        }

        [Test]
        public void ValidateRoomCode_ValidInvalidAndEmptyQuickJoin_EnforcesRegexContract()
        {
            Assert.IsTrue(FusionSessionManager.ValidateRoomCode("", allowEmptyQuickJoin: true));
            Assert.IsTrue(FusionSessionManager.ValidateRoomCode("ARENA_01", allowEmptyQuickJoin: false));
            Assert.IsTrue(FusionSessionManager.ValidateRoomCode("duel-room-2026", allowEmptyQuickJoin: false));

            Assert.IsFalse(FusionSessionManager.ValidateRoomCode("", allowEmptyQuickJoin: false));
            Assert.IsFalse(FusionSessionManager.ValidateRoomCode("ab", allowEmptyQuickJoin: false), "Too short (<3).");
            Assert.IsFalse(FusionSessionManager.ValidateRoomCode("ROOM_CODE_TOO_LONG_123", allowEmptyQuickJoin: false), "Too long (>16).");
            Assert.IsFalse(FusionSessionManager.ValidateRoomCode("BAD CODE!", allowEmptyQuickJoin: false), "Spaces/symbols forbidden.");
        }

        [Test]
        public void BuildStartGameArgs_ConfiguresSharedModeFourPlayerCapAndObjectProvider()
        {
            GameObject host = new GameObject("SessionArgs_Test");
            try
            {
                FusionObjectPoolProvider provider = host.AddComponent<FusionObjectPoolProvider>();
                NetworkSceneManagerDefault sceneMgr = host.AddComponent<NetworkSceneManagerDefault>();

                StartGameArgs args = FusionSessionManager.BuildStartGameArgs(
                    "room_alpha",
                    SceneRef.FromIndex(0),
                    provider,
                    sceneMgr);

                Assert.AreEqual(GameMode.Shared, args.GameMode);
                Assert.AreEqual("ROOM_ALPHA", args.SessionName);
                Assert.AreEqual(4, args.PlayerCount);
                Assert.AreSame(provider, args.ObjectProvider);

                StartGameArgs quickJoinArgs = FusionSessionManager.BuildStartGameArgs(
                    "   ",
                    SceneRef.FromIndex(0),
                    provider,
                    sceneMgr);
                Assert.IsNull(quickJoinArgs.SessionName, "Blank Room Code must map to null SessionName for Quick Join.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShouldSpawnLocalAvatar_OnlyReturnsTrueOnceForMatchingLocalPlayerRef()
        {
            PlayerRef p1 = PlayerRef.FromEncoded(1);
            PlayerRef p2 = PlayerRef.FromEncoded(2);

            Assert.IsFalse(FusionSessionManager.ShouldSpawnLocalAvatar(p2, p1, alreadySpawnedForLocal: false));
            Assert.IsTrue(FusionSessionManager.ShouldSpawnLocalAvatar(p1, p1, alreadySpawnedForLocal: false));
            Assert.IsFalse(FusionSessionManager.ShouldSpawnLocalAvatar(p1, p1, alreadySpawnedForLocal: true));
            Assert.IsFalse(FusionSessionManager.ShouldSpawnLocalAvatar(PlayerRef.None, PlayerRef.None, alreadySpawnedForLocal: false));
        }

        [UnityTest]
        public IEnumerator SessionLifecycle_InvalidCodeTimeoutAndGameIsFull_FiresSingleDisconnectAndPreservesManagerHost()
        {
            GameObject managerGo = new GameObject("FusionSessionManager_SmokeTest");
            FusionObjectPoolProvider pool = managerGo.AddComponent<FusionObjectPoolProvider>();
            ArenaSpawnManager spawnMgr = managerGo.AddComponent<ArenaSpawnManager>();
            FusionSessionManager sessionMgr = managerGo.AddComponent<FusionSessionManager>();
            InputFocusManager focus = new InputFocusManager();
            sessionMgr.ConfigureDependencies(pool, spawnMgr, null, focus);

            int disconnectCount = 0;
            string lastReason = string.Empty;
            sessionMgr.OnSessionDisconnected += reason =>
            {
                disconnectCount++;
                lastReason = reason;
            };

            // 1. Invalid Room Code -> immediate rejection without allocating runner:
            Task<bool> invalidTask = sessionMgr.StartSharedSessionAsync("BAD ROOM!");
            while (!invalidTask.IsCompleted) yield return null;

            Assert.IsFalse(invalidTask.Result);
            Assert.AreEqual(1, disconnectCount);
            Assert.AreEqual("InvalidRoomCode", lastReason);

            // 2. Watchdog ConnectionTimeout -> fast 0.05s timeout aborts stalled handshake, resets focus flags, keeps manager alive!
            focus.SetFocusFlag(InputFocusFlag.ChatFocused, true);
            TaskCompletionSource<StartGameResult> stalledHandshake = new TaskCompletionSource<StartGameResult>();
            sessionMgr.ConfigureSessionTestHooks(_ => stalledHandshake.Task, timeoutSecondsOverride: 0.05f);

            Task<bool> timeoutTask = sessionMgr.StartSharedSessionAsync("ARENA_TIMEOUT");
            while (!timeoutTask.IsCompleted) yield return null;
            yield return null;

            Assert.IsFalse(timeoutTask.Result);
            Assert.AreEqual(2, disconnectCount, "Timeout must fire OnSessionDisconnected strictly once.");
            Assert.AreEqual("ConnectionTimeout", lastReason);
            Assert.IsTrue(focus.IsGameplayInputAllowed, "Session shutdown must clear stuck UI focus flags.");
            Assert.IsTrue(sessionMgr != null && sessionMgr.gameObject != null, "Shutting down child NetworkRunner must NOT destroy FusionSessionManager!");

            // 3. Room Full (4/4) ShutdownReason callback -> deduplicated single notification on new attempt:
            TaskCompletionSource<StartGameResult> secondHandshake = new TaskCompletionSource<StartGameResult>();
            sessionMgr.ConfigureSessionTestHooks(_ => secondHandshake.Task, timeoutSecondsOverride: 5.0f);
            _ = sessionMgr.StartSharedSessionAsync("FULL_ROOM");
            sessionMgr.OnShutdown(null, ShutdownReason.GameIsFull);
            sessionMgr.OnShutdown(null, ShutdownReason.GameIsFull);

            Assert.AreEqual(3, disconnectCount, "Duplicate OnShutdown callbacks must fire OnSessionDisconnected strictly once.");
            Assert.AreEqual(ShutdownReason.GameIsFull.ToString(), lastReason);

            Object.Destroy(managerGo);
        }

        [Test]
        public void SelectOptimalSpawnPose_WithThreeRegisteredLivingOpponents_ChoosesMaxMinOppositeSpawn()
        {
            CharacterCombatConfigSO cfg = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            GameObject host = new GameObject("SpawnSelection_SessionTest");
            GameObject opp0 = new GameObject("Opp_0");
            GameObject opp1 = new GameObject("Opp_1");
            GameObject opp7 = new GameObject("Opp_7");
            try
            {
                ArenaSpawnManager spawnMgr = host.AddComponent<ArenaSpawnManager>();
                spawnMgr.RefreshSpawnPointCache();
                FusionSessionManager sessionMgr = host.AddComponent<FusionSessionManager>();
                sessionMgr.ConfigureDependencies(null, spawnMgr, null, null);

                Vector3[] canonical = ArenaSpawnManager.CalculateCanonicalSpawnPositions();
                opp0.transform.position = canonical[0];
                opp1.transform.position = canonical[1];
                opp7.transform.position = canonical[7];

                opp0.AddComponent<PlayerHealth>().ConfigureForStandaloneTest(cfg, PlayerRef.FromEncoded(1));
                opp1.AddComponent<PlayerHealth>().ConfigureForStandaloneTest(cfg, PlayerRef.FromEncoded(2));
                opp7.AddComponent<PlayerHealth>().ConfigureForStandaloneTest(cfg, PlayerRef.FromEncoded(3));

                Pose chosen = sessionMgr.SelectOptimalSpawnPose(fallbackIndex: 0);
                Assert.AreEqual(canonical[4].x, chosen.position.x, 0.01f, "Must select West spawn index 4 (-16, 0.05, 0) opposite East cluster.");
                Assert.AreEqual(canonical[4].z, chosen.position.z, 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(opp7);
                Object.DestroyImmediate(opp1);
                Object.DestroyImmediate(opp0);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cfg);
            }
        }

        [UnityTest]
        public IEnumerator PooledObjectLifecycle_PrewarmAcquireAndReleaseAcrossFrames_ZeroRuntimeDestroyCalls()
        {
            GameObject poolGo = new GameObject("PlayModePool_Test");
            FusionObjectPoolProvider pool = poolGo.AddComponent<FusionObjectPoolProvider>();

            GameObject prefabGo = new GameObject("PooledNetworkProjectile_Prefab");
            prefabGo.SetActive(false);
            NetworkObject prefabNet = prefabGo.AddComponent<NetworkObject>();

            pool.Prewarm(prefabNet, 32);
            yield return null;

            Vector3 muzzlePosition = new Vector3(0f, 1.1f, 0.75f);
            NetworkObject acquired = pool.AcquireFromPool(prefabNet, muzzlePosition, Quaternion.identity);
            yield return null;

            Assert.IsNotNull(acquired);
            Assert.IsTrue(acquired.gameObject.activeSelf);
            Assert.AreEqual(muzzlePosition, acquired.transform.position);
            Assert.AreEqual(0, pool.RuntimeInstantiateCallsAfterPrewarm);

            pool.ReleaseToPool(acquired);
            yield return null;

            Assert.IsFalse(acquired.gameObject.activeSelf);
            Assert.AreEqual(32, pool.GetInactiveCount(prefabNet));
            Assert.AreEqual(0, pool.RuntimeDestroyCalls);

            pool.ClearAndDestroyAllImmediate();
            Object.Destroy(prefabGo);
            Object.Destroy(poolGo);
        }

        [UnityTest]
        public IEnumerator MatchManagerAuthorityFailover_MidMatchHostDisconnectAcrossFrames_PreservesTimerAndCrownsSurvivor()
        {
            GameObject mmGo = new GameObject("MatchManager_PlayModeFailover");
            MatchManager mm = mmGo.AddComponent<MatchManager>();
            InputFocusManager focus = new InputFocusManager();

            PlayerRef p1Host = PlayerRef.FromEncoded(1);
            PlayerRef p2Next = PlayerRef.FromEncoded(2);
            PlayerRef p3Peer = PlayerRef.FromEncoded(3);
            mm.ConfigureServices(focus, "FAILOVER_PM", p2Next);

            mm.RegisterOrUpdateStandalonePlayer(p1Host, "Host_1", isAlive: true);
            mm.RegisterOrUpdateStandalonePlayer(p2Next, "NextMaster_2", isAlive: true);
            mm.RegisterOrUpdateStandalonePlayer(p3Peer, "Peer_3", isAlive: true);
            yield return null;

            mm.SimulateStandaloneTicks(1 + MatchManager.CountdownDurationTicks + 1200);
            Assert.AreEqual(MatchState.Playing, mm.CurrentMatchState);
            float timerBefore = mm.RemainingPhaseSeconds;

            // Host_1 disconnects mid-match; NextMaster_2 reconciles within 1 frame and match continues seamlessly:
            PlayerRef elected = MatchManager.SelectDeterministicNextMaster(new[] { p1Host, p2Next, p3Peer }, p1Host);
            Assert.AreEqual(p2Next, elected);

            mm.ReconcileRosterOnFailover(new[] { p2Next, p3Peer }, p1Host);
            yield return null;

            Assert.AreEqual(MatchState.Playing, mm.CurrentMatchState);
            Assert.AreEqual(timerBefore, mm.RemainingPhaseSeconds, 0.05f);

            // Peer_3 disconnects -> NextMaster_2 immediately crowned sole surviving Winner:
            mm.ReconcileRosterOnFailover(new[] { p2Next }, p3Peer);
            yield return null;

            Assert.AreEqual(MatchState.Ended, mm.CurrentMatchState);
            Assert.AreEqual(p2Next, mm.WinnerPlayer);

            Object.Destroy(mmGo);
        }
    }
}
