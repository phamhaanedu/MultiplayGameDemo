# Story 002: Max-Min Euclidean Spawn Point Selecto

> **Epic**: Arena Level & Tactical Obstacle Architecture
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/arena-level-architecture.md`
**Requirement**: `TR-arena-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture
**ADR Decision Summary**: Places 8 perimeter `SpawnPoint` transforms on a `16.0m` radius circle (`45 deg` apart) facing `(0,0,0)` and selects the candidate maximizing minimum squared Euclidean distance to all living players (`0 B` GC).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Pure `Vector3` math (`sqrMagnitude` / `Distance`) over a fixed 8-element `Transform[]` array and up to 4 active player positions allocates `0 B` heap memory.

**Control Manifest Rules (this layer)**:
- Required: Iterate over a cached `Transform[8]` array and pre-allocated `Vector3` span/array to compute Max-Min Euclidean distance.
- Forbidden: Never use `System.Linq` (`OrderBy`, `Max`, `Min`) or `GameObject.FindGameObjectsWithTag` inside `SelectBestSpawnPoint`.
- Guardrail: Spawn selection must guarantee `d_min >= 18.0m` when `<= 3` living opponents are in the arena.

---

## Acceptance Criteria

*From GDD `design/gdd/arena-level-architecture.md`, scoped to this story:*

- [x] `ArenaSpawnManager` holds 8 `SpawnPoint` transforms arranged at `R_spawn = 16.0m` (`0, 45, 90, 135, 180, 225, 270, 315 deg`) with `Y = 0.05m` and rotation facing `(0, 0, 0)` (`yaw = theta_k + 180 deg`).
- [x] `SelectBestSpawnIndex(ReadOnlySpan<Vector3> spawnPositions, ReadOnlySpan<Vector3> livingPositions, int fallbackPlayerIndex)` evaluates `S* = argmax_{S_k} (min_{P_j} ||pos(S_k) - pos(P_j)||_2)` and returns a spawn point at least `18.0m` away from all living opponents when `1..3` players are alive.
- [x] When `livingPositions.Length == 0` (simultaneous initial spawn), `SelectBestSpawnIndex` deterministically selects index `(fallbackPlayerIndex * 2) % 8` so 4 players spawn at `0, 90, 180, 270 deg` (`22.6m` apart).

---

## Implementation Notes

*Derived from ADR-0001 Implementation Guidelines:*

Create `Assets/Scripts/Arena/IArenaSpawnSelector.cs` and `Assets/Scripts/Arena/ArenaSpawnManager.cs`. Provide a pure static method `SelectBestSpawnIndex(ReadOnlySpan<Vector3> spawnPositions, ReadOnlySpan<Vector3> livingPositions, int fallbackPlayerIndex)` alongside `GetSpawnPose` so EditMode unit tests can test the Max-Min Euclidean distance algorithm directly with arbitrary `Vector3` coordinates without requiring a running Fusion session.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-arena-courtyard-and-cover-geometry.md`): Static courtyard walls, floor, altar, and pillar geometry.
- Story 003 (`story-003-physics-layer-collision-matrix.md`): Physics Layer Collision Matrix and `LineOfSightMask`.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Arena/IArenaSpawnSelector.cs`
- `Assets/Scripts/Arena/ArenaSpawnManager.cs`
- `Assets/Tests/EditMode/ArenaSpawnManagerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `ArenaSpawnManager` selects one of 8 perimeter `SpawnPoint` transforms (`16m` radius) maximizing minimum Euclidean distance to all living players with deterministic Round-Robin fallback on first spawn.
  - Given: 8 spawn points at `16.0m` radius (`0, 45, 90, 135, 180, 225, 270, 315 deg`) and 1–3 living player positions.
  - When: `SelectBestSpawnIndex()` is called.
  - Then: Returns the spawn point index maximizing `min_j ||S_k - P_j||_2` with distance `>= 18.0m` from all living players; when `livingPlayers.Length == 0`, returns deterministic index `(playerIndex * 2) % 8` (`0, 2, 4, 6` -> `22.62m` apart).
  - Edge cases: Equidistant tie-breaking selects the lowest index `k` deterministically; living player standing directly on `S_0` forces selection of the opposite hemisphere (`S_4`).

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/ArenaSpawnManagerTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/arena-level-architecture/story-001-arena-courtyard-and-cover-geometry.md`
- Unlocks: `production/epics/shared-mode-session-pool/story-001-fusion-shared-mode-session-bootstrap.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/ArenaSpawnManagerTests.cs` (`49/49` EditMode PASS) & `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` (`7/7` PlayMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode, PlayMode, and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
