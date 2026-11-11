# Epic: Photon Fusion 2 Shared Mode Session & Object Pool System

> **Layer**: Foundation
> **GDD**: design/gdd/shared-mode-session-pool.md
> **Architecture Module**: NetworkSessionPool
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này thiết lập hạ tầng kết nối mạng cốt lõi bằng **Photon Fusion 2 Shared Mode (`GameMode.Shared`, TickRate `60Hz`, AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`, `MaxPlayers = 4`)** qua `FusionSessionManager` và bộ quản lý hồ chứa đối tượng mạng không cấp phát rác `FusionObjectPoolProvider : INetworkObjectProvider`. Theo quyết định tại `ADR-0003`, mỗi máy trạm tự sinh (`Runner.Spawn`) nhân vật cục bộ của mình trong `OnPlayerJoined` để nắm giữ `StateAuthority` trực tiếp. Để loại bỏ hoàn toàn hiện tượng giật khung hình do `Instantiate`/`Destroy` khi 4 người chơi bắn đạn liên thanh, `FusionObjectPoolProvider` khởi tạo sẵn hàng đợi `32` viên đạn `NetworkProjectile` và `36` hiệu ứng VFX/SFX cục bộ ngay lúc nạp cảnh, đồng thời đặt lại sạch sẽ `TrailRenderer`, vận tốc vật lý và bộ đếm `TickTimer` (`120 ticks` / `2.0s`) mỗi khi lấy hoặc trả đạn về hồ chứa.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0003: Photon Fusion 2 Shared Mode Session & Zero-Alloc Network Object Pooling | Configures `GameMode.Shared` at `60Hz` (`MaxPlayers = 4`) and implements a pre-warmed `INetworkObjectProvider` pool of 32 `NetworkProjectiles` with zero runtime `Instantiate`/`Destroy`. | HIGH |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-session-001 | FusionSessionManager must start Photon Fusion 2 in GameMode.Shared at 60Hz tick rate (AppId fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9) with MaxPlayers=4 and spawn local player via OnPlayerJoined | ADR-0003 ✅ |
| TR-session-002 | FusionObjectPoolProvider must implement INetworkObjectProvider and pre-warm 32 NetworkProjectile instances plus 36 local VFX/SFX pooled objects at scene load with zero runtime Instantiate/Destroy in combat | ADR-0003 ✅ |
| TR-session-003 | NetworkProjectile lifecycle must reset TrailRenderer, Rigidbody velocity, and ExpiryTimer on pool acquire/release and automatically despawn back to pool after 120 ticks (2.0s) or collision | ADR-0003 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/shared-mode-session-pool.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Photon Fusion 2 Shared Mode Session Bootstrap](story-001-fusion-shared-mode-session-bootstrap.md) | Integration | M | Ready | TR-session-001 |
| 002 | [Zero-Alloc Network Object Pool Provider (`INetworkObjectProvider`)](story-002-network-object-pool-provider.md) | Logic | M | Ready | TR-session-002 |
| 003 | [Pooled NetworkProjectile Lifecycle & State Reset](story-003-pooled-projectile-lifecycle-reset.md) | Integration | S | Ready | TR-session-003 |
