# ADR-0004: Tick-Accurate Mobility (Dash/Dodge) & Victim-Authoritative Health RPC

## Status

Accepted

## Date

2026-11-10

## Last Verified

2026-11-10

## Decision Makers

`technical-director`, `gameplay-programmer`, `systems-designer`

## Summary

Quyết định này định nghĩa máy trạng thái di chuyển theo tick (`60Hz` `FixedUpdateNetwork`) cho nhân vật bao gồm Đi bộ (`6.0 m/s`), Nhảy (`7.5 m/s`), Lướt tiến (`Key 2` ở `18.0 m/s` trong `12 ticks` / `3.6m`), và Lăn né ngang (`Keys Q/E` ở `12.0 m/s` trong `15 ticks` / `3.0m`), đồng thời thiết lập giao thức máu và sát thương ủy quyền tại nạn nhân (`Victim-Authoritative Health` thông qua `Rpc_RequestTakeDamage`) nhằm triệt tiêu hoàn toàn xung đột ghi đè máu khi nhiều người cùng tấn công một mục tiêu trong Shared Mode.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.6 (6000.6.8f1) |
| **Domain** | Physics / Networking |
| **Layer** | Core |
| **Knowledge Risk** | HIGH |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `design/gdd/character-movement-mobility.md`, `design/gdd/health-damage-rpc-system.md` |
| **Post-Cutoff APIs Used** | Photon Fusion 2 (`[Networked]`, `OnChangedRender`, `TickTimer`, `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]`) |
| **Verification Required** | EditMode & PlayMode tests verifying Dash (`3.6m`) and Dodge (`3.0m`) displacement, cooldown timers, concurrent multi-attacker RPC damage deduction, and death layer transition |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001, ADR-0002, ADR-0003 |
| **Enables** | ADR-0005, ADR-0006, ADR-0007 |
| **Blocks** | EPIC-004 (Character Locomotion & Mobility), EPIC-005 (Health, Damage RPC & Death State) |
| **Ordering Note** | Requires `ADR-0001` (`CharacterCombatConfigSO`), `ADR-0002` (`NetworkInputData`), and `ADR-0003` (`FusionSessionManager`). |

## Context

### Problem Statement
Trong Photon Fusion 2 Shared Mode, mỗi người chơi nắm quyền `StateAuthority` trên đối tượng nhân vật của chính mình. Nếu người tấn công A và người tấn công B cùng cố gắng trừ trực tiếp biến `[Networked] CurrentHP` của nạn nhân C từ máy của A và B, hệ thống mạng sẽ từ chối hoặc gây ghi đè sai lệch (Race Condition). Bên cạnh đó, kỹ năng Lướt tiến (`Key 2`) và Lăn né (`Keys Q/E`) đòi hỏi quãng đường dịch chuyển chính xác theo từng tick (`3.6m` và `3.0m`), trượt mượt dọc theo tường khi va chạm chéo, và không làm méo hộp va chạm vật lý khi thực hiện hiệu ứng nghiêng người/Squash & Stretch.

### Current State
Các thông số cơ động và máu đã được chuẩn hóa trong `CharacterCombatConfigSO` (`ADR-0001`) và tín hiệu phím đã được đóng gói trong `NetworkInputData` (`ADR-0002`).

### Constraints
- Chỉ duy nhất máy sở hữu `Object.HasStateAuthority` của nạn nhân mới được phép ghi vào `[Networked] int CurrentHP` và `[Networked] NetworkBool IsAlive`.
- Lướt tiến (`Dash`) và Lăn né (`Dodge Roll`) loại trừ lẫn nhau (không thể vừa Dash vừa Dodge trong cùng một tick), sử dụng hai bộ đếm `TickTimer` hồi chiêu độc lập (`DashCooldownTimer = 2.50s`, `DodgeCooldownTimer = 1.80s`).
- Khi `CurrentHP` giảm về `0`, nhân vật phải chuyển sang `Layer 10 (GhostSpectator)` ngay trong tick đó để không cản đường đạn hay đòn chém của những người chơi còn sống.

### Requirements
- Đáp ứng trọn vẹn `TR-move-001`, `TR-move-002`, `TR-move-003` và `TR-health-001`, `TR-health-002`, `TR-health-003`.

## Decision

