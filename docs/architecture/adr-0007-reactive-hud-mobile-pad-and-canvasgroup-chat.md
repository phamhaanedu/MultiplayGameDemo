# ADR-0007: Event-Driven Combat HUD, 7-Button Mobile Action Pad & CanvasGroup Persistent Chat

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `ux-designer`, `gameplay-programmer`

## Summary

Quyết định này chuẩn hóa toàn bộ tầng giao diện người dùng (`Presentation`) theo mô hình hướng sự kiện (`Observer Pattern` / `OnChangedRender`), bao gồm thanh máu nổi 3D (`WorldSpaceBillboardUI`), cụm 7 nút cảm ứng góc dưới phải (`MobileActionPadView`) đồng bộ hai chiều với bàn phím PC, và hệ thống Chat Công khai / Thì thầm (`InGameChatController`) sử dụng `CanvasGroup` để giữ `GameObject` luôn hoạt động nhằm đảm bảo không bao giờ làm rớt gói tin RPC.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | UI / Networking |
| **Layer** | Presentation |
| **Knowledge Risk** | MEDIUM |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/combat-hud-mobile-action-pad.md`, `design/gdd/in-game-chat-system.md` |
| **Post-Cutoff APIs Used** | Unity uGUI (`Canvas`, `CanvasScaler`, `CanvasGroup`, `TextMeshProUGUI`), Photon Fusion 2 Targeted `[Rpc]` |
| **Verification Required** | PlayMode UI tests verifying Billboard camera alignment, Mobile Action Pad radial cooldown fill + keybind badge refresh, and Chat RPC delivery while panel alpha is faded (`0.15`) |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0002, ADR-0004, ADR-0005 |
| **Enables** | None |
| **Blocks** | EPIC-009 (Combat HUD, World Billboard & Mobile Action Pad), EPIC-010 (In-Game Public & Whisper Chat) |
| **Ordering Note** | Requires `ADR-0002` (`MobileInputBridge` & `InputFocusManager`), `ADR-0004` (Health & Mobility cooldowns), and `ADR-0005` (Combat cooldowns). |

## Context

### Problem Statement
Giao diện chiến đấu cần hiển thị mượt mà ở tốc độ `60 FPS` mà không làm giảm hiệu năng do truy vấn `GetComponent` hay tạo chuỗi `string` liên tục mỗi khung hình. Đặc biệt, cụm 7 nút kỹ năng ở góc dưới phải (`Mobile Action Pad`) phải vừa nhận thao tác chạm (`PointerDown`), vừa phản hồi hiệu ứng lún nút (`0.90x scale`) và vòng quay hồi chiêu (`fillAmount`) khi người chơi bấm phím cứng trên PC (`LMB, RMB, 1, 2, 3, Q, E`). Ngoài ra, một lỗi kinh điển trong Unity + Photon Fusion là tắt `gameObject.SetActive(false)` trên khung Chat khi người chơi đóng khung nhập liệu, khiến `NetworkBehaviour` ngừng nhận các lời gọi `Rpc_ReceiveChatMessage` từ người chơi khác.

### Current State
Thiết kế chi tiết cho HUD, Billboard, Mobile Action Pad và Chat đã được phê duyệt trong `combat-hud-mobile-action-pad.md` và `in-game-chat-system.md`.

### Constraints
- Nghiêm cấm gọi `GameObject.Find`, `GetComponent` hoặc nối chuỗi `+` trong `Update()` của các lớp UI; bắt buộc dùng `StringBuilder` hoặc bảng chuỗi số nguyên dựng sẵn (`"0.1"`..`"9.9"`) cho bộ đếm hồi chiêu.
- `InGameChatController` phải luôn giữ `gameObject.activeInHierarchy == true` trong suốt trận đấu.
- Tin nhắn thì thầm (`/w <Tên> <Nội dung>`) chỉ được gửi tới đúng người nhận (`[RpcTarget] PlayerRef`) và người gửi, không phát tán ra toàn phòng để tránh lộ thông tin.

### Requirements
- Đáp ứng trọn vẹn `TR-hud-001`, `TR-hud-002`, `TR-hud-003` và `TR-chat-001`, `TR-chat-002`, `TR-chat-003`.

## Decision

1. **Thanh Máu Nổi `WorldSpaceBillboardUI` & HUD Chính (`ScreenSpaceCombatHUD`)**:
   - `WorldSpaceBillboardUI` xoay mặt phẳng Canvas về phía camera trong `LateUpdate()` bằng phép gán `transform.rotation = _mainCameraTransform.rotation`. Sử dụng 2 lớp thanh máu: lớp chính cập nhật tức thời (`#22C55E` cho bản thân, `#EF4444` cho địch) và lớp bóng mờ (`Ghost Damage Bar` `#FACC15`) co lại sau `0.25s` bằng `DOTween`.
   - `ScreenSpaceCombatHUD` lắng nghe sự kiện `OnHealthChanged`, `OnAliveCountChanged`, và `OnKillConfirmed` để cập nhật thanh máu góc dưới trái, bộ đếm sinh tồn góc trên phải, và danh sách hạ gục (`KillFeed` tối đa 4 dòng).
