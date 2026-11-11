# Sprint 001 — 2026-11-11 to 2026-11-24

> **Producer Feasibility Review (PR-SPRINT)**: REALISTIC (2026-11-10)
> **QA Plan**: `production/qa/qa-plan-sprint-001-2026-11-10.md`

## Sprint Goal
Xây dựng hoàn chỉnh tầng Nền tảng (`Foundation`) và tầng Cốt lõi (`Core`) cho **Capsule Arena: Last Man Standing**: dữ liệu cấu hình bất biến `CharacterCombatConfigSO`, đấu trường đối xứng `40m x 40m` (`Assets/Scenes/Arena.unity`) kèm ma trận va chạm vật lý 4 lớp (`Layers 6, 8, 9, 10`), hệ thống lưu trữ phím nguyên tử `keybindings.json` + bộ thu thập điều khiển kép PC/Mobile `LocalInputPoller` + `InputFocusManager`, kết nối mạng **Photon Fusion 2 Shared Mode (`60Hz`)** + hồ chứa đối tượng `INetworkObjectProvider` (`32` viên đạn), di chuyển `WASD`/Jump + lướt tiến **Forward Dash (`Phím 2`)** + lăn né **Dodge Roll (`Phím Q/E`)**, và hệ thống máu/sát thương RPC + chuyển trạng thái hồn ma **`Layer 10 GhostSpectator`**.

