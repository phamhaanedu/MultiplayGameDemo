using System;
using CapsuleArena.CameraSystem;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying Vertical Slash (<c>25 dmg, 2.5m, 50 deg, cos(25 deg) = 0.9063078</c>),
    /// Horizontal Sweep Left/Right (<c>30 dmg, 3.0m, 90 deg offset +-45 deg, cos(45 deg) = 0.7071068</c>),
    /// <c>0 B</c> steady-state GC allocation, vertical altar tolerance (<c>+0.4m</c> hit vs <c>2.5m</c> reject),
    /// multi-target cleave, 36-tick cooldown &amp; same-tick priority lock, <c>Layer 6</c> wall LoS occlusion,
    /// multi-collider per-swing <see cref="EntityId"/> deduplication, and 11-tick (<c>0.18s</c>) +-90 deg
    /// authoritative yaw snap-turn + synchronous FPS/TPS camera tracking without Tick-11 snap-back
    /// (Stories 7-1, 7-2; ADR-0005; TR-combat-001, TR-combat-002).
    /// </summary>
    [TestFixture]
    public class MeleeSectorMathTests
    {
        private CharacterCombatConfigSO _config;

        private sealed class CountingDamageReceiver : MonoBehaviour, IDamageReceiver
        {
            public PlayerRef OwnerPlayer { get; set; }
            public bool IsDead { get; set; }
            public Transform ReceiverTransform => transform;
            public int HitCount { get; private set; }
            public int TotalDamage { get; private set; }
            public AttackType LastAttackType { get; private set; }

            public void RequestDamageFromAttacker(
                PlayerRef attacker,
                int damage,
                AttackType attackType,
                Vector3 attackerOrigin,
                int attackTick = 0,
                Vector3 hitPoint = default)
            {
                HitCount++;
                TotalDamage += damage;
                LastAttackType = attackType;
            }
        }

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            ArenaLayers.ApplyCollisionMatrix();
            _config = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
            }
        }

        [Test]
        public void IsTargetInsideSector_VerticalSlashBoundariesAndAltarElevation_Accepts24DegAnd0m4AltarRejects26DegAnd2m5Height()
        {
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;

            Vector3 inside24Deg = Quaternion.Euler(0f, 24.0f, 0f) * Vector3.forward * 2.4f;
            Assert.IsTrue(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, inside24Deg, _config.VerticalSlashRange, _config.VerticalSlashCosHalfAngle, 0f));

            Vector3 outside26Deg = Quaternion.Euler(0f, 26.0f, 0f) * Vector3.forward * 2.0f;
            Assert.IsFalse(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, outside26Deg, _config.VerticalSlashRange, _config.VerticalSlashCosHalfAngle, 0f));

            Vector3 outOfRange = new Vector3(0f, 0f, 2.6f);
            Assert.IsFalse(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, outOfRange, _config.VerticalSlashRange, _config.VerticalSlashCosHalfAngle, 0f));

            // Elevated Central Altar (+0.4m <= 2.2m vertical tolerance) -> accepted; extreme vertical delta (2.5m > 2.2m) -> rejected:
            Vector3 onAltar = new Vector3(0f, 0.4f, 2.0f);
            Vector3 tooHigh = new Vector3(0f, 2.5f, 2.0f);
            Assert.IsTrue(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, onAltar, _config.VerticalSlashRange, _config.VerticalSlashCosHalfAngle, 0f));
            Assert.IsFalse(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, tooHigh, _config.VerticalSlashRange, _config.VerticalSlashCosHalfAngle, 0f));
        }

        [Test]
        public void IsTargetInsideSector_HorizontalSweepLeftAndRightQuadrants_CoversExact90DegSideArcs()
        {
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;

            Vector3 leftMid = Quaternion.Euler(0f, -45f, 0f) * Vector3.forward * 2.9f;
            Vector3 leftEdge88 = Quaternion.Euler(0f, -88f, 0f) * Vector3.forward * 2.5f;
            Vector3 rightSide = Quaternion.Euler(0f, +30f, 0f) * Vector3.forward * 2.0f;

            Assert.IsTrue(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, leftMid, _config.HorizontalSweepRange, _config.HorizontalSweepCosHalfAngle, -45f));
            Assert.IsTrue(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, leftEdge88, _config.HorizontalSweepRange, _config.HorizontalSweepCosHalfAngle, -45f));
            Assert.IsFalse(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, rightSide, _config.HorizontalSweepRange, _config.HorizontalSweepCosHalfAngle, -45f));

            Vector3 rightEdge88 = Quaternion.Euler(0f, +88f, 0f) * Vector3.forward * 2.5f;
            Assert.IsTrue(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, rightEdge88, _config.HorizontalSweepRange, _config.HorizontalSweepCosHalfAngle, +45f));
            Assert.IsFalse(MeleeSectorMath.IsTargetInsideSector(
                origin, forward, leftMid, _config.HorizontalSweepRange, _config.HorizontalSweepCosHalfAngle, +45f));
        }

        [Test]
        public void ExecuteMeleeSectorAttack_WallBlocksLoSMultiTargetCleaveDeduplicatesEntityIdAndAllocatesZeroBytes()
        {
            GameObject attackerGo = new GameObject("Attacker_MeleeTest");
            GameObject multiColliderTarget = new GameObject("MultiColliderEnemy");
            GameObject secondOpenTarget = new GameObject("SecondCleaveEnemy");
            GameObject wallBlockedTarget = new GameObject("WallBlockedEnemy");
            GameObject coverWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                attackerGo.transform.position = new Vector3(0f, 30f, 0f);
                PlayerCombatController combat = attackerGo.AddComponent<PlayerCombatController>();
                combat.ConfigureForTest(_config, PlayerRef.FromEncoded(1));

                multiColliderTarget.layer = ArenaLayers.PlayerCharacter;
                multiColliderTarget.transform.position = new Vector3(-1.2f, 30f, 1.8f);
                multiColliderTarget.AddComponent<CapsuleCollider>().center = new Vector3(0f, 1.0f, 0f);
                multiColliderTarget.AddComponent<SphereCollider>().center = new Vector3(0f, 1.6f, 0f);
                CountingDamageReceiver openReceiver1 = multiColliderTarget.AddComponent<CountingDamageReceiver>();
                openReceiver1.OwnerPlayer = PlayerRef.FromEncoded(2);
                DamageReceiverRegistry.Register(multiColliderTarget, openReceiver1);

                secondOpenTarget.layer = ArenaLayers.PlayerCharacter;
                secondOpenTarget.transform.position = new Vector3(-1.8f, 30.4f, 1.0f);
                secondOpenTarget.AddComponent<CapsuleCollider>().center = new Vector3(0f, 1.0f, 0f);
                CountingDamageReceiver openReceiver2 = secondOpenTarget.AddComponent<CountingDamageReceiver>();
                openReceiver2.OwnerPlayer = PlayerRef.FromEncoded(4);
                DamageReceiverRegistry.Register(secondOpenTarget, openReceiver2);

                coverWall.layer = ArenaLayers.EnvironmentWall;
                coverWall.transform.position = new Vector3(0.35f, 31f, 1.95f);
                coverWall.transform.localScale = new Vector3(0.4f, 2.5f, 0.1f);

                wallBlockedTarget.layer = ArenaLayers.PlayerCharacter;
                wallBlockedTarget.transform.position = new Vector3(0.35f, 30f, 2.2f);
                wallBlockedTarget.AddComponent<CapsuleCollider>().center = new Vector3(0f, 1.0f, 0f);
                CountingDamageReceiver blockedReceiver = wallBlockedTarget.AddComponent<CountingDamageReceiver>();
                blockedReceiver.OwnerPlayer = PlayerRef.FromEncoded(3);
                DamageReceiverRegistry.Register(wallBlockedTarget, blockedReceiver);

                Physics.SyncTransforms();

                // Multi-target cleave on HorizontalSweepLeft hits BOTH open left-quadrant enemies for 30 dmg each, 0 on wall-blocked:
                int sweepHits = combat.ExecuteMeleeSectorAttack(
                    MeleeSwingKind.HorizontalSweepLeft,
                    attackerGo.transform.position,
                    Vector3.forward,
                    _config);

                Assert.AreEqual(2, sweepHits, "Horizontal Sweep Left must cleave both open enemies in [-90, 0] deg arc.");
                Assert.AreEqual(1, openReceiver1.HitCount, "Multi-collider enemy must be deduplicated via EntityId.");
                Assert.AreEqual(30, openReceiver1.TotalDamage);
                Assert.AreEqual(1, openReceiver2.HitCount, "+0.4m elevated enemy must be hit.");
                Assert.AreEqual(30, openReceiver2.TotalDamage);
                Assert.AreEqual(0, blockedReceiver.HitCount, "Layer 6 wall Linecast must block through-wall melee hits.");

                // Warm up once, then verify 0 B managed heap allocation across 100 physics sector queries (TR-combat-001):
                combat.ExecuteMeleeSectorAttack(MeleeSwingKind.VerticalSlash, attackerGo.transform.position, Vector3.forward, _config);
                long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    combat.ExecuteMeleeSectorAttack(MeleeSwingKind.VerticalSlash, attackerGo.transform.position, Vector3.forward, _config);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                Assert.AreEqual(0L, allocated, $"ExecuteMeleeSectorAttack must allocate 0 B in steady state, allocated {allocated} B.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(coverWall);
                UnityEngine.Object.DestroyImmediate(wallBlockedTarget);
                UnityEngine.Object.DestroyImmediate(secondOpenTarget);
                UnityEngine.Object.DestroyImmediate(multiColliderTarget);
                UnityEngine.Object.DestroyImmediate(attackerGo);
            }
        }

        [Test]
        public void SimulateCombatTick_VerticalSlash36TickCooldownAndSameTickPriorityLock_FiresStrictlyOneSkillPerTick()
        {
            GameObject attackerGo = new GameObject("Attacker_PriorityAndCooldown");
            GameObject enemyGo = new GameObject("Enemy_PriorityAndCooldown");
            try
            {
                attackerGo.transform.position = new Vector3(0f, 35f, 0f);
                PlayerCombatController combat = attackerGo.AddComponent<PlayerCombatController>();
                combat.ConfigureForTest(_config, PlayerRef.FromEncoded(1));

                enemyGo.layer = ArenaLayers.PlayerCharacter;
                enemyGo.transform.position = new Vector3(0f, 35f, 1.8f);
                enemyGo.AddComponent<CapsuleCollider>().center = new Vector3(0f, 1.0f, 0f);
                CountingDamageReceiver victim = enemyGo.AddComponent<CountingDamageReceiver>();
                victim.OwnerPlayer = PlayerRef.FromEncoded(2);
                DamageReceiverRegistry.Register(enemyGo, victim);
                Physics.SyncTransforms();

                // Same-tick LMB + Key 1 + Key 3 + RMB pressed simultaneously -> VerticalSlash wins priority; Sweep & Ranged do NOT fire!
                NetworkInputData allPressed = default;
                allPressed.Buttons.Set(NetworkInputButtons.VerticalSlash, true);
                allPressed.Buttons.Set(NetworkInputButtons.HorizontalSweepLeft, true);
                allPressed.Buttons.Set(NetworkInputButtons.HorizontalSweepRight, true);
                allPressed.Buttons.Set(NetworkInputButtons.RangedFire, true);

                combat.SimulateCombatTick(in allPressed, _config);
                Assert.AreEqual(1, victim.HitCount);
                Assert.AreEqual(AttackType.VerticalSlash, victim.LastAttackType);
                Assert.IsFalse(combat.IsPerformingHorizontalSweepTurn, "Same-tick priority lock must prevent Horizontal Sweep when Vertical Slash fires.");
                Assert.AreEqual(0, combat.StandaloneSpawnedProjectileCount, "Same-tick priority lock must prevent Ranged Fire when Vertical Slash fires.");

                // 36-tick (0.6s) VerticalSlash cooldown: ticks 1..35 reject LMB; tick 36 accepts LMB again!
                NetworkInputData lmbOnly = default;
                lmbOnly.Buttons.Set(NetworkInputButtons.VerticalSlash, true);
                for (int t = 1; t < 36; t++)
                {
                    combat.SimulateCombatTick(in lmbOnly, _config);
                }

                Assert.AreEqual(1, victim.HitCount, "VerticalSlash must stay gated throughout ticks 1..35 (0.6s cooldown).");
                combat.SimulateCombatTick(in lmbOnly, _config);
                Assert.AreEqual(2, victim.HitCount, "VerticalSlash must fire again on tick 36 once cooldown expires.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemyGo);
                UnityEngine.Object.DestroyImmediate(attackerGo);
            }
        }

        [Test]
        public void SimulateCombatTick_HorizontalSweepLeftAndRightWrapAroundAndCameraSync_TracksAll11TicksWithoutSnapBack()
        {
            GameObject playerGo = new GameObject("SweepTurn_Test");
            GameObject pollerGo = new GameObject("Poller_SweepTest");
            GameObject camGo = new GameObject("Camera_SweepTest");
            Camera cam = camGo.AddComponent<Camera>();
            try
            {
                // Start at +135 deg yaw to verify Key 3 (+90 deg) wraps cleanly across +-180 deg to -135 deg!
                playerGo.transform.SetPositionAndRotation(new Vector3(0f, 40f, 0f), Quaternion.Euler(0f, 135f, 0f));
                PlayerHealth health = playerGo.AddComponent<PlayerHealth>();
                health.ConfigureForStandaloneTest(_config, PlayerRef.FromEncoded(1));
                PlayerMobilityController mobility = playerGo.AddComponent<PlayerMobilityController>();
                LocalInputPoller poller = pollerGo.AddComponent<LocalInputPoller>();
                PlayerCombatController combat = playerGo.AddComponent<PlayerCombatController>();
                combat.ConfigureForTest(_config, PlayerRef.FromEncoded(1), health, mobility, poller);

                CameraController camCtrl = playerGo.AddComponent<CameraController>();
                camCtrl.BindLocalCamera(cam, poller, null, _config);
                camCtrl.SetPerspective(CameraPerspectiveMode.FPS);

                NetworkInputData triggerRight = default;
                triggerRight.LookYaw = 135f;
                triggerRight.Buttons.Set(NetworkInputButtons.HorizontalSweepRight, true);
                combat.SimulateCombatTick(in triggerRight, _config);
                Assert.IsTrue(combat.IsPerformingHorizontalSweepTurn);
                Assert.IsTrue(poller.IsHorizontalYawLocked);

                // Simulate all 11 ticks with noisy stale LookYaw = 10f -> monotonic +8.18 deg/tick, camera tracks synchronously, NO Tick-11 snap-back!
                NetworkInputData noisyInput = default;
                noisyInput.LookYaw = 10f;
                float prevUnwrapped = 135f;
                for (int tick = 1; tick <= 11; tick++)
                {
                    combat.SimulateCombatTick(in noisyInput, _config);
                    mobility.SimulateAuthoritativeTick(in noisyInput, 1f / 60f, _config, groundedOverride: true);
                    camCtrl.StepCameraFrame(0f);

                    float deltaFromPrev = Mathf.DeltaAngle(prevUnwrapped, playerGo.transform.eulerAngles.y);
                    Assert.Greater(deltaFromPrev, 7.5f, $"Tick {tick} yaw step must advance monotonically.");
                    prevUnwrapped += deltaFromPrev;

                    float camVsBody = Mathf.Abs(Mathf.DeltaAngle(playerGo.transform.eulerAngles.y, cam.transform.eulerAngles.y));
                    Assert.Less(camVsBody, 0.25f, $"Camera must stay locked to authoritative sweep yaw at tick {tick} without snap-back.");
                }

                Assert.IsFalse(combat.IsPerformingHorizontalSweepTurn);
                Assert.IsFalse(poller.IsHorizontalYawLocked);
                Assert.AreEqual(-135.0f, Mathf.DeltaAngle(0f, playerGo.transform.eulerAngles.y), 0.1f, "+135 + 90 must wrap cleanly to -135 deg.");

                // Advance cooldown (72 ticks total), switch camera to TPS, trigger Key 1 (-90 deg back toward +135 deg), then test mid-turn death cancel:
                camCtrl.SetPerspective(CameraPerspectiveMode.TPS);
                NetworkInputData idle = default;
                idle.LookYaw = -135f;
                for (int i = 0; i < 65; i++)
                {
                    combat.SimulateCombatTick(in idle, _config);
                }

                NetworkInputData triggerLeft = default;
                triggerLeft.LookYaw = -135f;
                triggerLeft.Buttons.Set(NetworkInputButtons.HorizontalSweepLeft, true);
                combat.SimulateCombatTick(in triggerLeft, _config);
                Assert.IsTrue(combat.IsPerformingHorizontalSweepTurn);

                for (int hitIdx = 1; hitIdx <= 4; hitIdx++)
                {
                    health.RequestDamageFromAttacker(PlayerRef.FromEncoded(2), 25, AttackType.VerticalSlash, new Vector3(0f, 40f, 1.5f), attackTick: hitIdx);
                }

                Assert.IsTrue(health.IsDead);
                Assert.IsFalse(combat.IsPerformingHorizontalSweepTurn);
                Assert.IsFalse(poller.IsHorizontalYawLocked);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(pollerGo);
                UnityEngine.Object.DestroyImmediate(playerGo);
            }
        }
    }
}
