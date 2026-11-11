# Visual & Feel Evidence — Story 001: Dual FPS/TPS Camera Toggle (`Key V`)

- **Story**: `production/epics/camera-fps-tps-spectator/story-001-dual-fps-tps-camera-toggle.md`
- **Requirement**: `TR-camera-001` (`ADR-0005`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP Perspective Render + Screen-Space Camera HUD)

## Retained Screenshots
- `production/qa/evidence/story-001-dual-fps-tps-camera-toggle.png`
- `production/qa/evidence/story-001-dual-fps-tps-camera-toggle/01-tps-over-shoulder-view.png`
- `production/qa/evidence/story-001-dual-fps-tps-camera-toggle/02-fps-eye-level-view.png`

## Visual & Feel Observations
1. **Default TPS Over-Shoulder Framing (`01-tps-over-shoulder-view.png`)**: `CameraController` drives the single scene `MainCamera` (`0` cameras/listeners inside `PlayerCapsule.prefab`) at `Height = 1.8m`, `Distance = 4.5m`, and `ShoulderOffset = +0.45m` right, giving clear peripheral sightlines around L-walls and cover pillars with `[V] TPS` displayed in the top HUD status bar.
2. **Instant `Key V` Switch to Eye-Level FPS (`02-fps-eye-level-view.png`)**: Switching to `CameraPerspectiveMode.FPS` anchors `MainCamera` at `(0, 1.5m, 0)` preserving exact world yaw (`25 deg`) with zero snapping, switches local capsule body/visor renderers to `ShadowCastingMode.ShadowsOnly` (eliminating 100% of back-face mesh clipping while retaining ground shadow projection), hides the local overhead billboard, and updates the HUD badge to `[V] FPS`.
3. **Debounce & Focus Protection**: Verified in `CameraControllerTests` that rapid `< 0.15s` toggles and active UI overlays (`InputFocusFlag != None`) cleanly suppress unintended perspective switches.

## Sign-Off
- [x] Approved — Technical Artist (`2026-11-11`)
- [x] Approved — Lead Programmer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
