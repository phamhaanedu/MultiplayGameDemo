using CapsuleArena.UI.Common;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.Lobby
{
    /// <summary>
    /// Non-blocking transient banner notification displaying validation errors, connection timeouts,
    /// and key-swap confirmations (Stories 11-1, 11-2; ADR-0002).
    /// </summary>
    public sealed class ToastNotificationView : MonoBehaviour
    {
        /// <summary>Default display duration in seconds before fading out (3.0s).</summary>
        public const float DefaultDisplayDurationSec = 3.0f;

        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _backgroundBanner;
        [SerializeField] private TMP_Text _messageText;

        private float _remainingSec;

        /// <summary>Gets a value indicating whether a toast message is currently visible.</summary>
        public bool IsShowing => _canvasGroup != null && _canvasGroup.alpha > 0.05f && _remainingSec > 0f;

        /// <summary>Gets the most recently displayed toast message.</summary>
        public string LastMessage => _messageText != null ? _messageText.text : string.Empty;

        private void Awake()
        {
            EnsureVisualsBuilt();
            HideImmediate();
        }

        private void Update()
        {
            if (_remainingSec <= 0f)
            {
                return;
            }

            _remainingSec -= Time.unscaledDeltaTime;
            if (_remainingSec <= 0f)
            {
                HideImmediate();
            }
        }

        /// <summary>Displays a non-blocking toast message styled as error or informational highlight.</summary>
        public void ShowToast(string message, bool isError = false, float durationSec = DefaultDisplayDurationSec)
        {
            EnsureVisualsBuilt();
            _remainingSec = Mathf.Max(0.2f, durationSec);

            if (_messageText != null)
            {
                UiThemeAndFontProvider.EnsureFontAssigned(_messageText);
                _messageText.text = message ?? string.Empty;
            }

            if (_backgroundBanner != null)
            {
                _backgroundBanner.color = isError
                    ? new Color32(185, 28, 28, 240)
                    : new Color32(30, 41, 59, 245);
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.DOKill(complete: false);
                _canvasGroup.alpha = 1.0f;
            }
        }

        /// <summary>Immediately hides the toast banner.</summary>
        public void HideImmediate()
        {
            _remainingSec = 0f;
            if (_canvasGroup != null)
            {
                _canvasGroup.DOKill(complete: false);
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void EnsureVisualsBuilt()
        {
            EnsureBannerContainer();
            EnsureMessageLabel();
        }

        private void EnsureBannerContainer()
        {
            if (!TryGetComponent(out RectTransform rt))
            {
                rt = gameObject.AddComponent<RectTransform>();
            }

            if (rt.sizeDelta.x <= 105f && rt.sizeDelta.y <= 105f)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -34f);
                rt.sizeDelta = new Vector2(640f, 42f);
            }

            if (_canvasGroup == null && !TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (_backgroundBanner == null && !TryGetComponent(out _backgroundBanner))
            {
                _backgroundBanner = gameObject.AddComponent<Image>();
                _backgroundBanner.sprite = UiThemeAndFontProvider.GetWhiteSprite();
                _backgroundBanner.color = UiThemeAndFontProvider.CardSurface;
                _backgroundBanner.raycastTarget = false;
            }
        }

        private void EnsureMessageLabel()
        {
            if (_messageText != null)
            {
                return;
            }

            GameObject txtGo = new GameObject("Txt_ToastMessage", typeof(RectTransform));
            txtGo.transform.SetParent(transform, worldPositionStays: false);
            RectTransform trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 4f);
            trt.offsetMax = new Vector2(-14f, -4f);

            _messageText = txtGo.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(_messageText);
            _messageText.fontSize = 16f;
            _messageText.fontStyle = FontStyles.Bold;
            _messageText.alignment = TextAlignmentOptions.Center;
            _messageText.color = UiThemeAndFontProvider.SolarAmber;
            _messageText.raycastTarget = false;
        }
    }
}
