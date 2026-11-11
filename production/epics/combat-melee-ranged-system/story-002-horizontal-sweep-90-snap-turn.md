# Story 002: Horizontal Sweep `90 deg` Snap-Turn Interpolation

> **Epic**: Multi-Directional Melee (`LMB`, `Keys 1/3`) & Ranged Combat (`RMB`)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/combat-melee-ranged-system.md`
**Requirement**: `TR-combat-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: Zero-Alloc Melee Sector Math (`90 deg` Sweep Snap-Turn), Pooled Projectiles & Dual Camera
**ADR Decision Summary**: Rotates the character's authoritative yaw by `-90.0 deg` (`Key 1` Left Sweep) or `+90.0 deg` (`Key 3` Right Sweep) over `HorizontalTurnTicks = 11` (`0.18s` at `60Hz`, `~8.1818 deg/tick`) in `FixedUpdateNetwork()`, locking mouse yaw during the turn and snapping camera yaw in lockstep.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Interpolating `[Networked] public float TargetSweepYaw { get; set; }` inside `FixedUpdateNetwork()` over `11` ticks ensures remote peers and the local `CameraController` observe a smooth, deterministic `90 deg` turn without camera desync.

**Control Manifest Rules (this layer)**:
- Required: Advance authoritative yaw across `HorizontalTurnTicks` (`11 ticks` = `0.18s`) in `FixedUpdateNetwork()` (`Mathf.MoveTowardsAngle(currentYaw, TargetSweepYaw, (90f / 11f))`) and suppress manual mouse look yaw until `SweepTurnTimer` expires.
- Forbidden: Never rotate only the visual mesh on `Keys 1/3` while leaving `transform.forward` or `CameraController.Yaw` at the old angle.
- Guardrail: Final yaw after `11` ticks must equal `(StartYaw +- 90.0f) mod 360.0f` within `1e-3 deg`.

---

## Acceptance Criteria

*From GDD `design/gdd/combat-melee-ranged-system.md`, scoped to this story:*

- [x] Triggering Horizontal Sweep Left (`Key 1`) or Right (`Key 3`) sets `SweepTurnTimer = 11 ticks` (`0.18s`), `StartSweepYaw = currentYaw`, and `TargetSweepYaw = NormalizeAngle(currentYaw + s * 90f)` (`s = -1` for Left, `s = +1` for Right).
- [x] Over the `11` active ticks in `FixedUpdateNetwork()`, the player's authoritative yaw advances by `90f / 11f` (`~8.1818 deg/tick`) while manual `input.LookYaw` deltas are ignored, arriving at `TargetSweepYaw` on tick `11`.
- [x] `CameraController` reads the authoritative player yaw during and after the `11`-tick turn so both `FPS` and `TPS` cameras rotate `-90 deg` or `+90 deg` in exact sync with the character's new facing direction.

---

## Implementation Notes

*Derived from ADR-0005 Implementation Guidelines:*

Extend `Assets/Scripts/Combat/PlayerCombatController.cs` with `[Networked] public TickTimer SweepTurnTimer { get; set; }`, `[Networked] public float StartSweepYaw { get; set; }`, and `[Networked] public float TargetSweepYaw { get; set; }`. Provide a deterministic step method `StepSweepTurnYaw(float currentYaw, float targetYaw, int ticksRemaining, float turnDeg = 90f, int totalTicks = 11)` so EditMode tests can assert exact `11`-tick progression from any initial angle (including wrap-around across `-180/+180 deg`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-zero-alloc-melee-sector-detection.md`): Sector hit detection and `Rpc_RequestApplyDamage` dispatch on Tick 0 of the swing.
- Story 003 (`story-003-ranged-projectile-fire-and-hit.md`): Ranged projectile firing (`RMB`).

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Combat/PlayerCombatController.cs`
- `Assets/Scripts/Player/PlayerMobilityController.cs`
- `Assets/Tests/EditMode/MeleeSectorMathTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: Horizontal Sweep (`Keys 1/3`) rotates the character and camera yaw by `-90 deg` (`Key 1`) or `+90 deg` (`Key 3`) over `11 ticks` (`0.18s`) while locking manual mouse yaw.
  - Given: Player facing `Yaw = 0 deg` with `HorizontalSweepCooldown` expired.
  - When: `HorizontalSweepLeft (Key 1)` is triggered and simulated across `11` ticks at `60Hz` while injecting conflicting mouse `LookYaw = +45 deg` input, followed by `HorizontalSweepRight (Key 3)` after cooldown.
  - Then: Conflicting mouse `LookYaw` is ignored during the `11` ticks; yaw progresses smoothly at `~8.1818 deg/tick` to `-90.0 deg` on tick `11` for `Key 1` (and `+90.0 deg` for `Key 3`).
  - Edge cases: Angle wrap-around from `+135 deg + 90 deg -> -135 deg` interpolates along the shortest `90 deg` arc without spinning `270 deg` the wrong way; dying mid-turn immediately cancels `SweepTurnTimer`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/MeleeSectorMathTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-movement-mobility/story-003-dodge-roll-qe-and-visual-tilt.md`, `production/epics/combat-melee-ranged-system/story-001-zero-alloc-melee-sector-detection.md`
- Unlocks: `production/epics/camera-fps-tps-spectator/story-001-dual-fps-tps-camera-toggle.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/MeleeSectorMathTests.cs` (`69/69` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
