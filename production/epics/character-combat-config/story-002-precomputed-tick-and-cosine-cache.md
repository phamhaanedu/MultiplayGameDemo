# Story 002: Precomputed Tick & Cosine Threshold Cache

> **Epic**: Character & Combat Config Data (`CharacterCombatConfigSO`)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/character-combat-config.md`
**Requirement**: `TR-config-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture
**ADR Decision Summary**: Precomputes derived tick counts (`DashTicks = 12`, `DodgeTicks = 15`, `HorizontalTurnTicks = 11`, `ProjectileLifetimeTicks = 120`) and cosine half-angle thresholds (`VerticalSlashCosHalfAngle = 0.9063078`, `HorizontalSweepCosHalfAngle = 0.7071068`) in `OnEnable()` and `OnValidate()`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: `Mathf.RoundToInt` and `Mathf.Cos` executed once in `OnEnable()`/`OnValidate()` eliminate per-tick trigonometry and float division inside `FixedUpdateNetwork()`.

**Control Manifest Rules (this layer)**:
- Required: Precompute all derived tick counts and cosine half-angle thresholds inside `CharacterCombatConfigSO.OnEnable()` and `OnValidate()` (`RecalculateDerivedValues()`).
- Forbidden: Never call `Mathf.Cos`, `Mathf.Acos`, or float-to-tick division inside `60Hz` `FixedUpdateNetwork()` combat or mobility loops.
- Guardrail: Cached values must remain deterministic across all clients (`DashTicks == 12`, `DodgeTicks == 15`, `HorizontalTurnTicks == 11`, `ProjectileLifetimeTicks == 120`).

---

## Acceptance Criteria

*From GDD `design/gdd/character-combat-config.md`, scoped to this story:*

- [x] `RecalculateDerivedValues()` is invoked in both `OnEnable()` and `OnValidate()` on `CharacterCombatConfigSO`.
- [x] At `TickRate = 60Hz`, `DashTicks == 12` (`0.20s`), `DodgeTicks == 15` (`0.25s`), `HorizontalTurnTicks == 11` (`0.18s`), and `ProjectileLifetimeTicks == 120` (`30.0m / 15.0m/s * 60`).
- [x] Cosine half-angle thresholds are cached as `VerticalSlashCosHalfAngle == 0.9063078f` (`+-1e-5f` for `50 deg` cone) and `HorizontalSweepCosHalfAngle == 0.7071068f` (`+-1e-5f` for `90 deg` cone), plus squared ranges `VerticalSlashRangeSqr == 6.25f` and `HorizontalSweepRangeSqr` (`9.0f` / `12.25f` per configured `HorizontalSweepRange`).

---

## Implementation Notes

*Derived from ADR-0001 Implementation Guidelines:*

Extend `Assets/Scripts/Core/Config/CharacterCombatConfigSO.cs` with `[NonSerialized]` cached backing fields and public read-only getters (`DashTicks`, `DodgeTicks`, `HorizontalTurnTicks`, `ProjectileLifetimeTicks`, `VerticalSlashCosHalfAngle`, `HorizontalSweepCosHalfAngle`, `VerticalSlashRangeSqr`, `HorizontalSweepRangeSqr`). Implement `public void RecalculateDerivedValues(int tickRate = 60)` using `Mathf.Max(1, Mathf.RoundToInt(durationSec * tickRate))` and `Mathf.Cos(angleDeg * 0.5f * Mathf.Deg2Rad)`. Call `RecalculateDerivedValues()` at the end of `OnValidate()` and inside `OnEnable()`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-combat-config-encapsulation.md`): Base serialized fields and `OnValidate()` range clamping.
- Story 003 (`story-003-default-combat-config-asset.md`): Creating the `DefaultCapsuleCombatConfig.asset` file on disk.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Core/Config/ICharacterCombatConfig.cs`
- `Assets/Scripts/Core/Config/CharacterCombatConfigSO.cs`
- `Assets/Tests/EditMode/CharacterCombatConfigTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `CharacterCombatConfigSO` precomputes derived tick and cosine thresholds in `OnEnable()` and `OnValidate()`.
  - Given: Default config values (`DashDuration = 0.20s`, `DodgeDuration = 0.25s`, `TurnDuration = 0.18s`, `RangedMaxDistance = 30.0m`, `RangedSpeed = 15.0m/s`, `VerticalAngle = 50 deg`, `HorizontalAngle = 90 deg`, `TickRate = 60`).
  - When: `OnEnable()` / `RecalculateDerivedValues()` executes.
  - Then: `DashTicks == 12`, `DodgeTicks == 15`, `HorizontalTurnTicks == 11`, `ProjectileLifetimeTicks == 120`, `VerticalSlashCosHalfAngle == 0.9063078f` (`+-1e-5f`), and `HorizontalSweepCosHalfAngle == 0.7071068f` (`+-1e-5f`).
  - Edge cases: `RangedSpeed` modified to minimal positive value (`0.01f`) does not throw `DivideByZeroException`; `DashTicks` never evaluates to `< 1`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/CharacterCombatConfigTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-combat-config/story-001-combat-config-encapsulation.md`
- Unlocks: `production/epics/character-combat-config/story-003-default-combat-config-asset.md`, `production/epics/character-movement-mobility/story-001-tick-accurate-walk-jump-gravity.md`, `production/epics/combat-melee-ranged-system/story-001-zero-alloc-melee-sector-detection.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/CharacterCombatConfigTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
