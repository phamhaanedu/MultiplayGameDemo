# ADR-0008: PlayFab Cloud Identity/Leaderboard Sync & Pooled 3D Spatial Audio/URP Combat VFX

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `engine-programmer`, `technical-artist`, `gameplay-programmer`

## Summary

Quyết định này thiết lập dịch vụ đám mây bất đồng bộ `PlayFabAuthService` và `PlayFabStatsService` (xác thực định danh, đồng bộ bảng xếp hạng `MatchesWon` / `TotalKills` với cơ chế chống gửi trùng `MatchSequenceId` và chế độ khách ngoại tuyến `Offline Guest Fallback`), đồng thời chuẩn hóa hệ thống âm thanh không gian 3D (`SoundManager` với `16` nguồn `AudioSource` trong hồ chứa) và hiệu ứng chiến đấu URP (`CombatVFXManager` sử dụng `MaterialPropertyBlock` + `DOTween`).

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | Audio / Rendering / Networking |
| **Layer** | Presentation |
| **Knowledge Risk** | MEDIUM |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/playfab-cloud-leaderboard.md`, `design/gdd/spatial-audio-combat-vfx.md`, `design/art/art-bible.md` |
| **Post-Cutoff APIs Used** | PlayFab C# Client SDK (`PlayFabClientAPI`), Unity `AudioMixer`, URP `MaterialPropertyBlock`, `DG.Tweening.DOTween` |
| **Verification Required** | EditMode & PlayMode tests verifying PlayFab retry/idempotency guard, Offline Guest fallback, 16-source 3D audio pool voice-stealing, and zero material instance leaks on hit-flash |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0005, ADR-0006 |
| **Enables** | None |
| **Blocks** | EPIC-011 (PlayFab Cloud Identity & Leaderboard), EPIC-012 (3D Spatial Audio & Combat VFX Polish) |
| **Ordering Note** | Requires `ADR-0005` (combat hit/slash events) and `ADR-0006` (`MatchOver` event & `MatchSequenceId`). |

## Context

### Problem Statement
Trò chơi cần tích hợp dịch vụ đám mây **PlayFab** để lưu trữ tên hiển thị (`DisplayName`) và bảng xếp hạng người chơi (`MatchesWon`, `TotalKills`, `MatchesPlayed`), nhưng tuyệt đối không được làm treo luồng chính hoặc chặn việc vào phòng Photon Fusion nếu máy chủ PlayFab phản hồi chậm hoặc mất kết nối Internet cục bộ. Song song đó, để đạt chất lượng "Game Feel" (Juiciness) cao nhất cho các đòn Chém dọc (`#F59E0B`), Chém ngang xoay `90°` (`#38BDF8`) và Đạn tầm xa (`#FACC15`), hệ thống âm thanh 3D và hiệu ứng hình ảnh phải phản hồi tức thời mà không nhân bản vật liệu (`renderer.material` leak) hay cấp phát bộ nhớ rác.

### Current State
Bảng màu hiệu ứng, thông số `AudioMixer` 4 nhóm và quy tắc `MaterialPropertyBlock` đã được định nghĩa đồng bộ giữa `art-bible.md`, `playfab-cloud-leaderboard.md` và `spatial-audio-combat-vfx.md`.

### Constraints
- Nghiêm cấm truy cập `renderer.material` lúc runtime để làm hiệu ứng nháy sáng trúng đòn; bắt buộc dùng `MaterialPropertyBlock` (`_EmissionColor`).
- Các lời gọi API tới PlayFab chỉ được thực hiện ở ngoài vòng lặp chiến đấu (lúc đăng nhập ở Lobby và đúng 1 lần khi chuyển sang trạng thái `MatchOver`).
- Hồ chứa âm thanh 3D giới hạn cứng ở `16` nguồn `AudioSource` tái sử dụng xoay vòng kèm thuật toán ưu tiên âm thanh quan trọng (`Priority Voice Stealing`).

### Requirements
- Đáp ứng trọn vẹn `TR-playfab-001`, `TR-playfab-002`, `TR-playfab-003` và `TR-audio-001`, `TR-audio-002`, `TR-audio-003`.

## Decision

