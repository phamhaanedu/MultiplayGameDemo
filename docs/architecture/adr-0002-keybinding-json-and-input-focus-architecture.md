# ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManager

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `engine-programmer`, `ux-designer`

## Summary

Quyết định này thiết lập kiến trúc thu thập và tùy biến điều khiển hợp nhất cho cả PC và Mobile thông qua `KeybindingManager` (lưu trữ nguyên tử vào `Application.persistentDataPath/keybindings.json`), `LocalInputPoller` kết hợp `MobileInputBridge` để đóng gói tín hiệu vào cấu trúc mạng `NetworkInputData` (`16 bytes`), và `InputFocusManager` dùng mặt nạ bit để khóa tức thời tín hiệu chiến đấu khi người chơi đang gõ Chat hoặc mở bảng cài đặt phím.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | Input / UI |
| **Layer** | Foundation |
| **Knowledge Risk** | LOW |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/keybinding-input-system.md`, `design/gdd/keybinding-rebind-lobby-ui.md` |
| **Post-Cutoff APIs Used** | Unity Input System (`InputAction`, `InputActionRebindingExtensions.PerformInteractiveRebinding`, `SaveBindingOverridesAsJson`) |
| **Verification Required** | EditMode tests for JSON serialization, corrupt file recovery, conflict swap logic, and `InputFocusFlag` suppression |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | ADR-0004, ADR-0007 |
| **Blocks** | EPIC-002 (Input, Keybinding JSON & Lobby Flow) |
| **Ordering Note** | Must be completed before player locomotion/combat input polling (`ADR-0004`/`ADR-0005`) and HUD/Chat UI (`ADR-0007`). |

## Context

### Problem Statement
Người chơi cần hệ thống điều khiển hỗ trợ đầy đủ 13 hành động (bao gồm `Key 2` Lướt tiến, `Q/E` Lăn né, `LMB` Chém dọc, `Keys 1/3` Chém ngang & Xoay `90°`, `RMB` Bắn xa, `V` Đổi góc nhìn, `Enter` Chat) có thể đổi phím trực tiếp trong game và lưu bền vững ra tệp `keybindings.json`. Đồng thời, trò chơi phải sẵn sàng cho nền tảng di động với cụm 7 nút cảm ứng (`Mobile Action Pad`) hoạt động song song với bàn phím/chuột mà không gây xung đột hay cướp phím khi đang gõ tin nhắn chat.

### Current State
Unity 6.6 sử dụng gói **Unity Input System** làm mặc định. Cần một lớp cầu nối sạch giữa `InputActionAsset`, giao diện cảm ứng `MobileInputBridge`, và callback `INetworkRunnerCallbacks.OnInput` của Photon Fusion 2.

### Constraints
- Cấu trúc dữ liệu mạng `NetworkInputData : INetworkInput` phải nhỏ gọn (`16 bytes`) và hoàn toàn không cấp phát bộ nhớ rác mỗi khung hình.
- Tệp `keybindings.json` bị hỏng hoặc sửa sai cú pháp bên ngoài không được làm văng game lúc khởi động.
- Khi khung nhập liệu Chat hoặc bảng Rebind đang mở, nhân vật tuyệt đối không được tung đòn hay di chuyển.

### Requirements
- Đáp ứng trọn vẹn `TR-input-001`, `TR-input-002`, `TR-input-003` và `TR-rebind-001`, `TR-rebind-002`, `TR-rebind-003`.

## Decision

1. **Lưu trữ Nguyên tử `keybindings.json` & Tự phục hồi (`KeybindingManager`)**:
   - Lưu sơ đồ `KeybindingSaveData` (`schemaVersion = 1`, danh sách 13 hành động, `mouseSensitivity`, `invertY`, `mobilePadVisibilityMode`) tại `Application.persistentDataPath/keybindings.json`.
   - Khi ghi file, luôn ghi ra `keybindings.json.tmp` trước rồi thay thế nguyên tử sang `keybindings.json`. Nếu khi đọc phát hiện lỗi JSON hoặc thiếu trường, tự động sao lưu thành `.corrupt.bak`, khôi phục bộ phím mặc định chuẩn và phát sự kiện `event Action OnBindingsChanged`.
2. **Đổi Phím Tương tác & Tự động Hoán đổi Xung đột (`KeybindingRebindModal`)**:
   - Sử dụng `PerformInteractiveRebinding()` với thời gian chờ `5.0s`, phím `Escape` để hủy, và danh sách từ chối các phím hệ thống (`F1–F12`, `Windows/Command`).
   - Nếu phím mới trùng với một hành động chiến đấu/di chuyển khác, tự động hoán đổi (`Swap-on-Conflict`) hai phím cho nhau và nháy viền vàng hổ phách (`#F59E0B`) trên cả hai dòng trong `1.2s`.
