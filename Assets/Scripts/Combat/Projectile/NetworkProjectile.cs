using System;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Networking.Pooling;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Combat.Projectile
{
    /// <summary>
    /// Pooled networked energy bolt traveling at 15.0 m/s for 120 ticks (30.0m max range)
    /// with full <see cref="IPooledResettable"/> state scrubbing and zero-alloc <see cref="DamageReceiverRegistry"/>
    /// impact resolution across reuse cycles (Story 4-3; ADR-0003; TR-session-003).
    /// </summary>
    public sealed class NetworkProjectile : NetworkBehaviour, IPooledResettable
    {
        private static readonly RaycastHit[] SharedHitBuffer = new RaycastHit[8];

        [Header("References")]
        [Tooltip("Shared combat configuration asset providing speed, radius, damage, and lifetime ticks.")]
        [SerializeField] private CharacterCombatConfigSO _combatConfig;

        [Tooltip("Optional TrailRenderer cleared on pool release/spawn to prevent origin-to-muzzle streaks.")]
        [SerializeField] private TrailRenderer _trailRenderer;

        [Tooltip("Optional Rigidbody whose residual linear/angular velocity is zeroed on pool release.")]
        [SerializeField] private Rigidbody _rigidbody;

        /// <summary>Gets the authoritative simulation tick timer governing 30.0m range auto-despawn.</summary>
        [Networked]
        public TickTimer LifetimeTimer { get; private set; }

        /// <summary>Gets the world-space muzzle position recorded when this projectile launched.</summary>
        [Networked]
        public Vector3 LaunchOrigin { get; private set; }

        private Vector3 _localDirection = Vector3.forward;
        private int _remainingSimulatedTicks = 120;
        private bool _hasDealtDamage;
        private bool _hasInitializedLaunch;
        private bool _componentsCached;

        /// <summary>Raised whenever any projectile impacts a Layer 6 wall or Layer 8 player: (impactPos, hitLivingPlayer).</summary>
        public static event Action<Vector3, bool> OnAnyProjectileImpact;

        /// <summary>Gets a value indicating whether this bolt has already dealt damage during its current life.</summary>
        public bool HasDealtDamage => _hasDealtDamage;

        /// <summary>Gets the remaining tick count in standalone/unit-test simulation mode.</summary>
        public int RemainingSimulatedTicks => _remainingSimulatedTicks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticImpactEvents()
        {
            OnAnyProjectileImpact = null;
        }

        /// <summary>Emits the global projectile impact presentation event (used by flush-wall checks and collisions).</summary>
        public static void RaiseWallOrPlayerImpact(Vector3 impactPoint, bool hitLivingPlayer)
        {
            OnAnyProjectileImpact?.Invoke(impactPoint, hitLivingPlayer);
        }

        private void Awake()
        {
            CacheComponentsOnce();
        }

        private void Start()
        {
            ValidateConfigOrDisable();
        }

        /// <summary>
        /// Validates that <see cref="CharacterCombatConfigSO"/> is assigned, logging an error and disabling if null (Story 1-1 AC-3).
        /// </summary>
        public bool ValidateConfigOrDisable()
        {
            return CharacterCombatConfigSO.EnsureValidConfig(_combatConfig, this);
        }

        /// <inheritdoc />
        public void OnPoolAcquire()
        {
            CacheComponentsOnce();
            _hasDealtDamage = false;
            _hasInitializedLaunch = false;
            _remainingSimulatedTicks = _combatConfig != null ? _combatConfig.ProjectileLifetimeTicks : 120;
            ZeroRigidbodyVelocities();

            // Keep emitting disabled during InstantiatePrefab at Vector3.zero until Spawned/InitializeLaunch
            // places the projectile at the actual muzzle position (prevents origin-to-muzzle trail streaks).
            if (_trailRenderer != null)
            {
                _trailRenderer.emitting = false;
                _trailRenderer.Clear();
            }
        }

        /// <inheritdoc />
        public void OnPoolRelease()
        {
            CacheComponentsOnce();
            _hasDealtDamage = false;
            _hasInitializedLaunch = false;
            _remainingSimulatedTicks = 0;
            ZeroRigidbodyVelocities();

            if (_trailRenderer != null)
            {
                _trailRenderer.emitting = false;
                _trailRenderer.Clear();
            }
        }

        /// <summary>
        /// Initializes launch direction, muzzle origin, lifetime ticks, and enables clean trail emission
        /// at the current muzzle position.
        /// </summary>
        public void InitializeLaunch(Vector3 muzzleOrigin, Vector3 direction, ICharacterCombatConfig configOverride = null)
        {
            ICharacterCombatConfig cfg = configOverride ?? _combatConfig;
            int lifetimeTicks = cfg != null ? cfg.ProjectileLifetimeTicks : 120;

            _localDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            _remainingSimulatedTicks = lifetimeTicks;
            _hasDealtDamage = false;
            _hasInitializedLaunch = true;

            if (Object != null && Runner != null && Object.HasStateAuthority)
            {
                LaunchOrigin = muzzleOrigin;
                LifetimeTimer = TickTimer.CreateFromTicks(Runner, lifetimeTicks);
            }

            ClearAndEnableTrailAtMuzzle();
        }

        /// <inheritdoc />
        public override void Spawned()
        {
            _hasDealtDamage = false;
            if (!_hasInitializedLaunch)
            {
                _localDirection = transform.forward;
                if (Object.HasStateAuthority)
                {
                    int ticks = _combatConfig != null ? _combatConfig.ProjectileLifetimeTicks : 120;
                    LaunchOrigin = transform.position;
                    LifetimeTimer = TickTimer.CreateFromTicks(Runner, ticks);
                }
            }

            ClearAndEnableTrailAtMuzzle();
        }

        /// <inheritdoc />
        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.HasStateAuthority)
            {
                return;
            }

            if (!Runner.IsPlayerValid(Object.InputAuthority) || LifetimeTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
                return;
            }

            float speed = _combatConfig != null ? _combatConfig.RangedProjectileSpeed : 15.0f;
            float radius = _combatConfig != null ? _combatConfig.RangedProjectileRadius : 0.20f;
            int damage = _combatConfig != null ? _combatConfig.RangedDamage : 15;

            bool shouldDespawn = AdvanceAndCheckCollision(
                Runner.DeltaTime,
                speed,
                radius,
                damage,
                Object.InputAuthority,
                LaunchOrigin,
                Runner.Tick.Raw);

            if (shouldDespawn)
            {
                Runner.Despawn(Object);
            }
        }

        /// <summary>
        /// Advances the projectile by one deterministic tick step and evaluates Layer 6 / Layer 8 impacts.
        /// Callable directly in EditMode tests without requiring an active <see cref="NetworkRunner"/>.
        /// </summary>
        public bool SimulateStandaloneTick(
            float deltaTime,
            PlayerRef ownerPlayer,
            Vector3 launchOrigin,
            ICharacterCombatConfig configOverride = null,
            bool isOwnerValid = true)
        {
            if (!isOwnerValid)
            {
                _remainingSimulatedTicks = 0;
                return true;
            }

            if (_trailRenderer != null && !_trailRenderer.emitting)
            {
                ClearAndEnableTrailAtMuzzle();
            }

            ICharacterCombatConfig cfg = configOverride ?? _combatConfig;
            float speed = cfg != null ? cfg.RangedProjectileSpeed : 15.0f;
            float radius = cfg != null ? cfg.RangedProjectileRadius : 0.20f;
            int damage = cfg != null ? cfg.RangedDamage : 15;

            bool hitDespawn = AdvanceAndCheckCollision(deltaTime, speed, radius, damage, ownerPlayer, launchOrigin, 0);
            _remainingSimulatedTicks--;
            return hitDespawn || _remainingSimulatedTicks <= 0;
        }

        private bool AdvanceAndCheckCollision(
            float deltaTime,
            float speed,
            float radius,
            int damage,
            PlayerRef ownerPlayer,
            Vector3 launchOrigin,
            int attackTick)
        {
            Vector3 startPos = transform.position;
            Vector3 dir = _localDirection.sqrMagnitude > 0.0001f ? _localDirection : transform.forward;
            float stepDistance = speed * deltaTime;

            int hitCount = Physics.SphereCastNonAlloc(
                startPos,
                radius,
                dir,
                SharedHitBuffer,
                stepDistance,
                ArenaLayers.ProjectileCollisionMask,
                QueryTriggerInteraction.Ignore);

            if (TryFindClosestValidImpact(hitCount, ownerPlayer, out RaycastHit winningHit, out IDamageReceiver victim))
            {
                Vector3 impactPos = winningHit.point.sqrMagnitude > 0f ? winningHit.point : winningHit.collider.bounds.center;
                transform.position = impactPos;
                RaiseWallOrPlayerImpact(impactPos, hitLivingPlayer: victim != null && !victim.IsDead);
                ApplyProjectileImpact(victim, ownerPlayer, damage, launchOrigin, attackTick, impactPos);
                return true;
            }

            transform.position = startPos + dir * stepDistance;
            return false;
        }

        private static bool TryFindClosestValidImpact(
            int hitCount,
            PlayerRef ownerPlayer,
            out RaycastHit winningHit,
            out IDamageReceiver winningReceiver)
        {
            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            winningReceiver = null;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = SharedHitBuffer[i].collider;
                if (col == null)
                {
                    continue;
                }

                IDamageReceiver receiver = null;
                if (col.gameObject.layer == ArenaLayers.PlayerCharacter &&
                    DamageReceiverRegistry.TryGetByCollider(col, out receiver) &&
                    IsIgnoredCombatant(receiver, ownerPlayer))
                {
                    continue;
                }

                if (SharedHitBuffer[i].distance < bestDistance)
                {
                    bestDistance = SharedHitBuffer[i].distance;
                    bestIndex = i;
                    winningReceiver = receiver;
                }
            }

            winningHit = bestIndex >= 0 ? SharedHitBuffer[bestIndex] : default;
            return bestIndex >= 0;
        }

        private static bool IsIgnoredCombatant(IDamageReceiver receiver, PlayerRef ownerPlayer)
        {
            return receiver.IsDead || (ownerPlayer != PlayerRef.None && receiver.OwnerPlayer == ownerPlayer);
        }

        private void ApplyProjectileImpact(
            IDamageReceiver victim,
            PlayerRef ownerPlayer,
            int damage,
            Vector3 launchOrigin,
            int attackTick,
            Vector3 impactPoint)
        {
            if (_hasDealtDamage || victim == null || victim.IsDead)
            {
                return;
            }

            _hasDealtDamage = true;
            victim.RequestDamageFromAttacker(
                ownerPlayer,
                damage,
                AttackType.RangedProjectile,
                launchOrigin,
                attackTick,
                impactPoint);
        }

        private void ClearAndEnableTrailAtMuzzle()
        {
            if (_trailRenderer != null)
            {
                _trailRenderer.Clear();
                _trailRenderer.emitting = true;
            }
        }

        private void CacheComponentsOnce()
        {
            if (_componentsCached)
            {
                return;
            }

            _componentsCached = true;
            if (_trailRenderer == null)
            {
                _trailRenderer = GetComponentInChildren<TrailRenderer>(includeInactive: true);
            }

            if (_rigidbody == null)
            {
                TryGetComponent(out _rigidbody);
            }
        }

        private void ZeroRigidbodyVelocities()
        {
            if (_rigidbody != null && !_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
        }
    }
}
