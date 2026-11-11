# Epic: Combat HUD, Overhead Billboard & Mobile Action Pad UI

> **Layer**: Presentation
> **GDD**: design/gdd/combat-hud-mobile-action-pad.md
> **Architecture Module**: CombatHUDAndPad
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này hiện thực hóa giao diện chiến đấu phản ứng theo sự kiện (`ScreenSpaceCombatHUD`), thanh máu nổi 3D trên đầu mỗi nhân vật (`WorldSpaceBillboardUI`), và cụm 7 nút kỹ năng cảm ứng ở góc dưới phải màn hình (`MobileActionPadView`). Theo quyết định tại `ADR-0007`, thanh máu Billboard luôn xoay hướng về `Camera.main` trong `LateUpdate()`, sử dụng bảng màu phân biệt rõ ràng (`#22C55E` cho bản thân, `#EF4444` cho kẻ địch) kèm lớp bóng sát thương trễ `0.25s` (`#FACC15`). Cụm 7 nút `MobileActionPadView` (`LMB` Chém dọc, `Keys 1/3` Chém ngang Trái/Phải, `RMB` Bắn xa, `Key 2` Lướt tiến, `Keys Q/E` Lăn né) vừa nhận thao tác chạm cảm ứng (`IPointerDownHandler` ghi vào `MobileInputBridge`), vừa đồng bộ hiệu ứng lún nút (`0.90x scale`), vòng quạt hồi chiêu (`Image.fillAmount`) và huy hiệu phím tắt (`Keybind Badge`) khi người chơi thao tác trên bàn phím/chuột PC, với cam kết không gọi `GetComponent`/`Find` hay tạo rác chuỗi `string` trong vòng lặp khung hình.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0007: Event-Driven Combat HUD, 7-Button Mobile Action Pad & CanvasGroup Persistent Chat | Builds observer-driven HUD, `LateUpdate` world-space health billboards, and the 7-button radial `MobileActionPadView` with zero per-frame string allocations. | MEDIUM |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-hud-001 | WorldSpaceBillboardUI must orient toward Camera.main in LateUpdate, render HP fill with 0.25s ghost damage trail, and enforce distinct local (#22C55E) vs enemy (#EF4444) color palettes | ADR-0007 ✅ |
| TR-hud-002 | MobileActionPadView must provide a 7-button radial cluster in the bottom-right corner with radial fill cooldown overlays, remaining seconds text, keybind badges, and dual PointerDown/Keybind pulse feedback | ADR-0007 ✅ |
| TR-hud-003 | ScreenSpaceCombatHUD must update HP bar, AliveCounter, KillFeed (max 4 entries), and DirectionalHitVignette strictly via C# Observer events and OnChangedRender callbacks without per-frame Find or GetComponent calls | ADR-0007 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/combat-hud-mobile-action-pad.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [World-Space Overhead Billboard UI (`OverheadBillboardUI`)](story-001-world-space-overhead-billboard-ui.md) | UI | S | Ready | TR-hud-001 |
| 002 | [7-Button Mobile Action Pad & Virtual Joystick](story-002-seven-button-mobile-action-pad.md) | UI | M | Ready | TR-hud-002 |
| 003 | [Event-Driven Screen-Space Combat HUD & Match End Overlay](story-003-event-driven-screen-space-combat-hud.md) | UI | M | Ready | TR-hud-003 |