3. **Hợp nhất Tín hiệu Desktop + Mobile vào `NetworkInputData` (`16 bytes`)**:
   - `LocalInputPoller` đọc trạng thái từ `InputAction` (PC) và hợp nhất bằng phép toán `Bitwise OR (|)` với mặt nạ nút bấm từ `MobileInputBridge` (cụm 7 nút ảo trên màn hình) trước khi ghi vào `NetworkInputData` trong `OnInput()`.
4. **Cơ chế Cờ Khóa Điều khiển (`InputFocusManager`)**:
   - Sử dụng mặt nạ bit `[Flags] enum InputFocusFlag { None = 0, ChatFocused = 1 << 0, RebindModalOpen = 1 << 1, PauseOrSettingsOpen = 1 << 2, MatchEnded = 1 << 3 }`.
   - Khi `ActiveFlags != InputFocusFlag.None`, `LocalInputPoller` lập tức trả về `NetworkInputData` rỗng (`MoveDirection = Vector2.zero`, `Buttons = default`), bảo đảm nhân vật đứng yên an toàn.

### Architecture Diagram

```text
+-----------------------+       +------------------------+
| Unity InputActionMap  |       |   MobileInputBridge    |
| (Keyboard + Mouse)    |       | (7-Button Action Pad)  |
+-----------+-----------+       +-----------+------------+
            |                               |
            +---------------+---------------+
                            | (Bitwise OR Merge)
                            v
               +-------------------------+
               |   InputFocusManager     | <--- Chat / Rebind Modal Flags
               +------------+------------+
                            |
                            v
               +-------------------------+
               |    LocalInputPoller     | ---> NetworkInputData (16B)
               +-------------------------+      to Photon Fusion OnInput
```

### Key Interfaces

```csharp
namespace CapsuleArena.Core.Input
{
    [System.Flags]
    public enum InputFocusFlag : byte
    {
        None = 0,
        ChatFocused = 1 << 0,
        RebindModalOpen = 1 << 1,
        SettingsModalOpen = 1 << 2,
        MatchEnded = 1 << 3
    }

    public interface IKeybindingService
    {
        event System.Action OnBindingsChanged;
        string GetDisplayString(GameActionId action);
        void StartInteractiveRebind(GameActionId action, System.Action<RebindResult> onComplete);
        void ResetToDefaults();
    }
}
```

### Implementation Guidelines
- Gameplay controllers **must** read player commands exclusively from `GetInput(out NetworkInputData input)` inside `FixedUpdateNetwork()` and **must never** query `UnityEngine.Input` or `Keyboard.current` directly inside network simulation code.
- Any UI modal or chat input field that captures keyboard focus **must** set its corresponding `InputFocusFlag` on `InputFocusManager` upon activation and clear it in `OnDisable()` / `OnDeactivate()`.
- `KeybindingManager` **must** invoke `OnBindingsChanged` whenever a binding changes or resets so that HUD key badges update reactively without polling.

## Alternatives Considered

### Alternative 1: Legacy `UnityEngine.Input.GetKey` Polling with `PlayerPrefs`
- **Description**: Kiểm tra `Input.GetKey(KeyCode)` trực tiếp trong `Update()` và lưu từng phím vào `PlayerPrefs`.
- **Pros**: Đơn giản, ít lớp trừu tượng.
- **Cons**: `Legacy Input Manager` đã bị ngừng khuyến nghị trên Unity 6; khó hỗ trợ đổi phím chuột/bàn phím đồng nhất và không xuất được ra file `keybindings.json` tường minh theo yêu cầu.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Không đáp ứng yêu cầu kiến trúc Unity 6.6 và yêu cầu lưu file `keybindings.json` của người dùng.

### Alternative 2: Separate Network RPCs for Mobile Buttons vs PC Keys
- **Description**: Nút bấm cảm ứng trên màn hình gọi thẳng RPC tấn công thay vì đi qua `NetworkInputData`.
- **Pros**: Dễ gắn sự kiện `Button.onClick`.
- **Cons**: Phá vỡ hoàn toàn mô hình mô phỏng theo tick (`FixedUpdateNetwork`) của Photon Fusion, gây lệch thời gian hồi chiêu và tạo lỗ hổng spam kỹ năng.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Hợp nhất qua `MobileInputBridge` vào cùng một struct `NetworkInputData` đảm bảo PC và Mobile chạy chung 100% logic mô phỏng mạng.

## Consequences

