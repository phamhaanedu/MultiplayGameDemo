# Story 003: Pooled NetworkProjectile Lifecycle & State Reset

> **Epic**: Photon Fusion 2 Shared Mode Session & Object Pool System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/shared-mode-session-pool.md`
**Requirement**: `TR-session-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Photon Fusion 2 Shared Mode Session & Zero-Alloc Network Object Pooling
**ADR Decision Summary**: Resets `TrailRenderer`, `Rigidbody` velocities, `_hasDealtDamage` guard, and `120-tick` (`2.0s`) expiry counter inside `OnPoolAcquire()` / `OnPoolRelease()` / `InitializeLaunch()` so recycled projectiles never draw ghost trails across the arena or deal duplicate damage.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: In Unity 6 (`6000.x`), `Rigidbody.linearVelocity` and `Rigidbody.angularVelocity` must be zeroed and `TrailRenderer.Clear()` called after placing at the new muzzle position before re-enabling `TrailRenderer.emitting`.

**Control Manifest Rules (this layer)**:
- Required: Implement `IPooledResettable` on `NetworkProjectile` to clear `TrailRenderer`, zero `Rigidbody.linearVelocity`/`angularVelocity`, reset `_hasDealtDamage = false`, and initialize the `120-tick` (`2.0s`) lifetime timer on launch.
- Forbidden: Never enable `TrailRenderer.emitting` before setting `transform.SetPositionAndRotation` on a pooled projectile.
- Guardrail: Every projectile must automatically despawn back to `FusionObjectPoolProvider` after `120 ticks` (`30.0m` at `15.0m/s`) or immediately upon hitting `Layer 6 (EnvironmentWall)` or `Layer 8 (PlayerCharacter)`.

---

## Acceptance Criteria

*From GDD `design/gdd/shared-mode-session-pool.md`, scoped to this story:*

- [x] `NetworkProjectile` implements `IPooledResettable` (`OnPoolAcquire` and `OnPoolRelease`), calling `TrailRenderer.Clear()`, keeping `emitting = false` until muzzle placement in `InitializeLaunch()`/`Spawned()`, zeroing `Rigidbody.linearVelocity` and `angularVelocity`, resetting `_hasDealtDamage = false`, and arming a `120-tick` (`2.0s`) lifetime counter.
- [x] During `FixedUpdateNetwork()` (on `HasStateAuthority`), `NetworkProjectile` advances at `15.0m/s * Runner.DeltaTime` (`0.25m/tick`) and despawns back to the pool when its `120-tick` timer expires (`30.0m` max distance).
- [x] If the owning player disconnects mid-flight or the projectile hits `Layer 6` / `Layer 8` (via O(1) zero-GC `DamageReceiverRegistry.TryGetByCollider`), `NetworkProjectile` despawns cleanly back to the pool without null reference exceptions or ghost trails.

---

## Implementation Notes

*Derived from ADR-0003 Implementation Guidelines:*

Create `Assets/Scripts/Combat/AttackType.cs`, `Assets/Scripts/Combat/IDamageReceiver.cs` (`DamageReceiverRegistry`), `Assets/Scripts/Combat/Projectile/NetworkProjectile.cs`, and `Assets/Prefabs/Combat/NetworkProjectile.prefab`. Keep `_trailRenderer.emitting = false` in `OnPoolAcquire()` at `(0,0,0)` and enable `emitting = true` only after `SetPositionAndRotation` at the muzzle inside `InitializeLaunch()` and `Spawned()`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-fusion-shared-mode-session-bootstrap.md`): `FusionSessionManager` connection startup.
- Story 002 (`story-002-network-object-pool-provider.md`): `FusionObjectPoolProvider` queue pre-warming.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Combat/AttackType.cs`
- `Assets/Scripts/Combat/IDamageReceiver.cs`
- `Assets/Scripts/Combat/Projectile/NetworkProjectile.cs`
- `Assets/Prefabs/Combat/NetworkProjectile.prefab`
- `Assets/Tests/EditMode/NetworkProjectileLifecycleTests.cs`
- `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `NetworkProjectile` lifecycle resets `TrailRenderer`, `Rigidbody` velocity, and `ExpiryTimer` on pool acquire/release and automatically despawns back to pool after `120 ticks` (`2.0s`) or collision.
  - Given: A pooled `NetworkProjectile` previously despawned at `(15, 1, 15)` and re-acquired at `(-10, 1, -10)`.
  - When: `OnPoolRelease()`, `OnPoolAcquire()`, and `InitializeLaunch()` execute, followed by `120` simulated `60Hz` ticks at `15.0 m/s`.
  - Then: `TrailRenderer.positionCount == 0` on muzzle placement (no ghost line across map), `Rigidbody.linearVelocity` resets cleanly, `_hasDealtDamage == false`, and the projectile triggers pool release on tick `120` (`30.0m` traveled).
  - Edge cases: Owner player disconnects while projectile is mid-flight (clean despawn without `NullReferenceException`); simultaneous wall and player hit in the same tick processes at most once (`_hasDealtDamage` guard).

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/NetworkProjectileLifecycleTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/shared-mode-session-pool/story-002-network-object-pool-provider.md`
- Unlocks: `production/epics/combat-melee-ranged-system/story-003-ranged-projectile-fire-and-hit.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/NetworkProjectileLifecycleTests.cs` (`49/49` EditMode PASS) & `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` (`7/7` PlayMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode, PlayMode, and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
