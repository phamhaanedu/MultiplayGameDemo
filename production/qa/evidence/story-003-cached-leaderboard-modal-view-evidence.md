# UI Evidence — Story 003: Cached Top-20 Leaderboard Modal View (`15.0s` TTL)

- **Story**: `production/epics/playfab-cloud-leaderboard/story-003-cached-leaderboard-modal-view.md`
- **Requirement**: `TR-playfab-003` (`ADR-0008`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP + `LeaderboardCanvas`)

## Retained Screenshots
- `production/qa/evidence/story-003-cached-leaderboard-modal-view.png`
- `production/qa/evidence/story-003-cached-leaderboard-modal-view/01-leaderboard-top20-gold-silver-bronze-cyan.png`

## Visual & UI Observations
1. **Top-20 Leaderboard Modal (`01-leaderboard-top20-gold-silver-bronze-cyan.png`)**: Displays the `780x610` tactical slate modal over the 3D Arena with `TOTAL WINS` (active `#00F5D4` Electric Cyan tab) and `TOTAL KILLS` tabs, `REFRESH` (`5.0s` manual cooldown) and `CLOSE [X]` (`#E63946`) buttons, podium accents (`#1 ApexStriker` in `#FFD166` Gold, `#2 SolarBlade` in `#E0E1DD` Silver, `#3 CrimsonViper` in `#CD7F32` Bronze), local player row `#4 ArenaHero (YOU)` highlighted in `#00F5D4` Electric Cyan, and the sticky personal stats footer (`YOUR RECORD (ArenaHero) | Wins: 36 | Kills: 114 | Matches: 58`) backed by 20 pre-warmed `LeaderboardRowView` items (`0` runtime `Instantiate`).

## Sign-Off
- [x] Approved — UI Designer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
