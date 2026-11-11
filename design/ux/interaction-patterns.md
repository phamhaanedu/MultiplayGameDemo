# Interaction Patterns Library: Capsule Arena: Last Man Standing

> **Engine**: Unity 6.6 (`6000.6.8f1`, uGUI + Unity Input System) | **Date**: 2026-11-10
> **Author**: `ux-designer`

## 1. Dual-Input Parity Pattern (PC Keyboard/Mouse + Mobile Action Pad)

Mọi hành động chiến đấu và cơ động đều tuân theo mẫu tương tác song hành hai chiều:
- **Kích hoạt từ Bàn phím/Chuột PC**: Khi người chơi nhấn phím cứng (`LMB`, `RMB`, `1`, `2`, `3`, `Q`, `E`), nút ảo tương ứng trên cụm 7 nút `MobileActionPadView` ở góc dưới phải lập tức thực hiện hiệu ứng lún xuống (`DOTween Scale 0.90x` trong `0.06s`) và bắt đầu chạy vòng quạt hồi chiêu (`Image.fillAmount` từ `1.0` về `0.0`).
- **Kích hoạt từ Màn hình Cảm ứng / Chuột Bấm Nút Ảo**: Khi chạm vào nút trên `MobileActionPadView` (`IPointerDownHandler`), tín hiệu được ghi vào `MobileInputBridge` và hợp nhất bằng phép `Bitwise OR` vào cùng cấu trúc `NetworkInputData` (`16 bytes`) của Photon Fusion.
- **Nhãn Phím Động (`Live Keybind Badge`)**: Mỗi nút trong cụm 7 nút có một nhãn phụ ở góc trên trái hiển thị phím tắt hiện tại (`LMB`, `RMB`, `1`, `2`, `3`, `Q`, `E`). Ngay khi người chơi đổi phím trong `KeybindingRebindModal`, sự kiện `OnBindingsChanged` cập nhật nhãn này tức thời.

## 2. Input Focus & Modal Locking Pattern (`InputFocusManager`)

| Trạng thái Giao diện | Cờ `InputFocusFlag` | Hành vi Điều khiển Nhân vật | Hành vi Con trỏ Chuột (`Cursor`) | Phím Thoát Nhanh |
|----------------------|---------------------|-----------------------------|----------------------------------|------------------|
| Đang Chiến đấu (`Arena Active`) | `None (0)` | Hoạt động 100% (`WASD`, Chuột, 7 Kỹ năng) | `Locked` & `Invisible` (hoặc `Confined` khi bật Mobile Pad) | `Enter` (Mở Chat), `Esc` (Mở Cài đặt) |
| Đang Gõ Chat (`ChatFocused`) | `ChatFocused (1 << 0)` | Khóa hoàn toàn (Gửi `NetworkInputData` rỗng) | `Unlocked` & `Visible` | `Enter` (Gửi & Đóng), `Esc` (Hủy & Đóng) |
| Đang Mở Cài đặt (`SettingsModalOpen`) | `SettingsModalOpen (1 << 2)` | Khóa hoàn toàn | `Unlocked` & `Visible` | `Esc` (Đóng Cài đặt) |
| Đang Gán Phím (`RebindModalOpen`) | `RebindModalOpen (1 << 1)` | Khóa hoàn toàn (Đang lắng nghe phím mới) | `Unlocked` & `Visible` | `Esc` (Hủy gán phím hoặc tự hủy sau `5.0s`) |

## 3. Non-Destructive `CanvasGroup` Visibility Pattern

Các thành phần UI nhận sự kiện mạng liên tục (như `InGameChatController` và `ScreenSpaceCombatHUD`) luôn giữ `gameObject.activeSelf == true` và chuyển đổi trạng thái hiển thị thông qua `CanvasGroup`:
- **Active State**: `alpha = 1.0f`, `interactable = true`, `blocksRaycasts = true`.
- **Passive Read-Only State (Chat History)**: `alpha = 0.85f` (tự động giảm xuống `0.15f` sau `8.0s` không có tin nhắn mới), `interactable = false`, `blocksRaycasts = false`.
- **Hidden Modal State**: `alpha = 0.0f`, `interactable = false`, `blocksRaycasts = false`.

## 4. Micro-Interaction & Feedback Timing Standards

- **Button Hover**: Tăng độ sáng viền lên `#38BDF8` trong `0.08s`.
- **Button Press**: Thu nhỏ `0.92x` trong `0.06s` (`Ease.OutQuad`), nảy về `1.0x` trong `0.10s` (`Ease.OutBack`).
- **Cooldown Ready Flash**: Khi kỹ năng hồi chiêu xong (`Remaining == 0`), nháy sáng viền trắng `#F8FAFC` (`1.12x scale` trong `0.12s`).
- **Conflict Swap Highlight**: Khi gán trùng phím và tự động hoán đổi hai hành động, nháy nền vàng hổ phách `#F59E0B` trên cả hai dòng trong `1.20s`.
