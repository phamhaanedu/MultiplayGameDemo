using CapsuleArena.CameraSystem;
using CapsuleArena.Player;
using CapsuleArena.UI.Common;
using DG.Tweening;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// World-space overhead nameplate and dual-fill HP bar anchored at <c>Y = +2.35m</c> above each capsule.
    /// Rotates to face the cached camera in <c>LateUpdate()</c>, scales by <c>clamp(d / 8.0m, 0.75, 1.35)</c>,
    /// disables <see cref="GraphicRaycaster"/>, hides the local billboard in <c>FPS</c> mode, differentiates
    /// local (<c>#22C55E</c>) vs enemy (<c>#EF4444</c>) HP fills, and shows <c>"[Name] [GHOST]"</c> at 50% opacity
    /// on elimination (Story 10-1; ADR-0007; TR-hud-001).
    /// </summary>
    public sealed class OverheadBillboardUI : MonoBehaviour
    {
        /// <summary>Canonical local Y-offset above the capsule origin (2.35m).</summary>
        public const float CanonicalLocalOffsetY = 2.35f;

        /// <summary>Reference distance in meters where billboard scale equals 1.0x (8.0m).</summary>
        public const float ReferenceDistanceMeters = 8.0f;

        /// <summary>Minimum billboard distance scale multiplier (0.75x).</summary>
        public const float MinDistanceScale = 0.75f;

        /// <summary>Maximum billboard distance scale multiplier (1.35x).</summary>
        public const float MaxDistanceScale = 1.35f;

        /// <summary>Trailing yellow damage bar tween duration in seconds (0.35s).</summary>
        public const float DelayedHpTweenDurationSec = 0.35f;

        /// <summary>Ghost nameplate opacity after elimination (0.50).</summary>
        public const float GhostAlpha = 0.50f;

        /// <summary>Enemy overhead HP fill color when HP &gt; 30% (#EF4444).</summary>
        public static readonly Color EnemyHealthyRed = new Color32(239, 68, 68, 255);

        /// <summary>Enemy overhead HP fill color when HP &lt;= 30% (#B91C1C).</summary>
        public static readonly Color EnemyCriticalCrimson = new Color32(185, 28, 28, 255);

        private const float BaseWorldScale = 0.01f;

        [Header("Hierarchy & State Bindings")]
        [Tooltip("Owning PlayerHealth driving event-based HP and Ghost transitions.")]
        [SerializeField] private PlayerHealth _playerHealth;

        [Tooltip("World-space Canvas hosting the billboard.")]
        [SerializeField] private Canvas _worldCanvas;

        [Tooltip("CanvasGroup controlling overall billboard visibility and Ghost 50% fade.")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Tooltip("Container holding the HP background, delayed bar, and instant fill bar.")]
        [SerializeField] private GameObject _hpBarContainer;

        [Tooltip("Immediate foreground HP fill image (Local Green / Enemy Red).")]
        [SerializeField] private Image _instantHpFill;

        [Tooltip("Delayed amber trailing damage fill image (#FFB703, 0.35s OutQuad).")]
        [SerializeField] private Image _delayedHpFill;

        [Tooltip("Player display name label (appends ' [GHOST]' on elimination).")]
        [SerializeField] private TMP_Text _playerNameText;

        private Transform _cachedCameraTransform;
        private string _baseDisplayName = "Player";
        private bool _isLocalPlayerBillboard;
        private bool _isDeadGhost;
        private CameraPerspectiveMode _localPerspective = CameraPerspectiveMode.TPS;

        /// <summary>Gets the current normalized instant HP fill in [0, 1].</summary>
        public float InstantFillAmount => _instantHpFill != null ? _instantHpFill.fillAmount : 0f;

        /// <summary>Gets the current normalized delayed HP fill in [0, 1].</summary>
        public float DelayedFillAmount => _delayedHpFill != null ? _delayedHpFill.fillAmount : 0f;

        /// <summary>Gets the current instant foreground HP bar color.</summary>
        public Color CurrentInstantFillColor => _instantHpFill != null ? _instantHpFill.color : Color.clear;

        /// <summary>Gets the rendered nameplate text.</summary>
        public string RenderedNameText => _playerNameText != null ? _playerNameText.text : string.Empty;

        /// <summary>Gets the current CanvasGroup alpha.</summary>
        public float CurrentAlpha => _canvasGroup != null ? _canvasGroup.alpha : 0f;

        /// <summary>Gets the HP bar container GameObject for prefab/death-state wiring.</summary>
        public GameObject HpBarContainer => _hpBarContainer;

        /// <summary>Gets the TMP nameplate label for prefab/death-state wiring.</summary>
        public TMP_Text PlayerNameLabel => _playerNameText;

        private void Awake()
        {
            if (_playerHealth == null)
            {
                _playerHealth = GetComponentInParent<PlayerHealth>();
            }

            EnsureHierarchyBuilt();
            DisableRaycasterOverhead();
            CacheFallbackCameraOnce();
        }

        private void Start()
        {
            CacheFallbackCameraOnce();
        }

        private void OnEnable()
        {
            CameraController.OnAnyLocalPerspectiveChanged += HandleLocalPerspectiveChanged;
            BindHealthEvents();
        }

        private void OnDisable()
        {
            CameraController.OnAnyLocalPerspectiveChanged -= HandleLocalPerspectiveChanged;
            UnbindHealthEvents();
            if (_delayedHpFill != null)
            {
                _delayedHpFill.DOKill(complete: false);
            }
        }

        private void OnDestroy()
        {
            CameraController.OnAnyLocalPerspectiveChanged -= HandleLocalPerspectiveChanged;
            UnbindHealthEvents();
        }

        private void LateUpdate()
        {
            if (_cachedCameraTransform == null)
            {
                return;
            }

            UpdateBillboardTransform(_cachedCameraTransform.position, _cachedCameraTransform.rotation);
        }

        /// <summary>Configures dependencies explicitly for prefabs, spawned players, and unit tests.</summary>
        public void Initialize(
            PlayerHealth health,
            string displayName,
            bool isLocalPlayer,
            Transform cameraTransform = null)
        {
            CameraController.OnAnyLocalPerspectiveChanged -= HandleLocalPerspectiveChanged;
            CameraController.OnAnyLocalPerspectiveChanged += HandleLocalPerspectiveChanged;
            UnbindHealthEvents();
            _playerHealth = health;
            _isLocalPlayerBillboard = isLocalPlayer;
            if (cameraTransform != null)
            {
                _cachedCameraTransform = cameraTransform;
            }
            else
            {
                CacheFallbackCameraOnce();
            }

            SetDisplayName(displayName);
            EnsureHierarchyBuilt();
            DisableRaycasterOverhead();
            BindHealthEvents();
            SyncInitialHealthAndGhostState();
        }

        /// <summary>Updates the combatant's base display name and refreshes Ghost suffix if dead.</summary>
        public void SetDisplayName(string displayName)
        {
            _baseDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName.Trim();
            RefreshNameplateLabel();
        }

        /// <summary>Applies camera-facing rotation and clamped distance scaling for a given camera pose.</summary>
        public void UpdateBillboardTransform(Vector3 cameraPosition, Quaternion cameraRotation)
        {
            transform.localPosition = new Vector3(0f, CanonicalLocalOffsetY, 0f);
            Vector3 awayFromCamera = transform.position - cameraPosition;
            if (awayFromCamera.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(awayFromCamera.normalized, cameraRotation * Vector3.up);
            }

            float scaleFactor = CalculateBillboardScale(awayFromCamera.magnitude);
            transform.localScale = Vector3.one * (BaseWorldScale * scaleFactor);
        }

        /// <summary>
        /// Pure distance-scaling curve <c>clamp(distance / 8.0m, 0.75, 1.35)</c> (Story 10-1 AC-1).
        /// </summary>
        public static float CalculateBillboardScale(
            float distance,
            float refDist = ReferenceDistanceMeters,
            float minScale = MinDistanceScale,
            float maxScale = MaxDistanceScale)
        {
            float safeRef = Mathf.Max(0.01f, refDist);
            return Mathf.Clamp(distance / safeRef, minScale, maxScale);
        }

        /// <summary>Handles HP updates strictly on event dispatch.</summary>
        public void HandleHealthChanged(int currentHp, int maxHp)
        {
            EnsureHierarchyBuilt();
            float normalized = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;
            UpdateInstantFill(normalized);
            UpdateDelayedFill(normalized);

            if (currentHp > 0 && _isDeadGhost)
            {
                _isDeadGhost = false;
                if (_hpBarContainer != null)
                {
                    _hpBarContainer.SetActive(true);
                }

                RefreshNameplateLabel();
                RefreshCanvasVisibility();
            }
        }

        /// <summary>Transitions nameplate to Ghost mode (hides HP bar, appends '[GHOST]' at 50% opacity).</summary>
        public void ApplyGhostPresentation()
        {
            _isDeadGhost = true;
            if (_hpBarContainer != null)
            {
                _hpBarContainer.SetActive(false);
            }

            RefreshNameplateLabel();
            RefreshCanvasVisibility();
        }

        /// <summary>Notifies this billboard of the local camera perspective (hides local billboard in FPS).</summary>
        public void HandleLocalPerspectiveChanged(CameraPerspectiveMode mode)
        {
            _localPerspective = mode;
            RefreshCanvasVisibility();
        }

        private void SyncInitialHealthAndGhostState()
        {
            if (_playerHealth == null)
            {
                return;
            }

            HandleHealthChanged(_playerHealth.CurrentHP, _playerHealth.MaxHP);
            if (_playerHealth.IsDead)
            {
                ApplyGhostPresentation();
            }
        }

        private void UpdateInstantFill(float normalized)
        {
            if (_instantHpFill == null)
            {
                return;
            }

            _instantHpFill.fillAmount = normalized;
            bool critical = normalized <= 0.30f;
            if (_isLocalPlayerBillboard)
            {
                _instantHpFill.color = critical ? UiThemeAndFontProvider.CrimsonDanger : UiThemeAndFontProvider.HealthyGreen;
            }
            else
            {
                _instantHpFill.color = critical ? EnemyCriticalCrimson : EnemyHealthyRed;
            }
        }

        private void UpdateDelayedFill(float normalized)
        {
            if (_delayedHpFill == null)
            {
                return;
            }

            _delayedHpFill.DOKill(complete: false);
            if (normalized >= _delayedHpFill.fillAmount)
            {
                _delayedHpFill.fillAmount = normalized;
            }
            else
            {
                _delayedHpFill.DOFillAmount(normalized, DelayedHpTweenDurationSec).SetEase(Ease.OutQuad);
            }
        }

        private void RefreshNameplateLabel()
        {
            if (_playerNameText == null)
            {
                return;
            }

            UiThemeAndFontProvider.EnsureFontAssigned(_playerNameText);
            _playerNameText.text = _isDeadGhost ? $"{_baseDisplayName} [GHOST]" : _baseDisplayName;
        }

        private void RefreshCanvasVisibility()
        {
            if (_canvasGroup == null)
            {
                return;
            }

            if (_isLocalPlayerBillboard && _localPerspective == CameraPerspectiveMode.FPS)
            {
                _canvasGroup.alpha = 0f;
                return;
            }

            _canvasGroup.alpha = _isDeadGhost ? GhostAlpha : 1.0f;
        }

        private void BindHealthEvents()
        {
            if (_playerHealth == null)
            {
                return;
            }

            UnbindHealthEvents();
            _playerHealth.OnHealthChanged += HandleHealthChanged;
            _playerHealth.OnPlayerDied += HandlePlayerDied;
        }

        private void UnbindHealthEvents()
        {
            if (_playerHealth == null)
            {
                return;
            }

            _playerHealth.OnHealthChanged -= HandleHealthChanged;
            _playerHealth.OnPlayerDied -= HandlePlayerDied;
        }

        private void HandlePlayerDied(PlayerRef victim, PlayerRef killer)
        {
            ApplyGhostPresentation();
        }

        private void CacheFallbackCameraOnce()
        {
            if (_cachedCameraTransform == null && Camera.main != null)
            {
                _cachedCameraTransform = Camera.main.transform;
            }
        }

        private void DisableRaycasterOverhead()
        {
            if (TryGetComponent(out GraphicRaycaster raycaster))
            {
                raycaster.enabled = false;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void EnsureHierarchyBuilt()
        {
            EnsureWorldCanvasAndGroup();
            EnsurePlayerNameLabel();
            EnsureHpBars();
            RefreshNameplateLabel();
            RefreshCanvasVisibility();
        }

        private void EnsureWorldCanvasAndGroup()
        {
            transform.localPosition = new Vector3(0f, CanonicalLocalOffsetY, 0f);
            if (_worldCanvas == null && !TryGetComponent(out _worldCanvas))
            {
                _worldCanvas = gameObject.AddComponent<Canvas>();
                _worldCanvas.renderMode = RenderMode.WorldSpace;
            }

            if (_canvasGroup == null && !TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (TryGetComponent(out RectTransform rootRect) && rootRect.sizeDelta.x <= 105f && rootRect.sizeDelta.y <= 105f)
            {
                rootRect.sizeDelta = new Vector2(220f, 46f);
                transform.localScale = Vector3.one * BaseWorldScale;
            }
        }

        private void EnsurePlayerNameLabel()
        {
            if (_playerNameText != null)
            {
                return;
            }

            GameObject nameObj = new GameObject("Txt_PlayerName", typeof(RectTransform));
            nameObj.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = nameObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _playerNameText = nameObj.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(_playerNameText);
            _playerNameText.fontSize = 16f;
            _playerNameText.textWrappingMode = TextWrappingModes.NoWrap;
            _playerNameText.alignment = TextAlignmentOptions.Center;
            _playerNameText.color = Color.white;
            _playerNameText.raycastTarget = false;
        }

        private void EnsureHpBars()
        {
            if (_hpBarContainer != null)
            {
                return;
            }

            _hpBarContainer = new GameObject("HPBarContainer", typeof(RectTransform));
            _hpBarContainer.transform.SetParent(transform, worldPositionStays: false);
            RectTransform hpRt = _hpBarContainer.GetComponent<RectTransform>();
            hpRt.anchorMin = new Vector2(0.08f, 0.08f);
            hpRt.anchorMax = new Vector2(0.92f, 0.42f);
            hpRt.offsetMin = Vector2.zero;
            hpRt.offsetMax = Vector2.zero;

            Image bg = _hpBarContainer.AddComponent<Image>();
            bg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            bg.color = new Color32(15, 23, 42, 220);
            bg.raycastTarget = false;

            _delayedHpFill = CreateFilledBarChild(_hpBarContainer.transform, "Img_HPFill_Delayed", UiThemeAndFontProvider.SolarAmber);
            _instantHpFill = CreateFilledBarChild(_hpBarContainer.transform, "Img_HPFill_Instant", UiThemeAndFontProvider.HealthyGreen);
        }

        private static Image CreateFilledBarChild(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(2f, 2f);
            rt.offsetMax = new Vector2(-2f, -2f);

            Image img = go.AddComponent<Image>();
            img.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1.0f;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
