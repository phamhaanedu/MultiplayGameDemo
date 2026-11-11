using System;
using System.Collections.Generic;
using System.Text;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Player;
using CapsuleArena.UI.Common;
using CapsuleArena.UI.HUD;
using CapsuleArena.UI.Lobby;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CapsuleArena.UI.Settings
{
    /// <summary>
    /// Interactive Settings &amp; Controls modal listing all rebindable <see cref="GameInputAction"/> rows,
    /// <c>MouseSensitivity [0.1, 5.0]</c> slider, and the 3-way <see cref="MobilePadVisibilityMode"/>
    /// (<c>Auto / ForceOn / ForceOff</c>) segmented toggle, while holding <see cref="InputFocusFlag.RebindModalOpen"/>
    /// and strictly disposing <see cref="InputActionRebindingExtensions.RebindingOperation"/> handles
    /// (Stories 11-2, 11-3; ADR-0002; TR-rebind-002, TR-rebind-003).
    /// </summary>
    public sealed class KeybindingRebindModal : MonoBehaviour
    {
        /// <summary>Timeout in seconds before an interactive key-listen session cancels automatically (5.0s).</summary>
        public const float RebindListenTimeoutSec = 5.0f;

        private static readonly GameInputAction[] OrderedActions =
            (GameInputAction[])Enum.GetValues(typeof(GameInputAction));

        [Header("Modal Containers")]
        [SerializeField] private CanvasGroup _modalCanvasGroup;
        [SerializeField] private RectTransform _rowsContainer;
        [SerializeField] private GameObject _listeningOverlay;
        [SerializeField] private TMP_Text _listeningPromptText;
        [SerializeField] private ToastNotificationView _toastView;

        [Header("Controls & Toggles")]
        [SerializeField] private Slider _sensitivitySlider;
        [SerializeField] private TMP_Text _sensitivityValueText;
        [SerializeField] private Button _padModeAutoButton;
        [SerializeField] private Button _padModeForceOnButton;
        [SerializeField] private Button _padModeForceOffButton;
        [SerializeField] private Button _resetDefaultsButton;
        [SerializeField] private Button _closeModalButton;

        private readonly Dictionary<GameInputAction, KeybindingRowView> _rowByAction =
            new Dictionary<GameInputAction, KeybindingRowView>(16);
        private readonly StringBuilder _listenBuilder = new StringBuilder(96);

        private IKeybindingService _keybindingService;
        private IInputFocusService _focusService;
        private MatchManager _matchManager;
        private PlayerHealth _localPlayerHealth;
        private InputAction _rebindProbeAction;
        private InputActionRebindingExtensions.RebindingOperation _activeRebindOp;
        private GameInputAction _listeningTargetAction;
        private bool _isOpen;
        private bool _isListening;
        private float _listenCountdownSec;
        private int _lastDisplayedListenSec = -1;

        /// <summary>Gets a value indicating whether the Settings &amp; Rebind modal is currently open.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>Gets a value indicating whether an interactive key-listen operation is currently active.</summary>
        public bool IsListeningForKey => _isListening;

        /// <summary>Gets the action currently waiting for a key press when <see cref="IsListeningForKey"/> is true.</summary>
        public GameInputAction ListeningTargetAction => _listeningTargetAction;

        /// <summary>Gets the read-only dictionary of instantiated action rows.</summary>
        public IReadOnlyDictionary<GameInputAction, KeybindingRowView> RowsByAction => _rowByAction;

        /// <summary>Gets the modal's <see cref="ToastNotificationView"/> for conflict swap and error banners.</summary>
        public ToastNotificationView ToastView => _toastView;

        /// <summary>Gets the mouse sensitivity slider ([0.1, 5.0]).</summary>
        public Slider SensitivitySlider => _sensitivitySlider;

        /// <summary>Gets the 'AUTO' mobile pad visibility button.</summary>
        public Button PadModeAutoButton => _padModeAutoButton;

        /// <summary>Gets the 'FORCE ON' mobile pad visibility button.</summary>
        public Button PadModeForceOnButton => _padModeForceOnButton;

        /// <summary>Gets the 'FORCE OFF' mobile pad visibility button.</summary>
        public Button PadModeForceOffButton => _padModeForceOffButton;

        /// <summary>Gets the underlying probe action while an interactive listen session is active (for unit inspection).</summary>
        public InputAction ActiveProbeAction => _rebindProbeAction;

        private void Awake()
        {
            EnsureVisualsBuilt();
            BindControlCallbacks();
            ApplyModalOpenVisualState(false);
        }

        private void Update()
        {
            TickListenCountdown(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            CancelActiveRebind();
            if (_isOpen)
            {
                _isOpen = false;
                _focusService?.SetFocusFlag(InputFocusFlag.RebindModalOpen, false);
            }
        }

        private void OnDestroy()
        {
            CancelActiveRebind();
            DisposeProbeAction();
            UnbindExternalEvents();
            UnbindLocalPlayerHealth();
        }

        /// <summary>Injects keybinding, input focus, and optional match-state services.</summary>
        public void Initialize(
            IKeybindingService keybindingService,
            IInputFocusService focusService,
            MatchManager matchManager = null)
        {
            UnbindExternalEvents();
            EnsureVisualsBuilt();
            BindControlCallbacks();

            _keybindingService = keybindingService;
            _focusService = focusService;
            _matchManager = matchManager;

            if (_keybindingService != null)
            {
                _keybindingService.OnBindingsChanged += RefreshAllControlsFromService;
            }

            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
            }

            RefreshAllControlsFromService();
        }

        /// <summary>Binds the local player's <see cref="PlayerHealth"/> so the modal closes automatically on elimination.</summary>
        public void BindLocalPlayerHealth(PlayerHealth localHealth)
        {
            UnbindLocalPlayerHealth();
            _localPlayerHealth = localHealth;
            if (_localPlayerHealth != null)
            {
                _localPlayerHealth.OnPlayerDied += HandleLocalPlayerDied;
            }
        }

        /// <summary>Opens the Settings &amp; Controls modal and sets <see cref="InputFocusFlag.RebindModalOpen"/>.</summary>
        public void OpenModal()
        {
            EnsureVisualsBuilt();
            _isOpen = true;
            _focusService?.SetFocusFlag(InputFocusFlag.RebindModalOpen, true);
            ApplyModalOpenVisualState(true);
            RefreshAllControlsFromService();
        }

        /// <summary>Cancels any active rebind, clears <see cref="InputFocusFlag.RebindModalOpen"/>, and hides the modal.</summary>
        public void CloseModal()
        {
            CancelActiveRebind();
            _isOpen = false;
            _focusService?.SetFocusFlag(InputFocusFlag.RebindModalOpen, false);
            ApplyModalOpenVisualState(false);
        }

        /// <summary>
        /// Starts an interactive keyboard + mouse button listen operation with a <c>5.0s</c> timeout and <c>Escape</c> cancel.
        /// </summary>
        public void StartInteractiveRebind(GameInputAction targetAction)
        {
            EnsureVisualsBuilt();
            CancelActiveRebind();

            _listeningTargetAction = targetAction;
            _isListening = true;
            _listenCountdownSec = RebindListenTimeoutSec;
            _lastDisplayedListenSec = -1;
            if (_listeningOverlay != null)
            {
                _listeningOverlay.SetActive(true);
            }

            UpdateListeningOverlayLabelIfChanged();
            StartInteractiveRebindOperation(targetAction);
        }

        /// <summary>Advances the 5.0s interactive listen countdown deterministically (callable in EditMode tests).</summary>
        public void TickListenCountdown(float unscaledDeltaTime)
        {
            if (!_isListening || unscaledDeltaTime <= 0f)
            {
                return;
            }

            _listenCountdownSec -= unscaledDeltaTime;
            if (_listenCountdownSec <= 0f)
            {
                CancelActiveRebind();
                _toastView?.ShowToast("Rebind timed out (5s). Binding unchanged.", isError: false, durationSec: 1.8f);
                return;
            }

            UpdateListeningOverlayLabelIfChanged();
        }

        /// <summary>
        /// Cancels and disposes any in-flight <see cref="InputActionRebindingExtensions.RebindingOperation"/> immediately.
        /// </summary>
        public void CancelActiveRebind()
        {
            if (_activeRebindOp != null)
            {
                var opToDispose = _activeRebindOp;
                _activeRebindOp = null;
                opToDispose.Cancel();
                opToDispose.Dispose();
            }

            DisposeProbeAction();
            FinishListeningOverlay();
        }

        /// <summary>
        /// Validates and applies <paramref name="candidateBindingPath"/> to <paramref name="targetAction"/>,
        /// swapping any conflicting action atomically and flashing both rows in <c>#FFB703</c>.
        /// </summary>
        public bool ApplyCandidateBindingPath(GameInputAction targetAction, string candidateBindingPath)
        {
            if (_keybindingService == null || string.IsNullOrWhiteSpace(candidateBindingPath))
            {
                return false;
            }

            string normalized = candidateBindingPath.Trim();
            if (IsEscapeCancelPath(normalized))
            {
                CancelActiveRebind();
                return false;
            }

            bool hasConflict = TryFindExistingOwner(normalized, targetAction, out GameInputAction swappedAction);
            string previousTargetBinding = _keybindingService.GetBindingPath(targetAction);
            if (!_keybindingService.TryRebindAction(targetAction, normalized))
            {
                _toastView?.ShowToast("Reserved system key cannot be bound.", isError: true);
                return false;
            }

            RefreshAllControlsFromService();
            if (hasConflict)
            {
                NotifySwapConflictHighlight(targetAction, swappedAction, normalized, previousTargetBinding);
            }

            return true;
        }

        /// <summary>Restores all bindings, sensitivity, and mobile pad preferences to canonical defaults.</summary>
        public void OnResetDefaultsClicked()
        {
            CancelActiveRebind();
            if (_keybindingService == null)
            {
                return;
            }

            foreach (KeyValuePair<GameInputAction, KeybindingRowView> kvp in _rowByAction)
            {
                kvp.Value?.ClearHighlight();
            }

            _keybindingService.ResetToDefaults();
            RefreshAllControlsFromService();
            _toastView?.ShowToast("Controls reset to canonical defaults.", isError: false);
        }

        /// <summary>Updates the 3-way <see cref="MobilePadVisibilityMode"/> (<c>Auto / ForceOn / ForceOff</c>) and saves immediately.</summary>
        public void SetMobilePadVisibilityMode(MobilePadVisibilityMode mode)
        {
            if (_keybindingService == null)
            {
                return;
            }

            _keybindingService.MobilePadMode = mode;
            RefreshSegmentHighlights(mode);
        }

        /// <summary>Updates mouse/touch look sensitivity (<c>[0.1, 5.0]</c>).</summary>
        public void SetMouseSensitivity(float value)
        {
            if (_keybindingService != null)
            {
                _keybindingService.MouseSensitivity = value;
            }

            float clamped = _keybindingService != null ? _keybindingService.MouseSensitivity : Mathf.Clamp(value, 0.1f, 5.0f);
            UpdateSensitivityReadout(clamped);
        }

        /// <summary>Formats a friendly display name for any <see cref="GameInputAction"/>.</summary>
        public static string FormatActionLabel(GameInputAction action)
        {
            return action switch
            {
                GameInputAction.MoveForward => "Move Forward",
                GameInputAction.MoveBackward => "Move Backward",
                GameInputAction.MoveLeft => "Strafe Left",
                GameInputAction.MoveRight => "Strafe Right",
                GameInputAction.Jump => "Jump",
                GameInputAction.DodgeLeft => "Dodge Roll Left",
                GameInputAction.DodgeRight => "Dodge Roll Right",
                GameInputAction.ForwardDash => "Forward Dash",
                GameInputAction.VerticalSlash => "Vertical Slash",
                GameInputAction.HorizontalSweepLeft => "Horizontal Sweep Left",
                GameInputAction.HorizontalSweepRight => "Horizontal Sweep Right",
                GameInputAction.RangedFire => "Ranged Bolt Fire",
                GameInputAction.ToggleCameraView => "Toggle FPS/TPS Camera",
                GameInputAction.ToggleChatFocus => "Toggle Chat Focus",
                _ => action.ToString()
            };
        }

        private void StartInteractiveRebindOperation(GameInputAction targetAction)
        {
            DisposeProbeAction();
            _rebindProbeAction = new InputAction(name: "RebindProbe", type: InputActionType.PassThrough);
            _rebindProbeAction.AddBinding("<Keyboard>/anyKey");
            _rebindProbeAction.AddBinding("<Mouse>/*Button");

            _activeRebindOp = _rebindProbeAction
                .PerformInteractiveRebinding()
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithTimeout(RebindListenTimeoutSec)
                .OnComplete(op => HandleRebindOperationComplete(op, targetAction))
                .OnCancel(HandleRebindOperationCancelled);
            _activeRebindOp.Start();
        }

        private void HandleRebindOperationComplete(
            InputActionRebindingExtensions.RebindingOperation op,
            GameInputAction targetAction)
        {
            string selectedPath = op.selectedControl != null
                ? ConvertControlToCanonicalBindingPath(op.selectedControl)
                : string.Empty;
            op.Dispose();
            _activeRebindOp = null;
            DisposeProbeAction();
            FinishListeningOverlay();

            if (!string.IsNullOrEmpty(selectedPath))
            {
                ApplyCandidateBindingPath(targetAction, selectedPath);
            }
        }

        private void HandleRebindOperationCancelled(InputActionRebindingExtensions.RebindingOperation op)
        {
            op.Dispose();
            _activeRebindOp = null;
            DisposeProbeAction();
            FinishListeningOverlay();
            _toastView?.ShowToast("Rebind cancelled.", isError: false, durationSec: 1.5f);
        }

        private static bool IsEscapeCancelPath(string normalized)
        {
            return string.Equals(normalized, "<Keyboard>/escape", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "<Keyboard>/esc", StringComparison.OrdinalIgnoreCase);
        }

        private void NotifySwapConflictHighlight(
            GameInputAction targetAction,
            GameInputAction swappedAction,
            string newPath,
            string prevPath)
        {
            if (_rowByAction.TryGetValue(targetAction, out KeybindingRowView rowA))
            {
                rowA.TriggerSwapHighlight();
            }

            if (_rowByAction.TryGetValue(swappedAction, out KeybindingRowView rowB))
            {
                rowB.TriggerSwapHighlight();
            }

            string newBadge = MobileActionButton.FormatHumanBadge(newPath);
            string prevBadge = MobileActionButton.FormatHumanBadge(prevPath);
            _toastView?.ShowToast(
                $"Swapped: {FormatActionLabel(targetAction)} -> {newBadge}, {FormatActionLabel(swappedAction)} -> {prevBadge}",
                isError: false);
        }

        private bool TryFindExistingOwner(string candidatePath, GameInputAction excludingAction, out GameInputAction owner)
        {
            for (int i = 0; i < OrderedActions.Length; i++)
            {
                GameInputAction act = OrderedActions[i];
                if (act != excludingAction &&
                    string.Equals(_keybindingService.GetBindingPath(act), candidatePath, StringComparison.OrdinalIgnoreCase))
                {
                    owner = act;
                    return true;
                }
            }

            owner = default;
            return false;
        }

        private static string ConvertControlToCanonicalBindingPath(InputControl control)
        {
            if (control == null || control.device == null)
            {
                return string.Empty;
            }

            string layout = control.device is Keyboard
                ? "Keyboard"
                : (control.device is Mouse ? "Mouse" : control.device.name);
            return $"<{layout}>/{control.name}";
        }

        private void RefreshAllControlsFromService()
        {
            EnsureVisualsBuilt();
            for (int i = 0; i < OrderedActions.Length; i++)
            {
                GameInputAction act = OrderedActions[i];
                string path = _keybindingService != null
                    ? _keybindingService.GetBindingPath(act)
                    : KeybindingManager.GetDefaultBindingPath(act);

                if (_rowByAction.TryGetValue(act, out KeybindingRowView row) && row != null)
                {
                    row.RefreshBindingBadge(path);
                }
            }

            float sens = _keybindingService != null ? _keybindingService.MouseSensitivity : 1.0f;
            _sensitivitySlider?.SetValueWithoutNotify(sens);
            UpdateSensitivityReadout(sens);

            MobilePadVisibilityMode padMode = _keybindingService != null
                ? _keybindingService.MobilePadMode
                : MobilePadVisibilityMode.Auto;
            RefreshSegmentHighlights(padMode);
        }

        private void RefreshSegmentHighlights(MobilePadVisibilityMode activeMode)
        {
            SetSegmentButtonColor(_padModeAutoButton, activeMode == MobilePadVisibilityMode.Auto);
            SetSegmentButtonColor(_padModeForceOnButton, activeMode == MobilePadVisibilityMode.ForceOn);
            SetSegmentButtonColor(_padModeForceOffButton, activeMode == MobilePadVisibilityMode.ForceOff);
        }

        private static void SetSegmentButtonColor(Button btn, bool selected)
        {
            if (btn != null && btn.targetGraphic != null)
            {
                btn.targetGraphic.color = selected ? new Color32(0, 180, 216, 255) : UiThemeAndFontProvider.CardSurface;
            }
        }

        private void UpdateSensitivityReadout(float sens)
        {
            if (_sensitivityValueText != null)
            {
                _sensitivityValueText.text = $"{sens:0.0}x";
            }
        }

        private void UpdateListeningOverlayLabelIfChanged()
        {
            if (_listeningPromptText == null)
            {
                return;
            }

            int ceilSec = Mathf.Max(1, Mathf.CeilToInt(_listenCountdownSec));
            if (ceilSec == _lastDisplayedListenSec)
            {
                return;
            }

            _lastDisplayedListenSec = ceilSec;
            _listenBuilder.Clear();
            _listenBuilder.Append("Press any key or mouse button for [");
            _listenBuilder.Append(FormatActionLabel(_listeningTargetAction));
            _listenBuilder.Append("]... (");
            _listenBuilder.Append(ceilSec);
            _listenBuilder.Append("s)\n[ESC] Cancel");
            _listeningPromptText.SetText(_listenBuilder);
        }

        private void FinishListeningOverlay()
        {
            _isListening = false;
            _listenCountdownSec = 0f;
            _lastDisplayedListenSec = -1;
            if (_listeningOverlay != null)
            {
                _listeningOverlay.SetActive(false);
            }
        }

        private void DisposeProbeAction()
        {
            if (_rebindProbeAction != null)
            {
                _rebindProbeAction.Disable();
                _rebindProbeAction.Dispose();
                _rebindProbeAction = null;
            }
        }

        private void HandleMatchStateChanged(MatchState state)
        {
            if (state == MatchState.Ended && _isOpen)
            {
                CloseModal();
            }
        }

        private void HandleLocalPlayerDied(PlayerRef victim, PlayerRef killer)
        {
            if (_isOpen)
            {
                CloseModal();
            }
        }

        private void ApplyModalOpenVisualState(bool open)
        {
            if (_modalCanvasGroup == null)
            {
                return;
            }

            _modalCanvasGroup.alpha = open ? 1.0f : 0.0f;
            _modalCanvasGroup.interactable = open;
            _modalCanvasGroup.blocksRaycasts = open;
        }

        private void BindControlCallbacks()
        {
            if (_sensitivitySlider != null)
            {
                _sensitivitySlider.minValue = KeybindingManager.MinSensitivity;
                _sensitivitySlider.maxValue = KeybindingManager.MaxSensitivity;
                _sensitivitySlider.onValueChanged.RemoveListener(SetMouseSensitivity);
                _sensitivitySlider.onValueChanged.AddListener(SetMouseSensitivity);
            }

            BindPadModeButtons();
            if (_resetDefaultsButton != null)
            {
                _resetDefaultsButton.onClick.RemoveListener(OnResetDefaultsClicked);
                _resetDefaultsButton.onClick.AddListener(OnResetDefaultsClicked);
            }

            if (_closeModalButton != null)
            {
                _closeModalButton.onClick.RemoveListener(CloseModal);
                _closeModalButton.onClick.AddListener(CloseModal);
            }
        }

        private void BindPadModeButtons()
        {
            if (_padModeAutoButton != null)
            {
                _padModeAutoButton.onClick.RemoveAllListeners();
                _padModeAutoButton.onClick.AddListener(() => SetMobilePadVisibilityMode(MobilePadVisibilityMode.Auto));
            }

            if (_padModeForceOnButton != null)
            {
                _padModeForceOnButton.onClick.RemoveAllListeners();
                _padModeForceOnButton.onClick.AddListener(() => SetMobilePadVisibilityMode(MobilePadVisibilityMode.ForceOn));
            }

            if (_padModeForceOffButton != null)
            {
                _padModeForceOffButton.onClick.RemoveAllListeners();
                _padModeForceOffButton.onClick.AddListener(() => SetMobilePadVisibilityMode(MobilePadVisibilityMode.ForceOff));
            }
        }

        private void UnbindExternalEvents()
        {
            if (_keybindingService != null)
            {
                _keybindingService.OnBindingsChanged -= RefreshAllControlsFromService;
            }

            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
            }
        }

        private void UnbindLocalPlayerHealth()
        {
            if (_localPlayerHealth != null)
            {
                _localPlayerHealth.OnPlayerDied -= HandleLocalPlayerDied;
                _localPlayerHealth = null;
            }
        }

        private void EnsureVisualsBuilt()
        {
            if (!TryGetComponent(out RectTransform selfRt))
            {
                selfRt = gameObject.AddComponent<RectTransform>();
            }

            selfRt.anchorMin = Vector2.zero;
            selfRt.anchorMax = Vector2.one;
            selfRt.offsetMin = Vector2.zero;
            selfRt.offsetMax = Vector2.zero;

            if (_modalCanvasGroup == null && !TryGetComponent(out _modalCanvasGroup))
            {
                _modalCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (_rowsContainer == null)
            {
                BuildDefaultModalHierarchy();
            }

            if (_rowByAction.Count == 0 && _rowsContainer != null)
            {
                PopulateActionRows();
            }

            EnsureToastBuilt();
        }

        private void EnsureToastBuilt()
        {
            if (_toastView != null)
            {
                return;
            }

            GameObject toastGo = new GameObject("ToastNotificationView", typeof(RectTransform));
            toastGo.transform.SetParent(transform, worldPositionStays: false);
            _toastView = toastGo.AddComponent<ToastNotificationView>();
        }

        private void BuildDefaultModalHierarchy()
        {
            if (!TryGetComponent(out Image scrim))
            {
                scrim = gameObject.AddComponent<Image>();
            }

            scrim.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            scrim.color = new Color32(2, 6, 23, 215);

            GameObject card = CreateBox(transform, "ModalCard", Vector2.zero, new Vector2(860f, 620f), UiThemeAndFontProvider.DeepSlate);
            BuildHeaderAndTopControls(card.transform);
            BuildRowsAndFooter(card.transform);
        }

        private void BuildHeaderAndTopControls(Transform cardTransform)
        {
            CreateText(cardTransform, "Txt_ModalHeader", "SETTINGS & KEYBINDINGS", 24f, new Vector2(-120f, 278f), new Vector2(520f, 36f), UiThemeAndFontProvider.ElectricCyan, TextAlignmentOptions.MidlineLeft);
            _closeModalButton = CreateActionButton(cardTransform, "BTN_CloseModal", "CLOSE [X]", new Vector2(350f, 278f), new Vector2(124f, 36f), new Color32(71, 85, 105, 255));

            CreateText(cardTransform, "Txt_SensLabel", "Mouse / Look Sensitivity:", 15f, new Vector2(-280f, 232f), new Vector2(240f, 30f), UiThemeAndFontProvider.PureFrost, TextAlignmentOptions.MidlineLeft);
            _sensitivitySlider = CreateHorizontalSlider(cardTransform, "Slider_Sensitivity", new Vector2(-30f, 232f), new Vector2(240f, 22f));
            _sensitivityValueText = CreateText(cardTransform, "Txt_SensValue", "1.0x", 15f, new Vector2(130f, 232f), new Vector2(64f, 30f), UiThemeAndFontProvider.SolarAmber, TextAlignmentOptions.Center);

            CreateText(cardTransform, "Txt_MobilePadLabel", "Mobile Action Pad:", 15f, new Vector2(-280f, 192f), new Vector2(240f, 30f), UiThemeAndFontProvider.PureFrost, TextAlignmentOptions.MidlineLeft);
            _padModeAutoButton = CreateActionButton(cardTransform, "BTN_PadMode_Auto", "AUTO", new Vector2(-95f, 192f), new Vector2(105f, 32f), UiThemeAndFontProvider.CardSurface);
            _padModeForceOnButton = CreateActionButton(cardTransform, "BTN_PadMode_ForceOn", "FORCE ON", new Vector2(20f, 192f), new Vector2(115f, 32f), UiThemeAndFontProvider.CardSurface);
            _padModeForceOffButton = CreateActionButton(cardTransform, "BTN_PadMode_ForceOff", "FORCE OFF", new Vector2(140f, 192f), new Vector2(115f, 32f), UiThemeAndFontProvider.CardSurface);
        }

        private void BuildRowsAndFooter(Transform cardTransform)
        {
            GameObject rowsGo = new GameObject("RowsContainer", typeof(RectTransform));
            rowsGo.transform.SetParent(cardTransform, worldPositionStays: false);
            _rowsContainer = rowsGo.GetComponent<RectTransform>();
            _rowsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            _rowsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _rowsContainer.anchoredPosition = new Vector2(0f, -24f);
            _rowsContainer.sizeDelta = new Vector2(820f, 360f);

            _resetDefaultsButton = CreateActionButton(cardTransform, "BTN_ResetDefaults", "RESET TO DEFAULTS", new Vector2(0f, -268f), new Vector2(240f, 40f), new Color32(180, 83, 9, 255));

            _listeningOverlay = CreateBox(cardTransform, "ListeningOverlay", Vector2.zero, new Vector2(860f, 620f), new Color32(2, 6, 23, 252));
            GameObject innerPromptCard = CreateBox(_listeningOverlay.transform, "ListeningPromptCard", Vector2.zero, new Vector2(540f, 150f), UiThemeAndFontProvider.CardSurface);
            _listeningPromptText = CreateText(innerPromptCard.transform, "Txt_ListeningPrompt", "Press any key or mouse button... (5s)", 19f, Vector2.zero, new Vector2(500f, 116f), UiThemeAndFontProvider.SolarAmber, TextAlignmentOptions.Center);
            _listeningOverlay.SetActive(false);
        }

        private void PopulateActionRows()
        {
            _rowByAction.Clear();
            for (int i = 0; i < OrderedActions.Length; i++)
            {
                GameInputAction act = OrderedActions[i];
                int col = i / 7;
                int rowIdx = i % 7;

                GameObject rowGo = new GameObject($"Row_{act}", typeof(RectTransform));
                rowGo.transform.SetParent(_rowsContainer, worldPositionStays: false);
                RectTransform rrt = rowGo.GetComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0.5f, 0.5f);
                rrt.anchorMax = new Vector2(0.5f, 0.5f);
                rrt.anchoredPosition = new Vector2(col == 0 ? -206f : 206f, 144f - (rowIdx * 46f));
                rrt.sizeDelta = new Vector2(396f, 38f);

                KeybindingRowView rowView = rowGo.AddComponent<KeybindingRowView>();
                rowView.Configure(act, FormatActionLabel(act), KeybindingManager.GetDefaultBindingPath(act), StartInteractiveRebind);
                _rowByAction[act] = rowView;
            }
        }

        private static GameObject CreateBox(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            img.color = color;
            return go;
        }

        private static TMP_Text CreateText(Transform parent, string name, string text, float fontSize, Vector2 pos, Vector2 size, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateActionButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color bgColor)
        {
            GameObject go = CreateBox(parent, name, pos, size, bgColor);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            CreateText(go.transform, "Txt_Label", label, 13f, Vector2.zero, size - new Vector2(8f, 4f), UiThemeAndFontProvider.PureFrost, TextAlignmentOptions.Center);
            return btn;
        }

        private static Slider CreateHorizontalSlider(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject root = CreateBox(parent, name, pos, size, UiThemeAndFontProvider.CardSurface);
            RectTransform fillRt = CreateSliderFillRect(root.transform);
            GameObject handle = CreateBox(root.transform, "Handle", Vector2.zero, new Vector2(14f, size.y + 6f), UiThemeAndFontProvider.PureFrost);

            Slider slider = root.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = KeybindingManager.MinSensitivity;
            slider.maxValue = KeybindingManager.MaxSensitivity;
            slider.value = 1.0f;
            return slider;
        }

        private static RectTransform CreateSliderFillRect(Transform rootTransform)
        {
            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(rootTransform, worldPositionStays: false);
            RectTransform faRt = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero;
            faRt.anchorMax = Vector2.one;
            faRt.offsetMin = new Vector2(6f, 4f);
            faRt.offsetMax = new Vector2(-6f, -4f);

            GameObject fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(fillArea.transform, worldPositionStays: false);
            RectTransform fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            Image fillImg = fill.AddComponent<Image>();
            fillImg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            fillImg.color = UiThemeAndFontProvider.ElectricCyan;
            return fillRt;
        }
    }
}
