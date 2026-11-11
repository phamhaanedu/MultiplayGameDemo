# Systems Index: Capsule Arena: Last Man Standing

> **Status**: Approved
> **Created**: 2026-11-10
> **Last Updated**: 2026-11-11
> **Source Concept**: design/gdd/game-concept.md
> **Technical Director Review (TD-SYSTEM-BOUNDARY)**: APPROVE 2026-11-10
> **Producer Review (PR-SCOPE)**: REALISTIC 2026-11-10
> **Creative Director Review (CD-SYSTEMS)**: APPROVE 2026-11-10

---

## Overview

*Capsule Arena: Last Man Standing* là một trò chơi đối kháng sinh tồn đấu trường 3D nhiều người chơi (**3D Multiplayer Arena Last Man Standing / Battle Royale**) được phát triển trên **Unity 6.6 (`6000.6.8f1`, URP)**, vận hành trên kiến trúc mạng **Photon Fusion 2 — Shared Mode (`GameMode.Shared`, AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`)** và dịch vụ đám mây **PlayFab C# SDK**. Để hiện thực hóa vòng lặp cốt lõi 30 giây (di chuyển 360°, lăn né đạn, lướt áp sát, tung đòn chém dọc / chém ngang xoay người $90^\circ$ / bắn đạn tầm xa, chuyển đổi góc nhìn `FPS <-> TPS` quanh vật cản) cũng như vòng lặp trận đấu 3–5 phút (sinh tồn đến người cuối cùng, chuyển sang `Ghost Mode Spectator` và trò chuyện `Public/Whisper Chat` khi tử trận, lưu danh lên `PlayFab Leaderboard`), toàn bộ trò chơi được phân rã thành **14 hệ thống độc lập** (11 hệ thống tường minh từ tài liệu Concept và 3 hệ thống nền tảng suy luận ngầm), chia thành **5 tầng kiến trúc (Foundation $\rightarrow$ Core $\rightarrow$ Feature $\rightarrow$ Presentation $\rightarrow$ Polish)** tuân thủ nghiêm ngặt4 trụ cột thiết kế (**Pillar 1: Cơ Động Phản Xạ**, **Pillar 2: Nhãn Quan Chiến Thuật Kép FPS/TPS**, **Pillar 3: Công Bằng Mạng Shared Mode**, **Pillar 4: Khán Đài Hồn Ma & Vinh Quang Bảng Vàng**).

### Phân tích Chi tiết Từng Hệ thống & Mối quan hệ Liên hệ thống (`docs.density: thorough`)

1. **Character & Combat Config Data (inferred)** (`Core` / `MVP` / `Foundation`): Định nghĩa bộ 3 lớp `ScriptableObject` cấu hình dữ liệu trung tâm: (a) `CharacterCombatConfigSO` (`Assets/Data/Config/DefaultCapsuleCombatConfig.asset`) chứa toàn bộ thông số nhân vật và kỹ năng: `MaxHealth` (`100`), `MoveSpeed` (`6.0 m/s`), thông số **Forward Dash phím `2`** (`DashSpeed = 18.0 m/s`, `DashDuration = 0.20s`, `DashCooldown = 3.0s`), thông số **Side Dodge Roll `Q`/`E`** (`DodgeSpeed = 12.0 m/s`, `DodgeDuration = 0.25s`, `DodgeCooldown = 1.5s`), thông số **Vertical Slash `Left Click`** (`VerticalSlashDamage = 25`, `VerticalSlashRange = 2.5m`, `VerticalSlashAngle = 50°`, `VerticalSlashCooldown = 0.60s`), thông số **Horizontal Turning Slash $90^\circ$ phím `1`/`3`** (`HorizontalSweepDamage = 30`, `HorizontalSweepRange = 3.0m`, `HorizontalSweepAngle = 90°`, `HorizontalSweepTurnDuration = 0.18s`, `HorizontalSweepCooldown = 1.20s`), và thông số **Ranged Attack `Right Click`** (`RangedDamage = 15`, `ProjectileSpeed = 15.0 m/s`, `ProjectileMaxDistance = 30.0m`, `RangedCooldown = 0.40s`); (b) `MatchRulesConfigSO` (`DefaultMatchRulesConfig.asset`: `MinPlayersToStart = 2`, `MaxPlayersPerMatch = 4`, `CountdownDurationSeconds = 5.0s`, `PlayingDurationSeconds = 180.0s`, `LateJoinActiveWindowSeconds = 60.0s`, `EndedDurationSeconds = 8.0s`); và (c) `FloatingDamageTextConfigSO` (`DefaultFloatingDamageConfig.asset`). Hệ thống này đóng vai trò hợp đồng dữ liệu chỉ đọc (Read-Only Data Contract) cho cả tầng mô phỏng mạng (`#5`, `#6`, `#7`, `#10`) lẫn tầng hiển thị UI/VFX (`#8`, `#14`), đảm bảo không có bất kỳ hằng số cứng (magic number) nào nằm rải rác trong code.
2. **Arena Level & Tactical Obstacle Architecture (inferred)** (`Core` / `MVP` / `Foundation`): Quy định cấu trúc hình học tĩnh của sàn đấu (`Plane` kẻ lưới Metric Grid `40m x 40m`), 4 bức tường bao chu vi (`Boundary`), các khối vật cản chiến thuật (`Obstacle` Cubes gồm khối che chắn cao `2.6m–3.0m`, khối che chắn thấp `1.2m–1.3m`, và 4 cụm góc vuông chữ L phục vụ đòn chém ngang xoay $90^\circ$), danh sách điểm hồi sinh an toàn (`SpawnPoint`), cùng ma trận va chạm vật lý 4 Layer (`Player`, `Projectile`, `Obstacle`, `Boundary`). Hệ thống này tạo nền tảng vật lý cho **Pillar 2 (Nhãn Quan Chiến Thuật Kép FPS/TPS)** và chống chém/bắn xuyên tường.
3. **Keybinding & Input Management System** (`Core` / `MVP` / `Foundation`): Chịu trách nhiệm nạp và kiểm tra tính hợp lệ của file `keybinding.json` ngay tại **Loading Scene** đầu game (tự động khởi tạo file mặc định nếu chưa tồn tại cho 14 hành động gồm `WASD`, `LMB`, `RMB`, `1`, `2`, `3`, `Q`, `E`, `V`, `Enter`). Trong trận đấu, `LocalInputPoller` thu thập tín hiệu từ cả bàn phím/chuột PC lẫn sự kiện chạm từ cụm 7 nút mềm trên màn hình thông qua cơ chế chốt kép (`SampleSimulationTickInput(Runner.Tick.Raw)` dùng chung theo tick mô phỏng cho mọi `NetworkBehaviour` giữ `StateAuthority` trên Shared Mode + `OnInput` cho đường truyền mạng), đồng thời chứa `InputFocusManager` quản lý các cờ khóa điều khiển độc quyền và phát sự kiện `OnKeybindingsUpdated` mỗi khi người chơi lưu phím mới.
4. **Photon Fusion 2 Shared Mode Session & Object Pool System** (`Core` / `MVP` / `Foundation`): Quản lý vòng đời `NetworkRunner` ở chế độ `GameMode.Shared` (AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`, `MaxPlayers = 4`), phân tách rõ `Quick Join` (`SessionName = ""` ghép phòng mở công khai `< 4` người) và `Create / Join Room` (vào đúng mã phòng viết hoa), bảo vệ truy cập thuộc tính `[Networked]` bằng `IsSpawnedOnRunner` (`_isNetworkSpawned && Object != null && Runner != null && Object.IsValid`), ưu tiên gắn kết Avatar cục bộ (`BindLocalAvatarObject`) ngay khi spawn cho cả Host lẫn máy khách join sau, và triển khai `INetworkObjectProvider` Object Pool để tái sử dụng `NetworkProjectile`, phục vụ trực tiếp **Pillar 3 (Công Bằng Mạng Tuyệt Đối Trên Shared Mode)**.
5. **Tick-Accurate Character Movement & Mobility System** (`Gameplay` / `MVP` / `Core`): Thực thi toàn bộ chuyển động của đấu sĩ Capsule bên trong `FixedUpdateNetwork()` khi `Object.HasStateAuthority == true`: di chuyển `WASD` 360° qua `CharacterController`, xoay mặt theo chuột (`NetworkTransform`), kích hoạt **Forward Dash (phím `2`)** và **Side Dodge Roll (`Q` Lăn trái / `E` Lăn phải)** sử dụng `[Networked] TickTimer` đọc thông số từ `CharacterCombatConfigSO`. Đặc biệt, trong `0.18s` (`11 ticks`) thực hiện đòn chém ngang xoay $90^\circ$ của `#7`, đầu vào xoay chuột ngang được khóa an toàn và tự động mở khóa phục hồi (`EnsureYawLocksReleasedIfIdle()`) ngay khi kết thúc xoay.
6. **Health, Damage RPC & Death State System** (`Gameplay` / `MVP` / `Core`): Quản lý trạng thái sinh tồn được đồng bộ qua mạng gồm `[Networked] int CurrentHealth`, `[Networked] NetworkBool IsDead` và `[OnChangedRender(nameof(OnHealthChanged))]`. Hệ thống tiếp nhận yêu cầu trừ máu từ mọi nguồn qua `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` (`Rpc_TakeDamage`), kiểm tra điều kiện chống trừ máu kép (`if (IsDead || CurrentHealth <= 0) return;`), xác định chủ sở hữu chuẩn trên Shared Mode (`ResolveNetworkOwner()`), phát sự kiện cập nhật thanh máu + chữ sát thương/hồi máu bay lên (`FloatingDamageTextManager`), vô hiệu hóa va chạm khi tử trận và thông báo một chiều cho Camera Ghost Mode (`#9`) cùng `MatchManager` (`#10`).
7. **Multi-Directional Melee & Pooled Ranged Combat System** (`Gameplay` / `MVP` / `Feature`): Trái tim của vòng lặp giao tranh 30 giây (**Pillar 1** & **Pillar 3**), thực thi 3 kỹ năng tấn công trong `FixedUpdateNetwork()` với `[Networked] TickTimer` riêng cho từng chiêu:
   - **Vertical Slash (`Left Click`):** Quét vùng sát thương hình quạt dọc đối xứng phía trước mặt (`2.5m`, `50°`, `25 dmg`, `0.60s` CD), ưu tiên sát thương trực diện.
   - **Horizontal Turning Slash $90^\circ$ (`1` Trái / `3` Phải):** Quét vùng sát thương hình quạt ngang lệch góc vuông $90^\circ$ (`3.0m`, `90°`, `30 dmg`, `1.20s` CD) sang trái hoặc phải, đồng thời **đồng bộ 1:1 theo thời gian thực** giữa tốc độ bung hình quạt VFX (`VisualSweepProgress` từ $0^\circ \to \pm 90^\circ$) và tốc độ xoay thân Capsule + góc nhìn Camera trong `0.18s` (`11 ticks`) — khi hình quạt vừa mở đủ $90^\circ$ cũng chính là lúc nhân vật xoay xong $90^\circ$.
   - **Ranged Attack (`Right Click`):** Lấy viên đạn từ `#4 INetworkObjectProvider` Object Pool (`15 dmg`, `15.0 m/s`, `30.0m`, `0.40s` CD) gắn `EffectiveOwner`, bay theo Tick mạng với cờ bảo vệ `_hasDealtDamage` trước khi `Runner.Despawn()`.
   - Toàn bộ đòn đánh kết hợp kiểm tra tia khuất tầm nhìn (`LineOfSightMask`) để ngăn chặn tuyệt đối việc gây sát thương xuyên tường.
8. **Combat HUD, Overhead Billboard & Mobile Action Pad UI** (`UI` / `MVP` / `Presentation`): Hệ thống giao diện chiến đấu trực tiếp gồm: (a) **World-Space Overhead Billboard** trên đầu mỗi nhân vật (`PlayerName` + Thanh máu `100/100` có lớp tụt máu trễ) kết hợp **World-Space Floating Damage/Heal Text (`FloatingDamageTextManager`)** bay lên trên mọi mục tiêu trúng đòn/hồi máu (ẩn chữ của riêng bản thân khi ở góc nhìn `FPS`); (b) Thanh máu HUD bản thân và thông tin trận đấu; và (c) **Cụm 7 Button kỹ năng mềm góc dưới phải màn hình (Mobile-Ready Thumb-Arc Action Pad)** hiển thị icon định hướng cho `LMB`, `RMB`, `1`, `2`, `3`, `Q`, `E`, vòng quét Cooldown `TickTimer` thời gian thực và nhãn phím tắt động (`Dynamic Keycap Pill`) tự động đổi chữ khi nhận sự kiện `OnKeybindingsUpdated` từ `#3`.
9. **Dual-Perspective Camera (FPS/TPS) & Ghost Mode Spectator System** (`Gameplay` / `Vertical Slice` / `Feature`): Quản lý `CameraController` gắn kết tức thì ngay khi Avatar cục bộ spawn (`BindLocalCamera` + `StepCameraFrame(0f)`) trên cả Host lẫn máy khách join phòng (**Pillar 2** & **Pillar 4**). Cho phép bấm phím `V` để chuyển đổi tức thì giữa góc nhìn thứ ba (**TPS** — quan sát góc khuất sau khối `Obstacle`) và góc nhìn thứ nhất (**FPS** — ngắm bắn khe hẹp kèm tâm ngắm `Crosshair`, tự động chuyển Mesh thân bản thân sang `ShadowCastingMode.ShadowsOnly` và ẩn chữ sát thương/hồi máu nổi của riêng bản thân để không che tầm nhìn). Camera xoay đồng bộ 1:1 với thân Capsule $90^\circ$ trong `0.18s` khi tung đòn chém ngang phím `1`/`3`, và tự động chuyển sang **Ghost Mode Spectator** khi tử trận hoặc khi vào phòng trễ sau `60.0s`.
10. **Last Man Standing Match Manager & Authority Failover System** (`Gameplay` / `Vertical Slice` / `Feature`): Scene `NetworkObject` điều phối vòng lặp trận đấu dựa trên `MatchRulesConfigSO` dưới quyền `SharedModeMasterClient` (bật `Allow State Authority Override = true` và `Destroy When State Authority Leaves = false`). Khi có đủ người chơi thứ 2 (`MinPlayersToStart = 2`), tự động đếm ngược `5.0s` (`CountdownDurationSeconds`) rồi vào trận (`180.0s`). Trong **`60.0s` đầu tiên** của pha `Playing` (`LateJoinActiveWindowSeconds`), người chơi thứ 3 và thứ 4 join vào phòng vẫn được tham chiến sống sót bình thường; chỉ sau mốc `60.0s` mới chuyển thẳng sang **Ghost Spectator** (chờ bảng `MatchRoster` đồng bộ xong trên máy khách trước khi áp trạng thái khán giả).
11. **In-Game Keybinding Rebind UI & Lobby Flow UI (inferred)** (`UI` / `Vertical Slice` / `Presentation`): Quản lý giao diện màn hình Lobby với 2 nhãn hướng dẫn tường minh (`PLAYER DISPLAY NAME` và `ROOM CODE (FOR CREATE / JOIN)`), phân biệt rõ nút `QUICK JOIN` và `CREATE / JOIN ROOM`, hàng đợi `PendingDisplayName` đảm bảo tên hiển thị của máy khách join phòng không bao giờ bị rớt trước `MatchManager.Spawned()`, sảnh chờ `ROOM: <CODE> (X/4)`, cùng cửa sổ **In-Game Keybinding Rebind UI** điều khiển bằng `CanvasGroup` hỗ trợ `Conflict Detection & Smart Swap` và lưu xuống `keybinding.json`.
12. **In-Game Chat System (Public & Private Whisper)** (`UI` / `Alpha` / `Presentation`): Hệ thống giao tiếp xã hội trong trận đấu và khi đang ở Ghost Mode (**Pillar 4**). Đồng bộ tin nhắn qua `[Rpc]` với 2 chế độ: **Public Chat (`[ALL]`)** gửi cho toàn phòng và **Private Whisper (`[WHISPER -> @Tên]`)** chỉ hiển thị cho người gửi và người nhận đích danh. Sử dụng `CanvasGroup` để ẩn/hiện mượt mà (0 Layout Rebuild spike) và phối hợp chặt chẽ với `#3 InputFocusManager` để khóa hoàn toàn điều khiển nhân vật/camera khi người chơi nhấn `Enter` gõ chữ.
13. **PlayFab Cloud Identity, Stats & Leaderboard System** (`Persistence` / `Alpha` / `Polish`): Tích hợp **PlayFab C# SDK** xử lý đăng nhập bất đồng bộ, đồng bộ `Player Data` / `Title Data`, gửi chỉ số `Total Wins` và `Total Kills` lên đám mây từ máy có `HasStateAuthority` sau khi `#10 MatchManager` kết thúc ván đấu, và hiển thị bảng xếp hạng **PlayFab Leaderboard** vinh danh Top đấu sĩ.
14. **3D Spatial Audio & Combat VFX Feedback System** (`Audio` / `Full Vision` / `Polish`): Đánh bóng cảm giác chiến đấu ("Game Feel" — **Aesthetic 3: Sensation**): phát âm thanh không gian 3D (`Spatial Blend = 1.0`), quản lý Local VFX Object Pool cho vệt quạt dọc `50°` (`LMB`), vệt quạt ngang $90^\circ$ (`1`/`3`) bung mở đồng bộ 1:1 theo góc xoay thân trong `0.18s`, tia nổ trúng đòn, biến dạng đàn hồi **DOTween Squash & Stretch** trên `VisualRoot`, và hồ chứa 16 chữ sát thương/hồi máu bay lên (**Floating Damage/Heal Text**: số nguyên không dấu `-`/`+`, Đỏ `#FF3B3B` khi mất máu / Xanh lá `#22C55E` khi hồi máu, `2/3` thời gian đầu phóng to `0.85x -> 1.50x`, `1/3` cuối thu nhỏ `1.50x -> 0.55x` đồng thời mờ dần `alpha -> 0`).

---

## Systems Enumeration

| # | System Name | Category | Priority | Status | Design Doc | Depends On |
|---|-------------|----------|----------|--------|------------|------------|
| 1 | Character & Combat Config Data (inferred) | Core | MVP | Approved | design/gdd/character-combat-config.md | — |
| 2 | Arena Level & Tactical Obstacle Architecture (inferred) | Core | MVP | Approved | design/gdd/arena-level-architecture.md | — |
| 3 | Keybinding & Input Management System | Core | MVP | Approved | design/gdd/keybinding-input-system.md | — |
| 4 | Photon Fusion 2 Shared Mode Session & Object Pool System | Core | MVP | Approved | design/gdd/shared-mode-session-pool.md | — |
| 5 | Tick-Accurate Character Movement & Mobility System | Gameplay | MVP | Approved | design/gdd/character-movement-mobility.md | Photon Fusion 2 Shared Mode Session & Object Pool System, Keybinding & Input Management System, Character & Combat Config Data (inferred), Arena Level & Tactical Obstacle Architecture (inferred) |
| 6 | Health, Damage RPC & Death State System | Gameplay | MVP | Approved | design/gdd/health-damage-rpc-system.md | Photon Fusion 2 Shared Mode Session & Object Pool System, Character & Combat Config Data (inferred) |
| 7 | Multi-Directional Melee & Pooled Ranged Combat System | Gameplay | MVP | Approved | design/gdd/combat-melee-ranged-system.md | Tick-Accurate Character Movement & Mobility System, Health, Damage RPC & Death State System, Photon Fusion 2 Shared Mode Session & Object Pool System, Character & Combat Config Data (inferred), Arena Level & Tactical Obstacle Architecture (inferred) |
| 8 | Combat HUD, Overhead Billboard & Mobile Action Pad UI | UI | MVP | Approved | design/gdd/combat-hud-mobile-action-pad.md | Multi-Directional Melee & Pooled Ranged Combat System, Health, Damage RPC & Death State System, Keybinding & Input Management System |
| 9 | Dual-Perspective Camera (FPS/TPS) & Ghost Mode Spectator System | Gameplay | Vertical Slice | Approved | design/gdd/camera-fps-tps-spectator.md | Tick-Accurate Character Movement & Mobility System, Health, Damage RPC & Death State System, Keybinding & Input Management System |
| 10 | Last Man Standing Match Manager & Authority Failover System | Gameplay | Vertical Slice | Approved | design/gdd/match-manager-authority-failover.md | Photon Fusion 2 Shared Mode Session & Object Pool System, Health, Damage RPC & Death State System |
| 11 | In-Game Keybinding Rebind UI & Lobby Flow UI (inferred) | UI | Vertical Slice | Approved | design/gdd/keybinding-rebind-lobby-ui.md | Keybinding & Input Management System, Photon Fusion 2 Shared Mode Session & Object Pool System |
| 12 | In-Game Chat System (Public & Private Whisper) | UI | Alpha | Approved | design/gdd/in-game-chat-system.md | Photon Fusion 2 Shared Mode Session & Object Pool System, Keybinding & Input Management System |
| 13 | PlayFab Cloud Identity, Stats & Leaderboard System | Persistence | Alpha | Approved | design/gdd/playfab-cloud-leaderboard.md | Last Man Standing Match Manager & Authority Failover System, In-Game Keybinding Rebind UI & Lobby Flow UI (inferred) |
| 14 | 3D Spatial Audio & Combat VFX Feedback System | Audio | Full Vision | Approved | design/gdd/spatial-audio-combat-vfx.md | Multi-Directional Melee & Pooled Ranged Combat System, Tick-Accurate Character Movement & Mobility System, Health, Damage RPC & Death State System |

---

## Categories

| Category | Description | Typical Systems |
|----------|-------------|-----------------|
| **Core** | Foundation systems everything depends on | `CharacterCombatConfigSO`, Arena level & 4-Layer Physics Matrix, `keybinding.json` & `InputFocusManager`, Photon Fusion 2 Shared Mode & `INetworkObjectProvider` Pool |
| **Gameplay** | The systems that make the game fun | Tick-accurate movement (`WASD`, Dash `2`, Dodge `Q`/`E`, $90^\circ$ turn), Multi-directional melee (`LMB`, `1`, `3`) & pooled ranged combat (`RMB`), Health/Damage RPC, Cinemachine `FPS <-> TPS` & Ghost Mode Spectator, `MatchManager` |
| **Persistence** | Save state, cloud sync, and continuity | PlayFab Cloud Identity, `Player Data` / `Title Data`, and Global Leaderboards (`Wins` / `Kills`) |
| **UI** | Player-facing information displays and interactive controls | World-Space Overhead Billboard (`100/100`), Screen-Space Combat HUD, Mobile-Ready 7-Button Action Pad, In-Game Keybinding Rebind UI, Lobby UI, `CanvasGroup` Public/Whisper Chat |
| **Audio** | Sound and visual feedback systems | 3D Spatial Audio (`Spatial Blend = 1.0`) & Combat VFX / `DOTween Squash & Stretch` via `Rpc_PlayAttackEffect` |

---

## Priority Tiers

| Tier | Definition | Target Milestone | Design Urgency |
|------|------------|------------------|----------------|
| **MVP** | Required for the core 30-second Shared Mode combat & mobility loop to function (`CharacterCombatConfigSO`, `keybinding.json`, 7 skills + mobile pad, Damage RPC, Overhead Billboard). | First playable prototype (Week 1) | Design FIRST |
| **Vertical Slice** | Required for a complete end-to-end Last Man Standing match loop (`FPS <-> TPS` Camera, `Ghost Mode Spectator`, `MatchManager` failover, In-Game Keybinding Rebind UI & Lobby). | Vertical slice / demo (Week 2) | Design SECOND |
| **Alpha** | Complete GAM302 feature scope with social communication and cloud persistence (`CanvasGroup` Public/Whisper Chat, PlayFab Login/Data/Leaderboards). | Alpha milestone (Week 3) | Design THIRD |
| **Full Vision** | Sensory polish, 3D Spatial Audio RPCs, Local VFX Object Pools, and `DOTween Squash & Stretch` game-feel refinement. | Beta / Release (Week 4) | Design FOURTH |

---

## Dependency Map

### Foundation Layer (no dependencies)

1. **Character & Combat Config Data (inferred)** — Nguồn dữ liệu `ScriptableObject` (`CharacterCombatConfigSO`) chỉ đọc duy nhất cho toàn bộ chỉ số máu, tốc độ, sát thương, góc quạt $90^\circ$, thời gian xoay `0.15s` và Cooldown.
2. **Arena Level & Tactical Obstacle Architecture (inferred)** — Định nghĩa mặt phẳng đấu trường `Plane`, tường bao `Boundary`, các khối `Obstacle` (cao, thấp, góc chữ L $90^\circ$), danh sách `SpawnPoint` và ma trận vật lý 4 Layer (`Player`, `Projectile`, `Obstacle`, `Boundary`).
3. **Keybinding & Input Management System** — Nạp/lưu file `keybinding.json` ngay từ Loading Scene, quản lý khóa phím độc quyền qua `InputFocusManager`, thu thập input phần cứng + nút mềm ảo qua `IInputProvider` vào `NetworkInputData`, và phát sự kiện `OnKeybindingsUpdated`.
4. **Photon Fusion 2 Shared Mode Session & Object Pool System** — Khởi tạo phiên `NetworkRunner` (`GameMode.Shared`), phân quyền `StateAuthority` trên từng Client và cung cấp bộ quản lý `INetworkObjectProvider` Object Pool.

### Core Layer (depends on foundation)

1. **Tick-Accurate Character Movement & Mobility System** — depends on: `Photon Fusion 2 Shared Mode Session & Object Pool System`, `Keybinding & Input Management System`, `Character & Combat Config Data (inferred)`, `Arena Level & Tactical Obstacle Architecture (inferred)`
2. **Health, Damage RPC & Death State System** — depends on: `Photon Fusion 2 Shared Mode Session & Object Pool System`, `Character & Combat Config Data (inferred)`

### Feature Layer (depends on core)

1. **Multi-Directional Melee & Pooled Ranged Combat System** — depends on: `Tick-Accurate Character Movement & Mobility System`, `Health, Damage RPC & Death State System`, `Photon Fusion 2 Shared Mode Session & Object Pool System`, `Character & Combat Config Data (inferred)`, `Arena Level & Tactical Obstacle Architecture (inferred)`
2. **Dual-Perspective Camera (FPS/TPS) & Ghost Mode Spectator System** — depends on: `Tick-Accurate Character Movement & Mobility System`, `Health, Damage RPC & Death State System`, `Keybinding & Input Management System`
3. **Last Man Standing Match Manager & Authority Failover System** — depends on: `Photon Fusion 2 Shared Mode Session & Object Pool System`, `Health, Damage RPC & Death State System`

### Presentation Layer (depends on features)

1. **Combat HUD, Overhead Billboard & Mobile Action Pad UI** — depends on: `Multi-Directional Melee & Pooled Ranged Combat System`, `Health, Damage RPC & Death State System`, `Keybinding & Input Management System`
2. **In-Game Keybinding Rebind UI & Lobby Flow UI (inferred)** — depends on: `Keybinding & Input Management System`, `Photon Fusion 2 Shared Mode Session & Object Pool System`
3. **In-Game Chat System (Public & Private Whisper)** — depends on: `Photon Fusion 2 Shared Mode Session & Object Pool System`, `Keybinding & Input Management System`

### Polish Layer (depends on everything)

1. **PlayFab Cloud Identity, Stats & Leaderboard System** — depends on: `Last Man Standing Match Manager & Authority Failover System`, `In-Game Keybinding Rebind UI & Lobby Flow UI (inferred)`
2. **3D Spatial Audio & Combat VFX Feedback System** — depends on: `Multi-Directional Melee & Pooled Ranged Combat System`, `Tick-Accurate Character Movement & Mobility System`, `Health, Damage RPC & Death State System`

---

## Recommended Design Order

| Order | System | Priority | Layer | Agent(s) | Est. Effort |
|-------|--------|----------|-------|----------|-------------|
| 1 | Character & Combat Config Data (inferred) | MVP | Foundation | systems-designer, gameplay-programmer | S |
| 2 | Arena Level & Tactical Obstacle Architecture (inferred) | MVP | Foundation | level-designer, unity-specialist | S |
| 3 | Keybinding & Input Management System | MVP | Foundation | systems-designer, gameplay-programmer | M |
| 4 | Photon Fusion 2 Shared Mode Session & Object Pool System | MVP | Foundation | network-engineer, unity-specialist | M |
| 5 | Tick-Accurate Character Movement & Mobility System | MVP | Core | game-designer, gameplay-programmer | M |
| 6 | Health, Damage RPC & Death State System | MVP | Core | systems-designer, network-engineer | S |
| 7 | Multi-Directional Melee & Pooled Ranged Combat System | MVP | Feature | game-designer, gameplay-programmer, network-engineer | M |
| 8 | Combat HUD, Overhead Billboard & Mobile Action Pad UI | MVP | Presentation | ux-designer, ui-programmer | M |
| 9 | Dual-Perspective Camera (FPS/TPS) & Ghost Mode Spectator System | Vertical Slice | Feature | game-designer, gameplay-programmer | M |
| 10 | Last Man Standing Match Manager & Authority Failover System | Vertical Slice | Feature | systems-designer, network-engineer | M |
| 11 | In-Game Keybinding Rebind UI & Lobby Flow UI (inferred) | Vertical Slice | Presentation | ux-designer, ui-programmer | M |
| 12 | In-Game Chat System (Public & Private Whisper) | Alpha | Presentation | ux-designer, network-engineer, ui-programmer | M |
| 13 | PlayFab Cloud Identity, Stats & Leaderboard System | Alpha | Polish | network-engineer, systems-designer | M |
| 14 | 3D Spatial Audio & Combat VFX Feedback System | Full Vision | Polish | sound-designer, technical-artist, gameplay-programmer | S |

---

## Circular Dependencies

- **None found (0 circular dependencies).** Ba mối tương tác hai chiều tiềm ẩn đã được giải quyết triệt để ngay ở thiết kế kiến trúc biên giới hệ thống (`TD-SYSTEM-BOUNDARY: APPROVE`):
  1. **Cụm 7 Nút Mềm HUD (`#8`) / Bảng Gán Phím Rebind UI (`#11`) $\leftrightarrow$ Hệ thống Quản lý Phím & Input (`#3`):** Giải quyết bằng **Đảo ngược Phụ thuộc (`IInputProvider`) & Sự kiện C# (`OnKeybindingsUpdated`)**. `#8` và `#11` phụ thuộc một chiều xuống `#3`; khi `#11` lưu `keybinding.json` qua `#3`, `#3` phát sự kiện `OnKeybindingsUpdated` để `#8` tự động làm mới 7 nhãn `Keycap Badge` mà `#3` và `#11` không cần giữ bất kỳ tham chiếu nào tới `#8`.
  2. **Chém Ngang Xoay Người $90^\circ$ (`#7 Combat`) $\leftrightarrow$ Xoay Thân Người (`#5 Movement`) $\leftrightarrow$ Xoay Góc Nhìn (`#9 Camera`):** Giải quyết bằng **Ủy quyền Mệnh lệnh Một chiều (`RequestTimedYawTurn`)**. Chỉ duy nhất `#5 Movement` sở hữu quyền ghi `transform.rotation` trong `FixedUpdateNetwork()`. Khi người chơi bấm phím `1` hoặc `3`, `#7 Combat` gọi một chiều xuống `#5 Movement.RequestTimedYawTurn(±90f, 0.15f)`; trong khi đó `#9 Camera` chỉ bám theo transform con `CameraTarget_FPS` / `CameraTarget_TPS` của nhân vật.
  3. **Trạng thái Tử trận (`#6 Health`) $\leftrightarrow$ Chế độ Hồn ma (`#9 Ghost Spectator`) & Xét thắng thua (`#10 MatchManager`):** `#6 Health` ở tầng Core phát sự kiện `OnPlayerDeathStateChanged` (kèm `PlayerRef attacker` và trạng thái `[Networked] NetworkBool IsDead`); cả `#9` và `#10` ở tầng Feature lắng nghe một chiều sự kiện này mà `#6` không cần biết sự tồn tại của `#9` hay `#10`.

---

## High-Risk Systems

| System | Risk Type | Risk Description | Mitigation |
|--------|-----------|-----------------|------------|
| **Multi-Directional Melee & Pooled Ranged Combat System (`#7`)** | Technical / Design | Đồng bộ vùng quét quạt dọc (`LMB`), quạt ngang $90^\circ$ (`1`/`3`) và đường đạn Object Pool (`RMB`) với `HitboxRoot` Lag Compensation trong Shared Mode mà không gây trừ máu kép hoặc chém xuyên khối `Obstacle`. | Sử dụng cờ `_hasDealtDamage` trên mỗi viên đạn từ Pool, kiểm tra `if (IsDead \|\| CurrentHealth <= 0) return;` tại `StateAuthority` nhận `Rpc_TakeDamage`, và bắt buộc bắn tia `Physics.Linecast` kiểm tra `LayerMask = Obstacle \| Boundary` trước khi xác nhận trúng đòn cận chiến. |
| **Tick-Accurate Character Movement & Mobility System (`#5`)** | Technical | Nội suy mượt phép xoay thân người $90^\circ$ trong `~0.15s` khi chém ngang (`1`/`3`) đồng thời với `Forward Dash [2]` và `Side Dodge Roll [Q/E]` trong `FixedUpdateNetwork()` mà không gây giật cục (rubber-banding) hay lệch `HitboxRoot`. | Thực thi toàn bộ chuyển động qua `CharacterController` trong `FixedUpdateNetwork()` dưới quyền `StateAuthority`, quản lý trạng thái bằng `[Networked] TickTimer` (`IsDashing`, `IsDodging`, `IsTurningYaw`), và cô lập hoàn toàn hiệu ứng `DOTween Squash & Stretch` lên nhánh con `VisualRoot`. |
| **Last Man Standing Match Manager & Authority Failover System (`#10`)** | Technical | Nếu máy giữ quyền `SharedModeMasterClient` thoát đột ngột giữa trận, đồng hồ `MatchTimer` và việc xét thắng thua khi `AliveCount == 1` có thể bị đóng băng. | Thiết lập `MatchManager` là Scene `NetworkObject` bật `Allow State Authority Override = true`, tắt `Destroy When State Authority Leaves`, dùng `[Networked] TickTimer MatchTimer` và tính `AliveCount` động từ các `Player` còn sống trong phòng. |
| **Keybinding & Input Management System (`#3`)** | Technical / UX | Xung đột phím điều khiển khiến nhân vật tự di chuyển/tung chiêu khi người chơi đang nhấn `Enter` gõ Chat hoặc đang mở bảng Rebind UI đổi phím `keybinding.json`. | Áp dụng máy trạng thái khóa độc quyền `InputFocusManager` (`CombatActive`, `ChatTyping`, `RebindModalOpen`, `GhostSpectating`): khi khác `CombatActive`, hàm `OnInput` gửi cấu trúc `NetworkInputData` rỗng (`default`) vào mạng. |

---

## Progress Tracker

| Metric | Count |
|--------|-------|
| Total systems identified | 14 |
| Design docs started | 14 |
| Design docs reviewed | 14 |
| Design docs approved | 14 |
| MVP systems designed | 8/8 |
| Vertical Slice systems designed | 3/3 |

---

## Next Steps

- [x] Review and approve this systems enumeration (`TD-SYSTEM-BOUNDARY: APPROVE`, `PR-SCOPE: REALISTIC`)
- [x] Design MVP-tier systems first (use `/design-system [system-name]`)
- [x] Design all remaining Vertical Slice, Alpha & Full Vision systems (`14/14` GDDs complete)
- [x] Run `/create-architecture` and `/architecture-decision` when GDDs are complete (`8/8` ADRs Accepted)
- [x] Run `/architecture-review` and `/gate-check` before moving to Pre-Production / Production (`PASS`)