2. **Cụm 7 Nút Cảm ứng Đa Nền tảng (`MobileActionPadView`)**:
   - Bố trí 7 nút theo cung tròn thái học ở góc dưới phải màn hình: `VerticalSlash (LMB)` làm nút tâm lớn (`110x110 px`), bao quanh bởi `HorizontalLeft (1)`, `HorizontalRight (3)`, `RangedFire (RMB)`, `ForwardDash (2)`, `DodgeLeft (Q)`, và `DodgeRight (E)`.
   - Khi chạm cảm ứng hoặc click chuột lên nút (`IPointerDownHandler`), ghi cờ tương ứng vào `MobileInputBridge`. Khi bấm phím cứng trên PC hoặc chạm cảm ứng, nút thực hiện hiệu ứng lún `DOTween` (`0.90x`) và cập nhật lớp phủ quạt `Image.fillAmount = RemainingCooldown / TotalCooldown`.
   - Lắng nghe `KeybindingManager.OnBindingsChanged` để cập nhật nhãn phím (`Keybind Badge`) ở góc mỗi nút tức thời khi người chơi đổi phím.
3. **Kiến trúc Chat Bền vững qua `CanvasGroup` & Định tuyến Whisper (`InGameChatController`)**:
   - Giữ `InGameChatController.gameObject` luôn ở trạng thái `activeSelf == true`. Khi ở chế độ thu gọn (`Passive Mode`), đặt `InputFieldCanvasGroup.alpha = 0f`, `interactable = false`, `blocksRaycasts = false`, và giảm độ mờ khung lịch sử xuống `alpha = 0.15f` sau `8.0s` không có tin nhắn mới.
   - Bộ phân tích cú pháp hỗ trợ Chat Công khai và lệnh Thì thầm (`/w <Tên> <Tin nhắn>`, `/whisper`, hoặc bấm `Tab` để trả lời nhanh người vừa nhắn cho mình).
   - Loại bỏ toàn bộ thẻ định dạng (`Rich Text Tag Sanitization`: thay `<` và `>` hoặc thoát thẻ) trên nội dung người chơi nhập vào để chống giả mạo tin nhắn hệ thống, giới hạn `160 ký tự`, và áp dụng bộ lọc chống spam `Token Bucket` (`5 tin nhắn`, hồi `1 token / 1.5s`) cùng hồ chứa xoay vòng `50 dòng` tin nhắn (`Circular Row Pool`).

### Architecture Diagram

```text
+-----------------------------------------------------------------------+
|                    Presentation Layer (uGUI Canvas)                   |
|                                                                       |
|  +-------------------------+    +----------------------------------+  |
|  |   MobileActionPadView   |    |      InGameChatController        |  |
|  | - 7 Radial Action Btns  |    | - Root GameObject ALWAYS Active  |  |
|  | - Writes MobileBridge   |    | - CanvasGroup Alpha (1.0 / 0.15) |  |
|  | - Reads TickTimer CDs   |    | - Public & Targeted Whisper RPC  |  |
|  +-------------------------+    +----------------------------------+  |
|                                                                       |
|  +-------------------------+    +----------------------------------+  |
|  |  ScreenSpaceCombatHUD   |    |     WorldSpaceBillboardUI        |  |
|  | - HP, AliveCount, Feed  |    | - LateUpdate Camera Orientation  |  |
|  +-------------------------+    +----------------------------------+  |
+-----------------------------------------------------------------------+
```

