using System;
using System.Collections.Generic;
using System.Text;
using CapsuleArena.CameraSystem;
using CapsuleArena.Combat;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Player;
using CapsuleArena.UI.Common;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// Event-driven screen-space Combat HUD Presenter coordinating:
    /// - Top Match Status Bar (<c>ALIVE: X / 4</c>, 1 Hz <c>MM:SS</c> clock turning <c>#E63946</c> &lt;= 30s, <c>[V] TPS/FPS</c> badge, Spectator Banner)
    /// - Bottom-Center Local HP Bar (<c>100 / 100</c> + &lt;= 25 HP low-health crimson vignette)
    /// - Dynamic Center Crosshair + <c>0.15s</c> <c>#FFB703</c>/<c>#E63946</c> hit-marker + <c>90 deg</c> Horizontal Sweep arc indicator
    /// - Top-Right 4-item pooled <see cref="KillFeedPoolView"/>
    /// - Post-Match Victory (<c>#FFB703</c>) / Defeat (<c>#E63946</c>) / Draw (<c>#A8DADC</c>) Modal
    /// Uses a single pre-allocated <see cref="StringBuilder"/> (<c>0 B</c> steady-state GC; Story 10-3; ADR-0007; TR-hud-003).
    /// </summary>
    public sealed class CombatHudPresenter : MonoBehaviour
    {
        /// <summary>HP threshold at or below which the low-health crimson vignette activates (25 HP).</summary>
        public const int LowHealthVignetteThresholdHp = 25;

        /// <summary>Hit-marker flash duration in seconds (0.15s).</summary>
        public const float HitMarkerDurationSeconds = 0.15f;

        /// <summary>Match timer threshold in seconds below which the clock highlights crimson (30s).</summary>
        public const int UrgentClockThresholdSeconds = 30;

        /// <summary>Resting diameter of the center crosshair reticle in pixels (12px).</summary>
        public const float BaseCrosshairSizePx = 12.0f;

        /// <summary>Maximum expanded diameter of the dynamic center crosshair reticle in pixels (22px).</summary>
        public const float ExpandedCrosshairSizePx = 22.0f;

        /// <summary>Draw modal headline color (#A8DADC).</summary>
        public static readonly Color DrawIceCyan = new Color32(168, 218, 220, 255);

        private static readonly string[] FallbackPlayerNames =
        {
            "Player_0", "Player_1", "Player_2", "Player_3", "Player_4",
            "Player_5", "Player_6", "Player_7", "Player_8"
        };

        [Header("Top Status Bar")]
        [SerializeField] private TMP_Text _aliveCountText;
        [SerializeField] private TMP_Text _matchClockText;
        [SerializeField] private TMP_Text _perspectiveBadgeText;
        [SerializeField] private TMP_Text _spectatorBannerText;

        [Header("Bottom-Center Health & Vignette")]
        [SerializeField] private Image _localHpFillImage;
        [SerializeField] private TMP_Text _localHpValueText;
        [SerializeField] private CanvasGroup _lowHpVignetteGroup;

        [Header("Crosshair & Indicators")]
        [SerializeField] private RectTransform _crosshairRoot;
        [SerializeField] private Image _hitMarkerImage;
        [SerializeField] private CanvasGroup _sweepArcIndicatorGroup;
        [SerializeField] private TMP_Text _sweepArcDirectionText;

        [Header("Kill Feed & Match End Modal")]
        [SerializeField] private KillFeedPoolView _killFeedPool;
        [SerializeField] private CanvasGroup _matchEndModalGroup;
        [SerializeField] private TMP_Text _matchEndHeadlineText;
        [SerializeField] private TMP_Text _matchEndStatsText;
        [SerializeField] private TMP_Text _matchEndCountdownText;
        [SerializeField] private Button _returnToLobbyButton;
        [SerializeField] private Button _playAgainButton;

        private readonly StringBuilder _sb = new StringBuilder(128);
        private readonly Dictionary<PlayerRef, (NetworkString<_16> Raw, string Cached)> _nameCache =
            new Dictionary<PlayerRef, (NetworkString<_16>, string)>(8);

        private MatchManager _matchManager;
        private PlayerHealth _localHealth;
        private PlayerCombatController _localCombat;
        private PlayerRef _localPlayerRef = PlayerRef.None;
        private int _lastDisplayedWholeSecond = -1;
        private int _lastEndModalSecond = -1;
        private float _hitMarkerRemainingSec;
        private float _sweepIndicatorRemainingSec;
        private float _currentCrosshairSizePx = BaseCrosshairSizePx;

        /// <summary>Raised when the user clicks 'Return to Lobby' on the Match End Modal.</summary>
        public event Action OnReturnToLobbyRequested;

        /// <summary>Raised when the user clicks 'Play Again' on the Match End Modal.</summary>
        public event Action OnPlayAgainRequested;

        /// <summary>Gets the 4-row pooled Kill Feed component.</summary>
        public KillFeedPoolView KillFeed => _killFeedPool;

        /// <summary>Gets a value indicating whether the low-HP vignette is currently visible.</summary>
        public bool IsLowHealthVignetteActive => _lowHpVignetteGroup != null && _lowHpVignetteGroup.alpha > 0.05f;

        /// <summary>Gets a value indicating whether the center hit-marker is currently lit.</summary>
        public bool IsHitMarkerVisible => _hitMarkerImage != null && _hitMarkerImage.enabled && _hitMarkerRemainingSec > 0f;

        /// <summary>Gets the current color of the center hit-marker image.</summary>
        public Color CurrentHitMarkerColor => _hitMarkerImage != null ? _hitMarkerImage.color : Color.clear;

        /// <summary>Gets a value indicating whether the 90-degree Horizontal Sweep indicator is currently visible.</summary>
        public bool IsSweepArcIndicatorVisible => _sweepArcIndicatorGroup != null && _sweepArcIndicatorGroup.alpha > 0.05f;

        /// <summary>Gets the current dynamic center crosshair size in pixels ([12, 22]).</summary>
        public float CurrentCrosshairSpreadPx => _crosshairRoot != null ? _crosshairRoot.sizeDelta.x : _currentCrosshairSizePx;

        /// <summary>Gets a value indicating whether the Match End Modal is currently open.</summary>
        public bool IsMatchEndModalOpen => _matchEndModalGroup != null && _matchEndModalGroup.alpha > 0.5f;

        /// <summary>Gets the current Match End headline text ("VICTORY", "DEFEAT", or "DRAW").</summary>
        public string MatchEndHeadline => _matchEndHeadlineText != null ? _matchEndHeadlineText.text : string.Empty;

        /// <summary>Gets the current clock text ("MM:SS").</summary>
        public string DisplayedClockText => _matchClockText != null ? _matchClockText.text : string.Empty;

        /// <summary>Gets the Match End Modal 'Play Again' button.</summary>
        public Button PlayAgainButton => _playAgainButton;

        /// <summary>Gets the Match End Modal 'Return to Lobby' button.</summary>
        public Button ReturnToLobbyButton => _returnToLobbyButton;

        private void Awake()
        {
            EnsureHudHierarchyBuilt();
            ResetInitialHudPresentation();
            WireModalButtons();
        }

        private void ResetInitialHudPresentation()
        {
            if (_spectatorBannerText != null)
            {
                _spectatorBannerText.text = string.Empty;
                _spectatorBannerText.gameObject.SetActive(false);
            }

            if (_matchEndModalGroup != null)
            {
                _matchEndModalGroup.alpha = 0f;
                _matchEndModalGroup.interactable = false;
                _matchEndModalGroup.blocksRaycasts = false;
            }

            if (_lowHpVignetteGroup != null)
            {
                _lowHpVignetteGroup.alpha = 0f;
            }

            if (_hitMarkerImage != null)
            {
                _hitMarkerImage.enabled = false;
            }

            _killFeedPool?.ClearAllEntries();
        }

        private void OnEnable()
        {
            CameraController.OnAnyLocalPerspectiveChanged += HandlePerspectiveChanged;
            SpectatorController.OnAnyLocalSpectatorStateChanged += HandleSpectatorBannerChanged;
            PlayerHealth.OnAnyKillConfirmedBroadcast += HandleKillConfirmedBroadcast;
            PlayerHealth.OnAnyHitFeedbackBroadcast += HandleAnyHitFeedbackBroadcast;
        }

        private void OnDisable()
        {
            CameraController.OnAnyLocalPerspectiveChanged -= HandlePerspectiveChanged;
            SpectatorController.OnAnyLocalSpectatorStateChanged -= HandleSpectatorBannerChanged;
            PlayerHealth.OnAnyKillConfirmedBroadcast -= HandleKillConfirmedBroadcast;
            PlayerHealth.OnAnyHitFeedbackBroadcast -= HandleAnyHitFeedbackBroadcast;
        }

        private void OnDestroy()
        {
            UnbindMatchManager();
            UnbindLocalPlayer();
        }

        private void Update()
        {
            TickTransientIndicators(Time.deltaTime);
            if (_matchManager != null)
            {
                RefreshMatchClock1Hz(_matchManager.RemainingPhaseSeconds, _matchManager.CurrentMatchState);
            }
        }

        /// <summary>Binds the active <see cref="MatchManager"/> via C# events.</summary>
        public void BindMatchManager(MatchManager matchManager)
        {
            UnbindMatchManager();
            _matchManager = matchManager;
            if (_matchManager == null)
            {
                return;
            }

            _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
            _matchManager.OnRosterCountsChanged += HandleRosterCountsChanged;
            _matchManager.OnMatchEnded += HandleMatchEnded;

            HandleRosterCountsChanged(_matchManager.CountLivingPlayers(), Mathf.Max(4, _matchManager.CountConnectedPlayers()));
            HandleMatchStateChanged(_matchManager.CurrentMatchState);
        }

        /// <summary>Binds the local player's <see cref="PlayerHealth"/> and <see cref="PlayerCombatController"/>.</summary>
        public void BindLocalPlayer(
            PlayerHealth localHealth,
            PlayerCombatController localCombat = null,
            PlayerRef localPlayerRef = default)
        {
            UnbindLocalPlayer();
            _localHealth = localHealth;
            _localCombat = localCombat;
            _localPlayerRef = ResolveLocalPlayerRef(localHealth, localPlayerRef);

            if (_localHealth != null)
            {
                _localHealth.OnHealthChanged += HandleLocalHealthChanged;
                HandleLocalHealthChanged(_localHealth.CurrentHP, _localHealth.MaxHP);
            }

            if (_localCombat != null)
            {
                _localCombat.OnLocalHitMarkerTriggered += TriggerHitMarker;
                _localCombat.OnHorizontalSweepIndicatorTriggered += TriggerHorizontalSweepArcIndicator;
                _localCombat.OnCooldownUpdated += HandleCombatCooldownImpulse;
            }
        }

        /// <summary>Updates the local HP bar and &lt;= 25 HP low-health crimson vignette strictly on event dispatch.</summary>
        public void HandleLocalHealthChanged(int currentHp, int maxHp)
        {
            EnsureHudHierarchyBuilt();
            int safeMax = Mathf.Max(1, maxHp);
            int clampedHp = Mathf.Clamp(currentHp, 0, safeMax);
            UpdateLocalHpFillAndLabel(clampedHp, safeMax);

            if (_lowHpVignetteGroup != null)
            {
                bool showVignette = clampedHp > 0 && clampedHp <= LowHealthVignetteThresholdHp;
                _lowHpVignetteGroup.alpha = showVignette ? 0.55f : 0f;
            }
        }

        /// <summary>Updates the <c>ALIVE: X / 4</c> counter via the cached <see cref="StringBuilder"/>.</summary>
        public void HandleRosterCountsChanged(int aliveCount, int maxSlots = 4)
        {
            EnsureHudHierarchyBuilt();
            if (_aliveCountText == null)
            {
                return;
            }

            _sb.Clear();
            _sb.Append("ALIVE: ");
            _sb.Append(Mathf.Max(0, aliveCount));
            _sb.Append(" / ");
            _sb.Append(Mathf.Max(2, maxSlots));
            _aliveCountText.SetText(_sb);
        }

        /// <summary>
        /// Refreshes the <c>MM:SS</c> match clock strictly at 1 Hz (only when the integer second changes, allocating 0 B).
        /// </summary>
        public bool RefreshMatchClock1Hz(float remainingSeconds, MatchState state = MatchState.Playing)
        {
            int wholeSec = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            UpdateEndModalCountdownIfNeeded(wholeSec, state);

            if (wholeSec == _lastDisplayedWholeSecond)
            {
                return false;
            }

            _lastDisplayedWholeSecond = wholeSec;
            if (_matchClockText != null)
            {
                _matchClockText.text = UiThemeAndFontProvider.FormatClockZeroAlloc(wholeSec);
                _matchClockText.color = (state == MatchState.Playing && wholeSec <= UrgentClockThresholdSeconds)
                    ? UiThemeAndFontProvider.CrimsonDanger
                    : UiThemeAndFontProvider.PublicOffWhite;
            }

            return true;
        }

        /// <summary>Expands the dynamic center crosshair reticle on movement, swing, or shot impulse.</summary>
        public void NotifyCrosshairImpulse(float amountPx = 8.0f)
        {
            EnsureHudHierarchyBuilt();
            _currentCrosshairSizePx = Mathf.Clamp(_currentCrosshairSizePx + Mathf.Max(0f, amountPx), BaseCrosshairSizePx, ExpandedCrosshairSizePx);
            if (_crosshairRoot != null)
            {
                _crosshairRoot.sizeDelta = new Vector2(_currentCrosshairSizePx, _currentCrosshairSizePx);
            }
        }

        /// <summary>Flashes the center crosshair hit-marker (#FFB703 normal, #E63946 lethal) for 0.15s.</summary>
        public void TriggerHitMarker(bool isLethal)
        {
            EnsureHudHierarchyBuilt();
            NotifyCrosshairImpulse(6.0f);
            _hitMarkerRemainingSec = HitMarkerDurationSeconds;
            if (_hitMarkerImage != null)
            {
                _hitMarkerImage.enabled = true;
                _hitMarkerImage.color = isLethal
                    ? UiThemeAndFontProvider.CrimsonDanger
                    : UiThemeAndFontProvider.SolarAmber;
            }
        }

        /// <summary>Displays the 90-degree directional sweep indicator for 0.20s during Keys 1/3.</summary>
        public void TriggerHorizontalSweepArcIndicator(bool isLeftSweep)
        {
            EnsureHudHierarchyBuilt();
            NotifyCrosshairImpulse(8.0f);
            _sweepIndicatorRemainingSec = 0.20f;
            if (_sweepArcIndicatorGroup != null)
            {
                _sweepArcIndicatorGroup.alpha = 1.0f;
            }

            if (_sweepArcDirectionText != null)
            {
                _sweepArcDirectionText.text = isLeftSweep ? "<< 90° SWEEP" : "90° SWEEP >>";
            }
        }

        /// <summary>Presents the Victory / Defeat / Draw Match End Modal.</summary>
        public void HandleMatchEnded(MatchResultSummary summary)
        {
            EnsureHudHierarchyBuilt();
            if (_matchEndModalGroup == null)
            {
                return;
            }

            _matchEndModalGroup.alpha = 1.0f;
            _matchEndModalGroup.interactable = true;
            _matchEndModalGroup.blocksRaycasts = true;
            ApplyMatchEndHeadline(summary);
            ApplyMatchEndStatsSummary(summary);
        }

        /// <summary>Advances hit-marker, sweep indicator, and crosshair bloom decay timers deterministically.</summary>
        public void TickTransientIndicators(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (_hitMarkerRemainingSec > 0f)
            {
                _hitMarkerRemainingSec -= deltaTime;
                if (_hitMarkerRemainingSec <= 0f && _hitMarkerImage != null)
                {
                    _hitMarkerImage.enabled = false;
                }
            }

            if (_sweepIndicatorRemainingSec > 0f)
            {
                _sweepIndicatorRemainingSec -= deltaTime;
                if (_sweepIndicatorRemainingSec <= 0f && _sweepArcIndicatorGroup != null)
                {
                    _sweepArcIndicatorGroup.alpha = 0f;
                }
            }

            DecayCrosshairSpread(deltaTime);
        }

        /// <summary>Updates modal visibility when the match transitions out of <see cref="MatchState.Ended"/>.</summary>
        public void HandleMatchStateChanged(MatchState state)
        {
            if (state != MatchState.Ended && _matchEndModalGroup != null)
            {
                _matchEndModalGroup.alpha = 0f;
                _matchEndModalGroup.interactable = false;
                _matchEndModalGroup.blocksRaycasts = false;
            }
        }

        private static PlayerRef ResolveLocalPlayerRef(PlayerHealth localHealth, PlayerRef explicitRef)
        {
            if (explicitRef != PlayerRef.None)
            {
                return explicitRef;
            }

            return localHealth != null ? localHealth.OwnerPlayer : PlayerRef.None;
        }

        private void UpdateLocalHpFillAndLabel(int clampedHp, int safeMax)
        {
            if (_localHpFillImage != null)
            {
                _localHpFillImage.fillAmount = (float)clampedHp / safeMax;
                _localHpFillImage.color = clampedHp <= LowHealthVignetteThresholdHp
                    ? UiThemeAndFontProvider.CrimsonDanger
                    : UiThemeAndFontProvider.HealthyGreen;
            }

            if (_localHpValueText != null)
            {
                _sb.Clear();
                _sb.Append(clampedHp);
                _sb.Append(" / ");
                _sb.Append(safeMax);
                _localHpValueText.SetText(_sb);
            }
        }

        private void UpdateEndModalCountdownIfNeeded(int wholeSec, MatchState state)
        {
            if (state != MatchState.Ended || _matchEndCountdownText == null || wholeSec == _lastEndModalSecond)
            {
                return;
            }

            _lastEndModalSecond = wholeSec;
            _sb.Clear();
            _sb.Append("Next round in ");
            _sb.Append(wholeSec);
            _sb.Append("s...");
            _matchEndCountdownText.SetText(_sb);
        }

        private void DecayCrosshairSpread(float deltaTime)
        {
            if (_currentCrosshairSizePx <= BaseCrosshairSizePx)
            {
                return;
            }

            _currentCrosshairSizePx = Mathf.MoveTowards(_currentCrosshairSizePx, BaseCrosshairSizePx, deltaTime * 36.0f);
            if (_crosshairRoot != null)
            {
                _crosshairRoot.sizeDelta = new Vector2(_currentCrosshairSizePx, _currentCrosshairSizePx);
            }
        }

        private void ApplyMatchEndHeadline(MatchResultSummary summary)
        {
            if (_matchEndHeadlineText == null)
            {
                return;
            }

            if (summary.IsDraw)
            {
                _matchEndHeadlineText.text = "DRAW";
                _matchEndHeadlineText.color = DrawIceCyan;
            }
            else if (summary.LocalDidWin)
            {
                _matchEndHeadlineText.text = "VICTORY";
                _matchEndHeadlineText.color = UiThemeAndFontProvider.SolarAmber;
            }
            else
            {
                _matchEndHeadlineText.text = "DEFEAT";
                _matchEndHeadlineText.color = UiThemeAndFontProvider.CrimsonDanger;
            }
        }

        private void ApplyMatchEndStatsSummary(MatchResultSummary summary)
        {
            if (_matchEndStatsText == null)
            {
                return;
            }

            _sb.Clear();
            _sb.Append("Winner: ");
            _sb.Append(string.IsNullOrEmpty(summary.WinnerDisplayName) ? "DRAW" : summary.WinnerDisplayName);
            _sb.Append("   |   Kills: ");
            _sb.Append(summary.LocalKills);
            _sb.Append("   |   Rank: #");
            _sb.Append(Mathf.Max(1, summary.LocalPlacementRank));
            _matchEndStatsText.SetText(_sb);
        }

        private void HandleCombatCooldownImpulse(CapsuleArena.Core.Config.CombatSkillId skillId, float remainingSec, float totalSec)
        {
            if (remainingSec > 0f && remainingSec >= totalSec - 0.05f)
            {
                NotifyCrosshairImpulse(6.0f);
            }
        }

        /// <summary>Updates the top-bar camera perspective badge (<c>[V] TPS</c> / <c>[V] FPS</c>).</summary>
        public void HandlePerspectiveChanged(CameraPerspectiveMode mode)
        {
            EnsureHudHierarchyBuilt();
            if (_perspectiveBadgeText != null)
            {
                _perspectiveBadgeText.text = mode == CameraPerspectiveMode.FPS ? "[V] FPS" : "[V] TPS";
            }
        }

        private void HandleSpectatorBannerChanged(bool active, string banner)
        {
            if (_spectatorBannerText != null)
            {
                _spectatorBannerText.gameObject.SetActive(active && !string.IsNullOrEmpty(banner));
                _spectatorBannerText.text = banner ?? string.Empty;
            }
        }

        private void HandleKillConfirmedBroadcast(PlayerRef killer, PlayerRef victim)
        {
            string killerName = ResolvePlayerName(killer, "Arena");
            string victimName = ResolvePlayerName(victim, "Player");
            if (_killFeedPool != null)
            {
                _killFeedPool.PushKillNotification(killerName, victimName);
            }
        }

        private void HandleAnyHitFeedbackBroadcast(
            PlayerHealth victim,
            Vector3 hitPos,
            int damage,
            bool isLethal,
            PlayerRef attacker)
        {
            if (_localPlayerRef != PlayerRef.None && attacker == _localPlayerRef)
            {
                TriggerHitMarker(isLethal);
            }
        }

        private string ResolvePlayerName(PlayerRef player, string fallback)
        {
            if (player == PlayerRef.None)
            {
                return fallback;
            }

            if (_matchManager != null && _matchManager.TryGetPlayerStats(player, out PlayerMatchStats stats))
            {
                if (_nameCache.TryGetValue(player, out var entry) && entry.Raw.Equals(stats.DisplayName))
                {
                    return entry.Cached;
                }

                string converted = stats.DisplayName.ToString();
                if (!string.IsNullOrEmpty(converted))
                {
                    _nameCache[player] = (stats.DisplayName, converted);
                    return converted;
                }
            }

            int id = player.PlayerId > 0 ? player.PlayerId : Mathf.Abs(player.RawEncoded);
            return id >= 0 && id < FallbackPlayerNames.Length ? FallbackPlayerNames[id] : fallback;
        }

        private void WireModalButtons()
        {
            if (_returnToLobbyButton != null)
            {
                _returnToLobbyButton.onClick.RemoveAllListeners();
                _returnToLobbyButton.onClick.AddListener(InvokeReturnToLobby);
            }

            if (_playAgainButton != null)
            {
                _playAgainButton.onClick.RemoveAllListeners();
                _playAgainButton.onClick.AddListener(InvokePlayAgain);
            }
        }

        private void InvokeReturnToLobby()
        {
            OnReturnToLobbyRequested?.Invoke();
        }

        private void InvokePlayAgain()
        {
            OnPlayAgainRequested?.Invoke();
        }

        private void UnbindMatchManager()
        {
            if (_matchManager != null)
            {
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
                _matchManager.OnRosterCountsChanged -= HandleRosterCountsChanged;
                _matchManager.OnMatchEnded -= HandleMatchEnded;
                _matchManager = null;
            }
        }

        private void UnbindLocalPlayer()
        {
            if (_localHealth != null)
            {
                _localHealth.OnHealthChanged -= HandleLocalHealthChanged;
                _localHealth = null;
            }

            if (_localCombat != null)
            {
                _localCombat.OnLocalHitMarkerTriggered -= TriggerHitMarker;
                _localCombat.OnHorizontalSweepIndicatorTriggered -= TriggerHorizontalSweepArcIndicator;
                _localCombat.OnCooldownUpdated -= HandleCombatCooldownImpulse;
                _localCombat = null;
            }
        }

        private void EnsureHudHierarchyBuilt()
        {
            if (!TryGetComponent(out RectTransform root))
            {
                root = gameObject.AddComponent<RectTransform>();
            }

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            EnsureVignette(root);
            EnsureTopStatusBar(root);
            EnsureBottomHealthBar(root);
            EnsureCrosshairAndSweepArc(root);
            EnsureKillFeedPool(root);
            if (_matchEndModalGroup == null)
            {
                BuildMatchEndModal(root);
                WireModalButtons();
            }
        }

        private void EnsureVignette(RectTransform root)
        {
            if (_lowHpVignetteGroup != null)
            {
                return;
            }

            GameObject vig = CreatePanel("LowHpVignette", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color32(230, 57, 70, 90));
            _lowHpVignetteGroup = vig.AddComponent<CanvasGroup>();
            _lowHpVignetteGroup.alpha = 0f;
            _lowHpVignetteGroup.blocksRaycasts = false;
        }

        private void EnsureTopStatusBar(RectTransform root)
        {
            if (_aliveCountText != null)
            {
                return;
            }

            GameObject topBar = CreatePanel("TopStatusBar", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(540f, 48f), UiThemeAndFontProvider.PanelDark);
            _aliveCountText = CreateText("Txt_AliveCount", topBar.transform, "ALIVE: 4 / 4", 16f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(0.36f, 1f), UiThemeAndFontProvider.HealthyGreen);
            _matchClockText = CreateText("Txt_MatchClock", topBar.transform, "03:00", 22f, TextAlignmentOptions.Center, new Vector2(0.36f, 0f), new Vector2(0.66f, 1f), UiThemeAndFontProvider.PublicOffWhite);
            _perspectiveBadgeText = CreateText("Txt_CameraBadge", topBar.transform, "[V] TPS", 15f, TextAlignmentOptions.MidlineRight, new Vector2(0.66f, 0f), new Vector2(1f, 1f), UiThemeAndFontProvider.ElectricCyan);

            _spectatorBannerText = CreateText("Txt_SpectatorBanner", root, string.Empty, 16f, TextAlignmentOptions.Center, new Vector2(0.25f, 0.88f), new Vector2(0.75f, 0.93f), UiThemeAndFontProvider.SolarAmber);
            _spectatorBannerText.gameObject.SetActive(false);
        }

        private void EnsureBottomHealthBar(RectTransform root)
        {
            if (_localHpFillImage != null)
            {
                return;
            }

            GameObject hpPanel = CreatePanel("BottomLocalHealthBar", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(380f, 32f), UiThemeAndFontProvider.PanelDark);
            GameObject fillGo = CreatePanel("Img_LocalHpFill", hpPanel.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8f, -8f), UiThemeAndFontProvider.HealthyGreen);
            _localHpFillImage = fillGo.GetComponent<Image>();
            _localHpFillImage.type = Image.Type.Filled;
            _localHpFillImage.fillMethod = Image.FillMethod.Horizontal;
            _localHpFillImage.fillAmount = 1.0f;
            _localHpValueText = CreateText("Txt_LocalHpValue", hpPanel.transform, "100 / 100", 16f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Color.white);
        }

        private void EnsureCrosshairAndSweepArc(RectTransform root)
        {
            if (_crosshairRoot != null)
            {
                return;
            }

            GameObject ch = CreatePanel("CenterCrosshair", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BaseCrosshairSizePx, BaseCrosshairSizePx), new Color(1f, 1f, 1f, 0.85f));
            ch.GetComponent<Image>().sprite = UiThemeAndFontProvider.GetCircleSprite();
            _crosshairRoot = ch.GetComponent<RectTransform>();

            GameObject hm = CreatePanel("Img_HitMarker", _crosshairRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 28f), UiThemeAndFontProvider.SolarAmber);
            _hitMarkerImage = hm.GetComponent<Image>();
            _hitMarkerImage.enabled = false;

            GameObject arcGo = CreatePanel("SweepArcIndicator", _crosshairRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -34f), new Vector2(180f, 26f), new Color32(255, 159, 28, 180));
            _sweepArcIndicatorGroup = arcGo.AddComponent<CanvasGroup>();
            _sweepArcIndicatorGroup.alpha = 0f;
            _sweepArcIndicatorGroup.blocksRaycasts = false;
            _sweepArcDirectionText = CreateText("Txt_SweepDir", arcGo.transform, "90° SWEEP", 14f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Color.white);
        }

        private void EnsureKillFeedPool(RectTransform root)
        {
            if (_killFeedPool != null)
            {
                return;
            }

            GameObject kfGo = new GameObject("KillFeedPool", typeof(RectTransform), typeof(VerticalLayoutGroup));
            kfGo.transform.SetParent(root, worldPositionStays: false);
            RectTransform kfrt = kfGo.GetComponent<RectTransform>();
            kfrt.anchorMin = new Vector2(1f, 1f);
            kfrt.anchorMax = new Vector2(1f, 1f);
            kfrt.pivot = new Vector2(1f, 1f);
            kfrt.anchoredPosition = new Vector2(-20f, -20f);
            kfrt.sizeDelta = new Vector2(330f, 140f);
            VerticalLayoutGroup vlg = kfGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperRight;
            vlg.childControlHeight = false;
            vlg.childControlWidth = false;
            _killFeedPool = kfGo.AddComponent<KillFeedPoolView>();
            _killFeedPool.EnsurePoolPrewarmed();
        }

        private void BuildMatchEndModal(RectTransform root)
        {
            GameObject modalBackdrop = CreatePanel("MatchEndModal", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color32(8, 12, 24, 215));
            modalBackdrop.GetComponent<Image>().raycastTarget = true;
            _matchEndModalGroup = modalBackdrop.AddComponent<CanvasGroup>();
            _matchEndModalGroup.alpha = 0f;
            _matchEndModalGroup.interactable = false;
            _matchEndModalGroup.blocksRaycasts = false;

            GameObject card = CreatePanel("EndCard", modalBackdrop.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 300f), UiThemeAndFontProvider.CardSurface);
            _matchEndHeadlineText = CreateText("Txt_Headline", card.transform, "VICTORY", 42f, TextAlignmentOptions.Center, new Vector2(0.05f, 0.65f), new Vector2(0.95f, 0.92f), UiThemeAndFontProvider.SolarAmber);
            _matchEndStatsText = CreateText("Txt_SummaryStats", card.transform, "Winner: Player_1   |   Kills: 3   |   Rank: #1", 18f, TextAlignmentOptions.Center, new Vector2(0.05f, 0.44f), new Vector2(0.95f, 0.64f), UiThemeAndFontProvider.PublicOffWhite);
            _matchEndCountdownText = CreateText("Txt_RematchCountdown", card.transform, "Next round in 8s...", 15f, TextAlignmentOptions.Center, new Vector2(0.1f, 0.30f), new Vector2(0.9f, 0.42f), UiThemeAndFontProvider.ElectricCyan);

            _playAgainButton = CreateModalButton(card.transform, "BTN_PlayAgain", "PLAY AGAIN", new Vector2(-115f, -95f), UiThemeAndFontProvider.HealthyGreen);
            _returnToLobbyButton = CreateModalButton(card.transform, "BTN_ReturnToLobby", "RETURN TO LOBBY", new Vector2(115f, -95f), UiThemeAndFontProvider.CrimsonDanger);
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = go.AddComponent<Image>();
            img.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        private static TMP_Text CreateText(string name, Transform parent, string content, float size, TextAlignmentOptions align, Vector2 min, Vector2 max, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = new Vector2(10f, 0f);
            rt.offsetMax = new Vector2(-10f, 0f);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.text = content;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateModalButton(Transform parent, string name, string label, Vector2 anchoredPos, Color bgColor)
        {
            GameObject go = CreatePanel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPos, new Vector2(195f, 46f), bgColor);
            Image img = go.GetComponent<Image>();
            img.raycastTarget = true;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            CreateText("Label", go.transform, label, 15f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Color.white);
            return btn;
        }
    }
}
