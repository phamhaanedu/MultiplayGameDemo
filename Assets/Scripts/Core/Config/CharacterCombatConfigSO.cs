using System;
using UnityEngine;

namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Immutable runtime combat, mobility, and feel configuration asset (ADR-0001).
    /// Encapsulates all Inspector fields as private serialized members and precomputes
    /// 60Hz tick counts, squared ranges, and half-angle cosines on load and validation.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DefaultCapsuleCombatConfig",
        menuName = "CapsuleArena/Config/Character Combat Config")]
    public sealed class CharacterCombatConfigSO : ScriptableObject, ICharacterCombatConfig
    {
        /// <summary>Canonical Photon Fusion simulation tick rate in Hz.</summary>
        public const int DefaultTickRate = 60;

        private const float MinPositiveStat = 0.05f;
        private const float MinRangeMeters = 0.1f;
        private const float MinConeAngleDeg = 5.0f;
        private const float MaxConeAngleDeg = 180.0f;

        [Header("1. Health & Core")]
        [Tooltip("Maximum hit points granted on spawn and respawn (canonical: 100).")]
        [SerializeField] private int _maxHealth = 100;

        [Header("2. Movement & Mobility")]
        [Tooltip("Horizontal walk speed in meters per second (canonical: 6.0).")]
        [SerializeField] private float _moveSpeed = 6.0f;

        [Tooltip("Optional sprint multiplier relative to MoveSpeed (canonical: 1.35).")]
        [SerializeField] private float _sprintMultiplier = 1.35f;

        [Tooltip("Target vertical jump apex height in meters (canonical: 1.5).")]
        [SerializeField] private float _jumpHeight = 1.5f;

        [Tooltip("Downward gravitational acceleration in m/s^2 (canonical: -19.62).")]
        [SerializeField] private float _gravity = -19.62f;

        [Tooltip("Base mouse/touch look sensitivity multiplier (canonical: 1.0).")]
        [SerializeField] private float _rotationSensitivity = 1.0f;

        [Tooltip("Forward Dash (Key 2) burst speed in m/s (canonical: 18.0).")]
        [SerializeField] private float _dashSpeed = 18.0f;

        [Tooltip("Forward Dash active burst duration in seconds (canonical: 0.20 -> 12 ticks).")]
        [SerializeField] private float _dashDurationSec = 0.20f;

        [Tooltip("Forward Dash cooldown duration in seconds (canonical: 3.0).")]
        [SerializeField] private float _dashCooldownSec = 3.0f;

        [Tooltip("Lateral Dodge Roll (Keys Q/E) burst speed in m/s (canonical: 12.0).")]
        [SerializeField] private float _dodgeSpeed = 12.0f;

        [Tooltip("Lateral Dodge Roll active duration in seconds (canonical: 0.25 -> 15 ticks).")]
        [SerializeField] private float _dodgeDurationSec = 0.25f;

        [Tooltip("Shared Lateral Dodge Roll cooldown in seconds (canonical: 1.5).")]
        [SerializeField] private float _dodgeCooldownSec = 1.5f;

        [Header("3. Melee Skills (LMB, Keys 1/3)")]
        [Tooltip("Vertical Slash (LMB) damage per hit (canonical: 25).")]
        [SerializeField] private int _verticalSlashDamage = 25;

        [Tooltip("Vertical Slash reach radius in meters (canonical: 2.5).")]
        [SerializeField] private float _verticalSlashRange = 2.5f;

        [Tooltip("Vertical Slash full cone angle in degrees (canonical: 50.0).")]
        [SerializeField] private float _verticalSlashAngleDeg = 50.0f;

        [Tooltip("Vertical Slash cooldown in seconds (canonical: 0.6).")]
        [SerializeField] private float _verticalSlashCooldownSec = 0.6f;

        [Tooltip("Horizontal Sweep (Keys 1/3) damage per hit (canonical: 30).")]
        [SerializeField] private int _horizontalSweepDamage = 30;

        [Tooltip("Horizontal Sweep reach radius in meters (canonical: 3.0).")]
        [SerializeField] private float _horizontalSweepRange = 3.0f;

        [Tooltip("Horizontal Sweep full cone angle in degrees (canonical: 90.0).")]
        [SerializeField] private float _horizontalSweepAngleDeg = 90.0f;

        [Tooltip("Character yaw rotation degrees during Horizontal Sweep (canonical: 90.0).")]
        [SerializeField] private float _horizontalSweepTurnDeg = 90.0f;

        [Tooltip("Duration of the 90-degree yaw sweep in seconds (canonical: 0.18 -> 11 ticks).")]
        [SerializeField] private float _horizontalSweepTurnDurationSec = 0.18f;

        [Tooltip("Shared Horizontal Sweep cooldown in seconds (canonical: 1.2).")]
        [SerializeField] private float _horizontalSweepCooldownSec = 1.2f;

        [Header("4. Ranged Projectile (RMB)")]
        [Tooltip("Ranged Fire (RMB) damage per bolt hit (canonical: 15).")]
        [SerializeField] private int _rangedDamage = 15;

        [Tooltip("Ranged bolt linear travel speed in m/s (canonical: 15.0).")]
        [SerializeField] private float _rangedProjectileSpeed = 15.0f;

        [Tooltip("Maximum ranged bolt travel distance before despawn in meters (canonical: 30.0).")]
        [SerializeField] private float _rangedMaxDistance = 30.0f;

        [Tooltip("Ranged bolt collision radius in meters (canonical: 0.20).")]
        [SerializeField] private float _rangedProjectileRadius = 0.20f;

        [Tooltip("Ranged Fire cooldown in seconds (canonical: 0.4).")]
        [SerializeField] private float _rangedFireCooldownSec = 0.4f;

        [Header("5. Camera & Feel Presentation")]
        [Tooltip("Third-person camera follow distance in meters (canonical: 4.5).")]
        [SerializeField] private float _tpsCameraDistance = 4.5f;

        [Tooltip("Third-person camera vertical pivot offset in meters (canonical: 1.6).")]
        [SerializeField] private float _tpsCameraHeight = 1.6f;

        [Tooltip("Duration of white/red hit flash on damage in seconds (canonical: 0.08).")]
        [SerializeField] private float _hitFlashDurationSec = 0.08f;

        [Tooltip("Duration of DOTween squash-and-stretch impulse in seconds (canonical: 0.18).")]
        [SerializeField] private float _squashStretchDurationSec = 0.18f;

        [Tooltip("VisualRoot stretch scale vector during burst motion.")]
        [SerializeField] private Vector3 _visualStretchScale = new Vector3(0.78f, 1.28f, 0.78f);

        [Tooltip("VisualRoot squash scale vector on landing or impact.")]
        [SerializeField] private Vector3 _visualSquashScale = new Vector3(1.30f, 0.70f, 1.15f);

        [Tooltip("Visual lean/tilt angle in degrees during lateral mobility (canonical: 35.0).")]
        [SerializeField] private float _dodgeVisualTiltAngle = 35.0f;

        [NonSerialized] private int _dashTicks;
        [NonSerialized] private int _dashCooldownTicks;
        [NonSerialized] private int _dodgeTicks;
        [NonSerialized] private int _dodgeCooldownTicks;
        [NonSerialized] private int _horizontalTurnTicks;
        [NonSerialized] private float _projectileLifetimeSec;
        [NonSerialized] private int _projectileLifetimeTicks;
        [NonSerialized] private float _verticalSlashCosHalfAngle;
        [NonSerialized] private float _horizontalSweepCosHalfAngle;
        [NonSerialized] private float _verticalSlashRangeSqr;
        [NonSerialized] private float _horizontalSweepRangeSqr;
        [NonSerialized] private float _jumpInitialVelocity;

        /// <inheritdoc />
        public int MaxHealth => _maxHealth;

        /// <inheritdoc />
        public int MaxHP => _maxHealth;

        /// <inheritdoc />
        public float MoveSpeed => _moveSpeed;

        /// <inheritdoc />
        public float SprintMultiplier => _sprintMultiplier;

        /// <inheritdoc />
        public float JumpHeight => _jumpHeight;

        /// <inheritdoc />
        public float Gravity => _gravity;

        /// <inheritdoc />
        public float RotationSensitivity => _rotationSensitivity;

        /// <inheritdoc />
        public float DashSpeed => _dashSpeed;

        /// <inheritdoc />
        public float DashDurationSec => _dashDurationSec;

        /// <inheritdoc />
        public float DashCooldownSec => _dashCooldownSec;

        /// <inheritdoc />
        public float DodgeSpeed => _dodgeSpeed;

        /// <inheritdoc />
        public float DodgeDurationSec => _dodgeDurationSec;

        /// <inheritdoc />
        public float DodgeCooldownSec => _dodgeCooldownSec;

        /// <inheritdoc />
        public int VerticalSlashDamage => _verticalSlashDamage;

        /// <inheritdoc />
        public float VerticalSlashRange => _verticalSlashRange;

        /// <inheritdoc />
        public float VerticalSlashAngleDeg => _verticalSlashAngleDeg;

        /// <inheritdoc />
        public float VerticalSlashCooldownSec => _verticalSlashCooldownSec;

        /// <inheritdoc />
        public int HorizontalSweepDamage => _horizontalSweepDamage;

        /// <inheritdoc />
        public float HorizontalSweepRange => _horizontalSweepRange;

        /// <inheritdoc />
        public float HorizontalSweepAngleDeg => _horizontalSweepAngleDeg;

        /// <inheritdoc />
        public float HorizontalSweepTurnDeg => _horizontalSweepTurnDeg;

        /// <inheritdoc />
        public float HorizontalSweepTurnDurationSec => _horizontalSweepTurnDurationSec;

        /// <inheritdoc />
        public float HorizontalSweepCooldownSec => _horizontalSweepCooldownSec;

        /// <inheritdoc />
        public int RangedDamage => _rangedDamage;

        /// <inheritdoc />
        public float RangedProjectileSpeed => _rangedProjectileSpeed;

        /// <inheritdoc />
        public float RangedMaxDistance => _rangedMaxDistance;

        /// <inheritdoc />
        public float RangedProjectileRadius => _rangedProjectileRadius;

        /// <inheritdoc />
        public float RangedFireCooldownSec => _rangedFireCooldownSec;

        /// <inheritdoc />
        public float TpsCameraDistance => _tpsCameraDistance;

        /// <inheritdoc />
        public float TpsCameraHeight => _tpsCameraHeight;

        /// <inheritdoc />
        public float HitFlashDurationSec => _hitFlashDurationSec;

        /// <inheritdoc />
        public float SquashStretchDurationSec => _squashStretchDurationSec;

        /// <inheritdoc />
        public Vector3 VisualStretchScale => _visualStretchScale;

        /// <inheritdoc />
        public Vector3 VisualSquashScale => _visualSquashScale;

        /// <inheritdoc />
        public float DodgeVisualTiltAngle => _dodgeVisualTiltAngle;

        /// <inheritdoc />
        public int DashTicks => _dashTicks;

        /// <inheritdoc />
        public int DashCooldownTicks => _dashCooldownTicks;

        /// <inheritdoc />
        public int DodgeTicks => _dodgeTicks;

        /// <inheritdoc />
        public int DodgeCooldownTicks => _dodgeCooldownTicks;

        /// <inheritdoc />
        public int HorizontalTurnTicks => _horizontalTurnTicks;

        /// <inheritdoc />
        public float ProjectileLifetimeSec => _projectileLifetimeSec;

        /// <inheritdoc />
        public int ProjectileLifetimeTicks => _projectileLifetimeTicks;

        /// <inheritdoc />
        public float VerticalSlashCosHalfAngle => _verticalSlashCosHalfAngle;

        /// <inheritdoc />
        public float HorizontalSweepCosHalfAngle => _horizontalSweepCosHalfAngle;

        /// <inheritdoc />
        public float VerticalSlashRangeSqr => _verticalSlashRangeSqr;

        /// <inheritdoc />
        public float HorizontalSweepRangeSqr => _horizontalSweepRangeSqr;

        /// <inheritdoc />
        public float JumpInitialVelocity => _jumpInitialVelocity;

        private void OnEnable()
        {
            ValidateAndClamp();
        }

        private void OnValidate()
        {
            ValidateAndClamp();
        }

        /// <summary>
        /// Verifies that a runtime consumer holds a non-null <see cref="CharacterCombatConfigSO"/> reference.
        /// Logs a descriptive error and disables the component cleanly when unassigned (Story 1-1 AC-3).
        /// </summary>
        public static bool EnsureValidConfig(CharacterCombatConfigSO config, Behaviour consumer)
        {
            if (config != null)
            {
                return true;
            }

            if (consumer != null)
            {
                Debug.LogError(
                    $"[{consumer.GetType().Name}] Missing CharacterCombatConfigSO reference on '{consumer.name}'; disabling component.",
                    consumer);
                consumer.enabled = false;
            }

            return false;
        }

        /// <summary>
        /// Clamps all serialized fields to safe physical bounds (guarding against NaN/Infinity,
        /// zero/negative speeds, velocity inversions, or inverted angles) and refreshes precomputed caches.
        /// </summary>
        public void ValidateAndClamp()
        {
            ClampHealthAndMobility();
            ClampMeleeSettings();
            ClampRangedAndPresentation();
            RecalculateDerivedValues(DefaultTickRate);
        }

        /// <summary>
        /// Precomputes tick counts, squared melee radii, initial jump velocity, and half-angle
        /// cosines for zero-trig, zero-alloc reads inside FixedUpdateNetwork().
        /// </summary>
        /// <param name="tickRate">Simulation tick rate in Hz (defaults to 60).</param>
        public void RecalculateDerivedValues(int tickRate = DefaultTickRate)
        {
            int safeTickRate = Mathf.Max(1, tickRate);
            _dashTicks = Mathf.Max(1, Mathf.RoundToInt(_dashDurationSec * safeTickRate));
            _dashCooldownTicks = Mathf.Max(0, Mathf.RoundToInt(_dashCooldownSec * safeTickRate));
            _dodgeTicks = Mathf.Max(1, Mathf.RoundToInt(_dodgeDurationSec * safeTickRate));
            _dodgeCooldownTicks = Mathf.Max(0, Mathf.RoundToInt(_dodgeCooldownSec * safeTickRate));
            _horizontalTurnTicks = Mathf.Max(1, Mathf.RoundToInt(_horizontalSweepTurnDurationSec * safeTickRate));

            float safeProjSpeed = Mathf.Max(MinPositiveStat, SanitizeFloat(_rangedProjectileSpeed, 15.0f));
            float safeMaxDist = Mathf.Max(MinRangeMeters, SanitizeFloat(_rangedMaxDistance, 30.0f));
            _projectileLifetimeSec = safeMaxDist / safeProjSpeed;
            _projectileLifetimeTicks = Mathf.Max(1, Mathf.RoundToInt(_projectileLifetimeSec * safeTickRate));

            _verticalSlashCosHalfAngle = Mathf.Cos(_verticalSlashAngleDeg * 0.5f * Mathf.Deg2Rad);
            _horizontalSweepCosHalfAngle = Mathf.Cos(_horizontalSweepAngleDeg * 0.5f * Mathf.Deg2Rad);
            _verticalSlashRangeSqr = _verticalSlashRange * _verticalSlashRange;
            _horizontalSweepRangeSqr = _horizontalSweepRange * _horizontalSweepRange;
            _jumpInitialVelocity = Mathf.Sqrt(2.0f * Mathf.Abs(_gravity) * _jumpHeight);
        }

        /// <inheritdoc />
        public float GetCooldown(CombatSkillId skillId)
        {
            return skillId switch
            {
                CombatSkillId.ForwardDash => _dashCooldownSec,
                CombatSkillId.DodgeRoll => _dodgeCooldownSec,
                CombatSkillId.VerticalSlash => _verticalSlashCooldownSec,
                CombatSkillId.HorizontalSweep => _horizontalSweepCooldownSec,
                CombatSkillId.RangedFire => _rangedFireCooldownSec,
                _ => 0f
            };
        }

        private void ClampHealthAndMobility()
        {
            _maxHealth = Mathf.Max(1, _maxHealth);
            _moveSpeed = Mathf.Max(MinPositiveStat, SanitizeFloat(_moveSpeed, 6.0f));
            _sprintMultiplier = Mathf.Clamp(SanitizeFloat(_sprintMultiplier, 1.35f), 1.0f, 3.0f);
            _jumpHeight = Mathf.Max(MinPositiveStat, SanitizeFloat(_jumpHeight, 1.5f));
            float rawGravity = SanitizeFloat(_gravity, -19.62f);
            _gravity = rawGravity >= -0.01f ? -Mathf.Max(0.1f, Mathf.Abs(rawGravity)) : rawGravity;
            _rotationSensitivity = Mathf.Clamp(SanitizeFloat(_rotationSensitivity, 1.0f), 0.1f, 5.0f);

            _dashSpeed = Mathf.Max(_moveSpeed + 0.1f, SanitizeFloat(_dashSpeed, 18.0f));
            _dashDurationSec = Mathf.Max(1f / DefaultTickRate, SanitizeFloat(_dashDurationSec, 0.20f));
            _dashCooldownSec = Mathf.Max(MinPositiveStat, SanitizeFloat(_dashCooldownSec, 3.0f));

            _dodgeSpeed = Mathf.Max(_moveSpeed + 0.1f, SanitizeFloat(_dodgeSpeed, 12.0f));
            _dodgeDurationSec = Mathf.Max(1f / DefaultTickRate, SanitizeFloat(_dodgeDurationSec, 0.25f));
            _dodgeCooldownSec = Mathf.Max(MinPositiveStat, SanitizeFloat(_dodgeCooldownSec, 1.5f));
        }

        private void ClampMeleeSettings()
        {
            _verticalSlashDamage = Mathf.Max(1, _verticalSlashDamage);
            _verticalSlashRange = Mathf.Max(MinRangeMeters, SanitizeFloat(_verticalSlashRange, 2.5f));
            _verticalSlashAngleDeg = Mathf.Clamp(
                SanitizeFloat(_verticalSlashAngleDeg, 50.0f), MinConeAngleDeg, MaxConeAngleDeg);
            _verticalSlashCooldownSec = Mathf.Max(MinPositiveStat, SanitizeFloat(_verticalSlashCooldownSec, 0.6f));

            _horizontalSweepDamage = Mathf.Max(1, _horizontalSweepDamage);
            _horizontalSweepRange = Mathf.Max(MinRangeMeters, SanitizeFloat(_horizontalSweepRange, 3.0f));
            _horizontalSweepAngleDeg = Mathf.Clamp(
                SanitizeFloat(_horizontalSweepAngleDeg, 90.0f), MinConeAngleDeg, MaxConeAngleDeg);
            _horizontalSweepTurnDeg = Mathf.Clamp(
                SanitizeFloat(_horizontalSweepTurnDeg, 90.0f), 0f, 360.0f);
            _horizontalSweepTurnDurationSec = Mathf.Max(
                1f / DefaultTickRate, SanitizeFloat(_horizontalSweepTurnDurationSec, 0.18f));
            float rawSweepCd = SanitizeFloat(_horizontalSweepCooldownSec, 1.2f);
            _horizontalSweepCooldownSec = Mathf.Max(_horizontalSweepTurnDurationSec + MinPositiveStat, rawSweepCd);
        }

        private void ClampRangedAndPresentation()
        {
            _rangedDamage = Mathf.Max(1, _rangedDamage);
            _rangedProjectileSpeed = Mathf.Max(MinPositiveStat, SanitizeFloat(_rangedProjectileSpeed, 15.0f));
            _rangedMaxDistance = Mathf.Max(MinRangeMeters, SanitizeFloat(_rangedMaxDistance, 30.0f));
            _rangedProjectileRadius = Mathf.Clamp(SanitizeFloat(_rangedProjectileRadius, 0.20f), 0.02f, 2.0f);
            _rangedFireCooldownSec = Mathf.Max(MinPositiveStat, SanitizeFloat(_rangedFireCooldownSec, 0.4f));

            _tpsCameraDistance = Mathf.Clamp(SanitizeFloat(_tpsCameraDistance, 4.5f), 1.0f, 15.0f);
            _tpsCameraHeight = Mathf.Clamp(SanitizeFloat(_tpsCameraHeight, 1.6f), 0.5f, 5.0f);
            _hitFlashDurationSec = Mathf.Clamp(SanitizeFloat(_hitFlashDurationSec, 0.08f), 0.01f, 1.0f);
            _squashStretchDurationSec = Mathf.Clamp(SanitizeFloat(_squashStretchDurationSec, 0.18f), 0.01f, 1.0f);
            _dodgeVisualTiltAngle = Mathf.Clamp(SanitizeFloat(_dodgeVisualTiltAngle, 35.0f), 0f, 90.0f);
        }

        private static float SanitizeFloat(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
