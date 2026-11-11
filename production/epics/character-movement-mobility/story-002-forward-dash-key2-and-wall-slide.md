# Story 002: Forward Dash (`Key 2`) & Wall-Slide Projection

> **Epic**: Character Movement & Tactical Mobility (`Dash Key 2` & `Dodge Q/E`)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/character-movement-mobility.md`
**Requirement**: `TR-move-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Client-Authoritative Mobility (`Dash Key 2` / `Dodge Q/E`) & Victim-Authoritative Damage RPC
**ADR Decision Summary**: Implements Forward Dash (`Key 2` / `BTN_Dash`) as a networked `TickTimer` burst (`18.0 m/s` for `12 ticks` = `0.20s`, `3.60m` total distance, `3.0s` cooldown) with `Vector3.ProjectOnPlane` wall-sliding on `Layer 6`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: `[Networked] public TickTimer ActiveMobilityTimer { get; private set; }` and `DashCooldownTimer` synchronize active burst state and HUD cooldown across ticks without GC allocation.

**Control Manifest Rules (this layer)**:
- Required: Lock `[Networked] Vector3 LockedMobilityDirection` on Dash start (`+transform.forward` on XZ plane) and project velocity via `Vector3.ProjectOnPlane(vel, hitNormal)` when contacting `Layer 6 (EnvironmentWall)`.
- Forbidden: Never use `Coroutine` or `DOTween.DOMove` to drive networked character positions during Dash.
- Guardrail: Mutual exclusion — an active Dash (`12 ticks`) blocks Dodge Roll and new Dash triggers until `ActiveMobilityTimer` expires.

---

## Acceptance Criteria

*From GDD `design/gdd/character-movement-mobility.md`, scoped to this story:*

- [x] Pressing `ForwardDash` (`Key 2` or `BTN_Dash`) when `DashCooldownTimer.ExpiredOrNotRunning` and not currently dodging locks `LockedMobilityDirection = ForwardXZ` and starts `ActiveMobilityTimer = 12 ticks` (`0.20s`) and `DashCooldownTimer = 3.0s` (`180 ticks`).
- [x] Across the `12` active ticks at `60Hz`, planar velocity overrides `WASD` input at `DashSpeed = 18.0 m/s` (`0.30m/tick`), displacing the character `3.60m` in unobstructed space.
- [x] When dashing into a `Layer 6 (EnvironmentWall)` collider at an oblique angle (`Physics.CapsuleCastNonAlloc`), velocity is projected onto the wall tangent plane via `Vector3.ProjectOnPlane(v_dash, n_wall)` so the capsule slides smoothly without snagging or clipping through the wall.

---

## Implementation Notes

*Derived from ADR-0004 Implementation Guidelines:*

Extend `Assets/Scripts/Player/PlayerMobilityController.cs` with `[Networked] public MobilityState CurrentState { get; private set; }`, `[Networked] public TickTimer DashCooldownTimer { get; private set; }`, `[Networked] public TickTimer ActiveMobilityTimer { get; private set; }`, and `[Networked] public Vector3 LockedMobilityDirection { get; private set; }`. Implement `ComputeWallSlideVelocity` + zero-GC `ProbeAndProjectWallSlide` (`Physics.CapsuleCastNonAlloc` against `Layer 6`) and expose `OnCooldownUpdated` C# event for the HUD radial timer.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-tick-accurate-walk-jump-gravity.md`): Base `WASD` walk, jump, and gravity.
- Story 003 (`story-003-dodge-roll-qe-and-visual-tilt.md`): Lateral Dodge Roll (`Keys Q/E`) and `VisualRoot` tilt/roll animations.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Player/PlayerMobilityController.cs`
- `Assets/Tests/EditMode/PlayerMobilityControllerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: Forward Dash (`Key 2`) locks `LockedMobilityDirection` at `18.0 m/s` for `12 ticks` (`3.60m`), enforces `3.0s` cooldown, and slides along `Layer 6` walls via `Vector3.ProjectOnPlane`.
  - Given: Grounded player with `DashCooldownTimer` expired facing `+Z (0, 0, 1)`.
  - When: `ForwardDash` button is pressed on Tick 0 and simulated for `12` ticks (`0.20s`), including an oblique `45 deg` wall normal collision check.
  - Then: Velocity is locked at `(0, 0, 18.0f)` for exactly `12` ticks covering `3.60m +- 0.05m`, `DashCooldownTimer` is set to `3.0s` (`180 ticks`), and oblique wall collision projects velocity via `Vector3.ProjectOnPlane` without penetrating `Layer 6`.
  - Edge cases: Pressing `ForwardDash` while `DashCooldownTimer` is running (`Tick 179` rejected, `Tick 180` accepted) or while `CurrentState == Dodging` is rejected; head-on `90 deg` wall impact zeroes forward component cleanly.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/PlayerMobilityControllerTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-movement-mobility/story-001-tick-accurate-walk-jump-gravity.md`
- Unlocks: `production/epics/character-movement-mobility/story-003-dodge-roll-qe-and-visual-tilt.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/PlayerMobilityControllerTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
