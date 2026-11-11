# Story 003: Default Capsule Combat Config Asset & Balance Baseline

> **Epic**: Character & Combat Config Data (`CharacterCombatConfigSO`)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Config/Data
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/character-combat-config.md`
**Requirement**: `TR-config-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture
**ADR Decision Summary**: Provides a single canonical `DefaultCapsuleCombatConfig.asset` referenced by player, projectile, camera, and audio/VFX prefabs so combat balance is data-driven from one source of truth.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Serialized `.asset` YAML format in Unity 6.6 (`6000.6.x`) is deterministic and inspectable via `AssetDatabase.LoadAssetAtPath<CharacterCombatConfigSO>()` in EditMode tests.

**Control Manifest Rules (this layer)**:
- Required: Store canonical combat balance in `Assets/Data/Config/DefaultCapsuleCombatConfig.asset` and bind all prefabs to it as a single source of truth.
- Forbidden: Never hardcode magic combat numbers (`100`, `6.0f`, `18.0f`, `25`, `30`, `15`) inside gameplay scripts.
- Guardrail: All balance values must match the GDD §4 and §7 specification exactly.

---

## Acceptance Criteria

*From GDD `design/gdd/character-combat-config.md`, scoped to this story:*

- [x] `DefaultCapsuleCombatConfig.asset` exists in the project and loads cleanly without serialization warnings.
- [x] Health and movement baseline values match GDD exactly: `MaxHP == 100`, `MoveSpeed == 6.0f`, `SprintMultiplier == 1.35f`, `JumpHeight == 1.5f`, `Gravity == -19.62f`.
- [x] Mobility and combat baseline values match GDD exactly: `DashSpeed == 18.0f` (`0.20s`, `3.0s` CD), `DodgeSpeed == 12.0f` (`0.25s`, `1.5s` CD), `VerticalSlashDamage == 25` (`2.5m`, `50 deg`, `0.6s` CD), `HorizontalSweepDamage == 30` (`3.5m`, `90 deg` cone + `90 deg` snap-turn in `0.18s`, `1.2s` CD), `RangedDamage == 15` (`15.0m/s`, `30.0m` max range, `0.4s` CD).

---

## Implementation Notes

*Derived from ADR-0001 Implementation Guidelines:*

Create the serialized ScriptableObject asset at `Assets/Data/Config/DefaultCapsuleCombatConfig.asset` with all serialized fields populated to the exact canonical values in `design/gdd/character-combat-config.md` §7. Verify via EditMode test in `Assets/Tests/EditMode/CharacterCombatConfigTests.cs` that loading the asset returns exact matches for all canonical properties and derived caches.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-combat-config-encapsulation.md`): C# class structure and `OnValidate()` clamping logic.
- Story 002 (`story-002-precomputed-tick-and-cosine-cache.md`): `RecalculateDerivedValues()` math implementation.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Data/Config/DefaultCapsuleCombatConfig.asset`
- `Assets/Tests/EditMode/CharacterCombatConfigTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `DefaultCapsuleCombatConfig.asset` enforces canonical combat balance (`MaxHP=100`, `MoveSpeed=6.0`, `DashSpeed=18.0`, `DodgeSpeed=12.0`, `VerticalDamage=25`, `HorizontalDamage=30`, `RangedDamage=15`).
  - Given: The serialized `DefaultCapsuleCombatConfig.asset` loaded via `AssetDatabase` in EditMode.
  - When: All public properties are asserted against the GDD §7 tuning table.
  - Then: Every property matches exact GDD baseline (`MaxHP == 100`, `MoveSpeed == 6.0f`, `DashSpeed == 18.0f`, `DodgeSpeed == 12.0f`, `VerticalDamage == 25`, `HorizontalDamage == 30`, `RangedDamage == 15`, `RangedCooldown == 0.4f`, `RangedSpeed == 15.0f`, `RangedMaxDistance == 30.0m`).
  - Edge cases: Asset reload after domain reload preserves all serialized fields and derived caches without dirtying the asset.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Config/Data
**Required evidence**:
- Config/Data: smoke check pass (`production/qa/smoke-*.md`) and `Assets/Tests/EditMode/CharacterCombatConfigTests.cs`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-combat-config/story-002-precomputed-tick-and-cosine-cache.md`
- Unlocks: `production/epics/health-damage-rpc-system/story-001-victim-authoritative-networked-health.md`, `production/epics/combat-melee-ranged-system/story-001-zero-alloc-melee-sector-detection.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/CharacterCombatConfigTests.cs` & `production/qa/smoke-2026-11-11.md`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