1. **Máy Trạng thái Di chuyển & Cơ động `60Hz` (`PlayerMovementController`)**:
   - Chạy hoàn toàn trong `FixedUpdateNetwork()` trên máy có `Object.HasStateAuthority`.
   - **Đi bộ & Nhảy**: Vận tốc ngang $V_{xz} = \text{MoveDir} \times 6.0\text{ m/s}$; vận tốc dọc $V_y$ áp dụng trọng lực $-19.62\text{ m/s}^2$, xung nhảy $7.5\text{ m/s}$ kèm bộ đệm `CoyoteTime = 0.10s` và `JumpBuffer = 0.10s`.
   - **Forward Dash (`Key 2`)**: Khóa hướng tiến ngang $\hat{F}_{xz}$ tại tick bắt đầu, di chuyển với vận tốc $18.0\text{ m/s}$ trong đúng $12\text{ ticks}$ ($0.20\text{s} \Rightarrow 3.60\text{m}$), khởi động `DashCooldownTimer = 2.50s`.
   - **Dodge Roll Left/Right (`Keys Q/E`)**: Khóa hướng ngang $\pm \hat{R}_{xz}$ tại tick bắt đầu, di chuyển với vận tốc $12.0\text{ m/s}$ trong đúng $15\text{ ticks}$ ($0.25\text{s} \Rightarrow 3.00\text{m}$), khởi động bộ đếm hồi chiêu chung `DodgeCooldownTimer = 1.80s`.
   - **Trượt Tường (`Wall Slide Projection`)**: Sử dụng `Physics.CapsuleCastNonAlloc` kiểm tra va chạm với `Layer 6 (EnvironmentWall)`. Nếu gặp tường có pháp tuyến $\hat{n}$, chiếu vector dịch chuyển lên mặt phẳng tường: $\vec{V}_{slide} = \vec{V} - (\vec{V} \cdot \hat{n})\hat{n}$.
   - **Biểu diễn Tách biệt trên `ChildVisualRoot`**: Trong `Render()`, áp dụng `DOTween` kéo giãn `(0.78, 1.28, 0.78)` khi Dash và nghiêng `±35°` kèm ép dẹt `(1.30, 0.70, 1.15)` khi Dodge Roll chỉ trên nhánh con `ChildVisualRoot`.
2. **Giao thức Sát thương Ủy quyền tại Nạn nhân (`PlayerHealthController`)**:
   - Khai báo `[Networked, OnChangedRender(nameof(OnHPChangedRender))] public int CurrentHP { get; set; }` và `[Networked, OnChangedRender(nameof(OnAliveChangedRender))] public NetworkBool IsAlive { get; set; }`.
   - Khi người tấn công phát hiện trúng đòn, gọi `[Rpc(RpcSources.All, RpcTargets.StateAuthority)] public void Rpc_RequestTakeDamage(int rawDamage, byte attackType, PlayerRef attacker, int attackTick)`.
   - Máy `StateAuthority` của nạn nhân kiểm tra 4 điều kiện hợp lệ: (1) `IsAlive == true`, (2) `MatchManager.Instance.CurrentMatchState == MatchStateEnum.Playing`, (3) `rawDamage` khớp với cấu hình của `attackType` trong `CharacterCombatConfigSO`, và (4) khoảng cách giữa `attacker` và nạn nhân nằm trong ngưỡng dung sai trễ mạng (`MaxRange + 1.5m`).
   - Nếu hợp lệ: cập nhật `CurrentHP = Mathf.Max(0, CurrentHP - rawDamage)`. Nếu `CurrentHP == 0`, đặt `IsAlive = false`, đổi tầng vật lý sang `Layer 10 (GhostSpectator)`, và phát `Rpc_NotifyKillConfirmed(attacker, Object.InputAuthority, attackType)` tới toàn phòng.

### Architecture Diagram

```text
Attacker Client                              Victim StateAuthority Client
+----------------------------+               +------------------------------------+
| Hit detected in local tick |               | PlayerHealthController             |
| Calls Rpc_RequestTakeDamage| ====[RPC]===> | 1. Validate IsAlive & MatchPlaying |
+----------------------------+               | 2. Validate Distance & Damage Stat |
                                             | 3. CurrentHP = Max(0, HP - dmg)    |
                                             | 4. If HP==0: IsAlive=false, Layer10|
                                             +-----------------+------------------+
                                                               |
                                             [Networked] State | Replication (60Hz)
                                                               v
                                             All Peers Trigger OnChangedRender()
```