1. **Xác thực PlayFab & Chế độ Dự phòng Ngoại tuyến (`PlayFabAuthService`)**:
   - Hỗ trợ đăng nhập nhanh một chạm qua `LoginWithCustomID` (dùng mã định danh thiết bị bền vững kèm hậu tố cấu hình cho phép mở nhiều cửa sổ kiểm thử trên cùng 1 máy PC: `DeviceUniqueIdentifier + "_" + InstanceProfileIndex`) và đăng nhập bằng `Email/Password`.
   - Sau khi đăng nhập, đồng bộ `DisplayName` (`3..16` ký tự) vào biến `[Networked] NetworkString<_16> PlayerName` khi vào phòng Photon.
   - Nếu quá thời gian chờ `6.0s` hoặc lỗi mạng, tự động chuyển sang `Offline Guest Mode` với tên `Guest_XXXX` để người chơi vẫn vào đấu trường Photon Fusion bình thường.
2. **Đồng bộ Bảng xếp hạng Chống Gửi Trùng (`PlayFabStatsService`)**:
   - Khi `MatchManager` phát sự kiện `OnMatchOver(int matchSequenceId, PlayerRef winner)`, mỗi máy trạm kiểm tra `if (_lastSubmittedMatchSeqId == matchSequenceId) return;` rồi mới gọi `PlayFabClientAPI.UpdatePlayerStatistics` để cộng dồn `MatchesPlayed (+1)`, `TotalKills (+localKills)`, và `MatchesWon (+1 nếu là người thắng)`.
   - Nếu gặp lỗi mạng tạm thời (`503` / Timeout), tự động thử lại tối đa `3 lần` theo cấp số nhân (`1.0s`, `2.0s`, `4.0s`).
   - Truy vấn bảng xếp hạng (`GetLeaderboard` Top 20 và `GetLeaderboardAroundPlayer`) sử dụng bộ nhớ đệm `15.0s TTL` để tránh vượt giới hạn tần suất gọi API của PlayFab.
3. **Âm thanh Không gian 3D & Hồ chứa Hiệu ứng URP (`SoundManager` & `CombatVFXManager`)**:
   - `SoundManager` định tuyến qua `4` nhóm `AudioMixer` (`Master`, `BGM`, `SFX_3D`, `UI_2D`) và quản lý hồ chứa `16` đối tượng `PooledAudioEmitter` (`spatialBlend = 1.0`, `Rolloff = Linear`, `minDistance = 2.0m`, `maxDistance = 28.0m`, biến thiên cao độ ngẫu nhiên `pitch ∈ [0.92, 1.08]`).
   - `CombatVFXManager` quản lý hồ chứa `20` hệ thống hạt/vệt chém (`VFX_Slash_Vertical` `#F59E0B`, `VFX_Slash_Horizontal` `#38BDF8`, `VFX_Projectile_Impact` `#FACC15`, `VFX_Mobility_DashTrail`).
   - Hiệu ứng nháy sáng khi trúng đòn (`HitFlash` trong `0.08s`) sử dụng duy nhất một đối tượng `MaterialPropertyBlock` dùng chung để ghi đè `_EmissionColor = Color.white * 2.5f` trên `ChildVisualRoot`, kết hợp `DOTween` giật co giãn `(1.28, 0.72, 1.28)` và rung chấn camera (`Camera Trauma Shake`).

### Architecture Diagram

```text
+----------------------------------+     +----------------------------------+
|     MatchManager / Combat        |     |         Lobby UI / Auth          |
| (OnMatchOver / OnHitConfirmed)   |     |   (Login / Leaderboard Modal)    |
+--------+-----------------+-------+     +-----------------+----------------+
         |                 |                               |
         v (Combat Events) v (MatchOver + MatchSeqId)      v
+--------------------+  +---------------------------------------------------+
| SoundManager (16)  |  | PlayFabAuthService & PlayFabStatsService          |
| CombatVFXMgr (20)  |  | - CustomID (Multi-Instance Safe) / Email Auth     |
| MPB HitFlash 0.08s |  | - Idempotent UpdatePlayerStatistics (Retry 1/2/4s)|
| DOTween Squash     |  | - 15s Cached GetLeaderboard + Offline Guest Mode  |
+--------------------+  +---------------------------------------------------+
```

### Key Interfaces

