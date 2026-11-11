# Story 003: Cached Top-20 Leaderboard Modal View (`15.0s` TTL)

> **Epic**: PlayFab Cloud Identity, Stats & Leaderboard
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/playfab-cloud-leaderboard.md`
**Requirement**: `TR-playfab-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: PlayFab Async Identity/Leaderboard Resilience & Pooled URP Audio/VFX Feedback
**ADR Decision Summary**: Queries Top 20 `TotalWins` and `TotalKills` via `GetLeaderboard(StartPosition = 0, MaxResultsCount = 20)` with a `15.0s` client cache TTL and renders them in `20` pre-warmed `LeaderboardRowView` UI items (`0` runtime `Instantiate`/`Destroy`).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Pre-warming `20` `LeaderboardRowView` items inside the ScrollRect content container and toggling `SetActive(i < entryCount)` eliminates UI instantiation spikes when switching between `Top Wins` and `Top Kills` tabs.

**Control Manifest Rules (this layer)**:
- Required: Cache `GetLeaderboard` responses per statistic tab (`TotalWins`, `TotalKills`) for `15.0s` (`LeaderboardCacheTtlSec = 15.0f`) and populate `20` pre-warmed `LeaderboardRowView` rows.
- Forbidden: Never call `GetLeaderboard` on rapid tab clicks when `currentTime - lastFetchTime < 15.0f` and cached data is valid.
- Guardrail: Highlight Top 3 ranks (`#FFD166` Gold, `#E0E1DD` Silver, `#CD7F32` Bronze) and the local player's row (`#00F5D4` Cyan border).

---

## Acceptance Criteria

*From GDD `design/gdd/playfab-cloud-leaderboard.md`, scoped to this story:*

- [x] `LeaderboardModalView` pre-warms `20` `LeaderboardRowView` rows and supports switching between `Tab_TotalWins` and `Tab_TotalKills`, plus a sticky bottom footer showing the local player's personal stats (`Escape` & `Countdown`/`Playing` auto-close).
- [x] `PlayFabLeaderboardService` serves cached Top 20 results immediately if `currentTime - lastFetchTime < 15.0s` (disabling `BTN_Refresh` for a `5.0s` cooldown after manual refresh) and invalidates the cache whenever `PlayFabStatsService` submits a new match result.
- [x] Ranks `#1, #2, #3` display Gold (`#FFD166`), Silver (`#E0E1DD`), and Bronze (`#CD7F32`) accents; the local player's entry highlights in Electric Cyan (`#00F5D4`); and empty/offline states display a clean message without throwing exceptions.

---

## Implementation Notes

*Derived from ADR-0008 Implementation Guidelines:*

Create `Assets/Scripts/Cloud/PlayFabLeaderboardService.cs` and `Assets/Scripts/UI/Leaderboard/LeaderboardModalView.cs`. Make cache TTL evaluation testable via `ShouldUseCachedLeaderboard(string statName, float currentTimeSec, float cacheTtlSec = 15.0f)` in EditMode unit tests.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-playfab-auth-and-offline-guest-fallback.md`): `LoginWithCustomID` authentication.
- Story 002 (`story-002-idempotent-match-stats-submission.md`): End-of-match stats submission.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Cloud/PlayFabLeaderboardService.cs`
- `Assets/Scripts/UI/Leaderboard/LeaderboardModalView.cs`
- `Assets/Scripts/UI/Leaderboard/LeaderboardRowView.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/PlayFabServiceTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `LeaderboardModalView` displays Top 20 `TotalWins` and `TotalKills` using `20` pre-warmed rows and a `15.0s` TTL cache.
  - Setup: Open Leaderboard Modal from Lobby in Play Mode, switch between `Top Wins` and `Top Kills` tabs rapidly within `15.0s`, and inspect row styling for Top 3 and local player.
  - Verify: First tab open fetches up to `20` rows into pre-warmed `LeaderboardRowView` items (`0` `Instantiate`); subsequent clicks within `15.0s` serve cached data (`0` extra network calls); Top 3 and local player rows highlight in Gold/Silver/Bronze and `#00F5D4` Cyan.
  - Pass condition: Retained screenshot of the Leaderboard Modal in `production/qa/evidence/story-003-cached-leaderboard-modal-view.png` + passing unit test in `PlayFabServiceTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-003-cached-leaderboard-modal-view.png`

**Status**: [x] Verified (`production/qa/evidence/story-003-cached-leaderboard-modal-view.png`, `production/qa/evidence/story-003-cached-leaderboard-modal-view-evidence.md`, `production/qa/evidence/story-003-cached-leaderboard-modal-view/stamp.txt`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/playfab-cloud-leaderboard/story-002-idempotent-match-stats-submission.md`
- Unlocks: None

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: Retained `1280x720` URP screenshot (`01-leaderboard-top20-gold-silver-bronze-cyan.png`) + SHA-1 `stamp.txt` (`UNCHANGED`) + `PlayFabServiceTests.cs` (`20` pre-warmed rows, `15.0s` per-tab TTL, `5.0s` refresh cooldown, sticky personal footer, `Escape` & `Countdown` auto-close).
