# ADR-0005: Multi-Directional Melee (90° Snap-Turn), Ranged Combat & Dual FPS/TPS/Ghost Camera

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `gameplay-programmer`, `systems-designer`

## Summary

Quyết định này định nghĩa thuật toán chiến đấu đa hướng không cấp phát bộ nhớ (`Zero-Alloc Physics.OverlapSphereNonAlloc` kết hợp tích vô hướng phẳng và kiểm tra khuất tầm nhìn `LineOfSightMask`) cho đòn Chém dọc (`LMB`, góc quạt `50°`, `25 dmg`), Chém ngang Quét & Xoay `90°` (`Keys 1/3`, góc quạt `90°`, `30 dmg`, tự động xoay thân nhân vật và tâm ngắm camera đúng `±90°` trong `0.18s` / `11 ticks`), Bắn đạn tầm xa (`RMB`, `15 dmg`), cùng hệ thống Camera Kép (`FPS/TPS` chuyển đổi qua phím `V`) và Khán giả Hồn ma (`Ghost Spectator`).

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | Physics / Rendering / Networking |
| **Layer** | Feature |
| **Knowledge Risk** | HIGH |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/combat-melee-ranged-system.md`, `design/gdd/camera-fps-tps-spectator.md` |
| **Post-Cutoff APIs Used** | Photon Fusion 2 (`FixedUpdateNetwork`, `Render`, `TickTimer`), Unity URP `ShadowCastingMode.ShadowsOnly` |
| **Verification Required** | EditMode & PlayMode tests verifying `50°` vertical sector, `90°` Asymmetric Horizontal Sweep sector (`[0°, 90°]` left/right), `±90°` body + camera yaw rotation over `11 ticks`, and TPS camera wall collision |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001, ADR-0003, ADR-0004 |
| **Enables** | ADR-0007, ADR-0008 |
| **Blocks** | EPIC-006 (Multi-Directional Melee & Ranged Combat), EPIC-007 (Dual FPS/TPS & Ghost Spectator Camera) |
| **Ordering Note** | Requires `ADR-0001` (combat math cache), `ADR-0003` (projectile pool), and `ADR-0004` (movement lock & damage RPC). |

## Context

### Problem Statement
Người dùng yêu cầu nâng cấp hệ thống cận chiến thành 2 cơ chế rõ rệt:
1. **Chém dọc (`LMB`)**: Hình quạt đối xứng `50°` thẳng phía trước mặt (`2.4m`, `25 dmg`).
2. **Chém ngang Trái/Phải (`Keys 1, 3`)**: Hình quạt lệch `90°` quét từ chính diện sang hẳn bên trái (`Key 1`, $s = -1$) hoặc sang hẳn bên phải (`Key 3`, $s = +1$) với bán kính `2.6m`, `30 dmg`, và **sau khi chém xong thì cả thân nhân vật lẫn góc nhìn Camera đều xoay đúng `90°` theo hướng tương tự**.
Điều này đòi hỏi sự phối hợp chặt chẽ giữa bộ điều khiển chiến đấu (`PlayerCombatController`), bộ điều khiển góc xoay nhân vật trong `FixedUpdateNetwork()`, và bộ điều khiển góc nhìn (`DualPerspectiveCameraController`) để khi kết thúc `0.18s` (`11 ticks`), camera không bị giật ngược về góc cũ.

### Current State
Đã có sẵn các hằng số tiền tính toán `VerticalSlashCosHalfAngle = 0.9063078f` ($\cos 25^\circ$), `HorizontalSweepCosHalfAngle = 0.7071068f` ($\cos 45^\circ$), và `HorizontalTurnTicks = 11` trong `CharacterCombatConfigSO` (`ADR-0001`).

### Constraints
- Tuyệt đối không dùng `Physics.OverlapSphere` (tạo rác bộ nhớ); bắt buộc dùng `Physics.OverlapSphereNonAlloc` với mảng tĩnh `Collider[16]`.
- Mỗi đòn chém chỉ được gây sát thương tối đa `1 lần` lên mỗi mục tiêu trong suốt thời gian vung kiếm (`HashSet<int>` hoặc mặt nạ bit theo `PlayerRef`).
- Trong `0.18s` (`11 ticks`) đang xoay thân `90°` của đòn Chém ngang, khóa tạm thời đầu vào xoay chuột ngang (`Mouse X`) để quỹ đạo xoay mượt mà và chính xác tuyệt đối `±90°`.

### Requirements
- Đáp ứng trọn vẹn `TR-combat-001`, `TR-combat-002`, `TR-combat-003` và `TR-camera-001`, `TR-camera-002`, `TR-camera-003`.

## Decision

1. **Thuật toán Kiểm tra Hình quạt Không Cấp phát (`Zero-Alloc Sector Hit Detection`)**:
   - Dùng `Physics.OverlapSphereNonAlloc(origin, range, _hitBuffer, LayerMask.GetMask("PlayerCharacter"))`.
   - Với mỗi mục tiêu khác bản thân và đang sống (`IsAlive == true`), tính vector hướng phẳng $\hat{D}_{xz} = \text{Normalize}(P_{target,xz} - P_{self,xz})$:
     - **Chém dọc (`LMB`)**: Trục tâm quạt $\hat{A}_{vert} = \hat{F}_0$ (hướng nhìn thẳng trước mặt). Trúng đòn khi $\hat{D}_{xz} \cdot \hat{F}_0 \ge 0.9063078$ ($\cos 25^\circ$) và không bị tường `Layer 6` chắn (`!Physics.Raycast(eyePos, targetChest, dist, LineOfSightMask)`).
     - **Chém ngang Trái/Phải (`Key 1`: $s=-1$, `Key 3`: $s=+1$)**: Vì quạt quét một góc `90°` từ thẳng trước mặt ($\hat{F}_0$) sang ngang ($\hat{S}_s = \text{Normalize}(\hat{F}_0 \cos(s \cdot 90^\circ) + \hat{R}_0 \sin(s \cdot 90^\circ))$), trục phân giác của hình quạt lệch đúng `45°` về phía quét: $\hat{A}_{horiz}(s) = \text{Normalize}(\hat{F}_0 + s \cdot \hat{R}_0)$. Trúng đòn khi $\hat{D}_{xz} \cdot \hat{A}_{horiz}(s) \ge 0.7071068$ ($\cos 45^\circ$) và không bị tường `Layer 6` chắn.
2. **Đồng bộ Xoay Thân & Tâm Ngắm `90°` (`Synchronized 90° Snap-Turn`)**:
   - Khi kích hoạt Chém ngang ($s \in \{-1, +1\}$), ghi nhận `[Networked] float SweepStartYaw = CurrentYaw` và `[Networked] float SweepTargetYaw = Mathf.Repeat(SweepStartYaw + s * 90f, 360f)` trong `11 ticks` (`0.18s`).
   - Tại mỗi tick $k \in [1..11]$, nội suy mượt theo hàm bậc ba `SmoothStep`: $\text{Yaw}(k) = \text{LerpAngle}(\text{SweepStartYaw}, \text{SweepTargetYaw}, u^2(3 - 2u))$ với $u = k / 11$, đồng thời cập nhật biến `CameraYaw` của `DualPerspectiveCameraController` theo đúng góc thân nhân vật.
3. **Bắn Đạn Tầm Xa (`Ranged Fire` — `RMB`)**:
   - Lấy một `NetworkProjectile` từ `FusionObjectPoolProvider` tại vị trí `MuzzleOffset = (0f, 1.1f, 0.75f)`, bay với vận tốc `28.0 m/s` trong tối đa `120 ticks` (`28.0m`), kiểm tra va chạm liên tục bằng `Physics.SphereCastNonAlloc` (`bán kính 0.20m`) để không xuyên qua mục tiêu mỏng.
4. **Hệ thống Camera Kép (`FPS/TPS`) & Khán giả Hồn ma (`Ghost Spectator`)**:
   - Phím `V` chuyển đổi giữa `TPS` (`Boom = 4.5m`, `ShoulderOffset = 0.65m`, `FOV = 65°`, chống xuyên tường bằng `Physics.SphereCast` bán kính `0.22m` trên `Layer 6`) và `FPS` (`EyeHeight = 1.55m`, `FOV = 80°`, chuyển `MeshRenderer` đầu/kính của nhân vật cục bộ sang `ShadowCastingMode.ShadowsOnly`).
   - Khi `IsAlive == false`, tự động chuyển sang `GhostSpectatorController`: cho phép bay tự do ở tốc độ `9.0 m/s` (hoặc bấm `Tab` để bám theo góc nhìn TPS của những người chơi còn sống) và áp dụng vật liệu `M_Capsule_Ghost_Translucent` (`#38BDF8`, Alpha `0.35`).