```csharp
namespace CapsuleArena.Cloud
{
    public interface ICloudIdentityService
    {
        bool IsAuthenticated { get; }
        bool IsOfflineGuest { get; }
        string DisplayName { get; }
        void LoginWithCustomId(string customId, System.Action<bool> onComplete);
        void SubmitMatchResult(int matchSequenceId, bool wonMatch, int killsEarned);
    }
}

namespace CapsuleArena.Presentation
{
    public interface ICombatFeedbackService
    {
        void Play3DSfx(int clipId, UnityEngine.Vector3 worldPos, float volumeScale = 1f);
        void SpawnCombatVfx(int vfxTypeId, UnityEngine.Vector3 worldPos, UnityEngine.Quaternion worldRot);
        void TriggerHitFeedback(UnityEngine.Renderer[] targetRenderers, UnityEngine.Transform visualRoot);
    }
}
```

### Implementation Guidelines
- `HitFeedbackController` **must** use `Renderer.GetPropertyBlock(_sharedMpb)` and `Renderer.SetPropertyBlock(_sharedMpb)` for hit-flash effects and **must never** access `Renderer.material`, which duplicates material instances in memory.
- `PlayFabAuthService` **must** append a command-line or ParrelSync/Multiplayer-Play-Mode profile suffix to `SystemInfo.deviceUniqueIdentifier` when generating the `CustomID` so multiple test instances on one PC log into distinct PlayFab accounts.
- `PlayFabStatsService` **must** guard `UpdatePlayerStatistics` with `MatchSequenceId` idempotency tracking so duplicate `MatchOver` notifications never double-count wins or kills.

## Alternatives Considered

### Alternative 1: Use `AudioSource.PlayClipAtPoint` for 3D Combat Sounds
- **Description**: Gọi hàm có sẵn `AudioSource.PlayClipAtPoint(clip, pos)` của Unity mỗi khi chém kiếm hoặc đạn nổ.
- **Pros**: Chỉ tốn 1 dòng code.
- **Cons**: `PlayClipAtPoint` tự động gọi `new GameObject()` và `Destroy()` ngầm bên dưới mỗi lần phát âm thanh, tạo rác bộ nhớ liên tục, không gán được vào nhóm `AudioMixerGroup (SFX_3D)` và không chỉnh được đường cong giảm âm `2m–28m`.
- **Estimated Effort**: Rất thấp.
- **Rejection Reason**: Vi phạm quy tắc Object Pooling và tối ưu hóa âm thanh.

### Alternative 2: Block Photon Room Entry Until PlayFab Authentication Succeeds
- **Description**: Cấm người chơi bấm nút Vào Phòng nếu PlayFab trả về lỗi hoặc không có mạng.
- **Pros**: Đảm bảo 100% người chơi trong phòng đều có PlayFab ID.
- **Cons**: Nếu dịch vụ PlayFab bảo trì hoặc giảng viên chấm bài trong mạng LAN hạn chế, trò chơi không thể vào đấu trường để kiểm thử phần mạng Photon Fusion.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Cơ chế `Offline Guest Fallback` đảm bảo luồng cốt lõi của trận đấu luôn hoạt động trong mọi điều kiện mạng.

## Consequences

### Positive
- Hỗ trợ mở đồng thời 2–4 cửa sổ game trên cùng một máy tính để chấm điểm/kiểm thử mà mỗi cửa sổ vẫn có một tài khoản PlayFab và tên hiển thị riêng biệt trên bảng xếp hạng.
- Âm thanh 3D và hiệu ứng chém kiếm/đạn nổ đạt độ nảy thị giác ("Juice") cao với `0 byte` cấp phát bộ nhớ rác và `0` bản sao vật liệu dư thừa.

### Negative
- Bảng xếp hạng có độ trễ cập nhật tối đa `15.0s` do bộ nhớ đệm phía máy trạm (có nút `Làm mới` thủ công sau mỗi `15s`).

### Neutral
- Tắt hoàn toàn hiệu ứng `HitStop` đóng băng thời gian toàn cục (`Time.timeScale` luôn giữ bằng `1.0`) để không làm lệch nhịp mô phỏng mạng `60Hz`, thay thế bằng việc dừng hoạt ảnh cục bộ trên `ChildVisualRoot` trong `0.05s`.