## Goal Prerequisites
| Needed to show the goal | Status |
|-------------------------|--------|
| `Assets/Scenes/Arena.unity` (main gameplay scene with `40m x 40m` courtyard & cover obstacles) | `built by [2-1]` (`story-001-arena-courtyard-and-cover-geometry.md`) |
| `ProjectSettings/EditorBuildSettings.asset` (scene list registration) | `exists` (updated by `[2-1]`) |
| `Assets/Data/Config/DefaultCapsuleCombatConfig.asset` (canonical combat & mobility balance) | `built by [1-3]` (`story-003-default-combat-config-asset.md`) |
| `Assets/Scripts/Core/Config/ArenaLayers.cs` (4-layer physics collision matrix `Layers 6, 8, 9, 10`) | `built by [2-3]` (`story-003-physics-layer-collision-matrix.md`) |
| `Assets/Scripts/Networking/FusionSessionManager.cs` (`GameMode.Shared`, `60Hz`, AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`) | `built by [4-1]` (`story-001-fusion-shared-mode-session-bootstrap.md`) |

## Milestone Context
- Current Milestone: Milestone 1 — Foundation & Core Networked Mobility/Health Loop (Epics 1–6)
- Milestone Deadline: 2026-11-24
- Sprints Remaining: 3 (Sprint 001: Foundation/Core; Sprint 002: Feature/UI Vertical Slice; Sprint 003: Social/Cloud/Polish Release)

## Capacity
- Total days: 14.0
- Buffer (20%): 2.8 days reserved for unplanned work
- Available: 11.2 days (Planned: 11.0 days across 18 stories)

## Tasks

### Must Have (Critical Path)
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| `1-1` | [Story 001: Combat Config Encapsulation & Validation](../epics/character-combat-config/story-001-combat-config-encapsulation.md) (`TR-config-001`) | `gameplay-programmer` | 0.5 | None | `[SerializeField] private` fields + read-only C# properties, `OnValidate()` clamping, `0 B` runtime GC |
| `1-2` | [Story 002: Precomputed Tick & Cosine Threshold Cache](../epics/character-combat-config/story-002-precomputed-tick-and-cosine-cache.md) (`TR-config-002`) | `gameplay-programmer` | 0.5 | `1-1` | `DashTicks=12`, `DodgeTicks=15`, `HorizontalTurnTicks=11`, `ProjectileLifetimeTicks=120`, `CosHalfAngle` cached in `OnEnable()`/`OnValidate()` |
| `1-3` | [Story 003: Default Capsule Combat Config Asset & Balance Baseline](../epics/character-combat-config/story-003-default-combat-config-asset.md) (`TR-config-003`) | `systems-designer` | 0.5 | `1-2` | `DefaultCapsuleCombatConfig.asset` matches exact GDD baseline (`MaxHP=100`, `MoveSpeed=6.0`, `Dash=18.0`, `Dodge=12.0`, `Dmg=25/30/15`) |
| `2-1` | [Story 001: Arena Courtyard & Tactical Cover Geometry](../epics/arena-level-architecture/story-001-arena-courtyard-and-cover-geometry.md) (`TR-arena-001`) | `level-designer` | 1.0 | None | `40m x 40m` courtyard in `Arena.unity`, `2.5m` perimeter wall, `6m x 6m` altar, 4 L-walls, 8 pillars on `Layer 6`/`Layer 9`, corridor gaps `>= 2.5m` |
| `2-2` | [Story 002: Max-Min Euclidean Spawn Point Selector](../epics/arena-level-architecture/story-002-max-min-euclidean-spawn-selector.md) (`TR-arena-002`) | `gameplay-programmer` | 0.5 | `2-1` | 8 perimeter `SpawnPoint` transforms (`16m` radius) selecting max-min Euclidean distance (`>= 18.0m` separation) + deterministic Round-Robin fallback |
| `2-3` | [Story 003: Physics Layer Collision Matrix & LineOfSightMask](../epics/arena-level-architecture/story-003-physics-layer-collision-matrix.md) (`TR-arena-003`) | `engine-programmer` | 0.5 | `2-1` | `ArenaLayers` enforces 4-layer collision matrix (`6, 8, 9, 10`) and `LineOfSightMask (1 << 6)` blocking walls while `Layer 10` passes through `Layer 6` |
| `3-1` | [Story 001: Keybinding JSON Atomic Persistence & Recovery](../epics/keybinding-input-system/story-001-keybinding-json-atomic-persistence.md) (`TR-input-001`) | `gameplay-programmer` | 0.75 | None | 13 game actions persisted in `keybindings.json` via atomic `.tmp` write, corrupt JSON auto-recovery to defaults, `OnBindingsChanged` event |
| `3-2` | [Story 002: Dual PC & Mobile Input Poller (`NetworkInputData`)](../epics/keybinding-input-system/story-002-dual-pc-mobile-input-poller.md) (`TR-input-002`) | `gameplay-programmer` | 0.75 | `3-1` | `LocalInputPoller` merges PC `InputAction` and `MobileInputBridge` via bitwise OR (`|`) into 16-byte `NetworkInputData` with one-shot latch |
| `4-1` | [Story 001: Photon Fusion 2 Shared Mode Session Bootstrap](../epics/shared-mode-session-pool/story-001-fusion-shared-mode-session-bootstrap.md) (`TR-session-001`) | `network-engineer` | 1.0 | `2-2` | `FusionSessionManager` starts `GameMode.Shared` at `60Hz` (`MaxPlayers=4`, AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`) and spawns local player in `OnPlayerJoined` |
| `4-2` | [Story 002: Zero-Alloc Network Object Pool Provider (`INetworkObjectProvider`)](../epics/shared-mode-session-pool/story-002-network-object-pool-provider.md) (`TR-session-002`) | `network-engineer` | 0.75 | `4-1` | `FusionObjectPoolProvider` pre-warms `32` `NetworkProjectile` instances at scene load with `0` runtime `Instantiate`/`Destroy` |
| `5-1` | [Story 001: Tick-Accurate Walk, Jump & Gravity (`NetworkCharacterController`)](../epics/character-movement-mobility/story-001-tick-accurate-walk-jump-gravity.md) (`TR-move-001`) | `gameplay-programmer` | 0.75 | `1-2, 3-2` | `WASD` walk (`6.0 m/s`), jump (`1.5m`, `v_y0=7.672 m/s`), gravity (`-19.62 m/s^2`), and `Y < -5.0m` out-of-bounds recovery in `FixedUpdateNetwork()` |
| `5-2` | [Story 002: Forward Dash (`Key 2`) & Wall-Slide Projection](../epics/character-movement-mobility/story-002-forward-dash-key2-and-wall-slide.md) (`TR-move-002`) | `gameplay-programmer` | 0.5 | `5-1` | Forward Dash (`Key 2`) locks `LockedMobilityDirection` at `18.0 m/s` for `12 ticks` (`3.60m`), `3.0s` cooldown, and `Vector3.ProjectOnPlane` wall slide |
| `6-1` | [Story 001: Victim-Authoritative Networked Health (`PlayerHealth`)](../epics/health-damage-rpc-system/story-001-victim-authoritative-networked-health.md) (`TR-health-001`) | `network-engineer` | 0.5 | `1-3` | `[Networked] CurrentHP (100)` and `IsDead` mutated only on victim `StateAuthority` with Fusion 2 `ChangeDetector` firing `OnHealthChanged` |
| `6-2` | [Story 002: Damage Request RPC & Distance Sanity Validation](../epics/health-damage-rpc-system/story-002-damage-request-rpc-validation.md) (`TR-health-002`) | `network-engineer` | 0.5 | `6-1` | `Rpc_RequestApplyDamage` validates `MatchState == Playing`, `!IsDead`, canonical damage (`25/30/15`), distance sanity (`5.0m`/`34.0m`), and single-killer attribution |

