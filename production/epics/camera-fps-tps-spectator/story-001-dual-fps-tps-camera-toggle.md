# Story 001: Dual-Perspective Camera Toggle (`FPS` / `TPS` via `Key V`)

> **Epic**: Dual-Perspective Camera (`FPS`/`TPS`) & Post-Death Ghost Spectator
> **Status**: Complete
> **Layer**: Feature
> **Type**: Visual/Feel
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/camera-fps-tps-spectator.md`
**Requirement**: `TR-camera-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: Zero-Alloc Melee Sector Math (`90 deg` Sweep Snap-Turn), Pooled Projectiles & Dual Camera
**ADR Decision Summary**: Binds a single scene `MainCamera` to the local player in `Spawned()` (`Object.HasInputAuthority == true`), supports instant `Key V` toggling between `TPS` (`Distance = 4.5m`, `Height = 1.8m`, `ShoulderOffset = +0.45m`) and `FPS` (`EyeHeight = 1.5m`) in `LateUpdate()`, and hides the local body mesh in `FPS` via `ShadowCastingMode.ShadowsOnly`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: HIGH
**Engine Notes**: Using `ShadowCastingMode.ShadowsOnly` on the local player's body/visor `MeshRenderer` in `FPS` mode prevents inner-faces clipping into the near plane while preserving the local player's ground shadow in URP.

**Control Manifest Rules (this layer)**:
- Required: Attach only one `Camera` + `AudioListener` in `Arena.unity`, bind it to the local player (`Object.HasInputAuthority`), update camera transforms in `LateUpdate()`, and set local body renderers to `ShadowCastingMode.ShadowsOnly` in `FPS` (`ShadowCastingMode.On` in `TPS`).
- Forbidden: Never place a `Camera` or `AudioListener` component inside the networked `PlayerCapsule.prefab`.
- Guardrail: Toggling `Key V` must preserve exact world aim orientation (`Yaw` and `Pitch`) with `0` frame stutter.

---

## Acceptance Criteria

*From GDD `design/gdd/camera-fps-tps-spectator.md`, scoped to this story:*

- [x] `CameraController` binds to the local player (`Object.HasInputAuthority == true`) in `Spawned()` and defaults to `TPS` mode (`Height = 1.8m`, `Distance = 4.5m`, `ShoulderOffset = +0.45m`, pitch clamped `[-60 deg, +75 deg]`).
- [x] Pressing `ToggleCameraView` (`Key V`, debounced `0.15s` and gated by `InputFocusManager.IsGameplayInputAllowed`) switches between `TPS` and `FPS` (`EyeHeight = 1.5m`, `Distance = 0.0m`, pitch clamped `[-80 deg, +80 deg]`) while preserving current `Yaw` and `Pitch`.
- [x] Entering `FPS` mode sets the local player's body and visor `MeshRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly` (preventing camera clipping while keeping ground shadows) and hides the local player's own overhead billboard UI; returning to `TPS` restores `ShadowCastingMode.On`.

---

## Implementation Notes

*Derived from ADR-0005 Implementation Guidelines:*

Create `Assets/Scripts/Camera/CameraController.cs`. Maintain `CameraPerspectiveMode { TPS = 0, FPS = 1 }`. In `LateUpdate()`, read interpolated position and authoritative yaw (including any active `90 deg` Horizontal Sweep turn from `PlayerCombatController`) and position the camera pivot. Expose `TogglePerspective()` and `SetPerspective(CameraPerspectiveMode mode)` for deterministic testing.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-tps-camera-spherecast-wall-collision.md`): `Physics.SphereCast` (`r = 0.25m`) boom arm wall collision clamping on `Layer 6`.
- Story 003 (`story-003-post-death-ghost-spectator-controller.md`): Post-death Ghost Free-Roam and Follow-Target spectator camera modes.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Camera/CameraController.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Prefabs/Player/PlayerCapsule.prefab`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/CameraCollisionTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `CameraController` toggles between `TPS` (`Distance=4.5m`, `Height=1.8m`, `ShoulderOffset=0.45m`) and `FPS` (`EyeHeight=1.5m`, `ShadowCastingMode.ShadowsOnly`) via `Key V` in `LateUpdate()`.
  - Setup: Enter Play Mode in `Arena.unity` as local player, observe initial `TPS` framing, press `Key V` to switch to `FPS`, and press `Key V` again to return to `TPS`.
  - Verify: `TPS` places camera at `Distance = 4.5m`, `Height = 1.8m`, `ShoulderOffset = +0.45m` with visible capsule mesh (`ShadowCastingMode.On`); `FPS` places camera at `EyeHeight = 1.5m`, switches local capsule mesh to `ShadowCastingMode.ShadowsOnly` (ground shadow still visible, zero inner-mesh clipping), and preserves aim `Yaw`/`Pitch`.
  - Pass condition: Retained screenshots of both `TPS` and `FPS` views in `production/qa/evidence/story-001-dual-fps-tps-camera-toggle.png` + lead sign-off in `production/qa/evidence/story-001-dual-fps-tps-camera-toggle-evidence.md` + passing unit test in `CameraControllerTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: a retained screenshot in `production/qa/evidence/` + sign-off in `production/qa/evidence/story-001-dual-fps-tps-camera-toggle-evidence.md`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/combat-melee-ranged-system/story-002-horizontal-sweep-90-snap-turn.md`
- Unlocks: `production/epics/camera-fps-tps-spectator/story-002-tps-camera-spherecast-wall-collision.md`, `production/epics/camera-fps-tps-spectator/story-003-post-death-ghost-spectator-controller.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-001-dual-fps-tps-camera-toggle-evidence.md` + `Assets/Tests/EditMode/CameraCollisionTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
