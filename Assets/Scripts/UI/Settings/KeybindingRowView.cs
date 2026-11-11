using System;
using CapsuleArena.Core.Input;
using CapsuleArena.UI.Common;
using CapsuleArena.UI.HUD;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.Settings
{
    /// <summary>
    /// Single action row inside <see cref="KeybindingRebindModal"/> displaying the human-readable action
    /// label, current key badge (<c>[W]</c>, <c>[Q]</c>, <c>[LMB]</c>), <c>[Rebind]</c> button, and a
    /// <c>1.2s</c> <c>#FFB703</c> Solar Amber highlight flash with high-contrast <c>#0F172A</c> foreground text
    /// when bindings swap on conflict (Story 11-2; ADR-0002; TR-rebind-002).
    /// </summary>
    public sealed class KeybindingRowView : MonoBehaviour
    {
        /// <summary>Duration in seconds of the <c>#FFB703</c> duplicate-key swap highlight flash (1.2s).</summary>
        public const float SwapHighlightDurationSec = 1.2f;

        /// <summary>High-contrast dark foreground text color (#0F172A) while row is highlighted in #FFB703.</summary>
        public static readonly Color HighlightDarkForeground = new Color32(15, 23, 42, 255);

        [SerializeField] private GameInputAction _action;
        [SerializeField] private Image _rowBackground;
        [SerializeField] private TMP_Text _actionNameText;
        [SerializeField] private TMP_Text _bindingBadgeText;
        [SerializeField] private Button _rebindButton;

        private Action<GameInputAction> _onRebindClicked;
        private float _swapHighlightRemainingSec;

        /// <summary>Gets the gameplay action represented by this row.</summary>
        public GameInputAction Action => _action;

        /// <summary>Gets the current formatted badge text (e.g. <c>"[W]"</c>, <c>"[LMB]"</c>).</summary>
        public string CurrentBadgeText => _bindingBadgeText != null ? _bindingBadgeText.text : string.Empty;

        /// <summary>Gets a value indicating whether this row is currently highlighted in <c>#FFB703</c> after a conflict swap.</summary>
        public bool IsSwapHighlighted => _swapHighlightRemainingSec > 0f;

        /// <summary>Gets the current row background color.</summary>
        public Color CurrentBackgroundColor => _rowBackground != null ? _rowBackground.color : Color.clear;

        /// <summary>Gets the current action name label color.</summary>
        public Color CurrentActionLabelColor => _actionNameText != null ? _actionNameText.color : Color.clear;

        /// <summary>Gets the row's <c>[Rebind]</c> button.</summary>
        public Button RebindButton => _rebindButton;

        private void Awake()
        {
            EnsureVisualsBuilt();
        }

        private void Update()
        {
            if (_swapHighlightRemainingSec <= 0f)
            {
                return;
            }

            _swapHighlightRemainingSec -= Time.unscaledDeltaTime;
            if (_swapHighlightRemainingSec <= 0f)
            {
                ClearHighlight();
            }
        }

        /// <summary>Configures the row for a specific <see cref="GameInputAction"/> and binds its callback.</summary>
        public void Configure(GameInputAction action, string actionDisplayLabel, string bindingPath, Action<GameInputAction> onRebindClicked)
        {
            EnsureVisualsBuilt();
            _action = action;
            _onRebindClicked = onRebindClicked;

            if (_actionNameText != null)
            {
                _actionNameText.text = actionDisplayLabel;
            }

            RefreshBindingBadge(bindingPath);

            if (_rebindButton != null)
            {
                _rebindButton.onClick.RemoveAllListeners();
                _rebindButton.onClick.AddListener(HandleRebindButtonClicked);
            }
        }

        /// <summary>Updates the human-readable bracketed badge (<c>[W]</c>, <c>[LMB]</c>, <c>[Enter]</c>).</summary>
        public void RefreshBindingBadge(string bindingPath)
        {
            if (_bindingBadgeText != null)
            {
                _bindingBadgeText.text = MobileActionButton.FormatHumanBadge(bindingPath);
            }
        }

        /// <summary>Flashes the row in <c>#FFB703</c> Solar Amber for <see cref="SwapHighlightDurationSec"/> (1.2s).</summary>
        public void TriggerSwapHighlight(float durationSec = SwapHighlightDurationSec)
        {
            EnsureVisualsBuilt();
            _swapHighlightRemainingSec = Mathf.Max(0.1f, durationSec);
            ApplyHighlightColors(highlighted: true);

            if (transform is RectTransform rt)
            {
                rt.DOKill(complete: true);
                rt.DOPulseScaleToOne(new Vector3(1.03f, 1.06f, 1f), 0.16f);
            }
        }

        /// <summary>Clears any active swap highlight immediately and restores resting text contrast.</summary>
        public void ClearHighlight()
        {
            _swapHighlightRemainingSec = 0f;
            ApplyHighlightColors(highlighted: false);
        }

        private void HandleRebindButtonClicked()
        {
            _onRebindClicked?.Invoke(_action);
        }

        private void ApplyHighlightColors(bool highlighted)
        {
            if (_rowBackground != null)
            {
                _rowBackground.color = highlighted ? UiThemeAndFontProvider.SwapHighlightAmber : UiThemeAndFontProvider.CardSurface;
            }

            if (_actionNameText != null)
            {
                _actionNameText.color = highlighted ? HighlightDarkForeground : UiThemeAndFontProvider.PureFrost;
            }

            if (_bindingBadgeText != null)
            {
                _bindingBadgeText.color = highlighted ? HighlightDarkForeground : UiThemeAndFontProvider.ElectricCyan;
            }
        }

        private void EnsureVisualsBuilt()
        {
            EnsureRowBackground();
            EnsureActionNameLabel();
            EnsureBindingBadgeLabel();
            EnsureRebindButton();
        }

        private void EnsureRowBackground()
        {
            if (!TryGetComponent(out RectTransform rt))
            {
                rt = gameObject.AddComponent<RectTransform>();
            }

            if (rt.sizeDelta.sqrMagnitude < 1f)
            {
                rt.sizeDelta = new Vector2(560f, 34f);
            }

            if (_rowBackground == null && !TryGetComponent(out _rowBackground))
            {
                _rowBackground = gameObject.AddComponent<Image>();
                _rowBackground.sprite = UiThemeAndFontProvider.GetWhiteSprite();
                _rowBackground.color = UiThemeAndFontProvider.CardSurface;
            }
        }

        private void EnsureActionNameLabel()
        {
            if (_actionNameText != null)
            {
                return;
            }

            GameObject labelGo = new GameObject("Txt_ActionName", typeof(RectTransform));
            labelGo.transform.SetParent(transform, worldPositionStays: false);
            RectTransform lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0.55f, 1f);
            lrt.offsetMin = new Vector2(12f, 2f);
            lrt.offsetMax = new Vector2(-4f, -2f);

            _actionNameText = labelGo.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(_actionNameText);
            _actionNameText.fontSize = 14f;
            _actionNameText.fontStyle = FontStyles.Bold;
            _actionNameText.alignment = TextAlignmentOptions.MidlineLeft;
            _actionNameText.color = UiThemeAndFontProvider.PureFrost;
            _actionNameText.raycastTarget = false;
        }

        private void EnsureBindingBadgeLabel()
        {
            if (_bindingBadgeText != null)
            {
                return;
            }

            GameObject badgeGo = new GameObject("Txt_BindingBadge", typeof(RectTransform));
            badgeGo.transform.SetParent(transform, worldPositionStays: false);
            RectTransform brt = badgeGo.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.55f, 0f);
            brt.anchorMax = new Vector2(0.77f, 1f);
            brt.offsetMin = new Vector2(4f, 2f);
            brt.offsetMax = new Vector2(-4f, -2f);

            _bindingBadgeText = badgeGo.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(_bindingBadgeText);
            _bindingBadgeText.fontSize = 14f;
            _bindingBadgeText.fontStyle = FontStyles.Bold;
            _bindingBadgeText.alignment = TextAlignmentOptions.Center;
            _bindingBadgeText.color = UiThemeAndFontProvider.ElectricCyan;
            _bindingBadgeText.raycastTarget = false;
        }

        private void EnsureRebindButton()
        {
            if (_rebindButton != null)
            {
                return;
            }

            GameObject btnGo = new GameObject("BTN_Rebind", typeof(RectTransform));
            btnGo.transform.SetParent(transform, worldPositionStays: false);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.78f, 0.1f);
            btnRt.anchorMax = new Vector2(0.98f, 0.9f);
            btnRt.offsetMin = Vector2.zero;
            btnRt.offsetMax = Vector2.zero;

            Image btnImg = btnGo.AddComponent<Image>();
            btnImg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            btnImg.color = new Color32(51, 65, 85, 255);
            _rebindButton = btnGo.AddComponent<Button>();
            _rebindButton.targetGraphic = btnImg;

            GameObject txtGo = new GameObject("Txt_Rebind", typeof(RectTransform));
            txtGo.transform.SetParent(btnGo.transform, worldPositionStays: false);
            RectTransform trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = txtGo.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = "Rebind";
            tmp.fontSize = 13f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = UiThemeAndFontProvider.PureFrost;
            tmp.raycastTarget = false;
        }
    }
}
