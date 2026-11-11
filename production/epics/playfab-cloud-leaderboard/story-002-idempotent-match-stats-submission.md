# Story 002: Idempotent Match Stats Submission (`TotalWins`, `TotalKills`, `MatchesPlayed`)

> **Epic**: PlayFab Cloud Identity, Stats & Leaderboard
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/playfab-cloud-leaderboard.md`
**Requirement**: `TR-playfab-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: PlayFab Async Identity/Leaderboard Resilience & Pooled URP Audio/VFX Feedback
**ADR Decision Summary**: Submits `TotalWins (+1 if Winner)`, `TotalKills (+Kills in [0, 15])`, and `MatchesPlayed (+1)` once on `MatchState.Ended` guarded by `_lastSubmittedMatchId` idempotency and a 1-slot pending retry queue in `playfab_identity.json`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Batching all 3 statistics in a single `UpdatePlayerStatistics` call at match end keeps API usage at `1 request / match` (`0` requests during active combat ticks).

**Control Manifest Rules (this layer)**:
- Required: Guard `SubmitMatchResult(MatchId, didWin, kills)` with `if (_lastSubmittedMatchId == matchId) return;`, clamp `kills` to `[0, 15]`, and persist failed submissions in `pendingSubmission` for automatic flush on next login/lobby return.
- Forbidden: Never call `UpdatePlayerStatistics` per kill during `MatchState.Playing`.
- Guardrail: Duplicate `OnMatchEnded` triggers for the same `MatchId` must result in at most `1` PlayFab API submission.

---

## Acceptance Criteria

*From GDD `design/gdd/playfab-cloud-leaderboard.md`, scoped to this story:*

- [x] `PlayFabStatsService.SubmitMatchResultAsync(string matchId, bool didWin, int matchKills)` rejects duplicate calls where `matchId == _lastSubmittedMatchId` (`Idempotent` guard) and clamps `matchKills = Mathf.Clamp(matchKills, 0, 15)` and `winDelta = didWin ? 1 : 0`.
- [x] In a single batched `UpdatePlayerStatistics` payload, it submits `TotalWins`, `TotalKills`, and `MatchesPlayed (+1)` (with `1s, 2s, 4s` transient backoff) and invalidates the local leaderboard cache upon `200 OK`.
- [x] If the player is in `OfflineGuest` mode or the network request fails after retries, `PlayFabStatsService` saves the delta into `pendingSubmission` inside `playfab_identity.json` and flushes it automatically on the next successful authentication.

---

## Implementation Notes

*Derived from ADR-0008 Implementation Guidelines:*

Create `Assets/Scripts/Cloud/PlayFabStatsService.cs`. Subscribe to `MatchManager.OnMatchEnded`. Expose `TryBuildStatsSubmission(string matchId, bool didWin, int rawKills, out StatSubmissionPayload payload)` so EditMode unit tests can verify idempotency (`_lastSubmittedMatchId`), kill clamping (`[0, 15]`), and offline pending queue persistence.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-playfab-auth-and-offline-guest-fallback.md`): `LoginWithCustomID` and `OfflineGuest` fallback.
- Story 003 (`story-003-cached-leaderboard-modal-view.md`): Top 20 Leaderboard Modal UI and `15.0s` TTL cache.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Cloud/PlayFabStatsService.cs`
- `Assets/Tests/EditMode/PlayFabServiceTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `PlayFabStatsService` submits `TotalWins`, `TotalKills` (clamped `[0, 15]`), and `MatchesPlayed` once on `MatchState.Ended` with `_lastSubmittedMatchId` idempotency and offline retry queueing.
  - Given: `PlayFabStatsService` initialized with mock `IPlayFabClientAdapter`.
  - When: `SubmitMatchResultAsync("MATCH_101", didWin: true, matchKills: 3)` is called twice with the same `"MATCH_101"` ID, and once with `"MATCH_102"` (`matchKills: 99`) while offline.
  - Then: First `"MATCH_101"` call sends `1` request (`TotalWins +1, TotalKills +3, MatchesPlayed +1`); second `"MATCH_101"` call is ignored (`0` duplicate requests); `"MATCH_102"` clamps kills to `15` and stores `pendingSubmission` on disk for automatic flush when back online.
  - Edge cases: Negative `matchKills (-5)` is clamped to `0`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/PlayFabServiceTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified (`Assets/Tests/EditMode/PlayFabServiceTests.cs`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/match-manager-authority-failover/story-002-last-man-standing-win-evaluator.md`, `production/epics/playfab-cloud-leaderboard/story-001-playfab-auth-and-offline-guest-fallback.md`
- Unlocks: `production/epics/playfab-cloud-leaderboard/story-003-cached-leaderboard-modal-view.md`

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: `PlayFabServiceTests.cs` verifying batched `UpdatePlayerStatistics`, `1s/2s/4s` transient backoff, in-memory & cross-restart `LastSubmittedMatchId` idempotency, `[0, 15]` kill clamping, `MatchState.Playing` guard, and `PendingSubmission` reconnect flush.
