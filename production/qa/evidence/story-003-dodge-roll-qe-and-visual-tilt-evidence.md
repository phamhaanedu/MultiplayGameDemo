# Visual & Feel Evidence — Story 003: Lateral Dodge Roll (`Keys Q/E`) & Visual Root Tilt

- **Story**: `production/epics/character-movement-mobility/story-003-dodge-roll-qe-and-visual-tilt.md`
- **Requirement**: `TR-move-003` (`ADR-0004`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP 17 Perspective Render)

## Retained Screenshots
- `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/01-dodge-roll-tilt-and-ghost.png`
- `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/02-fps-dodge-horizon-level.png`
- `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt-01.png`
- `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt.png`

## Visual & Kinematic Observations
1. **Child `VisualRoot` Roll & Pitch Separation (`01-dodge-roll-tilt-and-ghost.png`)**:
   - **Left Capsule (`DodgingLeft`)**: Demonstrates local Z-roll interpolation (`PlayerMobilityController.ComputeVisualRootLocalRotation` -> `±360°` over `15` ticks / `0.25s`, sub-tick smoothed via `Runner.LocalAlpha`) on `VisualRoot` (`#E63946` body + `#F1FAEE` direction visor) while the root physics `CapsuleCollider` stays strictly upright (`0°` Z-roll).
   - **Center Capsule (`Dashing`)**: Demonstrates `+15°` forward X-pitch tilt (`DashVisualPitchDeg`) on `VisualRoot` from an angled 3/4 view during `Key 2` Forward Dash (`18.0 m/s`, `12` ticks / `0.20s`).
   - **Right Capsule (`GhostSpectator`)**: Demonstrates simultaneous contrast against translucent cyan-grey `#A8DADC` (`Alpha = 0.35`) Ghost state over the `#F4A261` central altar and `#3A3D56` metric grid floor.
2. **FPS Horizon Stability (`02-fps-dodge-horizon-level.png`)**:
   - Captured directly from `CameraPivot` while `VisualRoot` is rolled `72°` on Z during an active Dodge Roll — confirms `0°` Z-roll on `CameraPivot` and a completely level courtyard horizon (zero motion-sickness camera roll).
3. **Deterministic Simulation**: Verified by `PlayerMobilityControllerTests.DodgeRoll_LeftAndRightAndGhostSuppression_Covers3MetersShares90TickCooldownAndPreservesCameraRoll` and `ForwardDash_Locks12TicksCovers3Point6mSlidesAlongPhysicalLayer6WallAndHonors180TickCooldown`.

## Sign-Off
- [x] Approved — Gameplay Programmer / Lead (`2026-11-11`)
- [x] Approved — Technical Artist (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
