# Story 003: Shared Mode MasterClient StateAuthority Failover

> **Epic**: Match Manager, Win Condition & Authority Failover
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/match-manager-authority-failover.md`
**Requirement**: `TR-match-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: MatchManager State Machine & Shared Mode MasterClient Authority Failover
**ADR Decision Summary**: Implements `IStateAuthorityChanged` and `OnPlayerLeft` on `MatchManager` so when the original MasterClient disconnects mid-match, the deterministic next lowest `PlayerRef.PlayerId` claims `Object.RequestStateAuthority()`, removes disconnected players from `MatchRoster`, and seamlessly continues the match and `PhaseTimer`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: In Photon Fusion 2 Shared Mode, scene `NetworkObject`s with `AllowStateAuthorityOverride = true` and `DestroyWhenStateAuthorityLeaves = false` allow deterministic `Object.RequestStateAuthority()` takeover on host disconnect.

**Control Manifest Rules (this layer)**:
- Required: Configure `MatchManager`'s `NetworkObject` with `AllowStateAuthorityOverride = true` and `DestroyWhenStateAuthorityLeaves = false`, elect the lowest connected `PlayerRef.PlayerId` on authority loss, call `Object.RequestStateAuthority()`, and immediately reconcile `MatchRoster` against `Runner.ActivePlayers` in `StateAuthorityChanged()`.
- Forbidden: Never let all remaining clients call `Object.RequestStateAuthority()` simultaneously without the lowest-`PlayerId` election guard.
- Guardrail: Authority failover and roster cleanup must complete within `<= 2` network ticks (`33.3ms`) without resetting `PhaseTimer` or `CurrentMatchState`.

---

## Acceptance Criteria

*From GDD `design/gdd/match-manager-authority-failover.md`, scoped to this story:*

- [x] `MatchManager` implements `IStateAuthorityChanged` and deterministic lowest-`PlayerId` election (`IsLocalPlayerLowestActiveCandidate(PlayerRef leavingPlayer)`) so exactly one surviving client calls `Object.RequestStateAuthority()` when the previous `StateAuthority` leaves.
- [x] Inside `StateAuthorityChanged()`, when `Object.HasStateAuthority == true`, `MatchManager` reconciles `MatchRoster` against `Runner.ActivePlayers` (`ReconcileDisconnectedPlayers()`), marking any disconnected player as `IsAlive = false` without resetting `PhaseTimer`.
- [x] After reconciling disconnected players on failover, `MatchManager` immediately re-evaluates `EvaluateWinConditions()` so if the disconnecting MasterClient was the second-to-last survivor, the remaining survivor is immediately crowned winner.

---

## Implementation Notes

*Derived from ADR-0006 Implementation Guidelines:*

Extend `Assets/Scripts/Gameplay/Match/MatchManager.cs` to implement `IStateAuthorityChanged`. Implement `public static PlayerRef SelectDeterministicNextMaster(ReadOnlySpan<PlayerRef> activePlayers, PlayerRef leavingPlayer)` (selecting the minimum `PlayerId` among connected players excluding `leavingPlayer`) and `ReconcileRosterOnFailover()` so EditMode and PlayMode tests can verify deterministic MasterClient election, roster cleanup, and immediate win evaluation when the 2nd-to-last player was the disconnecting MasterClient.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-networked-match-state-machine.md`): Base `Waiting -> Countdown -> Playing` transitions.
- Story 002 (`story-002-last-man-standing-win-evaluator.md`): Standard combat elimination and Sudden Death tie-breaker math.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Gameplay/Match/MatchManager.cs`
- `Assets/Scripts/Networking/FusionSessionManager.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/MatchManagerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `MatchManager` implements `IStateAuthorityChanged` and `OnPlayerLeft` so MasterClient disconnect mid-match seamlessly migrates `StateAuthority` to the next lowest `PlayerRef.PlayerId` without resetting `PhaseTimer` or `MatchRoster`.
  - Given: Active match at `PhaseTimer = 112.5s` (`MatchState.Playing`) with `Player 1 (MasterClient, Alive)`, `Player 2 (Alive)`, and `Player 3 (Alive)`.
  - When: `Player 1` disconnects (`OnPlayerLeft` + `StateAuthorityChanged`).
  - Then: `SelectDeterministicNextMaster` elects `Player 2` (lowest remaining `PlayerId`), `Player 1` is marked `IsAlive = false` in `MatchRoster`, `PhaseTimer` continues uninterrupted from `112.5s`, and if `Player 3` was already dead (`IsAlive = false`), `Player 2` immediately wins the match.
  - Edge cases: Consecutive disconnects (`Player 1` leaves, then `Player 2` leaves on the next tick) deterministically migrate authority `Player 1 -> Player 2 -> Player 3`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/MatchManagerStateTests.cs` and `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/match-manager-authority-failover/story-002-last-man-standing-win-evaluator.md`
- Unlocks: `production/epics/combat-hud-mobile-action-pad/story-003-event-driven-screen-space-combat-hud.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/MatchManagerTests.cs` (`69/69` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
