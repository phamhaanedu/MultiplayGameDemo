# Epic: Last Man Standing Match Manager & Authority Failover System

> **Layer**: Feature
> **GDD**: design/gdd/match-manager-authority-failover.md
> **Architecture Module**: MatchLifecycle
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này triển khai bộ điều phối vòng đời trận đấu toàn cục `MatchManager` (`WaitingForPlayers` $\rightarrow$ `Countdown 5.0s` $\rightarrow$ `Playing 180.0s` $\rightarrow$ `MatchOver 8.0s`) để xét điều kiện thắng thua **Last Man Standing** (`AliveCount <= 1`) và giải quyết bài toán rớt mạng chủ phòng trong Photon Fusion 2 Shared Mode. Theo quyết định tại `ADR-0006`, toàn bộ trạng thái trận đấu (`CurrentMatchState`, `AliveCount`, `WinnerPlayerRef`, `PhaseTimer`, `MatchSequenceId`) đều được khai báo là thuộc tính `[Networked]` trên một Scene `NetworkObject` có bật `AllowStateAuthorityOverride = true` và tắt `DestroyWhenStateAuthorityLeaves = false`. Khi máy đang giữ quyền `StateAuthority` ngắt kết nối giữa trận, máy trạm còn lại đang giữ cờ `Runner.IsSharedModeMasterClient` lập tức gọi `Object.RequestStateAuthority()` để kế thừa quyền điều khiển liền mạch trong vòng `1–3 ticks` mà không làm mất đồng hồ đếm giờ hay số người còn sống.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0006: MatchManager Last Man Standing State Machine & Shared Mode MasterClient Failover | Synchronizes `[Networked]` match state across 4 phases, evaluates Last Man Standing win/draw conditions, and executes automatic `RequestStateAuthority()` failover when the MasterClient disconnects. | HIGH |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-match-001 | MatchManager must synchronize [Networked] MatchState (WaitingForPlayers, Countdown, Playing, MatchOver), AliveCount, WinnerPlayerRef, and PhaseTimer across all peers in the session | ADR-0006 ✅ |
| TR-match-002 | MatchManager must evaluate Last Man Standing win conditions when AliveCount <= 1 during Playing state (including simultaneous double-KO draw handling) and transition to MatchOver with podium celebration | ADR-0006 ✅ |
| TR-match-003 | When the current StateAuthority peer disconnects, the remaining peer with Runner.IsSharedModeMasterClient must invoke Object.RequestStateAuthority() on MatchManager and resume match state evaluation without resetting timers or scores | ADR-0006 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/match-manager-authority-failover.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Networked Match State Machine (`Waiting` -> `Countdown` -> `Playing` -> `Ended`)](story-001-networked-match-state-machine.md) | Logic | M | Ready | TR-match-001 |
| 002 | [Last Man Standing Win Evaluator & Sudden Death Tie-Breaker](story-002-last-man-standing-win-evaluator.md) | Logic | S | Ready | TR-match-002 |
| 003 | [Shared Mode MasterClient StateAuthority Failover](story-003-masterclient-state-authority-failover.md) | Integration | M | Ready | TR-match-003 |
