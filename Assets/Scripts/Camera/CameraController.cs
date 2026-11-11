using System;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace CapsuleArena.CameraSystem
{
    /// <summary>
    /// Supported living-player camera perspectives toggled via <c>Key V</c> (Story 8-1).
    /// </summary>
    public enum CameraPerspectiveMode : byte
    {
        /// <summary>Third-person over-shoulder boom camera (Distance = 4.5m, Height = 1.8m, Shoulder = +0.45m, FOV = 65).</summary>
        TPS = 0,

        /// <summary>First-person eye camera (EyeHeight = 1.5m, FOV = 80, local renderers = ShadowsOnly).</summary>
        FPS = 1
    }

    /// <summary>
    /// Controls the single scene <c>MainCamera</c> bound to the local player (<c>Object.HasInputAuthority</c>),
    /// providing instant <c>Key V</c> <see cref="CameraPerspectiveMode.TPS"/> / <see cref="CameraPerspectiveMode.FPS"/>
    /// switching, <c>Physics.SphereCast</c> (<c>r = 0.25m</c>) wall collision anti-clip on <c>Layer 6</c>,
    /// tight-corner <c>ShadowCastingMode.ShadowsOnly</c> protection, and bounded impact trauma shake
    /// (Stories 8-1, 8-2; ADR-0005; TR-camera-001, TR-camera-002).
    /// </summary>
    public sealed class CameraController : NetworkBehaviour
    {
        /// <summary>Canonical TPS vertical pivot height in meters above capsule root (1.8m).</summary>
        public const float CanonicalTpsHeight = 1.8f;

        /// <summary>Canonical TPS uncompressed boom arm length in meters (4.5m).</summary>
        public const float CanonicalTpsDistance = 4.5f;

        /// <summary>Canonical TPS right-shoulder horizontal offset in meters (+0.45m).</summary>
        public const float CanonicalTpsShoulderOffset = 0.45f;

        /// <summary>Canonical FPS eye-level height in meters above capsule root (1.5m).</summary>
        public const float CanonicalFpsEyeHeight = 1.5f;

        /// <summary>Canonical vertical Field of View in TPS perspective (65 deg).</summary>
        public const float CanonicalTpsFov = 65.0f;

        /// <summary>Canonical vertical Field of View in FPS perspective (80 deg).</summary>
        public const float CanonicalFpsFov = 80.0f;

        /// <summary>SphereCast probe radius in meters for TPS wall anti-clip (0.25m).</summary>
        public const float BoomSphereCastRadius = 0.25f;

        /// <summary>Minimum clamped TPS boom length in meters against tight corners (0.6m).</summary>
        public const float MinBoomDistance = 0.6f;

        /// <summary>Wall skin safety buffer subtracted from SphereCast hit distance (0.15m).</summary>
        public const float BoomWallBuffer = 0.15f;

        /// <summary>Boom threshold below which local capsule meshes hide via ShadowsOnly to prevent back-clipping (0.85m).</summary>
        public const float TightCornerShadowsOnlyThreshold = 0.85f;

        /// <summary>Minimum TPS vertical pitch in degrees (-60 deg).</summary>
        public const float TpsMinPitchDeg = -60.0f;

        /// <summary>Maximum TPS vertical pitch in degrees (+75 deg).</summary>
        public const float TpsMaxPitchDeg = 75.0f;

        /// <summary>Minimum FPS vertical pitch in degrees (-80 deg).</summary>
        public const float FpsMinPitchDeg = -80.0f;

        /// <summary>Maximum FPS vertical pitch in degrees (+80 deg).</summary>
        public const float FpsMaxPitchDeg = 80.0f;

        /// <summary>Debounce interval in seconds for Key V perspective toggles (0.15s).</summary>
        public const float ToggleDebounceSeconds = 0.15f;

        private const float PullInLerpSpeed = 25.0f;
        private const float PushOutLerpSpeed = 10.0f;
        private const float MaxTraumaAngleDeg = 1.6f;
        private const float TraumaDecayPerSec = 4.5f;

        [Header("References")]
        [Tooltip("Shared combat configuration asset.")]
        [SerializeField] private CharacterCombatConfigSO _combatConfig;

        [Tooltip("Local body/visor renderers switched to ShadowsOnly in FPS or tight TPS corners.")]
        [SerializeField] private Renderer[] _localBodyRenderers;

        [Tooltip("Sibling PlayerHealth component used to pause living camera control when eliminated.")]
        [SerializeField] private PlayerHealth _playerHealth;

        [Tooltip("Sibling PlayerCombatController providing active 90-deg Horizontal Sweep yaw overrides.")]
        [SerializeField] private PlayerCombatController _combatController;

        private Camera _targetCamera;
        private LocalInputPoller _inputPoller;
        private IInputFocusService _focusService;
        private CameraPerspectiveMode _currentMode = CameraPerspectiveMode.TPS;
        private float _currentBoomDistance = CanonicalTpsDistance;
        private float _lastToggleUnscaledTime = -10f;
        private float _trauma;
        private bool _isBoundAsLocalPlayer;
        private bool _hasAppliedShadowState;
        private bool _isLocalMeshShadowsOnly;

        /// <summary>Raised whenever the local camera switches between TPS and FPS.</summary>
        public event Action<CameraPerspectiveMode> OnPerspectiveChanged;

        /// <summary>Global observer event for HUD badges &amp; local billboard visibility when perspective changes.</summary>
        public static event Action<CameraPerspectiveMode> OnAnyLocalPerspectiveChanged;

        /// <summary>Gets the active camera perspective mode (TPS default).</summary>
        public CameraPerspectiveMode CurrentPerspective => _currentMode;

        /// <summary>Gets the smoothed TPS boom arm distance in meters.</summary>
        public float CurrentBoomDistance => _currentBoomDistance;

        /// <summary>Gets a value indicating whether local body meshes are currently in ShadowsOnly mode.</summary>
        public bool IsLocalMeshHiddenViaShadowsOnly => _isLocalMeshShadowsOnly;

        /// <summary>Gets the current vertical pitch angle applied to the bound camera in degrees.</summary>
        public float LastAppliedPitchDeg { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCameraEvents()
        {
            OnAnyLocalPerspectiveChanged = null;
        }

        private void Awake()
        {
            if (_playerHealth == null) TryGetComponent(out _playerHealth);
            if (_combatController == null) TryGetComponent(out _combatController);
            CacheBodyRenderersIfEmpty();
        }

        private void OnDestroy()
        {
            UnbindInputPoller();
        }

        /// <summary>
        /// Binds this controller as the local player's camera rig for standalone tests or live Fusion spawns.
        /// </summary>
        public void BindLocalCamera(
            Camera sceneCamera,
            LocalInputPoller inputPoller = null,
            IInputFocusService focusService = null,
            CharacterCombatConfigSO configOverride = null)
        {
            UnbindInputPoller();
            if (configOverride != null) _combatConfig = configOverride;

            _targetCamera = sceneCamera != null ? sceneCamera : Camera.main;
            _inputPoller = inputPoller;
            _focusService = focusService;
            _isBoundAsLocalPlayer = true;
            _hasAppliedShadowState = false;
            _currentBoomDistance = CanonicalTpsDistance;

            if (_inputPoller != null)
            {
                _inputPoller.OnToggleCameraViewRequested += HandleToggleCameraShortcut;
            }

            SetPerspective(CameraPerspectiveMode.TPS, forceRefresh: true);
        }

        /// <inheritdoc />
        public override void Spawned()
        {
            if (!Object.HasInputAuthority)
            {
                _isBoundAsLocalPlayer = false;
            }
        }

        private void LateUpdate()
        {
            StepCameraFrame(Time.deltaTime);
        }

        /// <summary>Advances one local camera frame deterministically (used by LateUpdate and EditMode tests).</summary>
        public void StepCameraFrame(float deltaTime = 0f)
        {
            if (!_isBoundAsLocalPlayer || _targetCamera == null)
            {
                return;
            }

            if (_playerHealth != null && _playerHealth.IsDead)
            {
                return;
            }

            float yaw = ResolveEffectiveYaw();
            float rawPitch = _inputPoller != null ? _inputPoller.CurrentPitch : 0f;
            UpdateCameraTransform(yaw, rawPitch, deltaTime);
        }

        /// <summary>
        /// Toggles between <see cref="CameraPerspectiveMode.TPS"/> and <see cref="CameraPerspectiveMode.FPS"/>
        /// when gameplay input focus is allowed and the 0.15s debounce window has elapsed.
        /// </summary>
        public bool TryTogglePerspective(float currentUnscaledTime)
        {
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                return false;
            }

            if (currentUnscaledTime - _lastToggleUnscaledTime < ToggleDebounceSeconds)
            {
                return false;
            }

            _lastToggleUnscaledTime = currentUnscaledTime;
            CameraPerspectiveMode next = _currentMode == CameraPerspectiveMode.TPS
                ? CameraPerspectiveMode.FPS
                : CameraPerspectiveMode.TPS;
            SetPerspective(next, forceRefresh: false);
            return true;
        }

        /// <summary>Immediately switches camera perspective while preserving exact world yaw/pitch.</summary>
        public void SetPerspective(CameraPerspectiveMode mode, bool forceRefresh = false)
        {
            if (!forceRefresh && _currentMode == mode)
            {
                return;
            }

            _currentMode = mode;
            ApplyCameraFovForMode(_currentMode);

            bool shouldHideBody = _currentMode == CameraPerspectiveMode.FPS
                                  || _currentBoomDistance < TightCornerShadowsOnlyThreshold;
            ApplyLocalBodyShadowMode(shouldHideBody, forceApply: forceRefresh);

            OnPerspectiveChanged?.Invoke(_currentMode);
            OnAnyLocalPerspectiveChanged?.Invoke(_currentMode);
        }

        /// <summary>Adds bounded screen trauma in [0, 1] for melee/lethal impact feedback (Story 14-3).</summary>
        public void AddTrauma(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
        }

        /// <summary>
        /// Evaluates camera placement for a single frame given <paramref name="yawDeg"/> and <paramref name="rawPitchDeg"/>.
        /// </summary>
        public void UpdateCameraTransform(float yawDeg, float rawPitchDeg, float deltaTime)
        {
            if (_targetCamera == null)
            {
                return;
            }

            DecayTrauma(deltaTime);
            Quaternion shakeRot = ComputeTraumaShakeOffset();

            if (_currentMode == CameraPerspectiveMode.FPS)
            {
                ApplyFpsTransform(yawDeg, rawPitchDeg, shakeRot);
                return;
            }

            ApplyTpsTransform(yawDeg, rawPitchDeg, deltaTime, shakeRot);
        }

        /// <summary>
        /// Casts <c>Physics.SphereCast</c> (<c>r = 0.25m</c>) exclusively against <see cref="ArenaLayers.LineOfSightMask"/>
        /// (<c>Layer 6</c>) and returns a clamped boom distance in <c>[0.6m, maxDist]</c> (Story 8-2 AC-1, AC-2).
        /// </summary>
        public static float ComputeSafeBoomDistance(Vector3 pivotPos, Vector3 boomDir, float maxDist = CanonicalTpsDistance)
        {
            Vector3 safeDir = boomDir.sqrMagnitude > 0.0001f ? boomDir.normalized : Vector3.back;
            bool didHit = Physics.SphereCast(
                pivotPos,
                BoomSphereCastRadius,
                safeDir,
                out RaycastHit hit,
                maxDist,
                ArenaLayers.LineOfSightMask,
                QueryTriggerInteraction.Ignore);

            return CalculateClampedBoomDistance(didHit, didHit ? hit.distance : maxDist, MinBoomDistance, maxDist, BoomWallBuffer);
        }

        /// <summary>
        /// Pure clamping math for TPS boom distance given SphereCast hit output (Story 8-2).
        /// </summary>
        public static float CalculateClampedBoomDistance(
            bool didHit,
            float hitDistance,
            float minDistance = MinBoomDistance,
            float maxDistance = CanonicalTpsDistance,
            float wallBuffer = BoomWallBuffer)
        {
            if (!didHit)
            {
                return maxDistance;
            }

            return Mathf.Clamp(hitDistance - wallBuffer, minDistance, maxDistance);
        }

        private void ApplyFpsTransform(float yawDeg, float rawPitchDeg, Quaternion shakeRot)
        {
            float fpsPitch = Mathf.Clamp(rawPitchDeg, FpsMinPitchDeg, FpsMaxPitchDeg);
            LastAppliedPitchDeg = fpsPitch;
            Vector3 eyePos = transform.position + Vector3.up * CanonicalFpsEyeHeight;
            _targetCamera.transform.SetPositionAndRotation(eyePos, Quaternion.Euler(fpsPitch, yawDeg, 0f) * shakeRot);
            ApplyLocalBodyShadowMode(shadowsOnly: true, forceApply: false);
        }

        private void ApplyTpsTransform(float yawDeg, float rawPitchDeg, float deltaTime, Quaternion shakeRot)
        {
            float tpsPitch = Mathf.Clamp(rawPitchDeg, TpsMinPitchDeg, TpsMaxPitchDeg);
            LastAppliedPitchDeg = tpsPitch;
            Quaternion aimRotation = Quaternion.Euler(tpsPitch, yawDeg, 0f);
            Vector3 pivotPos = ResolveTpsShoulderPivot(yawDeg);

            Vector3 boomDir = -(aimRotation * Vector3.forward);
            float maxDist = _combatConfig != null ? _combatConfig.TpsCameraDistance : CanonicalTpsDistance;
            float targetBoom = ComputeSafeBoomDistance(pivotPos, boomDir, maxDist);

            float lerpSpeed = targetBoom < _currentBoomDistance ? PullInLerpSpeed : PushOutLerpSpeed;
            _currentBoomDistance = deltaTime > 0f
                ? Mathf.Lerp(_currentBoomDistance, targetBoom, Mathf.Clamp01(lerpSpeed * deltaTime))
                : targetBoom;

            Vector3 camPos = pivotPos + boomDir * _currentBoomDistance;
            _targetCamera.transform.SetPositionAndRotation(camPos, aimRotation * shakeRot);
            ApplyLocalBodyShadowMode(shadowsOnly: _currentBoomDistance < TightCornerShadowsOnlyThreshold, forceApply: false);
        }

        private Vector3 ResolveTpsShoulderPivot(float yawDeg)
        {
            Vector3 centerPivot = transform.position + Vector3.up * CanonicalTpsHeight;
            Vector3 shoulderRight = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.right * CanonicalTpsShoulderOffset;
            Vector3 pivotPos = centerPivot + shoulderRight;

            if (Physics.Linecast(centerPivot, pivotPos, out RaycastHit shoulderHit, ArenaLayers.LineOfSightMask, QueryTriggerInteraction.Ignore))
            {
                return Vector3.Lerp(centerPivot, shoulderHit.point, 0.7f);
            }

            return pivotPos;
        }

        private void ApplyCameraFovForMode(CameraPerspectiveMode mode)
        {
            if (_targetCamera == null)
            {
                return;
            }

            _targetCamera.fieldOfView = mode == CameraPerspectiveMode.FPS ? CanonicalFpsFov : CanonicalTpsFov;
        }

        private float ResolveEffectiveYaw()
        {
            if (_combatController != null && _combatController.HasActiveSweepYawOverride)
            {
                return _combatController.EffectiveSweepYaw;
            }

            return _inputPoller != null ? _inputPoller.CurrentYaw : transform.eulerAngles.y;
        }

        private void HandleToggleCameraShortcut()
        {
            TryTogglePerspective(Time.unscaledTime);
        }

        private void ApplyLocalBodyShadowMode(bool shadowsOnly, bool forceApply)
        {
            if (!forceApply && _hasAppliedShadowState && _isLocalMeshShadowsOnly == shadowsOnly)
            {
                return;
            }

            _hasAppliedShadowState = true;
            _isLocalMeshShadowsOnly = shadowsOnly;
            CacheBodyRenderersIfEmpty();
            if (_localBodyRenderers == null)
            {
                return;
            }

            ShadowCastingMode targetMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            for (int i = 0; i < _localBodyRenderers.Length; i++)
            {
                Renderer r = _localBodyRenderers[i];
                if (r != null && !(r is TrailRenderer) && !(r is ParticleSystemRenderer))
                {
                    r.shadowCastingMode = targetMode;
                }
            }
        }

        private void CacheBodyRenderersIfEmpty()
        {
            if (_localBodyRenderers == null || _localBodyRenderers.Length == 0)
            {
                _localBodyRenderers = GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            }
        }

        private void DecayTrauma(float deltaTime)
        {
            if (_trauma > 0f && deltaTime > 0f)
            {
                _trauma = Mathf.Max(0f, _trauma - TraumaDecayPerSec * deltaTime);
            }
        }

        private Quaternion ComputeTraumaShakeOffset()
        {
            if (_trauma <= 0.001f)
            {
                return Quaternion.identity;
            }

            float intensity = _trauma * _trauma * MaxTraumaAngleDeg;
            float t = Time.unscaledTime * 37.0f;
            return Quaternion.Euler(Mathf.Sin(t) * intensity, Mathf.Cos(t * 1.3f) * intensity, 0f);
        }

        private void UnbindInputPoller()
        {
            if (_inputPoller != null)
            {
                _inputPoller.OnToggleCameraViewRequested -= HandleToggleCameraShortcut;
            }
        }
    }
}
