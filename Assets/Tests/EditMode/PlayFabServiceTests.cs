using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CapsuleArena.Cloud;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.UI.Common;
using CapsuleArena.UI.Leaderboard;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// Comprehensive unit &amp; integration tests for Epic 13 PlayFab Cloud Identity, Stats &amp; Leaderboard:
    /// - Story 13-1: <see cref="PlayFabAuthService"/> atomic <c>playfab_identity.json</c>, <c>1s/2s/4s</c> backoff + retry recovery to <c>Authenticated</c>,
    ///   <c>4xx</c>/empty-TitleId immediate <c>OfflineGuest</c>, corrupt JSON <c>.corrupt.bak</c> recovery, <c>-profile</c> multi-instance isolation
    /// - Story 13-2: <see cref="PlayFabStatsService"/> online &amp; cross-restart <c>_lastSubmittedMatchId</c> idempotency, <c>[0, 15]</c> kill clamp,
    ///   <c>MatchManager.OnMatchEnded</c> (including <c>IsDraw -&gt; WinDelta = 0</c> &amp; <c>Playing</c> block), transient retry, 1-slot offline queue flush
    /// - Story 13-3: <see cref="PlayFabLeaderboardService"/> &amp; <see cref="LeaderboardModalView"/> per-tab <c>15.0s</c> TTL cache across
    ///   <c>TotalWins &lt;-&gt; TotalKills</c>, <c>5.0s</c> refresh cooldown, Top-3/Cyan styling, sticky footer, empty/offline banners, <c>Escape</c> &amp; match-start close
    /// </summary>
    [TestFixture]
    public sealed class PlayFabServiceTests
    {
        private sealed class MockPlayFabAdapter : IPlayFabClientAdapter
        {
            public readonly Queue<PlayFabApiResponse<PlayFabLoginResult>> LoginQueue =
                new Queue<PlayFabApiResponse<PlayFabLoginResult>>();

            public readonly Queue<PlayFabApiResponse<bool>> StatsQueue =
                new Queue<PlayFabApiResponse<bool>>();

            public List<LeaderboardEntryDto> WinsEntries = new List<LeaderboardEntryDto>();
            public List<LeaderboardEntryDto> KillsEntries = new List<LeaderboardEntryDto>();
            public int LoginCalls;
            public int DisplayNameSyncCalls;
            public int StatsCalls;
            public int LeaderboardCalls;
            public StatSubmissionPayload LastStatsPayload;
            public string LastSyncedDisplayName = string.Empty;

            public Task<PlayFabApiResponse<PlayFabLoginResult>> LoginWithCustomIdAsync(string titleId, string customId)
            {
                LoginCalls++;
                if (LoginQueue.Count > 0)
                {
                    return Task.FromResult(LoginQueue.Dequeue());
                }

                return Task.FromResult(PlayFabApiResponse<PlayFabLoginResult>.Ok(new PlayFabLoginResult
                {
                    PlayFabId = "PF_LOCAL_01",
                    SessionTicket = "TICKET_VALID_123",
                    DisplayName = string.Empty
                }));
            }

            public Task<PlayFabApiResponse<bool>> UpdateUserTitleDisplayNameAsync(string sessionTicket, string displayName)
            {
                DisplayNameSyncCalls++;
                LastSyncedDisplayName = displayName;
                return Task.FromResult(PlayFabApiResponse<bool>.Ok(true));
            }

            public Task<PlayFabApiResponse<bool>> UpdatePlayerStatisticsAsync(string sessionTicket, StatSubmissionPayload payload)
            {
                StatsCalls++;
                LastStatsPayload = payload;
                if (StatsQueue.Count > 0)
                {
                    return Task.FromResult(StatsQueue.Dequeue());
                }

                return Task.FromResult(PlayFabApiResponse<bool>.Ok(true));
            }

            public Task<PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>> GetLeaderboardAsync(
                string sessionTicket,
                string statisticName,
                int maxResults = 20)
            {
                LeaderboardCalls++;
                List<LeaderboardEntryDto> src = statisticName == PlayFabLeaderboardService.StatTotalKills
                    ? KillsEntries
                    : WinsEntries;
                IReadOnlyList<LeaderboardEntryDto> copy = new List<LeaderboardEntryDto>(src);
                return Task.FromResult(PlayFabApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Ok(copy));
            }
        }

        private string _tempDir;
        private string _identityPath;
        private GameObject _hostGo;
        private List<float> _recordedDelays;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CapsuleArena_PlayFabTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _identityPath = Path.Combine(_tempDir, PlayFabAuthService.IdentityFileName);
            _hostGo = new GameObject("CloudServicesTestHost");
            _recordedDelays = new List<float>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGo);
            }

            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void PlayFabAuthService_RetryRecovery_ExhaustedOfflineFallback_EmptyTitleId_DiskReload_CorruptRecovery_AndProfileIsolation()
        {
            // Multi-instance -profile CLI isolation check
            Assert.That(PlayFabAuthService.ResolveDefaultIdentityFileName(new[] { "-batchmode" }), Is.EqualTo("playfab_identity.json"));
            Assert.That(PlayFabAuthService.ResolveDefaultIdentityFileName(new[] { "-profile", "Client2" }), Is.EqualTo("playfab_identity_Client2.json"));

            var mock = new MockPlayFabAdapter();
            // Case B: Fail twice (503, 0 timeout -> 1.0s, 2.0s backoff) then succeed on attempt 3 -> Authenticated + DisplayName sync!
            mock.LoginQueue.Enqueue(PlayFabApiResponse<PlayFabLoginResult>.Fail(503, "ServiceUnavailable", "Down"));
            mock.LoginQueue.Enqueue(PlayFabApiResponse<PlayFabLoginResult>.Fail(0, "NetworkTimeout", "Timeout"));

            PlayFabAuthService auth = _hostGo.AddComponent<PlayFabAuthService>();
            auth.ConfigureForSession(mock, _identityPath, "TITLE1", d =>
            {
                _recordedDelays.Add(d);
                return Task.CompletedTask;
            });

            string firstCustomId = auth.CustomId;
            Assert.That(firstCustomId.Length, Is.EqualTo(32));
            Assert.That(File.Exists(_identityPath), Is.True);

            AuthState recoveredState = auth.AuthenticateAsync().GetAwaiter().GetResult();
            Assert.That(recoveredState, Is.EqualTo(AuthState.Authenticated));
            Assert.That(auth.IsAuthenticated, Is.True);
            Assert.That(auth.PlayFabId, Is.EqualTo("PF_LOCAL_01"));
            Assert.That(auth.SessionTicket, Is.EqualTo("TICKET_VALID_123"));
            Assert.That(_recordedDelays, Is.EqualTo(new[] { 1.0f, 2.0f }));
            Assert.That(mock.DisplayNameSyncCalls, Is.EqualTo(1));

            // Cross-instance disk reload preserves exact CustomId
            PlayFabAuthService secondAuth = _hostGo.AddComponent<PlayFabAuthService>();
            secondAuth.ConfigureForSession(mock, _identityPath, "TITLE1", _ => Task.CompletedTask);
            Assert.That(secondAuth.CustomId, Is.EqualTo(firstCustomId));

            // Case C: Exhaust all 3 retry backoffs (1.0s, 2.0s, 4.0s) -> OfflineGuest without blocking multiplayer
            _recordedDelays.Clear();
            for (int i = 0; i < 4; i++)
            {
                mock.LoginQueue.Enqueue(PlayFabApiResponse<PlayFabLoginResult>.Fail(503, "ServiceUnavailable", "Down"));
            }

            AuthState exhaustedState = auth.AuthenticateAsync().GetAwaiter().GetResult();
            Assert.That(exhaustedState, Is.EqualTo(AuthState.OfflineGuest));
            Assert.That(_recordedDelays, Is.EqualTo(new[] { 1.0f, 2.0f, 4.0f }));
            Assert.That(auth.CanStartMultiplayerMatch, Is.True);
            Assert.That(auth.DisplayName.StartsWith("Guest_"), Is.True);

            // Empty TitleId ("") with real REST adapter aborts immediately (0 retries) into OfflineGuest
            _recordedDelays.Clear();
            PlayFabAuthService emptyTitleAuth = _hostGo.AddComponent<PlayFabAuthService>();
            emptyTitleAuth.ConfigureForSession(null, Path.Combine(_tempDir, "empty_title_id.json"), string.Empty, d =>
            {
                _recordedDelays.Add(d);
                return Task.CompletedTask;
            });
            Assert.That(emptyTitleAuth.AuthenticateAsync().GetAwaiter().GetResult(), Is.EqualTo(AuthState.OfflineGuest));
            Assert.That(_recordedDelays.Count, Is.EqualTo(0));

            // Corrupt playfab_identity.json moves to .corrupt.bak and regenerates cleanly
            string corruptPath = Path.Combine(_tempDir, "corrupt_identity.json");
            File.WriteAllText(corruptPath, "{not_valid_json!!!");
            PlayFabAuthService recoveredCorrupt = _hostGo.AddComponent<PlayFabAuthService>();
            recoveredCorrupt.ConfigureForSession(mock, corruptPath, "TITLE1", _ => Task.CompletedTask);
            Assert.That(recoveredCorrupt.CustomId.Length, Is.EqualTo(32));
            Assert.That(File.Exists(corruptPath + ".corrupt.bak"), Is.True);
        }

        [Test]
        public void PlayFabStatsService_OnlineIdempotency_MatchEndedWiring_PlayingGuard_RetryBackoff_AndCrossRestartPersistence()
        {
            var mock = new MockPlayFabAdapter();
            PlayFabAuthService auth = _hostGo.AddComponent<PlayFabAuthService>();
            auth.ConfigureForSession(mock, _identityPath, "TITLE1", d =>
            {
                _recordedDelays.Add(d);
                return Task.CompletedTask;
            });
            Assert.That(auth.AuthenticateAsync().GetAwaiter().GetResult(), Is.EqualTo(AuthState.Authenticated));

            MatchManager matchMgr = _hostGo.AddComponent<MatchManager>();
            matchMgr.ConfigureServices(new InputFocusManager(), "ROOM9", PlayerRef.FromEncoded(1));

            PlayFabLeaderboardService lb = _hostGo.AddComponent<PlayFabLeaderboardService>();
            lb.Initialize(auth, clientAdapter: null); // Uses auth.ResolveClientAdapter() seamlessly!
            lb.SeedCachedEntries(PlayFabLeaderboardService.StatTotalWins, new List<LeaderboardEntryDto>(), 10f);

            PlayFabStatsService stats = _hostGo.AddComponent<PlayFabStatsService>();
            stats.Initialize(auth, clientAdapter: null, lb, matchMgr); // Shared adapter inheritance!

            // Direct online submission ("MATCH_101", transient 503 once -> retries 1.0s then succeeds):
            _recordedDelays.Clear();
            mock.StatsQueue.Enqueue(PlayFabApiResponse<bool>.Fail(503, "ServiceUnavailable", "Transient"));
            mock.StatsQueue.Enqueue(PlayFabApiResponse<bool>.Ok(true));

            bool firstOnline = stats.SubmitMatchResultAsync("MATCH_101", didWin: true, rawKills: 3).GetAwaiter().GetResult();
            Assert.That(firstOnline, Is.True);
            Assert.That(_recordedDelays, Is.EqualTo(new[] { 1.0f }));
            Assert.That(mock.StatsCalls, Is.EqualTo(2));
            Assert.That(mock.LastStatsPayload.WinDelta, Is.EqualTo(1));
            Assert.That(mock.LastStatsPayload.KillsDelta, Is.EqualTo(3));
            Assert.That(mock.LastStatsPayload.MatchesPlayedDelta, Is.EqualTo(1));
            Assert.That(lb.ShouldUseCachedLeaderboard(PlayFabLeaderboardService.StatTotalWins, 12f), Is.False);

            // Duplicate online "MATCH_101" -> 0 extra API calls! And cross-restart re-init also rejects "MATCH_101"!
            int callsBeforeDup = mock.StatsCalls;
            Assert.That(stats.SubmitMatchResultAsync("MATCH_101", didWin: true, rawKills: 3).GetAwaiter().GetResult(), Is.False);
            Assert.That(mock.StatsCalls, Is.EqualTo(callsBeforeDup));

            PlayFabStatsService restartedStats = _hostGo.AddComponent<PlayFabStatsService>();
            restartedStats.Initialize(auth, null, lb, null);
            Assert.That(restartedStats.SubmitMatchResultAsync("MATCH_101", didWin: true, rawKills: 3).GetAwaiter().GetResult(), Is.False);
            Assert.That(mock.StatsCalls, Is.EqualTo(callsBeforeDup));

            // Guard during MatchState.Playing -> rejected (0 API calls), then Mutual Double-KO Draw OnMatchEnded -> WinDelta == 0!
            matchMgr.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(1), "P1", isAlive: true);
            matchMgr.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(2), "P2", isAlive: true);
            matchMgr.SimulateStandaloneTicks(MatchManager.CountdownDurationTicks + 2);
            Assert.That(matchMgr.CurrentMatchState, Is.EqualTo(MatchState.Playing));
            Assert.That(stats.SubmitMatchResultAsync("DURING_PLAYING", didWin: true, rawKills: 1).GetAwaiter().GetResult(), Is.False);
            Assert.That(mock.StatsCalls, Is.EqualTo(callsBeforeDup));

            // Mutual Double-KO Draw -> OnMatchEnded fires automatically with IsDraw == true -> WinDelta == 0!
            matchMgr.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(1), "P1", isAlive: false);
            matchMgr.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(2), "P2", isAlive: false);
            matchMgr.EvaluateWinConditions(isTimeoutSuddenDeath: false);
            Assert.That(matchMgr.CurrentMatchState, Is.EqualTo(MatchState.Ended));
            Assert.That(mock.StatsCalls, Is.EqualTo(callsBeforeDup + 1));
            Assert.That(mock.LastStatsPayload.WinDelta, Is.EqualTo(0), "Draw match must submit WinDelta == 0.");

            // Offline clamping ([0, 15]) + 1-slot PendingSubmission auto-flush on reconnect
            mock.LoginQueue.Enqueue(PlayFabApiResponse<PlayFabLoginResult>.Fail(400, "InvalidTitleId", "Offline"));
            auth.AuthenticateAsync().GetAwaiter().GetResult();
            Assert.That(stats.SubmitMatchResultAsync("OFFLINE_99", didWin: true, rawKills: 99).GetAwaiter().GetResult(), Is.False);
            Assert.That(stats.HasPendingOfflineSubmission, Is.True);
            Assert.That(auth.IdentityData.PendingSubmission.KillsDelta, Is.EqualTo(15));
            Assert.That(stats.TryBuildStatsSubmission("NEG_CHECK", false, -5, out StatSubmissionPayload neg), Is.True);
            Assert.That(neg.KillsDelta, Is.EqualTo(0));

            auth.AuthenticateAsync().GetAwaiter().GetResult();
            Assert.That(stats.HasPendingOfflineSubmission, Is.False);
            Assert.That(mock.LastStatsPayload.MatchId, Is.EqualTo("OFFLINE_99"));
        }

        [Test]
        public void LeaderboardServiceAndModal_TabSwitchingTtl_PersonalFooter_EmptyAndOfflineBanners_EscapeAndMatchStartClose()
        {
            var mock = new MockPlayFabAdapter();
            for (int i = 0; i < 20; i++)
            {
                mock.WinsEntries.Add(new LeaderboardEntryDto
                {
                    Position = i,
                    PlayFabId = i == 4 ? "PF_LOCAL_01" : $"PF_OTHER_{i:00}",
                    DisplayName = i == 4 ? "ArenaHero" : $"Rival_{i + 1:00}",
                    StatValue = 100 - i * 3
                });
            }

            PlayFabAuthService auth = _hostGo.AddComponent<PlayFabAuthService>();
            auth.ConfigureForSession(mock, _identityPath, "TITLE1", _ => Task.CompletedTask);
            auth.SetDisplayName("ArenaHero");
            auth.AuthenticateAsync().GetAwaiter().GetResult();

            MatchManager matchMgr = _hostGo.AddComponent<MatchManager>();
            var focus = new InputFocusManager();
            PlayFabLeaderboardService lb = _hostGo.AddComponent<PlayFabLeaderboardService>();
            lb.Initialize(auth, clientAdapter: null);

            LeaderboardModalView modal = _hostGo.AddComponent<LeaderboardModalView>();
            modal.Initialize(auth, lb, focus, matchMgr);
            int childCountAfterInit = modal.PooledRows[0].transform.parent.childCount;
            Assert.That(modal.PooledRows.Count, Is.EqualTo(20));

            // Open TotalWins at t=10s (1 call), switch to TotalKills at t=11s (empty -> 2nd call + empty banner!),
            // then rapidly switch back & forth within 15.0s TTL -> 0 extra calls & 0 Instantiate!
            modal.OpenModalAsync(10.0f).GetAwaiter().GetResult();
            Assert.That(mock.LeaderboardCalls, Is.EqualTo(1));
            Assert.That(modal.PersonalFooterMessage, Does.Contain("YOUR RECORD (ArenaHero)"));
            Assert.That(modal.PooledRows[0].RankColor, Is.EqualTo(LeaderboardRowView.RankGoldColor));
            Assert.That(modal.PooledRows[1].RankColor, Is.EqualTo(LeaderboardRowView.RankSilverColor));
            Assert.That(modal.PooledRows[2].RankColor, Is.EqualTo(LeaderboardRowView.RankBronzeColor));
            Assert.That(modal.PooledRows[4].IsHighlightedAsLocal, Is.True);
            Assert.That(modal.PooledRows[4].AccentColor, Is.EqualTo(UiThemeAndFontProvider.ElectricCyan));

            // Switch to TotalKills (0 entries) -> clean empty-state banner & 0 active rows
            modal.SelectTabAsync(PlayFabLeaderboardService.StatTotalKills, 11.0f).GetAwaiter().GetResult();
            Assert.That(mock.LeaderboardCalls, Is.EqualTo(2));
            Assert.That(modal.StatusBannerMessage, Is.EqualTo("No leaderboard entries recorded yet."));
            Assert.That(modal.PooledRows[0].gameObject.activeSelf, Is.False);

            // Rapid tab switches within 15.0s TTL hit cache cleanly (still 2 API calls total, 0 new children)
            modal.SelectTabAsync(PlayFabLeaderboardService.StatTotalWins, 14.0f).GetAwaiter().GetResult();
            modal.SelectTabAsync(PlayFabLeaderboardService.StatTotalKills, 18.0f).GetAwaiter().GetResult();
            Assert.That(mock.LeaderboardCalls, Is.EqualTo(2));
            Assert.That(modal.PooledRows[0].transform.parent.childCount, Is.EqualTo(childCountAfterInit));

            // 5.0s manual refresh cooldown check
            Assert.That(modal.TryManualRefreshAsync(30.0f).GetAwaiter().GetResult(), Is.True);
            Assert.That(modal.TryManualRefreshAsync(32.0f).GetAwaiter().GetResult(), Is.False);
            Assert.That(modal.TryManualRefreshAsync(35.1f).GetAwaiter().GetResult(), Is.True);

            // Escape closes modal & restores gameplay input
            modal.HandleEscapePressed();
            Assert.That(modal.IsOpen, Is.False);
            Assert.That(focus.IsGameplayInputAllowed, Is.True);

            // MatchState.Countdown auto-closes modal if open in Waiting Room
            modal.OpenModalAsync(40.0f).GetAwaiter().GetResult();
            Assert.That(modal.IsOpen, Is.True);
            matchMgr.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(1), "P1", isAlive: true);
            matchMgr.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(2), "P2", isAlive: true);
            matchMgr.SimulateStandaloneTicks(2);
            Assert.That(matchMgr.CurrentMatchState, Is.EqualTo(MatchState.Countdown));
            Assert.That(modal.IsOpen, Is.False, "Transitioning to Countdown must auto-close LeaderboardModalView.");

            // OfflineGuest banner check (0 exceptions)
            mock.LoginQueue.Enqueue(PlayFabApiResponse<PlayFabLoginResult>.Fail(400, "InvalidTitleId", "Offline"));
            auth.AuthenticateAsync().GetAwaiter().GetResult();
            lb.InvalidateCache();
            Assert.DoesNotThrow(() => modal.OpenModalAsync(60.0f).GetAwaiter().GetResult());
            Assert.That(modal.StatusBannerMessage, Does.Contain("Offline Guest Mode"));
        }
    }
}
