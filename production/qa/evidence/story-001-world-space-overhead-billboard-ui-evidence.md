# UI Evidence — Story 001: World-Space Overhead Billboard UI

- **Story**: `production/epics/combat-hud-mobile-action-pad/story-001-world-space-overhead-billboard-ui.md`
- **Requirement**: `TR-hud-001` (`ADR-0007`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP + World-Space Canvas Render)

## Retained Screenshots
- `production/qa/evidence/story-001-world-space-overhead-billboard-ui.png`
- `production/qa/evidence/story-001-world-space-overhead-billboard-ui/01-overhead-billboards-alive-and-ghost.png`

## Visual & UI Observations
1. **Camera-Aligned World-Space Anchoring & Distance Scaling**: `OverheadBillboardUI` anchors at `Y = +2.35m` above each capsule, rotates cleanly toward the cached `MainCamera` in `LateUpdate()`, scales smoothly within `clamp(d / 8.0m, 0.75, 1.35)`, and keeps `GraphicRaycaster` disabled (`0 B` UI raycast overhead).
2. **Dual-Fill HP Bar (`Vanguard_02`)**: Instant green/crimson foreground bar reflects current HP (`60/100`) immediately on `PlayerHealth.OnHealthChanged` while the `#FFB703` Solar Amber delayed damage bar trails smoothly over `0.35s` (`Ease.OutQuad`).
3. **Ghost Elimination Presentation (`Specter_03 [GHOST]`)**: Upon elimination, the HP bar container hides completely, the nameplate appends `" [GHOST]"`, and `CanvasGroup.alpha` drops to `0.50` above the translucent `Layer 10` Ghost capsule.

## Sign-Off
- [x] Approved — UI Designer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
