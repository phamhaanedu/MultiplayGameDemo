using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Networking;
using CapsuleArena.UI.Common;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.Lobby
{
    /// <summary>
    /// Controls the Main Lobby entry screen (<c>DisplayName</c>, <c>RoomCode</c>, Quick Join, Create/Join,
    /// Settings, Leaderboard), enforces immediate button locks during Photon Shared Mode handshakes,
    /// renders the 4-slot Room Waiting roster with <c>[HOST]</c> badge and countdown banner, and recovers
    /// gracefully on validation or 10-second timeout errors via <see cref="ToastNotificationView"/>
    /// (Story 11-1; ADR-0002; TR-rebind-001).
    /// </summary>
    public sealed class LobbyViewController : MonoBehaviour
    {
        /// <summary>Regex enforcing 3..16 alphanumeric or underscore DisplayName characters.</summary>
        public const string DisplayNameRegexPattern = "^[a-zA-Z0-9_]{3,16}$";

        /// <summary>Regex enforcing 3..16 alphanumeric, underscore, or hyphen RoomCode characters.</summary>
        public const string RoomCodeRegexPattern = "^[A-Za-z0-9_-]{3,16}$";

        /// <summary>Persistent field label displayed directly above the DisplayName input box.</summary>
        public const string DisplayNameFieldLabelText = "PLAYER NAME (3-16 chars, blank = auto)";

        /// <summary>Persistent field label displayed directly above the RoomCode input box.</summary>
        public const string RoomCodeFieldLabelText = "ROOM CODE (Used for Create / Join Room)";

        private static readonly Regex DisplayNameRegex = new Regex(DisplayNameRegexPattern, RegexOptions.Compiled);
        private static readonly Regex RoomCodeRegex = new Regex(RoomCodeRegexPattern, RegexOptions.Compiled);

        private static readonly Color32[] SlotSwatchPalette =
        {
            new Color32(0, 245, 212, 255),
            new Color32(255, 159, 28, 255),
            new Color32(255, 77, 109, 255),
            new Color32(155, 93, 229, 255)
        };

        [Header("Panels & Overlays")]
        [SerializeField] private CanvasGroup _rootCanvasGroup;
        [SerializeField] private GameObject _mainLobbyPanel;
        [SerializeField] private GameObject _waitingRoomPanel;
        [SerializeField] private GameObject _connectingOverlay;
        [SerializeField] private ToastNotificationView _toastView;

        [Header("Main Lobby Controls")]
        [SerializeField] private TMP_Text _displayNameLabel;
        [SerializeField] private TMP_InputField _displayNameInput;
        [SerializeField] private TMP_Text _roomCodeLabel;
        [SerializeField] private TMP_InputField _roomCodeInput;
        [SerializeField] private Button _quickJoinButton;
        [SerializeField] private Button _createOrJoinButton;
        [SerializeField] private Button _openLeaderboardButton;
        [SerializeField] private Button _openSettingsButton;

        [Header("Waiting Room Controls")]
        [SerializeField] private TMP_Text _roomHeaderText;
        [SerializeField] private TMP_Text _waitingBannerText;
        [SerializeField] private Button _leaveRoomButton;
        [SerializeField] private Image[] _slotSwatchImages;
        [SerializeField] private TMP_Text[] _slotPlayerNameTexts;
        [SerializeField] private GameObject[] _slotHostBadges;

        private readonly string[] _waitingSlotNamesBuffer = new string[4];
        private INetworkSessionService _sessionService;
        private MatchManager _matchManager;
        private Action _openSettingsCallback;
        private Action _openLeaderboardCallback;
        private bool _isHandshakeInProgress;
        private int _lastReportedConnectedCount = 1;
        private int _lastDisplayedCountdownSec = -1;
        private string _validatedLocalDisplayName = string.Empty;
        private string _activeRoomLabel = "ARENA";

        /// <summary>Gets the validated local player display name most recently accepted.</summary>
        public string LocalDisplayName => _validatedLocalDisplayName;

        /// <summary>Gets a value indicating whether a connection handshake is currently in flight.</summary>
        public bool IsHandshakeInProgress => _isHandshakeInProgress;

        /// <summary>Gets a value indicating whether the Main Lobby panel is active.</summary>
        public bool IsMainLobbyVisible => _mainLobbyPanel != null && _mainLobbyPanel.activeSelf;

        /// <summary>Gets a value indicating whether the 4-slot Room Waiting panel is active.</summary>
        public bool IsWaitingRoomVisible => _waitingRoomPanel != null && _waitingRoomPanel.activeSelf;

        /// <summary>Gets a value indicating whether primary lobby action buttons are currently interactable.</summary>
        public bool AreActionButtonsInteractable =>
            _quickJoinButton != null && _quickJoinButton.interactable &&
            _createOrJoinButton != null && _createOrJoinButton.interactable;

        /// <summary>Gets the attached <see cref="ToastNotificationView"/> instance.</summary>
        public ToastNotificationView ToastView => _toastView;

        /// <summary>Gets the persistent label displayed above the DisplayName input field.</summary>
        public TMP_Text DisplayNameLabel => _displayNameLabel;

        /// <summary>Gets the DisplayName input field.</summary>
        public TMP_InputField DisplayNameInput => _displayNameInput;

        /// <summary>Gets the persistent label displayed above the RoomCode input field.</summary>
        public TMP_Text RoomCodeLabel => _roomCodeLabel;

        /// <summary>Gets the RoomCode input field.</summary>
        public TMP_InputField RoomCodeInput => _roomCodeInput;

        /// <summary>Gets the waiting room status/countdown banner label.</summary>
        public TMP_Text WaitingBannerText => _waitingBannerText;

        /// <summary>Gets the 4 waiting room slot player name labels.</summary>
        public IReadOnlyList<TMP_Text> SlotPlayerNameTexts => _slotPlayerNameTexts;

        /// <summary>Gets the 4 waiting room slot [HOST] badge GameObjects.</summary>
        public IReadOnlyList<GameObject> SlotHostBadges => _slotHostBadges;

        /// <summary>Gets the Leave Room button.</summary>
        public Button LeaveRoomButton => _leaveRoomButton;

        private void Awake()
        {
            EnsureVisualsBuilt();
            BindUiCallbacks();
            ShowMainLobby();
        }

        private void Update()
        {
            if (!IsWaitingRoomVisible || _matchManager == null || _matchManager.CurrentMatchState != MatchState.Countdown)
            {
                return;
            }

            int ceilSec = Mathf.Max(1, Mathf.CeilToInt(_matchManager.RemainingPhaseSeconds));
            if (ceilSec != _lastDisplayedCountdownSec)
            {
                _lastDisplayedCountdownSec = ceilSec;
                UpdateWaitingBanner(Mathf.Max(2, _lastReportedConnectedCount), ceilSec);
            }
        }

        private void OnDestroy()
        {
            UnbindDependencies();
        }

        /// <summary>
        /// Injects session, match, settings, and leaderboard collaborators for runtime or automated testing.
        /// </summary>
        public void Initialize(
            INetworkSessionService sessionService,
            MatchManager matchManager = null,
            Action openSettingsCallback = null,
            Action openLeaderboardCallback = null)
        {
            UnbindDependencies();
            EnsureVisualsBuilt();

            _sessionService = sessionService;
            _matchManager = matchManager;
            _openSettingsCallback = openSettingsCallback;
            _openLeaderboardCallback = openLeaderboardCallback;

            if (_sessionService != null)
            {
                _sessionService.OnSessionDisconnected += HandleSessionDisconnected;
                _sessionService.OnLocalPlayerSpawned += NotifyLocalPlayerSpawned;
            }

            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
                _matchManager.OnRosterCountsChanged += HandleRosterCountsChanged;
            }
        }

        /// <summary>Syncs the local validated DisplayName into <see cref="MatchManager"/> once the local player spawns.</summary>
        public void NotifyLocalPlayerSpawned(PlayerRef localPlayer)
        {
            if (_matchManager != null && localPlayer != PlayerRef.None && !string.IsNullOrEmpty(_validatedLocalDisplayName))
            {
                _matchManager.SubmitPlayerDisplayName(localPlayer, _validatedLocalDisplayName);
            }

            RefreshWaitingRoomFromMatchRoster();
        }

        /// <summary>
        /// Validates or auto-generates a player DisplayName.
        /// Blank/whitespace input generates <c>"Player_XXXX"</c> (<c>1000..9999</c>).
        /// Non-blank input must match <see cref="DisplayNameRegexPattern"/>; otherwise returns <c>string.Empty</c>.
        /// </summary>
        public static string ValidateOrGenerateDisplayName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                int suffix = UnityEngine.Random.Range(1000, 10000);
                return $"Player_{suffix}";
            }

            string trimmed = rawName.Trim();
            return DisplayNameRegex.IsMatch(trimmed) ? trimmed : string.Empty;
        }

        /// <summary>Returns true if <paramref name="displayName"/> matches <c>^[a-zA-Z0-9_]{3,16}$</c>.</summary>
        public static bool IsValidDisplayName(string displayName)
        {
            return !string.IsNullOrWhiteSpace(displayName) && DisplayNameRegex.IsMatch(displayName.Trim());
        }

        /// <summary>Returns true if <paramref name="roomCode"/> matches <c>^[A-Za-z0-9_-]{3,16}$</c>.</summary>
        public static bool IsValidRoomCode(string roomCode)
        {
            return !string.IsNullOrWhiteSpace(roomCode) && RoomCodeRegex.IsMatch(roomCode.Trim());
        }

        /// <summary>Initiates Quick Join (empty room code -> random open Shared Mode room).</summary>
        public void OnQuickJoinClicked()
        {
            _ = ExecuteJoinRequestAsync(isQuickJoin: true);
        }

        /// <summary>Initiates Create-or-Join using the explicit Room Code entered by the player.</summary>
        public void OnCreateOrJoinRoomClicked()
        {
            _ = ExecuteJoinRequestAsync(isQuickJoin: false);
        }

        /// <summary>Async entry point used by UI buttons and unit tests to await handshake completion deterministically.</summary>
        public async Task<bool> ExecuteJoinRequestAsync(bool isQuickJoin)
        {
            EnsureVisualsBuilt();
            if (_isHandshakeInProgress || !TryValidateLobbyInputs(isQuickJoin, out string resolvedName, out string roomCode))
            {
                return false;
            }

            _validatedLocalDisplayName = resolvedName;
            _activeRoomLabel = string.IsNullOrEmpty(roomCode) ? (isQuickJoin ? "QUICK-MATCH" : "SHARED-ARENA") : roomCode;
            BeginHandshakeLock();

            if (_sessionService == null)
            {
                EndHandshakeLock();
                ShowWaitingRoom(_activeRoomLabel, new[] { _validatedLocalDisplayName }, hostIndex: 0, countdownRemainingSec: 0);
                return true;
            }

            bool connected = await TryConnectSharedSessionAsync(roomCode);
            EndHandshakeLock();
            if (!connected)
            {
                return false;
            }

            if (_sessionService is FusionSessionManager fsm && fsm.ActiveRunner != null)
            {
                if (string.IsNullOrEmpty(roomCode) && fsm.ActiveRunner.SessionInfo.IsValid)
                {
                    string joinedName = fsm.ActiveRunner.SessionInfo.Name;
                    if (!string.IsNullOrWhiteSpace(joinedName) && joinedName.Length <= 16)
                    {
                        _activeRoomLabel = joinedName;
                    }
                }

                NotifyLocalPlayerSpawned(fsm.ActiveRunner.LocalPlayer);
            }

            if (_matchManager != null && _matchManager.CurrentMatchState == MatchState.Playing)
            {
                if (_mainLobbyPanel != null) _mainLobbyPanel.SetActive(false);
                if (_waitingRoomPanel != null) _waitingRoomPanel.SetActive(false);
                if (_connectingOverlay != null) _connectingOverlay.SetActive(false);
                ShowRootCanvas(false);
                return true;
            }

            ShowWaitingRoom(_activeRoomLabel, new[] { _validatedLocalDisplayName }, hostIndex: 0, countdownRemainingSec: 0);
            RefreshWaitingRoomFromMatchRoster();
            return true;
        }

        /// <summary>Leaves the current waiting room session and returns to the Main Lobby.</summary>
        public async Task LeaveWaitingRoomAsync()
        {
            if (_sessionService != null && _sessionService.IsSessionActive)
            {
                await _sessionService.ShutdownSessionAsync("LeaveWaitingRoom");
            }

            ShowMainLobby();
        }

        /// <summary>Renders the 4-slot Room Waiting roster, host badge, and status/countdown banner.</summary>
        public void ShowWaitingRoom(
            string roomLabel,
            string[] playerNames,
            int hostIndex = 0,
            int countdownRemainingSec = 0)
        {
            EnsureVisualsBuilt();
            ShowRootCanvas(true);
            SetWaitingRoomPanelsActive();
            _activeRoomLabel = string.IsNullOrWhiteSpace(roomLabel) ? "ARENA" : roomLabel;

            int occupiedCount = 0;
            for (int i = 0; i < 4; i++)
            {
                string slotName = playerNames != null && i < playerNames.Length ? playerNames[i] : string.Empty;
                if (ApplyWaitingSlotVisuals(i, slotName, hostIndex))
                {
                    occupiedCount++;
                }
            }

            if (_roomHeaderText != null)
            {
                _roomHeaderText.text = $"ROOM: {_activeRoomLabel} ({occupiedCount}/4)";
            }

            UpdateWaitingBanner(occupiedCount, countdownRemainingSec);
        }

        /// <summary>Updates the waiting room status banner for player count or match countdown.</summary>
        public void UpdateWaitingBanner(int connectedCount, int countdownRemainingSec)
        {
            if (_waitingBannerText == null)
            {
                return;
            }

            int clampedPlayers = Mathf.Clamp(connectedCount, 0, 4);
            if (countdownRemainingSec > 0)
            {
                _waitingBannerText.text = $"Match starting in {countdownRemainingSec}...";
                _waitingBannerText.color = UiThemeAndFontProvider.ElectricCyan;
            }
            else
            {
                _waitingBannerText.text = $"Waiting for players ({clampedPlayers}/4)...";
                _waitingBannerText.color = UiThemeAndFontProvider.SolarAmber;
            }
        }

        /// <summary>Returns the UI to the Main Lobby screen and unlocks all controls.</summary>
        public void ShowMainLobby()
        {
            EnsureVisualsBuilt();
            _isHandshakeInProgress = false;
            ShowRootCanvas(true);

            if (_mainLobbyPanel != null) _mainLobbyPanel.SetActive(true);
            if (_waitingRoomPanel != null) _waitingRoomPanel.SetActive(false);
            if (_connectingOverlay != null) _connectingOverlay.SetActive(false);
            SetLobbyButtonsInteractable(true);
        }

        /// <summary>Shows or hides the entire Lobby canvas overlay via <see cref="CanvasGroup"/>.</summary>
        public void ShowRootCanvas(bool visible)
        {
            if (_rootCanvasGroup == null)
            {
                return;
            }

            _rootCanvasGroup.alpha = visible ? 1.0f : 0.0f;
            _rootCanvasGroup.interactable = visible;
            _rootCanvasGroup.blocksRaycasts = visible;
            if (!visible && UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private bool TryValidateLobbyInputs(bool isQuickJoin, out string resolvedName, out string roomCode)
        {
            roomCode = string.Empty;
            string rawName = _displayNameInput != null ? _displayNameInput.text : string.Empty;
            resolvedName = ValidateOrGenerateDisplayName(rawName);
            if (string.IsNullOrEmpty(resolvedName))
            {
                _toastView?.ShowToast("Invalid Display Name! Use 3-16 letters, numbers, or underscores.", isError: true);
                return false;
            }

            if (_displayNameInput != null)
            {
                _displayNameInput.SetTextWithoutNotify(resolvedName);
            }

            if (isQuickJoin)
            {
                return true;
            }

            string rawCode = _roomCodeInput != null ? _roomCodeInput.text : string.Empty;
            string upperCode = string.IsNullOrWhiteSpace(rawCode) ? string.Empty : rawCode.Trim().ToUpperInvariant();
            if (!IsValidRoomCode(upperCode))
            {
                _toastView?.ShowToast("Invalid Room Code! Enter 3-16 alphanumeric characters.", isError: true);
                return false;
            }

            roomCode = upperCode;
            _roomCodeInput?.SetTextWithoutNotify(roomCode);
            return true;
        }

        private async Task<bool> TryConnectSharedSessionAsync(string roomCode)
        {
            try
            {
                bool ok = await _sessionService.StartSharedSessionAsync(roomCode);
                if (!ok)
                {
                    HandleConnectionFailure("Unable to join room (timeout or room full). Please retry.");
                }

                return ok;
            }
            catch (Exception ex)
            {
                HandleConnectionFailure($"Connection error: {ex.Message}");
                return false;
            }
        }

        private void BeginHandshakeLock()
        {
            _isHandshakeInProgress = true;
            SetLobbyButtonsInteractable(false);
            if (_connectingOverlay != null)
            {
                _connectingOverlay.SetActive(true);
            }
        }

        private void EndHandshakeLock()
        {
            _isHandshakeInProgress = false;
            if (_connectingOverlay != null)
            {
                _connectingOverlay.SetActive(false);
            }
        }

        private void SetWaitingRoomPanelsActive()
        {
            if (_mainLobbyPanel != null) _mainLobbyPanel.SetActive(false);
            if (_waitingRoomPanel != null) _waitingRoomPanel.SetActive(true);
            if (_connectingOverlay != null) _connectingOverlay.SetActive(false);
        }

        private bool ApplyWaitingSlotVisuals(int index, string slotName, int hostIndex)
        {
            bool hasPlayer = !string.IsNullOrWhiteSpace(slotName);
            if (_slotPlayerNameTexts != null && index < _slotPlayerNameTexts.Length && _slotPlayerNameTexts[index] != null)
            {
                _slotPlayerNameTexts[index].text = hasPlayer ? slotName : "Open Slot...";
                _slotPlayerNameTexts[index].color = hasPlayer ? UiThemeAndFontProvider.PureFrost : UiThemeAndFontProvider.MutedSteel;
            }

            if (_slotSwatchImages != null && index < _slotSwatchImages.Length && _slotSwatchImages[index] != null)
            {
                Color32 c = SlotSwatchPalette[index % SlotSwatchPalette.Length];
                _slotSwatchImages[index].color = hasPlayer ? (Color)c : new Color32(51, 65, 85, 160);
            }

            if (_slotHostBadges != null && index < _slotHostBadges.Length && _slotHostBadges[index] != null)
            {
                _slotHostBadges[index].SetActive(hasPlayer && index == hostIndex);
            }

            return hasPlayer;
        }

        private void RefreshWaitingRoomFromMatchRoster()
        {
            if (_matchManager == null)
            {
                return;
            }

            if (_matchManager.CurrentMatchState == MatchState.Playing)
            {
                if (_mainLobbyPanel != null) _mainLobbyPanel.SetActive(false);
                if (_waitingRoomPanel != null) _waitingRoomPanel.SetActive(false);
                ShowRootCanvas(false);
                return;
            }

            if (!IsWaitingRoomVisible)
            {
                return;
            }

            int count = _matchManager.CopyOrderedRosterSlotNames(_waitingSlotNamesBuffer, out int hostIndex);
            if (count <= 0)
            {
                return;
            }

            int countdown = _matchManager.CurrentMatchState == MatchState.Countdown
                ? Mathf.Max(1, Mathf.CeilToInt(_matchManager.RemainingPhaseSeconds))
                : 0;
            ShowWaitingRoom(_activeRoomLabel, _waitingSlotNamesBuffer, hostIndex, countdown);
        }

        private void HandleSessionDisconnected(string reason)
        {
            if (string.Equals(reason, "LeaveWaitingRoom", StringComparison.Ordinal) ||
                string.Equals(reason, "UserLeft", StringComparison.Ordinal))
            {
                _isHandshakeInProgress = false;
                ShowMainLobby();
                return;
            }

            HandleConnectionFailure($"Disconnected ({reason}). Returned to Lobby.");
        }

        private void HandleConnectionFailure(string userMessage)
        {
            _isHandshakeInProgress = false;
            ShowMainLobby();
            _toastView?.ShowToast(userMessage, isError: true);
        }

        private void HandleMatchStateChanged(MatchState state)
        {
            if (state == MatchState.Playing)
            {
                if (_mainLobbyPanel != null) _mainLobbyPanel.SetActive(false);
                if (_waitingRoomPanel != null) _waitingRoomPanel.SetActive(false);
                if (_connectingOverlay != null) _connectingOverlay.SetActive(false);
                ShowRootCanvas(false);
            }
            else if (state == MatchState.WaitingForPlayers && IsWaitingRoomVisible)
            {
                _lastDisplayedCountdownSec = -1;
                RefreshWaitingRoomFromMatchRoster();
                UpdateWaitingBanner(_lastReportedConnectedCount, 0);
            }
        }

        private void HandleRosterCountsChanged(int aliveCount, int totalConnected)
        {
            _lastReportedConnectedCount = Mathf.Max(1, totalConnected);
            RefreshWaitingRoomFromMatchRoster();
        }

        private void OnLeaveWaitingRoomClicked()
        {
            _ = LeaveWaitingRoomAsync();
        }

        private void HandleRoomCodeValueChanged(string rawValue)
        {
            if (_roomCodeInput == null || string.IsNullOrEmpty(rawValue))
            {
                return;
            }

            string upper = rawValue.ToUpperInvariant();
            if (!string.Equals(rawValue, upper, StringComparison.Ordinal))
            {
                _roomCodeInput.SetTextWithoutNotify(upper);
            }
        }

        private void SetLobbyButtonsInteractable(bool interactable)
        {
            if (_quickJoinButton != null) _quickJoinButton.interactable = interactable;
            if (_createOrJoinButton != null) _createOrJoinButton.interactable = interactable;
            if (_displayNameInput != null) _displayNameInput.interactable = interactable;
            if (_roomCodeInput != null) _roomCodeInput.interactable = interactable;
        }

        private void BindUiCallbacks()
        {
            BindPrimaryLobbyButtons();
            if (_leaveRoomButton != null)
            {
                _leaveRoomButton.onClick.RemoveListener(OnLeaveWaitingRoomClicked);
                _leaveRoomButton.onClick.AddListener(OnLeaveWaitingRoomClicked);
            }

            if (_roomCodeInput != null)
            {
                _roomCodeInput.characterLimit = 16;
                _roomCodeInput.onValueChanged.RemoveListener(HandleRoomCodeValueChanged);
                _roomCodeInput.onValueChanged.AddListener(HandleRoomCodeValueChanged);
            }

            if (_displayNameInput != null)
            {
                _displayNameInput.characterLimit = 16;
            }
        }

        private void BindPrimaryLobbyButtons()
        {
            if (_quickJoinButton != null)
            {
                _quickJoinButton.onClick.RemoveListener(OnQuickJoinClicked);
                _quickJoinButton.onClick.AddListener(OnQuickJoinClicked);
            }

            if (_createOrJoinButton != null)
            {
                _createOrJoinButton.onClick.RemoveListener(OnCreateOrJoinRoomClicked);
                _createOrJoinButton.onClick.AddListener(OnCreateOrJoinRoomClicked);
            }

            if (_openSettingsButton != null)
            {
                _openSettingsButton.onClick.RemoveAllListeners();
                _openSettingsButton.onClick.AddListener(() => _openSettingsCallback?.Invoke());
            }

            if (_openLeaderboardButton != null)
            {
                _openLeaderboardButton.onClick.RemoveAllListeners();
                _openLeaderboardButton.onClick.AddListener(() => _openLeaderboardCallback?.Invoke());
            }
        }

        private void UnbindDependencies()
        {
            if (_sessionService != null)
            {
                _sessionService.OnSessionDisconnected -= HandleSessionDisconnected;
                _sessionService.OnLocalPlayerSpawned -= NotifyLocalPlayerSpawned;
            }

            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
                _matchManager.OnRosterCountsChanged -= HandleRosterCountsChanged;
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

            if (_rootCanvasGroup == null && !TryGetComponent(out _rootCanvasGroup))
            {
                _rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (_mainLobbyPanel == null)
            {
                BuildDefaultLobbyHierarchy();
            }
            else
            {
                EnsureMainLobbyLabelsAndLayout();
            }

            if (_toastView == null)
            {
                GameObject toastGo = new GameObject("ToastNotificationView", typeof(RectTransform));
                toastGo.transform.SetParent(transform, worldPositionStays: false);
                _toastView = toastGo.AddComponent<ToastNotificationView>();
            }
        }

        private void BuildDefaultLobbyHierarchy()
        {
            BuildMainLobbyCard();
            BuildConnectingOverlay();
            BuildWaitingRoomCard();
        }

        private void BuildMainLobbyCard()
        {
            _mainLobbyPanel = CreatePanel("MainLobbyPanel", new Vector2(540f, 490f), UiThemeAndFontProvider.DeepSlate);
            CreateLabel(_mainLobbyPanel.transform, "Txt_Title", "CAPSULE ARENA DUEL", 28f, new Vector2(0f, 196f), UiThemeAndFontProvider.ElectricCyan);
            CreateLabel(_mainLobbyPanel.transform, "Txt_Subtitle", "4-Player 60Hz Shared Mode Arena", 15f, new Vector2(0f, 162f), UiThemeAndFontProvider.MutedSteel);

            _displayNameLabel = CreateFieldHeaderLabel(_mainLobbyPanel.transform, "Lbl_DisplayName", DisplayNameFieldLabelText, new Vector2(0f, 118f), UiThemeAndFontProvider.ElectricCyan);
            _displayNameInput = CreateInputField(_mainLobbyPanel.transform, "InputField_DisplayName", "e.g. Anph21 (leave blank for Player_XXXX)", new Vector2(0f, 82f));

            _roomCodeLabel = CreateFieldHeaderLabel(_mainLobbyPanel.transform, "Lbl_RoomCode", RoomCodeFieldLabelText, new Vector2(0f, 28f), UiThemeAndFontProvider.SolarAmber);
            _roomCodeInput = CreateInputField(_mainLobbyPanel.transform, "InputField_RoomCode", "e.g. ROOM1 (ignored by Quick Join)", new Vector2(0f, -8f));

            _quickJoinButton = CreateButton(_mainLobbyPanel.transform, "BTN_QuickJoin", "QUICK JOIN", new Vector2(-120f, -82f), new Vector2(215f, 48f), new Color32(0, 180, 216, 255));
            _createOrJoinButton = CreateButton(_mainLobbyPanel.transform, "BTN_CreateOrJoinRoom", "CREATE / JOIN ROOM", new Vector2(120f, -82f), new Vector2(215f, 48f), new Color32(255, 159, 28, 255));
            _openLeaderboardButton = CreateButton(_mainLobbyPanel.transform, "BTN_OpenLeaderboard", "LEADERBOARD", new Vector2(-120f, -148f), new Vector2(215f, 42f), UiThemeAndFontProvider.CardSurface);
            _openSettingsButton = CreateButton(_mainLobbyPanel.transform, "BTN_OpenSettings", "SETTINGS & KEYS", new Vector2(120f, -148f), new Vector2(215f, 42f), UiThemeAndFontProvider.CardSurface);
        }

        /// <summary>
        /// Ensures persistent field labels exist and aligns card spacing even when <c>MainLobbyPanel</c>
        /// was serialized prior to adding the input labels.
        /// </summary>
        public void EnsureMainLobbyLabelsAndLayout()
        {
            if (_mainLobbyPanel == null)
            {
                return;
            }

            Transform panelTr = _mainLobbyPanel.transform;
            if (_mainLobbyPanel.TryGetComponent(out RectTransform panelRt))
            {
                panelRt.sizeDelta = new Vector2(Mathf.Max(540f, panelRt.sizeDelta.x), 490f);
            }

            SetChildAnchoredY(panelTr, "Txt_Title", 196f);
            SetChildAnchoredY(panelTr, "Txt_Subtitle", 162f);

            if (_displayNameLabel == null)
            {
                Transform existingNameLbl = panelTr.Find("Lbl_DisplayName");
                _displayNameLabel = existingNameLbl != null && existingNameLbl.TryGetComponent(out TMP_Text dl)
                    ? dl
                    : CreateFieldHeaderLabel(panelTr, "Lbl_DisplayName", DisplayNameFieldLabelText, new Vector2(0f, 118f), UiThemeAndFontProvider.ElectricCyan);
            }

            _displayNameLabel.text = DisplayNameFieldLabelText;
            _displayNameLabel.rectTransform.anchoredPosition = new Vector2(0f, 118f);

            if (_displayNameInput != null)
            {
                ((RectTransform)_displayNameInput.transform).anchoredPosition = new Vector2(0f, 82f);
                if (_displayNameInput.placeholder is TMP_Text namePh)
                {
                    namePh.text = "e.g. Anph21 (leave blank for Player_XXXX)";
                }
            }

            if (_roomCodeLabel == null)
            {
                Transform existingCodeLbl = panelTr.Find("Lbl_RoomCode");
                _roomCodeLabel = existingCodeLbl != null && existingCodeLbl.TryGetComponent(out TMP_Text rl)
                    ? rl
                    : CreateFieldHeaderLabel(panelTr, "Lbl_RoomCode", RoomCodeFieldLabelText, new Vector2(0f, 28f), UiThemeAndFontProvider.SolarAmber);
            }

            _roomCodeLabel.text = RoomCodeFieldLabelText;
            _roomCodeLabel.rectTransform.anchoredPosition = new Vector2(0f, 28f);

            if (_roomCodeInput != null)
            {
                ((RectTransform)_roomCodeInput.transform).anchoredPosition = new Vector2(0f, -8f);
                if (_roomCodeInput.placeholder is TMP_Text codePh)
                {
                    codePh.text = "e.g. ROOM1 (ignored by Quick Join)";
                }
            }

            if (_quickJoinButton != null) ((RectTransform)_quickJoinButton.transform).anchoredPosition = new Vector2(-120f, -82f);
            if (_createOrJoinButton != null) ((RectTransform)_createOrJoinButton.transform).anchoredPosition = new Vector2(120f, -82f);
            if (_openLeaderboardButton != null) ((RectTransform)_openLeaderboardButton.transform).anchoredPosition = new Vector2(-120f, -148f);
            if (_openSettingsButton != null) ((RectTransform)_openSettingsButton.transform).anchoredPosition = new Vector2(120f, -148f);
        }

        private static void SetChildAnchoredY(Transform parent, string childName, float y)
        {
            Transform child = parent.Find(childName);
            if (child != null && child.TryGetComponent(out RectTransform rt))
            {
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
            }
        }

        private static TMP_Text CreateFieldHeaderLabel(Transform parent, string goName, string content, Vector2 anchoredPos, Color color)
        {
            TMP_Text tmp = CreateLabel(parent, goName, content, 13.5f, anchoredPos, color);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.rectTransform.sizeDelta = new Vector2(450f, 22f);
            return tmp;
        }

        private void BuildConnectingOverlay()
        {
            _connectingOverlay = CreatePanel("ConnectingOverlay", new Vector2(420f, 96f), new Color32(2, 6, 23, 240));
            CreateLabel(_connectingOverlay.transform, "Txt_Connecting", "Connecting to Photon Shared Room...", 17f, Vector2.zero, UiThemeAndFontProvider.SolarAmber);
            _connectingOverlay.SetActive(false);
        }

        private void BuildWaitingRoomCard()
        {
            _waitingRoomPanel = CreatePanel("WaitingRoomPanel", new Vector2(560f, 430f), UiThemeAndFontProvider.DeepSlate);
            _roomHeaderText = CreateLabel(_waitingRoomPanel.transform, "Txt_RoomHeader", "ROOM: ARENA (1/4)", 24f, new Vector2(0f, 170f), UiThemeAndFontProvider.PureFrost);
            _waitingBannerText = CreateLabel(_waitingRoomPanel.transform, "Txt_WaitingBanner", "Waiting for players (1/4)...", 17f, new Vector2(0f, 132f), UiThemeAndFontProvider.SolarAmber);

            _slotSwatchImages = new Image[4];
            _slotPlayerNameTexts = new TMP_Text[4];
            _slotHostBadges = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                BuildWaitingRoomSlotRow(i);
            }

            _leaveRoomButton = CreateButton(_waitingRoomPanel.transform, "BTN_LeaveRoom", "LEAVE ROOM", new Vector2(0f, -162f), new Vector2(200f, 42f), new Color32(190, 18, 60, 255));
            _waitingRoomPanel.SetActive(false);
        }

        private void BuildWaitingRoomSlotRow(int i)
        {
            float yPos = 72f - (i * 52f);
            GameObject row = CreatePanelChild(_waitingRoomPanel.transform, $"Slot_{i + 1}", new Vector2(0f, yPos), new Vector2(480f, 42f), UiThemeAndFontProvider.CardSurface);

            GameObject swatchGo = new GameObject("Img_Swatch", typeof(RectTransform));
            swatchGo.transform.SetParent(row.transform, worldPositionStays: false);
            RectTransform swRt = swatchGo.GetComponent<RectTransform>();
            swRt.anchorMin = new Vector2(0f, 0.5f);
            swRt.anchorMax = new Vector2(0f, 0.5f);
            swRt.anchoredPosition = new Vector2(24f, 0f);
            swRt.sizeDelta = new Vector2(22f, 22f);
            Image swImg = swatchGo.AddComponent<Image>();
            swImg.sprite = UiThemeAndFontProvider.GetCircleSprite();
            swImg.color = SlotSwatchPalette[i];
            _slotSwatchImages[i] = swImg;

            TMP_Text nameTxt = CreateLabel(row.transform, "Txt_SlotPlayer", "Open Slot...", 16f, new Vector2(-20f, 0f), UiThemeAndFontProvider.MutedSteel);
            nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
            nameTxt.rectTransform.sizeDelta = new Vector2(300f, 34f);
            _slotPlayerNameTexts[i] = nameTxt;

            GameObject hostBadge = CreatePanelChild(row.transform, "Badge_Host", new Vector2(185f, 0f), new Vector2(78f, 26f), new Color32(255, 183, 3, 255));
            TMP_Text hostTxt = CreateLabel(hostBadge.transform, "Txt_Host", "[HOST]", 13f, Vector2.zero, new Color32(15, 23, 42, 255));
            hostTxt.rectTransform.sizeDelta = new Vector2(74f, 24f);
            _slotHostBadges[i] = hostBadge;
        }

        private GameObject CreatePanel(string panelName, Vector2 size, Color bgColor)
        {
            return CreatePanelChild(transform, panelName, Vector2.zero, size, bgColor);
        }

        private static GameObject CreatePanelChild(Transform parent, string panelName, Vector2 anchoredPos, Vector2 size, Color bgColor)
        {
            GameObject go = new GameObject(panelName, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Image bg = go.AddComponent<Image>();
            bg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            bg.color = bgColor;
            return go;
        }

        private static TMP_Text CreateLabel(Transform parent, string goName, string content, float fontSize, Vector2 anchoredPos, Color color)
        {
            GameObject go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(460f, 36f);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static TMP_InputField CreateInputField(Transform parent, string goName, string placeholder, Vector2 anchoredPos)
        {
            GameObject root = CreatePanelChild(parent, goName, anchoredPos, new Vector2(450f, 44f), UiThemeAndFontProvider.CardSurface);
            GameObject textArea = new GameObject("TextArea", typeof(RectTransform));
            textArea.transform.SetParent(root.transform, worldPositionStays: false);
            RectTransform areaRt = textArea.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(14f, 6f);
            areaRt.offsetMax = new Vector2(-14f, -6f);

            TextMeshProUGUI phTmp = CreateStretchInputText(textArea.transform, "Placeholder", placeholder, 15f, FontStyles.Italic, UiThemeAndFontProvider.MutedSteel);
            TextMeshProUGUI txtTmp = CreateStretchInputText(textArea.transform, "Text", string.Empty, 16f, FontStyles.Normal, UiThemeAndFontProvider.PureFrost);

            TMP_InputField input = root.AddComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            return input;
        }

        private static TextMeshProUGUI CreateStretchInputText(Transform parent, string name, string content, float fontSize, FontStyles style, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = color;
            return tmp;
        }

        private static Button CreateButton(Transform parent, string goName, string label, Vector2 anchoredPos, Vector2 size, Color bgColor)
        {
            GameObject root = CreatePanelChild(parent, goName, anchoredPos, size, bgColor);
            Button btn = root.AddComponent<Button>();
            btn.targetGraphic = root.GetComponent<Image>();

            TMP_Text txt = CreateLabel(root.transform, "Txt_ButtonLabel", label, 15f, Vector2.zero, UiThemeAndFontProvider.PureFrost);
            txt.rectTransform.sizeDelta = size - new Vector2(12f, 6f);
            return btn;
        }
    }
}
