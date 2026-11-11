# ADR-0003: Photon Fusion 2 Shared Mode Session & Zero-Alloc Network Object Pooling

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `engine-programmer`, `gameplay-programmer`

## Summary

Quyết định này chuẩn hóa tầng kết nối mạng sử dụng **Photon Fusion 2 Shared Mode (`GameMode.Shared`, TickRate `60Hz`, AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`)** thông qua `FusionSessionManager`, kết hợp với bộ quản lý hồ chứa đối tượng mạng `FusionObjectPoolProvider` cài đặt `INetworkObjectProvider` (khởi tạo sẵn `32` viên đạn `NetworkProjectile` và `36` hiệu ứng VFX/SFX cục bộ) nhằm đảm bảo `0 byte` cấp phát rác và `0` lệnh `Instantiate`/`Destroy` trong suốt trận đấu.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | Networking / Core |
| **Layer** | Foundation |
| **Knowledge Risk** | HIGH |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/shared-mode-session-pool.md` |
| **Post-Cutoff APIs Used** | Photon Fusion 2 (`NetworkRunner.StartGame`, `GameMode.Shared`, `INetworkObjectProvider.AcquirePrefabInstance` / `ReleaseInstance`) |
| **Verification Required** | PlayMode integration test verifying 32 pre-warmed `NetworkProjectile` instances acquire/release without calling `Instantiate` or `Destroy` |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | ADR-0004, ADR-0005, ADR-0006 |
| **Blocks** | EPIC-003 (Photon Fusion Shared Mode Session & Object Pool) |
| **Ordering Note** | Must be in place before any networked player spawning, projectile firing, or match state synchronization can run. |

## Context

### Problem Statement
Trong một trận đấu hành động 4 người chơi bắn đạn liên tục (tốc độ hồi chiêu `0.75s`/viên), việc dùng `Runner.Spawn()` mặc định sẽ gọi `UnityEngine.Object.Instantiate` và `Destroy` liên tục, gây hiện tượng giật khung hình (GC spikes) và làm lệch quỹ đạo bay đầu tiên của viên đạn do trễ khởi tạo. Ngoài ra, trò chơi yêu cầu mô hình **Photon Fusion 2 Shared Mode** dễ triển khai cho môn học GAM302 mà không cần máy chủ chuyên dụng (Dedicated Server).

### Current State
Dự án đã chốt cấu hình Photon AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`, tần số mô phỏng `60Hz` và giới hạn phòng `MaxPlayers = 4` trong `game-concept.md` và `shared-mode-session-pool.md`.

### Constraints
- Phải sử dụng `GameMode.Shared` của Photon Fusion 2, trong đó mỗi máy trạm nắm giữ `StateAuthority` đối với nhân vật và viên đạn do chính mình sinh ra.
- Tuyệt đối không gọi `Instantiate` hoặc `Destroy` đối với đạn và hiệu ứng trong lúc trận đấu đang diễn ra (`MatchState == Playing`).
- Phải xóa sạch vệt đạn (`TrailRenderer.Clear()`) và vận tốc vật lý cũ ngay khi lấy đạn ra khỏi hồ chứa để tránh lỗi tia sáng kéo dài xuyên bản đồ.

### Requirements
- Đáp ứng trọn vẹn `TR-session-001`, `TR-session-002`, `TR-session-003`.

## Decision

1. **Quản lý Phiên Mạng `FusionSessionManager` (`GameMode.Shared`, `60Hz`)**:
   - Khởi tạo `NetworkRunner` với `StartGameArgs { GameMode = GameMode.Shared, SessionName = roomCode, PlayerCount = 4, ObjectProvider = _poolProvider, SceneManager = _sceneManager }`.
   - Khi callback `OnPlayerJoined(NetworkRunner runner, PlayerRef player)` kích hoạt cho `player == runner.LocalPlayer`, máy trạm cục bộ gọi `runner.Spawn(_playerPrefab, spawnPos, spawnRot, player)` để sinh ra nhân vật do chính mình giữ `StateAuthority`.
2. **Hồ chứa Đối tượng Mạng `FusionObjectPoolProvider : INetworkObjectProvider`**:
   - Lúc nạp cảnh đấu trường (`PrewarmPools()`), khởi tạo sẵn một hàng đợi `Queue<NetworkObject>` gồm đúng **32** bản sao `NetworkProjectile` ở trạng thái ẩn (`SetActive(false)`).
   - Khi `AcquirePrefabInstance(NetworkRunner runner, NetworkPrefabAcquireContext context, out NetworkObject instance)` được gọi: lấy đối tượng từ hàng đợi, đặt lại vị trí/góc xoay, xóa `TrailRenderer`, đặt lại `Rigidbody` vận tốc về `0`, bật `SetActive(true)` và trả về `NetworkObjectAcquireResult.Success`.
   - Khi `ReleaseInstance(NetworkRunner runner, NetworkObjectReleaseContext context)` được gọi: tắt `SetActive(false)` và đưa đối tượng trở lại hàng đợi thay vì hủy.
3. **Vòng đời Tự Hủy về Hồ chứa của `NetworkProjectile`**:
   - Mỗi viên đạn khởi tạo `[Networked] TickTimer LifeTimer` bằng `TickTimer.CreateFromTicks(Runner, 120)` (`2.0s` ở `60Hz`). Khi hết giờ hoặc va chạm mục tiêu/tường, máy sở hữu `StateAuthority` gọi `Runner.Despawn(Object)` để trả đạn về `FusionObjectPoolProvider`.

### Architecture Diagram

```text
+-------------------------------------------------------------------+
|                       FusionSessionManager                        |
|  - GameMode.Shared | 60Hz TickRate | MaxPlayers = 4               |
|  - Spawns Local Player on OnPlayerJoined (player == LocalPlayer)  |
+---------------------------------+---------------------------------+
                                  |
                                  v
+-------------------------------------------------------------------+
|            FusionObjectPoolProvider : INetworkObjectProvider      |
|  - Pre-warmed Queue<NetworkObject> (32 NetworkProjectiles)        |
|  - AcquirePrefabInstance -> Reset Trail/Velocity -> SetActive(1)  |
|  - ReleaseInstance       -> Stop VFX/Timers      -> SetActive(0)  |
+-------------------------------------------------------------------+
```

### Key Interfaces

```csharp
namespace CapsuleArena.Networking
{
    public interface INetworkSessionService
    {
        bool IsConnected { get; }
        Fusion.NetworkRunner Runner { get; }
        System.Threading.Tasks.Task<bool> JoinOrCreateRoomAsync(string roomName);
        void LeaveSession();
    }

    public interface IPoolableNetworkEntity
    {
        void OnFetchedFromPool();
        void OnReturnedToPool();
    }
}
```

### Implementation Guidelines
- All networked projectiles **must** be spawned via `Runner.Spawn()` backed by `FusionObjectPoolProvider` and **must never** be instantiated via `UnityEngine.Object.Instantiate` during gameplay.
- `NetworkProjectile.OnFetchedFromPool()` **must** call `_trailRenderer.Clear()` before enabling rendering so no visual artifact streaks from the previous despawn location to the new muzzle position.
- Only the peer with `Object.HasStateAuthority` **must** call `Runner.Despawn(Object)` on a `NetworkProjectile`.

## Alternatives Considered

### Alternative 1: Photon Fusion Host/Server Mode (`GameMode.AutoHostOrClient`)
- **Description**: Một người chơi làm Host chạy toàn bộ vật lý cho cả 4 người, các máy khác chỉ gửi Input.
- **Pros**: Quyền quyết định tập trung tại một máy duy nhất.
- **Cons**: Nếu Host thoát phòng, việc di chuyển toàn bộ Host Migration phức tạp hơn nhiều so với Shared Mode, đồng thời người chơi Client chịu độ trễ đầu vào cao hơn khi không cấu hình Client Prediction phức tạp.
- **Estimated Effort**: Cao.
- **Rejection Reason**: Yêu cầu đề bài GAM302 và thiết kế gốc chỉ định rõ kiến trúc **Photon Fusion 2 Shared Mode (`GameMode.Shared`)**.

### Alternative 2: Default `NetworkObjectProviderDefault` (No Pooling)
- **Description**: Sử dụng bộ cấp phát mặc định của Photon Fusion (tạo mới khi Spawn, hủy khi Despawn).
- **Pros**: Không cần viết code quản lý hàng đợi.
- **Cons**: Gây cấp phát bộ nhớ rác và giật khung hình mỗi khi 4 người chơi bắn đạn liên thanh, đồng thời vi phạm quy tắc bắt buộc dùng Object Pooling.
- **Estimated Effort**: Rất thấp.
- **Rejection Reason**: Vi phạm trực tiếp yêu cầu kỹ thuật cốt lõi (`INetworkObjectProvider` Object Pooling).

## Consequences

### Positive
- Đạt `0 ms` chi phí khởi tạo đạn trong trận đấu và loại bỏ hoàn toàn hiện tượng giật hình do Garbage Collector.
- Vòng đời kết nối phòng (`WaitingForPlayers` $\rightarrow$ `Arena`) rõ ràng, tự động dọn dẹp khi rời phòng.

### Negative
- Tốn khoảng `1.5 MB` bộ nhớ RAM lúc nạp cảnh để giữ sẵn 32 viên đạn và 36 đối tượng VFX/SFX trong hồ chứa.

### Neutral
- Mọi thành phần trên `NetworkProjectile` phải viết hàm đặt lại trạng thái (`OnFetchedFromPool`) cẩn thận như một đối tượng mới.

## Risks
- **Rủi ro cạn hồ chứa nếu hơn 32 viên đạn bay cùng lúc**: Với giới hạn 4 người chơi, hồi chiêu bắn `0.75s` và thời gian tồn tại của đạn `2.0s`, số đạn tối đa trên sân tại một thời điểm chỉ là $4 \times \lceil 2.0 / 0.75 \rceil = 12$ viên (dưới xa ngưỡng 32 viên). Ngoài ra, hồ chứa có cơ chế tự mở rộng an toàn (`ExpandPoolBy(4)`) nếu vượt ngưỡng.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `shared-mode-session-pool.md` | `TR-session-001`: Start Photon Fusion 2 in `GameMode.Shared` at `60Hz` (`MaxPlayers=4`) and spawn local player in `OnPlayerJoined` | Defines `FusionSessionManager` initialization and local player spawn authority |
| `shared-mode-session-pool.md` | `TR-session-002`: `FusionObjectPoolProvider` implements `INetworkObjectProvider` and pre-warms 32 `NetworkProjectile` + 36 local VFX/SFX objects | Establishes pre-warmed queue architecture and pool sizing |
| `shared-mode-session-pool.md` | `TR-session-003`: `NetworkProjectile` resets `TrailRenderer`, velocity, and `ExpiryTimer` on pool acquire/release and despawns after `120 ticks` | Enforces `IPoolableNetworkEntity` reset contract and `TickTimer` auto-despawn |

## Performance Implications
- **CPU**: `< 0.05 ms` mỗi lần lấy/trả đạn từ `Queue<NetworkObject>` ($O(1)$).
- **Memory**: `~1.5 MB` cấp phát cố định một lần duy nhất lúc nạp cảnh; `0 B` cấp phát lúc chiến đấu.
- **Load Time**: `+15 ms` lúc nạp cảnh `ArenaScene` để khởi tạo trước 68 đối tượng trong hồ chứa.
- **Network**: Mỗi viên đạn đồng bộ vị trí nén qua `NetworkTransform` (`~24 bytes/tick`), tối đa 12 viên đồng thời (`~17 KB/s`).

## Migration Plan
Triển khai `FusionSessionManager.cs` và `FusionObjectPoolProvider.cs` trong `Assets/_Project/Scripts/Networking/` trước khi ghép nối vũ khí tầm xa.

**Rollback plan**: Nếu phát sinh lỗi đồng bộ trạng thái trên một loại Prefab cụ thể khi tái sử dụng, có thể cấu hình danh sách loại trừ (`BypassPoolPrefabs`) trong `FusionObjectPoolProvider` để kiểm tra cô lập.

## Validation Criteria
- PlayMode Test bắn liên tiếp 64 phát đạn xác nhận tổng số đối tượng `NetworkProjectile` trong Hierarchy không bao giờ vượt quá 32 và không phát sinh rác bộ nhớ (`GC.Alloc == 0`).
- Kiểm tra trực quan bằng mắt xác nhận không có vệt sáng `TrailRenderer` nối từ điểm nổ cũ về nòng súng mới.

## Related
- Enables: [`adr-0004-tick-accurate-mobility-and-damage-rpc.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0004-tick-accurate-mobility-and-damage-rpc.md), [`adr-0005-multidirectional-combat-and-lag-compensation.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0005-multidirectional-combat-and-lag-compensation.md), [`adr-0006-match-manager-masterclient-failover.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0006-match-manager-masterclient-failover.md)
- GDD: [`shared-mode-session-pool.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/shared-mode-session-pool.md)
