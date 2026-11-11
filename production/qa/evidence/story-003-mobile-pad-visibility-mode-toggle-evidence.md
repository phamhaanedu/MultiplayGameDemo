# UI Evidence — Story 003: Mobile Action Pad Visibility Mode Toggle (`Auto / ForceOn / ForceOff`)

- **Story**: `production/epics/keybinding-rebind-lobby-ui/story-003-mobile-pad-visibility-mode-toggle.md`
- **Requirement**: `TR-rebind-003` (`ADR-0002`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP Perspective Render + Screen-Space Camera HUD)

## Retained Screenshots
- `production/qa/evidence/story-003-mobile-pad-visibility-mode-toggle.png`
- `production/qa/evidence/story-003-mobile-pad-visibility-mode-toggle/01-mobile-pad-force-on-toggle.png`
- `production/qa/evidence/story-003-mobile-pad-visibility-mode-toggle/02-mobile-pad-force-off-toggle.png`

## Visual & UI Observations
1. **Immediate `ForceOn` Visibility on Desktop/Editor (`01-mobile-pad-force-on-toggle.png`)**: Selecting `ForceOn` in the 3-way segmented control (`Auto / ForceOn / ForceOff`) inside `KeybindingRebindModal` persists `mobilePadMode` in `keybindings.json` and immediately sets `MobileActionPadView`'s `CanvasGroup` to `alpha = 1.0`, `interactable = true`, and `blocksRaycasts = true`, displaying both the virtual joystick and the 7-button radial combat cluster without reloading the scene.
2. **Zero-Allocation `ForceOff` Hide (`02-mobile-pad-force-off-toggle.png`)**: Switching to `ForceOff` transitions the same `MobileActionPadView` `CanvasGroup` to `alpha = 0.0`, `interactable = false`, and `blocksRaycasts = false` (`0` `Destroy` calls), leaving an unobstructed PC Arena HUD view while preserving instant re-activation and dead-Ghost suppression (`isAlive == false`).

## Sign-Off
- [x] Approved — Technical Artist (`2026-11-11`)
- [x] Approved — Lead Programmer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
