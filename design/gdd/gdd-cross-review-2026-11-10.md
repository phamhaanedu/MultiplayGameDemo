# Cross-GDD Review Report: Capsule Arena: Last Man Standing

> **Date**: 2026-11-10
> **Mode**: /review-all-gdds (Holistic Cross-GDD Consistency + Design Theory Review)
> **Workflow**: full
> **GDDs Reviewed**: 14 / 14 (`character-combat-config.md` through `spatial-audio-combat-vfx.md`)
> **Verdict**: **PASS (0 Blocking Contradictions, 100% Entity Registry Alignment)**

---

## 1. Cross-Document Consistency Audit

Toàn bộ 14 tài liệu GDD và `design/registry/entities.yaml` (`37` thực thể, công thức và hằng số) đã được đối chiếu chéo:

| Hạng mục Kiểm tra | Giá trị Chuẩn hóa Xuyên suốt 14 GDD | Các Tài liệu Tham chiếu | Trạng thái |
|-------------------|-------------------------------------|-------------------------|------------|
| Máu tối đa & Khởi đầu | `MaxHP = 100`, `StartingHP = 100` | `#1 Config`, `#6 Health`, `#8 HUD` | ✅ Đồng nhất 100% |
| Tốc độ Di chuyển & Nhảy | `MoveSpeed = 6.0 m/s`, `JumpImpulse = 7.5 m/s`, `Gravity = -19.62 m/s²` | `#1 Config`, `#5 Mobility`, `#9 Camera` | ✅ Đồng nhất 100% |
| Lướt Tiến (`Forward Dash` — `Key 2`) | `DashSpeed = 18.0 m/s`, `DashDuration = 0.20s` (`12 ticks`, `3.6m`), `DashCooldown = 2.50s` | `#1 Config`, `#3 Input`, `#5 Mobility`, `#8 HUD`, `#14 Audio/VFX` | ✅ Đồng nhất 100% |
| Lăn Né (`Dodge Roll` — `Keys Q/E`) | `DodgeSpeed = 12.0 m/s`, `DodgeDuration = 0.25s` (`15 ticks`, `3.0m`), `DodgeCooldown = 1.80s` (chung) | `#1 Config`, `#3 Input`, `#5 Mobility`, `#8 HUD`, `#14 Audio/VFX` | ✅ Đồng nhất 100% |
| Chém Dọc (`Vertical Slash` — `LMB`) | `Damage = 25`, `Range = 2.4m`, `Angle = 50.0°` ($\cos 25^\circ = 0.9063078$), `CD = 0.55s` | `#1 Config`, `#6 Health`, `#7 Combat`, `#8 HUD`, `#14 Audio/VFX` | ✅ Đồng nhất 100% |
| Chém Ngang & Xoay `90°` (`Keys 1/3`) | `Damage = 30`, `Range = 2.6m`, `Angle = 90.0°` ($\cos 45^\circ = 0.7071068$), `Turn = ±90°` trong `0.18s` (`11 ticks`), `CD = 1.20s` (chung) | `#1 Config`, `#5 Mobility`, `#7 Combat`, `#8 HUD`, `#9 Camera`, `#14 Audio/VFX` | ✅ Đồng nhất 100% |
| Bắn Xa (`Ranged Fire` — `RMB`) | `Damage = 15`, `Speed = 28.0 m/s`, `MaxDist = 28.0m` (`120 ticks`), `Radius = 0.20m`, `CD = 0.75s`, Pool `32` | `#1 Config`, `#4 Session/Pool`, `#7 Combat`, `#8 HUD`, `#14 Audio/VFX` | ✅ Đồng nhất 100% |
| Ma trận Vật lý 4 Lớp | `Layer 6 EnvironmentWall`, `Layer 8 PlayerCharacter`, `Layer 9 ArenaFloor`, `Layer 10 GhostSpectator` | `#1 Config`, `#2 Arena`, `#5 Mobility`, `#6 Health`, `#7 Combat`, `#9 Camera` | ✅ Đồng nhất 100% |

---

## 2. Design Theory & Balance Analysis (MDA Framework)

1. **Cân bằng Tam giác Tấn công (Combat Triangle & TTK)**:
   - **Chém dọc (`LMB`)**: `25 dmg` / `0.55s` ($\text{DPS lý thuyết} = 45.45$, $\text{TTK} = 4\text{ đòn} = 1.65\text{s}$), hình quạt hẹp `50°` đòi hỏi ngắm chuẩn trực diện.
   - **Chém ngang Quét & Xoay `90°` (`Keys 1/3`)**: `30 dmg` / `1.20s` ($\text{TTK kết hợp} = 2\text{ Chém ngang} + 2\text{ Chém dọc} = 110\text{ dmg}$), hình quạt rộng `90°` tầm xa `2.6m` khắc chế đối phương đang Lăn né (`Q/E`) sang bên hông và lập tức xoay mặt `90°` đón đầu.
   - **Bắn đạn tầm xa (`RMB`)**: `15 dmg` / `0.75s` ($\text{TTK thuần} = 7\text{ viên} = 4.50\text{s}$), dùng để cấu rỉa máu từ xa hoặc kết liễu đối thủ bỏ chạy sau vật chắn.
   - Không có chiến thuật áp đảo tuyệt đối (No Dominant Strategy): Bắn xa bị khắc chế bởi các cột chắn `Layer 6` và `Forward Dash (Key 2)`; giữ khoảng cách gần bị khắc chế bởi `Horizontal Sweep (Keys 1/3)`.
2. **Tải Nhận thức & Trải nghiệm Đa nền tảng (Cognitive Load & Mobile Readiness)**:
   - Bố cục 7 nút cảm ứng `MobileActionPadView` ở góc dưới phải ánh xạ 1-1 với cụm phím PC (`LMB, RMB, 1, 2, 3, Q, E`), giúp người chơi nắm bắt toàn bộ trạng thái hồi chiêu trong một vùng nhìn duy nhất.

---

## 3. Verdict & Sign-Off

- **Consistency Verdict**: `PASS` (`0` conflicts across 14 GDDs and `entities.yaml`)
- **Design Theory Verdict**: `PASS` (`CD-GDD-ALIGN: APPROVE` across all 14 systems)
