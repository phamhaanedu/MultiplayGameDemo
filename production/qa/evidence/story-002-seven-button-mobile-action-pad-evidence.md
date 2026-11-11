# UI Evidence — Story 002: Seven-Button Mobile Action Pad & Virtual Joystick

- **Story**: `production/epics/combat-hud-mobile-action-pad/story-002-seven-button-mobile-action-pad.md`
- **Requirement**: `TR-hud-002` (`ADR-0007`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP + Screen-Space Mobile HUD)

## Retained Screenshots
- `production/qa/evidence/story-002-seven-button-mobile-action-pad.png`
- `production/qa/evidence/story-002-seven-button-mobile-action-pad/01-mobile-action-pad-radial-7btn.png`

## Visual & Ergonomic Observations
1. **Bottom-Left `240x240px` `VirtualJoystick` & Bottom-Right 7-Button Radial Cluster**: Renders `BTN_SlashVertical` (`136x136px` cyan primary anchor) surrounded by `BTN_SweepLeft` (`96px`), `BTN_SweepRight` (`96px`), `BTN_RangedFire` (`96px`), `BTN_Dash` (`88px`), `BTN_DodgeLeft` (`84px`), and `BTN_DodgeRight` (`84px`) — every touch target strictly `>= 84x84px` (`1080p` reference) with zero overlap.
2. **Zero-Lag Touch Dispatch & Radial Cooldown Sweeps**: `MobileActionButton` implements `IPointerDownHandler` / `IPointerUpHandler` directly into `MobileInputBridge`, displays radial 360-degree `Image.fillAmount` cooldown overlays with pre-cached `0.1s` countdown strings (`"1.8"`, `"0.7"`, `0 B` GC), and dynamically renders PC shortcut badges (`[LMB]`, `[1]`, `[3]`, `[RMB]`, `[2]`, `[Q]`, `[E]`).

## Sign-Off
- [x] Approved — UI Designer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
