# Story 003: Physics Layer Collision Matrix & LineOfSightMask

> **Epic**: Arena Level & Tactical Obstacle Architecture
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/arena-level-architecture.md`
**Requirement**: `TR-arena-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture
**ADR Decision Summary**: Configures the 4-layer collision matrix (`Layer 6 EnvironmentWall`, `Layer 8 PlayerCharacter`, `Layer 9 ArenaFloor`, `Layer 10 GhostSpectator`) and exposes `ArenaLayers.LineOfSightMask = 1 << 6` so walls block melee, projectiles, and TPS camera while Ghost spectators pass through walls cleanly.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: `Physics.IgnoreLayerCollision`, `Physics.GetIgnoreLayerCollision`, and `Physics.Linecast` on `Layer 6..10` behave identically in Unity 6.6 PhysX.

**Control Manifest Rules (this layer)**:
- Required: Centralize layer indices and bitmasks in `ArenaLayers` (`EnvironmentWall = 6`, `PlayerCharacter = 8`, `ArenaFloor = 9`, `GhostSpectator = 10`, `LineOfSightMask = 1 << 6`) and enforce the collision matrix at startup.
- Forbidden: Never use `LayerMask.NameToLayer` strings inside `FixedUpdateNetwork()` or `LateUpdate()` loops.
- Guardrail: `Layer 10 (GhostSpectator)` must ignore collisions with `Layer 6`, `Layer 8`, and `Layer 10`, colliding ONLY with `Layer 9 (ArenaFloor)`.

---

## Acceptance Criteria

*From GDD `design/gdd/arena-level-architecture.md`, scoped to this story:*

- [x] `ArenaLayers` defines compile-time constants `EnvironmentWall = 6`, `PlayerCharacter = 8`, `ArenaFloor = 9`, `GhostSpectator = 10`, `LineOfSightMask = 1 << 6`, and `PlayerCharacterMask = 1 << 8`.
- [x] `ArenaLayers.ApplyCollisionMatrix()` configures `Physics.IgnoreLayerCollision` so `Layer 8` collides with `6, 8, 9` (and ignores `10`), while `Layer 10 (GhostSpectator)` collides ONLY with `Layer 9 (ArenaFloor)` and ignores `6, 8, 10`.
- [x] `Physics.Linecast(origin, target, ArenaLayers.LineOfSightMask)` returns `true` (blocked) whenever a `Layer 6` wall or pillar lies between `origin` and `target`, and `false` when unobstructed or when only a `Layer 10` Ghost stands between them.

---

## Implementation Notes

*Derived from ADR-0001 Implementation Guidelines:*

Create `Assets/Scripts/Core/Config/ArenaLayers.cs` containing the layer index constants, bitmasks (`LineOfSightMask`, `PlayerCharacterMask`, `ProjectileHitMask`), and `public static void ApplyCollisionMatrix()`. Configure `ProjectSettings/TagManager.asset` and call `ArenaLayers.ApplyCollisionMatrix()` via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` and in tests so the collision matrix is guaranteed in both EditMode/PlayMode tests and runtime builds.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-arena-courtyard-and-cover-geometry.md`): Courtyard geometry and materials.
- Story 002 (`story-002-max-min-euclidean-spawn-selector.md`): `ArenaSpawnManager` spawn point math.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Core/Config/ArenaLayers.cs`
- `ProjectSettings/TagManager.asset`
- `Assets/Tests/EditMode/ArenaLayersCollisionTests.cs`
- `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: Physics Layer Collision Matrix and `LineOfSightMask (1 << 6)` block melee sectors, projectiles, and TPS camera booms on `Layer 6` while allowing `Layer 10 GhostSpectator` to pass through `Layer 6` and collide with `Layer 9`.
  - Given: Configured Unity Physics Layer Collision Matrix (`Layers 6, 8, 9, 10`) after `ArenaLayers.ApplyCollisionMatrix()`.
  - When: Querying `Physics.GetIgnoreLayerCollision` and casting `Physics.Linecast` across a `Layer 6` wall collider.
  - Then: `(8,6)=collide`, `(8,8)=collide`, `(8,9)=collide`, `(8,10)=ignore`, `(10,6)=ignore`, `(10,9)=collide`, `(10,10)=ignore`; `Physics.Linecast` with `LineOfSightMask (1 << 6)` returns `true` when blocked by a `Layer 6` wall.
  - Edge cases: Raycast starting inside or grazing the corner of an L-wall; Ghost collider on `Layer 10` does not intercept `LineOfSightMask` or `PlayerCharacterMask`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/ArenaLayersCollisionTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/arena-level-architecture/story-001-arena-courtyard-and-cover-geometry.md`
- Unlocks: `production/epics/health-damage-rpc-system/story-003-death-state-and-ghost-layer-transition.md`, `production/epics/combat-melee-ranged-system/story-001-zero-alloc-melee-sector-detection.md`, `production/epics/camera-fps-tps-spectator/story-002-tps-camera-spherecast-wall-collision.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/ArenaLayersCollisionTests.cs` (`49/49` EditMode PASS) & `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` (`7/7` PlayMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode, PlayMode, and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
