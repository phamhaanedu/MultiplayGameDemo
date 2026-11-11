using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CapsuleArena.Cloud;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CapsuleArena.UI.Leaderboard
{
    /// <summary>
    /// Cached Top-20 PlayFab Leaderboard Modal View featuring <c>TotalWins</c> / <c>TotalKills</c> tabs,
    /// <c>20</c> pre-warmed <see cref="LeaderboardRowView"/> rows (<c>0</c> runtime <c>Instantiate</c>/<c>Destroy</c>),
    /// <c>5.0s</c> manual refresh cooldown, sticky local personal stats footer, offline/empty state messaging,
    /// and <see cref="InputFocusFlag.LeaderboardOpen"/> gameplay input suppression (Story 13-3; ADR-0008; TR-playfab-003).
    /// </summary>
    public sealed class LeaderboardModalView : MonoBehaviour
    {
        /// <summary>Fixed pre-warmed row pool capacity (20).</summary>
        public const int PrewarmedRowCount = 20;

        /// <summary>Minimum cooldown between manual Refresh button clicks in seconds (5.0s).</summary>
        public const float ManualRefreshCooldownSec = 5.0f;

        [Header("Dependencies")]
        [SerializeField] private PlayFabAuthService _authService;
        [SerializeField] private PlayFabLeaderboardService _leaderboardService;
        [SerializeField] private MatchManager _matchManager;

        [Header("UI Controls")]
        [SerializeField] private CanvasGroup _modalGroup;
        [SerializeField] private RectTransform _rowsContainer;
        [SerializeField] private Button _winsTabButton;
        [SerializeField] private Button _killsTabButton;
        [SerializeField] private Button _refreshButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TMP_Text _statusBannerText;
        [SerializeField] private TMP_Text _personalFooterText;
        [SerializeField] private LeaderboardRowView[] _pooledRows;

        private readonly StringBuilder _sb = new StringBuilder(128);
        private IInputFocusService _focusService;
        private string _activeStatTab = PlayFabLeaderboardService.StatTotalWins;
        private float _lastManualRefreshTimeSec = -999f;
        private bool _isOpen;

        /// <summary>Gets a value indicating whether the Leaderboard Modal is currently open.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>Gets the active statistic tab name ("TotalWins" or "TotalKills").</summary>
        public string ActiveStatTab => _activeStatTab;

        /// <summary>Gets the pre-warmed 20-item row array for test inspection.</summary>
        public IReadOnlyList<LeaderboardRowView> PooledRows
        {
            get
            {
                EnsureVisualsBuilt();
                return _pooledRows;
            }
        }

        /// <summary>Gets the current status/empty/offline banner message.</summary>
        public string StatusBannerMessage => _statusBannerText != null ? _statusBannerText.text : string.Empty;

        /// <summary>Gets the sticky local personal stats footer text.</summary>
        public string PersonalFooterMessage => _personalFooterText != null ? _personalFooterText.text : string.Empty;

        private void Awake()
        {
            EnsureVisualsBuilt();
            ApplyModalVisibility(false);
        }

        private void OnEnable()
        {
            BindMatchStateEvents();
        }

        private void OnDisable()
        {
            UnbindMatchStateEvents();
            if (_isOpen)
            {
                CloseModal();
            }
        }

        private void Update()
        {
            if (_isOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleEscapePressed();
            }
        }

        /// <summary>Injects cloud, focus, and optional match services explicitly for scene bootstrapping and EditMode tests.</summary>
        public void Initialize(
            PlayFabAuthService authService,
            PlayFabLeaderboardService leaderboardService,
            IInputFocusService focusService,
            MatchManager matchManager = null)
        {
            UnbindMatchStateEvents();
            _authService = authService;
            _leaderboardService = leaderboardService;
            _focusService = focusService;
            _matchManager = matchManager;
            BindMatchStateEvents();
            EnsureVisualsBuilt();
        }

        /// <summary>Closes the modal when <c>Escape</c> is pressed while open.</summary>
        public void HandleEscapePressed()
        {
            if (_isOpen)
            {
                CloseModal();
            }
        }

        /// <summary>Opens the Leaderboard Modal, sets <see cref="InputFocusFlag.LeaderboardOpen"/>, and loads the active tab.</summary>
        public async Task OpenModalAsync(float currentTimeSec = -1f)
        {
            EnsureVisualsBuilt();
            _isOpen = true;
            ApplyModalVisibility(true);
            _focusService?.SetFocusFlag(InputFocusFlag.LeaderboardOpen, true);
            RefreshPersonalStatsFooter();

            float now = ResolveNow(currentTimeSec);
            UpdateRefreshInteractability(now);
            await LoadTabInternalAsync(_activeStatTab, now, forceRefresh: false);
        }

        /// <summary>Synchronous wrapper opening the modal from UI button listeners.</summary>
        public void OpenModal()
        {
            _ = OpenModalAsync();
        }

        /// <summary>Closes the Leaderboard Modal via <see cref="CanvasGroup"/> and clears <see cref="InputFocusFlag.LeaderboardOpen"/>.</summary>
        public void CloseModal()
        {
            _isOpen = false;
            ApplyModalVisibility(false);
            _focusService?.SetFocusFlag(InputFocusFlag.LeaderboardOpen, false);
        }

        /// <summary>Switches between <c>TotalWins</c> and <c>TotalKills</c> tabs, respecting the 15.0s TTL cache.</summary>
        public async Task SelectTabAsync(string statName, float currentTimeSec = -1f)
        {
            EnsureVisualsBuilt();
            _activeStatTab = string.Equals(statName, PlayFabLeaderboardService.StatTotalKills)
                ? PlayFabLeaderboardService.StatTotalKills
                : PlayFabLeaderboardService.StatTotalWins;
            HighlightActiveTabButton();
            await LoadTabInternalAsync(_activeStatTab, ResolveNow(currentTimeSec), forceRefresh: false);
        }

        /// <summary>Attempts a manual refresh if at least <c>5.0s</c> have elapsed since the previous manual refresh.</summary>
        public async Task<bool> TryManualRefreshAsync(float currentTimeSec = -1f)
        {
            float now = ResolveNow(currentTimeSec);
            if (!CanManualRefresh(now))
            {
                UpdateRefreshInteractability(now);
                return false;
            }

            _lastManualRefreshTimeSec = now;
            UpdateRefreshInteractability(now);
            if (_leaderboardService != null)
            {
                _leaderboardService.InvalidateCache();
            }

            await LoadTabInternalAsync(_activeStatTab, now, forceRefresh: true);
            return true;
        }

        /// <summary>Returns true when the 5.0s manual refresh cooldown has expired.</summary>
        public bool CanManualRefresh(float currentTimeSec)
        {
            return (currentTimeSec - _lastManualRefreshTimeSec) >= ManualRefreshCooldownSec;
        }

        /// <summary>Populates the 20 pre-warmed rows directly from an entry list without network calls.</summary>
        public void RenderEntries(IReadOnlyList<LeaderboardEntryDto> entries, string localPlayFabId = null, string localDisplayName = null)
        {
            EnsureVisualsBuilt();
            int count = entries != null ? Mathf.Min(PrewarmedRowCount, entries.Count) : 0;

            for (int i = 0; i < PrewarmedRowCount; i++)
            {
                if (i < count)
                {
                    LeaderboardEntryDto dto = entries[i];
                    bool isLocal = IsEntryLocalPlayer(dto, localPlayFabId, localDisplayName);
                    _pooledRows[i].Populate(dto, i, isLocal, _sb);
                }
                else
                {
                    _pooledRows[i].HideRow();
                }
            }

            SetStatusBanner(count == 0 ? "No leaderboard entries recorded yet." : string.Empty);
            RefreshPersonalStatsFooter();
        }

        private async Task LoadTabInternalAsync(string statName, float nowSec, bool forceRefresh)
        {
            RefreshPersonalStatsFooter();
            if (_leaderboardService == null)
            {
                HideAllRows();
                SetStatusBanner("Leaderboard unavailable in Offline Guest Mode.");
                return;
            }

            PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>> resp =
                await _leaderboardService.FetchTop20Async(statName, nowSec, forceRefresh);

            if (!resp.IsSuccess)
            {
                HideAllRows();
                SetStatusBanner(resp.ErrorCode == "OfflineGuest"
                    ? "Offline Guest Mode — Connect PlayFab TitleId for Global Rankings."
                    : $"Leaderboard unavailable ({resp.ErrorCode}).");
                return;
            }

            string localId = _authService != null ? _authService.PlayFabId : string.Empty;
            string localName = _authService != null ? _authService.DisplayName : string.Empty;
            RenderEntries(resp.Data, localId, localName);
        }

        private static bool IsEntryLocalPlayer(LeaderboardEntryDto dto, string localPlayFabId, string localName)
        {
            if (!string.IsNullOrEmpty(localPlayFabId) && string.Equals(dto.PlayFabId, localPlayFabId))
            {
                return true;
            }

            return string.IsNullOrEmpty(localPlayFabId) &&
                   !string.IsNullOrEmpty(localName) &&
                   string.Equals(dto.DisplayName, localName);
        }

        private void RefreshPersonalStatsFooter()
        {
            if (_personalFooterText == null)
            {
                return;
            }

            PlayFabIdentitySaveData id = _authService != null ? _authService.IdentityData : null;
            string name = id != null && !string.IsNullOrEmpty(id.DisplayName) ? id.DisplayName : "Guest";
            int wins = id != null ? id.LocalTotalWins : 0;
            int kills = id != null ? id.LocalTotalKills : 0;
            int played = id != null ? id.LocalMatchesPlayed : 0;

            _sb.Clear();
            _sb.Append("YOUR RECORD (").Append(name).Append(")   |   Wins: ").Append(wins)
               .Append("   |   Kills: ").Append(kills)
               .Append("   |   Matches: ").Append(played);
            _personalFooterText.SetText(_sb);
        }

        private void HideAllRows()
        {
            if (_pooledRows == null)
            {
                return;
            }

            for (int i = 0; i < _pooledRows.Length; i++)
            {
                if (_pooledRows[i] != null)
                {
                    _pooledRows[i].HideRow();
                }
            }
        }

        private void SetStatusBanner(string message)
        {
            if (_statusBannerText != null)
            {
                _statusBannerText.text = message ?? string.Empty;
                _statusBannerText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }
        }

        private void UpdateRefreshInteractability(float nowSec)
        {
            if (_refreshButton != null)
            {
                _refreshButton.interactable = CanManualRefresh(nowSec);
            }
        }

        private void HighlightActiveTabButton()
        {
            bool winsActive = string.Equals(_activeStatTab, PlayFabLeaderboardService.StatTotalWins);
            if (_winsTabButton != null && _winsTabButton.targetGraphic != null)
            {
                _winsTabButton.targetGraphic.color = winsActive
                    ? UiThemeAndFontProvider.ElectricCyan
                    : UiThemeAndFontProvider.CardSurface;
            }

            if (_killsTabButton != null && _killsTabButton.targetGraphic != null)
            {
                _killsTabButton.targetGraphic.color = !winsActive
                    ? UiThemeAndFontProvider.ElectricCyan
                    : UiThemeAndFontProvider.CardSurface;
            }
        }

        private void ApplyModalVisibility(bool visible)
        {
            if (_modalGroup == null)
            {
                return;
            }

            _modalGroup.alpha = visible ? 1.0f : 0.0f;
            _modalGroup.interactable = visible;
            _modalGroup.blocksRaycasts = visible;
        }

        private static float ResolveNow(float explicitTimeSec)
        {
            return explicitTimeSec >= 0f ? explicitTimeSec : Time.unscaledTime;
        }

        private void HandleWinsTabClicked()
        {
            _ = SelectTabAsync(PlayFabLeaderboardService.StatTotalWins);
        }

        private void HandleKillsTabClicked()
        {
            _ = SelectTabAsync(PlayFabLeaderboardService.StatTotalKills);
        }

        private void HandleRefreshClicked()
        {
            _ = TryManualRefreshAsync();
        }

        private void BindMatchStateEvents()
        {
            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
                _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
            }
        }

        private void UnbindMatchStateEvents()
        {
            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
            }
        }

        private void HandleMatchStateChanged(MatchState nextState)
        {
            if (_isOpen && (nextState == MatchState.Countdown || nextState == MatchState.Playing))
            {
                CloseModal();
            }
        }

        private void EnsureVisualsBuilt()
        {
            if (_modalGroup != null && _pooledRows != null && _pooledRows.Length == PrewarmedRowCount)
            {
                WireButtons();
                return;
            }

            RectTransform root = EnsureFullScreenRoot();
            GameObject card = BuildBackdropAndCard(root);
            BuildHeaderAndTabs(card.transform);
            BuildScrollableRowPool(card.transform);
            BuildFooter(card.transform);
            WireButtons();
            HighlightActiveTabButton();
        }

        private RectTransform EnsureFullScreenRoot()
        {
            if (!TryGetComponent(out RectTransform root))
            {
                root = gameObject.AddComponent<RectTransform>();
            }

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            if (!TryGetComponent(out _modalGroup))
            {
                _modalGroup = gameObject.AddComponent<CanvasGroup>();
            }

            return root;
        }

        private static GameObject BuildBackdropAndCard(RectTransform root)
        {
            GameObject backdrop = CreatePanel("Backdrop", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UiThemeAndFontProvider.DeepSlate);
            backdrop.GetComponent<Image>().raycastTarget = true;
            return CreatePanel("LeaderboardCard", backdrop.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, 610f), UiThemeAndFontProvider.CardSurface);
        }

        private void BuildHeaderAndTabs(Transform card)
        {
            CreateText("Txt_Title", card, "TOP 20 ARENA LEADERBOARD", 24f, TextAlignmentOptions.MidlineLeft, new Vector2(0.03f, 0.90f), new Vector2(0.62f, 0.98f), UiThemeAndFontProvider.SolarAmber);
            _refreshButton = CreateButton(card, "BTN_Refresh", "REFRESH", new Vector2(210f, 272f), new Vector2(120f, 36f), UiThemeAndFontProvider.PanelDark, UiThemeAndFontProvider.PureFrost);
            _closeButton = CreateButton(card, "BTN_Close", "CLOSE [X]", new Vector2(330f, 272f), new Vector2(104f, 36f), UiThemeAndFontProvider.CrimsonDanger, Color.white);

            _winsTabButton = CreateButton(card, "Tab_TotalWins", "TOTAL WINS", new Vector2(-260f, 224f), new Vector2(220f, 38f), UiThemeAndFontProvider.ElectricCyan, new Color32(15, 23, 42, 255));
            _killsTabButton = CreateButton(card, "Tab_TotalKills", "TOTAL KILLS", new Vector2(-25f, 224f), new Vector2(220f, 38f), UiThemeAndFontProvider.PanelDark, UiThemeAndFontProvider.PureFrost);
            _statusBannerText = CreateText("Txt_StatusBanner", card, string.Empty, 16f, TextAlignmentOptions.Center, new Vector2(0.08f, 0.44f), new Vector2(0.92f, 0.56f), UiThemeAndFontProvider.MutedSteel);
            _statusBannerText.gameObject.SetActive(false);
        }

        private void BuildScrollableRowPool(Transform card)
        {
            GameObject viewport = CreatePanel("Viewport", card, new Vector2(0.03f, 0.11f), new Vector2(0.97f, 0.80f), Vector2.zero, Vector2.zero, UiThemeAndFontProvider.PanelDark);
            viewport.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            GameObject contentGo = new GameObject("RowsContainer", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport.transform, worldPositionStays: false);
            _rowsContainer = contentGo.GetComponent<RectTransform>();
            _rowsContainer.anchorMin = new Vector2(0f, 1f);
            _rowsContainer.anchorMax = new Vector2(1f, 1f);
            _rowsContainer.pivot = new Vector2(0.5f, 1f);
            _rowsContainer.anchoredPosition = Vector2.zero;
            _rowsContainer.sizeDelta = new Vector2(0f, PrewarmedRowCount * 32f);

            VerticalLayoutGroup vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _rowsContainer;
            scroll.viewport = viewport.GetComponent<RectTransform>();

            _pooledRows = new LeaderboardRowView[PrewarmedRowCount];
            for (int i = 0; i < PrewarmedRowCount; i++)
            {
                _pooledRows[i] = CreatePooledRow(_rowsContainer, i);
            }
        }

        private static LeaderboardRowView CreatePooledRow(Transform parent, int index)
        {
            GameObject rowGo = CreatePanel($"LeaderboardRow_{index:00}", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 28f), LeaderboardRowView.EvenRowBg);
            LayoutElement le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
            le.minHeight = 28f;

            GameObject accentGo = CreatePanel("AccentBar", rowGo.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3f, 0f), new Vector2(5f, 0f), UiThemeAndFontProvider.ElectricCyan);
            TMP_Text rankTxt = CreateText("Txt_Rank", rowGo.transform, "#1", 15f, TextAlignmentOptions.MidlineLeft, new Vector2(0.02f, 0f), new Vector2(0.14f, 1f), LeaderboardRowView.RankGoldColor);
            TMP_Text nameTxt = CreateText("Txt_Name", rowGo.transform, "Player", 15f, TextAlignmentOptions.MidlineLeft, new Vector2(0.15f, 0f), new Vector2(0.76f, 1f), UiThemeAndFontProvider.PureFrost);
            TMP_Text scoreTxt = CreateText("Txt_Score", rowGo.transform, "0", 16f, TextAlignmentOptions.MidlineRight, new Vector2(0.76f, 0f), new Vector2(0.98f, 1f), UiThemeAndFontProvider.PureFrost);

            LeaderboardRowView row = rowGo.AddComponent<LeaderboardRowView>();
            row.BindControls(rowGo.GetComponent<Image>(), accentGo.GetComponent<Image>(), rankTxt, nameTxt, scoreTxt);
            row.HideRow();
            return row;
        }

        private void BuildFooter(Transform card)
        {
            GameObject footer = CreatePanel("PersonalStatsFooter", card, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.09f), Vector2.zero, Vector2.zero, new Color32(10, 18, 34, 245));
            _personalFooterText = CreateText("Txt_PersonalRecord", footer.transform, "YOUR RECORD (Guest)   |   Wins: 0   |   Kills: 0   |   Matches: 0", 15f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, UiThemeAndFontProvider.ElectricCyan);
        }

        private void WireButtons()
        {
            if (_winsTabButton != null)
            {
                _winsTabButton.onClick.RemoveAllListeners();
                _winsTabButton.onClick.AddListener(HandleWinsTabClicked);
            }

            if (_killsTabButton != null)
            {
                _killsTabButton.onClick.RemoveAllListeners();
                _killsTabButton.onClick.AddListener(HandleKillsTabClicked);
            }

            if (_refreshButton != null)
            {
                _refreshButton.onClick.RemoveAllListeners();
                _refreshButton.onClick.AddListener(HandleRefreshClicked);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveAllListeners();
                _closeButton.onClick.AddListener(CloseModal);
            }
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 min, Vector2 max, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.GetComponent<Image>();
            img.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions align, Vector2 min, Vector2 max, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = new Vector2(8f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color bgColor, Color textColor)
        {
            GameObject go = CreatePanel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size, bgColor);
            Image img = go.GetComponent<Image>();
            img.raycastTarget = true;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            CreateText("Label", go.transform, label, 14f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, textColor);
            return btn;
        }
    }
}