### Key Interfaces

```csharp
namespace CapsuleArena.UI.HUD
{
    public interface ICombatHUDPresenter
    {
        void BindLocalPlayer(CapsuleArena.Player.Health.IDamageableTarget health, CapsuleArena.Player.Movement.IMobilityStateReadOnly mobility, CapsuleArena.Player.Combat.ICombatStateReadOnly combat);
        void ShowKillFeedEntry(string killerName, string victimName, byte attackType);
    }
}

namespace CapsuleArena.UI.Chat
{
    public enum ChatChannelType : byte
    {
        Public = 0,
        Whisper = 1,
        SystemAnnouncement = 2
    }
}
```

### Implementation Guidelines
- `InGameChatController` **must** control visibility via `CanvasGroup.alpha`, `CanvasGroup.interactable`, and `CanvasGroup.blocksRaycasts`, and **must never** call `gameObject.SetActive(false)` on the networked chat object.
- UI scripts **must** use a shared `StringBuilder` or pre-cached string lookup table when formatting cooldown numbers and **must never** allocate new strings every frame when a cooldown is `0`.
- All user-submitted chat strings **must** be sanitized to strip TMP rich-text tags (`<color>`, `<size>`, `<sprite>`) and truncated to `160` characters before network transmission.

## Alternatives Considered

### Alternative 1: Toggle `chatPanelGameObject.SetActive(false)` When Chat Is Closed
- **Description**: Tắt hẳn `GameObject` của khung Chat khi người chơi nhấn `Enter` gửi xong hoặc nhấn `Escape`.
- **Pros**: Thói quen phổ biến khi làm UI đơn giản.
- **Cons**: Trong Photon Fusion, một `NetworkBehaviour` nằm trên `GameObject` bị `SetActive(false)` sẽ không xử lý RPC gửi tới nó, làm mất toàn bộ tin nhắn chat của đồng đội khi khung chat đang đóng.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Sử dụng `CanvasGroup` giữ nguyên kết nối RPC 100% đồng thời tiết kiệm chi phí bật/tắt cây phân cấp UI (`Rebuild Hierarchy`).

### Alternative 2: Broadcast Whisper Messages to All Clients and Filter in UI
- **Description**: Gửi tin nhắn mật cho tất cả mọi người trong phòng rồi dùng câu lệnh `if` ở từng máy để ẩn đi nếu không phải người nhận.
- **Pros**: Dùng chung 1 hàm RPC không cần tham số `[RpcTarget]`.
- **Cons**: Lộ nội dung tin nhắn mật trên đường truyền mạng của các đối thủ khác và tốn băng thông vô ích.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Sử dụng `[RpcTarget] PlayerRef targetPlayer` của Photon Fusion 2 định tuyến gói tin Whisper chỉ tới đúng máy người nhận.

## Consequences

### Positive
- Khung Chat nhận đủ 100% tin nhắn công khai và thì thầm ở mọi trạng thái ẩn/hiện, tự động khóa điều khiển chiến đấu thông qua `InputFocusManager` khi đang gõ chữ.
- Cụm 7 nút `MobileActionPadView` giúp trò chơi sẵn sàng chuyển đổi sang Android/iOS ngay lập tức mà vẫn đóng vai trò bảng hiển thị hồi chiêu trực quan trên PC.

### Negative
- Cần khởi tạo sẵn 50 dòng `ChatRowView` lúc nạp cảnh (chiếm khoảng `200 KB` bộ nhớ UI).

### Neutral
- Tách biệt hoàn toàn các Canvas tĩnh (`Static Frame Canvas`) và Canvas động (`Dynamic Cooldown/HP Canvas`) để tối ưu hóa chi phí vẽ lại (`Canvas Batch Rebuild`).

