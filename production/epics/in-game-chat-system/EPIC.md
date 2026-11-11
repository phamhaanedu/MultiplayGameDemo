# Epic: In-Game Chat System (Public & Private Whisper)

> **Layer**: Presentation
> **GDD**: design/gdd/in-game-chat-system.md
> **Architecture Module**: InGameChat
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này xây dựng hệ thống trò chuyện thời gian thực trong trận đấu (`InGameChatController`) hỗ trợ kênh Công khai (`Public`) và kênh Thì thầm riêng tư (`Whisper` qua cú pháp `/w <Tên> <Tin nhắn>` hoặc phím `Tab` trả lời nhanh). Theo quyết định tại `ADR-0007`, đối tượng gốc chứa `InGameChatController` luôn được giữ ở trạng thái `gameObject.activeSelf == true` và chỉ điều khiển ẩn/hiện thông qua `CanvasGroup` (`alpha`, `interactable`, `blocksRaycasts`) nhằm đảm bảo không bao giờ làm rớt các gói tin `Rpc_ReceiveChatMessage` khi khung nhập liệu đang đóng. Khi người chơi nhấn `Enter` mở khung gõ chữ, `InputFocusManager` lập tức bật cờ `ChatFocused` để khóa hoàn toàn điều khiển chiến đấu. Mọi tin nhắn đều được lọc bỏ thẻ định dạng (`Rich Text Tag Sanitization`), giới hạn `160 ký tự`, kiểm soát chống spam bằng thuật toán `Token Bucket` (`5 tin nhắn`, hồi `1 token / 1.5s`) và hiển thị trên hồ chứa xoay vòng `50 dòng` tự làm mờ xuống `alpha = 0.15` sau `8.0s`.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0007: Event-Driven Combat HUD, 7-Button Mobile Action Pad & CanvasGroup Persistent Chat | Enforces persistent active `GameObject` with `CanvasGroup` visibility control, targeted `[RpcTarget]` Whisper routing, rich-text sanitization, and 5-message token bucket rate limiting. | MEDIUM |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-chat-001 | InGameChatController must keep its root GameObject active at all times and control panel visibility strictly via CanvasGroup (alpha, interactable, blocksRaycasts) so Photon Fusion RPCs never drop when the chat UI is faded | ADR-0007 ✅ |
| TR-chat-002 | Chat parser must support Public messages and private Whisper commands (/w <TargetName> <Message> and Tab reply) routed via targeted Rpc_ReceiveChatMessage with rich-text sanitization and 160-character clamping | ADR-0007 ✅ |
| TR-chat-003 | Chat rate limiter must enforce a 5-message token bucket (1 token/1.5s refill) and maintain a 50-row circular UI pool with 8.0s passive auto-fade to alpha 0.15 | ADR-0007 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/in-game-chat-system.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [CanvasGroup Persistent Chat Window & Focus Toggle](story-001-canvasgroup-persistent-chat-window.md) | UI | S | Ready | TR-chat-001 |
| 002 | [Public Broadcast & Private Whisper (`[RpcTarget]`) Chat Router](story-002-public-and-whisper-chat-rpc-router.md) | Integration | M | Ready | TR-chat-002 |
| 003 | [Token-Bucket Chat Rate Limiter & 50-Row Ring Buffer Pool](story-003-token-bucket-rate-limiter-and-row-pool.md) | Logic | S | Ready | TR-chat-003 |
