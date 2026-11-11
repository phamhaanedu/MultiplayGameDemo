# Sprint 002 — 2026-11-25 to 2026-12-08

> **Producer Feasibility Review (PR-SPRINT)**: REALISTIC (2026-11-10)
> **QA Plan**: `production/qa/qa-plan-sprint-002-2026-11-25.md`

## Sprint Goal
Hoàn thiện bản **Vertical Slice** chơi được trọn vẹn từ đầu đến cuối cho 4 người chơi: hệ thống chiến đấu đa hướng zero-alloc (**Chém dọc `LMB`**, **Chém ngang xoay thân người `90 deg` `Phím 1/3`** trong `11 ticks`, **Bắn đạn tầm xa `RMB`** qua Object Pool), hệ thống Camera kép **`FPS <-> TPS` (`Phím V`)** chống xuyên tường + **Ghost Spectator**, bộ điều phối vòng đời trận đấu **`MatchManager`** (`Waiting -> Countdown -> Playing -> Ended`) kèm tự động kế thừa quyền **`MasterClient Failover`**, giao diện **World-Space Overhead Billboard** + **Cụm 7 nút cảm ứng Mobile Action Pad** + **Combat HUD**, và luồng **Lobby + Bảng cài đặt gán lại 13 phím (`KeybindingRebindModal`)**.

## Goal Prerequisites
| Needed to show the goal | Status |
|-------------------------|--------|
| `Assets/Scenes/Arena.unity` & `DefaultCapsuleCombatConfig.asset` | `built by Sprint 001 ([2-1], [1-3])` |
| `FusionSessionManager`, `FusionObjectPoolProvider` & `NetworkProjectile` | `built by Sprint 001 ([4-1], [4-2], [4-3])` |
| `PlayerMobilityController` & `PlayerHealth` (`Rpc_RequestApplyDamage` + `Layer 10` Ghost transition) | `built by Sprint 001 ([5-1..5-3], [6-1..6-3])` |
| `LobbyViewController` (main entry UI & 4-slot waiting room overlay) | `built by [11-1]` (`story-001-lobby-flow-and-room-waiting-ui.md`) |

## Milestone Context
- Current Milestone: Milestone 2 — Combat, Dual Camera, MatchManager & HUD/Lobby Vertical Slice (Epics 7–11)
- Milestone Deadline: 2026-12-08
- Sprints Remaining: 2 (Sprint 002: Feature/UI Vertical Slice; Sprint 003: Social/Cloud/Polish Release)

## Capacity
- Total days: 14.0
- Buffer (20%): 2.8 days reserved for unplanned work
- Available: 11.2 days (Planned: 11.0 days across 15 stories)

## Tasks

