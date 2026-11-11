# Story 003: InputFocusManager Exclusive Gating

> **Epic**: Keybinding & Input Management System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/keybinding-input-system.md`
**Requirement**: `TR-input-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManage
**ADR Decision Summary**: Tracks active UI focus flags (`ChatFocused`, `RebindModalOpen`, `MatchEnded`, `LeaderboardOpen`) via a bitwise `[Flags] enum InputFocusFlag` and suppresses gameplay movement/combat inputs whenever `_activeFocusFlags != InputFocusFlag.None`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Pure C# bitmask state management decoupled from UI rendering ensures zero race conditions when overlapping modals open and close.

**Control Manifest Rules (this layer)**:
- Required: Gate `LocalInputPoller` and `MobileInputBridge` through `InputFocusManager.IsGameplayInputAllowed` (`_activeFocusFlags == InputFocusFlag.None`).
- Forbidden: Never toggle `PlayerInput.enabled` directly from individual UI widgets without going through `InputFocusManager.SetFocusFlag(flag, active)`.
- Guardrail: Typing `WASD`, `Q/E`, `1/2/3`, or `Space` in the Chat `TMP_InputField` must produce `MoveDirection == Vector2.zero` and `Buttons == default` in `NetworkInputData`.

---

## Acceptance Criteria

*From GDD `design/gdd/keybinding-input-system.md`, scoped to this story:*

- [x] `InputFocusFlag` defines `[Flags] enum` values `None = 0`, `ChatFocused = 1 << 0`, `RebindModalOpen = 1 << 1`, `MatchEnded = 1 << 2`, `LeaderboardOpen = 1 << 3`.
- [x] `InputFocusManager.SetFocusFlag(InputFocusFlag flag, bool isActive)` sets or clears individual bit flags and exposes `bool IsGameplayInputAllowed => _activeFocusFlags == InputFocusFlag.None`, firing `OnFocusStateChanged(bool isAllowed)` when the effective state transitions.
- [x] When `IsGameplayInputAllowed == false`, `LocalInputPoller` outputs `MoveDirection = Vector2.zero` and `Buttons = default` (preserving current `LookYaw`/`LookPitch` orientation) and `MobileInputBridge` ignores pointer presses and clears active latches.

---

## Implementation Notes

*Derived from ADR-0002 Implementation Guidelines:*

Create `Assets/Scripts/Core/Input/InputFocusManager.cs`. Implement `SetFocusFlag(InputFocusFlag flag, bool active)` and `ResetAllFlags()`. Integrate `InputFocusManager` into `LocalInputPoller` and `MobileInputBridge` so any active flag immediately zeroes out gameplay movement and button bits and clears any held mobile button states (`ClearAllInput()`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-keybinding-json-atomic-persistence.md`): `keybindings.json` disk persistence.
- Story 002 (`story-002-dual-pc-mobile-input-poller.md`): Bitwise-OR input merging when `IsGameplayInputAllowed == true`.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Core/Input/InputFocusManager.cs`
- `Assets/Scripts/Core/Input/MobileInputBridge.cs`
- `Assets/Scripts/Core/Input/LocalInputPoller.cs`
- `Assets/Tests/EditMode/InputFocusManagerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `InputFocusManager` suppresses gameplay `ActionMap` and zeroes out `NetworkInputData` when `ChatFocused`, `RebindModalOpen`, or `MatchEnded` flags are active.
  - Given: `InputFocusManager` with active gameplay input (`MoveDirection = (0, 1)`, `Buttons = VerticalSlash`).
  - When: `SetFocusFlag(InputFocusFlag.ChatFocused, true)` and `SetFocusFlag(InputFocusFlag.RebindModalOpen, true)` are called sequentially, then `ChatFocused` is cleared while `RebindModalOpen` remains set.
  - Then: `IsGameplayInputAllowed` remains `false` and `NetworkInputData` outputs `MoveDirection == Vector2.zero` and `Buttons == 0` until ALL active flags are cleared (`_activeFocusFlags == InputFocusFlag.None`).
  - Edge cases: Clearing a flag that was never set is a no-op; `ResetAllFlags()` on scene transition restores `IsGameplayInputAllowed == true`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/InputFocusManagerTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/keybinding-input-system/story-002-dual-pc-mobile-input-poller.md`
- Unlocks: `production/epics/keybinding-rebind-lobby-ui/story-002-interactive-keybinding-rebind-modal.md`, `production/epics/in-game-chat-system/story-001-canvasgroup-persistent-chat-window.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/InputFocusManagerTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
