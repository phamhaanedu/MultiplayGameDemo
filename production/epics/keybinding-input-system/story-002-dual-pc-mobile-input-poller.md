# Story 002: Dual PC & Mobile Input Poller (`NetworkInputData`)

> **Epic**: Keybinding & Input Management System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/keybinding-input-system.md`
**Requirement**: `TR-input-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManage
**ADR Decision Summary**: Merges desktop Input System states with `MobileInputBridge` on-screen button flags via bitwise OR (`|`) into a 16-byte `NetworkInputData : INetworkInput` struct (`PackedMove`, `LookYaw`, `LookPitch`, `NetworkButtons`) with one-shot latch consumption.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Photon Fusion 2 `INetworkInput` and `NetworkButtons` struct layout is strictly `16 bytes` (`Marshal.SizeOf<NetworkInputData>() == 16`) via 32-bit word-aligned `PackedMove` (`short` X/Y fixed-point) + `float LookYaw` + `float LookPitch` + `NetworkButtons Buttons`.

**Control Manifest Rules (this layer)**:
- Required: Merge PC input state and `MobileInputBridge` state via bitwise OR (`|`) inside `LocalInputPoller` and clamp `MoveDirection` magnitude to `<= 1.0f`.
- Forbidden: Never use `UnityEngine.Input` legacy APIs or let gameplay scripts poll `Keyboard.current` / UI buttons directly.
- Guardrail: `Marshal.SizeOf<NetworkInputData>()` must be `<= 16 bytes` and `LocalInputPoller` must allocate `0 B` GC.

---

## Acceptance Criteria

*From GDD `design/gdd/keybinding-input-system.md`, scoped to this story:*

- [x] `NetworkInputData : INetworkInput` exposes `Vector2 MoveDirection` (bit-packed into `PackedMove` for exact `16-byte` Fusion IL Weaver alignment), `float LookYaw`, `float LookPitch` (clamped `[-80f, +80f]`), and `NetworkButtons Buttons` covering all 8 combat/mobility buttons (`Jump=0`, `DodgeLeft=1`, `DodgeRight=2`, `ForwardDash=3`, `VerticalSlash=4`, `HorizontalSweepLeft=5`, `HorizontalSweepRight=6`, `RangedFire=7`).
- [x] `MobileInputBridge` captures one-shot presses (`_latchedButtons`), continuous holds (`_heldButtons`), and virtual joystick `MoveVector`, clearing `_latchedButtons` only after `LocalInputPoller` consumes them in `OnInput`.
- [x] `LocalInputPoller` merges PC and Mobile inputs via `B_final = B_PC | B_Mobile`, accumulates per-frame mouse delta in `Update()`, guards LMB over uGUI widgets, and clamps `MoveDirection = Vector2.ClampMagnitude(V_PC + V_Mobile, 1.0f)` so sub-tick quick taps (`< 16.6ms`) are never dropped.

---

## Implementation Notes

*Derived from ADR-0002 Implementation Guidelines:*

Create `Assets/Scripts/Core/Input/NetworkInputData.cs`, `Assets/Scripts/Core/Input/MobileInputBridge.cs`, and `Assets/Scripts/Core/Input/LocalInputPoller.cs`. Expose deterministic `MergeInput(...)` alongside `CollectNetworkInput()` so automated tests can verify `Marshal.SizeOf<NetworkInputData>() == 16`, bitwise-OR merging, vector clamping (`<= 1.0f`), pitch clamping (`[-80f, +80f]`), and sub-tick latch retention without needing hardware input devices.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-keybinding-json-atomic-persistence.md`): JSON persistence of `keybindings.json`.
- Story 003 (`story-003-input-focus-manager-gating.md`): `InputFocusManager` bitmask state machine.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Core/Input/NetworkInputData.cs`
- `Assets/Scripts/Core/Input/MobileInputBridge.cs`
- `Assets/Scripts/Core/Input/LocalInputPoller.cs`
- `Assets/Tests/EditMode/LocalInputPollerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `LocalInputPoller` merges desktop input state with `MobileInputBridge` flags via bitwise OR into a 16-byte `NetworkInputData` struct (`MoveDirection`, `LookYaw`, `LookPitch`, `NetworkButtons`).
  - Given: `LocalInputPoller` and `MobileInputBridge` initialized in test harness.
  - When: PC input presses `MoveForward (0, 1)` + `VerticalSlash` while `MobileInputBridge` simultaneously latches `ForwardDash (Key 2)` + `MoveRight (1, 0)`, and `CollectNetworkInput()` / `MergeInput()` runs.
  - Then: `Marshal.SizeOf<NetworkInputData>() == 16`, `MoveDirection` is clamped to magnitude `1.0f` (`(0.7071f, 0.7071f)`), both `VerticalSlash` and `ForwardDash` bits are `true` in `Buttons`, and `_latchedButtons` clears after consumption.
  - Edge cases: Sub-tick mobile tap (`PressButton` + `ReleaseButton` before `OnInput` poll) still registers `1` tick in `NetworkButtons`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/LocalInputPollerTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/keybinding-input-system/story-001-keybinding-json-atomic-persistence.md`
- Unlocks: `production/epics/keybinding-input-system/story-003-input-focus-manager-gating.md`, `production/epics/character-movement-mobility/story-001-tick-accurate-walk-jump-gravity.md`, `production/epics/combat-hud-mobile-action-pad/story-002-seven-button-mobile-action-pad.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/LocalInputPollerTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