### Must Have (Critical Path)
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| `7-1` | [Story 001: Zero-Alloc Melee Sector & Line-of-Sight Detection](../epics/combat-melee-ranged-system/story-001-zero-alloc-melee-sector-detection.md) (`TR-combat-001`) | `gameplay-programmer` | 1.0 | `1-2, 2-3, 6-2` | `OverlapSphereNonAlloc` (`Collider[8]`), planar dot-product against `0.9063078` (`50 deg`, `2.5m`, `25 dmg`) & `0.7071068` (`90 deg` offset `+-45 deg`, `3.0m`, `30 dmg`), `Linecast` wall block |
| `7-2` | [Story 002: Horizontal Sweep `90 deg` Snap-Turn Interpolation](../epics/combat-melee-ranged-system/story-002-horizontal-sweep-90-snap-turn.md) (`TR-combat-002`) | `gameplay-programmer` | 0.75 | `5-3, 7-1` | `Keys 1/3` rotate authoritative character & camera yaw by `-90 deg` or `+90 deg` over `11 ticks` (`0.18s`) while locking manual mouse yaw |
| `7-3` | [Story 003: Ranged Projectile Fire (`RMB`) & Continuous Hit Detection](../epics/combat-melee-ranged-system/story-003-ranged-projectile-fire-and-hit.md) (`TR-combat-003`) | `network-engineer` | 0.75 | `4-3, 7-1` | `RMB` (`0.4s` CD) spawns pooled `NetworkProjectile` (`15.0 m/s`, `30.0m` max) with per-tick `SphereCast` (`r=0.15m`) dealing `15` dmg on `Layer 8` and stopping on `Layer 6` |
| `8-1` | [Story 001: Dual-Perspective Camera Toggle (`FPS` / `TPS` via `Key V`)](../epics/camera-fps-tps-spectator/story-001-dual-fps-tps-camera-toggle.md) (`TR-camera-001`) | `gameplay-programmer` | 0.75 | `7-2` | `Key V` toggles `TPS` (`Dist=4.5m, Height=1.8m, Shoulder=+0.45m`) and `FPS` (`EyeHeight=1.5m`, local body `ShadowCastingMode.ShadowsOnly`) in `LateUpdate()` |
| `8-2` | [Story 002: TPS Camera `SphereCast` Wall Collision Anti-Clip](../epics/camera-fps-tps-spectator/story-002-tps-camera-spherecast-wall-collision.md) (`TR-camera-002`) | `gameplay-programmer` | 0.5 | `2-3, 8-1` | `SphereCast` (`r=0.25m`) against `LineOfSightMask (Layer 6)` clamps boom length to `[0.6m, 4.5m]` while ignoring `Layer 8` and `Layer 10` |
| `9-1` | [Story 001: Networked Match State Machine (`Waiting` -> `Countdown` -> `Playing` -> `Ended`)](../epics/match-manager-authority-failover/story-001-networked-match-state-machine.md) (`TR-match-001`) | `network-engineer` | 0.75 | `4-1` | `MatchManager` drives `Waiting -> Countdown (3.0s) -> Playing (180.0s) -> Ended (8.0s)` when `ConnectedPlayers >= 2` and aborts countdown if `< 2` |
| `9-2` | [Story 002: Last Man Standing Win Evaluator & Sudden Death Tie-Breaker](../epics/match-manager-authority-failover/story-002-last-man-standing-win-evaluator.md) (`TR-match-002`) | `systems-designer` | 0.75 | `6-2, 9-1` | Crowns sole survivor (`AliveCount == 1`), resolves `180s` Sudden Death by `(CurrentHP DESC, Kills DESC)`, and declares `DRAW` on same-tick double KO |
| `9-3` | [Story 003: Shared Mode MasterClient StateAuthority Failover](../epics/match-manager-authority-failover/story-003-masterclient-state-authority-failover.md) (`TR-match-003`) | `network-engineer` | 0.75 | `9-2` | `IStateAuthorityChanged` + `OnPlayerLeft` migrates `MatchManager` authority to lowest remaining `PlayerRef.PlayerId` within `<= 2` ticks without resetting `PhaseTimer` |
| `10-1` | [Story 001: World-Space Overhead Billboard UI (`OverheadBillboardUI`)](../epics/combat-hud-mobile-action-pad/story-001-world-space-overhead-billboard-ui.md) (`TR-hud-001`) | `ui-programmer` | 0.5 | `6-1` | World-space canvas (`Y=+2.35m`) faces `MainCamera` in `LateUpdate()`, scales `clamp(d/8m, 0.75, 1.35)`, updates dual-fill HP bar on `OnHealthChanged` |
| `10-2` | [Story 002: 7-Button Mobile Action Pad & Virtual Joystick](../epics/combat-hud-mobile-action-pad/story-002-seven-button-mobile-action-pad.md) (`TR-hud-002`) | `ui-programmer` | 0.75 | `3-2` | Bottom-right 7-button radial pad (`>= 84x84px`) + bottom-left joystick driving `MobileInputBridge` via `IPointerDownHandler`/`Up` with radial cooldowns & dynamic key badges |
| `11-1` | [Story 001: Lobby Flow, Room Code Validation & Waiting Room UI](../epics/keybinding-rebind-lobby-ui/story-001-lobby-flow-and-room-waiting-ui.md) (`TR-rebind-001`) | `ui-programmer` | 1.0 | `4-1` | `LobbyViewController` validates `DisplayName` & `RoomCode`, locks buttons during handshake, renders 4-slot waiting roster, and shows error toasts on timeout |

