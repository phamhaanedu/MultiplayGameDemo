using System;
using System.Threading.Tasks;
using CapsuleArena.Gameplay.Match;
using UnityEngine;

namespace CapsuleArena.Cloud
{
    /// <summary>
    /// Submits batched match statistics (<c>TotalWins</c>, <c>TotalKills</c>, <c>MatchesPlayed</c>) strictly once on
    /// <see cref="MatchManager.OnMatchEnded"/> (<c>0</c> API calls during <c>MatchState.Playing</c>), guarded by
    /// <c>_lastSubmittedMatchId</c> idempotency, <c>[0, 15]</c> kill clamping, <c>1s, 2s, 4s</c> exponential backoff
    /// retry on transient errors, and a persistent 1-slot offline queue in <c>playfab_identity.json</c> that
    /// auto-flushes upon authentication (Story 13-2; ADR-0008; TR-playfab-002).
    /// </summary>
    public sealed class PlayFabStatsService : MonoBehaviour
    {
        /// <summary>Maximum plausible kills in a single 4-player match for sanity clamping (15).</summary>
        public const int MaxClampedMatchKills = 15;

        [Header("Dependencies")]
        [SerializeField] private PlayFabAuthService _authService;
        [SerializeField] private PlayFabLeaderboardService _leaderboardService;
        [SerializeField] private MatchManager _matchManager;

        private IPlayFabClientAdapter _clientAdapter;
        private string _lastSubmittedMatchId = string.Empty;
        private int _totalApiSubmissionsSent;

        /// <summary>Raised when a match statistics batch is accepted by PlayFab (<c>HTTP 200 OK</c>).</summary>
        public event Action<StatSubmissionPayload> OnStatsSubmittedSuccess;

        /// <summary>Gets the deterministic MatchId of the most recently accepted or queued match submission.</summary>
        public string LastSubmittedMatchId => _lastSubmittedMatchId;

        /// <summary>Gets the total number of outbound <c>UpdatePlayerStatistics</c> API calls dispatched this session.</summary>
        public int TotalApiSubmissionsSent => _totalApiSubmissionsSent;

        /// <summary>Gets a value indicating whether an unsent match payload is currently queued in local storage.</summary>
        public bool HasPendingOfflineSubmission =>
            _authService != null && _authService.IdentityData.HasPendingSubmission;