### Key Interfaces

```csharp
namespace CapsuleArena.Player.Movement
{
    public enum LocomotionState : byte
    {
        GroundedMove = 0,
        Airborne = 1,
        Dashing = 2,
        DodgingLeft = 3,
        DodgingRight = 4,
        DeadGhost = 5
    }

    public interface IMobilityStateReadOnly
    {
        LocomotionState CurrentState { get; }
        float GetDashCooldownRemaining(Fusion.NetworkRunner runner);
        float GetDodgeCooldownRemaining(Fusion.NetworkRunner runner);
    }
}
```

### Implementation Guidelines
- `PlayerHealthController` **must** execute all modifications to `CurrentHP` and `IsAlive` inside an `if (Object.HasStateAuthority)` guard and **must never** allow non-authoritative peers to mutate health properties directly.
- `PlayerMovementController` **must** use pre-allocated `RaycastHit[]` arrays with `Physics.CapsuleCastNonAlloc` and **must never** allocate arrays per tick.
- Visual squash, stretch, and roll-tilt tweens **must** target `_childVisualRoot` and **must never** modify the root `Transform.localScale` or `CapsuleCollider` dimensions.

## Alternatives Considered

### Alternative 1: Attacker Directly Modifies Victim's Networked Health via Shared Authority Transfer
- **Description**: Người tấn công gọi `RequestStateAuthority()` lên nạn nhân để tự trừ máu nạn nhân.
- **Pros**: Không cần gửi RPC.
- **Cons**: Cướp quyền điều khiển vị trí (`NetworkTransform`) của nạn nhân, làm nhân vật của nạn nhân bị giật lùi và hỏng hoàn toàn mô hình điều khiển cục bộ.
- **Estimated Effort**: Trung bình.
- **Rejection Reason**: Phá vỡ quyền điều khiển nhân vật trong Shared Mode.

### Alternative 2: Continuous Rigidbody Force (`AddForce`) for Dash and Dodge Roll
- **Description**: Dùng `Rigidbody.AddForce(Impulse)` để đẩy nhân vật khi lướt và lăn.
- **Pros**: Tận dụng ma sát vật lý có sẵn.
- **Cons**: Quãng đường lướt bị phụ thuộc vào ma sát sàn và khung hình vật lý, khó đảm bảo chính xác tuyệt đối `3.60m` trong `12 ticks` và `3.00m` trong `15 ticks`.
- **Estimated Effort**: Thấp.
- **Rejection Reason**: Di chuyển động học theo tick (`Kinematic Tick-Driven Displacement` kèm `CapsuleCastNonAlloc`) đảm bảo độ chính xác toán học 100% và cảm giác điều khiển sắc gọn.

## Consequences

### Positive
- Giải quyết triệt để bài toán đồng bộ máu trong Shared Mode: dù 3 người chơi cùng chém 1 mục tiêu trong cùng một tick, các lệnh `Rpc_RequestTakeDamage` được xếp hàng tuần tự tại máy `StateAuthority` của nạn nhân, đảm bảo trừ máu chính xác tuyệt đối.
- Quãng đường Dash (`3.6m`) và Dodge (`3.0m`) nhất quán 100% ở mọi máy trạm.

### Negative
- Thời điểm thanh máu giảm có độ trễ bằng `1 RTT` mạng (khoảng `30–60 ms`), được bù đắp hoàn toàn về mặt cảm giác nhờ hiệu ứng tia lửa/âm thanh phản hồi tức thời (`0 ms`) ở phía người tấn công.

### Neutral
- Bộ đếm hồi chiêu sử dụng `Fusion.TickTimer` đồng bộ tự nhiên với đồng hồ mạng.

