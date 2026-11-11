# UI Evidence — Story 002: Interactive 13-Action Keybinding Rebind Modal

- **Story**: `production/epics/keybinding-rebind-lobby-ui/story-002-interactive-keybinding-rebind-modal.md`
- **Requirement**: `TR-rebind-002` (`ADR-0002`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP Perspective Render + Screen-Space Camera Settings Modal)

## Retained Screenshots
- `production/qa/evidence/story-002-interactive-keybinding-rebind-modal.png`
- `production/qa/evidence/story-002-interactive-keybinding-rebind-modal/01-keybinding-modal-swap-highlight.png`
- `production/qa/evidence/story-002-interactive-keybinding-rebind-modal/02-keybinding-modal-listening-overlay.png`

## Visual & UI Observations
1. **13-Action Table & Conflict Swap Highlight (`01-keybinding-modal-swap-highlight.png`)**: `KeybindingRebindModal` sets `InputFocusFlag.RebindModalOpen = true` on open, renders all 13 gameplay/UI actions (`MoveForward` through `ToggleChatFocus`) with crisp human-readable badges (`[W]`, `[Q]`, `[E]`, `[2]`, `[LMB]`, `[1]`, `[3]`, `[RMB]`, `[V]`, `[Enter]`), the `MouseSensitivity` slider (`1.40`), and `BTN_ResetDefaults`. Binding `ForwardDash` to `<Keyboard>/q` atomically swaps `ForwardDash <-> DodgeLeft` and highlights both swapped `KeybindingRowView` rows in Warm Amber (`#FFB703`) alongside a swap confirmation toast.
2. **Interactive Listening Overlay (`02-keybinding-modal-listening-overlay.png`)**: Clicking `[Rebind]` on `VerticalSlash` displays the `"Press any key... (5s) — [Esc] Cancel"` modal overlay backed by `PerformInteractiveRebinding()` with proper `RebindingOperation.Dispose()` lifecycle cleanup in `OnComplete`, `OnCancel`, and `OnDisable`.

## Sign-Off
- [x] Approved — Technical Artist (`2026-11-11`)
- [x] Approved — Lead Programmer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
