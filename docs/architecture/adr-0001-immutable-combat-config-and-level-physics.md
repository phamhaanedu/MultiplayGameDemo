# ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `systems-designer`, `gameplay-programmer`, `level-designer`

## Summary

Quyết định này chuẩn hóa toàn bộ thông số gameplay của nhân vật và chiến đấu vào một tài sản dữ liệu bất biến lúc runtime (`CharacterCombatConfigSO`) với bộ nhớ đệm toán học tiền tính toán (`OnEnable`/`OnValidate`), đồng thời thiết lập ma trận tầng vật lý 4 lớp (`Layer 6 EnvironmentWall`, `Layer 8 PlayerCharacter`, `Layer 9 ArenaFloor`, `Layer 10 GhostSpectator`) và thuật toán chọn điểm hồi sinh cực đại hóa khoảng cách (`Max-Min Euclidean Spawn Selection`) cho đấu trường `40m x 40m`.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | Core / Physics |
| **Layer** | Foundation |
| **Knowledge Risk** | LOW |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/character-combat-config.md`, `design/gdd/arena-level-architecture.md` |
| **Post-Cutoff APIs Used** | None (Standard `ScriptableObject`, `Physics.IgnoreLayerCollision`, `Physics.Raycast`) |
| **Verification Required** | EditMode unit tests verifying `CharacterCombatConfigSO` property clamping, precomputed tick/cosine values, and `ArenaSpawnManager` distance-maximizing selection |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | ADR-0004, ADR-0005 |
| **Blocks** | EPIC-001 (Foundation Config & Arena Setup) |
| **Ordering Note** | Must be implemented first before any player locomotion, combat hitbox, or camera collision code is written. |

## Context

### Problem Statement
Trong một trò chơi hành động đấu trường nhiều người chơi chạy ở tần số `60Hz` (`FixedUpdateNetwork`), việc lưu trữ rải rác các hằng số tốc độ, sát thương, góc chém, thời gian hồi chiêu trên từng `MonoBehaviour` hoặc tính toán lại hàm lượng giác (`Mathf.Cos`) mỗi tick sẽ dẫn đến sai lệch thông số giữa các hệ thống, khó cân bằng game và gây lãng phí CPU. Bên cạnh đó, đấu trường `40m x 40m` cần một ma trận va chạm vật lý rõ ràng để ngăn đòn chém cận chiến xuyên tường, chặn tầm nhìn camera TPS nhưng vẫn cho phép hồn ma (`GhostSpectator`) bay xuyên chướng ngại vật.

### Current State
Dự án đang ở giai đoạn khởi tạo kiến trúc từ 14 tài liệu GDD đã được phê duyệt (`CD-GDD-ALIGN: APPROVE`). Cần một nền tảng dữ liệu và vật lý chuẩn mực ở tầng `Foundation`.

### Constraints
- Tuân thủ quy tắc đóng gói C#: toàn bộ biến chỉnh sửa trong Inspector phải là `[SerializeField] private` với tên `_camelCase` và暴露 qua thuộc tính `public` chỉ đọc (`PascalCase`).
- Cam kết `0 byte` cấp phát bộ nhớ rác (`Zero-GC Allocation`) khi đọc thông số trong `FixedUpdateNetwork()`.
- Đấu trường giới hạn `2–4` người chơi trong không gian `40m x 40m` với `8` điểm xuất phát trên đường tròn bán kính `16.0m`.

### Requirements
- Đáp ứng trọn vẹn `TR-config-001`, `TR-config-002`, `TR-config-003` và `TR-arena-001`, `TR-arena-002`, `TR-arena-003`.

## Decision

1. **Thiết kế `CharacterCombatConfigSO` Bất biến & Tiền tính toán (`Precomputed Math Cache`)**:
   - Tạo lớp `CharacterCombatConfigSO : ScriptableObject` đóng gói 5 nhóm thông số: Sinh tồn (`MaxHP = 100`), Di chuyển (`MoveSpeed = 6.0`, `JumpImpulse = 7.5`, `Gravity = -19.62`), Cơ động (`DashSpeed = 18.0`, `DashDuration = 0.20`, `DashCooldown = 2.50`, `DodgeSpeed = 12.0`, `DodgeDuration = 0.25`, `DodgeCooldown = 1.80`), Chiến đấu (`VerticalSlashDamage = 25`, `VerticalSlashRange = 2.4`, `VerticalSlashAngle = 50.0`, `HorizontalSweepDamage = 30`, `HorizontalSweepRange = 2.6`, `HorizontalSweepAngle = 90.0`, `HorizontalTurnAngle = 90.0`, `HorizontalTurnDuration = 0.18`, `RangedDamage = 15`, `RangedProjectileSpeed = 28.0`, `RangedMaxDistance = 28.0`), và Biểu diễn (`HitFlashDuration = 0.08`, `VisualStretchScale = (0.78, 1.28, 0.78)`, `VisualSquashScale = (1.30, 0.70, 1.15)`).
   - Trong `OnEnable()` và `OnValidate()`, tự động kẹp biên (`Mathf.Clamp`) và tính sẵn các thuộc tính dẫn xuất: `DashTicks = 12`, `DodgeTicks = 15`, `HorizontalTurnTicks = 11`, `ProjectileLifetimeTicks = 120`, `VerticalSlashCosHalfAngle = 0.9063078f` ($\cos 25^\circ$), và `HorizontalSweepCosHalfAngle = 0.7071068f` ($\cos 45^\circ$).
2. **Kiến trúc Vật lý Đấu trường & Ma trận Va chạm 4 Lớp**:
   - Định nghĩa cố định: `Layer 6 = EnvironmentWall`, `Layer 8 = PlayerCharacter`, `Layer 9 = ArenaFloor`, `Layer 10 = GhostSpectator`.
   - Cấu hình `LineOfSightMask = (1 << 6)` để chặn tia kiểm tra cận chiến, đạn tầm xa và cần camera TPS.
   - Cấu hình `Layer 10 (GhostSpectator)` chỉ va chạm với `Layer 9 (ArenaFloor)` và biên giới hạn ngoài, bỏ qua hoàn toàn `Layer 6` và `Layer 8`.
3. **Thuật toán Chọn Điểm Hồi Sinh `ArenaSpawnManager`**:
   - Duy trì mảng 8 điểm `SpawnPoint` trên vòng tròn bán kính `16.0m` hướng mặt vào tâm `(0, 0, 0)`.
   - Khi có người chơi đang sống trên sân, chọn điểm $S_k$ cực đại hóa khoảng cách Euclid ngắn nhất tới mọi người chơi đang sống: $S^* = \arg\max_{S_k} \min_{P_j \in \text{Alive}} \| \text{Pos}(S_k) - \text{Pos}(P_j) \|_2$. Nếu sân trống, dùng chỉ số vòng tròn `(PlayerId + MatchSeed) % 8`.

### Architecture Diagram

```text
+-------------------------------------------------------------------+
|               DefaultCapsuleCombatConfig.asset (SO)               |
|  - Private [SerializeField] backing fields (_maxHP, _moveSpeed..) |
|  - Public read-only properties + Precomputed Tick/Cosine Cache    |
+-------------------------------------------------------------------+
                 |                                 |
                 v                                 v
