# Story 003: Lateral Dodge Roll (`Keys Q/E`) & Visual Root Tilt

> **Epic**: Character Movement & Tactical Mobility (`Dash Key 2` & `Dodge Q/E`)
> **Status**: Complete
> **Layer**: Core
> **Type**: Visual/Feel
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/character-movement-mobility.md`
**Requirement**: `TR-move-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Client-Authoritative Mobility (`Dash Key 2` / `Dodge Q/E`) & Victim-Authoritative Damage RPC
**ADR Decision Summary**: Implements Lateral Dodge Roll (`Keys Q/E`) at `12.0 m/s` for `15 ticks` (`0.25s`, `3.00m` lateral distance, `1.5s` shared cooldown) and applies a purely visual `360 deg` roll / `15 deg` dash pitch tilt on the child `VisualRoot` in `Render()` without rotating the physics `CapsuleCollider`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Separating the child `VisualRoot` Transform from the root `NetworkObject` Transform prevents camera roll sickness in FPS mode and preserves upright `CapsuleCollider` hit detection.

**Control Manifest Rules (this layer)**:
- Required: Lock lateral direction (`-transform.right` for `Q`, `+transform.right` for `E`) for `15 ticks` (`0.25s`) at `12.0 m/s` with a shared `1.5s` `DodgeCooldownTimer`, and apply roll/tilt animations exclusively to the child `VisualRoot` transform.
- Forbidden: Never rotate the root `NetworkObject` or `CameraPivot` on the X/Z axes during Dodge Roll or Forward Dash.
- Guardrail: If both `Q` and `E` are pressed on the same tick, `DodgeLeft (Q)` takes deterministic priority.

---

## Acceptance Criteria

*From GDD `design/gdd/character-movement-mobility.md`, scoped to this story:*

- [x] Pressing `DodgeLeft (Q)` or `DodgeRight (E)` when `DodgeCooldownTimer.ExpiredOrNotRunning` and `CurrentState == Normal` locks `LockedMobilityDirection = +-RightXZ` for `15 ticks` (`0.25s`) at `DodgeSpeed = 12.0 m/s` (`3.00m` lateral displacement) and sets `DodgeCooldownTimer = 1.5s` (`90 ticks`).
- [x] In `Render()`, `VisualRoot` tilts forward `+15 deg` around local X during `Dashing` and performs a smooth `360 deg` roll around local Z (`+360 deg` for `Q`, `-360 deg` for `E`) over `0.25s` using `Runner.LocalAlpha` sub-tick interpolation during `Dodging`, while `CameraPivot` remains horizon-level (`0 deg` Z-roll).
- [x] Simultaneous `Q + E` input on the same tick deterministically selects `DodgeLeft (-transform.right)` and prevents cancelling an active Dodge into a Dash until the `15` ticks finish.

---

## Implementation Notes

*Derived from ADR-0004 Implementation Guidelines:*

Extend `Assets/Scripts/Player/PlayerMobilityController.cs` to handle `DodgeLeft` and `DodgeRight` in `FixedUpdateNetwork()` and drive `_visualRoot.localRotation` + `_visualRoot.localScale` squash/stretch in `Render()` via `Runner.LocalAlpha` sub-tick smoothing without mutating `[Networked]` state. Ensure `ApplyGhostVisualReset()` snaps `_visualRoot.localRotation = Quaternion.identity`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-tick-accurate-walk-jump-gravity.md`): Base `WASD` walk and jump.
- Story 002 (`story-002-forward-dash-key2-and-wall-slide.md`): Forward Dash (`Key 2`) kinematics.

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

- **AC-1**: Lateral Dodge Roll (`Keys Q/E`) moves at `12.0 m/s` for `15 ticks` (`0.25s`, `3.00m`), enforces `1.5s` shared cooldown, and animates `360 deg` Z-roll on `VisualRoot` while keeping `CameraPivot` level.
  - Setup: Enter Play Mode in `Arena.unity`, trigger `DodgeLeft (Q)`, `DodgeRight (E)`, and `ForwardDash (2)` in both `TPS` and `FPS` camera perspectives.
  - Verify: Capsule displaces `3.00m +- 0.05m` laterally over `15 ticks` (`0.25s`), shared `1.5s` cooldown blocks immediate re-dodge, `VisualRoot` completes a smooth `360 deg` Z-roll during Dodge and `15 deg` X-pitch tilt during Dash, and `FPS` camera horizon never rolls.
  - Pass condition: Retained screenshots at `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/01-dodge-roll-tilt-and-ghost.png` & `02-fps-dodge-horizon-level.png` + lead sign-off at `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/story-003-dodge-roll-qe-and-visual-tilt-evidence.md` + passing unit test in `PlayerMobilityControllerTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots at `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/01-dodge-roll-tilt-and-ghost.png` & `02-fps-dodge-horizon-level.png` + sign-off at `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/story-003-dodge-roll-qe-and-visual-tilt-evidence.md` + `stamp.txt`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-movement-mobility/story-002-forward-dash-key2-and-wall-slide.md`
- Unlocks: `production/epics/combat-melee-ranged-system/story-002-horizontal-sweep-90-snap-turn.md`, `production/epics/spatial-audio-combat-vfx/story-003-mpb-hitflash-and-dotween-squash-feedback.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/01-dodge-roll-tilt-and-ghost.png`, `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/02-fps-dodge-horizon-level.png`, `production/qa/evidence/story-003-dodge-roll-qe-and-visual-tilt/story-003-dodge-roll-qe-and-visual-tilt-evidence.md`, and `Assets/Tests/EditMode/PlayerMobilityControllerTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) `1280x720` URP 3/4 TPS & FPS horizon captures + EditMode (`49/49 PASS`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
