# Sprint 003 — 2026-12-09 to 2026-12-22

> **Producer Feasibility Review (PR-SPRINT)**: REALISTIC (2026-11-10)
> **QA Plan**: `production/qa/qa-plan-sprint-003-2026-12-09.md`

## Sprint Goal
Hoàn thiện toàn bộ phạm vi **Alpha & Full Vision Release** của dự án: hệ thống **In-Game Public & Private Whisper Chat (`[RpcTarget]`)** bền vững bằng `CanvasGroup` kèm bộ giới hạn tốc độ Token-Bucket (`5 msgs / 3.0s`) và hồ chứa 50 dòng tin nhắn, tích hợp đám mây **PlayFab Cloud Identity (`LoginWithCustomID` + `OfflineGuest` fallback), đồng bộ chỉ số trận đấu bất biến (`Idempotent Stats`) & Bảng xếp hạng Top 20 (`15.0s` TTL cache)**, cùng hệ thống **Âm thanh không gian 3D (`16` pooled emitters), hiệu ứng chiến đấu URP (`20` pooled VFX instances) và phản hồi thị giác `MaterialPropertyBlock` Hit-Flash + DOTween Squash & Stretch**.

## Goal Prerequisites
| Needed to show the goal | Status |
|-------------------------|--------|
| `Arena.unity`, `InputFocusManager`, `MatchManager`, `PlayerCombatController`, `PlayerHealth` | `built by Sprints 001–002` |
| `PlayFabAuthService` (`playfab_identity.json` + `OfflineGuest` fallback when `TitleId` is unconfigured/offline) | `built by [13-1]` (`story-001-playfab-auth-and-offline-guest-fallback.md`) |
| `SoundManager` & `VfxManager` procedural/pooled fallback assets | `built by [14-1], [14-2]` (`story-001-pooled-3d-spatial-sound-manager.md`, `story-002-pooled-urp-combat-vfx-manager.md`) |

## Milestone Context
- Current Milestone: Milestone 3 — Social Chat, PlayFab Cloud Leaderboards & Sensory Audio/VFX Polish (Epics 12–14)
- Milestone Deadline: 2026-12-22
- Sprints Remaining: 1 (Final Release Sprint; includes `3.45` days of unallocated engineering capacity + `2.8` buffer days for 4-client multiplayer soak testing and release stabilization)

## Capacity
- Total days: 14.0
- Buffer (20%): 2.8 days reserved for unplanned work
- Available: 11.2 days (Planned: 7.75 days across the remaining 9 stories + 3.45 days reserved for 4-client soak testing & release hardening)

## Tasks

### Must Have (Critical Path)
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| `12-1` | [Story 001: CanvasGroup Persistent Chat Window & Focus Toggle](../epics/in-game-chat-system/story-001-canvasgroup-persistent-chat-window.md) (`TR-chat-001`) | `ui-programmer` | 0.75 | `3-3` | `ChatViewController` uses `CanvasGroup` (`alpha`, `interactable`, `blocksRaycasts`) without ever calling `SetActive(false)` and toggles `InputFocusFlag.ChatFocused` on `Enter`/`Escape` |
| `12-2` | [Story 002: Public Broadcast & Private Whisper (`[RpcTarget]`) Chat Router](../epics/in-game-chat-system/story-002-public-and-whisper-chat-rpc-router.md) (`TR-chat-002`) | `network-engineer` | 1.0 | `12-1` | Parses `/w <TargetName> <Msg>`, strips `<`/`>` rich-text tags, clamps `<= 120` chars, broadcasts Public (`#F8F9FA`) and routes Whispers (`#C77DFF`) exclusively via `[RpcTarget] PlayerRef` |
| `13-1` | [Story 001: PlayFab CustomID Auth & Offline Guest Mode Fallback](../epics/playfab-cloud-leaderboard/story-001-playfab-auth-and-offline-guest-fallback.md) (`TR-playfab-001`) | `network-engineer` | 1.0 | None | `LoginWithCustomID` with persistent GUID in `playfab_identity.json`, exponential backoff (`1s, 2s, 4s`), and non-blocking `OfflineGuest` fallback |
| `13-2` | [Story 002: Idempotent Match Stats Submission (`TotalWins`, `TotalKills`, `MatchesPlayed`)](../epics/playfab-cloud-leaderboard/story-002-idempotent-match-stats-submission.md) (`TR-playfab-002`) | `systems-designer` | 1.0 | `9-2, 13-1` | Batched `UpdatePlayerStatistics` once on `MatchState.Ended` guarded by `_lastSubmittedMatchId`, kills clamped `[0, 15]`, and 1-slot offline pending queue |
| `14-1` | [Story 001: Pooled 3D Spatial Sound Manager (`SoundManager`)](../epics/spatial-audio-combat-vfx/story-001-pooled-3d-spatial-sound-manager.md) (`TR-audio-001`) | `sound-designer` | 0.75 | `7-3` | Pre-warms `16` 3D `AudioSource` emitters (`spatialBlend=1.0`, `min=2.5m`, `max=28.0m`, `Linear` rolloff, pitch `+-0.07`), `30m` culling, and oldest-voice stealing |
| `14-2` | [Story 002: Pooled URP Combat VFX Manager (`VfxManager`)](../epics/spatial-audio-combat-vfx/story-002-pooled-urp-combat-vfx-manager.md) (`TR-audio-002`) | `technical-artist` | 1.0 | `14-1` | Pre-warms `20` URP VFX instances (`50 deg / 2.5m` `#00F5D4` Vertical Slash arc, `90 deg / 3.0m` `#FF9F1C` Horizontal Sweep arc, impact sparks) with `0` runtime `Instantiate`/`Destroy` |

