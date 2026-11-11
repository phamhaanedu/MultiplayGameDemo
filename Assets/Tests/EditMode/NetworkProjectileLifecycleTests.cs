using CapsuleArena.Combat;
using CapsuleArena.Combat.Projectile;
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
    /// EditMode integration tests verifying <see cref="NetworkProjectile"/> pooled state scrubbing
    /// (<see cref="TrailRenderer"/> origin-streak prevention across distant respawns, Rigidbody velocity reset,
    /// <c>_hasDealtDamage</c> scrub), 120-tick 30m lifetime, <c>Layer 6</c> wall despawn, shooter self-hit ignore,
    /// owner-disconnect mid-flight despawn, <c>Layer 8</c> single-hit <see cref="DamageReceiverRegistry"/> resolution,
    /// 24-tick (<c>0.4s</c>) ranged cooldown, flush-wall muzzle guard, 3D vertical pitch aim ray, and Dodge Roll lock
    /// (Stories 4-3, 7-3; ADR-0003, ADR-0005; TR-session-003, TR-combat-003).
    /// </summary>
    [TestFixture]
    public class NetworkProjectileLifecycleTests
    {
        private CharacterCombatConfigSO _config;
        private GameObject _projectileGo;
        private NetworkProjectile _projectile;
        private TrailRenderer _trail;
        private Rigidbody _rb;

        private sealed class MockDamageReceiver : MonoBehaviour, IDamageReceiver
        {
            public PlayerRef OwnerPlayer { get; set; }
            public bool IsDead { get; set; }
            public Transform ReceiverTransform => transform;
            public int HitCount { get; private set; }
            public int LastDamage { get; private set; }
            public AttackType LastAttackType { get; private set; }
            public Vector3 LastHitPoint { get; private set; }

            public void RequestDamageFromAttacker(
                PlayerRef attacker,
                int damage,
                AttackType attackType,
                Vector3 attackerOrigin,
                int attackTick = 0,
                Vector3 hitPoint = default)
            {
                HitCount++;
                LastDamage = damage;
                LastAttackType = attackType;
                LastHitPoint = hitPoint;
            }
        }

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            ArenaLayers.ApplyCollisionMatrix();
            _config = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();

            _projectileGo = new GameObject("PooledProjectile_Test");
            _trail = _projectileGo.AddComponent<TrailRenderer>();
            _rb = _projectileGo.AddComponent<Rigidbody>();
            _rb.useGravity = false;
            _projectile = _projectileGo.AddComponent<NetworkProjectile>();
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (_projectileGo != null) Object.DestroyImmediate(_projectileGo);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void PoolRecycleAcrossDistantMuzzles_KeepsTrailDisabledAtOriginAndClearsStreakAtNewMuzzle()
        {
            _rb.linearVelocity = new Vector3(12f, -3f, 8f);
            _rb.angularVelocity = new Vector3(2f, 5f, 1f);

            Vector3 firstMuzzle = new Vector3(15f, 1f, 15f);
            _projectileGo.transform.position = firstMuzzle;
            _projectile.OnPoolAcquire();
            Assert.IsFalse(_trail.emitting);

            _projectile.InitializeLaunch(firstMuzzle, Vector3.forward, _config);
            Assert.IsTrue(_trail.emitting);
            _projectile.SimulateStandaloneTick(1f / 60f, PlayerRef.None, firstMuzzle, _config);

            _projectile.OnPoolRelease();
            Assert.AreEqual(Vector3.zero, _rb.linearVelocity);
            Assert.AreEqual(Vector3.zero, _rb.angularVelocity);
            Assert.IsFalse(_trail.emitting);
            Assert.AreEqual(0, _trail.positionCount);
            Assert.IsFalse(_projectile.HasDealtDamage);
            Assert.AreEqual(0, _projectile.RemainingSimulatedTicks);

            _projectileGo.transform.position = Vector3.zero;
            _projectile.OnPoolAcquire();
            Assert.IsFalse(_trail.emitting);

            Vector3 secondMuzzle = new Vector3(-10f, 1f, -10f);
            _projectileGo.transform.position = secondMuzzle;
            _projectile.InitializeLaunch(secondMuzzle, Vector3.right, _config);

            Assert.AreEqual(0, _trail.positionCount);
            Assert.IsTrue(_trail.emitting);
            Assert.AreEqual(120, _projectile.RemainingSimulatedTicks);
        }

        [Test]
        public void SimulateStandaloneTick_UnobstructedFlightAndOwnerDisconnect_ExpiresAt120TicksOrImmediatelyOnInvalidOwner()
        {
            _projectileGo.transform.position = new Vector3(0f, 50f, 0f);
            _projectile.OnPoolAcquire();
            _projectile.InitializeLaunch(new Vector3(0f, 50f, 0f), Vector3.forward, _config);

            const float dt = 1f / 60f;
            int ticksRun = 0;
            bool despawned = false;

            while (!despawned && ticksRun < 200)
            {
                ticksRun++;
                despawned = _projectile.SimulateStandaloneTick(dt, PlayerRef.None, new Vector3(0f, 50f, 0f), _config);
            }

            Assert.IsTrue(despawned);
            Assert.AreEqual(120, ticksRun);
            Assert.AreEqual(30.0f, _projectileGo.transform.position.z, 0.05f);

            _projectile.OnPoolRelease();
            _projectile.OnPoolAcquire();
            _projectile.InitializeLaunch(new Vector3(0f, 50f, 0f), Vector3.forward, _config);
            bool disconnectedDespawn = _projectile.SimulateStandaloneTick(
                dt, PlayerRef.FromEncoded(2), new Vector3(0f, 50f, 0f), _config, isOwnerValid: false);
            Assert.IsTrue(disconnectedDespawn);
        }

        [Test]
        public void SimulateStandaloneTick_IgnoresSelfAndGhostBlocksOnLayer6WallAndHitsLivingOpponentOnce()
        {
            PlayerRef shooter = PlayerRef.FromEncoded(1);
            PlayerRef enemyOwner = PlayerRef.FromEncoded(2);

            GameObject shooterCapsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            GameObject ghostCapsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            GameObject enemyCapsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            GameObject coverWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                shooterCapsule.layer = ArenaLayers.PlayerCharacter;
                shooterCapsule.transform.position = new Vector3(0f, 20f, 0.2f);
                MockDamageReceiver shooterReceiver = shooterCapsule.AddComponent<MockDamageReceiver>();
                shooterReceiver.OwnerPlayer = shooter;
                DamageReceiverRegistry.Register(shooterCapsule, shooterReceiver);

                ghostCapsule.layer = ArenaLayers.GhostSpectator;
                ghostCapsule.transform.position = new Vector3(0f, 20f, 1.5f);

                enemyCapsule.layer = ArenaLayers.PlayerCharacter;
                enemyCapsule.transform.position = new Vector3(0f, 20f, 3.0f);
                MockDamageReceiver victim = enemyCapsule.AddComponent<MockDamageReceiver>();
                victim.OwnerPlayer = enemyOwner;
                DamageReceiverRegistry.Register(enemyCapsule, victim);

                coverWall.layer = ArenaLayers.EnvironmentWall;
                coverWall.transform.position = new Vector3(10f, 20f, 2.0f);
                coverWall.transform.localScale = new Vector3(3f, 3f, 0.5f);

                Physics.SyncTransforms();

                _projectileGo.transform.position = new Vector3(0f, 20f, 0f);
                _projectile.OnPoolAcquire();
                _projectile.InitializeLaunch(new Vector3(0f, 20f, 0f), Vector3.forward, _config);

                bool despawned = false;
                for (int tick = 0; tick < 30 && !despawned; tick++)
                {
                    despawned = _projectile.SimulateStandaloneTick(1f / 60f, shooter, new Vector3(0f, 20f, 0f), _config);
                }

                Assert.IsTrue(despawned);
                Assert.AreEqual(0, shooterReceiver.HitCount);
                Assert.AreEqual(1, victim.HitCount);
                Assert.AreEqual(15, victim.LastDamage);
                Assert.AreEqual(AttackType.RangedProjectile, victim.LastAttackType);

                enemyCapsule.transform.position = new Vector3(10f, 20f, 4.0f);
                Physics.SyncTransforms();

                _projectile.OnPoolRelease();
                _projectileGo.transform.position = new Vector3(10f, 20f, 0f);
                _projectile.OnPoolAcquire();
                _projectile.InitializeLaunch(new Vector3(10f, 20f, 0f), Vector3.forward, _config);

                bool blockedByWall = false;
                for (int tick = 0; tick < 20 && !blockedByWall; tick++)
                {
                    blockedByWall = _projectile.SimulateStandaloneTick(1f / 60f, shooter, new Vector3(10f, 20f, 0f), _config);
                }

                Assert.IsTrue(blockedByWall);
                Assert.AreEqual(1, victim.HitCount);
            }
            finally
            {
                Object.DestroyImmediate(shooterCapsule);
                Object.DestroyImmediate(ghostCapsule);
                Object.DestroyImmediate(enemyCapsule);
                Object.DestroyImmediate(coverWall);
            }
        }

        [Test]
        public void PlayerCombatController_RangedFire_Enforces24TickCooldown3DPitchAimDodgeLockAndFlushWallGuard()
        {
            GameObject shooterGo = new GameObject("RangedShooter_Story7_3");
            GameObject thinWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                shooterGo.transform.SetPositionAndRotation(new Vector3(0f, 60f, 0f), Quaternion.identity);
                PlayerMobilityController mobility = shooterGo.AddComponent<PlayerMobilityController>();
                PlayerCombatController combat = shooterGo.AddComponent<PlayerCombatController>();
                combat.ConfigureForTest(_config, PlayerRef.FromEncoded(1), mobility: mobility);

                // 1. 3D Pitch Aim Ray: LookPitch = -25 deg (aiming upward toward elevated Central Altar) produces positive Y direction!
                NetworkInputData fireUpward = default;
                fireUpward.LookYaw = 0f;
                fireUpward.LookPitch = -25.0f;
                fireUpward.Buttons.Set(NetworkInputButtons.RangedFire, true);
                combat.SimulateCombatTick(in fireUpward, _config);
                Assert.AreEqual(1, combat.StandaloneSpawnedProjectileCount);
                Assert.Greater(combat.LastSpawnedAimDirection.y, 0.35f, "Upward LookPitch must tilt ranged bolt aim vector in 3D.");

                // 2. 24-tick (0.4s) cooldown gates ticks 1..23, fires on tick 24:
                for (int t = 1; t < 24; t++)
                {
                    combat.SimulateCombatTick(in fireUpward, _config);
                }

                Assert.AreEqual(1, combat.StandaloneSpawnedProjectileCount);
                combat.SimulateCombatTick(in fireUpward, _config);
                Assert.AreEqual(2, combat.StandaloneSpawnedProjectileCount);

                // 3. Expire ranged cooldown, start Q Dodge Roll on mobility, and verify RMB is suppressed while IsPerformingDodgeRoll!
                NetworkInputData idle = default;
                for (int t = 0; t < 25; t++)
                {
                    combat.SimulateCombatTick(in idle, _config);
                }

                NetworkInputData startDodge = default;
                startDodge.Buttons.Set(NetworkInputButtons.DodgeLeft, true);
                mobility.SimulateAuthoritativeTick(in startDodge, 1f / 60f, _config, groundedOverride: true);
                Assert.AreEqual(MobilityState.DodgingLeft, mobility.CurrentMobilityState);

                combat.SimulateCombatTick(in fireUpward, _config);
                Assert.AreEqual(2, combat.StandaloneSpawnedProjectileCount, "Active Dodge Roll must suppress Ranged Fire.");

                // Finish Dodge Roll:
                for (int t = 0; t < 20; t++)
                {
                    mobility.SimulateAuthoritativeTick(in idle, 1f / 60f, _config, groundedOverride: true);
                }

                Assert.AreNotEqual(MobilityState.DodgingLeft, mobility.CurrentMobilityState);

                // 4. Flush-against-wall muzzle guard (re-anchor shooter at (0, 60, 0) after Dodge Roll displacement):
                shooterGo.transform.SetPositionAndRotation(new Vector3(0f, 60f, 0f), Quaternion.identity);
                thinWall.layer = ArenaLayers.EnvironmentWall;
                thinWall.transform.position = new Vector3(0f, 61.1f, 0.35f);
                thinWall.transform.localScale = new Vector3(2.0f, 2.5f, 0.08f);
                Physics.SyncTransforms();

                NetworkInputData fireLevel = default;
                fireLevel.Buttons.Set(NetworkInputButtons.RangedFire, true);

                bool wallClipEventRaised = false;
                void OnShot(Vector3 start, Vector3 end, bool blockedByWall)
                {
                    if (blockedByWall) wallClipEventRaised = true;
                }

                PlayerCombatController.OnAnyRangedShotExecuted += OnShot;
                try
                {
                    combat.SimulateCombatTick(in fireLevel, _config);
                }
                finally
                {
                    PlayerCombatController.OnAnyRangedShotExecuted -= OnShot;
                }

                Assert.AreEqual(2, combat.StandaloneSpawnedProjectileCount);
                Assert.IsTrue(wallClipEventRaised);
            }
            finally
            {
                Object.DestroyImmediate(thinWall);
                Object.DestroyImmediate(shooterGo);
            }
        }
    }
}
