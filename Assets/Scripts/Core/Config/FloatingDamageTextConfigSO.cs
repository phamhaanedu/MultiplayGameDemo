using UnityEngine;

namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Designer-editable <see cref="ScriptableObject"/> controlling world-space overhead floating
    /// damage (red) and heal (green) numbers, upward rise height, <c>2/3</c> enlarge + <c>1/3</c> shrink
    /// curve, fade-out, and pre-warmed pool capacity.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DefaultFloatingDamageConfig",
        menuName = "CapsuleArena/Config/Floating Damage Text Config")]
    public sealed class FloatingDamageTextConfigSO : ScriptableObject, IFloatingDamageTextConfig
    {
        /// <summary>Canonical default damage text color (#FF3B3B vibrant red).</summary>
        public static readonly Color DefaultDamageRed = new Color32(255, 59, 59, 255);

        /// <summary>Canonical default heal text color (#22C55E healthy green).</summary>
        public static readonly Color DefaultHealGreen = new Color32(34, 197, 94, 255);

        /// <summary>Canonical 2/3 normalized grow-phase split.</summary>
        public const float CanonicalGrowPhaseRatio = 2.0f / 3.0f;

        [Header("1. Colors (Damage vs Heal)")]
        [Tooltip("Text color displayed when a player loses HP (default: Red).")]
        [SerializeField] private Color _damageTextColor = new Color32(255, 59, 59, 255);

        [Tooltip("Text color displayed when a player recovers HP, e.g., health potions (default: Green).")]
        [SerializeField] private Color _healTextColor = new Color32(34, 197, 94, 255);

        [Header("2. Timing & Trajectory")]
        [Tooltip("Total upward flight duration in seconds before disappearing.")]
        [SerializeField] private float _lifetimeSeconds = 0.85f;

        [Tooltip("Fraction of flight spent enlarging before shrinking and fading out (canonical: 0.6667 = 2/3).")]
        [SerializeField] private float _growPhaseNormalizedRatio = CanonicalGrowPhaseRatio;

        [Tooltip("Vertical spawn elevation above capsule root in meters (2.65m sits cleanly above head & HP bar).")]
        [SerializeField] private float _spawnHeightOffsetMeters = 2.65f;

        [Tooltip("Total upward distance traveled in meters over LifetimeSeconds.")]
        [SerializeField] private float _riseDistanceMeters = 1.25f;

        [Tooltip("Small horizontal alternating spread in meters so rapid consecutive hits do not overlap.")]
        [SerializeField] private float _horizontalSpreadMeters = 0.16f;

        [Header("3. Scale (2/3 Enlarge -> 1/3 Shrink) & Opacity")]
        [Tooltip("Starting scale multiplier at spawn (t = 0).")]
        [SerializeField] private float _startScale = 0.85f;

        [Tooltip("Peak enlarged scale multiplier at t = 2/3.")]
        [SerializeField] private float _peakScale = 1.50f;

        [Tooltip("Final shrunken scale multiplier at t = 1.0.")]
        [SerializeField] private float _endScale = 0.55f;

        [Tooltip("Opacity at t = 2/3 (gently fades 1.0 -> AlphaAtPeak while rising, then fades to 0.0 in final 1/3).")]
        [SerializeField] private float _alphaAtPeak = 0.85f;

        [Header("4. Typography & Pooling")]
        [Tooltip("Base world-space TextMeshPro font size.")]
        [SerializeField] private float _fontSize = 34.0f;

        [Tooltip("Pre-warmed object pool size (zero runtime Instantiate/Destroy).")]
        [SerializeField] private int _poolSize = 16;

        /// <inheritdoc />
        public Color DamageTextColor => _damageTextColor;

        /// <inheritdoc />
        public Color HealTextColor => _healTextColor;

        /// <inheritdoc />
        public float LifetimeSeconds => _lifetimeSeconds;

        /// <inheritdoc />
        public float GrowPhaseNormalizedRatio => _growPhaseNormalizedRatio;

        /// <inheritdoc />
        public float SpawnHeightOffsetMeters => _spawnHeightOffsetMeters;

        /// <inheritdoc />
        public float RiseDistanceMeters => _riseDistanceMeters;

        /// <inheritdoc />
        public float HorizontalSpreadMeters => _horizontalSpreadMeters;

        /// <inheritdoc />
        public float StartScale => _startScale;

        /// <inheritdoc />
        public float PeakScale => _peakScale;

        /// <inheritdoc />
        public float EndScale => _endScale;

        /// <inheritdoc />
        public float AlphaAtPeak => _alphaAtPeak;

        /// <inheritdoc />
        public float FontSize => _fontSize;

        /// <inheritdoc />
        public int PoolSize => _poolSize;

        private void OnEnable()
        {
            ValidateAndClamp();
        }

        private void OnValidate()
        {
            ValidateAndClamp();
        }

        /// <summary>Clamps all serialized fields to safe physical and visual bounds.</summary>
        public void ValidateAndClamp()
        {
            _lifetimeSeconds = Mathf.Clamp(Sanitize(_lifetimeSeconds, 0.85f), 0.15f, 5.0f);
            _growPhaseNormalizedRatio = Mathf.Clamp(Sanitize(_growPhaseNormalizedRatio, CanonicalGrowPhaseRatio), 0.10f, 0.90f);
            _spawnHeightOffsetMeters = Mathf.Clamp(Sanitize(_spawnHeightOffsetMeters, 2.65f), 1.5f, 5.0f);
            _riseDistanceMeters = Mathf.Clamp(Sanitize(_riseDistanceMeters, 1.25f), 0.1f, 5.0f);
            _horizontalSpreadMeters = Mathf.Clamp(Sanitize(_horizontalSpreadMeters, 0.16f), 0f, 1.0f);

            _startScale = Mathf.Clamp(Sanitize(_startScale, 0.85f), 0.1f, 5.0f);
            _peakScale = Mathf.Max(_startScale + 0.05f, Mathf.Clamp(Sanitize(_peakScale, 1.50f), 0.2f, 6.0f));
            _endScale = Mathf.Min(_peakScale - 0.05f, Mathf.Clamp(Sanitize(_endScale, 0.55f), 0.05f, 4.0f));
            _alphaAtPeak = Mathf.Clamp(Sanitize(_alphaAtPeak, 0.85f), 0.2f, 1.0f);

            _fontSize = Mathf.Clamp(Sanitize(_fontSize, 34.0f), 12.0f, 96.0f);
            _poolSize = Mathf.Clamp(_poolSize, 4, 64);

            if (_damageTextColor.a <= 0.01f)
            {
                _damageTextColor.a = 1.0f;
            }

            if (_healTextColor.a <= 0.01f)
            {
                _healTextColor.a = 1.0f;
            }
        }

        private static float Sanitize(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
