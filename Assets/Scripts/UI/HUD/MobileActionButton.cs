using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.UI.Common;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// Zero-frame-lag touch action button implementing <see cref="IPointerDownHandler"/> and <see cref="IPointerUpHandler"/>
    /// (never <c>Button.onClick</c>), driving <see cref="MobileInputBridge"/>, radial <see cref="Image.fillAmount"/>
    /// cooldown sweeps, pre-cached <c>0.1s</c> countdown strings, and dynamic PC keybinding badges
    /// (Story 10-2; ADR-0007; TR-hud-002).
    /// </summary>
    public sealed class MobileActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private const float PressScaleMultiplier = 0.90f;
        private const float PressScaleDurationSec = 0.06f;

        [Header("Action Mapping")]
        [Tooltip("Bit index forwarded to MobileInputBridge on pointer down/up.")]
        [SerializeField] private NetworkInputButtons _targetButton;

        [Tooltip("Corresponding rebindable action used to render the shortcut badge ([LMB], [1], [Q], etc.).")]
        [SerializeField] private GameInputAction _mappedGameAction;

        [Tooltip("Cooldown group displayed on this button's radial sweep overlay.")]
        [SerializeField] private CombatSkillId _cooldownSkillId;

        [Tooltip("Whether this button maps to a cooldown-gated skill.")]
        [SerializeField] private bool _usesCooldown = true;

        [Header("UI Elements")]
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Image _radialCooldownOverlay;
        [SerializeField] private TMP_Text _actionTitleText;
        [SerializeField] private TMP_Text _cooldownTimerText;
        [SerializeField] private TMP_Text _shortcutBadgeText;

        private MobileInputBridge _mobileBridge;
        private bool _isPointerHeld;
        private float _cooldownRemainingSec;
        private float _cooldownTotalSec;

        /// <summary>Gets the mapped <see cref="NetworkInputButtons"/> bit.</summary>
        public NetworkInputButtons TargetButton => _targetButton;

        /// <summary>Gets the mapped <see cref="GameInputAction"/>.</summary>
        public GameInputAction MappedGameAction => _mappedGameAction;

        /// <summary>Gets the current shortcut badge text (e.g. <c>[2]</c> or <c>[Q]</c>).</summary>
        public string CurrentBadgeText => _shortcutBadgeText != null ? _shortcutBadgeText.text : string.Empty;

        /// <summary>Gets the current countdown timer text (e.g. <c>"1.5"</c> or empty).</summary>
        public string CurrentCooldownText => _cooldownTimerText != null ? _cooldownTimerText.text : string.Empty;

        /// <summary>Gets the current radial cooldown fill in [0, 1].</summary>
        public float RadialFillAmount => _radialCooldownOverlay != null ? _radialCooldownOverlay.fillAmount : 0f;

        /// <summary>Gets a value indicating whether a pointer is currently holding this button down.</summary>
        public bool IsPointerHeld => _isPointerHeld;

        private void OnDisable()
        {
            ReleaseIfHeld();
            transform.DOKill(complete: false);
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (_cooldownRemainingSec > 0f)
            {
                SetCooldownState(Mathf.Max(0f, _cooldownRemainingSec - Time.deltaTime), _cooldownTotalSec);
            }
        }

        /// <summary>Initializes button metadata, bridge reference, and child visuals.</summary>
        public void Configure(
            MobileInputBridge bridge,
            NetworkInputButtons buttonBit,
            GameInputAction gameAction,
            CombatSkillId skillId,
            string label,
            Color accentColor,
            bool usesCooldown = true)
        {
            _mobileBridge = bridge;
            _targetButton = buttonBit;
            _mappedGameAction = gameAction;
            _cooldownSkillId = skillId;
            _usesCooldown = usesCooldown;

            EnsureVisualsBuilt(label, accentColor);
            SetCooldownState(0f, 1f);
        }

        /// <summary>Rebinds the <see cref="MobileInputBridge"/> target at runtime.</summary>
        public void BindBridge(MobileInputBridge bridge)
        {
            _mobileBridge = bridge;
        }

        /// <inheritdoc />
        public void OnPointerDown(PointerEventData eventData)
        {
            _isPointerHeld = true;
            _mobileBridge?.PressButton(_targetButton);
            transform.DOKill(complete: false);
            transform.DOScale(Vector3.one * PressScaleMultiplier, PressScaleDurationSec).SetEase(Ease.OutQuad);
        }

        /// <inheritdoc />
        public void OnPointerUp(PointerEventData eventData)
        {
            ReleaseIfHeld();
            transform.DOKill(complete: false);
            transform.DOScale(Vector3.one, PressScaleDurationSec).SetEase(Ease.OutQuad);
        }

        /// <summary>Releases any active hold immediately (invoked on focus loss or Ghost transition).</summary>
        public void ReleaseIfHeld()
        {
            if (!_isPointerHeld)
            {
                return;
            }

            _isPointerHeld = false;
            _mobileBridge?.ReleaseButton(_targetButton);
        }

        /// <summary>Updates radial sweep fill and 0.1s countdown text if <paramref name="skillId"/> matches.</summary>
        public void NotifySkillCooldown(CombatSkillId skillId, float remainingSec, float totalSec)
        {
            if (_usesCooldown && _cooldownSkillId == skillId)
            {
                SetCooldownState(remainingSec, totalSec);
            }
        }

        /// <summary>Updates the radial fill and pre-cached tenths countdown string directly.</summary>
        public void SetCooldownState(float remainingSec, float totalSec)
        {
            _cooldownRemainingSec = Mathf.Max(0f, remainingSec);
            _cooldownTotalSec = Mathf.Max(0.01f, totalSec);

            float fill = _cooldownRemainingSec > 0f ? Mathf.Clamp01(_cooldownRemainingSec / _cooldownTotalSec) : 0f;
            if (_radialCooldownOverlay != null)
            {
                _radialCooldownOverlay.fillAmount = fill;
                _radialCooldownOverlay.enabled = fill > 0.001f;
            }

            if (_cooldownTimerText != null)
            {
                _cooldownTimerText.text = UiThemeAndFontProvider.FormatCooldownTenthsZeroAlloc(_cooldownRemainingSec);
            }
        }

        /// <summary>Refreshes the shortcut badge text from an Input System binding path.</summary>
        public void RefreshBadgeFromBindingPath(string bindingPath)
        {
            if (_shortcutBadgeText != null)
            {
                UiThemeAndFontProvider.EnsureFontAssigned(_shortcutBadgeText);
                _shortcutBadgeText.text = FormatHumanBadge(bindingPath);
            }
        }

        /// <summary>Converts an Input System path (<c>&lt;Keyboard&gt;/q</c>, <c>&lt;Mouse&gt;/leftButton</c>) into a concise badge (<c>[Q]</c>, <c>[LMB]</c>).</summary>
        public static string FormatHumanBadge(string bindingPath)
        {
            if (string.IsNullOrWhiteSpace(bindingPath))
            {
                return "[-]";
            }

            string trimmed = bindingPath.Trim();
            int slash = trimmed.LastIndexOf('/');
            string token = slash >= 0 && slash + 1 < trimmed.Length ? trimmed.Substring(slash + 1) : trimmed;

            return token.ToLowerInvariant() switch
            {
                "leftbutton" => "[LMB]",
                "rightbutton" => "[RMB]",
                "middlebutton" => "[MMB]",
                "space" => "[Space]",
                "enter" or "return" => "[Enter]",
                "leftshift" or "rightshift" => "[SHF]",
                "leftctrl" or "rightctrl" => "[CTL]",
                _ => $"[{token.ToUpperInvariant()}]"
            };
        }

        private void EnsureVisualsBuilt(string label, Color accentColor)
        {
            EnsureBackgroundAndRadialOverlay(accentColor);
            EnsureButtonLabels(label);
        }

        private void EnsureBackgroundAndRadialOverlay(Color accentColor)
        {
            if (_backgroundImage == null && !TryGetComponent(out _backgroundImage))
            {
                _backgroundImage = gameObject.AddComponent<Image>();
            }

            _backgroundImage.sprite = UiThemeAndFontProvider.GetCircleSprite();
            _backgroundImage.color = accentColor;
            _backgroundImage.raycastTarget = true;

            if (_radialCooldownOverlay != null)
            {
                return;
            }

            GameObject cdGo = new GameObject("RadialCooldownOverlay", typeof(RectTransform));
            cdGo.transform.SetParent(transform, worldPositionStays: false);
            StretchFull(cdGo.GetComponent<RectTransform>());
            _radialCooldownOverlay = cdGo.AddComponent<Image>();
            _radialCooldownOverlay.sprite = UiThemeAndFontProvider.GetCircleSprite();
            _radialCooldownOverlay.type = Image.Type.Filled;
            _radialCooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            _radialCooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
            _radialCooldownOverlay.fillClockwise = false;
            _radialCooldownOverlay.color = new Color(0f, 0f, 0f, 0.72f);
            _radialCooldownOverlay.raycastTarget = false;
        }

        private void EnsureButtonLabels(string label)
        {
            if (_actionTitleText == null)
            {
                _actionTitleText = CreateLabel("Txt_Label", label, 15f, new Vector2(0f, 0.25f), new Vector2(1f, 0.80f), Color.white);
            }
            else
            {
                _actionTitleText.text = label;
            }

            if (_cooldownTimerText == null)
            {
                _cooldownTimerText = CreateLabel("Txt_Cooldown", string.Empty, 18f, Vector2.zero, Vector2.one, UiThemeAndFontProvider.SolarAmber);
            }

            if (_shortcutBadgeText == null)
            {
                _shortcutBadgeText = CreateLabel("Txt_KeyBadge", "[?]", 12f, new Vector2(0f, 0.02f), new Vector2(1f, 0.30f), new Color(1f, 1f, 1f, 0.88f));
            }
        }

        private TMP_Text CreateLabel(string childName, string text, float fontSize, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            GameObject go = new GameObject(childName, typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
