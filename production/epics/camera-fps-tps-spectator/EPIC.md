# Epic: Dual-Perspective Camera (FPS/TPS) & Ghost Mode Spectator System

> **Layer**: Feature
> **GDD**: design/gdd/camera-fps-tps-spectator.md
> **Architecture Module**: CameraAndSpectator
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này xây dựng hệ thống điều khiển góc nhìn kép (`DualPerspectiveCameraController`) và chế độ Khán giả Hồn ma (`GhostSpectatorController`) sau khi tử trận. Theo quyết định tại `ADR-0005`, người chơi có thể nhấn phím `V` bất kỳ lúc nào để chuyển đổi mượt trong `0.20s` giữa góc nhìn thứ ba (`TPS`: khoảng cách cần camera `4.5m`, lệch vai phải `0.65m`, độ cao `1.6m`, `FOV 65°`) và góc nhìn thứ nhất (`FPS`: đặt tại tầm mắt `1.55m`, `FOV 80°`, tự động ẩn mô hình đầu/kính cục bộ bằng `ShadowCastingMode.ShadowsOnly` để không che màn hình nhưng vẫn giữ bóng đổ dưới đất). Cần camera `TPS` sử dụng `Physics.SphereCast` (`bán kính 0.22m`, khoảng cách tối thiểu `0.60m`) đối chiếu với `Layer 6 (EnvironmentWall)` để chống xuyên tường. Khi nhân vật cục bộ tử trận (`IsAlive == false`), hệ thống tự động kích hoạt chế độ bay tự do (`Ghost Free-Roam` ở tốc độ `9.0 m/s`), phủ vật liệu bán trong suốt `#38BDF8` và cho phép bấm `Tab` để luân chuyển góc nhìn theo dõi những người chơi còn sống.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0005: Multi-Directional Melee (90° Snap-Turn), Ranged Combat & Dual FPS/TPS/Ghost Camera | Implements `Key V` FPS/TPS blending, `ShadowsOnly` local head masking, `SphereCast` wall pull-in on `Layer 6`, and post-death `GhostSpectatorController`. | HIGH |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-camera-001 | DualPerspectiveCameraController must toggle between TPS (boom 4.5m, shoulder offset 0.65m, height 1.6m, FOV 65) and FPS (eye height 1.55m, FOV 80, local head/visor hidden) within 0.20s on Key V | ADR-0005 ✅ |
| TR-camera-002 | TPS camera boom must prevent wall clipping using Physics.SphereCast (radius 0.22m, MinCameraDistance 0.60m) against Layer 6 (EnvironmentWall) with immediate pull-in and smoothed push-out | ADR-0005 ✅ |
| TR-camera-003 | Upon local player death (IsAlive==false), GhostSpectatorController must enable free-roam ghost movement at 9.0m/s, apply #38BDF8 fresnel ghost shader, and allow Tab cycling through living players in TPS follow mode | ADR-0005 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/camera-fps-tps-spectator.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Dual-Perspective Camera Toggle (`FPS` / `TPS` via `Key V`)](story-001-dual-fps-tps-camera-toggle.md) | Visual/Feel | M | Ready | TR-camera-001 |
| 002 | [TPS Camera `SphereCast` Wall Collision Anti-Clip](story-002-tps-camera-spherecast-wall-collision.md) | Logic | S | Ready | TR-camera-002 |
| 003 | [Post-Death Ghost Free-Roam & Follow-Target Spectator](story-003-post-death-ghost-spectator-controller.md) | Integration | M | Ready | TR-camera-003 |