## Risks
- **Rủi ro hai cửa sổ build chạy trên cùng một máy PC bị trùng `SystemInfo.deviceUniqueIdentifier`**: Đã được giải quyết triệt để bằng cách nối thêm tham số dòng lệnh `-profile <N>` hoặc mã định danh tiến trình vào `CustomID`.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `playfab-cloud-leaderboard.md` | `TR-playfab-001`: Authenticate via `LoginWithCustomID`/`Email`, sync `DisplayName` (`3..16` chars) to `[Networked] PlayerName`, and fallback to Offline Guest | Defines multi-instance safe `PlayFabAuthService` and Offline Guest fallback |
| `playfab-cloud-leaderboard.md` | `TR-playfab-002`: Submit `MatchesWon`, `TotalKills`, `MatchesPlayed` at `MatchOver` with `MatchSequenceId` idempotency and `1s/2s/4s` backoff retry | Specifies `PlayFabStatsService` idempotency guard and retry queue |
| `playfab-cloud-leaderboard.md` | `TR-playfab-003`: `LeaderboardModalView` fetches Top 20 and AroundPlayer with `15.0s` cache TTL and highlights local row in `#38BDF8` | Defines cached leaderboard query and row highlighting |
| `spatial-audio-combat-vfx.md` | `TR-audio-001`: Route audio through 4 `AudioMixer` groups using 16 pooled 3D `AudioSources` (`2.0m..28.0m`) with `±8%` pitch variation | Specifies `SoundManager` 16-emitter pool and mixer routing |
| `spatial-audio-combat-vfx.md` | `TR-audio-002`: Render pooled URP VFX for Vertical Slash (`#F59E0B`), Horizontal Sweep (`#38BDF8`), Ranged (`#FACC15`), and Dash/Dodge trails | Establishes 20-instance `CombatVFXManager` pool and URP additive/alpha shaders |
| `spatial-audio-combat-vfx.md` | `TR-audio-003`: Apply `0.08s` MPB `_EmissionColor` hit-flash, DOTween squash `(1.28, 0.72, 1.28)`, and camera trauma shake without mutating shared materials | Enforces `MaterialPropertyBlock` and `ChildVisualRoot` tweening |

## Performance Implications
- **CPU**: `< 0.10 ms/frame` cho quản lý hồ chứa âm thanh/VFX và cập nhật `MaterialPropertyBlock`.
- **Memory**: `0 B` cấp phát động khi phát âm thanh hoặc hiệu ứng trúng đòn; tiết kiệm hàng chục MB nhờ không nhân bản `Material`.
- **Load Time**: `+10 ms` lúc nạp cảnh để khởi tạo trước 16 nguồn âm thanh và 20 đối tượng VFX.
- **Network**: Các yêu cầu HTTPS REST tới PlayFab chỉ diễn ra ở Lobby và màn hình `MatchOver` (`0 bps` trong lúc đang chiến đấu).

## Migration Plan
Triển khai `PlayFabAuthService.cs`, `PlayFabStatsService.cs`, `LeaderboardModalView.cs`, `SoundManager.cs`, và `CombatVFXManager.cs` ở tầng `Polish` / `Presentation`, kết nối qua C# Events với `MatchManager` và `PlayerCombatController`.

**Rollback plan**: Nếu chưa cấu hình `PlayFabTitleId` trên máy chấm bài, `PlayFabAuthService` tự động chuyển sang chế độ `Offline Guest Mode` ngay lập tức mà không báo lỗi đỏ trên Console.

## Validation Criteria
- EditMode Unit Test xác nhận gọi `SubmitMatchResult` 3 lần liên tiếp với cùng một `matchSequenceId` chỉ gửi đúng `1` yêu cầu cập nhật chỉ số.
- PlayMode Test kích hoạt `TriggerHitFeedback` 100 lần liên tiếp xác nhận số lượng `Material` trong bộ nhớ không tăng thêm (`0` material leak).

## Related
- Depends on: [`adr-0005-multidirectional-combat-and-lag-compensation.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0005-multidirectional-combat-and-lag-compensation.md), [`adr-0006-match-manager-masterclient-failover.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0006-match-manager-masterclient-failover.md)
- GDDs: [`playfab-cloud-leaderboard.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/playfab-cloud-leaderboard.md), [`spatial-audio-combat-vfx.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/spatial-audio-combat-vfx.md)
