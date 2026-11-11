# Accessibility Requirements: Capsule Arena: Last Man Standing

> **Engine**: Unity 6.6 (`6000.6.8f1`) | **Date**: 2026-11-10
> **Author**: `ux-designer` + `creative-director`

## 1. Visual & Colorblind Accessibility

1. **Colorblind-Safe Team & Status Palettes (`Art Bible §2`)**:
   - Thanh máu bản thân (`#22C55E` Emerald Green) và thanh máu kẻ địch (`#EF4444` Crimson Red) luôn đi kèm sự khác biệt về hình học và biểu tượng: thanh máu bản thân có viền sáng trắng `2px` kèm biểu tượng khiên bên trái, thanh máu kẻ địch có biểu tượng đầu lâu/kiếm chéo để người mù màu Đỏ-Lục (Deuteranopia / Protanopia) phân biệt tức thời mà không phụ thuộc vào màu sắc đơn thuần.
   - Ba loại đòn tấn công có cả màu sắc lẫn hình dáng vệt sáng hoàn toàn khác biệt:
     - **Chém dọc (`LMB`)**: Màu Vàng Hổ Phách (`#F59E0B`) + Vệt chém thẳng đứng từ trên xuống.
     - **Chém ngang Quét & Xoay `90°` (`Keys 1/3`)**: Màu Xanh Băng (`#38BDF8`) + Vệt lưỡi liềm quét ngang `90°`.
     - **Đạn tầm xa (`RMB`)**: Màu Vàng Chanh (`#FACC15`) + Quả cầu năng lượng bay thẳng kèm đuôi sao băng.
2. **Độ tương phản Chữ & HUD (`WCAG AA/AAA`)**:
   - Toàn bộ chữ trên HUD, bảng cài đặt phím và khung Chat sử dụng phông chữ không chân đậm nét (`Inter-Bold` / `Roboto-Bold` SDF) màu trắng `#F8FAFC` trên nền tối bán trong suốt `#0F172ACC` (tỷ lệ tương phản `> 11.5 : 1`, vượt chuẩn WCAG AAA).

## 2. Motor & Input Accessibility

1. **Tùy biến Phím bấm Toàn diện (`Full Keybinding Remapping`)**:
   - Toàn bộ 13 hành động trong game đều cho phép gán lại phím bất kỳ (Bàn phím hoặc Chuột 5 nút) và lưu bền vững vào `keybindings.json` (`ADR-0002`).
2. **Hỗ trợ Cụm 7 Nút Bấm Mềm Trên Màn Hình (`Mobile Action Pad`)**:
   - Cung cấp cụm 7 nút bấm kích thước lớn (`>= 84x84 px`, nút chính `110x110 px`) ở góc dưới phải màn hình cho phép kích hoạt mọi kỹ năng bằng thao tác chạm cảm ứng hoặc click chuột trực tiếp.
3. **Tùy chỉnh Độ nhạy Chuột & Giảm Chóng mặt Camera**:
   - Hỗ trợ thanh trượt `Mouse Sensitivity` (`0.1` đến `5.0`), tùy chọn `Invert Y-Axis`, và chuyển đổi tự do giữa góc nhìn `TPS` và `FPS` (phím `V`) bất cứ lúc nào để tránh say chuyển động (Motion Sickness).

## 3. Audio & Cognitive Accessibility

1. **Chỉ báo Sát thương Trực quan Đa kênh (`Visual + Audio Redundancy`)**:
   - Mọi âm thanh 3D quan trọng (tiếng chém kiếm, tiếng đạn bay, tiếng trúng đòn) đều đi kèm chỉ báo thị giác tương ứng (viền đỏ chỉ hướng sát thương `DirectionalHitVignette`, nháy trắng `HitFlash 0.08s`, và dòng thông báo `KillFeed`).
2. **Tách biệt 4 Kênh Âm lượng (`AudioMixer`)**:
   - Người chơi có thể điều chỉnh riêng biệt âm lượng `Master`, `BGM` (Nhạc nền), `SFX_3D` (Tiếng động chiến đấu), và `UI_2D` (Tiếng nút bấm/thông báo).
