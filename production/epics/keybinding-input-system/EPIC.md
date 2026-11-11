# Epic: Keybinding & Input Management System

> **Layer**: Foundation
> **GDD**: design/gdd/keybinding-input-system.md
> **Architecture Module**: KeybindingInput
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này xây dựng tầng thu thập điều khiển hợp nhất cho cả Bàn phím/Chuột PC và cụm 7 nút cảm ứng Mobile (`MobileInputBridge`), lưu trữ sơ đồ 13 hành động bền vững vào `Application.persistentDataPath/keybindings.json` thông qua `KeybindingManager`. Theo quyết định tại `ADR-0002`, mọi thao tác ghi file đều sử dụng cơ chế ghi nguyên tử qua file tạm `.tmp` kèm khả năng tự động phục hồi về bộ phím chuẩn khi phát hiện JSON lỗi. Tín hiệu điều khiển từ PC và Mobile được hợp nhất bằng phép toán `Bitwise OR` vào cấu trúc mạng `NetworkInputData` (`16 bytes`) trong `LocalInputPoller` để truyền vào `FixedUpdateNetwork()` của Photon Fusion 2, đồng thời được bảo vệ bởi mặt nạ khóa độc quyền `InputFocusManager` nhằm ngăn chặn triệt để lỗi nhân vật tự di chuyển hoặc tung chiêu khi người chơi đang gõ khung Chat hay mở bảng cài đặt phím.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManager | Implements atomic `keybindings.json` persistence, bitwise-OR merging of PC `InputAction` and `MobileInputBridge` into 16-byte `NetworkInputData`, and `InputFocusFlag` gating. | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-input-001 | KeybindingManager must persist 13 game actions in Application.persistentDataPath/keybindings.json with atomic .tmp write, corrupt JSON auto-recovery, and OnBindingsChanged C# event dispatch | ADR-0002 ✅ |
| TR-input-002 | LocalInputPoller must merge desktop InputAction state with MobileInputBridge flags via bitwise OR into a 16-byte NetworkInputData struct (MoveDirection, LookYaw, LookPitch, NetworkButtons) | ADR-0002 ✅ |
| TR-input-003 | InputFocusManager must suppress gameplay ActionMap and zero out NetworkInputData when ChatFocused, RebindModalOpen, or MatchEnded flags are active | ADR-0002 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/keybinding-input-system.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Keybinding JSON Atomic Persistence & Recovery](story-001-keybinding-json-atomic-persistence.md) | Logic | M | Ready | TR-input-001 |
| 002 | [Dual PC & Mobile Input Poller (`NetworkInputData`)](story-002-dual-pc-mobile-input-poller.md) | Integration | M | Ready | TR-input-002 |
| 003 | [InputFocusManager Exclusive Gating](story-003-input-focus-manager-gating.md) | Logic | S | Ready | TR-input-003 |
