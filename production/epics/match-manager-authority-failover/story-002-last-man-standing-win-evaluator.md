# Story 002: Last Man Standing Win Evaluator & Sudden Death Tie-Breaker

> **Epic**: Match Manager, Win Condition & Authority Failover
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/match-manager-authority-failover.md`
**Requirement**: `TR-match-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: MatchManager State Machine & Shared Mode MasterClient Authority Failover
**ADR Decision Summary**: Evaluates `AliveCount <= 1` on every elimination or disconnect during `MatchState.Playing`, resolves `180.0s` timeout via `(CurrentHP DESC, Kills DESC)` Sudden Death ranking, and resolves same-tick double KOs (`AliveCount == 0`) or exact ties with `WinnerPlayer = PlayerRef.None` (`DRAW`).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Deterministic tie-breaker evaluation over `MatchRoster` and `PlayerHealth.CurrentHP` on `StateAuthority` ensures all clients receive identical `WinnerPlayer` and `MatchId` results.

**Control Manifest Rules (this layer)**:
- Required: Evaluate win conditions inside `MatchManager` on `StateAuthority` whenever `Rpc_ReportElimination`, `OnPlayerLeft`, or `PhaseTimer.Expired` occurs during `MatchState.Playing`, and generate a deterministic `MatchId` for PlayFab idempotency.
- Forbidden: Never declare a random winner when two survivors tie on both `CurrentHP` and `Kills` at timeout or die on the same tick (`WinnerPlayer` must be `PlayerRef.None`).
- Guardrail: Transitioning to `MatchState.Ended` must immediately set `InputFocusFlag.MatchEnded` and start the `8.0s` (`480 ticks`) post-match timer.

---

## Acceptance Criteria

*From GDD `design/gdd/match-manager-authority-failover.md`, scoped to this story:*

- [x] When `AliveCount == 1` during `MatchState.Playing` (via elimination or opponent disconnect), `MatchManager` sets `WinnerPlayer` to the sole surviving `PlayerRef`, assigns `PlacementRank = 1`, and transitions to `MatchState.Ended (3)` (`PhaseTimer = 8.0s`).
- [x] When `AliveCount == 0` on the same simulation tick (mutual projectile double-KO), `MatchManager` sets `WinnerPlayer = PlayerRef.None` (`DRAW`) and transitions to `MatchState.Ended`.
- [x] When the `180.0s` match timer expires with `AliveCount >= 2`, `MatchManager` ranks living survivors by `(CurrentHP DESC, Kills DESC)`; if a single top survivor exists, they are crowned `WinnerPlayer`, otherwise `WinnerPlayer = PlayerRef.None` (`DRAW`).

---

## Implementation Notes

*Derived from ADR-0006 Implementation Guidelines:*

Extend `Assets/Scripts/Gameplay/Match/MatchManager.cs` with `public static PlayerRef ResolveWinnerOrDraw(ReadOnlySpan<SurvivorCandidate> survivors)` where `SurvivorCandidate` holds `(PlayerRef Player, int CurrentHP, int Kills)`. When `CurrentMatchState` transitions to `MatchState.Ended`, raise `OnMatchEnded(MatchResultSummary result)` containing `MatchId`, `WinnerPlayer`, and local player stats (`DidWin`, `Kills`, `Deaths`) for the HUD Victory/Defeat modal and `PlayFabStatsService`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-networked-match-state-machine.md`): `WaitingForPlayers -> Countdown -> Playing` state machine.
- Story 003 (`story-003-masterclient-state-authority-failover.md`): `IStateAuthorityChanged` MasterClient migration on disconnect.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Gameplay/Match/MatchManager.cs`
- `Assets/Tests/EditMode/MatchManagerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `MatchManager` evaluates Last Man Standing (`AliveCount <= 1`), `180.0s` Sudden Death tie-breaker (`CurrentHP DESC, Kills DESC`), and same-tick double KO (`WinnerPlayer = PlayerRef.None`).
  - Given: Active match in `MatchState.Playing` with 2–4 players in `MatchRoster`.
  - When: (Case A) 3 of 4 players are eliminated leaving `Player 2` alive; (Case B) last 2 players die on the same tick (`AliveCount == 0`); (Case C) `180.0s` timer expires with `Player 1 (60 HP, 1 Kill)` vs `Player 2 (45 HP, 2 Kills)`; (Case D) `180.0s` timer expires with `Player 1 (50 HP, 1 Kill)` vs `Player 2 (50 HP, 1 Kill)`.
  - Then: Case A crowns `Player 2`; Case B declares `PlayerRef.None` (`DRAW`); Case C crowns `Player 1` (`60 HP > 45 HP`); Case D declares `PlayerRef.None` (`DRAW`); all cases transition to `MatchState.Ended` with `PhaseTimer = 8.0s`.
  - Edge cases: Last opponent disconnects mid-match (`OnPlayerLeft`) immediately crowns the remaining connected survivor.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/MatchManagerStateTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/health-damage-rpc-system/story-002-damage-request-rpc-validation.md`, `production/epics/match-manager-authority-failover/story-001-networked-match-state-machine.md`
- Unlocks: `production/epics/match-manager-authority-failover/story-003-masterclient-state-authority-failover.md`, `production/epics/playfab-cloud-leaderboard/story-002-idempotent-match-stats-submission.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/MatchManagerTests.cs` (`69/69` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
