# Story 002: Zero-Alloc Network Object Pool Provider (`INetworkObjectProvider`)

> **Epic**: Photon Fusion 2 Shared Mode Session & Object Pool System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/shared-mode-session-pool.md`
**Requirement**: `TR-session-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Photon Fusion 2 Shared Mode Session & Zero-Alloc Network Object Pooling
**ADR Decision Summary**: Implements `FusionObjectPoolProvider : INetworkObjectProvider` with a pre-warmed FIFO `Queue<NetworkObject>` of `32` `NetworkProjectile` instances at scene load so runtime projectile spawning/despawning causes `0` `Object.Instantiate` or `Object.Destroy` calls.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Fusion 2 `INetworkObjectProvider.AcquirePrefabInstance` and `ReleaseInstance` must return `NetworkObjectAcquireResult.Success` and keep pooled objects deactivated (`SetActive(false)`) when idle.

**Control Manifest Rules (this layer)**:
- Required: Pre-warm `32` `NetworkProjectile` instances at scene load (`PrewarmPool()`) and route all `Runner.Spawn` / `Runner.Despawn` calls for projectiles through `FusionObjectPoolProvider`.
- Forbidden: Never call `Object.Instantiate` or `Object.Destroy` for projectiles, VFX, or SFX during active combat ticks.
- Guardrail: Peak simultaneous projectiles for 4 players (`4 * (2.0s / 0.4s) = 20`) must never exhaust the `32`-instance pre-warmed queue (`1.6x` safety headroom).

---

## Acceptance Criteria

*From GDD `design/gdd/shared-mode-session-pool.md`, scoped to this story:*

- [x] `FusionObjectPoolProvider` implements `INetworkObjectProvider` (`AcquirePrefabInstance` and `ReleaseInstance`) backed by FIFO `Queue<NetworkObject>` per prefab key with cached `IPooledResettable[]` hooks.
- [x] `PrewarmPool()` instantiates and deactivates `32` `NetworkProjectile` instances at scene load before combat begins (`AvailableCount == 32`).
- [x] Acquiring and releasing `20` concurrent projectiles in a loop reuses existing `NetworkObject` instances with `0` runtime `Instantiate`/`Destroy` calls and restores `AvailableCount == 32` upon release.

---

## Implementation Notes

*Derived from ADR-0003 Implementation Guidelines:*

Create `Assets/Scripts/Networking/Pooling/FusionObjectPoolProvider.cs` and `Assets/Scripts/Networking/Pooling/IPooledResettable.cs`. Provide direct testable methods `Prewarm(NetworkObject prefab, int count = 32)`, `AcquireFromPool(...)`, and `ReleaseToPool(...)` that `AcquirePrefabInstance` and `ReleaseInstance` delegate to, enabling fast EditMode unit testing of pool capacity, queue FIFO reuse, and fallback expansion cap (`MaxPoolCapacity = 48`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-fusion-shared-mode-session-bootstrap.md`): `FusionSessionManager` `StartGameArgs` connection lifecycle.
- Story 003 (`story-003-pooled-projectile-lifecycle-reset.md`): `NetworkProjectile` component state reset (`TrailRenderer.Clear()`, `TickTimer`, `Rigidbody` zeroing).

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Networking/Pooling/IPooledResettable.cs`
- `Assets/Scripts/Networking/Pooling/FusionObjectPoolProvider.cs`
- `Assets/Tests/EditMode/FusionObjectPoolProviderTests.cs`
- `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `FusionObjectPoolProvider` implements `INetworkObjectProvider` and pre-warms `32` `NetworkProjectile` instances at scene load with zero runtime `Instantiate`/`Destroy` in combat.
  - Given: `FusionObjectPoolProvider` initialized at scene load with a projectile prefab.
  - When: `Prewarm()` runs, followed by `20` sequential `AcquireFromPool()` calls and `20` `ReleaseToPool()` calls.
  - Then: Queue starts with `32` inactive instances, drops to `12` during peak burst (`20` active), returns to `32` after release, and performs `0` `Object.Instantiate` or `Object.Destroy` calls during the acquire/release cycle.
  - Edge cases: Pool exhaustion beyond `32` gracefully expands up to hard cap `48` with a warning log instead of crashing; `49th` returns `Failed`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/FusionObjectPoolProviderTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/shared-mode-session-pool/story-001-fusion-shared-mode-session-bootstrap.md`
- Unlocks: `production/epics/shared-mode-session-pool/story-003-pooled-projectile-lifecycle-reset.md`, `production/epics/combat-melee-ranged-system/story-003-ranged-projectile-fire-and-hit.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/FusionObjectPoolProviderTests.cs` (`49/49` EditMode PASS) & `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` (`7/7` PlayMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode, PlayMode, and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
