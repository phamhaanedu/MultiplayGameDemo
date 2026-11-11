# ADR-0006: MatchManager Last Man Standing State Machine & Shared Mode MasterClient Failover

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `gameplay-programmer`, `systems-designer`

## Summary

Quyết định này thiết lập bộ điều phối vòng đời trận đấu `MatchManager` (`WaitingForPlayers` $\rightarrow$ `Countdown` $\rightarrow$ `Playing` $\rightarrow$ `MatchOver`) xét điều kiện thắng **Last Man Standing** (`AliveCount <= 1`), kèm cơ chế kế thừa quyền điều khiển tự động (`MasterClient Authority Failover` thông qua `Object.RequestStateAuthority()`) để trận đấu không bao giờ bị treo khi người chơi chủ phòng ban đầu ngắt kết nối giữa chừng.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | Networking / Core |
| **Layer** | Feature |
| **Knowledge Risk** | HIGH |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/match-manager-authority-failover.md` |
| **Post-Cutoff APIs Used** | Photon Fusion 2 (`Runner.IsSharedModeMasterClient`, `Object.RequestStateAuthority()`, `IStateAuthorityChanged`, `INetworkRunnerCallbacks.OnPlayerLeft`) |
| **Verification Required** | PlayMode integration test simulating MasterClient disconnect mid-match and verifying new MasterClient claims `StateAuthority` within 3 ticks without losing `PhaseTimer` or `AliveCount` |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003, ADR-0004 |
| **Enables** | ADR-0008 |
| **Blocks** | EPIC-008 (Match Manager, Last Man Standing & MasterClient Failover) |
| **Ordering Note** | Requires `ADR-0003` (`FusionSessionManager`) and `ADR-0004` (`PlayerHealthController.IsAlive`). |

## Context

### Problem Statement
Trong Photon Fusion 2 Shared Mode, các đối tượng quản lý toàn cục của màn chơi như `MatchManager` (quản lý đồng hồ đếm ngược, số người còn sống `AliveCount`, và tuyên bố người chiến thắng `WinnerPlayerRef`) được điều khiển bởi một máy trạm nắm giữ `StateAuthority` (mặc định là người tạo phòng / `SharedModeMasterClient`). Nếu người chơi này bị rớt mạng hoặc thoát game sau khi đã bị hạ gục, `MatchManager` sẽ mất người điều khiển và trận đấu sẽ bị kẹt vĩnh viễn ở trạng thái `Playing` dù chỉ còn 1 người sống sót.

### Current State
Tài liệu `match-manager-authority-failover.md` đã định nghĩa chi tiết máy trạng thái 4 pha và các trường hợp biên (Double-KO hòa, thoát phòng lúc đếm ngược, chủ phòng rớt mạng).

### Constraints
- Toàn bộ trạng thái trận đấu (`MatchState`, `AliveCount`, `WinnerPlayerRef`, `PhaseTimer`, `MatchSequenceId`) phải được lưu trong các thuộc tính `[Networked]` trên `MatchManager` để khi quyền `StateAuthority` chuyển sang máy mới, dữ liệu tiếp tục liền mạch 100% mà không bị đặt lại về 0.
- Phải xử lý chính xác trường hợp 2 người chơi cuối cùng hạ gục lẫn nhau trong cùng một tick (`AliveCount == 0` $\rightarrow$ Xử lý Hòa hoặc trao chiến thắng cho người có sát thương kết liễu trước theo thứ tự tick).

### Requirements
- Đáp ứng trọn vẹn `TR-match-001`, `TR-match-002`, `TR-match-003`.

## Decision

1. **Máy Trạng thái Trận đấu Đồng bộ Mạng (`[Networked]` State Machine)**:
   - `MatchManager : NetworkBehaviour, IStateAuthorityChanged` lưu trữ:
     - `[Networked, OnChangedRender(nameof(OnMatchStateChanged))] public MatchStateEnum CurrentMatchState { get; set; }`
     - `[Networked, OnChangedRender(nameof(OnAliveCountChanged))] public int AliveCount { get; set; }`
     - `[Networked] public PlayerRef WinnerPlayerRef { get; set; }`
     - `[Networked] public TickTimer PhaseTimer { get; set; }`
     - `[Networked] public int MatchSequenceId { get; set; }`
   - Vòng đời 4 trạng thái:
     - `WaitingForPlayers`: Chờ đủ tối thiểu `MinPlayersToStart = 2` người chơi kết nối.
     - `Countdown`: Đếm ngược `5.0s` (`PhaseTimer`). Khóa tấn công, cho phép xoay camera quan sát. Nếu người chơi thoát làm tổng số người `< 2`, hủy đếm ngược quay về `WaitingForPlayers`.
     - `Playing`: Mở khóa toàn bộ chiến đấu. Thời gian tối đa mỗi hiệp là `180.0s`. Mỗi khi có sự kiện `OnPlayerDied` hoặc `OnPlayerLeft`, máy giữ `StateAuthority` đếm lại số lượng `PlayerHealthController` có `IsAlive == true`.
     - `MatchOver`: Khi `AliveCount <= 1` (hoặc hết `180.0s`), xác định `WinnerPlayerRef`, chuyển sang `MatchOver` trong `8.0s` để hiển thị bảng vinh danh và đẩy kết quả lên PlayFab trước khi cho phép bấm `Chơi Lại` (`Rematch`).
2. **Giao thức Kế thừa Quyền Tự động (`SharedModeMasterClient Failover`)**:
   - Đặt cờ `AllowStateAuthorityOverride = true` và tắt `DestroyWhenStateAuthorityLeaves = false` trên `NetworkObject` của `MatchManager`.
   - Khi callback `OnPlayerLeft(NetworkRunner runner, PlayerRef player)` kích hoạt hoặc trong `FixedUpdateNetwork()` phát hiện `Object.StateAuthority == PlayerRef.None` (hoặc chủ cũ không còn trong `runner.ActivePlayers`):
   - Máy trạm hiện có `runner.IsSharedModeMasterClient == true` và `!Object.HasStateAuthority` lập tức gọi `Object.RequestStateAuthority()`.
   - Ngay khi `StateAuthorityChanged()` xác nhận quyền mới, máy chủ mới tiếp tục chạy hàm `EvaluateLivingPlayers()` trên chính các biến `[Networked]` hiện có mà không làm gián đoạn đồng hồ `PhaseTimer`.

### Architecture Diagram

```text
+-----------------------------------------------------------------------+
|                     MatchManager (NetworkBehaviour)                   |
|  [Networked] CurrentMatchState | AliveCount | WinnerRef | PhaseTimer  |
+-------------------+-----------------------------------+---------------+
                    |                                   |
    State Machine   v                                   v  Failover on Disconnect