### Architecture Diagram

```text
+-----------------------------------------------------------------------+
|                        PlayerCombatController                         |
|  - LMB: Vertical Slash (50 deg, 2.4m, 25 dmg, 0.55s CD)               |
|  - Key 1/3: Horizontal Sweep (90 deg, 2.6m, 30 dmg, 1.20s Shared CD)  |
|             + 90 deg Snap-Turn over 11 ticks (0.18s)                  |
|  - RMB: Ranged Projectile (28 m/s, 15 dmg, 0.75s CD via Object Pool)  |
+-------------------+-----------------------------------+---------------+
                    |                                   |
                    v (Drives CameraYaw during Sweep)   v (Spawns / Hits)
+-----------------------------------+   +-------------------------------+
|  DualPerspectiveCameraController  |   |     PlayerHealthController    |
|  - Key V: FPS (80 FOV) / TPS (65) |   |  - Rpc_RequestTakeDamage()    |
|  - SphereCast Wall Pull-In (L6)   |   +-------------------------------+
|  - Death -> GhostSpectator (9m/s) |
+-----------------------------------+
```

### Key Interfaces

```csharp
namespace CapsuleArena.Player.Combat
{
    public enum CombatAttackType : byte
    {
        VerticalSlash = 1,
        HorizontalSweepLeft = 2,
        HorizontalSweepRight = 3,
        RangedProjectile = 4
    }

    public interface ICombatStateReadOnly
    {
        bool IsPerformingHorizontalTurn { get; }
        float CurrentAuthoritativeYaw { get; }
        float GetVerticalCooldownRemaining(Fusion.NetworkRunner runner);
        float GetHorizontalCooldownRemaining(Fusion.NetworkRunner runner);
        float GetRangedCooldownRemaining(Fusion.NetworkRunner runner);
    }
}
```

