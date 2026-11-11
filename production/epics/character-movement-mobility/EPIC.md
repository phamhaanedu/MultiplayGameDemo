# Epic: Tick-Accurate Character Movement & Mobility System

> **Layer**: Core
> **GDD**: design/gdd/character-movement-mobility.md
> **Architecture Module**: PlayerMobility
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này hiện thực hóa máy trạng thái di chuyển và cơ động theo từng tick mạng `60Hz` (`FixedUpdateNetwork`) trong `PlayerMovementController`, bao gồm Đi bộ (`6.0 m/s`), Nhảy (`7.5 m/s` với `0.10s` Coyote Time & Jump Buffer), Lướt tiến (`Forward Dash` trên phím `Key 2` với tốc độ `18.0 m/s` trong `12 ticks` / `3.6m`, hồi chiêu `2.50s`), và Lăn né ngang (`Dodge Roll Left/Right` trên phím `Q/E` với tốc độ `12.0 m/s` trong `15 ticks` / `3.0m`, hồi chiêu chung `1.80s`). Theo quyết định tại `ADR-0004`, mọi chuyển động đều sử dụng truy vấn không cấp phát `Physics.CapsuleCastNonAlloc` kèm phép chiếu trượt dọc mặt phẳng tường (`Wall Slide Projection`) để nhân vật không bị khựng khi lướt chéo vào chướng ngại vật `Layer 6`, đồng thời cô lập toàn bộ hiệu ứng nghiêng người `35°` và `DOTween` Squash & Stretch lên nhánh con `ChildVisualRoot` nhằm giữ nguyên hình dạng `CapsuleCollider` vật lý.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0004: Tick-Accurate Mobility (Dash/Dodge) & Victim-Authoritative Health RPC | Implements deterministic 60Hz locomotion, `Key 2` Forward Dash (`12 ticks`), `Keys Q/E` Dodge Roll (`15 ticks`), `CapsuleCastNonAlloc` wall sliding, and `ChildVisualRoot` visual separation. | HIGH |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-move-001 | PlayerMovementController must execute 60Hz FixedUpdateNetwork locomotion at MoveSpeed=6.0m/s, JumpImpulse=7.5m/s, and Gravity=-19.62m/s2 with 0.10s coyote time and 0.10s jump buffer | ADR-0004 ✅ |
| TR-move-002 | Forward Dash (Key 2) must propel the capsule along horizontal forward at DashSpeed=18.0m/s for 0.20s (12 ticks, 3.6m) with a 2.50s TickTimer cooldown and wall slide projection via Physics.CapsuleCastNonAlloc | ADR-0004 ✅ |
| TR-move-003 | Dodge Roll Left/Right (Keys Q/E) must strafe at DodgeSpeed=12.0m/s for 0.25s (15 ticks, 3.0m) with a shared 1.80s TickTimer cooldown, mutual exclusion with Dash, and DOTween visual tilt/squash on ChildVisualRoot | ADR-0004 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/character-movement-mobility.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Tick-Accurate Walk, Jump & Gravity (`NetworkCharacterController`)](story-001-tick-accurate-walk-jump-gravity.md) | Logic | M | Ready | TR-move-001 |
| 002 | [Forward Dash (`Key 2`) & Wall-Slide Projection](story-002-forward-dash-key2-and-wall-slide.md) | Logic | M | Ready | TR-move-002 |
| 003 | [Lateral Dodge Roll (`Keys Q/E`) & Visual Root Tilt](story-003-dodge-roll-qe-and-visual-tilt.md) | Visual/Feel | M | Ready | TR-move-003 |
