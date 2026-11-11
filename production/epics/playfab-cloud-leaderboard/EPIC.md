# Epic: PlayFab Cloud Identity, Stats & Leaderboard System

> **Layer**: Presentation
> **GDD**: design/gdd/playfab-cloud-leaderboard.md
> **Architecture Module**: PlayFabCloud
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này tích hợp dịch vụ đám mây **PlayFab C# Client SDK** thông qua `PlayFabAuthService`, `PlayFabStatsService`, và `LeaderboardModalView`. Theo quyết định tại `ADR-0008`, hệ thống hỗ trợ đăng nhập một chạm bằng `LoginWithCustomID` (kèm hậu tố định danh tiến trình/profile giúp mở 2–4 cửa sổ game trên cùng một máy PC mà vẫn tạo các tài khoản PlayFab độc lập) hoặc đăng nhập bằng `Email/Password`, đồng bộ `DisplayName` (`3..16 ký tự`) vào biến mạng `[Networked] PlayerName` của Photon Fusion, và tự động chuyển sang chế độ `Offline Guest Mode` nếu mất kết nối đám mây để không bao giờ chặn việc vào phòng đấu trường. Khi trận đấu kết thúc (`MatchOver`), `PlayFabStatsService` đẩy các chỉ số `MatchesWon`, `TotalKills`, và `MatchesPlayed` lên PlayFab với khóa chống gửi trùng `MatchSequenceId` và cơ chế thử lại theo cấp số nhân (`1s, 2s, 4s`), đồng thời cung cấp bảng xếp hạng `Top 20` và `AroundPlayer` có bộ nhớ đệm `15.0s TTL`.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0008: PlayFab Cloud Identity/Leaderboard Sync & Pooled 3D Spatial Audio/URP Combat VFX | Implements multi-instance safe `PlayFabAuthService` with Offline Guest fallback, idempotent `MatchSequenceId` stat submission with exponential backoff, and 15s cached leaderboards. | MEDIUM |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-playfab-001 | PlayFabAuthService must authenticate players via LoginWithCustomID or LoginWithEmailAddress, sync DisplayName (3..16 chars) to Photon Fusion [Networked] PlayerName, and fallback gracefully to Offline Guest Mode on network timeout | ADR-0008 ✅ |
| TR-playfab-002 | PlayFabStatsService must submit match results (MatchesWon, TotalKills, MatchesPlayed) via UpdatePlayerStatistics at MatchOver with idempotent MatchSequenceId protection and exponential backoff retry (1s, 2s, 4s) | ADR-0008 ✅ |
| TR-playfab-003 | LeaderboardModalView must fetch Top 20 and AroundPlayer rankings via GetLeaderboard with a 15.0s client cache TTL and highlight the local player row in #38BDF8 | ADR-0008 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/playfab-cloud-leaderboard.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [PlayFab CustomID Auth & Offline Guest Mode Fallback](story-001-playfab-auth-and-offline-guest-fallback.md) | Integration | M | Ready | TR-playfab-001 |
| 002 | [Idempotent Match Stats Submission (`TotalWins`, `TotalKills`, `MatchesPlayed`)](story-002-idempotent-match-stats-submission.md) | Logic | M | Ready | TR-playfab-002 |
| 003 | [Cached Top-20 Leaderboard Modal View (`15.0s` TTL)](story-003-cached-leaderboard-modal-view.md) | UI | S | Ready | TR-playfab-003 |