### Implementation Guidelines
- Melee sector checks **must** use `Physics.OverlapSphereNonAlloc` with a pre-allocated buffer and compare planar dot products against `Config.VerticalSlashCosHalfAngle` / `Config.HorizontalSweepCosHalfAngle`; they **must never** call `Mathf.Acos` or `Vector3.Angle` inside the per-collider loop.
- During the `11-tick` (`0.18s`) Horizontal Sweep turn window, `DualPerspectiveCameraController` **must** lock horizontal mouse delta and synchronize its yaw directly to `CurrentAuthoritativeYaw` so the camera finishes rotated cleanly by `±90°`.
- Every melee and projectile hit **must** verify unobstructed line-of-sight against `Config.LineOfSightMask` (`Layer 6 EnvironmentWall`) before invoking `Rpc_RequestTakeDamage`.

## Alternatives Considered

### Alternative 1: MeshCollider Trigger Volumes Attached to Animated Weapon Bones
- **Description**: Gắn một `BoxCollider (isTrigger = true)` vào thanh kiếm và bật/tắt theo khung hình hoạt ảnh (`Animation Event`).
- **Pros**: Trực quan khi nhìn trong Scene View.
- **Cons**: Phụ thuộc vào tốc độ khung hình hiển thị (`Update`), dễ bỏ sót va chạm khi nhân vật xoay nhanh `90°` trong `0.18s`, và không đồng bộ chính xác với tick mạng `60Hz` của Photon Fusion.
- **Estimated Effort**: Trung bình.
- **Rejection Reason**: Truy vấn hình học toán học (`OverlapSphereNonAlloc` + `Dot Product` + `Raycast`) trong `FixedUpdateNetwork()` chính xác tuyệt đối 100%, không phụ thuộc tốc độ khung hình và tốn ít CPU hơn nhiều.

### Alternative 2: Rotate Only Visual Model During Horizontal Sweep Without Rotating Camera Yaw
- **Description**: Chỉ xoay mô hình 3D `90°` còn camera giữ nguyên hướng cũ, sau đó xoay trả mô hình về hướng camera.
- **Pros**: Không cần đồng bộ biến `CameraYaw`.
- **Cons**: Trái ngược với yêu cầu thiết kế của người dùng ("sau khi chém xong thì người cũng xoay 90 theo hướng tương tự") và gây lệch hướng tiến khi người chơi bấm `W` ngay sau nhát chém.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Đồng bộ cả thân nhân vật lẫn `CameraYaw` xoay `±90°` tạo nên cơ chế chiến thuật đặc trưng ("Snap-Turn Cleave") đúng như ý đồ thiết kế.

## Consequences

### Positive
- Ba đòn tấn công (`LMB` dọc, `Keys 1/3` ngang xoay `90°`, `RMB` bắn xa) mang lại chiều sâu kỹ năng rõ rệt và cảm giác chiến đấu tốc độ cao.
- Góc nhìn `FPS/TPS` chuyển đổi mượt mà không xuyên tường, kết hợp chế độ Khán giả Hồn ma giữ chân người chơi đã bị loại đến hết ván.

### Negative
- Người chơi mới cần vài phút làm quen với việc tâm ngắm xoay `90°` sau khi bấm phím `1` hoặc `3` (được hỗ trợ trực quan bằng vệt kiếm xanh băng `#38BDF8` chỉ hướng quét).

### Neutral
- Cả hai phím `1` và `3` dùng chung một bộ đếm `HorizontalSweepCooldownTimer` (`1.20s`) để tránh spam xoay vòng liên tục.