### Should Have
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| `3-3` | [Story 003: InputFocusManager Exclusive Gating](../epics/keybinding-input-system/story-003-input-focus-manager-gating.md) (`TR-input-003`) | `gameplay-programmer` | 0.5 | `3-2` | `InputFocusManager` bitmask zeroes out `NetworkInputData` when `ChatFocused`, `RebindModalOpen`, `MatchEnded`, or `LeaderboardOpen` is active |
| `4-3` | [Story 003: Pooled NetworkProjectile Lifecycle & State Reset](../epics/shared-mode-session-pool/story-003-pooled-projectile-lifecycle-reset.md) (`TR-session-003`) | `network-engineer` | 0.5 | `4-2` | `NetworkProjectile` resets `TrailRenderer`, `Rigidbody` velocity, `_hasDealtDamage`, and `120-tick` (`2.0s`) expiry timer on pool acquire/release |
| `5-3` | [Story 003: Lateral Dodge Roll (`Keys Q/E`) & Visual Root Tilt](../epics/character-movement-mobility/story-003-dodge-roll-qe-and-visual-tilt.md) (`TR-move-003`) | `gameplay-programmer` | 0.5 | `5-2` | Lateral Dodge Roll (`Keys Q/E`) at `12.0 m/s` for `15 ticks` (`3.00m`, `1.5s` shared CD) + `360 deg` Z-roll / `15 deg` dash pitch tilt on child `VisualRoot` |
| `6-3` | [Story 003: Death State & Ghost Spectator Layer Transition](../epics/health-damage-rpc-system/story-003-death-state-and-ghost-layer-transition.md) (`TR-health-003`) | `gameplay-programmer` | 0.5 | `2-3, 6-2` | On `CurrentHP == 0`, switches player collider to `Layer 10 (GhostSpectator)`, swaps mesh to `M_Capsule_Ghost` (`Alpha=0.35`), and preserves `NetworkObject` |

### Nice to Have
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| — | *None — all 18 Foundation & Core stories are allocated to Must Have and Should Have.* | — | 0.0 | — | — |

## Carryover from Previous Sprint
| Task | Reason | New Estimate |
|------|--------|-------------|
| *None (Sprint 001 is the first sprint)* | — | — |

## Risks
*(Note: No risk register directory at `production/risk-register/` — risks assessed from the sprint contents only.)*

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| High-speed Dash (`18.0 m/s`) snagging or tunneling at `90 deg` L-wall corners in `Arena.unity` | Medium | High | Use `Vector3.ProjectOnPlane` tangent velocity projection in Story `5-2` and enforce `>= 2.5m` corridor gaps with primitive `BoxCollider`/`CylinderCollider` geometry in Story `2-1`. |
| Pooled `NetworkProjectile` drawing a 1-frame ghost trail across the arena when recycled at a new muzzle position | Medium | Medium | Disable `TrailRenderer.emitting` and call `TrailRenderer.Clear()` after `transform.SetPositionAndRotation` inside `OnPoolAcquire()` (Story `4-3`). |
| Should Have stories (`3-3`, `4-3`, `5-3`, `6-3`) slipping and delaying Sprint 002 Feature stories that depend on them | Low | Medium | All 4 Should Have stories are scoped at `0.5d` (`2.0d` total) and backed by a `2.8d` (20%) sprint buffer plus `0.2d` unallocated slack. |

## Dependencies on External Factors
- **Photon Fusion 2 SDK & Realtime Cloud**: Requires active Photon AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9` configured in `PhotonAppSettings` for `GameMode.Shared` (`60Hz`) multi-client testing.
- **Unity Input System (`com.unity.inputsystem` 1.11+)**: Required for `InputAction` JSON override persistence and `NetworkInputData` polling.

## Definition of Done for this Sprint
- [x] All Must Have tasks completed (`14/14` Must Have + `4/4` Should Have = `18/18` Complete)
- [x] All tasks pass acceptance criteria
- [x] QA plan exists (`production/qa/qa-plan-sprint-001-2026-11-10.md`, from `/qa-plan sprint`)
- [x] All Logic/Integration stories have passing unit/integration tests (`49/49` EditMode + `7/7` PlayMode = `56/56 PASS`)
- [x] Smoke check passed (`/smoke-check sprint` -> `production/qa/smoke-2026-11-11.md`, `Verdict: PASS`)
- [x] QA sign-off report: APPROVED (`QL-TEST-COVERAGE: 18/18 ADEQUATE`, `LP-CODE-REVIEW: 18/18 APPROVE`)
- [x] No S1 or S2 bugs in delivered features
- [x] Design documents updated for any deviations (`0` deviations)
- [x] Code reviewed (`unity-specialist` + `qa-tester` + `lead-programmer`)