### Should Have
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| `8-3` | [Story 003: Post-Death Ghost Free-Roam & Follow-Target Spectator](../epics/camera-fps-tps-spectator/story-003-post-death-ghost-spectator-controller.md) (`TR-camera-003`) | `gameplay-programmer` | 0.75 | `6-3, 8-2` | Ghost Free-Roam (`9.0 m/s` on `Layer 10`, clamped `[-22m, +22m]`) and Follow-Target survivor cycling (`LMB`/`RMB`/`Space`) with auto-switch on target death |
| `10-3` | [Story 003: Event-Driven Screen-Space Combat HUD & Match End Overlay](../epics/combat-hud-mobile-action-pad/story-003-event-driven-screen-space-combat-hud.md) (`TR-hud-003`) | `ui-programmer` | 0.75 | `8-3, 9-3, 10-1` | Event-driven local HP bar, cooldown bar, crosshair hit-marker, 4-row pooled kill feed, and Victory/Defeat/Draw end modal (`0 B` steady-state GC) |
| `11-2` | [Story 002: Interactive 13-Action Keybinding Rebind Modal](../epics/keybinding-rebind-lobby-ui/story-002-interactive-keybinding-rebind-modal.md) (`TR-rebind-002`) | `ui-programmer` | 0.75 | `3-1, 3-3, 11-1` | `PerformInteractiveRebinding()` for 13 actions (`5.0s` timeout, `Escape` cancel, conflict swap with `#FFB703` highlight, `Reset to Defaults`) under `InputFocusFlag.RebindModalOpen` |
| `11-3` | [Story 003: Mobile Action Pad Visibility Mode Toggle (`Auto / ForceOn / ForceOff`)](../epics/keybinding-rebind-lobby-ui/story-003-mobile-pad-visibility-mode-toggle.md) (`TR-rebind-003`) | `ui-programmer` | 0.5 | `10-2, 11-2` | 3-way segmented control (`Auto / ForceOn / ForceOff`) persisted in `keybindings.json` and immediately toggling `MobileActionPadView` `CanvasGroup` |

### Nice to Have
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| — | *None — all 15 Feature & Core Presentation stories are allocated to Must Have and Should Have.* | — | 0.0 | — | — |

## Carryover from Previous Sprint
| Task | Reason | New Estimate |
|------|--------|-------------|
| *Populated at Sprint 001 close if any Sprint 001 story is incomplete* | — | — |

## Risks
| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Camera disorientation during `90 deg` Horizontal Sweep (`Keys 1/3`) in `FPS` mode | Medium | High | Interpolate authoritative yaw smoothly across `11` network ticks (`0.18s`, `~8.18 deg/tick`) while locking manual mouse yaw delta (Story `7-2`). |
| MasterClient disconnecting at the exact moment the 2nd-to-last survivor dies | Low | High | Reconcile `MatchRoster` against `Runner.ActivePlayers` immediately inside `StateAuthorityChanged()` and re-run `EvaluateWinConditions()` on the new MasterClient (Story `9-3`). |
| Mobile Action Pad touch events firing behind open Settings/Rebind modals | Low | Medium | Gate `MobileInputBridge` and `MobileActionPadView.blocksRaycasts` via `InputFocusManager.IsGameplayInputAllowed` (Stories `10-2`, `11-2`). |

## Dependencies on External Factors
- Completion of Sprint 001 (`Foundation` & `Core` Epics 1–6).
- Unity UI (`uGUI` + `TextMeshPro`) package for Screen-Space and World-Space HUD canvases.

## Definition of Done for this Sprint
- [x] All Must Have tasks completed (`11/11` Must-Have + `4/4` Should-Have = `15/15` Complete)
- [x] All tasks pass acceptance criteria (`45/45` ACs verified)
- [x] QA plan exists (`production/qa/qa-plan-sprint-002-2026-11-25.md`, from `/qa-plan sprint`)
- [x] All Logic/Integration stories have passing unit/integration tests (`69/69` EditMode + `8/8` PlayMode PASS)
- [x] Smoke check passed (`production/qa/smoke-sprint-002-2026-11-11.md` — `Verdict: PASS`)
- [x] QA sign-off report: APPROVED (`QL-TEST-COVERAGE: 15/15 ADEQUATE`, SHA-1 `stamp.txt` receipts verified)
- [x] No S1 or S2 bugs in delivered features
- [x] Design documents updated for any deviations (`None`)
- [x] Code reviewed (`LP-CODE-REVIEW: 15/15 APPROVE`)
