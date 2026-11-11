using System;
using System.Collections.Generic;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Gameplay.Match
{
    /// <summary>
    /// Networked Last Man Standing match state machine (<c>WaitingForPlayers -&gt; Countdown -&gt; Playing -&gt; Ended</c>),
    /// Sudden Death tie-breaker evaluator, and deterministic Shared Mode MasterClient failover coordinator
    /// implementing <see cref="IStateAuthorityChanged"/> (Stories 9-1, 9-2, 9-3; ADR-0006; TR-match-001..003).
    /// </summary>
    public sealed class MatchManager : NetworkBehaviour, IStateAuthorityChanged
    {
        /// <summary>Minimum connected players required to start and sustain the pre-match countdown (2).</summary>
        public const int MinPlayersToStart = 2;

        /// <summary>Maximum players supported per Shared Mode room (4).</summary>
        public const int MaxPlayersPerMatch = 4;

        /// <summary>Countdown phase duration in seconds (5.0s = 300 ticks @ 60Hz).</summary>
        public const float CountdownDurationSeconds = 5.0f;

        /// <summary>Countdown phase duration in 60Hz ticks (300).</summary>
        public const int CountdownDurationTicks = 300;

        /// <summary>Active combat phase duration in seconds (180.0s = 10,800 ticks @ 60Hz).</summary>
        public const float PlayingDurationSeconds = 180.0f;

        /// <summary>Active combat phase duration in 60Hz ticks (10,800).</summary>
        public const int PlayingDurationTicks = 10800;

        /// <summary>Default opening window of Playing in seconds allowing 3rd/4th players to join alive (60.0s = 3,600 ticks @ 60Hz).</summary>
        public const float DefaultLateJoinActiveWindowSeconds = 60.0f;

        /// <summary>Default opening window of Playing in 60Hz ticks allowing 3rd/4th players to join alive (3,600).</summary>
        public const int DefaultLateJoinActiveWindowTicks = 3600;

        /// <summary>Post-match summary phase duration in seconds (8.0s = 480 ticks @ 60Hz).</summary>
        public const float EndedDurationSeconds = 8.0f;

        /// <summary>Post-match summary phase duration in 60Hz ticks (480).</summary>
        public const int EndedDurationTicks = 480;

        [Header("Match Rules Configuration")]
        [Tooltip("Designer-editable ScriptableObject defining countdown duration (default 5s) and late-join active combatant window (default 60s).")]
        [SerializeField] private MatchRulesConfigSO _matchRulesConfig;

        /// <summary>Gets the replicated current match lifecycle state.</summary>
        [Networked] public MatchState NetworkedMatchState { get; private set; }

        /// <summary>Gets the replicated countdown / playing / post-match TickTimer.</summary>
        [Networked] public TickTimer PhaseTimer { get; private set; }

        /// <summary>Gets the replicated winning PlayerRef (or PlayerRef.None on Draw).</summary>
        [Networked] public PlayerRef NetworkedWinnerPlayer { get; private set; }

        /// <summary>Gets the replicated monotonically increasing match sequence number for deterministic MatchIds.</summary>
        [Networked] public int NetworkedMatchSequence { get; private set; }

        /// <summary>Gets the replicated 4-slot player roster surviving MasterClient disconnects.</summary>
        [Networked, Capacity(MaxPlayersPerMatch)]
        public NetworkDictionary<PlayerRef, PlayerMatchStats> MatchRoster => default;

        private readonly Dictionary<PlayerRef, PlayerMatchStats> _localRoster =
            new Dictionary<PlayerRef, PlayerMatchStats>(MaxPlayersPerMatch);

        private readonly Dictionary<PlayerRef, int> _standaloneHpOverrides =
            new Dictionary<PlayerRef, int>(MaxPlayersPerMatch);

        private readonly PlayerRef[] _activePlayerBuffer = new PlayerRef[MaxPlayersPerMatch];
        private readonly PlayerRef[] _staleKeyBuffer = new PlayerRef[MaxPlayersPerMatch];
        private readonly SurvivorCandidate[] _survivorBuffer = new SurvivorCandidate[MaxPlayersPerMatch];
        private readonly List<PlayerHealth> _avatarScratchList = new List<PlayerHealth>(MaxPlayersPerMatch);

        private ChangeDetector _changeDetector;
        private IInputFocusService _focusService;
        private IMatchRulesConfig _runtimeRulesOverride;
        private Func<bool> _cachedCombatAllowedPredicate;
        private MatchState _localState = MatchState.WaitingForPlayers;
        private MatchState _lastPresentationState = MatchState.WaitingForPlayers;
        private PlayerRef _localWinner = PlayerRef.None;
        private PlayerRef _localPlayerRef = PlayerRef.None;
        private int _localPhaseTicksRemaining;
        private int _localMatchSequence = 1;
        private int _cachedMatchIdSequence = -1;
        private string _sessionRoomId = "LOCAL";
        private string _cachedMatchIdRoom = string.Empty;
        private string _cachedMatchId = "LOCAL_M1";
        private bool _hasDispatchedEndedForCurrentMatch;
        private bool _pendingKillEvaluation;
        private bool _isNetworkSpawned;
        private PlayerRef _pendingDisplayNamePlayer = PlayerRef.None;
        private string _pendingDisplayName;

        /// <summary>Raised whenever <see cref="CurrentMatchState"/> transitions.</summary>
        public event Action<MatchState> OnMatchStateChanged;

        /// <summary>Raised whenever living/total roster counts change: (aliveCount, totalConnected).</summary>
        public event Action<int, int> OnRosterCountsChanged;

        /// <summary>Raised strictly once per match when entering <see cref="MatchState.Ended"/>.</summary>
        public event Action<MatchResultSummary> OnMatchEnded;

        /// <summary>Gets the active match rules configuration (runtime DI override or serialized <see cref="MatchRulesConfigSO"/>).</summary>
        public IMatchRulesConfig ActiveRulesConfig => _runtimeRulesOverride ?? _matchRulesConfig;

        /// <summary>Gets the configured minimum connected players required to start countdown (default: 2).</summary>
        public int ActiveMinPlayersToStart => ActiveRulesConfig != null ? ActiveRulesConfig.MinPlayersToStart : MinPlayersToStart;

        /// <summary>Gets the configured maximum connected players per Shared Mode room (default: 4).</summary>
        public int ActiveMaxPlayersPerMatch => ActiveRulesConfig != null ? ActiveRulesConfig.MaxPlayersPerMatch : MaxPlayersPerMatch;

        /// <summary>Gets the configured pre-match countdown duration in seconds (default: 5.0s).</summary>
        public float ActiveCountdownDurationSeconds => ActiveRulesConfig != null ? ActiveRulesConfig.CountdownDurationSeconds : CountdownDurationSeconds;

        /// <summary>Gets the configured pre-match countdown duration in 60Hz ticks (default: 300 = 5.0s).</summary>
        public int ActiveCountdownDurationTicks => ActiveRulesConfig != null ? ActiveRulesConfig.CountdownDurationTicks : CountdownDurationTicks;

        /// <summary>Gets the configured active combat round duration in seconds (default: 180.0s).</summary>
        public float ActivePlayingDurationSeconds => ActiveRulesConfig != null ? ActiveRulesConfig.PlayingDurationSeconds : PlayingDurationSeconds;

        /// <summary>Gets the configured active combat round duration in 60Hz ticks (default: 10,800 = 180.0s).</summary>
        public int ActivePlayingDurationTicks => ActiveRulesConfig != null ? ActiveRulesConfig.PlayingDurationTicks : PlayingDurationTicks;

        /// <summary>Gets the configured late-join active combatant window in seconds (default: 60.0s).</summary>
        public float ActiveLateJoinWindowSeconds => ActiveRulesConfig != null ? ActiveRulesConfig.LateJoinActiveWindowSeconds : DefaultLateJoinActiveWindowSeconds;

        /// <summary>Gets the configured late-join active combatant window in 60Hz ticks (default: 3,600 = 60.0s).</summary>
        public int ActiveLateJoinWindowTicks => ActiveRulesConfig != null ? ActiveRulesConfig.LateJoinActiveWindowTicks : DefaultLateJoinActiveWindowTicks;

        /// <summary>Gets the configured post-match summary duration in seconds (default: 8.0s).</summary>
        public float ActiveEndedDurationSeconds => ActiveRulesConfig != null ? ActiveRulesConfig.EndedDurationSeconds : EndedDurationSeconds;

        /// <summary>Gets the configured post-match summary duration in 60Hz ticks (default: 480 = 8.0s).</summary>
        public int ActiveEndedDurationTicks => ActiveRulesConfig != null ? ActiveRulesConfig.EndedDurationTicks : EndedDurationTicks;

        /// <summary>Gets the active match state across networked or standalone test execution.</summary>
        public MatchState CurrentMatchState => IsSpawnedOnRunner ? NetworkedMatchState : _localState;

        /// <summary>Gets the crowned winner (or <see cref="PlayerRef.None"/> when Draw or undecided).</summary>
        public PlayerRef WinnerPlayer => IsSpawnedOnRunner ? NetworkedWinnerPlayer : _localWinner;

        /// <summary>Gets a value indicating whether combat and damage are permitted right now.</summary>
        public bool IsCombatAllowed => CurrentMatchState == MatchState.Playing;

        /// <summary>Gets the remaining phase time in seconds.</summary>
        public float RemainingPhaseSeconds => IsSpawnedOnRunner
            ? (PhaseTimer.RemainingTime(Runner) ?? 0f)
            : (_localPhaseTicksRemaining / (float)CharacterCombatConfigSO.DefaultTickRate);

        /// <summary>Gets the elapsed time in seconds since entering <see cref="MatchState.Playing"/> (0 outside Playing).</summary>
        public float ElapsedPlayingSeconds
        {
            get
            {
                if (CurrentMatchState != MatchState.Playing)
                {
                    return 0f;
                }

                if (IsSpawnedOnRunner)
                {
                    float totalPlayingSec = ActivePlayingDurationSeconds;
                    if (!PhaseTimer.IsRunning)
                    {
                        return 0f;
                    }

                    if (PhaseTimer.Expired(Runner))
                    {
                        return totalPlayingSec;
                    }

                    float remainingSec = PhaseTimer.RemainingTime(Runner) ?? 0f;
                    return Mathf.Clamp(totalPlayingSec - remainingSec, 0f, totalPlayingSec);
                }

                return GetElapsedPlayingTicks() / (float)CharacterCombatConfigSO.DefaultTickRate;
            }
        }

        /// <summary>Gets the current deterministic MatchId string (<c>&lt;Room&gt;_M&lt;Seq&gt;</c>).</summary>
        public string CurrentMatchId
        {
            get
            {
                int seq = IsSpawnedOnRunner ? Mathf.Max(1, NetworkedMatchSequence) : Mathf.Max(1, _localMatchSequence);
                if (seq != _cachedMatchIdSequence || !string.Equals(_sessionRoomId, _cachedMatchIdRoom, StringComparison.Ordinal))
                {
                    _cachedMatchIdSequence = seq;
                    _cachedMatchIdRoom = _sessionRoomId;
                    _cachedMatchId = $"{_sessionRoomId}_M{seq}";
                }

                return _cachedMatchId;
            }
        }

        /// <summary>Gets a value indicating whether this <see cref="MatchManager"/> has completed Fusion <c>Spawned()</c> attach.</summary>
        public bool IsReadyOnNetwork => IsSpawnedOnRunner;

        /// <summary>Gets any queued display name awaiting <c>Spawned()</c> flush on a joining client.</summary>
        public string PendingDisplayName => _pendingDisplayName;

        private bool IsSpawnedOnRunner => _isNetworkSpawned && Object != null && Runner != null && Object.IsValid;

        private void Awake()
        {
            _cachedCombatAllowedPredicate = CheckIsCombatAllowed;
        }

        private void OnEnable()
        {
            if (_cachedCombatAllowedPredicate == null)
            {
                _cachedCombatAllowedPredicate = CheckIsCombatAllowed;
            }

            PlayerHealth.OnAnyKillConfirmedBroadcast += HandleKillConfirmed;
            DamageReceiverRegistry.OnReceiverRegistered += HandleAvatarRegistered;
            BindRegisteredAvatarsNonAlloc();
        }

        private void OnDisable()
        {
            PlayerHealth.OnAnyKillConfirmedBroadcast -= HandleKillConfirmed;
            DamageReceiverRegistry.OnReceiverRegistered -= HandleAvatarRegistered;
        }

        /// <summary>Injects an <see cref="IMatchRulesConfig"/> implementation for runtime configuration or unit testing.</summary>
        public void SetMatchRulesConfig(IMatchRulesConfig rulesConfig)
        {
            _runtimeRulesOverride = rulesConfig;
        }

        /// <summary>
        /// Returns the integer number of 60Hz ticks elapsed since the current match entered <see cref="MatchState.Playing"/>
        /// (returns <c>0</c> whenever <see cref="CurrentMatchState"/> is not <see cref="MatchState.Playing"/>).
        /// </summary>
        public int GetElapsedPlayingTicks()
        {
            if (CurrentMatchState != MatchState.Playing)
            {
                return 0;
            }

            int totalPlayingTicks = ActivePlayingDurationTicks;
            if (IsSpawnedOnRunner)
            {
                return Mathf.Clamp(
                    Mathf.RoundToInt(ElapsedPlayingSeconds * CharacterCombatConfigSO.DefaultTickRate),
                    0,
                    totalPlayingTicks);
            }

            return Mathf.Clamp(totalPlayingTicks - _localPhaseTicksRemaining, 0, totalPlayingTicks);
        }

        /// <summary>
        /// Returns <c>true</c> if a newly joining player should spawn alive as an active combatant:
        /// always <c>true</c> before <see cref="MatchState.Playing"/>, and <c>true</c> during the opening
        /// <see cref="ActiveLateJoinWindowSeconds"/> (default <c>60.0s</c>) of <see cref="MatchState.Playing"/>.
        /// Strictly after that window, returns <c>false</c> so the player joins as a Ghost Spectator.
        /// </summary>
        public bool CanJoinCurrentMatchAlive()
        {
            if (CurrentMatchState != MatchState.Playing)
            {
                return true;
            }

            if (IsSpawnedOnRunner)
            {
                return ElapsedPlayingSeconds <= ActiveLateJoinWindowSeconds + 0.0001f;
            }

            return GetElapsedPlayingTicks() <= ActiveLateJoinWindowTicks;
        }

        /// <summary>
        /// Scrubs transient local state when a Shared Mode <see cref="NetworkRunner"/> shuts down so
        /// <see cref="MatchManager"/> can be cleanly re-registered on subsequent room joins without destroying the scene object.
        /// </summary>
        public void ResetAfterSessionShutdown()
        {
            _localRoster.Clear();
            _standaloneHpOverrides.Clear();
            _localState = MatchState.WaitingForPlayers;
            _lastPresentationState = MatchState.WaitingForPlayers;
            _localWinner = PlayerRef.None;
            _localPlayerRef = PlayerRef.None;
            _localPhaseTicksRemaining = 0;
            _localMatchSequence = 1;
            _cachedMatchIdSequence = -1;
            _hasDispatchedEndedForCurrentMatch = false;
            _pendingKillEvaluation = false;
            _isNetworkSpawned = false;
            _pendingDisplayNamePlayer = PlayerRef.None;
            _pendingDisplayName = null;
            _focusService?.SetFocusFlag(InputFocusFlag.MatchEnded, false);
            EmitRosterCounts();
            OnMatchStateChanged?.Invoke(MatchState.WaitingForPlayers);
        }

        /// <summary>Injects focus service, session room code, and local player reference for HUD/tests.</summary>
        public void ConfigureServices(
            IInputFocusService focusService,
            string sessionRoomId = "ARENA",
            PlayerRef localPlayer = default)
        {
            _focusService = focusService;
            if (!string.IsNullOrWhiteSpace(sessionRoomId))
            {
                _sessionRoomId = sessionRoomId.Trim().ToUpperInvariant();
                _cachedMatchIdSequence = -1;
            }

            _localPlayerRef = localPlayer;
            BindRegisteredAvatarsNonAlloc();
        }

        /// <summary>Requests an immediate rematch transition when currently in <see cref="MatchState.Ended"/>.</summary>
        public void RequestImmediateRematchIfEnded()
        {
            if (CurrentMatchState != MatchState.Ended)
            {
                return;
            }

            if (IsSpawnedOnRunner && !Object.HasStateAuthority)
            {
                Rpc_RequestImmediateRematch();
                return;
            }

            int connected = CountConnectedPlayers();
            AdvanceMatchSequenceAndResetPlayers();
            TransitionToState(connected >= ActiveMinPlayersToStart ? MatchState.Countdown : MatchState.WaitingForPlayers);
        }

        /// <summary>Submits or updates a player's validated display name in the match roster.</summary>
        public void SubmitPlayerDisplayName(PlayerRef player, string displayName)
        {
            if (player == PlayerRef.None || string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            string trimmed = displayName.Trim();
            if (trimmed.Length > 16) trimmed = trimmed.Substring(0, 16);

            if (IsSpawnedOnRunner)
            {
                _pendingDisplayNamePlayer = PlayerRef.None;
                _pendingDisplayName = null;

                if (Object.HasStateAuthority)
                {
                    ApplyAuthoritativeDisplayName(player, trimmed);
                }
                else
                {
                    Rpc_RegisterDisplayName(player, trimmed);
                }

                return;
            }

            // Scene NetworkObjects on joining Shared Mode clients have Object & Runner assigned before Spawned().
            // Queue the display name so Spawned() flushes Rpc_RegisterDisplayName once Object.IsValid is true.
            _pendingDisplayNamePlayer = player;
            _pendingDisplayName = trimmed;

            if (_localRoster.TryGetValue(player, out PlayerMatchStats existing))
            {
                existing.DisplayName = trimmed;
                _localRoster[player] = existing;
                EmitRosterCounts();
            }
        }

        /// <summary>RPC forwarding a peer's lobby DisplayName to the MasterClient's <see cref="MatchRoster"/>.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void Rpc_RegisterDisplayName(PlayerRef player, NetworkString<_16> displayName)
        {
            ApplyAuthoritativeDisplayName(player, displayName.ToString());
        }

        /// <summary>RPC allowing any peer on the Match End modal to request an immediate rematch.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void Rpc_RequestImmediateRematch()
        {
            RequestImmediateRematchIfEnded();
        }

        /// <inheritdoc />
        public override void Spawned()
        {
            _isNetworkSpawned = true;
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            _localPlayerRef = Runner.LocalPlayer;
            if (Runner.SessionInfo.IsValid && !string.IsNullOrEmpty(Runner.SessionInfo.Name))
            {
                _sessionRoomId = Runner.SessionInfo.Name;
                _cachedMatchIdSequence = -1;
            }

            BindRegisteredAvatarsNonAlloc();

            if (Object.HasStateAuthority)
            {
                MatchRoster.Clear();
                NetworkedMatchState = MatchState.WaitingForPlayers;
                NetworkedWinnerPlayer = PlayerRef.None;
                NetworkedMatchSequence = 1;
                PhaseTimer = TickTimer.None;
                SyncActiveRunnerPlayersIntoRoster();
            }

            FlushPendingDisplayNameIfSpawned();
            ApplyPresentationStateChange(CurrentMatchState);
        }

        /// <inheritdoc />
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isNetworkSpawned = false;
        }

        /// <summary>
        /// Flushes any display name queued prior to <see cref="Spawned"/> (e.g., when a joining client's avatar
        /// spawns before the scene <see cref="MatchManager"/> snapshot arrives from the MasterClient).
        /// </summary>
        public void FlushPendingDisplayNameIfSpawned()
        {
            if (_pendingDisplayNamePlayer == PlayerRef.None || string.IsNullOrEmpty(_pendingDisplayName))
            {
                return;
            }

            if (!IsSpawnedOnRunner)
            {
                return;
            }

            PlayerRef player = _pendingDisplayNamePlayer;
            string nameToSubmit = _pendingDisplayName;
            _pendingDisplayNamePlayer = PlayerRef.None;
            _pendingDisplayName = null;
            SubmitPlayerDisplayName(player, nameToSubmit);
        }

        /// <inheritdoc />
        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.HasStateAuthority)
            {
                return;
            }

            SyncActiveRunnerPlayersIntoRoster();
            if (_pendingKillEvaluation)
            {
                _pendingKillEvaluation = false;
                if (EvaluateWinConditions(isTimeoutSuddenDeath: false))
                {
                    return;
                }
            }

            bool expired = !PhaseTimer.IsRunning || PhaseTimer.Expired(Runner);
            StepAuthoritativeStateMachine(expired);
        }

        /// <inheritdoc />
        public override void Render()
        {
            foreach (string changed in _changeDetector.DetectChanges(this))
            {
                if (changed == nameof(NetworkedMatchState))
                {
                    ApplyPresentationStateChange(NetworkedMatchState);
                }
                else if (changed == nameof(MatchRoster))
                {
                    EmitRosterCounts();
                }
            }
        }

        /// <inheritdoc />
        public void StateAuthorityChanged()
        {
            if (!IsSpawnedOnRunner || !Object.HasStateAuthority)
            {
                return;
            }

            int activeCount = CopyRunnerActivePlayersNonAlloc(_activePlayerBuffer);
            ReconcileRosterOnFailover(new ReadOnlySpan<PlayerRef>(_activePlayerBuffer, 0, activeCount));
        }

        /// <summary>
        /// Called when a peer disconnects so the deterministic next lowest <see cref="PlayerRef"/> requests
        /// <c>StateAuthority</c> within &lt;= 2 ticks and removes the disconnected entry (Story 9-3 AC-1..AC-3).
        /// </summary>
        public void NotifyPlayerLeft(PlayerRef leavingPlayer)
        {
            if (IsSpawnedOnRunner)
            {
                int count = CopyRunnerActivePlayersNonAlloc(_activePlayerBuffer);
                ReadOnlySpan<PlayerRef> activeSpan = new ReadOnlySpan<PlayerRef>(_activePlayerBuffer, 0, count);

                if (!Object.HasStateAuthority)
                {
                    PlayerRef nextMaster = SelectDeterministicNextMaster(activeSpan, leavingPlayer);
                    if (nextMaster != PlayerRef.None && nextMaster == Runner.LocalPlayer)
                    {
                        Object.RequestStateAuthority();
                    }

                    return;
                }

                ReconcileRosterOnFailover(activeSpan, leavingPlayer);
                return;
            }

            MarkPlayerDisconnectedInStandaloneRoster(leavingPlayer);
        }

        /// <summary>Registers or updates a player inside the standalone test roster.</summary>
        public void RegisterOrUpdateStandalonePlayer(
            PlayerRef player,
            string displayName = "",
            bool isAlive = true,
            int kills = 0,
            int currentHp = 100)
        {
            if (player == PlayerRef.None)
            {
                return;
            }

            bool isNewMidMatchJoin = CurrentMatchState == MatchState.Playing && !_localRoster.ContainsKey(player);
            bool effectiveAlive = isNewMidMatchJoin
                ? (isAlive && CanJoinCurrentMatchAlive())
                : isAlive;

            PlayerMatchStats stats = new PlayerMatchStats
            {
                IsAlive = effectiveAlive,
                Kills = Mathf.Max(0, kills),
                PlacementRank = effectiveAlive ? 0 : ActiveMaxPlayersPerMatch,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"P{GetSortablePlayerKey(player)}" : displayName
            };

            _localRoster[player] = stats;
            _standaloneHpOverrides[player] = Mathf.Clamp(currentHp, 0, 100);
            BindRegisteredAvatarsNonAlloc();
            EmitRosterCounts();
        }

        /// <summary>Advances the standalone state machine by <paramref name="ticks"/> simulation ticks for deterministic unit tests.</summary>
        public void SimulateStandaloneTicks(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                if (_pendingKillEvaluation)
                {
                    _pendingKillEvaluation = false;
                    if (EvaluateWinConditions(isTimeoutSuddenDeath: false))
                    {
                        continue;
                    }
                }

                if (_localPhaseTicksRemaining > 0)
                {
                    _localPhaseTicksRemaining--;
                }

                StepAuthoritativeStateMachine(_localPhaseTicksRemaining <= 0);
            }
        }

        /// <summary>Records an authoritative player elimination and optionally defers win-check to end-of-tick (Story 9-2).</summary>
        public void RecordElimination(PlayerRef killer, PlayerRef victim, bool evaluateImmediately = true)
        {
            if (CurrentMatchState != MatchState.Playing)
            {
                return;
            }

            int currentAliveBefore = CountLivingPlayers();
            SetPlayerAliveAndRank(victim, isAlive: false, placementRank: Mathf.Max(2, currentAliveBefore));
            _standaloneHpOverrides[victim] = 0;

            if (killer != PlayerRef.None && killer != victim)
            {
                IncrementPlayerKills(killer);
            }

            EmitRosterCounts();
            if (evaluateImmediately)
            {
                EvaluateWinConditions(isTimeoutSuddenDeath: false);
            }
            else
            {
                _pendingKillEvaluation = true;
            }
        }

        /// <summary>
        /// Reconciles <c>MatchRoster</c> against <paramref name="activePlayers"/> after a MasterClient failover,
        /// removing disconnected entries, preserving <c>PhaseTimer</c>, and re-evaluating win conditions (Story 9-3 AC-1..AC-3).
        /// </summary>
        public void ReconcileRosterOnFailover(ReadOnlySpan<PlayerRef> activePlayers, PlayerRef explicitLeavingPlayer = default)
        {
            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                RemoveStaleNetworkedRosterEntries(activePlayers, explicitLeavingPlayer);
            }
            else
            {
                RemoveStaleLocalRosterEntries(activePlayers, explicitLeavingPlayer);
            }

            EmitRosterCounts();
            EvaluatePostFailoverTransition(CountConnectedPlayers());
        }

        /// <summary>
        /// Evaluates Last Man Standing (<c>AliveCount &lt;= 1</c>), mutual double-KO (<c>AliveCount == 0 -&gt; DRAW</c>),
        /// and 180.0s Sudden Death (<c>CurrentHP DESC, Kills DESC</c>) (Story 9-2 AC-1..AC-3).
        /// </summary>
        public bool EvaluateWinConditions(bool isTimeoutSuddenDeath = false)
        {
            if (CurrentMatchState != MatchState.Playing)
            {
                return false;
            }

            int livingCount = GatherLivingCandidatesNonAlloc(_survivorBuffer);
            if (!isTimeoutSuddenDeath && livingCount > 1)
            {
                return false;
            }

            PlayerRef winner = ResolveWinnerOrDraw(new ReadOnlySpan<SurvivorCandidate>(_survivorBuffer, 0, livingCount));
            ConcludeMatchWithWinner(winner);
            return true;
        }

        /// <summary>
        /// Copies active roster player display names in deterministic <c>PlayerRef</c> order for the 4-slot Waiting UI,
        /// returning the number of occupied slots and setting <paramref name="hostSlotIndex"/>.
        /// </summary>
        public int CopyOrderedRosterSlotNames(string[] slotNames, out int hostSlotIndex)
        {
            hostSlotIndex = 0;
            if (slotNames == null || slotNames.Length == 0)
            {
                return 0;
            }

            int count = CollectRosterKeysSorted(_activePlayerBuffer);
            for (int i = 0; i < slotNames.Length; i++)
            {
                if (i < count && TryGetPlayerStats(_activePlayerBuffer[i], out PlayerMatchStats stats))
                {
                    string name = stats.DisplayName.ToString();
                    slotNames[i] = string.IsNullOrEmpty(name) ? $"Player_{GetSortablePlayerKey(_activePlayerBuffer[i])}" : name;
                }
                else
                {
                    slotNames[i] = string.Empty;
                }
            }

            return count;
        }

        /// <summary>
        /// Pure state-transition function testable without a <see cref="NetworkRunner"/> (Story 9-1).
        /// </summary>
        public static MatchState EvaluateStateTransition(
            MatchState currentState,
            int connectedPlayers,
            int alivePlayers,
            bool phaseTimerExpired,
            int minPlayersToStart = MinPlayersToStart)
        {
            int minRequired = Mathf.Max(2, minPlayersToStart);
            return currentState switch
            {
                MatchState.WaitingForPlayers => connectedPlayers >= minRequired
                    ? MatchState.Countdown
                    : MatchState.WaitingForPlayers,
                MatchState.Countdown => connectedPlayers < minRequired
                    ? MatchState.WaitingForPlayers
                    : (phaseTimerExpired ? MatchState.Playing : MatchState.Countdown),
                MatchState.Playing => (alivePlayers <= 1 || phaseTimerExpired)
                    ? MatchState.Ended
                    : MatchState.Playing,
                MatchState.Ended => phaseTimerExpired
                    ? (connectedPlayers >= minRequired ? MatchState.Countdown : MatchState.WaitingForPlayers)
                    : MatchState.Ended,
                _ => MatchState.WaitingForPlayers
            };
        }

        /// <summary>
        /// Pure winner / tie-breaker resolver (Story 9-2):
        /// 0 survivors -&gt; DRAW; 1 survivor -&gt; Winner; 2+ survivors -&gt; highest (CurrentHP DESC, Kills DESC) or DRAW on tie.
        /// </summary>
        public static PlayerRef ResolveWinnerOrDraw(ReadOnlySpan<SurvivorCandidate> survivors)
        {
            if (survivors.Length == 0) return PlayerRef.None;
            if (survivors.Length == 1) return survivors[0].Player;

            SurvivorCandidate best = survivors[0];
            bool isTiedAtTop = false;

            for (int i = 1; i < survivors.Length; i++)
            {
                int cmp = CompareSurvivors(in survivors[i], in best);
                if (cmp > 0)
                {
                    best = survivors[i];
                    isTiedAtTop = false;
                }
                else if (cmp == 0)
                {
                    isTiedAtTop = true;
                }
            }

            return isTiedAtTop ? PlayerRef.None : best.Player;
        }

        /// <summary>
        /// Elects the deterministic next MasterClient with the minimum active <c>PlayerId</c> excluding <paramref name="leavingPlayer"/> (Story 9-3).
        /// </summary>
        public static PlayerRef SelectDeterministicNextMaster(
            ReadOnlySpan<PlayerRef> activePlayers,
            PlayerRef leavingPlayer)
        {
            PlayerRef bestCandidate = PlayerRef.None;
            int lowestKey = int.MaxValue;

            for (int i = 0; i < activePlayers.Length; i++)
            {
                PlayerRef candidate = activePlayers[i];
                if (candidate == PlayerRef.None || candidate == leavingPlayer)
                {
                    continue;
                }

                int key = GetSortablePlayerKey(candidate);
                if (key < lowestKey)
                {
                    lowestKey = key;
                    bestCandidate = candidate;
                }
            }

            return bestCandidate;
        }

        /// <summary>Counts living combatants currently in the roster.</summary>
        public int CountLivingPlayers()
        {
            int alive = 0;
            if (IsSpawnedOnRunner)
            {
                foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in MatchRoster)
                {
                    if (kvp.Value.IsAlive) alive++;
                }

                return alive;
            }

            foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in _localRoster)
            {
                if (kvp.Value.IsAlive) alive++;
            }

            return alive;
        }

        /// <summary>Counts connected players currently registered in the roster.</summary>
        public int CountConnectedPlayers()
        {
            if (IsSpawnedOnRunner)
            {
                return MatchRoster.Count;
            }

            return _localRoster.Count;
        }

        /// <summary>Retrieves a player's current <see cref="PlayerMatchStats"/> snapshot if present.</summary>
        public bool TryGetPlayerStats(PlayerRef player, out PlayerMatchStats stats)
        {
            return IsSpawnedOnRunner
                ? MatchRoster.TryGet(player, out stats)
                : _localRoster.TryGetValue(player, out stats);
        }

        private bool CheckIsCombatAllowed() => IsCombatAllowed;

        private void RemoveStaleNetworkedRosterEntries(ReadOnlySpan<PlayerRef> activePlayers, PlayerRef explicitLeaving)
        {
            int staleCount = 0;
            foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in MatchRoster)
            {
                if ((kvp.Key == explicitLeaving || !ContainsPlayer(activePlayers, kvp.Key)) && staleCount < _staleKeyBuffer.Length)
                {
                    _staleKeyBuffer[staleCount++] = kvp.Key;
                }
            }

            for (int i = 0; i < staleCount; i++)
            {
                MatchRoster.Remove(_staleKeyBuffer[i]);
            }
        }

        private void RemoveStaleLocalRosterEntries(ReadOnlySpan<PlayerRef> activePlayers, PlayerRef explicitLeaving)
        {
            int staleCount = 0;
            foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in _localRoster)
            {
                if ((kvp.Key == explicitLeaving || !ContainsPlayer(activePlayers, kvp.Key)) && staleCount < _staleKeyBuffer.Length)
                {
                    _staleKeyBuffer[staleCount++] = kvp.Key;
                }
            }

            for (int i = 0; i < staleCount; i++)
            {
                _localRoster.Remove(_staleKeyBuffer[i]);
                _standaloneHpOverrides.Remove(_staleKeyBuffer[i]);
            }
        }

        private void EvaluatePostFailoverTransition(int remainingConnected)
        {
            if (CurrentMatchState == MatchState.Countdown && remainingConnected < ActiveMinPlayersToStart)
            {
                TransitionToState(MatchState.WaitingForPlayers);
            }
            else if (CurrentMatchState == MatchState.Playing)
            {
                EvaluateWinConditions(isTimeoutSuddenDeath: false);
            }
        }

        private void StepAuthoritativeStateMachine(bool timerExpired)
        {
            MatchState cur = CurrentMatchState;
            int connected = CountConnectedPlayers();
            int alive = CountLivingPlayers();

            if (cur == MatchState.Playing)
            {
                if (alive <= 1 || timerExpired)
                {
                    EvaluateWinConditions(isTimeoutSuddenDeath: timerExpired && alive > 1);
                }

                return;
            }

            MatchState next = EvaluateStateTransition(cur, connected, alive, timerExpired, ActiveMinPlayersToStart);
            if (next != cur)
            {
                if (cur == MatchState.Ended && next != MatchState.Ended)
                {
                    AdvanceMatchSequenceAndResetPlayers();
                }

                TransitionToState(next);
            }
        }

        private void TransitionToState(MatchState nextState)
        {
            _localState = nextState;
            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                NetworkedMatchState = nextState;
            }

            switch (nextState)
            {
                case MatchState.WaitingForPlayers:
                    SetPhaseTimer(0f, 0);
                    break;
                case MatchState.Countdown:
                    _hasDispatchedEndedForCurrentMatch = false;
                    ResetAllConnectedRosterToAlive();
                    SetPhaseTimer(ActiveCountdownDurationSeconds, ActiveCountdownDurationTicks);
                    break;
                case MatchState.Playing:
                    _hasDispatchedEndedForCurrentMatch = false;
                    ResetAllConnectedRosterToAlive();
                    SetPhaseTimer(ActivePlayingDurationSeconds, ActivePlayingDurationTicks);
                    break;
                case MatchState.Ended:
                    SetPhaseTimer(ActiveEndedDurationSeconds, ActiveEndedDurationTicks);
                    break;
            }

            ApplyPresentationStateChange(nextState);
        }

        private void ConcludeMatchWithWinner(PlayerRef winner)
        {
            _localWinner = winner;
            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                NetworkedWinnerPlayer = winner;
            }

            if (winner != PlayerRef.None)
            {
                SetPlayerAliveAndRank(winner, isAlive: true, placementRank: 1);
            }

            TransitionToState(MatchState.Ended);
        }

        private void ApplyPresentationStateChange(MatchState state)
        {
            bool comingFromEnded = _lastPresentationState == MatchState.Ended && state != MatchState.Ended;
            _lastPresentationState = state;

            if (comingFromEnded || state == MatchState.Countdown)
            {
                if (comingFromEnded)
                {
                    _hasDispatchedEndedForCurrentMatch = false;
                }

                RestoreOwnedAvatarsForRematch();
            }

            bool isEnded = state == MatchState.Ended;
            _focusService?.SetFocusFlag(InputFocusFlag.MatchEnded, isEnded);
            BindRegisteredAvatarsNonAlloc();
            EmitRosterCounts();
            OnMatchStateChanged?.Invoke(state);

            if (isEnded && !_hasDispatchedEndedForCurrentMatch)
            {
                _hasDispatchedEndedForCurrentMatch = true;
                OnMatchEnded?.Invoke(BuildMatchResultSummary());
            }
        }

        private MatchResultSummary BuildMatchResultSummary()
        {
            PlayerRef winner = WinnerPlayer;
            bool isDraw = winner == PlayerRef.None;
            string winnerName = ResolveWinnerDisplayName(winner, isDraw);
            ResolveLocalStats(winner, isDraw, out bool didWin, out int localKills, out int localRank);
            return new MatchResultSummary(CurrentMatchId, winner, winnerName, isDraw, didWin, localKills, localRank);
        }

        private string ResolveWinnerDisplayName(PlayerRef winner, bool isDraw)
        {
            if (isDraw)
            {
                return "DRAW";
            }

            if (TryGetPlayerStats(winner, out PlayerMatchStats winStats))
            {
                string name = winStats.DisplayName.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return $"Player_{GetSortablePlayerKey(winner)}";
        }

        private void ResolveLocalStats(PlayerRef winner, bool isDraw, out bool didWin, out int localKills, out int localRank)
        {
            didWin = !isDraw && _localPlayerRef != PlayerRef.None && winner == _localPlayerRef;
            localKills = 0;
            localRank = didWin ? 1 : 2;

            if (_localPlayerRef != PlayerRef.None && TryGetPlayerStats(_localPlayerRef, out PlayerMatchStats myStats))
            {
                localKills = myStats.Kills;
                if (myStats.PlacementRank > 0)
                {
                    localRank = myStats.PlacementRank;
                }
            }
        }

        private void SetPhaseTimer(float durationSeconds, int standaloneTicks)
        {
            _localPhaseTicksRemaining = Mathf.Max(0, standaloneTicks);
            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                PhaseTimer = durationSeconds > 0f
                    ? TickTimer.CreateFromSeconds(Runner, durationSeconds)
                    : TickTimer.None;
            }
        }

        private void AdvanceMatchSequenceAndResetPlayers()
        {
            _localWinner = PlayerRef.None;
            _localMatchSequence++;
            _cachedMatchIdSequence = -1;
            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                NetworkedWinnerPlayer = PlayerRef.None;
                NetworkedMatchSequence = _localMatchSequence;
            }

            ResetAllConnectedRosterToAlive();
            RestoreOwnedAvatarsForRematch();
        }

        private void RestoreOwnedAvatarsForRematch()
        {
            int count = DamageReceiverRegistry.CopyTypedReceiversNonAlloc(_avatarScratchList);
            for (int i = 0; i < count; i++)
            {
                PlayerHealth health = _avatarScratchList[i];
                if (health == null)
                {
                    continue;
                }

                health.ResetHealthToFull();
                if (!health.IsDead && health.TryGetComponent(out PlayerMobilityController mobility))
                {
                    mobility.RespawnForRematch();
                }
            }
        }

        private void ResetAllConnectedRosterToAlive()
        {
            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                int keyCount = CollectRosterKeysSorted(_staleKeyBuffer);
                for (int i = 0; i < keyCount; i++)
                {
                    PlayerRef key = _staleKeyBuffer[i];
                    if (MatchRoster.TryGet(key, out PlayerMatchStats s))
                    {
                        s.IsAlive = true;
                        s.Kills = 0;
                        s.PlacementRank = 0;
                        MatchRoster.Set(key, s);
                    }
                }
            }
            else
            {
                int keyCount = CollectRosterKeysSorted(_staleKeyBuffer);
                for (int i = 0; i < keyCount; i++)
                {
                    PlayerRef key = _staleKeyBuffer[i];
                    PlayerMatchStats s = _localRoster[key];
                    s.IsAlive = true;
                    s.Kills = 0;
                    s.PlacementRank = 0;
                    _localRoster[key] = s;
                    _standaloneHpOverrides[key] = 100;
                }
            }

            EmitRosterCounts();
        }

        private void SyncActiveRunnerPlayersIntoRoster()
        {
            if (!IsSpawnedOnRunner || !Object.HasStateAuthority)
            {
                return;
            }

            int maxSlots = ActiveMaxPlayersPerMatch;
            foreach (PlayerRef p in Runner.ActivePlayers)
            {
                if (!MatchRoster.ContainsKey(p) && MatchRoster.Count < maxSlots)
                {
                    bool joinAlive = CanJoinCurrentMatchAlive();
                    MatchRoster.Add(p, new PlayerMatchStats
                    {
                        IsAlive = joinAlive,
                        Kills = 0,
                        PlacementRank = joinAlive ? 0 : maxSlots,
                        DisplayName = $"Player_{GetSortablePlayerKey(p)}"
                    });
                }
            }
        }

        private void ApplyAuthoritativeDisplayName(PlayerRef player, string displayName)
        {
            if (!IsSpawnedOnRunner || !Object.HasStateAuthority)
            {
                return;
            }

            if (MatchRoster.TryGet(player, out PlayerMatchStats stats))
            {
                stats.DisplayName = displayName;
                MatchRoster.Set(player, stats);
            }
        }

        private void MarkPlayerDisconnectedInStandaloneRoster(PlayerRef leavingPlayer)
        {
            _localRoster.Remove(leavingPlayer);
            _standaloneHpOverrides.Remove(leavingPlayer);
            EmitRosterCounts();
            EvaluatePostFailoverTransition(CountConnectedPlayers());
        }

        private int GatherLivingCandidatesNonAlloc(SurvivorCandidate[] buffer)
        {
            int count = 0;
            if (IsSpawnedOnRunner)
            {
                foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in MatchRoster)
                {
                    if (!kvp.Value.IsAlive || count >= buffer.Length) continue;
                    int hp = ResolveLivePlayerHp(kvp.Key);
                    if (hp > 0) buffer[count++] = new SurvivorCandidate(kvp.Key, hp, kvp.Value.Kills);
                }

                return count;
            }

            foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in _localRoster)
            {
                if (!kvp.Value.IsAlive || count >= buffer.Length) continue;
                int hp = ResolveLivePlayerHp(kvp.Key);
                if (hp > 0) buffer[count++] = new SurvivorCandidate(kvp.Key, hp, kvp.Value.Kills);
            }

            return count;
        }

        private int ResolveLivePlayerHp(PlayerRef player)
        {
            if (!IsSpawnedOnRunner && _standaloneHpOverrides.TryGetValue(player, out int standaloneHp))
            {
                return standaloneHp;
            }

            if (DamageReceiverRegistry.TryGetByPlayer(player, out IDamageReceiver receiver) &&
                receiver is PlayerHealth liveHealth &&
                liveHealth != null)
            {
                return liveHealth.CurrentHP;
            }

            return _standaloneHpOverrides.TryGetValue(player, out int hp) ? hp : 100;
        }

        private void SetPlayerAliveAndRank(PlayerRef player, bool isAlive, int placementRank)
        {
            if (IsSpawnedOnRunner && Object.HasStateAuthority && MatchRoster.TryGet(player, out PlayerMatchStats netStats))
            {
                netStats.IsAlive = isAlive;
                netStats.PlacementRank = placementRank;
                MatchRoster.Set(player, netStats);
            }
            else if (_localRoster.TryGetValue(player, out PlayerMatchStats localStats))
            {
                localStats.IsAlive = isAlive;
                localStats.PlacementRank = placementRank;
                _localRoster[player] = localStats;
            }
        }

        private void IncrementPlayerKills(PlayerRef killer)
        {
            if (IsSpawnedOnRunner && Object.HasStateAuthority && MatchRoster.TryGet(killer, out PlayerMatchStats netStats))
            {
                netStats.Kills += 1;
                MatchRoster.Set(killer, netStats);
            }
            else if (_localRoster.TryGetValue(killer, out PlayerMatchStats localStats))
            {
                localStats.Kills += 1;
                _localRoster[killer] = localStats;
            }
        }

        private int CopyRunnerActivePlayersNonAlloc(PlayerRef[] buffer)
        {
            if (!IsSpawnedOnRunner) return 0;

            int written = 0;
            foreach (PlayerRef p in Runner.ActivePlayers)
            {
                if (written < buffer.Length) buffer[written++] = p;
            }

            return written;
        }

        private int CollectRosterKeysSorted(PlayerRef[] buffer)
        {
            int count = 0;
            if (IsSpawnedOnRunner)
            {
                foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in MatchRoster)
                {
                    if (count < buffer.Length) buffer[count++] = kvp.Key;
                }
            }
            else
            {
                foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in _localRoster)
                {
                    if (count < buffer.Length) buffer[count++] = kvp.Key;
                }
            }

            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (GetSortablePlayerKey(buffer[j]) < GetSortablePlayerKey(buffer[i]))
                    {
                        (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
                    }
                }
            }

            return count;
        }

        private void HandleAvatarRegistered(IDamageReceiver receiver)
        {
            if (receiver is PlayerHealth health && health != null)
            {
                BindSingleAvatarPredicates(health);
            }
        }

        private void BindRegisteredAvatarsNonAlloc()
        {
            int count = DamageReceiverRegistry.CopyTypedReceiversNonAlloc(_avatarScratchList);
            for (int i = 0; i < count; i++)
            {
                BindSingleAvatarPredicates(_avatarScratchList[i]);
            }
        }

        private void BindSingleAvatarPredicates(PlayerHealth health)
        {
            if (health == null)
            {
                return;
            }

            _cachedCombatAllowedPredicate ??= CheckIsCombatAllowed;
            health.BindMatchPlayingPredicate(_cachedCombatAllowedPredicate);
            if (health.TryGetComponent(out PlayerCombatController combat))
            {
                combat.BindMatchPlayingPredicate(_cachedCombatAllowedPredicate);
            }
        }

        private void HandleKillConfirmed(PlayerRef killer, PlayerRef victim)
        {
            if (IsSpawnedOnRunner)
            {
                if (Object.HasStateAuthority)
                {
                    RecordElimination(killer, victim, evaluateImmediately: false);
                }

                return;
            }

            RecordElimination(killer, victim, evaluateImmediately: true);
        }

        private void EmitRosterCounts()
        {
            OnRosterCountsChanged?.Invoke(CountLivingPlayers(), CountConnectedPlayers());
        }

        private static bool ContainsPlayer(ReadOnlySpan<PlayerRef> span, PlayerRef target)
        {
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == target) return true;
            }

            return false;
        }

        private static int CompareSurvivors(in SurvivorCandidate a, in SurvivorCandidate b)
        {
            return a.CurrentHP != b.CurrentHP
                ? a.CurrentHP.CompareTo(b.CurrentHP)
                : a.Kills.CompareTo(b.Kills);
        }

        private static int GetSortablePlayerKey(PlayerRef player)
        {
            return player.PlayerId > 0 ? player.PlayerId : Mathf.Abs(player.RawEncoded);
        }
    }
}
