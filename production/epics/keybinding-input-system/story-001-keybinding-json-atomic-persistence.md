# Story 001: Keybinding JSON Atomic Persistence & Recovery

> **Epic**: Keybinding & Input Management System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/keybinding-input-system.md`
**Requirement**: `TR-input-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManage
**ADR Decision Summary**: Persists all 14 game actions in `Application.persistentDataPath/keybindings.json` via atomic `.tmp` write (`File.WriteAllText(tmp)` + `File.Replace`/`Move`), automatically backs up (`.corrupt.bak`) and recovers from corrupt JSON, and fires `OnBindingsChanged`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: `JsonUtility.ToJson` / `FromJson` and `com.unity.inputsystem` 1.11+ path controls are verified compatible with Unity 6.6.

**Control Manifest Rules (this layer)**:
- Required: Write `keybindings.json` atomically via a `.tmp` file and dispatch `event Action OnBindingsChanged` whenever bindings load, change, or reset.
- Forbidden: Never use `PlayerPrefs` for keybinding storage or perform disk I/O inside `Update()`/`FixedUpdateNetwork()`.
- Guardrail: Corrupt or truncated `keybindings.json` must never throw an unhandled exception at startup; it must auto-recover to canonical defaults (`Q/E`, `2`, `LMB`, `1/3`, `RMB`, `V`, `Enter`).

---

## Acceptance Criteria

*From GDD `design/gdd/keybinding-input-system.md`, scoped to this story:*

- [x] `KeybindingSaveData` serializes `schemaVersion = 1`, `mobileControlMode` (`0 = Auto, 1 = ForceOn, 2 = ForceOff`), `mouseSensitivity` (`[0.1f, 5.0f]`), and all 14 `ActionBindingEntry` items (`MoveForward=W`, `MoveBackward=S`, `MoveLeft=A`, `MoveRight=D`, `Jump=Space`, `DodgeLeft=Q`, `DodgeRight=E`, `ForwardDash=2`, `VerticalSlash=LMB`, `HorizontalSweepLeft=1`, `HorizontalSweepRight=3`, `RangedFire=RMB`, `ToggleCameraView=V`, `ToggleChatFocus=Enter`).
- [x] `KeybindingManager.Save()` writes to `keybindings.json.tmp` first and atomically replaces `keybindings.json`, then invokes `OnBindingsChanged`.
- [x] `KeybindingManager.Load()` validates JSON integrity and missing action IDs; if the file is missing, corrupt, or invalid, it backs up `.corrupt.bak`, logs a warning, restores canonical defaults, overwrites a clean `keybindings.json`, and fires `OnBindingsChanged`.

---

## Implementation Notes

*Derived from ADR-0002 Implementation Guidelines:*

Create `Assets/Scripts/Core/Input/GameInputAction.cs`, `Assets/Scripts/Core/Input/IKeybindingService.cs`, `Assets/Scripts/Core/Input/KeybindingSaveData.cs`, and `Assets/Scripts/Core/Input/KeybindingManager.cs`. Allow injecting a custom file directory path in `KeybindingManager` (defaulting to `Application.persistentDataPath`) so EditMode unit tests can test atomic `.tmp` creation, `.corrupt.bak` backup & corrupt JSON recovery, reserved key rejection (`Escape`, `F1-F12`, Meta), conflict swapping (`RebindAction(action, newPath)` swaps paths if `newPath` is already bound), and `ResetToDefaults()` in an isolated temporary directory.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-dual-pc-mobile-input-poller.md`): `NetworkInputData` struct and `LocalInputPoller` bitwise-OR merging with `MobileInputBridge`.
- Story 003 (`story-003-input-focus-manager-gating.md`): `InputFocusManager` bitmask suppression during Chat and Modal states.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Core/Input/GameInputAction.cs`
- `Assets/Scripts/Core/Input/IKeybindingService.cs`
- `Assets/Scripts/Core/Input/KeybindingSaveData.cs`
- `Assets/Scripts/Core/Input/KeybindingManager.cs`
- `Assets/Tests/EditMode/KeybindingManagerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `KeybindingManager` persists all game actions in `keybindings.json` with atomic `.tmp` write, corrupt JSON auto-recovery, and `OnBindingsChanged` C# event dispatch.
  - Given: Temporary test directory path for `keybindings.json` in EditMode.
  - When: `Save()` is called after rebinding `ForwardDash` from `<Keyboard>/2` to `<Keyboard>/leftShift`, and `Load()` is called on both valid JSON and intentionally corrupted JSON (`"{bad_json"`).
  - Then: Valid save writes via `.tmp` atomic replacement and fires `OnBindingsChanged`; corrupted JSON backs up `.corrupt.bak`, logs a warning, restores all default bindings (`Q/E`, `2`, `LMB`, `1/3`, `RMB`, `V`, `Enter`), writes a clean `keybindings.json`, and does not throw unhandled exceptions.
  - Edge cases: Duplicate binding swap between two existing actions (`ForwardDash` swapped with `DodgeLeft` exchanges their paths cleanly); read-only file fallback to in-memory defaults.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/KeybindingManagerTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: None
- Unlocks: `production/epics/keybinding-input-system/story-002-dual-pc-mobile-input-poller.md`, `production/epics/keybinding-rebind-lobby-ui/story-002-interactive-keybinding-rebind-modal.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/KeybindingManagerTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
