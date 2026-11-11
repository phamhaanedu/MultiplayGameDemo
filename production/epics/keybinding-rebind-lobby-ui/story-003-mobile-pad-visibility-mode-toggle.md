# Story 003: Mobile Action Pad Visibility Mode Toggle (`Auto / ForceOn / ForceOff`)

> **Epic**: In-Game Keybinding Rebind UI & Lobby Flow
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/keybinding-rebind-lobby-ui.md`
**Requirement**: `TR-rebind-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManager
**ADR Decision Summary**: Provides a 3-state segmented toggle (`Auto = 0`, `ForceOn = 1`, `ForceOff = 2`) in the Settings modal persisted in `keybindings.json` so PC/Editor testers and mobile players can explicitly show or hide the 7-button Mobile Action Pad.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Evaluating `Application.isMobilePlatform || Touchscreen.current != null` in `Auto` mode while allowing `ForceOn` in the Unity Editor makes testing the 7-button Mobile Action Pad effortless on desktop.

**Control Manifest Rules (this layer)**:
- Required: Persist `mobileControlMode` (`0=Auto, 1=ForceOn, 2=ForceOff`) inside `KeybindingSaveData` (`keybindings.json`) and apply visibility via `CanvasGroup` (`alpha`, `interactable`, `blocksRaycasts`) on `MobileActionPadView`.
- Forbidden: Never `Destroy` `MobileActionPadView` when toggled off; use `CanvasGroup` so switching back to `ForceOn` is instantaneous.
- Guardrail: Even when `mobileControlMode == ForceOn`, `MobileActionPadView` must still hide if the local player is a dead Ghost (`IsDead == true`).

---

## Acceptance Criteria

*From GDD `design/gdd/keybinding-rebind-lobby-ui.md`, scoped to this story:*

- [x] `KeybindingRebindModal` includes a 3-way segmented control (`Auto`, `ForceOn`, `ForceOff`) bound to `KeybindingManager.MobileControlMode` and persisted in `keybindings.json`.
- [x] `MobileActionPadView.EvaluateVisibility(MobileControlMode mode, bool isMobileOrTouch, bool isLocalPlayerAlive)` returns `true` when `isLocalPlayerAlive && (mode == ForceOn || (mode == Auto && isMobileOrTouch))` and `false` when `mode == ForceOff` or `!isLocalPlayerAlive`.
- [x] Toggling `Auto / ForceOn / ForceOff` in Settings immediately updates `MobileActionPadView`'s `CanvasGroup` (`alpha`, `interactable`, `blocksRaycasts`) without requiring a scene reload.

---

## Implementation Notes

*Derived from ADR-0002 Implementation Guidelines:*

Extend `Assets/Scripts/UI/Settings/KeybindingRebindModal.cs` and `Assets/Scripts/UI/HUD/MobileActionPadView.cs` to bind the 3-way `MobileControlMode` toggle and subscribe to `KeybindingManager.OnBindingsChanged`. Expose `MobileActionPadView.ShouldShowMobilePad(int mode, bool isTouchDevice, bool isAlive)` as a pure static helper for EditMode unit tests.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-lobby-flow-and-room-waiting-ui.md`): Lobby connection flow.
- Story 002 (`story-002-interactive-keybinding-rebind-modal.md`): Interactive key rebinding rows.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/Settings/KeybindingRebindModal.cs`
- `Assets/Scripts/UI/HUD/MobileActionPadView.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/LobbyAndRebindUiTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `MobileControlMode` toggle (`Auto / ForceOn / ForceOff`) persists in `keybindings.json` and immediately updates the 7-button Mobile Action Pad visibility.
  - Setup: Open Settings Modal in Play Mode on PC/Editor, toggle `MobileControlMode` between `Auto`, `ForceOn`, and `ForceOff`, and inspect the bottom-right Mobile Action Pad and `keybindings.json`.
  - Verify: `ForceOn` immediately shows and enables the 7-button Mobile Action Pad on PC; `ForceOff` hides it (`alpha == 0`, `blocksRaycasts == false`); `Auto` shows it iff `isMobilePlatform || Touchscreen.current != null`; choice persists across `LoadBindings()`.
  - Pass condition: Retained screenshot in `production/qa/evidence/story-003-mobile-pad-visibility-mode-toggle.png` + passing unit test in `KeybindingManagerTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-003-mobile-pad-visibility-mode-toggle.png`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/combat-hud-mobile-action-pad/story-002-seven-button-mobile-action-pad.md`, `production/epics/keybinding-rebind-lobby-ui/story-002-interactive-keybinding-rebind-modal.md`
- Unlocks: None

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-003-mobile-pad-visibility-mode-toggle-evidence.md` + `Assets/Tests/EditMode/LobbyAndRebindUiTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
