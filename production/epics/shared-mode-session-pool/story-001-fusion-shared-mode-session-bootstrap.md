# Story 001: Photon Fusion 2 Shared Mode Session Bootstrap

> **Epic**: Photon Fusion 2 Shared Mode Session & Object Pool System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/shared-mode-session-pool.md`
**Requirement**: `TR-session-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Photon Fusion 2 Shared Mode Session & Zero-Alloc Network Object Pooling
**ADR Decision Summary**: Configures `NetworkRunner.StartGame(StartGameArgs)` in `GameMode.Shared` at `60Hz` tick rate (`PlayerCount = 4`, AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9`) and spawns the local player's `NetworkObject` inside `OnPlayerJoined` when `player == runner.LocalPlayer`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: In Photon Fusion 2 Shared Mode, only the client where `player == runner.LocalPlayer` may call `runner.Spawn(playerPrefab, spawnPos, spawnRot, player)` so that client receives `StateAuthority`.

**Control Manifest Rules (this layer)**:
- Required: Initialize `StartGameArgs` with `GameMode = GameMode.Shared`, `PlayerCount = 4`, and `ObjectProvider = _poolProvider`, and guard player spawning with `if (player == runner.LocalPlayer)`.
- Forbidden: Never use `GameMode.Host`, `GameMode.Server`, or `GameMode.Client`, and never spawn another player's avatar on a remote peer.
- Guardrail: Connection timeout is capped at `10.0s` with clean `Runner.Shutdown()` cleanup on disconnect.

---

## Acceptance Criteria

*From GDD `design/gdd/shared-mode-session-pool.md`, scoped to this story:*

- [x] `FusionSessionManager` builds `StartGameArgs` with `GameMode = GameMode.Shared`, `SessionName` (validated `[A-Za-z0-9_-]{3,16}` or empty for Quick Join), `PlayerCount = 4`, `SceneManager`, and `ObjectProvider = _poolProvider` using Photon AppId `fbee3c4f-1f3b-42da-80b9-0e4d2b47f3a9` at `60Hz` tick rate.
- [x] In `OnPlayerJoined(NetworkRunner runner, PlayerRef player)`, when `player == runner.LocalPlayer`, `FusionSessionManager` queries `ArenaSpawnManager` with live opponent positions (`DamageReceiverRegistry.CopyLivingPositionsNonAlloc`) for the optimal spawn transform and spawns the local player's `NetworkObject` with `HasStateAuthority == true`.
- [x] If connection exceeds `10.0s` or `OnShutdown` fires unexpectedly, `FusionSessionManager` cleanly disposes the disposable child `FusionSessionRunner` `NetworkRunner` and raises `OnSessionDisconnected(string reason)` once for the UI layer.

---

## Implementation Notes

*Derived from ADR-0003 Implementation Guidelines:*

Create `Assets/Scripts/Networking/FusionSessionManager.cs` implementing `INetworkRunnerCallbacks`. Host each single-use `NetworkRunner` on a disposable child `FusionSessionRunner` GameObject so `Shutdown(destroyGameObject: true)` never destroys `FusionSessionManager`. Expose `BuildStartGameArgs(...)` and `ValidateRoomCode(...)` as testable builder methods so PlayMode tests can verify `GameMode.Shared`, `PlayerCount == 4`, room code validation (`^[A-Za-z0-9_-]{3,16}$`), `10.0s` timeout watchdog, and `ObjectProvider` assignment deterministically.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-network-object-pool-provider.md`): `FusionObjectPoolProvider : INetworkObjectProvider` pre-warming and queue management.
- Story 003 (`story-003-pooled-projectile-lifecycle-reset.md`): `NetworkProjectile` 120-tick expiry and trail/velocity reset on pool acquire/release.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Networking/FusionSessionManager.cs`
- `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `FusionSessionManager` starts Photon Fusion 2 in `GameMode.Shared` at `60Hz` tick rate with `MaxPlayers=4` and spawns local player via `OnPlayerJoined`.
  - Given: `FusionSessionManager` configured with `DefaultCapsuleCombatConfig` and `FusionObjectPoolProvider`.
  - When: `BuildStartGameArgs("ARENA01", true, 4, poolProvider, sceneManager)` is invoked and `OnPlayerJoined` triggers for `runner.LocalPlayer`.
  - Then: `StartGameArgs.GameMode == GameMode.Shared`, `PlayerCount == 4`, `ObjectProvider != null`, room name `"ARENA01"` validates true, and local player spawn grants `Object.HasStateAuthority == true`.
  - Edge cases: Room full (`5th` join attempt returns `ShutdownReason.GameIsFull` and notifies UI cleanly); invalid room name (`"ab"` or `"bad!name"`) is rejected before calling `StartGame`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/arena-level-architecture/story-002-max-min-euclidean-spawn-selector.md`
- Unlocks: `production/epics/shared-mode-session-pool/story-002-network-object-pool-provider.md`, `production/epics/match-manager-authority-failover/story-001-networked-match-state-machine.md`, `production/epics/keybinding-rebind-lobby-ui/story-001-lobby-flow-and-room-waiting-ui.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` (`7/7` PlayMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) PlayMode (`7/7 PASS`), EditMode (`49/49 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
