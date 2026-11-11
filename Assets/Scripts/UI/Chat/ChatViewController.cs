using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CapsuleArena.UI.Chat
{
    /// <summary>
    /// Visual states of the persistent bottom-left Chat Panel controlled exclusively via <see cref="CanvasGroup"/> (Story 12-1).
    /// </summary>
    public enum ChatVisualState : byte
    {
        /// <summary>Input field focused; alpha = 1.0, interactable = true, blocksRaycasts = true, InputFocusFlag.ChatFocused = true.</summary>
        Focused = 0,

        /// <summary>Recently unfocused; alpha = 0.65, interactable = false, blocksRaycasts = false.</summary>
        Passive = 1,

        /// <summary>Woken briefly by an incoming message while unfocused; alpha = 0.75, interactable = false, blocksRaycasts = false.</summary>
        IncomingWake = 2,

        /// <summary>Idle for &gt;= 6.0s without new messages; alpha = 0.35, interactable = false, blocksRaycasts = false.</summary>
        Minimized = 3
    }

    /// <summary>
    /// Controls the persistent bottom-left <c>520x280px</c> In-Game Chat Window exclusively via <see cref="CanvasGroup"/>
    /// (<c>alpha</c>, <c>interactable</c>, <c>blocksRaycasts</c>) while keeping <c>gameObject.activeSelf == true</c>
    /// permanently so Photon Fusion 2 RPC handlers never drop messages. Synchronizes <see cref="InputFocusFlag.ChatFocused"/>
    /// on <c>Enter</c>/<c>Escape</c> (and custom <c>ToggleChatFocus</c> bindings), enforces the <c>5 msgs / 3.0s</c>
    /// <see cref="ChatRateLimiter"/>, and renders into the 50-row <see cref="ChatMessageRingBuffer"/>
    /// (Stories 12-1, 12-2, 12-3; ADR-0007; TR-chat-001..003).
    /// </summary>
    public sealed class ChatViewController : MonoBehaviour
    {
        /// <summary>Canonical chat window width in reference pixels (520px).</summary>
        public const float PanelWidthPx = 520.0f;

        /// <summary>Canonical chat window height in reference pixels (280px).</summary>
        public const float PanelHeightPx = 280.0f;

        /// <summary>Idle seconds before unfocused chat fades from Passive/Wake down to Minimized 0.35 alpha (6.0s).</summary>
        public const float PassiveFadeDelaySeconds = 6.0f;

        /// <summary>CanvasGroup alpha when actively focused (1.00).</summary>
        public const float FocusedAlpha = 1.00f;

        /// <summary>CanvasGroup alpha immediately after unfocusing (0.65).</summary>
        public const float PassiveAlpha = 0.65f;

        /// <summary>CanvasGroup alpha when an incoming message arrives while unfocused (0.75).</summary>
        public const float IncomingWakeAlpha = 0.75f;

        /// <summary>CanvasGroup alpha after 6.0s without messages while unfocused (0.35).</summary>
        public const float MinimizedAlpha = 0.35f;

        [Header("UI Hierarchy")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_InputField _chatInputField;
        [SerializeField] private Button _chatToggleButton;
        [SerializeField] private Button _sendButton;
        [SerializeField] private TMP_Text _channelHintText;
        [SerializeField] private ChatMessageRingBuffer _ringBuffer;
        [SerializeField] private ChatNetworkRouter _networkRouter;

        private ChatRateLimiter _rateLimiter = new ChatRateLimiter();
        private IInputFocusService _focusService;
        private IKeybindingService _keybindingService;
        private LocalInputPoller _inputPoller;
        private MatchManager _matchManager;
        private ChatVisualState _visualState = ChatVisualState.Passive;
        private float _idleTimerSec;
        private bool _isChatFocused;

        /// <summary>Gets a value indicating whether the chat input currently holds keyboard focus.</summary>
        public bool IsChatFocused => _isChatFocused;

        /// <summary>Gets the current <see cref="ChatVisualState"/>.</summary>
        public ChatVisualState CurrentVisualState => _visualState;

        /// <summary>Gets the current <see cref="CanvasGroup"/> alpha.</summary>
        public float CurrentAlpha => _canvasGroup != null ? _canvasGroup.alpha : 0f;

        /// <summary>Gets the underlying <see cref="CanvasGroup"/> component.</summary>
        public CanvasGroup PanelCanvasGroup => _canvasGroup;

        /// <summary>Gets the 50-row <see cref="ChatMessageRingBuffer"/>.</summary>
        public ChatMessageRingBuffer RingBuffer => _ringBuffer;

        /// <summary>Gets the attached <see cref="ChatNetworkRouter"/>.</summary>
        public ChatNetworkRouter Router => _networkRouter;

        /// <summary>Gets the <see cref="TMP_InputField"/> for chat entry.</summary>
        public TMP_InputField InputField => _chatInputField;

        /// <summary>Gets the token-bucket <see cref="ChatRateLimiter"/>.</summary>
        public ChatRateLimiter RateLimiter => _rateLimiter;

        /// <summary>Gets the send button for UI click verification.</summary>
        public Button SendButton => _sendButton;

        /// <summary>Gets the header toggle button for UI click verification.</summary>
        public Button ToggleButton => _chatToggleButton;

        private void Awake()
        {
            EnsureActiveRoot();
            EnsureHierarchyBuilt();
            BindUiButtons();
            ApplyVisualState(ChatVisualState.Passive);
        }

        private void OnEnable()
        {
            ChatNetworkRouter.OnAnyChatLineDelivered -= HandleRoutedChatLineDelivered;
            ChatNetworkRouter.OnAnyChatLineDelivered += HandleRoutedChatLineDelivered;
        }

        private void OnDisable()
        {
            ChatNetworkRouter.OnAnyChatLineDelivered -= HandleRoutedChatLineDelivered;
            if (_isChatFocused)
            {
                SetChatFocus(false);
            }
        }

        private void OnDestroy()
        {
            UnbindCollaborators();
        }

        private void Update()
        {
            PollKeyboardShortcuts();
            TickIdleFade(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Injects input focus, router, rate limiter, match-state, and input-poller collaborators for runtime or unit tests.
        /// </summary>
        public void Initialize(
            IInputFocusService focusService,
            ChatNetworkRouter router = null,
            ChatRateLimiter rateLimiter = null,
            MatchManager matchManager = null,
            IKeybindingService keybindingService = null,
            LocalInputPoller inputPoller = null)
        {
            EnsureActiveRoot();
            UnbindCollaborators();
            EnsureHierarchyBuilt();
            BindUiButtons();

            _focusService = focusService;
            _keybindingService = keybindingService;
            _inputPoller = inputPoller;
            _matchManager = matchManager;
            if (router != null)
            {
                _networkRouter = router;
            }

            if (rateLimiter != null)
            {
                _rateLimiter = rateLimiter;
            }

            SubscribeCollaborators();
            SetChatFocus(false);
        }

        /// <summary>
        /// Transitions between Focused (<c>alpha = 1.0, interactable = true, blocksRaycasts = true</c>) and
        /// Passive (<c>alpha = 0.65, interactable = false, blocksRaycasts = false</c>) while keeping
        /// <c>gameObject.activeSelf == true</c> at all times (Story 12-1 AC-1, AC-2).
        /// </summary>
        public void SetChatFocus(bool focused)
        {
            EnsureHierarchyBuilt();
            EnsureActiveRoot();

            _isChatFocused = focused;
            _idleTimerSec = 0f;
            _focusService?.SetFocusFlag(InputFocusFlag.ChatFocused, focused);

            if (focused)
            {
                ApplyFocusedInputState();
            }
            else
            {
                ApplyPassiveInputState();
            }
        }

        /// <summary>
        /// Handles pressing <c>Enter</c> cleanly: focuses chat when unfocused; submits non-empty draft and unfocuses
        /// (or simply unfocuses on empty draft) when focused (Story 12-1 AC-2).
        /// </summary>
        public void HandleEnterPressed(float currentTimeSec)
        {
            if (!_isChatFocused)
            {
                if (IsBlockedByOtherModal())
                {
                    return;
                }

                SetChatFocus(true);
                return;
            }

            string rawDraft = _chatInputField != null ? _chatInputField.text : string.Empty;
            if (!string.IsNullOrWhiteSpace(rawDraft))
            {
                TrySubmitMessage(rawDraft, currentTimeSec);
            }

            ClearDraftText();
            SetChatFocus(false);
        }

        /// <summary>Handles pressing <c>Escape</c>: clears draft and releases <see cref="InputFocusFlag.ChatFocused"/>.</summary>
        public void HandleEscapePressed()
        {
            if (!_isChatFocused)
            {
                return;
            }

            ClearDraftText();
            SetChatFocus(false);
        }

        /// <summary>Handles pressing <c>Tab</c> while focused to auto-fill <c>/w &lt;LastWhisperSender&gt; </c>.</summary>
        public bool HandleTabQuickReply()
        {
            if (!_isChatFocused || _networkRouter == null || string.IsNullOrEmpty(_networkRouter.LastWhisperSenderName))
            {
                return false;
            }

            if (_chatInputField != null)
            {
                string replyPrefix = $"/w {_networkRouter.LastWhisperSenderName} ";
                _chatInputField.SetTextWithoutNotify(replyPrefix);
                _chatInputField.caretPosition = replyPrefix.Length;
            }

            return true;
        }

        /// <summary>
        /// Validates rate-limit tokens and routes <paramref name="rawText"/> via <see cref="ChatNetworkRouter"/>.
        /// Appends a local <c>#FFB703</c> <c>[SYSTEM]</c> rate-limit warning if tokens are exhausted (Story 12-3 AC-1).
        /// </summary>
        public bool TrySubmitMessage(string rawText, float currentTimeSec)
        {
            EnsureHierarchyBuilt();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return false;
            }

            if (!_rateLimiter.TryConsumeToken(currentTimeSec))
            {
                _ringBuffer.PushFormattedMessage(
                    "[SYSTEM] ",
                    "Slow down! Chat rate limit exceeded (max 5 msgs / 3s).",
                    UiThemeAndFontProvider.SolarAmber);
                return false;
            }

            return RouteSanitizedMessage(rawText);
        }

        /// <summary>Advances the 6.0s passive idle timer deterministically (callable directly in unit tests).</summary>
        public void TickIdleFade(float deltaTimeSec)
        {
            if (_isChatFocused || deltaTimeSec <= 0f || _visualState == ChatVisualState.Minimized)
            {
                return;
            }

            _idleTimerSec += deltaTimeSec;
            if (_idleTimerSec >= PassiveFadeDelaySeconds)
            {
                ApplyVisualState(ChatVisualState.Minimized);
            }
        }

        /// <summary>
        /// Appends an incoming message directly to the ring buffer and, if unfocused, raises <c>CanvasGroup.alpha</c>
        /// to <c>0.75</c> and resets the <c>6.0s</c> fade timer without stealing keyboard focus (Story 12-1 AC-3).
        /// </summary>
        public void NotifyIncomingMessage(ChatChannelKind kind, string prefix, string body)
        {
            EnsureHierarchyBuilt();
            Color color = kind switch
            {
                ChatChannelKind.Whisper => UiThemeAndFontProvider.WhisperViolet,
                ChatChannelKind.LocalSystemError => UiThemeAndFontProvider.SolarAmber,
                _ => UiThemeAndFontProvider.PublicOffWhite
            };

            _ringBuffer.PushFormattedMessage(prefix, body, color);
            if (!_isChatFocused)
            {
                _idleTimerSec = 0f;
                ApplyVisualState(ChatVisualState.IncomingWake);
            }
        }

        private void SubscribeCollaborators()
        {
            ChatNetworkRouter.OnAnyChatLineDelivered -= HandleRoutedChatLineDelivered;
            ChatNetworkRouter.OnAnyChatLineDelivered += HandleRoutedChatLineDelivered;

            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
            }

            if (_inputPoller != null)
            {
                _inputPoller.OnToggleChatFocusRequested += HandlePollerToggleChatRequested;
            }
        }

        private void UnbindCollaborators()
        {
            ChatNetworkRouter.OnAnyChatLineDelivered -= HandleRoutedChatLineDelivered;
            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
            }

            if (_inputPoller != null)
            {
                _inputPoller.OnToggleChatFocusRequested -= HandlePollerToggleChatRequested;
            }
        }

        private void HandlePollerToggleChatRequested()
        {
            HandleEnterPressed(Time.unscaledTime);
        }

        private bool IsBlockedByOtherModal()
        {
            return _focusService != null &&
                   (_focusService.ActiveFlags & ~InputFocusFlag.ChatFocused) != InputFocusFlag.None;
        }

        private void ApplyFocusedInputState()
        {
            ApplyVisualState(ChatVisualState.Focused);
            if (_chatInputField != null)
            {
                _chatInputField.interactable = true;
                _chatInputField.ActivateInputField();
            }

            UpdateHintLabel(focused: true);
        }

        private void ApplyPassiveInputState()
        {
            if (_chatInputField != null)
            {
                _chatInputField.DeactivateInputField();
            }

            ApplyVisualState(ChatVisualState.Passive);
            UpdateHintLabel(focused: false);
        }

        private void ClearDraftText()
        {
            if (_chatInputField != null)
            {
                _chatInputField.SetTextWithoutNotify(string.Empty);
            }
        }

        private bool RouteSanitizedMessage(string rawText)
        {
            if (_networkRouter != null)
            {
                ParsedChatCommand cmd = _networkRouter.SubmitRawChatInput(rawText);
                return cmd.Kind == ChatChannelKind.Public || cmd.Kind == ChatChannelKind.Whisper;
            }

            string clean = ChatCommandParser.SanitizeMessageBody(rawText);
            if (string.IsNullOrEmpty(clean))
            {
                return false;
            }

            _ringBuffer.PushFormattedMessage("[Local]: ", clean, UiThemeAndFontProvider.PublicOffWhite);
            return true;
        }

        private void EnsureActiveRoot()
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
        }

        private void HandleRoutedChatLineDelivered(ChatChannelKind kind, string prefix, string body)
        {
            if (this == null)
            {
                ChatNetworkRouter.OnAnyChatLineDelivered -= HandleRoutedChatLineDelivered;
                return;
            }

            NotifyIncomingMessage(kind, prefix, body);
        }

        private void HandleMatchStateChanged(MatchState state)
        {
            if (state == MatchState.Ended && _isChatFocused)
            {
                SetChatFocus(false);
            }
        }

        private void ApplyVisualState(ChatVisualState state)
        {
            _visualState = state;
            if (_canvasGroup == null)
            {
                return;
            }

            bool interactive = state == ChatVisualState.Focused;
            _canvasGroup.interactable = interactive;
            _canvasGroup.blocksRaycasts = interactive;
            _canvasGroup.alpha = state switch
            {
                ChatVisualState.Focused => FocusedAlpha,
                ChatVisualState.IncomingWake => IncomingWakeAlpha,
                ChatVisualState.Minimized => MinimizedAlpha,
                _ => PassiveAlpha
            };
        }

        private void PollKeyboardShortcuts()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (_isChatFocused && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleEscapePressed();
                return;
            }

            if (_isChatFocused && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                HandleTabQuickReply();
                return;
            }

            if (_inputPoller == null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                HandleEnterPressed(Time.unscaledTime);
            }
        }

        private void BindUiButtons()
        {
            if (_chatToggleButton != null)
            {
                _chatToggleButton.onClick.RemoveAllListeners();
                _chatToggleButton.onClick.AddListener(ToggleChatFocusFromButton);
            }

            if (_sendButton != null)
            {
                _sendButton.onClick.RemoveAllListeners();
                _sendButton.onClick.AddListener(SubmitChatFromButton);
            }
        }

        private void ToggleChatFocusFromButton()
        {
            SetChatFocus(!_isChatFocused);
        }

        private void SubmitChatFromButton()
        {
            HandleEnterPressed(Time.unscaledTime);
        }

        private void UpdateHintLabel(bool focused)
        {
            if (_channelHintText == null)
            {
                return;
            }

            _channelHintText.text = focused
                ? "ALL / /w <Name> <Msg>   |   [Enter] Send   [Esc] Close"
                : "[Enter] Open Chat   (/w <Name> to Whisper)";
        }

        private void EnsureHierarchyBuilt()
        {
            EnsurePanelRectAndCanvasGroup();
            if (_ringBuffer == null)
            {
                BuildDefaultChatPanelHierarchy();
            }

            _ringBuffer.EnsurePrewarmed(ChatMessageRingBuffer.CanonicalCapacity);
            if (_networkRouter == null && TryGetComponent<Fusion.NetworkObject>(out _))
            {
                TryGetComponent(out _networkRouter);
            }
        }

        private void EnsurePanelRectAndCanvasGroup()
        {
            if (!TryGetComponent(out RectTransform selfRt))
            {
                selfRt = gameObject.AddComponent<RectTransform>();
            }

            if (selfRt.sizeDelta.sqrMagnitude < 10f)
            {
                selfRt.anchorMin = Vector2.zero;
                selfRt.anchorMax = Vector2.zero;
                selfRt.pivot = Vector2.zero;
                selfRt.anchoredPosition = new Vector2(18f, 18f);
                selfRt.sizeDelta = new Vector2(PanelWidthPx, PanelHeightPx);
            }

            if (_canvasGroup == null && !TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void BuildDefaultChatPanelHierarchy()
        {
            if (!TryGetComponent(out Image panelBg))
            {
                panelBg = gameObject.AddComponent<Image>();
            }

            panelBg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            panelBg.color = new Color32(15, 23, 42, 222);

            BuildChatHeaderBar();
            BuildChatScrollViewport();
            BuildChatInputBar();
            UpdateHintLabel(focused: false);
        }

        private void BuildChatHeaderBar()
        {
            GameObject headerGo = CreateAnchoredRect("ChatHeaderBar", transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 28f));
            Image hBg = headerGo.AddComponent<Image>();
            hBg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            hBg.color = new Color32(30, 41, 59, 240);

            GameObject hintGo = CreateStretchRect("Txt_ChannelHint", headerGo.transform, new Vector2(10f, 2f), new Vector2(-72f, -2f));
            _channelHintText = hintGo.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(_channelHintText);
            _channelHintText.fontSize = 12f;
            _channelHintText.fontStyle = FontStyles.Bold;
            _channelHintText.color = UiThemeAndFontProvider.ElectricCyan;
            _channelHintText.alignment = TextAlignmentOptions.MidlineLeft;
            _channelHintText.raycastTarget = false;

            GameObject toggleGo = CreateAnchoredRect("BTN_ChatToggle", headerGo.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(64f, -4f));
            Image tImg = toggleGo.AddComponent<Image>();
            tImg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            tImg.color = new Color32(51, 65, 85, 255);
            _chatToggleButton = toggleGo.AddComponent<Button>();
            _chatToggleButton.targetGraphic = tImg;
        }

        private void BuildChatScrollViewport()
        {
            GameObject scrollGo = CreateStretchRect("ChatScrollRect", transform, new Vector2(6f, 44f), new Vector2(-6f, -32f));
            ScrollRect scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            GameObject viewportGo = CreateStretchRect("Viewport", scrollGo.transform, Vector2.zero, Vector2.zero);
            viewportGo.AddComponent<RectMask2D>();
            scrollRect.viewport = viewportGo.GetComponent<RectTransform>();

            GameObject contentGo = CreateAnchoredRect("ChatContentRingBuffer", viewportGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            ContentSizeFitter csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = contentGo.GetComponent<RectTransform>();

            _ringBuffer = contentGo.AddComponent<ChatMessageRingBuffer>();
            _ringBuffer.BindScrollRect(scrollRect);
        }

        private void BuildChatInputBar()
        {
            GameObject inputBarGo = CreateAnchoredRect("ChatInputBar", transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(-12f, 34f));
            BuildChatInputField(inputBarGo.transform);
            BuildChatSendButton(inputBarGo.transform);
        }

        private void BuildChatInputField(Transform parent)
        {
            GameObject fieldGo = CreateStretchRect("InputField_Chat", parent, Vector2.zero, new Vector2(-74f, 0f));
            Image fBg = fieldGo.AddComponent<Image>();
            fBg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            fBg.color = new Color32(30, 41, 59, 255);

            GameObject textAreaGo = CreateStretchRect("Text Area", fieldGo.transform, new Vector2(8f, 3f), new Vector2(-8f, -3f));
            textAreaGo.AddComponent<RectMask2D>();
            RectTransform tart = textAreaGo.GetComponent<RectTransform>();

            TextMeshProUGUI phTmp = CreateInputLabel("Placeholder", tart, "Type message or /w <Name> <Msg>...", 13f, FontStyles.Italic, UiThemeAndFontProvider.MutedSteel);
            TextMeshProUGUI itTmp = CreateInputLabel("Text", tart, string.Empty, 13.5f, FontStyles.Normal, UiThemeAndFontProvider.PureFrost);

            _chatInputField = fieldGo.AddComponent<TMP_InputField>();
            _chatInputField.textViewport = tart;
            _chatInputField.textComponent = itTmp;
            _chatInputField.placeholder = phTmp;
            _chatInputField.characterLimit = 160;
            _chatInputField.richText = false;
        }

        private void BuildChatSendButton(Transform parent)
        {
            GameObject sendGo = CreateAnchoredRect("BTN_SendChat", parent, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(68f, 0f));
            Image sendImg = sendGo.AddComponent<Image>();
            sendImg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            sendImg.color = new Color32(0, 180, 216, 255);
            _sendButton = sendGo.AddComponent<Button>();
            _sendButton.targetGraphic = sendImg;

            TextMeshProUGUI sendTmp = CreateInputLabel("Txt_Send", sendGo.transform, "SEND", 12.5f, FontStyles.Bold, UiThemeAndFontProvider.PureFrost);
            sendTmp.alignment = TextAlignmentOptions.Center;
            sendTmp.raycastTarget = false;
        }

        private static GameObject CreateAnchoredRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return go;
        }

        private static GameObject CreateStretchRect(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        private static TextMeshProUGUI CreateInputLabel(
            string name,
            Transform parent,
            string initialText,
            float fontSize,
            FontStyles style,
            Color color)
        {
            GameObject go = CreateStretchRect(name, parent, Vector2.zero, Vector2.zero);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = initialText;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.richText = false;
            return tmp;
        }
    }
}
