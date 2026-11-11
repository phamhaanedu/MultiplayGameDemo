# Visual & Feel Evidence — Story 001: Arena Courtyard & Tactical Cover Geometry

- **Story**: `production/epics/arena-level-architecture/story-001-arena-courtyard-and-cover-geometry.md`
- **Requirement**: `TR-arena-001` (`ADR-0001`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP 17 Perspective Render)

## Retained Screenshots
- `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry/01-arena-courtyard-overview.png`
- `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry-01.png`
- `production/qa/evidence/story-001-arena-courtyard-and-cover-geometry.png`

## Visual & Layout Observations
1. **Symmetric `40m x 40m` Tactical Courtyard & Metric Grid**: `ArenaFloor` spans `[-20m, +20m]` on `X/Z` assigned to `Layer 9 (ArenaFloor)` with deep slate-grey `#2B2D42` base and `#3A3D56` 1m metric grid lines (`T_ArenaFloorGrid.png` tiled `40x40` on `M_ArenaFloor.mat`).
2. **Perimeter, L-Walls, and Cover Pillars**: Four outer perimeter walls (`Height = 2.5m`, `Thickness = 1.0m`), 4 quadrant L-shaped corner walls (`Height = 2.5m`), and 8 radially symmetric cylindrical cover pillars (`Radius = 0.75m`, `Height = 2.5m`) are assigned to `Layer 6 (EnvironmentWall)` using `#5C6274` (`M_CoverWall.mat`).
3. **Central Raised Altar & Corridor Widths**: `CentralAltar` (`6m x 6m`, `Height = 0.4m`, `#F4A261` `M_CentralAltar.mat`) with 4 sloped access ramps sits at `(0, 0, 0)`. `ArenaLayoutValidator.ValidateArenaHierarchy` verified in `ArenaLayersCollisionTests.ArenaScene_LoadedInEditMode_PassesAllCourtyardClearanceAndSpawnInvariants` confirms `0` `MeshCollider` instances, wall/pillar heights `>= 2.5m`, and minimum obstacle passage clearance `W_gap >= 2.5m` across all interior obstacles and perimeter walls.
4. **Draw Call Budget**: Static primitive `BoxCollider` and `CapsuleCollider` meshes with SRP Batcher URP Lit materials render well below the `< 80` batch budget.

## Sign-Off
- [x] Approved — Level Designer (`2026-11-11`)
- [x] Approved — Technical Artist (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
