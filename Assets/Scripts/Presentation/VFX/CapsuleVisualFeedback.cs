using CapsuleArena.Player;
using DG.Tweening;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Presentation.VFX
{
    /// <summary>
    /// Per-capsule URP Hit-Flash (<c>0.12s</c> HDR emissive white via cached <see cref="MaterialPropertyBlock"/>
    /// with <c>GetPropertyBlock</c> / <c>SetPropertyBlock</c> and <c>0</c> material clones) and <c>DG.Tweening</c>
    /// Squash &amp; Stretch (<c>0.15s</c> on child <c>VisualRoot</c> ONLY so root <see cref="CapsuleCollider"/> scale
    /// stays <c>(1, 1, 1)</c> forever), with Forward Dash stretch, overlap-safe <c>DOKill(true)</c> scale recovery,
    /// and Ghost Spectator death reset (Story 14-3; ADR-0008; TR-audio-003).
    /// </summary>
    public sealed class CapsuleVisualFeedback : MonoBehaviour
    {
        /// <summary>Duration of the white HDR emissive hit-flash in seconds (0.12s).</summary>
        public const float HitFlashDurationSec = 0.12f;

        /// <summary>Duration of the impact squash-and-recover pulse in seconds (0.15s).</summary>
        public const float SquashStretchDurationSec = 0.15f;

        /// <summary>Peak impact squash scale applied exclusively to child <c>VisualRoot</c>.</summary>
        public static readonly Vector3 HitSquashPeakScale = new Vector3(1.22f, 0.78f, 1.22f);

        /// <summary>Peak forward dash stretch scale applied exclusively to child <c>VisualRoot</c>.</summary>
        public static readonly Vector3 DashStretchPeakScale = new Vector3(0.82f, 0.90f, 1.30f);

        /// <summary>HDR white emission color applied during Hit-Flash (<c>3.5x</c> intensity).</summary>
        public static readonly Color HdrFlashEmission = new Color(3.5f, 3.5f, 3.5f, 1.0f);

        /// <summary>Crisp surface white base color applied during Hit-Flash.</summary>
        public static readonly Color FlashSurfaceWhite = new Color(1.0f, 1.0f, 1.0f, 1.0f);

        private static readonly int EmissionColorShaderId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");

        [Header("References")]
        [Tooltip("Child visual hierarchy scaled during Squash & Stretch (NEVER the root collider transform!).")]
        [SerializeField] private Transform _visualRoot;

        [Tooltip("Mesh renderers receiving MaterialPropertyBlock Hit-Flash without cloning materials.")]
        [SerializeField] private Renderer[] _bodyRenderers;

        [Tooltip("Sibling PlayerHealth emitting OnHitFeedbackReceived and OnPlayerDied.")]
        [SerializeField] private PlayerHealth _playerHealth;

        [Tooltip("Optional sibling PlayerMobilityController emitting OnMobilityBurstStarted for Forward Dash stretch.")]
        [SerializeField] private PlayerMobilityController _mobilityController;

        private MaterialPropertyBlock _mpb;
        private float _flashRemainingSec;
        private bool _isFlashActive;

        /// <summary>Gets the child <c>VisualRoot</c> transform animated by Squash &amp; Stretch.</summary>
        public Transform VisualRoot => _visualRoot;

        /// <summary>Gets a value indicating whether the 0.12s white Hit-Flash is currently active.</summary>
        public bool IsHitFlashActive => _isFlashActive && _flashRemainingSec > 0f;

        private void Awake()
        {
            EnsureCachedReferences();
        }

        private void OnEnable()
        {
            EnsureCachedReferences();
            SubscribeSiblingEvents();
        }

        private void OnDisable()
        {
            UnsubscribeSiblingEvents();
            ResetVisualState();
        }

        private void Update()
        {
            TickFeedback(Time.deltaTime);
        }

        /// <summary>Injects dependencies explicitly for prefab wiring and EditMode unit tests.</summary>
        public void ConfigureReferences(
            Transform visualRoot,
            Renderer[] bodyRenderers,
            PlayerHealth playerHealth = null,
            PlayerMobilityController mobilityController = null)
        {
            UnsubscribeSiblingEvents();
            _visualRoot = visualRoot;
            _bodyRenderers = bodyRenderers;
            _playerHealth = playerHealth;
            _mobilityController = mobilityController;
            EnsureCachedReferences();
            SubscribeSiblingEvents();
        }

        /// <summary>
        /// Triggers the <c>0.12s</c> <see cref="MaterialPropertyBlock"/> white Hit-Flash and <c>0.15s</c>
        /// <c>VisualRoot</c> Squash &amp; Stretch pulse with overlap-safe <c>DOKill(true)</c> recovery.
        /// </summary>
        public void TriggerHitFlashAndSquash()
        {
            EnsureCachedReferences();
            if (_playerHealth != null && _playerHealth.IsDead)
            {
                return;
            }

            ApplyFlashPropertyBlock();
            _flashRemainingSec = HitFlashDurationSec;
            _isFlashActive = true;
            PulseVisualRoot(HitSquashPeakScale, SquashStretchDurationSec);
        }

        /// <summary>Triggers a directional stretch pulse (<c>(0.82, 0.90, 1.30)</c>) on <c>VisualRoot</c> during Forward Dash.</summary>
        public void PlayDashStretch()
        {
            EnsureCachedReferences();
            if (_playerHealth != null && _playerHealth.IsDead)
            {
                return;
            }

            PulseVisualRoot(DashStretchPeakScale, 0.16f);
        }

        /// <summary>Advances the Hit-Flash timer deterministically and clears the property block upon expiry.</summary>
        public void TickFeedback(float deltaTime)
        {
            if (!_isFlashActive || deltaTime <= 0f)
            {
                return;
            }

            _flashRemainingSec -= deltaTime;
            if (_flashRemainingSec <= 0f)
            {
                ClearFlashPropertyBlock();
            }
        }

        /// <summary>Immediately cancels active tweens, clears emissive overrides, and restores <c>VisualRoot</c> scale to <c>(1, 1, 1)</c>.</summary>
        public void ResetVisualState()
        {
            ClearFlashPropertyBlock();
            if (_visualRoot != null && _visualRoot != transform)
            {
                _visualRoot.DOKill(true);
                _visualRoot.localScale = Vector3.one;
            }
        }

        private void SubscribeSiblingEvents()
        {
            if (_playerHealth != null)
            {
                _playerHealth.OnHitFeedbackReceived -= HandleHitFeedbackReceived;
                _playerHealth.OnHitFeedbackReceived += HandleHitFeedbackReceived;
                _playerHealth.OnPlayerDied -= HandlePlayerDied;
                _playerHealth.OnPlayerDied += HandlePlayerDied;
            }

            if (_mobilityController != null)
            {
                _mobilityController.OnMobilityBurstStarted -= HandleMobilityBurstStarted;
                _mobilityController.OnMobilityBurstStarted += HandleMobilityBurstStarted;
            }
        }

        private void UnsubscribeSiblingEvents()
        {
            if (_playerHealth != null)
            {
                _playerHealth.OnHitFeedbackReceived -= HandleHitFeedbackReceived;
                _playerHealth.OnPlayerDied -= HandlePlayerDied;
            }

            if (_mobilityController != null)
            {
                _mobilityController.OnMobilityBurstStarted -= HandleMobilityBurstStarted;
            }
        }

        private void PulseVisualRoot(Vector3 peakScale, float durationSec)
        {
            if (_visualRoot == null || _visualRoot == transform)
            {
                return;
            }

            _visualRoot.DOKill(true);
            _visualRoot.localScale = Vector3.one;
            _visualRoot.DOPulseScaleToOne(peakScale, durationSec).SetEase(Ease.OutQuad);
        }

        private void ApplyFlashPropertyBlock()
        {
            if (_bodyRenderers == null)
            {
                return;
            }

            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                Renderer r = _bodyRenderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorShaderId, FlashSurfaceWhite);
                _mpb.SetColor(EmissionColorShaderId, HdrFlashEmission);
                r.SetPropertyBlock(_mpb);
            }
        }

        private void ClearFlashPropertyBlock()
        {
            _isFlashActive = false;
            _flashRemainingSec = 0f;
            if (_bodyRenderers == null)
            {
                return;
            }

            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                Renderer r = _bodyRenderers[i];
                if (r != null)
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        private void EnsureCachedReferences()
        {
            _mpb ??= new MaterialPropertyBlock();
            if (_playerHealth == null)
            {
                TryGetComponent(out _playerHealth);
            }

            if (_mobilityController == null)
            {
                TryGetComponent(out _mobilityController);
            }

            if (_visualRoot == null)
            {
                Transform found = transform.Find("VisualRoot");
                if (found != null)
                {
                    _visualRoot = found;
                }
            }

            if ((_bodyRenderers == null || _bodyRenderers.Length == 0) && _visualRoot != null)
            {
                _bodyRenderers = _visualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            }
        }

        private void HandleHitFeedbackReceived(Vector3 hitPos, int damage, bool isLethal, PlayerRef attacker)
        {
            if (!isLethal)
            {
                TriggerHitFlashAndSquash();
            }
        }

        private void HandlePlayerDied(PlayerRef victim, PlayerRef killer)
        {
            ResetVisualState();
        }

        private void HandleMobilityBurstStarted(MobilityState state, Vector3 origin, Quaternion rotation)
        {
            if (state == MobilityState.Dashing)
            {
                PlayDashStretch();
            }
        }
    }
}