+-----------------------------------+   +-------------------------------+
| WaitingForPlayers (>= 2 Players)  |   | Old MasterClient Disconnects  |
|   -> Countdown (5.0s TickTimer)   |   |   -> Photon promotes new      |
|   -> Playing (180s Max, LMS Check)|   |      IsSharedModeMasterClient |
|   -> MatchOver (8.0s Podium/Stats)|   |   -> Calls RequestStateAuth() |
+-----------------------------------+   +-------------------------------+
```

### Key Interfaces

```csharp
namespace CapsuleArena.Match
{
    public enum MatchStateEnum : byte
    {
        WaitingForPlayers = 0,
        Countdown = 1,
        Playing = 2,
        MatchOver = 3
    }

    public interface IMatchStateService
    {
        MatchStateEnum CurrentMatchState { get; }
        int AliveCount { get; }
        Fusion.PlayerRef WinnerPlayerRef { get; }
        int MatchSequenceId { get; }
        event System.Action<MatchStateEnum> OnMatchStateChangedEvent;
        event System.Action<int> OnAliveCountChangedEvent;
    }
}
```

### Implementation Guidelines
- The `MatchManager` `NetworkObject` prefab/scene object **must** have `AllowStateAuthorityOverride = true` and `DestroyWhenStateAuthorityLeaves = false` configured in the Unity Inspector.
- All match timers and counters **must** be stored as `[Networked]` properties (`TickTimer`, `int`, `PlayerRef`) and **must never** be stored in local non-networked C# fields that would be lost during a MasterClient handover.
- Only the peer where `Object.HasStateAuthority == true` **must** transition `CurrentMatchState` or modify `AliveCount`.

## Alternatives Considered

### Alternative 1: Every Client Evaluates Win Condition Locally Without a Shared MatchManager
- **Description**: Mỗi máy tự đếm số người sống trên màn hình của mình và tự bật bảng thắng cuộc.
- **Pros**: Không cần chuyển giao quyền `StateAuthority`.
- **Cons**: Do độ trễ mạng khác nhau, các máy có thể kết thúc trận đấu ở các thời điểm lệch nhau hoặc tuyên bố người thắng khác nhau khi xảy ra Double-KO sát nút.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Cần một nguồn chân lý duy nhất (`Single Authoritative Match State`) để đồng bộ kết quả gửi lên bảng xếp hạng PlayFab.

### Alternative 2: Abort and Close Room Whenever MasterClient Disconnects
- **Description**: Đóng phòng và đẩy tất cả người chơi còn lại ra sảnh chờ nếu chủ phòng thoát game.
- **Pros**: Không cần viết code chuyển giao quyền.
- **Cons**: Trải nghiệm người chơi rất tệ (người chơi bị hạ gục thường thoát phòng sớm, làm hỏng trận đấu của những người còn sống).
- **Estimated Effort**: Rất thấp.
- **Rejection Reason**: Cơ chế kế thừa `RequestStateAuthority()` trong Photon Fusion 2 Shared Mode giải quyết triệt để vấn đề này chỉ với ~25 dòng code sạch.

## Consequences

### Positive
- Trận đấu tiếp diễn hoàn toàn liền mạch ngay cả khi chủ phòng ban đầu tắt ngang ứng dụng (`Alt+F4`) hoặc rớt mạng.
- Mọi máy trạm đều nhìn thấy cùng một kết quả thắng/thua (`WinnerPlayerRef`) và cùng một mã phiên trận đấu (`MatchSequenceId`).

### Negative
- Trong khoảng `1–3 ticks` (`16–50 ms`) chuyển giao quyền giữa chủ cũ và chủ mới, việc xét thắng thua tạm hoãn cho đến khi `StateAuthorityChanged` hoàn tất (hoàn toàn không thể nhận biết bằng mắt thường).

### Neutral
- Sử dụng `MatchSequenceId` tăng dần mỗi khi bấm `Rematch` để phân biệt các ván đấu liên tiếp trong cùng một phòng.

## Risks
- **Rủi ro hai người chơi cuối cùng hạ gục nhau trong cùng một tick (`Simultaneous Double-KO`)**: Giải pháp giảm thiểu là lưu `LastAlivePlayerRef` trước mỗi lần trừ máu; nếu `AliveCount` giảm từ `1` xuống `0` trong cùng tick, người sống sót cuối cùng bị hạ gục sau sẽ nhận hạng Nhất hoặc tuyên bố `Draw (WinnerPlayerRef = PlayerRef.None)` theo quy tắc rõ ràng trong `MatchManager`.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `match-manager-authority-failover.md` | `TR-match-001`: Synchronize `[Networked] MatchState`, `AliveCount`, `WinnerPlayerRef`, and `PhaseTimer` across all peers | Defines `MatchManager` networked properties and `OnChangedRender` hooks |
| `match-manager-authority-failover.md` | `TR-match-002`: Evaluate Last Man Standing win conditions when `AliveCount <= 1` (including Double-KO handling) and transition to `MatchOver` | Specifies authoritative `EvaluateLivingPlayers()` state machine rules |
| `match-manager-authority-failover.md` | `TR-match-003`: Remaining `IsSharedModeMasterClient` invokes `Object.RequestStateAuthority()` on disconnect without resetting timers | Defines the `AllowStateAuthorityOverride` + `RequestStateAuthority()` failover protocol |

## Performance Implications
- **CPU**: `< 0.02 ms/tick` — chỉ duyệt danh sách tối đa 4 người chơi khi có sự kiện hạ gục hoặc thoát phòng.
- **Memory**: `0 B` cấp phát động trong vòng lặp trận đấu.
- **Load Time**: Không ảnh hưởng.
- **Network**: Toàn bộ trạng thái `MatchManager` chỉ chiếm `20 bytes` trong gói tin đồng bộ của Photon Fusion.

## Migration Plan
Đặt `MatchManager` làm đối tượng mạng cố định trong `ArenaScene` với cấu hình `AllowStateAuthorityOverride = true`.

**Rollback plan**: Nếu việc tự động `Rematch` trong cùng cảnh gặp lỗi trạng thái dư thừa từ bên thứ ba, gọi `Runner.LoadScene("ArenaScene")` để khởi tạo lại cảnh sạch giữa các ván.

## Validation Criteria
- PlayMode Integration Test: Khởi tạo trận đấu 3 người chơi, hạ gục người chơi #1 (đang là MasterClient) và ngắt kết nối người chơi #1; xác nhận người chơi #2 tự động nhận `HasStateAuthority == true` trên `MatchManager` và khi người chơi #2 hạ gục người chơi #3, trận đấu chuyển sang `MatchOver` với `WinnerPlayerRef == Player #2`.

## Related
- Depends on: [`adr-0003-photon-fusion-shared-mode-and-object-pooling.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0003-photon-fusion-shared-mode-and-object-pooling.md), [`adr-0004-tick-accurate-mobility-and-damage-rpc.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0004-tick-accurate-mobility-and-damage-rpc.md)
- Enables: [`adr-0008-playfab-cloud-leaderboard-and-spatial-vfx-pool.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0008-playfab-cloud-leaderboard-and-spatial-vfx-pool.md)
- GDD: [`match-manager-authority-failover.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/match-manager-authority-failover.md)
