# Story 001: Arena Courtyard & Tactical Cover Geometry

> **Epic**: Arena Level & Tactical Obstacle Architecture
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Visual/Feel
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/arena-level-architecture.md`
**Requirement**: `TR-arena-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture
**ADR Decision Summary**: Defines a symmetric `40m x 40m` tactical courtyard (`[-20m, +20m]` on `X/Z`) with a `2.5m` perimeter wall, `6m x 6m` central altar, 4 L-walls, and 8 cover pillars assigned to `Layer 6 (EnvironmentWall)` and `Layer 9 (ArenaFloor)`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Static primitive `BoxCollider` and `CapsuleCollider` geometry with SRP Batcher-compatible `Universal Render Pipeline/Lit` materials ensures `< 80` draw calls and `0` MeshCollider overhead.

**Control Manifest Rules (this layer)**:
- Required: Use primitive `BoxCollider` and `CapsuleCollider` on all static arena geometry and assign walls/pillars to `Layer 6 (EnvironmentWall)` and floor/altar top to `Layer 9 (ArenaFloor)`.
- Forbidden: Never attach non-convex `MeshCollider` to arena obstacles or place cover pillars closer than `2.5m` to another obstacle (`W_gap >= 2.5m`).
- Guardrail: Total scene batches in `Arena.unity` must remain `< 80` with `>= 60 FPS`.

---

## Acceptance Criteria

*From GDD `design/gdd/arena-level-architecture.md`, scoped to this story:*

- [x] `Arena.unity` builds a symmetric `40m x 40m` floor (`X, Z in [-20m, +20m]`) assigned to `Layer 9 (ArenaFloor)` with slate-grey `#2B2D42` and `1m` metric grid `#3A3D56` URP materials per the Art Bible.
- [x] Four outer perimeter walls (`Height = 2.5m`, `Thickness = 1.0m`), 4 symmetric L-shaped corner walls (`Height = 2.5m`), and 8 cylindrical cover pillars (`Radius = 0.75m`, `Height = 2.5m`) are placed at radial symmetry on `Layer 6 (EnvironmentWall)` using `#5C6274` / `#8D99AE` materials.
- [x] A central raised altar (`6m x 6m`, `Height = 0.4m`) with 4 sloped access ramps sits at `(0, 0, 0)`, and every corridor gap between obstacles satisfies `W_gap >= 2.5m` so 2 capsules + `90 deg` Horizontal Sweep (`3.0m`) never snag.

---

## Implementation Notes

*Derived from ADR-0001 Implementation Guidelines:*

Create `Assets/Scripts/Arena/ArenaLayoutValidator.cs` and `Assets/Scenes/Arena.unity` constructing and validating the symmetric `40m x 40m` arena hierarchy: `ArenaFloor` (`Layer 9`), `PerimeterWalls` (`North/South/East/West` at `+-20.5m`, `Height = 2.5m`, `Layer 6`), `CentralAltar` (`6m x 6m x 0.4m` + 4 ramps on `Layer 9`), 4 `LWall_Quadrant` assemblies at `(+-10m, +-10m)` (`Layer 6`), and 8 `CoverPillar` cylinders (`Radius = 0.75m`, `Height = 2.5m`, `Layer 6`). Use primitive colliders only and apply URP Lit materials matching `design/art-bible.md` (`#2B2D42` + `#3A3D56` floor grid, `#5C6274` walls, `#F4A261` altar).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-max-min-euclidean-spawn-selector.md`): `ArenaSpawnManager` Max-Min Euclidean spawn point selection logic.
- Story 003 (`story-003-physics-layer-collision-matrix.md`): Physics Layer Collision Matrix configuration and `LineOfSightMask` LoS blocking verification.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Arena/ArenaLayoutValidator.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Materials/Arena/T_ArenaFloorGrid.png`
- `Assets/Materials/Arena/M_ArenaFloor.mat`
- `Assets/Materials/Arena/M_CoverWall.mat`
- `Assets/Materials/Arena/M_CentralAltar.mat`
- `Assets/Tests/EditMode/ArenaLayersCollisionTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: Arena geometry is a `40m x 40m` symmetric tactical courtyard with a `2.5m` high perimeter wall, central altar, 4 L-walls, and 8 cover pillars on `Layer 6` and `Layer 9`.
  - Setup: Open `Arena.unity` in Play Mode and inspect the scene hierarchy, collider bounds, layer assignments, and top-down + perspective camera views.
  - Verify: Floor bounds span `[-20m, +20m]` on `X/Z` (`Layer 9`); perimeter walls (`2.5m` height), 4 L-walls, and 8 pillars (`Radius 0.75m`) are on `Layer 6`; central altar (`6m x 6m`, `0.4m` height) sits at `(0, 0, 0)`; minimum corridor gap between any two obstacles is `>= 2.5m`.
  - Pass condition: Retained screenshot in `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry/01-arena-courtyard-overview.png` + lead sign-off in `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry/story-001-arena-courtyard-and-cover-geometry-evidence.md` confirming symmetric layout, Art Bible palette contrast (`>= 4.5:1`), and `< 80` batches.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshot at `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry/01-arena-courtyard-overview.png` + sign-off at `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry/story-001-arena-courtyard-and-cover-geometry-evidence.md` + `stamp.txt`

**Status**: [x] Verified

---

## Dependencies

- Depends on: None
- Unlocks: `production/epics/arena-level-architecture/story-002-max-min-euclidean-spawn-selector.md`, `production/epics/arena-level-architecture/story-003-physics-layer-collision-matrix.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry/01-arena-courtyard-overview.png`, `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry/story-001-arena-courtyard-and-cover-geometry-evidence.md`, and `Assets/Tests/EditMode/ArenaLayersCollisionTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) URP `1280x720` render capture + EditMode/PlayMode layout & clearance audit (`0` `MeshCollider`s, `W_gap >= 2.5m`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
