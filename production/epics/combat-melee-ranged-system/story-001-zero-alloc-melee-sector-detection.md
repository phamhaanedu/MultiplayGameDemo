# Story 001: Zero-Alloc Melee Sector & Line-of-Sight Detection

> **Epic**: Multi-Directional Melee (`LMB`, `Keys 1/3`) & Ranged Combat (`RMB`)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/combat-melee-ranged-system.md`
**Requirement**: `TR-combat-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: Zero-Alloc Melee Sector Math (`90 deg` Sweep Snap-Turn), Pooled Projectiles & Dual Camera
**ADR Decision Summary**: Evaluates Vertical Slash (`LMB`, `25 dmg`, `2.5m`, `50 deg` cone, `0.6s` CD) and Horizontal Sweep (`Keys 1/3`, `30 dmg`, `3.0m`, `90 deg` offset cone, `1.2s` CD) using a pre-allocated `Collider[8]` buffer with `Physics.OverlapSphereNonAlloc`, planar dot-product against cached cosine thresholds, and `Physics.Linecast` on `ArenaLayers.LineOfSightMask (1 << 6)`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: `Physics.OverlapSphereNonAlloc` with a pre-allocated `Collider[8]` buffer and precomputed `CosHalfAngle` from `CharacterCombatConfigSO` guarantees `0 B` heap allocation per swing.

**Control Manifest Rules (this layer)**:
- Required: Use `Physics.OverlapSphereNonAlloc(origin, range, _hitBuffer, ArenaLayers.PlayerCharacterMask)` (`_hitBuffer = new Collider[8]`), planar XZ dot-product against `VerticalSlashCosHalfAngle (0.9063078)` / `HorizontalSweepCosHalfAngle (0.7071068)`, and `Physics.Linecast` against `ArenaLayers.LineOfSightMask (1 << 6)`.
- Forbidden: Never use `Physics.OverlapSphere` (allocating array), mesh trigger colliders for melee cones, or `Mathf.Acos`/`Mathf.Cos` per swing.
- Guardrail: Deduplicate hit targets per swing so a single target capsule never receives duplicate damage in the same swing.

---

## Acceptance Criteria

*From GDD `design/gdd/combat-melee-ranged-system.md`, scoped to this story:*

- [x] Vertical Slash (`LMB` / `BTN_SlashVertical`) checks `R_v = 2.5m` around `ForwardXZ` with `dot(ForwardXZ, DirToTargetXZ) >= 0.9063078f` (`cos 25 deg`), dealing `25` damage with a `0.6s` (`36 ticks`) cooldown.
- [x] Horizontal Sweep Left (`Key 1` / `BTN_SweepLeft`) and Right (`Key 3` / `BTN_SweepRight`) rotate the sector bisector by `-45 deg` (Left, spanning `[-90 deg, 0 deg]`) or `+45 deg` (Right, spanning `[0 deg, +90 deg]`) and check `R_h = 3.0m` with `dot(Axis_sweep, DirToTargetXZ) >= 0.7071068f` (`cos 45 deg`), dealing `30` damage with a `1.2s` (`72 ticks`) shared cooldown.
- [x] Every candidate target inside the distance and angular cone must pass `!Physics.Linecast(attackerChest, targetChest, ArenaLayers.LineOfSightMask)` (`y + 0.9m`) so enemies behind a `Layer 6` wall or pillar take `0` damage.

---

## Implementation Notes

*Derived from ADR-0005 Implementation Guidelines:*

Create `Assets/Scripts/Combat/MeleeSectorMath.cs` (pure static geometry evaluator for unit testing) and `Assets/Scripts/Combat/PlayerCombatController.cs`. Implement `public static bool IsTargetInsideSector(Vector3 attackerPos, Vector3 attackerForward, Vector3 targetPos, float maxRange, float cosHalfAngle, float bisectorYawOffsetDeg)` and combine it with `Physics.OverlapSphereNonAlloc` and `Physics.Linecast(origin, target, ArenaLayers.LineOfSightMask)` inside `PlayerCombatController.ExecuteMeleeAttack()`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-horizontal-sweep-90-snap-turn.md`): Post-swing `90 deg` character yaw rotation over `11 ticks` (`0.18s`).
- Story 003 (`story-003-ranged-projectile-fire-and-hit.md`): Ranged projectile firing (`RMB`) and projectile collision resolution.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Combat/MeleeSectorMath.cs`
- `Assets/Scripts/Combat/PlayerCombatController.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Tests/EditMode/MeleeSectorMathTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: Melee sector detection uses `OverlapSphereNonAlloc`, planar dot-product against `0.9063078` (`50 deg` Vertical) and `0.7071068` (`90 deg` Horizontal offset `+-45 deg`), and `Physics.Linecast` on `LineOfSightMask`.
  - Given: Attacker at `(0, 0, 0)` facing `+Z (0, 0, 1)` with `DefaultCapsuleCombatConfig`.
  - When: Evaluating Vertical Slash (`2.5m`, `+-25 deg`) and Horizontal Sweep Left/Right (`3.0m`, `[-90 deg, 0 deg]` / `[0 deg, +90 deg]`) against targets inside cone, outside cone (`26 deg` for Vertical, `+5 deg` for Left Sweep), beyond range (`3.1m`), and behind a `Layer 6` wall.
  - Then: Only targets inside both radial range and angular sector with unobstructed `LineOfSightMask` return `true`; targets outside angle, beyond range, or behind a `Layer 6` wall return `false`; `0 B` GC is allocated.
  - Edge cases: Target standing on the `0.4m` central altar (`deltaY = 0.4m`) still hits via XZ planar projection; multi-target hit inside one `90 deg` sweep damages all valid targets in the cone once.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/MeleeSectorMathTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-combat-config/story-002-precomputed-tick-and-cosine-cache.md`, `production/epics/arena-level-architecture/story-003-physics-layer-collision-matrix.md`, `production/epics/health-damage-rpc-system/story-002-damage-request-rpc-validation.md`
- Unlocks: `production/epics/combat-melee-ranged-system/story-002-horizontal-sweep-90-snap-turn.md`, `production/epics/combat-melee-ranged-system/story-003-ranged-projectile-fire-and-hit.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/MeleeSectorMathTests.cs` (`69/69` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
