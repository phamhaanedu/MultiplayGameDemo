# Story 001: Networked Match State Machine (`Waiting` -> `Countdown` -> `Playing` -> `Ended`)

> **Epic**: Match Manager, Win Condition & Authority Failover
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/match-manager-authority-failover.md`
**Requirement**: `TR-match-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: MatchManager State Machine & Shared Mode MasterClient Authority Failover
**ADR Decision Summary**: Synchronizes the 4-state match lifecycle (`WaitingForPlayers = 0`, `Countdown = 1`, `Playing = 2`, `Ended = 3`), `[Networked] public TickTimer PhaseTimer`, and `[Networked, Capacity(4)] public NetworkDictionary<PlayerRef, PlayerMatchStats> MatchRoster` on a scene `MatchManager : NetworkBehaviour` controlled by the current `StateAuthority`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Fusion 2 `[Networked, Capacity(4)] NetworkDictionary<PlayerRef, PlayerMatchStats>` replicates all 4 players' alive status, kills, and placement rank inside the scene snapshot so any peer can take over authority seamlessly.

**Control Manifest Rules (this layer)**:
- Required: Mutate `CurrentMatchState`, `PhaseTimer`, and `MatchRoster` exclusively when `Object.HasStateAuthority == true` inside `FixedUpdateNetwork()`, and broadcast UI state transitions via `ChangeDetector` in `Render()`.
- Forbidden: Never store match state or living player counts solely in local non-networked fields on the original host client.
- Guardrail: If a player disconnects during `Countdown` (`3.0s`) and `TotalConnectedPlayers < 2`, immediately abort `Countdown` back to `WaitingForPlayers`.

---

## Acceptance Criteria

*From GDD `design/gdd/match-manager-authority-failover.md`, scoped to this story:*

- [x] `MatchManager` starts in `MatchState.WaitingForPlayers (0)` and transitions to `MatchState.Countdown (1)` with `PhaseTimer = 3.0s` (`180 ticks`) as soon as `TotalConnectedPlayers >= 2` (`MinPlayersToStart = 2`).
- [x] If `TotalConnectedPlayers` drops below `2` while in `MatchState.Countdown`, `MatchManager` cancels `PhaseTimer` and reverts to `MatchState.WaitingForPlayers`; otherwise, when `PhaseTimer` expires, it transitions to `MatchState.Playing (2)` with `PhaseTimer = 180.0s` (`10,800 ticks`).
- [x] During `WaitingForPlayers` and `Countdown`, combat and damage evaluation are gated off (`IsCombatAllowed == false`), unlocking on the exact tick `CurrentMatchState` becomes `MatchState.Playing`.

---

## Implementation Notes

*Derived from ADR-0006 Implementation Guidelines:*

Create `Assets/Scripts/Gameplay/Match/MatchManager.cs` and `Assets/Scripts/Gameplay/Match/PlayerMatchStats.cs` (`INetworkStruct` containing `NetworkBool IsAlive`, `int Kills`, `int PlacementRank`). Expose a deterministic state evaluation method `EvaluateStateTransition(MatchState currentState, int connectedPlayers, int alivePlayers, bool phaseTimerExpired)` so EditMode unit tests can test all state transitions (`Waiting -> Countdown -> Waiting` abort, `Countdown -> Playing`, `Playing -> Ended`) without spinning up a live network runner.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-last-man-standing-win-evaluator.md`): Last Man Standing (`AliveCount <= 1`), `180s` Sudden Death tie-breaker, and same-tick double KO tie resolution.
- Story 003 (`story-003-masterclient-state-authority-failover.md`): `IStateAuthorityChanged` / `OnPlayerLeft` MasterClient authority failover.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Gameplay/Match/PlayerMatchStats.cs`
- `Assets/Scripts/Gameplay/Match/MatchManager.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/MatchManagerTests.cs`
- `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `MatchManager` drives `WaitingForPlayers -> Countdown (3.0s) -> Playing (180.0s) -> Ended (8.0s)` when `ConnectedPlayers >= 2` and aborts `Countdown` if `ConnectedPlayers < 2`.
  - Given: `MatchManager` initialized in `MatchState.WaitingForPlayers` (`MinPlayersToStart = 2`).
  - When: Player 2 joins (`ConnectedPlayers = 2`), then leaves mid-countdown (`ConnectedPlayers = 1`), then re-joins (`ConnectedPlayers = 2`) and `PhaseTimer` (`180 ticks` = `3.0s`) expires.
  - Then: State transitions `WaitingForPlayers -> Countdown -> WaitingForPlayers -> Countdown -> Playing` (`PhaseTimer = 180.0s`), and `IsCombatAllowed` is `false` during `Waiting`/`Countdown` and `true` during `Playing`.
  - Edge cases: Late joiner attempting to join after `CurrentMatchState == Playing` is Either spawned as a Ghost spectator (`IsAlive = false`) or blocked by session lock (`SessionInfo.IsOpen = false`).

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/MatchManagerStateTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/shared-mode-session-pool/story-001-fusion-shared-mode-session-bootstrap.md`
- Unlocks: `production/epics/match-manager-authority-failover/story-002-last-man-standing-win-evaluator.md`, `production/epics/match-manager-authority-failover/story-003-masterclient-state-authority-failover.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/MatchManagerTests.cs` + `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
