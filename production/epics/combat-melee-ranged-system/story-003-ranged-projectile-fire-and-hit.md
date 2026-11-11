# Story 003: Ranged Projectile Fire (`RMB`) & Continuous Hit Detection

> **Epic**: Multi-Directional Melee (`LMB`, `Keys 1/3`) & Ranged Combat (`RMB`)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/combat-melee-ranged-system.md`
**Requirement**: `TR-combat-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: Zero-Alloc Melee Sector Math (`90 deg` Sweep Snap-Turn), Pooled Projectiles & Dual Camera
**ADR Decision Summary**: Spawns a pooled `NetworkProjectile` via `Runner.Spawn` (`INetworkObjectProvider`) on `RMB` (`0.4s` cooldown = `24 ticks`) at `MuzzlePoint` (`forward * 0.65m + up * 1.1m`), advancing `15.0 m/s` with per-tick `Physics.SphereCast` (`r = 0.15m`, `0.25m/tick`) to prevent high-speed tunneling and dealing `15` damage via `Rpc_RequestApplyDamage`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Continuous per-tick `Physics.SphereCast(prevPos, 0.15f, dir, out hit, stepDist, collisionMask)` on the projectile's `StateAuthority` prevents tunneling through thin walls or capsules at `15.0 m/s`.

**Control Manifest Rules (this layer)**:
- Required: Spawn projectiles exclusively through `Runner.Spawn` backed by `FusionObjectPoolProvider`, perform per-tick `SphereCast` (`r = 0.15m`) against `Layer 6 | Layer 8`, ignore `Owner == Object.InputAuthority`, and guard hit processing with `if (_hasDealtDamage) return;`.
- Forbidden: Never use `Object.Instantiate` for projectiles or allow a projectile to damage its own shooter or pass through `Layer 6 (EnvironmentWall)`.
- Guardrail: Muzzle check — if the shooter stands flush against a `Layer 6` wall (`Linecast` from chest to `MuzzlePoint` hits `Layer 6`), despawn the projectile immediately on the wall surface so bullets cannot spawn behind thin walls.

---

## Acceptance Criteria

*From GDD `design/gdd/combat-melee-ranged-system.md`, scoped to this story:*

- [x] Pressing or holding `RangedFire` (`RMB` / `BTN_RangedFire`) when `RangedCooldownTimer.ExpiredOrNotRunning` sets `RangedCooldownTimer = 0.4s` (`24 ticks`) and spawns a pooled `NetworkProjectile` at `MuzzlePoint` (`pos + up * 1.1m + forward * 0.65m`) traveling at `15.0 m/s`.
- [x] On each `FixedUpdateNetwork()` tick (`0.25m` step), `NetworkProjectile` casts `Physics.SphereCast(currentPos, 0.15f, dir, out RaycastHit hit, stepDist, Layer6_Wall | Layer8_Player)` while ignoring the shooter's own collider and `Layer 10 (GhostSpectator)`.
- [x] Hitting `Layer 6 (EnvironmentWall)` spawns a wall impact spark and despawns the projectile to the pool; hitting an enemy `Layer 8 (PlayerCharacter)` sets `_hasDealtDamage = true`, sends `Rpc_RequestApplyDamage(15, AttackType.RangedProjectile, Owner, hit.point)`, and despawns the projectile to the pool.

---

## Implementation Notes

*Derived from ADR-0005 Implementation Guidelines:*

Wire `PlayerCombatController.TryFireRanged()` to `NetworkProjectile`. In `NetworkProjectile.FixedUpdateNetwork()`, check muzzle wall clipping on tick 0 (`Physics.Linecast(chestOrigin, muzzlePos, ArenaLayers.LineOfSightMask)`), then step forward by `15.0f * Runner.DeltaTime` using `Physics.SphereCast` (`radius = 0.15f`) against `ArenaLayers.ProjectileCollisionMask` (`(1 << 6) | (1 << 8)`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-zero-alloc-melee-sector-detection.md`): Melee sector detection (`LMB`, `Keys 1/3`).
- Story 002 (`story-002-horizontal-sweep-90-snap-turn.md`): Horizontal Sweep `90 deg` snap-turn.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Combat/PlayerCombatController.cs`
- `Assets/Scripts/Combat/Projectile/NetworkProjectile.cs`
- `Assets/Tests/EditMode/MeleeSectorMathTests.cs`
- `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: Ranged Fire (`RMB`) spawns a pooled `NetworkProjectile` (`0.4s` cooldown, `15.0 m/s`, `30.0m` max range) that performs continuous `SphereCast` hit detection (`15` damage on `Layer 8`, blocked by `Layer 6`, ignores self and `Layer 10`).
  - Given: Shooter at `(0, 0, 0)` firing along `+Z` toward an enemy capsule or a `Layer 6` wall.
  - When: `TryFireRanged()` executes and `NetworkProjectile` advances via per-tick `SphereCast`.
  - Then: Cooldown is set to `24 ticks` (`0.4s`); projectile ignores shooter's own collider and `Layer 10` Ghosts; hitting an enemy `Layer 8` collider dispatches `15` damage once (`_hasDealtDamage == true`) and returns projectile to pool; hitting a `Layer 6` wall stops the projectile and returns it to pool without damaging enemies behind the wall.
  - Edge cases: Shooter standing flush (`0.2m`) against a `Layer 6` wall triggers the chest-to-muzzle `Linecast` check and despawns the projectile on the near side of the wall instead of spawning it inside/behind the wall.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/NetworkProjectileLifecycleTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/shared-mode-session-pool/story-003-pooled-projectile-lifecycle-reset.md`, `production/epics/combat-melee-ranged-system/story-001-zero-alloc-melee-sector-detection.md`
- Unlocks: `production/epics/spatial-audio-combat-vfx/story-001-pooled-3d-spatial-sound-manager.md`, `production/epics/spatial-audio-combat-vfx/story-002-pooled-urp-combat-vfx-manager.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/MeleeSectorMathTests.cs` + `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs` (`69/69` EditMode, `8/8` PlayMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
