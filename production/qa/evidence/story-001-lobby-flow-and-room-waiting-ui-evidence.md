# UI Evidence — Story 001: Lobby Flow, Room Code Validation & Waiting Room UI

- **Story**: `production/epics/keybinding-rebind-lobby-ui/story-001-lobby-flow-and-room-waiting-ui.md`
- **Requirement**: `TR-rebind-001` (`ADR-0002`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP Perspective Render + Screen-Space Camera Lobby UI)

## Retained Screenshots
- `production/qa/evidence/story-001-lobby-flow-and-room-waiting-ui.png`
- `production/qa/evidence/story-001-lobby-flow-and-room-waiting-ui/01-main-lobby-entry.png`
- `production/qa/evidence/story-001-lobby-flow-and-room-waiting-ui/02-waiting-room-4slot-countdown.png`

## Visual & UI Observations
1. **Main Lobby Entry (`01-main-lobby-entry.png`)**: `LobbyViewController` renders the Deep Slate (`#161D2B`) card over the URP Arena vista with `InputField_DisplayName` (`3..16` alphanumeric/underscore chars, auto-generating `"Player_XXXX"` if blank), auto-uppercased `InputField_RoomCode` (`"DUEL_07"`), `BTN_QuickJoin`, `BTN_CreateOrJoinRoom`, `BTN_OpenLeaderboard`, and `BTN_OpenSettings`.
2. **Connection Handshake Lock & 4-Slot Waiting Room (`02-waiting-room-4slot-countdown.png`)**: Clicking Quick Join or Create/Join immediately locks Lobby action buttons (`interactable = false`) to prevent duplicate `StartGame` calls, and transitions into the 4-slot Room Waiting overlay displaying connected player rows (`3/4`), per-slot color swatches, the `[HOST]` badge on Slot 1 (`Duelist_01`), and the live `"Match starting in 3s..."` countdown banner.
3. **Validation & Timeout Recovery**: Verified in `LobbyValidationTests` that invalid room codes surface a non-blocking `ToastNotificationView` warning and `10.0s` connection timeouts cleanly unlock controls.

## Sign-Off
- [x] Approved — Technical Artist (`2026-11-11`)
- [x] Approved — Lead Programmer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
