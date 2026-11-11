# Story 001: PlayFab CustomID Auth & Offline Guest Mode Fallback

> **Epic**: PlayFab Cloud Identity, Stats & Leaderboard
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/playfab-cloud-leaderboard.md`
**Requirement**: `TR-playfab-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: PlayFab Async Identity/Leaderboard Resilience & Pooled URP Audio/VFX Feedback
**ADR Decision Summary**: Authenticates asynchronously via `LoginWithCustomID` (`CreateAccount = true`) with a persistent GUID in `Application.persistentDataPath/playfab_identity.json` and exponential backoff (`1s, 2s, 4s`, max 3 retries); if PlayFab is unreachable or `TitleId` is unconfigured, seamlessly falls back to `OfflineGuest` mode so Photon Fusion 2 matchmaking is never blocked.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: All PlayFab REST/SDK calls must execute asynchronously on the main thread callbacks without blocking scene loads or Photon Fusion `StartGame`.

**Control Manifest Rules (this layer)**:
- Required: Persist `CustomID` (`GUID`) and `DisplayName` in `Application.persistentDataPath/playfab_identity.json`, use exponential backoff (`1.0s, 2.0s, 4.0s`) on `5xx`/timeout errors, and transition to `AuthState.OfflineGuest` on failure.
- Forbidden: Never block `FusionSessionManager.StartGame` when PlayFab authentication fails or is offline.
- Guardrail: All network requests enforce a `5.0s` timeout and never retry `4xx` client validation errors (`InvalidTitleId`, `NameNotAvailable`).

---

## Acceptance Criteria

*From GDD `design/gdd/playfab-cloud-leaderboard.md`, scoped to this story:*

- [x] `PlayFabAuthService` loads or generates a persistent `CustomID` (`GUID`, `32` hex chars) and `DisplayName` (`3..16` alphanumeric/underscore chars) in `Application.persistentDataPath/playfab_identity.json` (using atomic `.tmp` write and `-profile <id>` multi-instance isolation).
- [x] `LoginAsync()` executes `LoginWithCustomID` (`CreateAccount = true`) with exponential backoff (`1.0s`, `2.0s`, `4.0s` across up to `3` attempts on transient network errors) and syncs `UpdateUserTitleDisplayName`.
- [x] If `TitleId` is empty or all `3` retry attempts fail, `PlayFabAuthService` transitions to `AuthState.OfflineGuest`, generates a valid guest display name (`"Guest_XXXX"`), and allows the player to enter Photon Fusion 2 matches normally.

---

## Implementation Notes

*Derived from ADR-0008 Implementation Guidelines:*

Create `Assets/Scripts/Cloud/PlayFabAuthService.cs` and `Assets/Scripts/Cloud/IPlayFabClientAdapter.cs` (interface adapter over PlayFab SDK/HTTP calls so EditMode unit tests can inject mock responses for `200 OK`, `503 Service Unavailable` retries, and offline fallback). Implement `CalculateBackoffDelaySec(int attemptIndex)` (`1f * Mathf.Pow(2f, attemptIndex)`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-idempotent-match-stats-submission.md`): End-of-match idempotent stats submission (`TotalWins`, `TotalKills`, `MatchesPlayed`).
- Story 003 (`story-003-cached-leaderboard-modal-view.md`): Top 20 Leaderboard Modal with `15.0s` TTL cache.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Cloud/IPlayFabClientAdapter.cs`
- `Assets/Scripts/Cloud/PlayFabAuthService.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/PlayFabServiceTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `PlayFabAuthService` authenticates via `LoginWithCustomID` with exponential backoff (`1s, 2s, 4s`) and falls back to `OfflineGuest` mode without blocking Photon Fusion 2 matchmaking.
  - Given: `PlayFabAuthService` with a mock `IPlayFabClientAdapter` and temporary identity file path.
  - When: (Case A) Adapter succeeds on attempt 1; (Case B) Adapter fails twice with transient timeout then succeeds on attempt 3; (Case C) Adapter fails all 3 attempts (`1.0s, 2.0s, 4.0s` backoff).
  - Then: `CustomID` is persisted to `playfab_identity.json`; Case A & B reach `AuthState.Authenticated`; Case C transitions to `AuthState.OfflineGuest` with a valid `"Guest_XXXX"` name and `CanStartMultiplayerMatch == true`.
  - Edge cases: `400 InvalidTitleId` aborts retries immediately into `OfflineGuest` without waiting `7.0s`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/PlayFabServiceTests.cs` — must exist and pass

**Status**: [x] Verified (`Assets/Tests/EditMode/PlayFabServiceTests.cs`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: None
- Unlocks: `production/epics/playfab-cloud-leaderboard/story-002-idempotent-match-stats-submission.md`, `production/epics/playfab-cloud-leaderboard/story-003-cached-leaderboard-modal-view.md`

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: `PlayFabServiceTests.cs` verifying atomic `.tmp` persistence, `-profile <id>` isolation, `1s/2s/4s` transient backoff, non-retry on `4xx`/empty `TitleId`, `DisplayName` sync, and non-blocking `OfflineGuest` fallback.
