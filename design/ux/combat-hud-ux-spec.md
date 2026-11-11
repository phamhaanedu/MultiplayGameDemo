# UX Specification: Combat HUD, Overhead Billboard & 7-Button Mobile Action Pad

> **System**: `combat-hud-mobile-action-pad`, `spatial-audio-combat-vfx` & `in-game-chat-system`
> **Engine**: Unity 6.6 (`6000.6.8f1`, uGUI `1920x1080` `Match=0.5`) | **Last Updated**: 2026-11-11
> **Status**: `APPROVED` (`UX-ACCESSIBILITY: PASS`, `CD-GDD-ALIGN: APPROVE`)

## 1. Screen Layout & Safe-Zone Topology (`1920x1080` Reference)

```text
+-----------------------------------------------------------------------------------+
| [Top-Left: Room Info & Ping]        [Top-Center: Phase Timer]   [Top-Right: Alive]|
|  Room: GAM302 | Ping: 32ms           PLAYING  02:14              ALIVE: 3 / 4     |
|                                       [Kill Feed Stack (Max 4)]                   |
|                                                                                   |
|                                        30  (Floating Damage/Heal Text)            |
|                                    [Player2  70/100]                              |
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
     - **Nút Tâm (`LMB` — Chém dọc `25 dmg`, `0.6s` CD)**: `110 x 110 px`, màu nhấn `#F59E0B`.
     - **Nút Quét Trái (`Key 1`) & Quét Phải (`Key 3`) (`30 dmg`, `1.2s` CD chung)**: `88 x 88 px`, màu nhấn `#38BDF8`, có mũi tên cong báo hiệu hình quạt `90°` và thân người Capsule xoay đồng bộ 1:1 đúng `±90°` trong `0.18s`.
     - **Nút Bắn Xa (`RMB` — `15 dmg`, `0.4s` CD)**: `92 x 92 px`, màu nhấn `#FACC15`.
     - **Nút Lướt Tiến (`Key 2` — `3.0s` CD)**: `88 x 88 px`, màu nhấn `#22C55E`.
     - **Nút Lăn Trái (`Key Q`) & Lăn Phải (`Key E` — `1.5s` CD chung)**: `84 x 84 px`, màu nhấn `#A855F7`.
3. **World-Space Overhead Billboard (`WorldSpaceBillboardUI`)**:
   - Đặt ở độ cao `Y = +2.35m` trên đầu mỗi nhân vật, kích thước `1.60m x 0.32m`, luôn hướng mặt về `Camera.main` trong `LateUpdate()`.
   - Hiển thị `DisplayName` (đồng bộ từ Lobby / PlayFab) và thanh máu kèm số thực (`70 / 100`), tự động ẩn đối với chính `LocalPlayer` khi chuyển sang góc nhìn `FPS`.
4. **World-Space Floating Damage & Heal Text (`FloatingDamageTextManager` + `FloatingDamageTextConfigSO`)**:
   - **Kích hoạt cho mọi biến thiên máu**: Bất kỳ sát thương nhận vào hoặc lượng hồi máu nào trên mọi đấu sĩ đều sinh một chữ số nổi từ hồ chứa `PoolSize = 16` (`0` cấp phát rác runtime) tại độ cao `Y = +2.65m` ngay phía trên thanh máu đầu nhân vật, bay thẳng lên thêm `+1.25m` trong tổng thời gian `LifetimeSeconds = 0.85s` (lệch ngang luân phiên `±0.16m` để các đòn liên hoàn không đè lên nhau).
   - **Định dạng số & Màu sắc theo `ScriptableObject` (`DefaultFloatingDamageConfig.asset`)**: Hiển thị **số nguyên dương thuần túy không kèm dấu `-` hay `+`** (ví dụ: `25`, `30`, `15`). Khi **mất máu** hiển thị màu **Đỏ (`#FF3B3B`, `DamageTextColor`)**; khi **tăng/hồi máu** (ví dụ ăn bình máu) hiển thị màu **Xanh Lá (`#22C55E`, `HealTextColor`)**.
   - **Đường cong Tỷ lệ 2 Pha (`2/3` Phóng to $\rightarrow$ `1/3` Thu nhỏ + Mờ dần)**:
     - **`2/3` thời gian đầu (`t ∈ [0.0, 0.6667]`)**: Vừa bay lên vừa **phóng to mượt** từ `StartScale = 0.85x` lên `PeakScale = 1.50x` (độ rõ giảm nhẹ `1.0 -> 0.85`).
     - **`1/3` thời gian cuối (`t ∈ [0.6667, 1.0]`)**: Tiếp tục bay lên đồng thời **thu nhỏ** từ `1.50x` về `EndScale = 0.55x` **cùng lúc mờ dần (`alpha: 0.85 -> 0.0`)** rồi thu hồi về Pool.
   - **Quy tắc Hiển thị theo Góc nhìn (`TPS` vs `FPS`)**: Hiển thị đầy đủ trên đầu mọi đối thủ và **trên cả bản thân người chơi** khi ở góc nhìn `TPS` hoặc `Ghost Spectator`; **chỉ ẩn đối với chỉ số máu tăng/giảm của chính bản thân khi người chơi đang ở góc nhìn thứ nhất (`FPS`)** để không che tâm ngắm (vẫn thấy đầy đủ số máu bay lên trên đầu các đối thủ khác ở `FPS`).
