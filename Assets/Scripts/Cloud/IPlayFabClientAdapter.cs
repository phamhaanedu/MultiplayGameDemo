using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CapsuleArena.Cloud
{
    /// <summary>
    /// Lifecycle state of the non-blocking PlayFab CustomID authentication service (Story 13-1; ADR-0008; TR-playfab-001).
    /// </summary>
    public enum AuthState : byte
    {
        /// <summary>Service has not yet attempted authentication.</summary>
        Uninitialized = 0,

        /// <summary>Authentication request or exponential backoff retry is in flight.</summary>
        Authenticating = 1,

        /// <summary>Valid PlayFab session ticket acquired.</summary>
        Authenticated = 2,

        /// <summary>Offline or unconfigured TitleId fallback; multiplayer rooms remain 100% unlocked.</summary>
        OfflineGuest = 3
    }

    /// <summary>
    /// Standardized result wrapper returned by <see cref="IPlayFabClientAdapter"/> operations.
    /// </summary>
    /// <typeparam name="T">Payload type on HTTP 200 success.</typeparam>
    public readonly struct PlayFabApiResponse<T>
    {
        /// <summary>Gets a value indicating whether the operation succeeded with HTTP 200 OK.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets the HTTP status code (200 OK, 400 Bad Request, 429 Too Many Requests, 503 Unavailable, 0 Timeout/Network).</summary>
        public int HttpStatusCode { get; }

        /// <summary>Gets the PlayFab symbolic error code (e.g., "InvalidTitleId", "ServiceUnavailable", "NetworkTimeout").</summary>
        public string ErrorCode { get; }

        /// <summary>Gets the human-readable diagnostic message.</summary>
        public string ErrorMessage { get; }

        /// <summary>Gets the typed response payload when <see cref="IsSuccess"/> is true.</summary>
        public T Data { get; }

        /// <summary>Initializes an immutable <see cref="PlayFabApiResponse{T}"/>.</summary>
        public PlayFabApiResponse(bool isSuccess, int httpStatusCode, string errorCode, string errorMessage, T data)
        {
            IsSuccess = isSuccess;
            HttpStatusCode = httpStatusCode;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            Data = data;
        }

        /// <summary>Creates a successful HTTP 200 response.</summary>
        public static PlayFabApiResponse<T> Ok(T data)
        {
            return new PlayFabApiResponse<T>(true, 200, string.Empty, string.Empty, data);
        }

        /// <summary>Creates a failed HTTP/network response.</summary>
        public static PlayFabApiResponse<T> Fail(int httpStatusCode, string errorCode, string errorMessage)
        {
            return new PlayFabApiResponse<T>(false, httpStatusCode, errorCode, errorMessage, default);
        }
    }

    /// <summary>
    /// Encapsulated result payload returned by <c>LoginWithCustomID</c>.
    /// </summary>
    [Serializable]
    public struct PlayFabLoginResult
    {
        [SerializeField] private string _playFabId;
        [SerializeField] private string _sessionTicket;
        [SerializeField] private string _displayName;

        /// <summary>Gets or sets the unique PlayFab player identifier.</summary>
        public string PlayFabId
        {
            get => _playFabId ?? string.Empty;
            set => _playFabId = value ?? string.Empty;
        }

        /// <summary>Gets or sets the session ticket used as <c>X-Authorization</c> header for subsequent Client API calls.</summary>
        public string SessionTicket
        {
            get => _sessionTicket ?? string.Empty;
            set => _sessionTicket = value ?? string.Empty;
        }

        /// <summary>Gets or sets the synchronized player display name if already set on PlayFab.</summary>
        public string DisplayName
        {
            get => _displayName ?? string.Empty;
            set => _displayName = value ?? string.Empty;
        }
    }

    /// <summary>
    /// Encapsulated serializable batched match statistics delta payload persisted in the 1-slot offline queue (Story 13-2; TR-playfab-002).
    /// </summary>
    [Serializable]
    public struct StatSubmissionPayload
    {
        [SerializeField] private string _matchId;
        [SerializeField] private int _winDelta;
        [SerializeField] private int _killsDelta;
        [SerializeField] private int _matchesPlayedDelta;

        /// <summary>Gets or sets the deterministic match identifier preventing duplicate submissions.</summary>
        public string MatchId
        {
            get => _matchId ?? string.Empty;
            set => _matchId = value ?? string.Empty;
        }

        /// <summary>Gets or sets the clamped win delta (0 or 1).</summary>
        public int WinDelta
        {
            get => _winDelta;
            set => _winDelta = value;
        }

        /// <summary>Gets or sets the clamped match kills delta ([0, 15]).</summary>
        public int KillsDelta
        {
            get => _killsDelta;
            set => _killsDelta = value;
        }

        /// <summary>Gets or sets the matches played delta (always 1).</summary>
        public int MatchesPlayedDelta
        {
            get => _matchesPlayedDelta;
            set => _matchesPlayedDelta = value;
        }

        /// <summary>Gets a value indicating whether this struct holds an unsent pending match payload.</summary>
        public bool IsValid => !string.IsNullOrEmpty(_matchId) && _matchesPlayedDelta > 0;
    }

    /// <summary>
    /// Encapsulated single leaderboard entry returned by <c>GetLeaderboard</c> (Story 13-3; TR-playfab-003).
    /// </summary>
    [Serializable]
    public struct LeaderboardEntryDto
    {
        [SerializeField] private int _position;
        [SerializeField] private string _playFabId;
        [SerializeField] private string _displayName;
        [SerializeField] private int _statValue;

        /// <summary>Gets or sets the zero-based leaderboard position (0 = Rank #1).</summary>
        public int Position
        {
            get => _position;
            set => _position = value;
        }

        /// <summary>Gets or sets the unique PlayFab player identifier.</summary>
        public string PlayFabId
        {
            get => _playFabId ?? string.Empty;
            set => _playFabId = value ?? string.Empty;
        }

        /// <summary>Gets or sets the sanitized player display name.</summary>
        public string DisplayName
        {
            get => _displayName ?? string.Empty;
            set => _displayName = value ?? string.Empty;
        }

        /// <summary>Gets or sets the aggregated statistic score (TotalWins or TotalKills).</summary>
        public int StatValue
        {
            get => _statValue;
            set => _statValue = value;
        }
    }

    /// <summary>
    /// Encapsulated persistent identity + 1-slot offline pending stats record stored atomically in <c>playfab_identity.json</c>.
    /// </summary>
    [Serializable]
    public sealed class PlayFabIdentitySaveData
    {
        [SerializeField] private string _customId = string.Empty;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private string _lastSubmittedMatchId = string.Empty;
        [SerializeField] private int _localTotalWins;
        [SerializeField] private int _localTotalKills;
        [SerializeField] private int _localMatchesPlayed;
        [SerializeField] private bool _hasPendingSubmission;
        [SerializeField] private StatSubmissionPayload _pendingSubmission;

        /// <summary>Gets or sets the persistent 32-character hex CustomID generated once per install/profile.</summary>
        public string CustomId
        {
            get => _customId ?? string.Empty;
            set => _customId = value ?? string.Empty;
        }

        /// <summary>Gets or sets the sanitized 3-16 character player display name.</summary>
        public string DisplayName
        {
            get => _displayName ?? string.Empty;
            set => _displayName = value ?? string.Empty;
        }

        /// <summary>Gets or sets the last successfully submitted deterministic MatchId.</summary>
        public string LastSubmittedMatchId
        {
            get => _lastSubmittedMatchId ?? string.Empty;
            set => _lastSubmittedMatchId = value ?? string.Empty;
        }

        /// <summary>Gets or sets cumulative local wins tracked for sticky footer and offline display.</summary>
        public int LocalTotalWins
        {
            get => _localTotalWins;
            set => _localTotalWins = value;
        }

        /// <summary>Gets or sets cumulative local kills tracked for sticky footer and offline display.</summary>
        public int LocalTotalKills
        {
            get => _localTotalKills;
            set => _localTotalKills = value;
        }

        /// <summary>Gets or sets cumulative local matches played tracked for sticky footer and offline display.</summary>
        public int LocalMatchesPlayed
        {
            get => _localMatchesPlayed;
            set => _localMatchesPlayed = value;
        }

        /// <summary>Gets or sets a value indicating whether <see cref="PendingSubmission"/> holds an unsent match result.</summary>
        public bool HasPendingSubmission
        {
            get => _hasPendingSubmission;
            set => _hasPendingSubmission = value;
        }

        /// <summary>Gets or sets the single-slot pending match stats delta queued while offline or on HTTP failure.</summary>
        public StatSubmissionPayload PendingSubmission
        {
            get => _pendingSubmission;
            set => _pendingSubmission = value;
        }
    }

    /// <summary>
    /// Dependency-inverted PlayFab Client API adapter enabling deterministic offline/mock unit testing
    /// without live network calls (ADR-0008; TR-playfab-001..003).
    /// </summary>
    public interface IPlayFabClientAdapter
    {
        /// <summary>Authenticates or creates a player account via PlayFab <c>/Client/LoginWithCustomID</c>.</summary>
        Task<PlayFabApiResponse<PlayFabLoginResult>> LoginWithCustomIdAsync(string titleId, string customId);

        /// <summary>Synchronizes the player's sanitized display name via PlayFab <c>/Client/UpdateUserTitleDisplayName</c>.</summary>
        Task<PlayFabApiResponse<bool>> UpdateUserTitleDisplayNameAsync(string sessionTicket, string displayName);

        /// <summary>Submits batched statistic deltas via PlayFab <c>/Client/UpdatePlayerStatistics</c>.</summary>
        Task<PlayFabApiResponse<bool>> UpdatePlayerStatisticsAsync(string sessionTicket, StatSubmissionPayload payload);

        /// <summary>Fetches up to <paramref name="maxResults"/> entries via PlayFab <c>/Client/GetLeaderboard</c>.</summary>
        Task<PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>> GetLeaderboardAsync(
            string sessionTicket,
            string statisticName,
            int maxResults = 20);
    }

    /// <summary>
    /// Lightweight <see cref="UnityWebRequest"/> HTTPS adapter targeting PlayFab Client REST endpoints
    /// with a strict 5-second timeout and immediate 400 validation when <c>TitleId</c> is unconfigured.
    /// </summary>
    public sealed class PlayFabRestClientAdapter : IPlayFabClientAdapter
    {
        /// <summary>HTTP request timeout in whole seconds (5s).</summary>
        public const int RequestTimeoutSeconds = 5;

        private readonly string _titleId;

        /// <summary>Gets the configured PlayFab TitleId bound to this REST adapter.</summary>
        public string BoundTitleId => _titleId;

        /// <summary>Initializes a REST client adapter bound to <paramref name="titleId"/>.</summary>
        public PlayFabRestClientAdapter(string titleId)
        {
            _titleId = titleId != null ? titleId.Trim() : string.Empty;
        }

        /// <inheritdoc />
        public async Task<PlayFabApiResponse<PlayFabLoginResult>> LoginWithCustomIdAsync(string titleId, string customId)
        {
            string effectiveTitle = !string.IsNullOrWhiteSpace(titleId) ? titleId.Trim() : _titleId;
            if (!IsValidTitleId(effectiveTitle))
            {
                return PlayFabApiResponse<PlayFabLoginResult>.Fail(400, "InvalidTitleId", "PlayFab TitleId is not configured.");
            }

            string body = $"{{\"TitleId\":\"{EscapeJson(effectiveTitle)}\",\"CustomId\":\"{EscapeJson(customId)}\",\"CreateAccount\":true}}";
            PlayFabApiResponse<string> raw = await PostJsonAsync(effectiveTitle, "/Client/LoginWithCustomID", null, body);
            if (!raw.IsSuccess)
            {
                return PlayFabApiResponse<PlayFabLoginResult>.Fail(raw.HttpStatusCode, raw.ErrorCode, raw.ErrorMessage);
            }

            PlayFabLoginResult parsed = ParseLoginJson(raw.Data);
            return PlayFabApiResponse<PlayFabLoginResult>.Ok(parsed);
        }

        /// <inheritdoc />
        public async Task<PlayFabApiResponse<bool>> UpdateUserTitleDisplayNameAsync(string sessionTicket, string displayName)
        {
            if (string.IsNullOrEmpty(sessionTicket) || !IsValidTitleId(_titleId))
            {
                return PlayFabApiResponse<bool>.Fail(401, "NotAuthenticated", "Missing PlayFab SessionTicket.");
            }

            string body = $"{{\"DisplayName\":\"{EscapeJson(displayName)}\"}}";
            PlayFabApiResponse<string> raw = await PostJsonAsync(_titleId, "/Client/UpdateUserTitleDisplayName", sessionTicket, body);
            return raw.IsSuccess
                ? PlayFabApiResponse<bool>.Ok(true)
                : PlayFabApiResponse<bool>.Fail(raw.HttpStatusCode, raw.ErrorCode, raw.ErrorMessage);
        }

        /// <inheritdoc />
        public async Task<PlayFabApiResponse<bool>> UpdatePlayerStatisticsAsync(string sessionTicket, StatSubmissionPayload payload)
        {
            if (string.IsNullOrEmpty(sessionTicket) || !IsValidTitleId(_titleId))
            {
                return PlayFabApiResponse<bool>.Fail(401, "NotAuthenticated", "Missing PlayFab SessionTicket.");
            }

            string body = BuildStatisticsJson(payload);
            PlayFabApiResponse<string> raw = await PostJsonAsync(_titleId, "/Client/UpdatePlayerStatistics", sessionTicket, body);
            return raw.IsSuccess
                ? PlayFabApiResponse<bool>.Ok(true)
                : PlayFabApiResponse<bool>.Fail(raw.HttpStatusCode, raw.ErrorCode, raw.ErrorMessage);
        }

        /// <inheritdoc />
        public async Task<PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>> GetLeaderboardAsync(
            string sessionTicket,
            string statisticName,
            int maxResults = 20)
        {
            if (string.IsNullOrEmpty(sessionTicket) || !IsValidTitleId(_titleId))
            {
                return PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Fail(401, "NotAuthenticated", "Missing SessionTicket.");
            }

            int clampedMax = Mathf.Clamp(maxResults, 1, 20);
            string body = $"{{\"StatisticName\":\"{EscapeJson(statisticName)}\",\"StartPosition\":0,\"MaxResultsCount\":{clampedMax}}}";
            PlayFabApiResponse<string> raw = await PostJsonAsync(_titleId, "/Client/GetLeaderboard", sessionTicket, body);
            if (!raw.IsSuccess)
            {
                return PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Fail(raw.HttpStatusCode, raw.ErrorCode, raw.ErrorMessage);
            }

            return PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Ok(ParseLeaderboardJson(raw.Data));
        }

        private static bool IsValidTitleId(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId) || titleId.Length < 4 || titleId.Length > 16)
            {
                return false;
            }

            for (int i = 0; i < titleId.Length; i++)
            {
                if (!char.IsLetterOrDigit(titleId[i]))
                {
                    return false;
                }
            }

            return !string.Equals(titleId, "UNCONFIGURED", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<PlayFabApiResponse<string>> PostJsonAsync(
            string titleId,
            string endpointPath,
            string sessionTicket,
            string jsonBody)
        {
            string url = $"https://{titleId}.playfabapi.com{endpointPath}";
            using UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(payloadBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(sessionTicket))
            {
                req.SetRequestHeader("X-Authorization", sessionTicket);
            }

            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }

            int code = (int)req.responseCode;
            if (req.result == UnityWebRequest.Result.Success && code == 200)
            {
                return PlayFabApiResponse<string>.Ok(req.downloadHandler.text);
            }

            string errCode = code == 0 ? "NetworkTimeout" : $"HTTP_{code}";
            return PlayFabApiResponse<string>.Fail(code, errCode, req.error ?? "PlayFab HTTP request failed.");
        }

        private static string BuildStatisticsJson(StatSubmissionPayload p)
        {
            return $"{{\"Statistics\":[" +
                   $"{{\"StatisticName\":\"TotalWins\",\"Value\":{p.WinDelta}}}," +
                   $"{{\"StatisticName\":\"TotalKills\",\"Value\":{p.KillsDelta}}}," +
                   $"{{\"StatisticName\":\"MatchesPlayed\",\"Value\":{p.MatchesPlayedDelta}}}]}}";
        }

        private static PlayFabLoginResult ParseLoginJson(string json)
        {
            return new PlayFabLoginResult
            {
                PlayFabId = ExtractJsonString(json, "PlayFabId"),
                SessionTicket = ExtractJsonString(json, "SessionTicket"),
                DisplayName = ExtractJsonString(json, "DisplayName")
            };
        }

        private static List<LeaderboardEntryDto> ParseLeaderboardJson(string json)
        {
            var list = new List<LeaderboardEntryDto>(20);
            if (string.IsNullOrEmpty(json))
            {
                return list;
            }

            int searchIdx = 0;
            while (list.Count < 20)
            {
                int posKey = json.IndexOf("\"Position\":", searchIdx, StringComparison.Ordinal);
                if (posKey < 0)
                {
                    break;
                }

                int nextBrace = json.IndexOf('}', posKey);
                if (nextBrace < 0)
                {
                    break;
                }

                string slice = json.Substring(posKey, nextBrace - posKey + 1);
                list.Add(new LeaderboardEntryDto
                {
                    Position = ExtractJsonInt(slice, "Position"),
                    PlayFabId = ExtractJsonString(slice, "PlayFabId"),
                    DisplayName = ExtractJsonString(slice, "DisplayName"),
                    StatValue = ExtractJsonInt(slice, "StatValue")
                });
                searchIdx = nextBrace + 1;
            }

            return list;
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return string.Empty;
            }

            string token = $"\"{key}\":\"";
            int start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            start += token.Length;
            int end = json.IndexOf('"', start);
            return end > start ? json.Substring(start, end - start) : string.Empty;
        }

        private static int ExtractJsonInt(string json, string key)
        {
            string token = $"\"{key}\":";
            int start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
            {
                return 0;
            }

            start += token.Length;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
            {
                end++;
            }

            return end > start && int.TryParse(json.Substring(start, end - start), out int val) ? val : 0;
        }

        private static string EscapeJson(string raw)
        {
            return string.IsNullOrEmpty(raw) ? string.Empty : raw.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
