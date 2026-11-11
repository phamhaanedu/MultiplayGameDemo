# Story 002: Interactive 13-Action Keybinding Rebind Modal

> **Epic**: In-Game Keybinding Rebind UI & Lobby Flow
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/keybinding-rebind-lobby-ui.md`
**Requirement**: `TR-rebind-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManager
**ADR Decision Summary**: Provides an interactive `KeybindingRebindModal` for all 13 actions using `PerformInteractiveRebinding()` with a `5.0s` timeout, `Escape` cancel, automatic duplicate-key swap with `#FFB703` highlight toast, and `Reset to Defaults`, while holding `InputFocusFlag.RebindModalOpen`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Always `Dispose()` the `RebindingOperation` inside `OnComplete`, `OnCancel`, and `OnDisable` to prevent native memory leaks in `com.unity.inputsystem`.

**Control Manifest Rules (this layer)**:
- Required: Set `InputFocusManager.SetFocusFlag(InputFocusFlag.RebindModalOpen, true)` when the modal opens (and `false` on close) and always dispose `InputActionRebindingExtensions.RebindingOperation`.
- Forbidden: Never allow reserved system keys (`Escape`, `F1..F12`, `Windows/Command`) to be bound to gameplay actions.
- Guardrail: Rebinding a key that is already assigned to another action must swap the two bindings atomically and highlight both rows in `#FFB703` (`1.2s`) instead of leaving an unbound action.

---

## Acceptance Criteria

*From GDD `design/gdd/keybinding-rebind-lobby-ui.md`, scoped to this story:*

- [x] Opening `KeybindingRebindModal` sets `InputFocusFlag.RebindModalOpen = true` and lists all 13 actions (`MoveForward..ToggleChatFocus`) with their human-readable binding badges (`[W]`, `[Q]`, `[E]`, `[2]`, `[LMB]`, `[1]`, `[3]`, `[RMB]`, `[V]`, `[Enter]`) plus the `MouseSensitivity` slider (`[0.1, 5.0]`).
- [x] Clicking `[Rebind]` on any row starts `PerformInteractiveRebinding()` with a `"Press any key... (5s)"` overlay: pressing `Escape` or waiting `5.0s` cancels cleanly; pressing an already-used key swaps the two actions' bindings and flashes both rows `#FFB703` with a notification toast.
- [x] Clicking `BTN_ResetDefaults` restores all 13 bindings to canonical defaults (`Q/E` Dodge, `2` Dash, `LMB` Vertical Slash, `1/3` Horizontal Sweep, `RMB` Ranged, `V` Camera, `Enter` Chat) and persists `keybindings.json`.

---

## Implementation Notes

*Derived from ADR-0002 Implementation Guidelines:*

Create `Assets/Scripts/UI/Settings/KeybindingRebindModal.cs` and `Assets/Scripts/UI/Settings/KeybindingRowView.cs`. Ensure `CancelActiveRebind()` is called in `OnDisable()` and if the match transitions to `MatchState.Ended` while the modal is open.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-lobby-flow-and-room-waiting-ui.md`): Main Lobby and Room Waiting screens.
- Story 003 (`story-003-mobile-pad-visibility-mode-toggle.md`): `MobileControlMode` 3-way segmented toggle.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/Settings/KeybindingRebindModal.cs`
- `Assets/Scripts/UI/Settings/KeybindingRowView.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/LobbyAndRebindUiTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `KeybindingRebindModal` supports interactive rebind for all 13 actions (`5.0s` timeout, `Escape` cancel, conflict swap with `#FFB703` highlight, and `Reset to Defaults`) while holding `InputFocusFlag.RebindModalOpen`.
  - Setup: Open Settings & Controls Modal in Play Mode, click `[Rebind]` on `ForwardDash [2]`, press `Q` (already bound to `DodgeLeft`), test `Escape` cancel on another row, and click `Reset to Defaults`.
  - Verify: While modal is open, capsule does not move or attack; pressing `Q` swaps `ForwardDash -> [Q]` and `DodgeLeft -> [2]` with `#FFB703` highlight toast; `Escape` cancels rebind cleanly; `Reset to Defaults` restores all 13 canonical keys.
  - Pass condition: Retained screenshot of the Keybinding Rebind Modal (including Listening/Swap states) in `production/qa/evidence/story-002-interactive-keybinding-rebind-modal.png` + passing unit test in `KeybindingManagerTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-002-interactive-keybinding-rebind-modal.png`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/keybinding-input-system/story-001-keybinding-json-atomic-persistence.md`, `production/epics/keybinding-input-system/story-003-input-focus-manager-gating.md`, `production/epics/keybinding-rebind-lobby-ui/story-001-lobby-flow-and-room-waiting-ui.md`
- Unlocks: `production/epics/keybinding-rebind-lobby-ui/story-003-mobile-pad-visibility-mode-toggle.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-002-interactive-keybinding-rebind-modal-evidence.md` + `Assets/Tests/EditMode/LobbyAndRebindUiTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