### Should Have
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| `12-3` | [Story 003: Token-Bucket Chat Rate Limiter & 50-Row Ring Buffer Pool](../epics/in-game-chat-system/story-003-token-bucket-rate-limiter-and-row-pool.md) (`TR-chat-003`) | `ui-programmer` | 0.75 | `12-2` | Token-bucket rate limiter (`5 msgs / 3.0s`) and `50`-row pre-warmed `ChatMessageRow` ring buffer recycling oldest row in `O(1)` |
| `13-3` | [Story 003: Cached Top-20 Leaderboard Modal View (`15.0s` TTL)](../epics/playfab-cloud-leaderboard/story-003-cached-leaderboard-modal-view.md) (`TR-playfab-003`) | `ui-programmer` | 0.75 | `13-2` | `GetLeaderboard` Top 20 (`TotalWins`, `TotalKills`) with `15.0s` cache TTL and `20` pre-warmed `LeaderboardRowView` UI items |
| `14-3` | [Story 003: `MaterialPropertyBlock` Hit-Flash & DOTween Squash/Stretch Feedback](../epics/spatial-audio-combat-vfx/story-003-mpb-hitflash-and-dotween-squash-feedback.md) (`TR-audio-003`) | `technical-artist` | 0.75 | `5-3, 14-2` | `0.12s` white Hit-Flash via cached `MaterialPropertyBlock` (`0` material clones) and `0.15s` Squash & Stretch on child `VisualRoot` with `DOKill(true)` reset to `(1,1,1)` |

### Nice to Have
| ID | Task | Agent/Owner | Est. Days | Dependencies | Acceptance Criteria |
|----|------|-------------|-----------|-------------|-------------------|
| — | *None — all 9 remaining Ready stories in the project backlog are scheduled in Must Have and Should Have.* | — | 0.0 | — | — |

## Carryover from Previous Sprint
| Task | Reason | New Estimate |
|------|--------|-------------|
| *Populated at Sprint 002 close if any Sprint 002 story is incomplete* | — | — |

## Risks
| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| PlayFab API rate limiting (`HTTP 429`) or missing TitleId in local grader test environments | Medium | Medium | Enforce `15.0s` client cache TTL on `GetLeaderboard` (Story `13-3`), batch stats into `1` call per match (Story `13-2`), and auto-fallback to `OfflineGuest` mode when offline or unconfigured (Story `13-1`). |
| URP SRP Batcher breaking if `MeshRenderer.material` is accessed during Hit-Flash | Low | High | Strictly use `MeshRenderer.GetPropertyBlock` / `SetPropertyBlock` with cached `Shader.PropertyToID("_EmissionColor")` and verify `sharedMaterial` identity in EditMode tests (Story `14-3`). |
| Chat TMP Rich-Text injection (`<size=999>`) disrupting HUD readability | Low | Medium | Sanitize all chat strings in `ChatCommandParser` by stripping `<` and `>` characters before transmitting or rendering (Story `12-2`). |

## Dependencies on External Factors
- Completion of Sprint 001 and Sprint 002.
- **PlayFab C# SDK / REST API**: Optional live `TitleId` (`PlayFabSharedSettings`); `OfflineGuest` mode guarantees full functionality even without internet access.
- **DOTween (`DG.Tweening`)**: Used for `VisualRoot` Squash & Stretch and UI tweens.

## Definition of Done for this Sprint
- [x] All Must Have tasks completed (`6/6` Must Have + `3/3` Should Have = `9/9` Complete)
- [x] All tasks pass acceptance criteria
- [x] QA plan exists (`production/qa/qa-plan-sprint-003-2026-12-09.md`, from `/qa-plan sprint`)
- [x] All Logic/Integration stories have passing unit/integration tests (`80/80` EditMode + `8/8` PlayMode `PASS`)
- [x] Smoke check passed (`/smoke-check sprint` — `production/qa/smoke-sprint-003-2026-11-11.md`)
- [x] QA sign-off report: APPROVED (`QL-TEST-COVERAGE: 9/9 ADEQUATE`, `14/14` SHA-1 `stamp.txt` receipts `UNCHANGED`)
- [x] No S1 or S2 bugs in delivered features
- [x] Design documents updated for any deviations
- [x] Code reviewed (`LP-CODE-REVIEW: 9/9 APPROVE`, `unity-specialist: PASS`, `qa-tester: PASS`)
