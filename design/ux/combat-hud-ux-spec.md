# UX Specification: Combat HUD, Overhead Billboard & 7-Button Mobile Action Pad

> **System**: `combat-hud-mobile-action-pad` & `in-game-chat-system`
> **Engine**: Unity 6.6 (`6000.6.8f1`, uGUI `1920x1080` `Match=0.5`) | **Date**: 2026-11-10
> **Status**: `APPROVED` (`UX-ACCESSIBILITY: PASS`, `CD-GDD-ALIGN: APPROVE`)

## 1. Screen Layout & Safe-Zone Topology (`1920x1080` Reference)

```text
+-----------------------------------------------------------------------------------+
| [Top-Left: Room Info & Ping]        [Top-Center: Phase Timer]   [Top-Right: Alive]|
|  Room: GAM302 | Ping: 32ms           PLAYING  02:14              ALIVE: 3 / 4     |
|                                       [Kill Feed Stack (Max 4)]                   |
|                                                                                   |
|                                                                                   |
|                                    (+) Crosshair                                  |
|                              (Dynamic Bloom + Hit X)                              |
|                                                                                   |
| [Bottom-Left: Persistent Chat]                      [Bottom-Right: 7-Btn Pad]     |
|  [All] Player1: Good luck!                                [1:SweepL]  [3:SweepR]  |
|  [Whisper->P2]: Watch left!                        [Q:RollL]  (LMB:Vert) [E:RollR]|
|  [Enter to chat... /w Name]                         [2:Dash]     [RMB:Ranged]     |
|  [Local HP Bar: 100 / 100  ================]                                      |
+-----------------------------------------------------------------------------------+
```

## 2. Component Hierarchy & Specifications

1. **Bottom-Left Local Status & Persistent Chat**:
   - **Local Health Bar**: `420 x 36 px`, neo góc dưới trái `(40, 32)`, thanh máu chính `#22C55E`, thanh trễ sát thương `#FACC15` (`0.25s` `DOTween`), chữ `100 / 100 HP` cỡ `22pt Bold`.
   - **Persistent Chat Box (`InGameChatController`)**: `460 x 240 px`, nằm ngay phía trên thanh máu `(40, 84)`, điều khiển hoàn toàn bằng `CanvasGroup` để luôn nhận RPC.
2. **Bottom-Right 7-Button Mobile Action Pad (`MobileActionPadView`)**:
   - Neo góc dưới phải `(-40, 36)`, gồm 7 nút kỹ năng có lớp phủ quạt hồi chiêu (`Radial 360° Fill`), bộ đếm giây (`0.1s`..`9.9s`) và huy hiệu phím tắt (`Keybind Badge`):
     - **Nút Tâm (`LMB` — Chém dọc)**: `110 x 110 px`, màu nhấn `#F59E0B`.
     - **Nút Quét Trái (`Key 1`) & Quét Phải (`Key 3`)**: `88 x 88 px`, màu nhấn `#38BDF8`, có mũi tên cong báo hiệu xoay `90°`.
     - **Nút Bắn Xa (`RMB`)**: `92 x 92 px`, màu nhấn `#FACC15`.
     - **Nút Lướt Tiến (`Key 2`)**: `88 x 88 px`, màu nhấn `#22C55E`.
     - **Nút Lăn Trái (`Key Q`) & Lăn Phải (`Key E`)**: `84 x 84 px`, màu nhấn `#A855F7`.
3. **World-Space Overhead Billboard (`WorldSpaceBillboardUI`)**:
   - Đặt ở độ cao `Y = +2.35m` trên đầu mỗi nhân vật, kích thước `1.60m x 0.32m`, luôn hướng mặt về `Camera.main` trong `LateUpdate()`.
   - Hiển thị `DisplayName` (đồng bộ từ PlayFab) và thanh máu kèm số thực (`75 / 100`).
