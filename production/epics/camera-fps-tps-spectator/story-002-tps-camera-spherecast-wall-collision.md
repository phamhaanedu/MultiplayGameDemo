# Story 002: TPS Camera `SphereCast` Wall Collision Anti-Clip

> **Epic**: Dual-Perspective Camera (`FPS`/`TPS`) & Post-Death Ghost Spectator
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/camera-fps-tps-spectator.md`
**Requirement**: `TR-camera-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: Zero-Alloc Melee Sector Math (`90 deg` Sweep Snap-Turn), Pooled Projectiles & Dual Camera
**ADR Decision Summary**: Casts `Physics.SphereCast(pivotPos, 0.25f, boomDir, out RaycastHit hit, 4.5f, ArenaLayers.LineOfSightMask)` in `TPS` mode and clamps boom length to `Mathf.Clamp(hit.distance - 0.15f, 0.6f, 4.5f)` so the camera never clips inside `Layer 6` walls or pillars while ignoring `Layer 8` players and `Layer 10` Ghosts.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: HIGH
**Engine Notes**: Asymmetric damping (`25f` pull-in speed on wall hit, `10f` push-out recovery speed) prevents camera jitter when backing into L-walls or cylindrical cover pillars.

**Control Manifest Rules (this layer)**:
- Required: Query `Physics.SphereCast` with `radius = 0.25f` exclusively against `ArenaLayers.LineOfSightMask (1 << 6)` and clamp boom distance to `[0.6f, 4.5f]`.
- Forbidden: Never include `Layer 8 (PlayerCharacter)` or `Layer 10 (GhostSpectator)` in the camera collision mask (which would cause the camera to violently zoom in during melee combat).
- Guardrail: If boom distance is compressed below `0.85m` in tight corners, temporarily switch the local player mesh to `ShadowCastingMode.ShadowsOnly` so the camera does not clip inside the capsule's back.

---

## Acceptance Criteria

*From GDD `design/gdd/camera-fps-tps-spectator.md`, scoped to this story:*

- [x] `CameraController.ComputeSafeBoomDistance(Vector3 pivotPos, Vector3 boomDir, float maxDist = 4.5f)` casts `Physics.SphereCast` (`r = 0.25m`) against `ArenaLayers.LineOfSightMask (1 << 6)` and returns `Mathf.Clamp(hit.distance - 0.15f, 0.6f, maxDist)` on hit or `maxDist` when unobstructed.
- [x] Other players (`Layer 8`) and Ghost spectators (`Layer 10`) passing between the camera and the local player never trigger boom pull-in.
- [x] When `currentBoomDist < 0.85f` against a tight corner wall in `TPS` mode, the local capsule mesh automatically switches to `ShadowCastingMode.ShadowsOnly` and restores `ShadowCastingMode.On` once `currentBoomDist >= 0.85f`.

---

## Implementation Notes

*Derived from ADR-0005 Implementation Guidelines:*

Extend `Assets/Scripts/Camera/CameraController.cs` with `public static float CalculateClampedBoomDistance(bool didHit, float hitDistance, float minDistance = 0.6f, float maxDistance = 4.5f, float wallBuffer = 0.15f)` and integrate `Physics.SphereCast` in `LateUpdate()` with asymmetric smoothing (`Lerp` factor `25f * Time.deltaTime` when shortening, `10f * Time.deltaTime` when lengthening).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-dual-fps-tps-camera-toggle.md`): `Key V` `FPS`/`TPS` perspective switching.
- Story 003 (`story-003-post-death-ghost-spectator-controller.md`): Post-death Ghost spectator modes.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Camera/CameraController.cs`
- `Assets/Tests/EditMode/CameraCollisionTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `TPS` camera `SphereCast` (`r = 0.25m`) against `LineOfSightMask (Layer 6)` clamps boom length to `[0.6m, 4.5m]` without clipping walls or reacting to `Layer 8/10` capsules.
  - Given: `CameraController` in `TPS` mode (`maxDist = 4.5m`, `minDist = 0.6m`, `buffer = 0.15m`) and a `Layer 6` wall at `hit.distance = 1.80m` behind the player.
  - When: `ComputeSafeBoomDistance()` and `SphereCast` execute against `Layer 6` wall vs `Layer 8` player collider.
  - Then: Wall at `1.80m` clamps target boom distance to `1.65m` (`1.80 - 0.15`); wall at `0.40m` clamps to floor `0.60m` and triggers `ShadowCastingMode.ShadowsOnly` (`< 0.85m`); `Layer 8` and `Layer 10` colliders are ignored (`boomDist == 4.50m`).
  - Edge cases: Backing into a `90 deg` L-wall corner keeps the camera inside the arena bounds with zero see-through clipping outside the perimeter wall.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/CameraControllerTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/arena-level-architecture/story-003-physics-layer-collision-matrix.md`, `production/epics/camera-fps-tps-spectator/story-001-dual-fps-tps-camera-toggle.md`
- Unlocks: `production/epics/camera-fps-tps-spectator/story-003-post-death-ghost-spectator-controller.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/CameraCollisionTests.cs` (`69/69` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