+--------------------------------+  +-------------------------------+
|  Player Prefab Controllers     |  |      ArenaSpawnManager        |
|  (Movement, Health, Combat)    |  |  - 8 Perimeter SpawnPoints    |
|  - Cache SO ref in Awake()     |  |  - Max-Min Distance Selector  |
+--------------------------------+  +-------------------------------+
```

### Key Interfaces

```csharp
namespace CapsuleArena.Core.Config
{
    public interface ICombatConfigProvider
    {
        CharacterCombatConfigSO Config { get; }
    }
}

namespace CapsuleArena.Arena
{
    public interface IArenaSpawnService
    {
        void GetOptimalSpawnPose(System.ReadOnlySpan<UnityEngine.Vector3> livingPlayerPositions, int fallbackIndex, out UnityEngine.Vector3 position, out UnityEngine.Quaternion rotation);
    }
}
```

### Implementation Guidelines
- Systems **must** cache their `CharacterCombatConfigSO` reference in `Awake()` or `Spawned()` and **must never** call `Resources.Load` or `ScriptableObject.CreateInstance` during `FixedUpdateNetwork()`.
- Runtime gameplay scripts **must never** mutate any field of `CharacterCombatConfigSO`; all runtime state (such as `CurrentHP` or active cooldown timers) **must** reside on `NetworkBehaviour` components.
- Melee and camera line-of-sight checks **must** use the precomputed `LineOfSightMask` (`1 << 6`) so cover walls reliably block attacks and prevent camera wall-clipping.

## Alternatives Considered

### Alternative 1: Hardcoded Constants or Public Inspector Fields on Prefabs
- **Description**: Đặt trực tiếp các biến `public float moveSpeed = 6f;` trên từng script `PlayerMovementController`, `PlayerCombatController`.
- **Pros**: Nhanh khi viết mẫu thử nhỏ.
- **Cons**: Vi phạm chuẩn đóng gói C#, phân mảnh dữ liệu trên nhiều Prefab/Script, dễ lệch thông số giữa HUD và logic chiến đấu.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Không đáp ứng yêu cầu kiến trúc hướng dữ liệu (Data-Driven SO) của người dùng và gây rủi ro đồng bộ thông số.

### Alternative 2: Cloud-Fetched JSON Config on Every Match Start
- **Description**: Tải toàn bộ thông số chiến đấu từ PlayFab TitleData mỗi khi vào trận.
- **Pros**: Cho phép chỉnh sửa thông số từ xa mà không cần build lại.
- **Cons**: Tăng độ trễ vào phòng, phát sinh cấp phát bộ nhớ khi parse JSON và phụ thuộc mạng khi khởi tạo vật lý.
- **Estimated Effort**: Trung bình.
- **Rejection Reason**: `ScriptableObject` tĩnh được đóng gói sẵn trong build đảm bảo tính tất định (`determinism`) tức thời cho mô phỏng `60Hz` của Photon Fusion.

## Consequences

### Positive
- Toàn bộ 14 hệ thống đọc chung một nguồn dữ liệu chuẩn duy nhất (`DefaultCapsuleCombatConfig.asset`) với chi phí CPU bằng 0 nhờ bộ đệm toán học tiền tính toán.
- Nhà thiết kế có thể tinh chỉnh thông số trực tiếp trong Unity Inspector kèm cơ chế tự động kiểm tra hợp lệ (`OnValidate`).
- Ma trận va chạm 4 lớp giải quyết triệt để bài toán xuyên tường và chế độ khán giả hồn ma.

### Negative
- Cần đảm bảo mọi Prefab nhân vật và HUD đều được gán đúng tham chiếu `DefaultCapsuleCombatConfig.asset` (được kiểm tra tự động qua EditMode test).

### Neutral
- Mọi thay đổi cân bằng thông số trong Editor sẽ cập nhật lại tệp `.asset` trong Git.

## Risks
- **Rủi ro quên gán tham chiếu `CharacterCombatConfigSO` trên Prefab**: Giải pháp giảm thiểu là kiểm tra `Debug.Assert(_config != null)` trong `Awake()` và viết EditMode test quét Prefab.
- **Rủi ro đặt nhầm Layer cho chướng ngại vật trong Scene**: Giải pháp giảm thiểu là kiểm tra tự động tất cả khối tường/cột trong `ArenaScene` phải thuộc `Layer 6 (EnvironmentWall)`.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `character-combat-config.md` | `TR-config-001`: Read-only C# properties backed by `[SerializeField] private` fields with `OnValidate()` clamping and zero GC | Defines `CharacterCombatConfigSO` encapsulation and validation rules |
| `character-combat-config.md` | `TR-config-002`: Precompute derived tick and cosine thresholds (`DashTicks=12`, `DodgeTicks=15`, `CosHalfAngle`) | Mandates `RecalculateDerivedValues()` in `OnEnable()` and `OnValidate()` |
| `character-combat-config.md` | `TR-config-003`: Canonical combat balance (`MaxHP=100`, `MoveSpeed=6.0`, `DashSpeed=18.0`, `VerticalDamage=25`, `HorizontalDamage=30`, `RangedDamage=15`) | Locks canonical baseline values in `DefaultCapsuleCombatConfig.asset` |
| `arena-level-architecture.md` | `TR-arena-001`: `40m x 40m` symmetric courtyard with perimeter wall, central altar, 4 L-walls, 8 pillars on Layers 6 & 9 | Standardizes arena geometry layout and physics layer assignment |
| `arena-level-architecture.md` | `TR-arena-002`: `ArenaSpawnManager` selects among 8 perimeter spawn points (`16m` radius) maximizing minimum Euclidean distance | Specifies deterministic Max-Min Euclidean spawn algorithm |
| `arena-level-architecture.md` | `TR-arena-003`: Physics Layer Collision Matrix and `LineOfSightMask (1 << 6)` block attacks/camera while allowing Layer 10 Ghost pass-through | Defines the 4-layer collision matrix (`Layers 6, 8, 9, 10`) |

## Performance Implications
- **CPU**: `< 0.01 ms/frame` — truy cập trường dữ liệu đã tính sẵn trên bộ nhớ và phép tính khoảng cách bình phương trên tối đa `8 x 4 = 32` cặp điểm khi spawn.
- **Memory**: `< 4 KB` cho tài sản `DefaultCapsuleCombatConfig.asset`; `0 B` cấp phát động lúc runtime.
- **Load Time**: Không đáng kể (`< 1 ms` khi nạp Scene).
- **Network**: `0 bps` — các máy trạm đọc trực tiếp từ `CharacterCombatConfigSO` đồng bộ sẵn trong bản dựng mà không cần truyền hằng số qua mạng.

## Migration Plan
Đây là quyết định nền tảng (`Foundation`) cho dự án mới; khởi tạo trực tiếp `CharacterCombatConfigSO.cs`, `DefaultCapsuleCombatConfig.asset` và `ArenaSpawnManager.cs`.

**Rollback plan**: Nếu cần tách nhỏ file cấu hình theo từng mô-đun, giữ nguyên interface `ICombatConfigProvider` để các lớp tiêu thụ không phải sửa code.

## Validation Criteria
- EditMode Unit Test xác nhận toàn bộ thuộc tính của `DefaultCapsuleCombatConfig.asset` khớp 100% bảng thông số chuẩn và các giá trị dẫn xuất (`DashTicks == 12`, `DodgeTicks == 15`, `VerticalSlashCosHalfAngle == 0.9063078f`, `HorizontalSweepCosHalfAngle == 0.7071068f`) chính xác tuyệt đối.
- EditMode Unit Test xác nhận `ArenaSpawnManager` luôn chọn điểm xa kẻ địch nhất trong 8 điểm xuất phát.

## Related
- Enables: [`adr-0004-tick-accurate-mobility-and-damage-rpc.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0004-tick-accurate-mobility-and-damage-rpc.md), [`adr-0005-multidirectional-combat-and-lag-compensation.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0005-multidirectional-combat-and-lag-compensation.md)
- GDDs: [`character-combat-config.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/character-combat-config.md), [`arena-level-architecture.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/arena-level-architecture.md)
