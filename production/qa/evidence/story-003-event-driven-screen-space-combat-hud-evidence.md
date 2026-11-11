# UI Evidence — Story 003: Event-Driven Screen-Space Combat HUD & Pooled Kill Feed

- **Story**: `production/epics/combat-hud-mobile-action-pad/story-003-event-driven-screen-space-combat-hud.md`
- **Requirement**: `TR-hud-003` (`ADR-0007`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP + Screen-Space Combat HUD)

## Retained Screenshots
- `production/qa/evidence/story-003-event-driven-screen-space-combat-hud.png`
- `production/qa/evidence/story-003-event-driven-screen-space-combat-hud/01-combat-hud-active-lowhp-killfeed.png`
- `production/qa/evidence/story-003-event-driven-screen-space-combat-hud/02-combat-hud-victory-modal.png`

## Visual & UI Observations
1. **Active Combat HUD (`01-combat-hud-active-lowhp-killfeed.png`)**: Top status bar displays `ALIVE: 3 / 4`, 1 Hz `00:28` match timer highlighted in `#E63946` (`<= 30s`), and `[V] TPS`; center crosshair flashes the `0.15s` `#FFB703` hit-marker alongside `<< 90° SWEEP`; bottom-center displays `22 / 100` crimson HP bar with the `<= 25 HP` screen-edge crimson vignette; top-right `KillFeedPoolView` renders active eliminations from its 4 pre-warmed rows (`0` runtime `Instantiate`).
2. **Post-Match Summary Modal (`02-combat-hud-victory-modal.png`)**: Displays `VICTORY` (`#FFB703`), winner & local match stats (`Winner: Duelist_01 | Kills: 3 | Rank: #1`), `"Next round in 8s..."` countdown, and interactive `PLAY AGAIN` / `RETURN TO LOBBY` actions.

## Sign-Off
- [x] Approved — UI Designer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