        private void OnEnable()
        {
            BindEvents();
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        /// <summary>Injects dependencies explicitly for scene bootstrapping and EditMode unit tests.</summary>
        public void Initialize(
            PlayFabAuthService authService,
            IPlayFabClientAdapter clientAdapter,
            PlayFabLeaderboardService leaderboardService = null,
            MatchManager matchManager = null)
        {
            UnbindEvents();
            _authService = authService;
            _clientAdapter = clientAdapter;
            _leaderboardService = leaderboardService;
            _matchManager = matchManager;

            if (_authService != null)
            {
                _lastSubmittedMatchId = _authService.IdentityData.LastSubmittedMatchId;
            }

            BindEvents();
        }

        /// <summary>
        /// Validates idempotency against <c>_lastSubmittedMatchId</c> and builds a clamped
        /// <see cref="StatSubmissionPayload"/> (<c>WinDelta in {0,1}</c>, <c>KillsDelta in [0,15]</c>, <c>MatchesPlayedDelta = 1</c>).
        /// </summary>
        public bool TryBuildStatsSubmission(
            string matchId,
            bool didWin,
            int rawKills,
            out StatSubmissionPayload payload)
        {
            if (string.IsNullOrWhiteSpace(matchId) ||
                string.Equals(matchId, _lastSubmittedMatchId, StringComparison.Ordinal))
            {
                payload = default;
                return false;
            }

            payload = new StatSubmissionPayload
            {
                MatchId = matchId,
                WinDelta = didWin ? 1 : 0,
                KillsDelta = Mathf.Clamp(rawKills, 0, MaxClampedMatchKills),
                MatchesPlayedDelta = 1
            };
            return true;
        }

        /// <summary>
        /// Submits or queues the result of a completed match idempotently. Never transmits duplicate MatchIds.
        /// </summary>
        public async Task<bool> SubmitMatchResultAsync(string matchId, bool didWin, int rawKills)
        {
            if (_matchManager != null && _matchManager.CurrentMatchState == MatchState.Playing)
            {
                return false;
            }

            if (!TryBuildStatsSubmission(matchId, didWin, rawKills, out StatSubmissionPayload payload))
            {
                return false;
            }

            RecordLocalTotalsAndMarkHandled(payload);
            return await TransmitOrQueuePayloadAsync(payload);
        }

        /// <summary>Flushes any persisted 1-slot <c>PendingSubmission</c> when authenticated.</summary>
        public async Task<bool> FlushPendingSubmissionAsync()
        {
            if (_authService == null || !_authService.IsAuthenticated)
            {
                return false;
            }

            PlayFabIdentitySaveData data = _authService.IdentityData;
            if (!data.HasPendingSubmission || !data.PendingSubmission.IsValid)
            {
                return false;
            }

            StatSubmissionPayload pending = data.PendingSubmission;
            bool sent = await SendPayloadWithRetryAsync(pending);
            if (sent)
            {
                data.HasPendingSubmission = false;
                data.PendingSubmission = default;
                _authService.SaveIdentityAtomically();
            }

            return sent;
        }

        private void BindEvents()
        {
            if (_matchManager != null)
            {
                _matchManager.OnMatchEnded -= HandleMatchEnded;
                _matchManager.OnMatchEnded += HandleMatchEnded;
            }

            if (_authService != null)
            {
                _authService.OnAuthenticated -= HandleAuthenticated;
                _authService.OnAuthenticated += HandleAuthenticated;
            }
        }

        private void UnbindEvents()
        {
            if (_matchManager != null)
            {
                _matchManager.OnMatchEnded -= HandleMatchEnded;
            }

            if (_authService != null)
            {
                _authService.OnAuthenticated -= HandleAuthenticated;
            }
        }

        private void HandleMatchEnded(MatchResultSummary summary)
        {
            _ = SubmitMatchResultAsync(summary.MatchId, summary.LocalDidWin && !summary.IsDraw, summary.LocalKills);
        }

        private void HandleAuthenticated()
        {
            _ = FlushPendingSubmissionAsync();
        }

        private void RecordLocalTotalsAndMarkHandled(StatSubmissionPayload payload)
        {
            _lastSubmittedMatchId = payload.MatchId;
            if (_authService == null)
            {
                return;
            }

            PlayFabIdentitySaveData data = _authService.IdentityData;
            data.LastSubmittedMatchId = payload.MatchId;
            data.LocalTotalWins += payload.WinDelta;
            data.LocalTotalKills += payload.KillsDelta;
            data.LocalMatchesPlayed += payload.MatchesPlayedDelta;
            _authService.SaveIdentityAtomically();
        }

        private async Task<bool> TransmitOrQueuePayloadAsync(StatSubmissionPayload payload)
        {
            if (_authService != null && _authService.IsAuthenticated)
            {
                bool ok = await SendPayloadWithRetryAsync(payload);
                if (ok)
                {
                    ClearPendingSlotIfMatches(payload.MatchId);
                    return true;
                }
            }

            PersistPendingSubmission(payload);
            return false;
        }

        private async Task<bool> SendPayloadWithRetryAsync(StatSubmissionPayload payload)
        {
            IPlayFabClientAdapter adapter = ResolveEffectiveAdapter();
            string ticket = _authService != null ? _authService.SessionTicket : string.Empty;

            for (int attempt = 0; attempt <= PlayFabAuthService.MaxRetryBackoffSteps; attempt++)
            {
                _totalApiSubmissionsSent++;
                PlayFabApiResponse<bool> resp = await adapter.UpdatePlayerStatisticsAsync(ticket, payload);
                if (resp.IsSuccess)
                {
                    NotifyStatsAccepted(payload);
                    return true;
                }

                if (PlayFabAuthService.IsNonRetryableClientError(resp.HttpStatusCode) ||
                    attempt >= PlayFabAuthService.MaxRetryBackoffSteps)
                {
                    return false;
                }

                float delay = PlayFabAuthService.CalculateBackoffDelaySec(attempt);
                await WaitRetryDelayAsync(delay);
            }

            return false;
        }

        private IPlayFabClientAdapter ResolveEffectiveAdapter()
        {
            if (_clientAdapter != null)
            {
                return _clientAdapter;
            }

            return _authService != null
                ? _authService.ResolveClientAdapter()
                : new PlayFabRestClientAdapter(string.Empty);
        }

        private Task WaitRetryDelayAsync(float seconds)
        {
            return _authService != null
                ? _authService.WaitBackoffOrDefaultAsync(seconds)
                : Task.Delay(Mathf.Max(1, Mathf.RoundToInt(seconds * 1000f)));
        }

        private void NotifyStatsAccepted(StatSubmissionPayload payload)
        {
            if (_leaderboardService != null)
            {
                _leaderboardService.InvalidateCache();
            }

            OnStatsSubmittedSuccess?.Invoke(payload);
        }

        private void PersistPendingSubmission(StatSubmissionPayload payload)
        {
            if (_authService == null)
            {
                return;
            }

            PlayFabIdentitySaveData data = _authService.IdentityData;
            data.HasPendingSubmission = true;
            data.PendingSubmission = payload;
            _authService.SaveIdentityAtomically();
        }

        private void ClearPendingSlotIfMatches(string matchId)
        {
            if (_authService == null)
            {
                return;
            }

            PlayFabIdentitySaveData data = _authService.IdentityData;
            if (data.HasPendingSubmission && string.Equals(data.PendingSubmission.MatchId, matchId, StringComparison.Ordinal))
            {
                data.HasPendingSubmission = false;
                data.PendingSubmission = default;
                _authService.SaveIdentityAtomically();
            }
        }
    }
}
