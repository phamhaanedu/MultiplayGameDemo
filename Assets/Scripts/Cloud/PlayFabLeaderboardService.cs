using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CapsuleArena.Cloud
{
    /// <summary>
    /// Fetches and caches PlayFab Top-20 leaderboards (<c>TotalWins</c>, <c>TotalKills</c>) with a strict
    /// <c>15.0s</c> client-side cache TTL to prevent HTTP 429 rate-limiting on rapid tab/modal switches
    /// (Story 13-3; ADR-0008; TR-playfab-003).
    /// </summary>
    public sealed class PlayFabLeaderboardService : MonoBehaviour
    {
        /// <summary>Statistic name for cumulative Last Man Standing victories.</summary>
        public const string StatTotalWins = "TotalWins";

        /// <summary>Statistic name for cumulative confirmed eliminations.</summary>
        public const string StatTotalKills = "TotalKills";

        /// <summary>Maximum leaderboard rows requested and displayed (20).</summary>
        public const int MaxLeaderboardEntries = 20;

        /// <summary>Client-side cache time-to-live in seconds (15.0s).</summary>
        public const float LeaderboardCacheTtlSec = 15.0f;

        [Header("Dependencies")]
        [SerializeField] private PlayFabAuthService _authService;

        private sealed class CachedTab
        {
            public readonly List<LeaderboardEntryDto> Entries = new List<LeaderboardEntryDto>(MaxLeaderboardEntries);
            public float FetchedAtTimeSec = -999f;
            public bool HasValidCache;
        }

        private readonly Dictionary<string, CachedTab> _cacheByStat =
            new Dictionary<string, CachedTab>(StringComparer.Ordinal)
            {
                { StatTotalWins, new CachedTab() },
                { StatTotalKills, new CachedTab() }
            };

        private IPlayFabClientAdapter _clientAdapter;
        private int _totalLeaderboardApiCalls;

        /// <summary>Gets the total number of outbound <c>GetLeaderboard</c> API requests issued this session.</summary>
        public int TotalLeaderboardApiCalls => _totalLeaderboardApiCalls;

        /// <summary>Injects dependencies explicitly for scene bootstrapping and EditMode unit tests.</summary>
        public void Initialize(PlayFabAuthService authService, IPlayFabClientAdapter clientAdapter)
        {
            _authService = authService;
            _clientAdapter = clientAdapter;
        }

        /// <summary>
        /// Pure TTL predicate returning true when <paramref name="statName"/> holds a valid cache younger than
        /// <paramref name="cacheTtlSec"/> (<c>15.0s</c> default).
        /// </summary>
        public bool ShouldUseCachedLeaderboard(
            string statName,
            float currentTimeSec,
            float cacheTtlSec = LeaderboardCacheTtlSec)
        {
            if (string.IsNullOrEmpty(statName) || !_cacheByStat.TryGetValue(statName, out CachedTab tab))
            {
                return false;
            }

            if (!tab.HasValidCache)
            {
                return false;
            }

            return (currentTimeSec - tab.FetchedAtTimeSec) < cacheTtlSec;
        }

        /// <summary>Invalidates all cached leaderboard tabs immediately (invoked after new stats submission).</summary>
        public void InvalidateCache()
        {
            foreach (KeyValuePair<string, CachedTab> kvp in _cacheByStat)
            {
                kvp.Value.HasValidCache = false;
                kvp.Value.FetchedAtTimeSec = -999f;
            }
        }

        /// <summary>
        /// Seeds a deterministic cached tab directly (used for offline demo inspection and unit test fixtures).
        /// </summary>
        public void SeedCachedEntries(string statName, IReadOnlyList<LeaderboardEntryDto> entries, float fetchedAtTimeSec)
        {
            CachedTab tab = GetOrCreateTab(statName);
            tab.Entries.Clear();
            if (entries != null)
            {
                int count = Mathf.Min(MaxLeaderboardEntries, entries.Count);
                for (int i = 0; i < count; i++)
                {
                    tab.Entries.Add(entries[i]);
                }
            }

            tab.FetchedAtTimeSec = fetchedAtTimeSec;
            tab.HasValidCache = true;
        }

        /// <summary>
        /// Returns Top-20 entries for <paramref name="statName"/>, serving from cache when within the 15.0s TTL
        /// unless <paramref name="forceRefresh"/> is true.
        /// </summary>
        public async Task<PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>> FetchTop20Async(
            string statName,
            float currentTimeSec,
            bool forceRefresh = false)
        {
            string resolvedStat = string.IsNullOrEmpty(statName) ? StatTotalWins : statName;
            CachedTab tab = GetOrCreateTab(resolvedStat);

            if (!forceRefresh && ShouldUseCachedLeaderboard(resolvedStat, currentTimeSec, LeaderboardCacheTtlSec))
            {
                return PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Ok(tab.Entries);
            }

            if (_authService == null || !_authService.IsAuthenticated)
            {
                return PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Fail(
                    503,
                    "OfflineGuest",
                    "Leaderboard unavailable in Offline Guest Mode.");
            }

            IPlayFabClientAdapter adapter = _clientAdapter ?? _authService.ResolveClientAdapter();
            _totalLeaderboardApiCalls++;
            PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>> resp =
                await adapter.GetLeaderboardAsync(_authService.SessionTicket, resolvedStat, MaxLeaderboardEntries);

            if (!resp.IsSuccess)
            {
                return resp;
            }

            CopyEntriesIntoCache(tab, resp.Data, currentTimeSec);
            return PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Ok(tab.Entries);
        }

        private CachedTab GetOrCreateTab(string statName)
        {
            if (!_cacheByStat.TryGetValue(statName, out CachedTab tab))
            {
                tab = new CachedTab();
                _cacheByStat[statName] = tab;
            }

            return tab;
        }

        private static void CopyEntriesIntoCache(CachedTab tab, IReadOnlyList<LeaderboardEntryDto> source, float nowSec)
        {
            tab.Entries.Clear();
            if (source != null)
            {
                int count = Mathf.Min(MaxLeaderboardEntries, source.Count);
                for (int i = 0; i < count; i++)
                {
                    tab.Entries.Add(source[i]);
                }
            }

            tab.FetchedAtTimeSec = nowSec;
            tab.HasValidCache = true;
        }
    }
}