## Risks
- **Rủi ro chóng mặt khi xoay camera `90°` ở góc nhìn FPS**: Giải pháp giảm thiểu là sử dụng đường cong `SmoothStep` trong `0.18s` (`11 ticks`) thay vì nhảy góc tức thời trong 1 khung hình.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `combat-melee-ranged-system.md` | `TR-combat-001`: Vertical Slash (`LMB`, `25 dmg`, `50°`, `2.4m`) & Horizontal Sweep (`Keys 1/3`, `30 dmg`, `90°`, `2.6m`) via `OverlapSphereNonAlloc` + LOS | Defines zero-alloc sector math and `LineOfSightMask` validation |
| `combat-melee-ranged-system.md` | `TR-combat-002`: Horizontal Sweep Left/Right applies synchronized `90°` snap-turn over `0.18s` (`11 ticks`) to body and camera | Specifies `SmoothStep` yaw interpolation and camera yaw coupling |
| `combat-melee-ranged-system.md` | `TR-combat-003`: Ranged Fire (`RMB`, `15 dmg`, `0.75s` CD) spawns pooled `NetworkProjectile` (`28.0 m/s`, `120 ticks`) | Integrates `FusionObjectPoolProvider` with `SphereCastNonAlloc` projectile simulation |
| `camera-fps-tps-spectator.md` | `TR-camera-001`: Toggle TPS (`4.5m` boom, `FOV 65`) and FPS (`EyeHeight 1.55m`, `FOV 80`, local head hidden) on Key `V` | Defines `DualPerspectiveCameraController` blend and `ShadowsOnly` head masking |
| `camera-fps-tps-spectator.md` | `TR-camera-002`: TPS camera boom prevents wall clipping via `Physics.SphereCast` (`0.22m` radius) against `Layer 6` | Specifies spring-arm collision pull-in/push-out math |
| `camera-fps-tps-spectator.md` | `TR-camera-003`: Local death activates `GhostSpectatorController` (`9.0 m/s` free-roam, `#38BDF8` ghost shader, `Tab` follow) | Defines automatic post-death spectator transition |

## Performance Implications
- **CPU**: `< 0.10 ms` cho mỗi lần kiểm tra hình quạt cận chiến (`OverlapSphereNonAlloc` + tối đa 4 phép nhân vô hướng); `< 0.05 ms/frame` cho `SphereCast` camera.
- **Memory**: `0 B` cấp phát động nhờ bộ đệm tĩnh `Collider[16]` và `RaycastHit[8]`.
- **Load Time**: Không ảnh hưởng.
- **Network**: Đồng bộ góc `CurrentAuthoritativeYaw` và các bộ đếm `TickTimer` hồi chiêu (`12 bytes`).

## Migration Plan
Triển khai `PlayerCombatController.cs`, `NetworkProjectile.cs`, `DualPerspectiveCameraController.cs`, và `GhostSpectatorController.cs` trong tầng `Feature`.

**Rollback plan**: Nếu thời gian xoay `0.18s` cần điều chỉnh để êm hơn, thay đổi trực tiếp `HorizontalTurnDuration` trong `DefaultCapsuleCombatConfig.asset` mà không cần sửa thuật toán.

## Validation Criteria
- EditMode Unit Test kiểm tra hàm toán học hình quạt xác nhận mục tiêu ở góc `+20°` trúng cả Chém dọc và Chém ngang phải, mục tiêu ở góc `+75°` chỉ trúng Chém ngang phải, và mục tiêu bị chắn bởi tường `Layer 6` nhận `0` sát thương.
- PlayMode Test xác nhận sau khi kích hoạt `HorizontalSweepRight` (`Key 3`) và chạy đủ `11 ticks`, góc `transform.eulerAngles.y` của nhân vật và `CameraYaw` tăng đúng `+90.0° ± 0.1°`.

## Related
- Depends on: [`adr-0001-immutable-combat-config-and-level-physics.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0001-immutable-combat-config-and-level-physics.md), [`adr-0003-photon-fusion-shared-mode-and-object-pooling.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0003-photon-fusion-shared-mode-and-object-pooling.md), [`adr-0004-tick-accurate-mobility-and-damage-rpc.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0004-tick-accurate-mobility-and-damage-rpc.md)
- Enables: [`adr-0007-reactive-hud-mobile-pad-and-canvasgroup-chat.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0007-reactive-hud-mobile-pad-and-canvasgroup-chat.md), [`adr-0008-playfab-cloud-leaderboard-and-spatial-vfx-pool.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0008-playfab-cloud-leaderboard-and-spatial-vfx-pool.md)
- GDDs: [`combat-melee-ranged-system.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/combat-melee-ranged-system.md), [`camera-fps-tps-spectator.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/camera-fps-tps-spectator.md)