## Risks
- **Rủi ro gói tin RPC sát thương đến trễ khi nạn nhân đã chạy ra xa**: Giải pháp giảm thiểu là cộng thêm biên độ dung sai vận tốc `1.5m` (`MaxAttackRange + 1.5m`) trong bước kiểm tra khoảng cách tại `Rpc_RequestTakeDamage`.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `character-movement-mobility.md` | `TR-move-001`: 60Hz `FixedUpdateNetwork` locomotion (`MoveSpeed=6.0`, `JumpImpulse=7.5`, `Gravity=-19.62`, `0.10s` coyote/buffer) | Defines tick-accurate kinematic locomotion state machine |
| `character-movement-mobility.md` | `TR-move-002`: Forward Dash (`Key 2`, `18.0 m/s`, `12 ticks`, `3.6m`, `2.50s` CD) with wall slide projection | Specifies Dash tick displacement and `CapsuleCastNonAlloc` wall projection |
| `character-movement-mobility.md` | `TR-move-003`: Dodge Roll (`Keys Q/E`, `12.0 m/s`, `15 ticks`, `3.0m`, `1.80s` shared CD) and `ChildVisualRoot` DOTween | Specifies Dodge Roll state, shared `TickTimer`, and visual root separation |
| `health-damage-rpc-system.md` | `TR-health-001`: Synchronize `[Networked] CurrentHP` (`0..100`) and `IsAlive` with victim `StateAuthority` write lock | Enforces victim `HasStateAuthority` ownership on `[Networked]` health state |
| `health-damage-rpc-system.md` | `TR-health-002`: `Rpc_RequestTakeDamage` routes from `RpcSources.All` to `RpcTargets.StateAuthority` with validation | Defines the 4-step server/authority validation pipeline for damage RPCs |
| `health-damage-rpc-system.md` | `TR-health-003`: `CurrentHP==0` sets `IsAlive=false`, switches to `Layer 10`, disables hitboxes, and broadcasts `Rpc_NotifyKillConfirmed` | Specifies deterministic death state transition and kill notification |

## Performance Implications
- **CPU**: `< 0.15 ms/tick` cho `CapsuleCastNonAlloc` và cập nhật vị trí nhân vật.
- **Memory**: `0 B` cấp phát động nhờ tái sử dụng mảng tĩnh `RaycastHit[8]`.
- **Load Time**: Không ảnh hưởng.
- **Network**: Đồng bộ `CurrentHP` (`4 bytes`), `IsAlive` (`1 byte`), `LocomotionState` (`1 byte`) và `2` bộ `TickTimer` (`8 bytes`).

## Migration Plan
Triển khai `PlayerMovementController.cs` và `PlayerHealthController.cs` trong tầng `Core` ngay sau khi hoàn tất `ADR-0001..0003`.

**Rollback plan**: Nếu ngưỡng dung sai khoảng cách `1.5m` quá chặt khi mạng lag cao (`> 200ms`), điều chỉnh hệ số `RpcDistanceTolerance` trực tiếp trong `CharacterCombatConfigSO`.

## Validation Criteria
- EditMode & PlayMode Tests xác nhận quãng đường Dash sau 12 ticks đạt `3.60m ± 0.05m`, quãng đường Dodge sau 15 ticks đạt `3.00m ± 0.05m`.
- PlayMode Test giả lập 2 đòn đánh `25 dmg` và `30 dmg` gửi đồng thời tới `Rpc_RequestTakeDamage` xác nhận `CurrentHP` giảm chính xác từ `100` xuống `45`.

## Related
- Depends on: [`adr-0001-immutable-combat-config-and-level-physics.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0001-immutable-combat-config-and-level-physics.md), [`adr-0002-keybinding-json-and-input-focus-architecture.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0002-keybinding-json-and-input-focus-architecture.md), [`adr-0003-photon-fusion-shared-mode-and-object-pooling.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0003-photon-fusion-shared-mode-and-object-pooling.md)
- Enables: [`adr-0005-multidirectional-combat-and-lag-compensation.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0005-multidirectional-combat-and-lag-compensation.md), [`adr-0006-match-manager-masterclient-failover.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0006-match-manager-masterclient-failover.md), [`adr-0007-reactive-hud-mobile-pad-and-canvasgroup-chat.md`](file:///d:/workspace/MultiplayGameDemo/docs/architecture/adr-0007-reactive-hud-mobile-pad-and-canvasgroup-chat.md)
- GDDs: [`character-movement-mobility.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/character-movement-mobility.md), [`health-damage-rpc-system.md`](file:///d:/workspace/MultiplayGameDemo/design/gdd/health-damage-rpc-system.md)
