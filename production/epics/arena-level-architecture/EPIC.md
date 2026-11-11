# Epic: Arena Level & Tactical Obstacle Architecture

> **Layer**: Foundation
> **GDD**: design/gdd/arena-level-architecture.md
> **Architecture Module**: ArenaArchitecture
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này thiết lập cấu trúc hình học đấu trường đối xứng `40m x 40m`, ma trận va chạm vật lý 4 lớp (`Layer 6 EnvironmentWall`, `Layer 8 PlayerCharacter`, `Layer 9 ArenaFloor`, `Layer 10 GhostSpectator`) và bộ chọn điểm xuất phát thông minh `ArenaSpawnManager`. Theo quyết định tại `ADR-0001`, mọi bức tường bao (`cao 2.5m`), bệ trung tâm, 4 bức tường chữ L và 8 cột chắn chiến thuật đều được gắn `Layer 6` và kiểm tra qua mặt nạ `LineOfSightMask (1 << 6)` để chặn đòn chém cận chiến xuyên tường, chặn đạn tầm xa và kéo cần camera `TPS` tránh xuyên vật thể, trong khi vẫn cho phép người chơi đã tử trận ở `Layer 10 (GhostSpectator)` bay xuyên qua tự do. Thuật toán chọn điểm hồi sinh trên đường tròn bán kính `16.0m` cực đại hóa khoảng cách Euclid tối thiểu tới mọi đối thủ đang sống nhằm loại bỏ hoàn toàn rủi ro bị tấn công ngay lúc vừa xuất hiện.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture | Defines the `40m x 40m` arena geometry, 4-layer collision matrix (`Layers 6, 8, 9, 10`), `LineOfSightMask`, and Max-Min Euclidean distance spawn selector. | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-arena-001 | Arena geometry must be a 40m x 40m symmetric tactical courtyard with a 2.5m high perimeter wall, central altar, 4 L-walls, and 8 cover pillars on Layer 6 (EnvironmentWall) and Layer 9 (ArenaFloor) | ADR-0001 ✅ |
| TR-arena-002 | ArenaSpawnManager must select one of 8 perimeter SpawnPoint transforms (16m radius) maximizing minimum Euclidean distance to all living players with deterministic Round-Robin fallback on first spawn | ADR-0001 ✅ |
| TR-arena-003 | Physics Layer Collision Matrix and LineOfSightMask (1 << 6) must block melee sectors, projectiles, and TPS camera booms on Layer 6 while allowing Layer 10 GhostSpectator to pass through Layer 6 and collide with Layer 9 | ADR-0001 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/arena-level-architecture.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Arena Courtyard & Tactical Cover Geometry](story-001-arena-courtyard-and-cover-geometry.md) | Visual/Feel | M | Ready | TR-arena-001 |
| 002 | [Max-Min Euclidean Spawn Point Selector](story-002-max-min-euclidean-spawn-selector.md) | Logic | S | Ready | TR-arena-002 |
| 003 | [Physics Layer Collision Matrix & LineOfSightMask](story-003-physics-layer-collision-matrix.md) | Integration | S | Ready | TR-arena-003 |
