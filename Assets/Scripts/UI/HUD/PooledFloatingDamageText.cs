using System;
using System.Text;
using CapsuleArena.CameraSystem;
using CapsuleArena.Core.Config;
using CapsuleArena.UI.Common;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// Pooled world-space billboard floating number driven by zero-allocation <see cref="DOTween"/>:
    /// rises from above the target's head, enlarges over the first <c>2/3</c> of flight while gently fading,
    /// and shrinks while fading to <c>0</c> over the final <c>1/3</c> of flight before returning to pool.
    /// </summary>
    public sealed class PooledFloatingDamageText : MonoBehaviour
    {
        private const float BaseWorldScale = 0.01f;

        private readonly StringBuilder _sb = new StringBuilder(16);
        private Canvas _worldCanvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _rootRect;
        private TMP_Text _valueText;

        private Action<float> _cachedSampleAction;
        private Action _cachedCompleteAction;
        private Action<PooledFloatingDamageText> _returnToPoolCallback;

        private Vector3 _spawnWorldOrigin;
        private float _riseDistanceMeters = 1.25f;
        private float _growPhaseRatio = FloatingDamageTextConfigSO.CanonicalGrowPhaseRatio;
        private float _startScale = 0.85f;
        private float _peakScale = 1.50f;
        private float _endScale = 0.55f;
        private float _alphaAtPeak = 0.85f;

        private float _currentNormalizedTime;
        private float _currentScaleMultiplier = 1.0f;
        private float _currentAnimAlpha = 1.0f;
        private float _lastDistanceScale = 1.0f;
        private int _displayedValue;
        private bool _isLocalPlayerTarget;
        private bool _isHealNumber;
        private bool _hiddenByLocalFps;
        private bool _isActiveInScene;

        /// <summary>Gets a value indicating whether this pooled instance is currently active in world space.</summary>
        public bool IsActiveInScene => _isActiveInScene;

        /// <summary>Gets a value indicating whether this active instance is currently visible to the local camera (not suppressed by local FPS).</summary>
        public bool IsVisibleToLocalCamera => _isActiveInScene && !_hiddenByLocalFps;

        /// <summary>Gets a value indicating whether this number belongs to the local player's own avatar.</summary>
        public bool IsLocalPlayerTarget => _isLocalPlayerTarget;

        /// <summary>Gets a value indicating whether this entry represents healing (green) rather than damage (red).</summary>
        public bool IsHealNumber => _isHealNumber;

        /// <summary>Gets the positive integer amount currently displayed (without minus sign).</summary>
        public int DisplayedValue => _displayedValue;

        /// <summary>Gets the rendered string inside the TMP label.</summary>
        public string RenderedText => _valueText != null ? _valueText.text : string.Empty;

        /// <summary>Gets the active text color assigned from <see cref="IFloatingDamageTextConfig"/>.</summary>
        public Color CurrentTextColor => _valueText != null ? _valueText.color : Color.clear;

        /// <summary>Gets the current normalized flight progress in [0, 1].</summary>
        public float CurrentNormalizedTime => _currentNormalizedTime;

        /// <summary>Gets the current animation scale multiplier evaluated from the 2/3-grow, 1/3-shrink curve.</summary>
        public float CurrentScaleMultiplier => _currentScaleMultiplier;

        /// <summary>Gets the current effective CanvasGroup alpha.</summary>
        public float CurrentAlpha => _canvasGroup != null ? _canvasGroup.alpha : 0f;

        /// <summary>Builds world-space Canvas + TMP hierarchy once during pool pre-warm.</summary>
        public void InitializePoolItem(Action<PooledFloatingDamageText> returnToPoolCallback)
        {
            _returnToPoolCallback = returnToPoolCallback;
            _cachedSampleAction = ApplyNormalizedTweenSample;
            _cachedCompleteAction = HandleTweenCompleted;
            BuildWorldTextHierarchy();
            DeactivateToPool();
        }

        /// <summary>
        /// Activates and starts the upward <see cref="DOTween"/> animation from <paramref name="worldSpawnOrigin"/>.
        /// </summary>
        public void PlayFloatingNumber(
            Vector3 worldSpawnOrigin,
            int amount,
            bool isHeal,
            bool isLocalTarget,
            IFloatingDamageTextConfig config,
            CameraPerspectiveMode currentLocalPerspective = CameraPerspectiveMode.TPS)
        {
            BuildWorldTextHierarchy();
            DOTween.Kill(this, complete: false);

            _spawnWorldOrigin = worldSpawnOrigin;
            _displayedValue = Mathf.Max(1, Mathf.Abs(amount));
            _isHealNumber = isHeal;
            _isLocalPlayerTarget = isLocalTarget;
            _hiddenByLocalFps = isLocalTarget && currentLocalPerspective == CameraPerspectiveMode.FPS;

            ApplyConfigParameters(config, isHeal);
            FormatNumberZeroAlloc(_displayedValue);

            _isActiveInScene = true;
            _lastDistanceScale = 1.0f;
            gameObject.SetActive(true);

            float lifetime = config != null ? Mathf.Max(0.10f, config.LifetimeSeconds) : 0.85f;
            DOTween.To(this, _cachedSampleAction, lifetime).OnComplete(_cachedCompleteAction);
        }

        /// <summary>Updates camera-facing billboard rotation, distance scale, and local-FPS suppression.</summary>
        public void UpdateBillboardAndVisibility(Transform cameraTransform, CameraPerspectiveMode localPerspective)
        {
            if (!_isActiveInScene)
            {
                return;
            }

            _hiddenByLocalFps = _isLocalPlayerTarget && localPerspective == CameraPerspectiveMode.FPS;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _hiddenByLocalFps ? 0f : _currentAnimAlpha;
            }

            if (cameraTransform == null)
            {
                return;
            }

            Vector3 awayFromCamera = transform.position - cameraTransform.position;
            if (awayFromCamera.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(awayFromCamera.normalized, cameraTransform.rotation * Vector3.up);
            }

            _lastDistanceScale = OverheadBillboardUI.CalculateBillboardScale(awayFromCamera.magnitude);
            transform.localScale = Vector3.one * (BaseWorldScale * _lastDistanceScale * _currentScaleMultiplier);
        }

        /// <summary>Cancels active tweens and returns this GameObject to inactive pool storage.</summary>
        public void DeactivateToPool()
        {
            DOTween.Kill(this, complete: false);
            _isActiveInScene = false;
            _currentNormalizedTime = 0f;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// Pure two-phase scale curve: enlarges from <paramref name="startScale"/> to <paramref name="peakScale"/>
        /// across <c>[0, growRatio]</c> (<c>2/3</c>), then shrinks to <paramref name="endScale"/> across <c>[growRatio, 1]</c> (<c>1/3</c>).
        /// </summary>
        public static float EvaluateScaleMultiplier(
            float normalizedTime,
            float growRatio = FloatingDamageTextConfigSO.CanonicalGrowPhaseRatio,
            float startScale = 0.85f,
            float peakScale = 1.50f,
            float endScale = 0.55f)
        {
            float t = Mathf.Clamp01(normalizedTime);
            float split = Mathf.Clamp(growRatio, 0.10f, 0.90f);

            if (t <= split)
            {
                float u = Mathf.Clamp01(t / split);
                float easedGrow = 1.0f - (1.0f - u) * (1.0f - u);
                return Mathf.Lerp(startScale, peakScale, easedGrow);
            }

            float v = Mathf.Clamp01((t - split) / (1.0f - split));
            return Mathf.Lerp(peakScale, endScale, v);
        }

        /// <summary>
        /// Pure monotonic fade curve: gently dims from <c>1.0</c> to <paramref name="alphaAtPeak"/> while enlarging
        /// across <c>[0, growRatio]</c>, then fades to <c>0.0</c> simultaneously with shrinking across <c>[growRatio, 1]</c>.
        /// </summary>
        public static float EvaluateAlpha(
            float normalizedTime,
            float growRatio = FloatingDamageTextConfigSO.CanonicalGrowPhaseRatio,
            float alphaAtPeak = 0.85f)
        {
            float t = Mathf.Clamp01(normalizedTime);
            float split = Mathf.Clamp(growRatio, 0.10f, 0.90f);
            float clampedMidAlpha = Mathf.Clamp(alphaAtPeak, 0.10f, 1.0f);

            if (t <= split)
            {
                float u = Mathf.Clamp01(t / split);
                return Mathf.Lerp(1.0f, clampedMidAlpha, u);
            }

            float v = Mathf.Clamp01((t - split) / (1.0f - split));
            return Mathf.Lerp(clampedMidAlpha, 0.0f, v);
        }

        /// <summary>Pure smooth upward displacement in meters for normalized time <paramref name="normalizedTime"/>.</summary>
        public static float EvaluateVerticalOffset(float normalizedTime, float riseDistanceMeters)
        {
            float t = Mathf.Clamp01(normalizedTime);
            float eased = 1.0f - (1.0f - t) * (1.0f - t);
            return riseDistanceMeters * eased;
        }

        private void ApplyNormalizedTweenSample(float normalizedT)
        {
            _currentNormalizedTime = Mathf.Clamp01(normalizedT);
            float yOffset = EvaluateVerticalOffset(_currentNormalizedTime, _riseDistanceMeters);
            _currentScaleMultiplier = EvaluateScaleMultiplier(
                _currentNormalizedTime, _growPhaseRatio, _startScale, _peakScale, _endScale);
            _currentAnimAlpha = EvaluateAlpha(_currentNormalizedTime, _growPhaseRatio, _alphaAtPeak);

            transform.position = _spawnWorldOrigin + Vector3.up * yOffset;
            transform.localScale = Vector3.one * (BaseWorldScale * _lastDistanceScale * _currentScaleMultiplier);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _hiddenByLocalFps ? 0f : _currentAnimAlpha;
            }
        }

        private void HandleTweenCompleted()
        {
            if (!_isActiveInScene)
            {
                return;
            }

            Action<PooledFloatingDamageText> cb = _returnToPoolCallback;
            DeactivateToPool();
            cb?.Invoke(this);
        }

        private void ApplyConfigParameters(IFloatingDamageTextConfig cfg, bool isHeal)
        {
            _riseDistanceMeters = cfg != null ? cfg.RiseDistanceMeters : 1.25f;
            _growPhaseRatio = cfg != null ? cfg.GrowPhaseNormalizedRatio : FloatingDamageTextConfigSO.CanonicalGrowPhaseRatio;
            _startScale = cfg != null ? cfg.StartScale : 0.85f;
            _peakScale = cfg != null ? cfg.PeakScale : 1.50f;
            _endScale = cfg != null ? cfg.EndScale : 0.55f;
            _alphaAtPeak = cfg != null ? cfg.AlphaAtPeak : 0.85f;

            if (_valueText != null)
            {
                Color targetColor = isHeal
                    ? (cfg != null ? cfg.HealTextColor : FloatingDamageTextConfigSO.DefaultHealGreen)
                    : (cfg != null ? cfg.DamageTextColor : FloatingDamageTextConfigSO.DefaultDamageRed);
                _valueText.color = targetColor;
                _valueText.fontSize = cfg != null ? cfg.FontSize : 34.0f;
            }
        }

        private void FormatNumberZeroAlloc(int positiveValue)
        {
            if (_valueText == null)
            {
                return;
            }

            _sb.Clear();
            _sb.Append(positiveValue);
            _valueText.SetText(_sb);
        }

        private void BuildWorldTextHierarchy()
        {
            if (_valueText != null && _canvasGroup != null)
            {
                return;
            }

            if (!TryGetComponent(out _rootRect))
            {
                _rootRect = gameObject.AddComponent<RectTransform>();
            }

            _rootRect.sizeDelta = new Vector2(180f, 64f);

            if (!TryGetComponent(out _worldCanvas))
            {
                _worldCanvas = gameObject.AddComponent<Canvas>();
                _worldCanvas.renderMode = RenderMode.WorldSpace;
                _worldCanvas.sortingOrder = 25;
            }

            if (!TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            GameObject txtObj = new GameObject("Txt_FloatingDelta", typeof(RectTransform));
            txtObj.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = txtObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.fontStyle = FontStyles.Bold;
            tmp.fontSize = 34.0f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.outlineWidth = 0.22f;
            tmp.outlineColor = new Color32(10, 15, 28, 235);
            _valueText = tmp;
        }
    }
}
