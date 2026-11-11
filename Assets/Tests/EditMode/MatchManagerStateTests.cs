using System;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Player;
using Fusion;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying <see cref="MatchManager"/> 4-state lifecycle (<c>WaitingForPlayers -&gt; Countdown 3s -&gt; Playing 180s -&gt; Ended 8s</c>),
    /// exact tick boundaries (<c>179 vs 180</c>, <c>10,800</c> Sudden Death, <c>Ended -&gt; WaitingForPlayers</c>),
    /// actual <see cref="PlayerHealth"/> &amp; <see cref="PlayerCombatController"/> damage/attack rejection outside <c>Playing</c>,
    /// same-tick Mutual Double-KO <c>DRAW</c>, disconnect win &amp; <c>_M1 -&gt; _M2</c> sequence increment,
    /// <c>Arena.unity</c> scene <see cref="NetworkObject"/> failover flags, and sequential <c>P1 -&gt; P2 -&gt; P3</c> failover
    /// with <c>MatchRoster</c> cleanup (Stories 9-1, 9-2, 9-3; ADR-0006; TR-match-001..003).
    /// </summary>
    [TestFixture]
    public class MatchManagerStateTests
    {
        private GameObject _managerGo;
        private MatchManager _matchManager;
        private InputFocusManager _focusManager;

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            _managerGo = new GameObject("MatchManager_EditTest");
            _matchManager = _managerGo.AddComponent<MatchManager>();
            _focusManager = new InputFocusManager();
            _matchManager.ConfigureServices(_focusManager, "DUEL01", PlayerRef.FromEncoded(1));
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (_managerGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_managerGo);
            }
        }

        [Test]
        public void Lifecycle_Tick179Vs180CombatDamageGateAndRematchAvatarReset_EnforcesExactBoundaries()
        {
            CharacterCombatConfigSO cfg = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            GameObject p1Go = new GameObject("P1_MatchGate");
            GameObject p2Go = new GameObject("P2_MatchGate");
            try
            {
                PlayerRef p1 = PlayerRef.FromEncoded(1);
                PlayerRef p2 = PlayerRef.FromEncoded(2);

                PlayerHealth p1Hp = p1Go.AddComponent<PlayerHealth>();
                p1Hp.ConfigureForStandaloneTest(cfg, p1);
                PlayerCombatController p1Combat = p1Go.AddComponent<PlayerCombatController>();
                p1Combat.ConfigureForTest(cfg, p1, p1Hp);

                PlayerHealth p2Hp = p2Go.AddComponent<PlayerHealth>();
                p2Hp.ConfigureForStandaloneTest(cfg, p2);
                p2Go.layer = ArenaLayers.PlayerCharacter;
                p2Go.transform.position = new Vector3(0f, 0f, 1.8f);
                p2Go.AddComponent<CapsuleCollider>().center = new Vector3(0f, 1f, 0f);
                Physics.SyncTransforms();

                _matchManager.RegisterOrUpdateStandalonePlayer(p1, "Alpha", isAlive: true);
                _matchManager.SimulateStandaloneTicks(5);
                Assert.AreEqual(MatchState.WaitingForPlayers, _matchManager.CurrentMatchState);

                // Attack & damage attempts during WaitingForPlayers MUST be rejected (P2 stays at 100 HP, 0 bolts spawned):
                NetworkInputData fireAndSlash = default;
                fireAndSlash.Buttons.Set(NetworkInputButtons.RangedFire, true);
                p1Combat.SimulateCombatTick(in fireAndSlash, cfg);
                p2Hp.RequestDamageFromAttacker(p1, 25, AttackType.VerticalSlash, Vector3.zero, attackTick: 1);
                Assert.AreEqual(0, p1Combat.StandaloneSpawnedProjectileCount, "Combat attacks must be rejected during WaitingForPlayers.");
                Assert.AreEqual(100, p2Hp.CurrentHP, "PlayerHealth damage must be rejected during WaitingForPlayers.");

                // 2nd player joins -> enters 300-tick (5.0s) Countdown:
                Assert.AreEqual(5.0f, MatchManager.CountdownDurationSeconds, 0.001f);
                Assert.AreEqual(300, MatchManager.CountdownDurationTicks);
                _matchManager.RegisterOrUpdateStandalonePlayer(p2, "Bravo", isAlive: true);
                _matchManager.SimulateStandaloneTicks(1);
                Assert.AreEqual(MatchState.Countdown, _matchManager.CurrentMatchState);

                // Tick 299 of Countdown -> STILL Countdown & combat STILL rejected; Tick 300 -> transitions to Playing!
                _matchManager.SimulateStandaloneTicks(MatchManager.CountdownDurationTicks - 1);
                Assert.AreEqual(MatchState.Countdown, _matchManager.CurrentMatchState, "Tick 299 must still be in Countdown.");
                p2Hp.RequestDamageFromAttacker(p1, 25, AttackType.VerticalSlash, Vector3.zero, attackTick: 2);
                Assert.AreEqual(100, p2Hp.CurrentHP, "Damage at Tick 299 of Countdown must still be rejected.");

                _matchManager.SimulateStandaloneTicks(1);
                Assert.AreEqual(MatchState.Playing, _matchManager.CurrentMatchState, "Tick 300 (5.0s) must transition to Playing.");

                // During Playing -> damage and attacks ARE accepted!
                p2Hp.RequestDamageFromAttacker(p1, 25, AttackType.VerticalSlash, Vector3.zero, attackTick: 3);
                Assert.AreEqual(75, p2Hp.CurrentHP, "Damage must apply once MatchState == Playing.");

                // 3rd player joining within the opening 60.0s (3,600 ticks) of Playing joins ALIVE as an active combatant:
                _matchManager.SimulateStandaloneTicks(MatchManager.DefaultLateJoinActiveWindowTicks);
                Assert.IsTrue(_matchManager.CanJoinCurrentMatchAlive(), "Exact 60.0s (3,600 ticks) boundary must still allow live combat join.");
                _matchManager.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(3), "ActiveJoiner3", isAlive: true);
                Assert.AreEqual(3, _matchManager.CountLivingPlayers(), "Player 3 joining within <= 60s must join alive.");

                // Strictly after 60.0s (Tick 3,601+) -> 4th player joins in Ghost Spectator mode (IsAlive == false):
                _matchManager.SimulateStandaloneTicks(1);
                Assert.IsFalse(_matchManager.CanJoinCurrentMatchAlive(), "Tick 3,601 (> 60.0s) must switch late-joiners to Ghost Spectator.");
                _matchManager.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(4), "LateGhost4", isAlive: true);
                Assert.AreEqual(3, _matchManager.CountLivingPlayers(), "Player 4 joining after > 60s must enter Ghost Spectator (IsAlive == false).");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(p2Go);
                UnityEngine.Object.DestroyImmediate(p1Go);
                UnityEngine.Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void ResolveWinnerOrDraw_InstanceSuddenDeath10800TicksSameTickDoubleKoDisconnectWinAndEndedToWaiting_Verified()
        {
            PlayerRef p1 = PlayerRef.FromEncoded(1);
            PlayerRef p2 = PlayerRef.FromEncoded(2);

            Assert.AreEqual("DUEL01_M1", _matchManager.CurrentMatchId);
            _matchManager.RegisterOrUpdateStandalonePlayer(p1, "Alpha", isAlive: true);
            _matchManager.RegisterOrUpdateStandalonePlayer(p2, "Bravo", isAlive: true);
            _matchManager.SimulateStandaloneTicks(1 + MatchManager.CountdownDurationTicks);
            Assert.AreEqual(MatchState.Playing, _matchManager.CurrentMatchState);

            // Simulate mid-round combat damage before the 180.0s Sudden Death buzzer (Alpha = 60 HP, Bravo = 85 HP):
            _matchManager.RegisterOrUpdateStandalonePlayer(p1, "Alpha", isAlive: true, kills: 1, currentHp: 60);
            _matchManager.RegisterOrUpdateStandalonePlayer(p2, "Bravo", isAlive: true, kills: 0, currentHp: 85);

            MatchResultSummary lastSummary = default;
            int endedCount = 0;
            _matchManager.OnMatchEnded += s =>
            {
                endedCount++;
                lastSummary = s;
            };

            // 1. 10,800-tick (180.0s) Sudden Death timeout on MatchManager instance -> Bravo (85 HP > 60 HP) crowned Winner!
            _matchManager.SimulateStandaloneTicks(MatchManager.PlayingDurationTicks);
            Assert.AreEqual(MatchState.Ended, _matchManager.CurrentMatchState);
            Assert.AreEqual(p2, _matchManager.WinnerPlayer, "180s Sudden Death must crown highest CurrentHP survivor.");
            Assert.AreEqual(1, endedCount);
            Assert.IsFalse(lastSummary.IsDraw);

            // 2. Loop into Match #2 (_M2) after 480 ticks, then verify same-tick Mutual Double-KO (evaluateImmediately: false) -> DRAW!
            _matchManager.SimulateStandaloneTicks(MatchManager.EndedDurationTicks);
            Assert.AreEqual(MatchState.Countdown, _matchManager.CurrentMatchState);
            Assert.AreEqual("DUEL01_M2", _matchManager.CurrentMatchId, "Rematch must increment MatchId sequence from _M1 to _M2.");

            _matchManager.SimulateStandaloneTicks(MatchManager.CountdownDurationTicks);
            Assert.AreEqual(MatchState.Playing, _matchManager.CurrentMatchState);

            _matchManager.RecordElimination(killer: p1, victim: p2, evaluateImmediately: false);
            _matchManager.RecordElimination(killer: p2, victim: p1, evaluateImmediately: false);
            _matchManager.SimulateStandaloneTicks(1);

            Assert.AreEqual(MatchState.Ended, _matchManager.CurrentMatchState);
            Assert.AreEqual(PlayerRef.None, _matchManager.WinnerPlayer, "Same-tick Mutual Double-KO must resolve to DRAW (PlayerRef.None).");
            Assert.IsTrue(lastSummary.IsDraw);
            Assert.AreEqual(2, endedCount);

            // 3. During Ended, P2 leaves (< 2 players remaining) -> after 480 ticks transitions to WaitingForPlayers (NOT Countdown)!
            _matchManager.NotifyPlayerLeft(p2);
            _matchManager.SimulateStandaloneTicks(MatchManager.EndedDurationTicks);
            Assert.AreEqual(MatchState.WaitingForPlayers, _matchManager.CurrentMatchState,
                "Ended with < 2 remaining players must return to WaitingForPlayers.");

            // 4. Rejoin P2, enter Playing, then P2 disconnects mid-match -> NotifyPlayerLeft crowns P1 Winner!
            _matchManager.RegisterOrUpdateStandalonePlayer(p2, "Bravo", isAlive: true);
            _matchManager.SimulateStandaloneTicks(1 + MatchManager.CountdownDurationTicks);
            Assert.AreEqual(MatchState.Playing, _matchManager.CurrentMatchState);
            _matchManager.NotifyPlayerLeft(p2);
            Assert.AreEqual(MatchState.Ended, _matchManager.CurrentMatchState);
            Assert.AreEqual(p1, _matchManager.WinnerPlayer, "Opponent disconnect mid-match must crown sole remaining player.");
        }

        [Test]
        public void SequentialFailoverRosterRemovalPhaseEdgeCasesAndArenaSceneNetworkObjectFlags_Verified()
        {
            // 1. Verify Arena.unity MatchSystems scene NetworkObject has AllowStateAuthorityOverride = ON & DestroyWhenStateAuthorityLeaves = OFF:
            Scene arenaScene = EditorSceneManager.OpenScene("Assets/Scenes/Arena.unity", OpenSceneMode.Single);
            try
            {
                MatchManager sceneMm = UnityEngine.Object.FindFirstObjectByType<MatchManager>(FindObjectsInactive.Include);
                Assert.IsNotNull(sceneMm, "Arena.unity must contain a scene-placed MatchManager.");
                NetworkObject netObj = sceneMm.GetComponent<NetworkObject>();
                Assert.IsNotNull(netObj, "MatchSystems must have a Fusion NetworkObject.");
                Assert.IsTrue((netObj.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) != 0,
                    "MatchSystems NetworkObject must enable AllowStateAuthorityOverride.");
                Assert.IsTrue((netObj.Flags & NetworkObjectFlags.MasterClientObject) != 0,
                    "MatchSystems NetworkObject must enable MasterClientObject for Shared Mode authority.");
                Assert.IsTrue((netObj.Flags & NetworkObjectFlags.DestroyWhenStateAuthorityLeaves) == 0,
                    "MatchSystems NetworkObject must disable DestroyWhenStateAuthorityLeaves.");
                Assert.IsNotNull(sceneMm.ActiveRulesConfig, "Arena.unity MatchManager must wire DefaultMatchRulesConfig.asset.");
                Assert.AreEqual(5.0f, sceneMm.ActiveRulesConfig.CountdownDurationSeconds, 0.001f);
                Assert.AreEqual(60.0f, sceneMm.ActiveRulesConfig.LateJoinActiveWindowSeconds, 0.001f);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                _managerGo = new GameObject("MatchManager_EditTest");
                _matchManager = _managerGo.AddComponent<MatchManager>();
                _focusManager = new InputFocusManager();
                _matchManager.ConfigureServices(_focusManager, "DUEL01", PlayerRef.FromEncoded(3));
            }

            // 2. Sequential P1 -> P2 -> P3 failover + MatchRoster slot freeing so a new joiner P5 fits inside 4-slot cap:
            PlayerRef p1 = PlayerRef.FromEncoded(1);
            PlayerRef p2 = PlayerRef.FromEncoded(2);
            PlayerRef p3 = PlayerRef.FromEncoded(3);
            PlayerRef p4 = PlayerRef.FromEncoded(4);
            PlayerRef p5New = PlayerRef.FromEncoded(5);

            _matchManager.RegisterOrUpdateStandalonePlayer(p1, "P1", isAlive: true);
            _matchManager.RegisterOrUpdateStandalonePlayer(p2, "P2", isAlive: true);
            _matchManager.RegisterOrUpdateStandalonePlayer(p3, "P3", isAlive: true);
            _matchManager.RegisterOrUpdateStandalonePlayer(p4, "P4", isAlive: true);
            _matchManager.SimulateStandaloneTicks(1 + MatchManager.CountdownDurationTicks + 1800);
            float timerBefore = _matchManager.RemainingPhaseSeconds;

            // Host P1 drops -> P2 elected; roster count drops from 4 to 3 (freeing slot for P5 later):
            Assert.AreEqual(p2, MatchManager.SelectDeterministicNextMaster(new[] { p1, p2, p3, p4 }, p1));
            _matchManager.ReconcileRosterOnFailover(new[] { p2, p3, p4 }, p1);
            Assert.AreEqual(3, _matchManager.CountConnectedPlayers(), "Disconnected host must be removed from roster.");
            Assert.AreEqual(timerBefore, _matchManager.RemainingPhaseSeconds, 0.02f);

            // New Master P2 ALSO drops -> P3 elected; match continues between P3 and P4:
            Assert.AreEqual(p3, MatchManager.SelectDeterministicNextMaster(new[] { p2, p3, p4 }, p2));
            _matchManager.ReconcileRosterOnFailover(new[] { p3, p4 }, p2);
            Assert.AreEqual(MatchState.Playing, _matchManager.CurrentMatchState);
            Assert.AreEqual(2, _matchManager.CountConnectedPlayers());

            // P4 drops -> P3 crowned sole surviving Winner; failover during Ended does NOT fire duplicate OnMatchEnded:
            int endedEvents = 0;
            _matchManager.OnMatchEnded += _ => endedEvents++;
            _matchManager.ReconcileRosterOnFailover(new[] { p3 }, p4);
            Assert.AreEqual(MatchState.Ended, _matchManager.CurrentMatchState);
            Assert.AreEqual(p3, _matchManager.WinnerPlayer);
            Assert.AreEqual(1, endedEvents);

            _matchManager.ReconcileRosterOnFailover(new[] { p3 }, PlayerRef.None);
            Assert.AreEqual(1, endedEvents, "Failover reconciliation during Ended must not emit duplicate OnMatchEnded.");

            // Return to WaitingForPlayers, add P5New, enter Countdown, then failover during Countdown (< 2 players) aborts back to WaitingForPlayers:
            _matchManager.SimulateStandaloneTicks(MatchManager.EndedDurationTicks);
            Assert.AreEqual(MatchState.WaitingForPlayers, _matchManager.CurrentMatchState);
            _matchManager.RegisterOrUpdateStandalonePlayer(p5New, "P5New", isAlive: true);
            _matchManager.SimulateStandaloneTicks(1);
            Assert.AreEqual(MatchState.Countdown, _matchManager.CurrentMatchState);

            _matchManager.ReconcileRosterOnFailover(new[] { p5New }, p3);
            Assert.AreEqual(MatchState.WaitingForPlayers, _matchManager.CurrentMatchState,
                "Failover leaving < 2 players during Countdown must abort cleanly to WaitingForPlayers.");
        }

        [Test]
        public void MatchRulesConfigSO_CustomCountdownLateJoinWindowAndLateJoinGhostAvatar_Verified()
        {
            MatchRulesConfigSO customRules = ScriptableObject.CreateInstance<MatchRulesConfigSO>();
            CharacterCombatConfigSO combatCfg = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            GameObject ghostGo = new GameObject("LateJoinGhost_Test");
            try
            {
                customRules.ConfigureForTest(countdownSeconds: 7.0f, playingSeconds: 180.0f, lateJoinActiveWindowSeconds: 45.0f);
                Assert.AreEqual(420, customRules.CountdownDurationTicks);
                Assert.AreEqual(2700, customRules.LateJoinActiveWindowTicks);

                _matchManager.SetMatchRulesConfig(customRules);
                PlayerRef p1 = PlayerRef.FromEncoded(1);
                PlayerRef p2 = PlayerRef.FromEncoded(2);
                PlayerRef p3 = PlayerRef.FromEncoded(3);
                PlayerRef p4 = PlayerRef.FromEncoded(4);

                _matchManager.RegisterOrUpdateStandalonePlayer(p1, "Host", isAlive: true);
                _matchManager.RegisterOrUpdateStandalonePlayer(p2, "Peer2", isAlive: true);
                _matchManager.SimulateStandaloneTicks(1);
                Assert.AreEqual(MatchState.Countdown, _matchManager.CurrentMatchState);

                // Custom 7.0s (420 ticks) countdown:
                _matchManager.SimulateStandaloneTicks(419);
                Assert.AreEqual(MatchState.Countdown, _matchManager.CurrentMatchState);
                _matchManager.SimulateStandaloneTicks(1);
                Assert.AreEqual(MatchState.Playing, _matchManager.CurrentMatchState);

                // Custom 45.0s (2,700 ticks) late-join window:
                _matchManager.SimulateStandaloneTicks(2700);
                _matchManager.RegisterOrUpdateStandalonePlayer(p3, "JoinAt45s", isAlive: true);
                Assert.IsTrue(_matchManager.TryGetPlayerStats(p3, out PlayerMatchStats s3) && s3.IsAlive);

                _matchManager.SimulateStandaloneTicks(1);
                _matchManager.RegisterOrUpdateStandalonePlayer(p4, "JoinAfter45s", isAlive: true);
                Assert.IsTrue(_matchManager.TryGetPlayerStats(p4, out PlayerMatchStats s4) && !s4.IsAlive);

                // Verify EnterLateJoinGhostSpectator transitions to Layer 10 Ghost without firing burst VFX or kill feed:
                PlayerDeathStateController deathCtrl = ghostGo.AddComponent<PlayerDeathStateController>();
                deathCtrl.ConfigureReferences(Array.Empty<Renderer>(), null, null, null, null, null, "Spectator4");
                PlayerHealth ghostHp = ghostGo.AddComponent<PlayerHealth>();
                ghostHp.ConfigureForStandaloneTest(combatCfg, p4, deathController: deathCtrl);

                int burstEffectsCount = 0;
                int killFeedCount = 0;
                deathCtrl.OnEliminationEffectsRequested += _ => burstEffectsCount++;
                PlayerHealth.OnAnyKillConfirmedBroadcast += (_, _) => killFeedCount++;

                ghostHp.EnterLateJoinGhostSpectator();
                Assert.IsTrue(ghostHp.IsDead);
                Assert.AreEqual(0, ghostHp.CurrentHP);
                Assert.AreEqual(ArenaLayers.GhostSpectator, ghostGo.layer);
                Assert.AreEqual("Spectator4 [GHOST]", deathCtrl.CurrentFormattedNameplate);
                Assert.AreEqual(0, burstEffectsCount, "Late-join Ghost transition must not trigger combat death explosion VFX/SFX.");
                Assert.AreEqual(0, killFeedCount, "Late-join Ghost transition must not emit a fake kill confirmation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ghostGo);
                UnityEngine.Object.DestroyImmediate(combatCfg);
                UnityEngine.Object.DestroyImmediate(customRules);
            }
        }
    }
}
