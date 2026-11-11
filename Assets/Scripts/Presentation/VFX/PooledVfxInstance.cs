using CapsuleArena.Combat;
using UnityEngine;

namespace CapsuleArena.Presentation.VFX
{
    /// <summary>
    /// Category of pooled URP combat visual effect managed by <see cref="VfxManager"/> (Story 14-2; TR-audio-002).
    /// </summary>
    public enum VfxPoolCategory : byte
    {
        /// <summary>50-deg / 2.5m Vertical Slash (#00F5D4) &amp; 90-deg / 3.0m Horizontal Sweep (#FF9F1C) sector arcs (8 pooled).</summary>
        SlashArc = 0,

        /// <summary>Melee/projectile player hit bursts (14 sparks) &amp; Layer 6 wall impact sparks (8 sparks) (8 pooled).</summary>
        ImpactSpark = 1,

        /// <summary>Forward Dash wind streaks &amp; lethal elimination ring bursts (4 pooled).</summary>
        MobilityAndDeathBurst = 2
    }

    /// <summary>
    /// Reusable pooled URP visual effect wrapper combining a zero-clone <see cref="MaterialPropertyBlock"/>
    /// procedural sector arc mesh (with progressive angular <c>_SweepProgress</c> reveal synchronized to
    /// <see cref="PlayerCombatController"/> turn progress) and a local-space <see cref="ParticleSystem"/>
    /// with deterministic <c>StopEmittingAndClear</c> reset before replay (Story 14-2; ADR-0008; TR-audio-002).
    /// </summary>
    public sealed class PooledVfxInstance : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SweepProgressId = Shader.PropertyToID("_SweepProgress");

        [SerializeField] private VfxPoolCategory _category;
        [SerializeField] private MeshFilter _arcMeshFilter;
        [SerializeField] private MeshRenderer _arcMeshRenderer;
        [SerializeField] private ParticleSystem _particleSystem;

        private MaterialPropertyBlock _mpb;
        private Color _activeBaseColor = Color.white;
        private float _elapsedSec;
        private float _remainingLifetimeSec;
        private float _totalLifetimeSec = 0.25f;
        private float _sweepRevealDurationSec;
        private float _currentSweepProgress = 1f;
        private float _configuredArcAngleDeg;
        private float _configuredArcRadiusMeters;
        private int _lastEmittedSparkCount;
        private Transform _followOriginTransform;
        private PlayerCombatController _syncCombatController;
        private bool _isActiveInScene;

        /// <summary>Gets the pool bucket to which this instance returns on expiry.</summary>
        public VfxPoolCategory Category => _category;

        /// <summary>Gets a value indicating whether this instance is currently playing in world space.</summary>
        public bool IsActiveInScene => _isActiveInScene;

        /// <summary>Gets the angular width in degrees of the most recently configured slash sector mesh.</summary>
        public float ConfiguredArcAngleDeg => _configuredArcAngleDeg;

        /// <summary>Gets the outer radius in meters of the most recently configured slash sector mesh.</summary>
        public float ConfiguredArcRadiusMeters => _configuredArcRadiusMeters;

        /// <summary>Gets the active tint color assigned to this VFX instance.</summary>
        public Color ActiveTintColor => _activeBaseColor;

        /// <summary>Gets the normalized [0, 1] angular fan reveal progress (reaches 1.0 when the capsule completes its +-90 deg turn).</summary>
        public float CurrentSweepProgress => _currentSweepProgress;

        /// <summary>Gets the particle count emitted on the most recent spark/burst trigger.</summary>
        public int LastEmittedSparkCount => _lastEmittedSparkCount;

        /// <summary>Initializes child renderers and particle system once during pool pre-warm.</summary>
        public void InitializePoolItem(
            VfxPoolCategory category,
            MeshFilter arcFilter,
            MeshRenderer arcRenderer,
            ParticleSystem particleSystem)
        {
            _category = category;
            _arcMeshFilter = arcFilter;
            _arcMeshRenderer = arcRenderer;
            _particleSystem = particleSystem;
            _mpb = new MaterialPropertyBlock();
            DeactivateToPool();
        }

        /// <summary>Activates this instance as a glowing planar melee slash arc with optional progressive angular sweep.</summary>
        public void PlaySlashArc(
            Vector3 worldPos,
            Quaternion worldRot,
            Mesh sectorMesh,
            float arcAngleDeg,
            float arcRadiusMeters,
            Color tintColor,
            float durationSec = 0.22f,
            float sweepRevealDurationSec = 0f,
            Transform followOriginTransform = null,
            PlayerCombatController syncCombatController = null)
        {
            PrepareTransformAndTimers(worldPos, worldRot, tintColor, durationSec);
            _configuredArcAngleDeg = arcAngleDeg;
            _configuredArcRadiusMeters = arcRadiusMeters;
            _sweepRevealDurationSec = Mathf.Max(0f, sweepRevealDurationSec);
            _followOriginTransform = followOriginTransform;
            _syncCombatController = syncCombatController;
            _currentSweepProgress = _sweepRevealDurationSec > 0.001f ? 0f : 1f;
            _lastEmittedSparkCount = 6;

            if (_arcMeshFilter != null && _arcMeshRenderer != null)
            {
                _arcMeshFilter.sharedMesh = sectorMesh;
                _arcMeshRenderer.enabled = true;
                ApplyArcColorAlpha(1.0f, _currentSweepProgress);
            }

            ResetAndEmitParticles(tintColor, 6);
        }

