# Story 003: Post-Death Ghost Free-Roam & Follow-Target Spectator

> **Epic**: Dual-Perspective Camera (`FPS`/`TPS`) & Post-Death Ghost Spectator
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/camera-fps-tps-spectator.md`
**Requirement**: `TR-camera-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: Zero-Alloc Melee Sector Math (`90 deg` Sweep Snap-Turn), Pooled Projectiles & Dual Camera
**ADR Decision Summary**: Activates `SpectatorController` when the local player dies (`IsDead == true`), supporting Ghost Free-Roam (`WASD` at `9.0 m/s` on `Layer 10`, clamped to `[-22m, +22m]`) and Follow-Target orbital camera cycling across living survivors via `LMB` / `RMB` (or `Space` to toggle modes).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: When the currently followed survivor dies or disconnects, `SpectatorController` automatically advances to the next living survivor or falls back to Ghost Free-Roam if `<= 1` player remains.

**Control Manifest Rules (this layer)**:
- Required: Clamp Ghost Free-Roam position to `X, Z in [-22.0f, +22.0f]` and `Y in [0.5f, 8.0f]` at `9.0 m/s` (`1.5 * MoveSpeed`), and disable `Physics.SphereCast` wall pull-in during Ghost Free-Roam so spectators can glide through `Layer 6` walls smoothly.
- Forbidden: Never allow a Ghost spectator to trigger combat actions, Dash/Dodge cooldowns, or collide with living `Layer 8` players.
- Guardrail: If the followed survivor dies (`IsDead == true`) or leaves the room, `SpectatorController` must switch to the next living survivor within `1` frame without throwing `NullReferenceException`.

---

## Acceptance Criteria

*From GDD `design/gdd/camera-fps-tps-spectator.md`, scoped to this story:*

- [x] Upon local player elimination (`IsDead == true`), `SpectatorController` activates in `GhostFreeRoam` mode, allowing `WASD` movement at `9.0 m/s` (`1.5x` base walk speed) through `Layer 6` obstacles while clamping position within `X, Z in [-22.0m, +22.0m]` and `Y in [0.5m, 8.0m]`.
- [x] Pressing `LMB` (Next Survivor) or `RMB` (Previous Survivor) switches to `FollowTarget` mode, orbiting the selected living player (`IsDead == false`) with `SphereCast` wall anti-clip active and updating the top banner `"SPECTATING: [PlayerName] ([CurrentHP]/100 HP)"`.
- [x] Pressing `Space` returns from `FollowTarget` to `GhostFreeRoam`, and if a followed target dies or disconnects, `SpectatorController` automatically cycles to the next living survivor (or `GhostFreeRoam` when the match ends).

---

## Implementation Notes

*Derived from ADR-0005 Implementation Guidelines:*

Create `Assets/Scripts/Camera/SpectatorController.cs`. Maintain a non-allocating list/span of living players (`IsDead == false`). Implement `CycleTarget(int direction)`, `ClampGhostPosition(Vector3 pos)`, and `OnFollowedTargetEliminated(PlayerRef victim)` so EditMode/PlayMode tests can verify target cycling, automatic failover on target death/disconnect, and arena boundary clamping `[-22m, +22m]`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-dual-fps-tps-camera-toggle.md`): Living player `FPS`/`TPS` camera toggle.
- Story 002 (`story-002-tps-camera-spherecast-wall-collision.md`): Living player `TPS` boom collision math.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Camera/SpectatorController.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Tests/EditMode/CameraCollisionTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `SpectatorController` enables Ghost Free-Roam (`9.0 m/s` clamped to `[-22m, +22m]`) and Follow-Target survivor cycling (`LMB` next / `RMB` prev / `Space` free-roam) with automatic re-targeting on survivor death/disconnect.
  - Given: Local player with `IsDead == true` on `Layer 10` and 2 living survivors (`Player B`, `Player C`).
  - When: Moving in `GhostFreeRoam` toward `(30, 0, 30)`, pressing `LMB` to follow `Player B`, and simulating `Player B` dying (`IsDead = true`).
  - Then: Ghost position is clamped to `(22.0, Y, 22.0)`; `LMB` locks orbital camera onto `Player B`; when `Player B` dies, `SpectatorController` automatically switches to `Player C` without null reference errors; pressing `Space` returns to `GhostFreeRoam`.
  - Edge cases: Pressing `LMB`/`RMB` when `0` living survivors remain keeps camera in `GhostFreeRoam` cleanly.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/CameraControllerTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/health-damage-rpc-system/story-003-death-state-and-ghost-layer-transition.md`, `production/epics/camera-fps-tps-spectator/story-002-tps-camera-spherecast-wall-collision.md`
- Unlocks: `production/epics/combat-hud-mobile-action-pad/story-003-event-driven-screen-space-combat-hud.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/CameraCollisionTests.cs` (`69/69` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
