# Epic: Multi-Directional Melee & Pooled Ranged Combat System

> **Layer**: Feature
> **GDD**: design/gdd/combat-melee-ranged-system.md
> **Architecture Module**: PlayerCombat
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này hiện thực hóa hệ thống chiến đấu đa hướng đặc trưng của trò chơi trong `PlayerCombatController` và `NetworkProjectile`. Theo quyết định tại `ADR-0005`, hệ thống cận chiến bao gồm hai cơ chế tách biệt: (1) **Chém dọc (`Vertical Slash` — `LMB`)** quét hình quạt đối xứng `50°` thẳng phía trước (`bán kính 2.4m`, `25 dmg`, hồi chiêu `0.55s`), và (2) **Chém ngang Quét & Xoay `90°` (`Horizontal Sweep Left/Right` — `Keys 1, 3`)** quét hình quạt bất đối xứng `90°` từ thẳng trước mặt sang hẳn bên trái (`Key 1`, $s=-1$) hoặc bên phải (`Key 3`, $s=+1$) với `bán kính 2.6m`, `30 dmg`, hồi chiêu chung `1.20s`, đồng thời xoay cả thân nhân vật lẫn góc nhìn camera đúng `±90°` theo đường cong `SmoothStep` trong `0.18s` (`11 ticks`). Mọi truy vấn cận chiến sử dụng `Physics.OverlapSphereNonAlloc`, tích vô hướng phẳng so sánh với ngưỡng cosine tiền tính toán, và tia `Physics.Raycast` (`LineOfSightMask` `Layer 6`) để ngăn chém xuyên tường. Đòn **Bắn đạn tầm xa (`Ranged Fire` — `RMB`, `15 dmg`, hồi chiêu `0.75s`)** lấy đạn từ `FusionObjectPoolProvider` bay ở tốc độ `28.0 m/s` trong tối đa `120 ticks` (`28.0m`).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0005: Multi-Directional Melee (90° Snap-Turn), Ranged Combat & Dual FPS/TPS/Ghost Camera | Defines zero-alloc `OverlapSphereNonAlloc` + dot-product + LOS melee validation, synchronized `±90°` body/camera yaw turn over `11 ticks`, and pooled projectile simulation. | HIGH |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-combat-001 | Vertical Slash (LMB, 25 dmg, 0.55s CD) and Horizontal Sweep Left/Right (Keys 1/3, 30 dmg, 1.20s shared CD) must evaluate sector hits using zero-alloc Physics.OverlapSphereNonAlloc (radius 2.4m/2.6m), planar dot product, and LineOfSightMask raycast | ADR-0005 ✅ |
| TR-combat-002 | Horizontal Sweep Left (Key 1, s=-1) and Right (Key 3, s=+1) must apply a synchronized 90-degree snap-turn (TargetYaw = StartYaw + s * 90 deg) over 0.18s (11 ticks) in FixedUpdateNetwork and drive CameraController yaw smoothly | ADR-0005 ✅ |
| TR-combat-003 | Ranged Fire (RMB, 15 dmg, 0.75s CD) must acquire a pooled NetworkProjectile at MuzzleOffset=(0, 1.1, 0.75) travelling at 28.0m/s for up to 28.0m (120 ticks) with client-side visual prediction and single-hit consumption | ADR-0005 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/combat-melee-ranged-system.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Zero-Alloc Melee Sector & Line-of-Sight Detection](story-001-zero-alloc-melee-sector-detection.md) | Logic | M | Ready | TR-combat-001 |
| 002 | [Horizontal Sweep `90 deg` Snap-Turn Interpolation](story-002-horizontal-sweep-90-snap-turn.md) | Integration | M | Ready | TR-combat-002 |
| 003 | [Ranged Projectile Fire (`RMB`) & Continuous Hit Detection](story-003-ranged-projectile-fire-and-hit.md) | Integration | M | Ready | TR-combat-003 |