        /// <summary>Activates this instance as an impact spark or death/dash burst.</summary>
        public void PlayParticleBurst(
            Vector3 worldPos,
            Quaternion worldRot,
            Color tintColor,
            int emitCount,
            float durationSec = 0.30f)
        {
            PrepareTransformAndTimers(worldPos, worldRot, tintColor, durationSec);
            _configuredArcAngleDeg = 0f;
            _configuredArcRadiusMeters = 0f;
            _sweepRevealDurationSec = 0f;
            _followOriginTransform = null;
            _syncCombatController = null;
            _currentSweepProgress = 1f;
            _lastEmittedSparkCount = emitCount;

            if (_arcMeshRenderer != null)
            {
                _arcMeshRenderer.enabled = false;
            }

            ResetAndEmitParticles(tintColor, emitCount);
        }

        /// <summary>Advances progressive fan sweep + lifetime fade and returns true when expired and ready to return to pool.</summary>
        public bool TickLifetime(float deltaTime)
        {
            if (!_isActiveInScene)
            {
                return false;
            }

            FollowAttackerChestIfBound();

            _elapsedSec += deltaTime;
            _remainingLifetimeSec -= deltaTime;
            if (_remainingLifetimeSec <= 0f)
            {
                _currentSweepProgress = 1f;
                return true;
            }

            if (_arcMeshRenderer != null && _arcMeshRenderer.enabled)
            {
                UpdateSweepProgressAndAlpha();
            }

            return false;
        }

        /// <summary>Updates chest-anchor tracking and exact sweep progress in LateUpdate so the fan never lags behind capsule rotation or locomotion.</summary>
        public void SyncFollowPosition()
        {
            if (!_isActiveInScene)
            {
                return;
            }

            FollowAttackerChestIfBound();
            if (_arcMeshRenderer != null && _arcMeshRenderer.enabled && _sweepRevealDurationSec > 0.001f)
            {
                UpdateSweepProgressAndAlpha();
            }
        }

        /// <summary>Clears particles, hides renderers, and deactivates the GameObject for pool storage.</summary>
        public void DeactivateToPool()
        {
            _isActiveInScene = false;
            _elapsedSec = 0f;
            _remainingLifetimeSec = 0f;
            _sweepRevealDurationSec = 0f;
            _followOriginTransform = null;
            _syncCombatController = null;
            _currentSweepProgress = 1f;
            if (_particleSystem != null)
            {
                _particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_arcMeshRenderer != null)
            {
                _arcMeshRenderer.enabled = false;
            }

            gameObject.SetActive(false);
        }

        private void UpdateSweepProgressAndAlpha()
        {
            if (_sweepRevealDurationSec > 0.001f)
            {
                float timeProgress = Mathf.Clamp01(_elapsedSec / _sweepRevealDurationSec);
                if (_syncCombatController != null && _syncCombatController.IsVisualSweepActive)
                {
                    _currentSweepProgress = Mathf.Max(timeProgress, _syncCombatController.VisualSweepProgress);
                }
                else
                {
                    _currentSweepProgress = timeProgress;
                }

                float tailDuration = Mathf.Max(0.01f, _totalLifetimeSec - _sweepRevealDurationSec);
                float alpha = _elapsedSec <= _sweepRevealDurationSec
                    ? 1.0f
                    : Mathf.Clamp01(1.0f - ((_elapsedSec - _sweepRevealDurationSec) / tailDuration));

                ApplyArcColorAlpha(alpha, _currentSweepProgress);
                return;
            }

            _currentSweepProgress = 1.0f;
            float linearAlpha = Mathf.Clamp01(_remainingLifetimeSec / Mathf.Max(0.01f, _totalLifetimeSec));
            ApplyArcColorAlpha(linearAlpha, 1.0f);
        }

        private void FollowAttackerChestIfBound()
        {
            if (_followOriginTransform != null)
            {
                transform.position = _followOriginTransform.position + Vector3.up * MeleeSectorMath.ChestHeightOffsetMeters;
            }
        }

        private void PrepareTransformAndTimers(Vector3 pos, Quaternion rot, Color color, float durationSec)
        {
            transform.SetPositionAndRotation(pos, rot);
            _activeBaseColor = color;
            _elapsedSec = 0f;
            _totalLifetimeSec = Mathf.Max(0.05f, durationSec);
            _remainingLifetimeSec = _totalLifetimeSec;
            _isActiveInScene = true;
            gameObject.SetActive(true);
        }

        private void ResetAndEmitParticles(Color tint, int count)
        {
            if (_particleSystem == null)
            {
                return;
            }

            _particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _particleSystem.main;
            main.startColor = tint;
            _particleSystem.Play(true);
            if (count > 0)
            {
                _particleSystem.Emit(count);
            }
        }

        private void ApplyArcColorAlpha(float alpha, float sweepProgress)
        {
            if (_arcMeshRenderer == null)
            {
                return;
            }

            _mpb ??= new MaterialPropertyBlock();
            Color c = new Color(_activeBaseColor.r, _activeBaseColor.g, _activeBaseColor.b, alpha * 0.88f);
            Color hdr = new Color(_activeBaseColor.r * 2.2f * alpha, _activeBaseColor.g * 2.2f * alpha, _activeBaseColor.b * 2.2f * alpha, 1f);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(LegacyColorId, c);
            _mpb.SetColor(EmissionColorId, hdr);
            _mpb.SetFloat(SweepProgressId, Mathf.Clamp01(sweepProgress));
            _arcMeshRenderer.SetPropertyBlock(_mpb);
        }
    }
}