### Positive
- Toàn bộ 13 hành động có thể tùy biến linh hoạt, tự động hoán đổi khi trùng phím và tự phục hồi nếu file JSON lỗi.
- Cụm 7 nút cảm ứng Mobile Action Pad và bàn phím PC hoạt động đồng bộ tuyệt đối trên cùng một đường ống `NetworkInputData` `16 bytes`.
- Ngăn chặn triệt để lỗi nhân vật tự tung chiêu khi người chơi đang gõ phím `1, 2, 3, Q, E, WASD` trong khung Chat.

### Negative
- Cần quản lý chặt chẽ vòng đời bật/tắt cờ trong `InputFocusManager` khi đóng mở các cửa sổ UI.

### Neutral
- Tệp `keybindings.json` được lưu cục bộ trên từng thiết bị của người chơi.

## Risks
- **Rủi ro kẹt cờ `InputFocusFlag` khi chuyển cảnh đột ngột**: Giải pháp giảm thiểu là gọi `InputFocusManager.ClearAllGameplayBlocks()` trong `OnSceneLoaded` và `OnDisable` của từng cửa sổ UI.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `keybinding-input-system.md` | `TR-input-001`: Persist 13 actions in `keybindings.json` with atomic `.tmp` write, corrupt recovery, and `OnBindingsChanged` event | Specifies `KeybindingManager` atomic persistence and fallback pipeline |
| `keybinding-input-system.md` | `TR-input-002`: Merge desktop `InputAction` with `MobileInputBridge` via bitwise OR into 16-byte `NetworkInputData` | Defines `LocalInputPoller` and `NetworkInputData` 16-byte layout |
| `keybinding-input-system.md` | `TR-input-003`: `InputFocusManager` suppresses gameplay input when Chat or Rebind UI is active | Introduces `InputFocusFlag` bitmask gating in `LocalInputPoller` |
| `keybinding-rebind-lobby-ui.md` | `TR-rebind-001`: `LobbyFlowController` orchestrates Login, Main Lobby, Room Waiting, and Settings Modal screens | Defines non-destructive CanvasGroup modal state transitions |
| `keybinding-rebind-lobby-ui.md` | `TR-rebind-002`: Interactive rebind with 5.0s timeout, Esc cancel, reserved key rejection, and swap-on-conflict | Specifies `PerformInteractiveRebinding` rules and conflict swap logic |
| `keybinding-rebind-lobby-ui.md` | `TR-rebind-003`: `MobileActionPadVisibilityMode` (`Auto`, `AlwaysOn`, `Off`) persists and toggles the 7-button pad | Stores visibility mode in `keybindings.json` and notifies HUD listeners |

## Performance Implications
- **CPU**: `< 0.05 ms/frame` cho việc đọc trạng thái phím và đóng gói `NetworkInputData`.
- **Memory**: `0 B` cấp phát mỗi khung hình khi thu thập input; chỉ cấp phát chuỗi JSON (`~2 KB`) khi người chơi bấm lưu cài đặt phím.
- **Load Time**: `< 5 ms` khi đọc `keybindings.json` lúc khởi động game.
- **Network**: `16 bytes/tick` (`960 bytes/s` ở `60Hz`) cho cấu trúc `NetworkInputData`.

## Migration Plan
Khởi tạo trực tiếp các lớp `KeybindingManager`, `InputFocusManager`, `MobileInputBridge`, `LocalInputPoller`, và `KeybindingRebindModal` trong tầng `Foundation` và `Presentation`.

**Rollback plan**: Nếu việc lưu file JSON gặp hạn chế quyền ghi trên nền tảng WebGL/di động đặc thù, chuyển đổi lớp lưu trữ bên dưới `IKeybindingStorageProvider` sang `PlayerPrefs` mà không ảnh hưởng tới logic bên trên.

## Validation Criteria
- EditMode Unit Test xác nhận ghi/đọc `keybindings.json`, tự động khôi phục khi truyền chuỗi JSON hỏng, và hoán đổi chính xác khi gán phím trùng.
- PlayMode Test xác nhận khi `InputFocusFlag.ChatFocused` bật, mọi nút bấm trong `NetworkInputData` đều bằng `0`.

## Related
- Enables: [`adr-0004-tick-accurate-mobility-and-damage-rpc.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0004-tick-accurate-mobility-and-damage-rpc.md), [`adr-0007-reactive-hud-mobile-pad-and-canvasgroup-chat.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0007-reactive-hud-mobile-pad-and-canvasgroup-chat.md)
- GDDs: [`keybinding-input-system.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/keybinding-input-system.md), [`keybinding-rebind-lobby-ui.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/keybinding-rebind-lobby-ui.md)