## Risks
- **Rủi ro tràn bố cục cụm 7 nút trên màn hình tỷ lệ siêu hẹp (`4:3`) hoặc siêu rộng (`21:9`)**: Giải pháp giảm thiểu là neo (`Anchor`) cụm nút vào góc dưới phải `(1, 0)` với `CanvasScaler` chuẩn `1920x1080` (`Match Width Or Height = 0.5`).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `combat-hud-mobile-action-pad.md` | `TR-hud-001`: `WorldSpaceBillboardUI` orients toward `Camera.main` in `LateUpdate` with 0.25s ghost bar and `#22C55E`/`#EF4444` palettes | Specifies `LateUpdate` quaternion copy and dual-fill HP bar tweening |
| `combat-hud-mobile-action-pad.md` | `TR-hud-002`: `MobileActionPadView` 7-button cluster with radial cooldown fill, seconds text, keybind badges, and dual input pulse | Defines the 7-button radial cluster architecture and `MobileInputBridge` binding |
| `combat-hud-mobile-action-pad.md` | `TR-hud-003`: `ScreenSpaceCombatHUD` updates strictly via C# Observer events and `OnChangedRender` without per-frame `Find`/`GetComponent` | Enforces event-driven presenter bindings and `StringBuilder` formatting |
| `in-game-chat-system.md` | `TR-chat-001`: `InGameChatController` keeps root `GameObject` active and controls visibility strictly via `CanvasGroup` | Mandates `CanvasGroup` visibility states (`Active`, `Passive`, `Faded`) |
| `in-game-chat-system.md` | `TR-chat-002`: Support Public and `/w <TargetName> <Message>` Whisper commands via targeted RPC with rich-text sanitization | Defines command parser, `[RpcTarget]` Whisper routing, and tag stripping |
| `in-game-chat-system.md` | `TR-chat-003`: Enforce 5-message token bucket rate limiter (`1 token/1.5s`) and 50-row circular UI pool | Specifies token-bucket anti-spam guard and zero-alloc row recycling |

## Performance Implications
- **CPU**: `< 0.15 ms/frame` cho cập nhật vòng quay hồi chiêu (chỉ cập nhật các nút đang thực sự trong thời gian hồi chiêu) và xoay tối đa 4 thanh máu Billboard.
- **Memory**: `0 B` cấp phát chuỗi trong khung hình nhờ bảng chuỗi số thập phân dựng sẵn (`"0.1"`..`"9.9"`) và hồ chứa 50 dòng chat cố định.
- **Load Time**: `< 3 ms` khi khởi tạo sẵn 50 dòng chat.
- **Network**: Tin nhắn chat chỉ phát sinh băng thông khi người chơi nhấn gửi (`~64–200 bytes/tin nhắn`), được bảo vệ bởi bộ giới hạn tốc độ `Token Bucket`.

## Migration Plan
Triển khai `WorldSpaceBillboardUI.cs`, `ScreenSpaceCombatHUD.cs`, `MobileActionPadView.cs`, và `InGameChatController.cs` trong `Assets/_Project/Scripts/UI/`.

**Rollback plan**: Người chơi trên PC không muốn nhìn thấy cụm nút ảo có thể chuyển `MobileActionPadVisibilityMode` sang `Off` trong bảng cài đặt mà vẫn giữ nguyên thanh hiển thị kỹ năng thu gọn.

## Validation Criteria
- EditMode Unit Test xác nhận bộ phân tích cú pháp `/w PlayerTwo Xin chào!` tách đúng tên người nhận, nội dung, và lọc bỏ các thẻ `<color=red>` độc hại.
- PlayMode Test xác nhận khi khung Chat đang ở trạng thái ẩn (`CanvasGroup.alpha == 0.15f`), việc gửi một `Rpc_ReceiveChatMessage` vẫn thêm dòng mới vào lịch sử và làm sáng khung Chat lên `alpha == 0.85f`.

## Related
- Depends on: [`adr-0002-keybinding-json-and-input-focus-architecture.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0002-keybinding-json-and-input-focus-architecture.md), [`adr-0004-tick-accurate-mobility-and-damage-rpc.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0004-tick-accurate-mobility-and-damage-rpc.md), [`adr-0005-multidirectional-combat-and-lag-compensation.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0005-multidirectional-combat-and-lag-compensation.md)
- GDDs: [`combat-hud-mobile-action-pad.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/combat-hud-mobile-action-pad.md), [`in-game-chat-system.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/in-game-chat-system.md)
