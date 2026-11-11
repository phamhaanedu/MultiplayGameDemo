# Story 002: 7-Button Mobile Action Pad & Virtual Joystick

> **Epic**: Combat HUD, Overhead Billboard & Mobile Action Pad
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/combat-hud-mobile-action-pad.md`
**Requirement**: `TR-hud-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Observer-Driven Combat HUD, Mobile 7-Button Action Pad & Persistent RPC Chat
**ADR Decision Summary**: Implements the bottom-right ergonomic 7-button radial cluster (`BTN_SlashVertical 136x136px`, `BTN_SweepLeft 96x96px`, `BTN_SweepRight 96x96px`, `BTN_RangedFire 96x96px`, `BTN_Dash 88x88px`, `BTN_DodgeLeft 84x84px`, `BTN_DodgeRight 84x84px`) and bottom-left virtual joystick driving `MobileInputBridge` via `IPointerDownHandler`/`IPointerUpHandler`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Using `IPointerDownHandler` and `IPointerUpHandler` (instead of `Button.onClick`) guarantees zero-frame-lag touch registration on the exact frame a finger contacts the glass.

**Control Manifest Rules (this layer)**:
- Required: Implement `IPointerDownHandler` and `IPointerUpHandler` on `MobileActionButton`, route all signals to `MobileInputBridge`, display radial `Image.fillAmount` cooldown overlays, and refresh key badge labels on `KeybindingManager.OnBindingsChanged`.
- Forbidden: Never use `Button.onClick` (which waits for pointer release) for combat or mobility buttons.
- Guardrail: Every button touch target must be `>= 84x84px` at `1080p` (`>= 48x48dp` WCAG mobile touch target compliance).

---

## Acceptance Criteria

*From GDD `design/gdd/combat-hud-mobile-action-pad.md`, scoped to this story:*

- [x] `MobileActionPadView` renders all 7 action buttons in the bottom-right quadrant (`BTN_SlashVertical` `136x136px` at `(-140, 140)` surrounded by `BTN_SweepLeft [1]`, `BTN_SweepRight [3]`, `BTN_RangedFire [RMB]`, `BTN_Dash [2]`, `BTN_DodgeLeft [Q]`, `BTN_DodgeRight [E]`) plus the bottom-left virtual joystick (`240x240px`).
- [x] `MobileActionButton` implements `IPointerDownHandler` and `IPointerUpHandler`, immediately forwarding `PressButton` / `ReleaseButton` to `MobileInputBridge`, animating a `0.90x` press scale (`0.06s`), and displaying radial cooldown fill + `0.1s` step countdown text.
- [x] Subscribing to `KeybindingManager.OnBindingsChanged`, all 7 buttons dynamically update their shortcut badge text (`[LMB]`, `[1]`, `[3]`, `[RMB]`, `[2]`, `[Q]`, `[E]`) and hide (`Alpha = 0`, `interactable = false`) when the local player becomes a Ghost (`IsDead == true`).

---

## Implementation Notes

*Derived from ADR-0007 Implementation Guidelines:*

Create `Assets/Scripts/UI/HUD/MobileActionButton.cs`, `Assets/Scripts/UI/HUD/VirtualJoystick.cs`, and `Assets/Scripts/UI/HUD/MobileActionPadView.cs`. Ensure `OnDisable()` and `InputFocusManager.OnFocusStateChanged(false)` automatically release any held pointer state so buttons never stick if a modal opens mid-press.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-world-space-overhead-billboard-ui.md`): World-space overhead capsule billboard.
- Story 003 (`story-003-event-driven-screen-space-combat-hud.md`): Top match status bar, kill feed, and Victory/Defeat modal.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/HUD/MobileActionPadView.cs`
- `Assets/Scripts/UI/HUD/MobileActionButton.cs`
- `Assets/Scripts/UI/HUD/VirtualJoystick.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/CombatHudAndPadTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: Bottom-right 7-button Mobile Action Pad (`>= 84x84px` targets) triggers `MobileInputBridge` on `OnPointerDown`/`OnPointerUp`, displays radial cooldowns, and updates badges on `OnBindingsChanged`.
  - Setup: Open `Arena.unity` in Play Mode at `1920x1080` with Mobile Action Pad visible (`ForceOn` or `Auto`), click/tap each of the 7 action buttons, and rebind `ForwardDash` in Settings.
  - Verify: Every button meets minimum size (`136x136px` anchor, `84x84px..96x96px` satellites), triggers its combat/mobility action on pointer down (not up), displays radial `Image.fillAmount` cooldown sweep, and updates its key badge immediately on `OnBindingsChanged`.
  - Pass condition: Retained screenshot in `production/qa/evidence/story-002-seven-button-mobile-action-pad.png` + passing unit test in `CombatHudPresenterTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-002-seven-button-mobile-action-pad.png`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/keybinding-input-system/story-002-dual-pc-mobile-input-poller.md`
- Unlocks: `production/epics/keybinding-rebind-lobby-ui/story-003-mobile-pad-visibility-mode-toggle.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-002-seven-button-mobile-action-pad-evidence.md` + `Assets/Tests/EditMode/CombatHudAndPadTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
