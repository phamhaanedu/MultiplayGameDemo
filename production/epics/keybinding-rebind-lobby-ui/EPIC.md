# Epic: In-Game Keybinding Rebind UI & Lobby Flow UI

> **Layer**: Presentation
> **GDD**: design/gdd/keybinding-rebind-lobby-ui.md
> **Architecture Module**: LobbyAndRebindUI
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này hiện thực hóa luồng giao diện sảnh chờ (`LobbyFlowController`: Màn hình Đăng nhập, Sảnh chính, Phòng chờ Shared Mode `2–4` người) và cửa sổ tùy biến phím bấm trực quan (`KeybindingRebindModal`) có thể mở ngay tại Sảnh chờ hoặc trong trận đấu (`Escape`). Theo quyết định tại `ADR-0002`, giao diện gán phím sử dụng `PerformInteractiveRebinding` với bộ đếm thời gian chờ `5.0s`, phím `Escape` để hủy, tự động từ chối các phím hệ thống bị cấm, và tự động hoán đổi (`Swap-on-Conflict`) kèm hiệu ứng nháy sáng vàng hổ phách (`#F59E0B` trong `1.2s`) khi người chơi gán trùng phím với một hành động khác. Cửa sổ cài đặt cũng cung cấp nút `Khôi phục Mặc định` (`Reset to Defaults`) và công tắc `MobileActionPadVisibilityMode` (`Auto`, `AlwaysOn`, `Off`) để bật/tắt cụm 7 nút cảm ứng ngay lập tức.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManager | Orchestrates non-destructive `CanvasGroup` lobby/settings modals, interactive key/mouse rebinding with 5.0s timeout and conflict swapping, and `MobileActionPadVisibilityMode` persistence. | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-rebind-001 | LobbyFlowController must orchestrate Title/Login, Main Lobby, Room Waiting, and Settings Modal screens with reactive connection status and zero scene-reload overhead on modal transitions | ADR-0002 ✅ |
| TR-rebind-002 | KeybindingRebindModal must support interactive key/mouse capture with a 5.0s timeout, Esc cancel, reserved key rejection, automatic swap-on-conflict with amber highlight (#F59E0B), and Reset to Defaults | ADR-0002 ✅ |
| TR-rebind-003 | MobileActionPadVisibilityMode (Auto, AlwaysOn, Off) must persist in settings and immediately toggle the on-screen 7-button combat pad in both Lobby preview and Arena HUD | ADR-0002 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/keybinding-rebind-lobby-ui.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Lobby Flow, Room Code Validation & Waiting Room UI](story-001-lobby-flow-and-room-waiting-ui.md) | UI | M | Ready | TR-rebind-001 |
| 002 | [Interactive 13-Action Keybinding Rebind Modal](story-002-interactive-keybinding-rebind-modal.md) | UI | M | Ready | TR-rebind-002 |
| 003 | [Mobile Action Pad Visibility Mode Toggle (`Auto / ForceOn / ForceOff`)](story-003-mobile-pad-visibility-mode-toggle.md) | UI | S | Ready | TR-rebind-003 |
