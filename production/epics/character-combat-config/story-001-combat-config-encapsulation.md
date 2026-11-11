# Story 001: Combat Config Encapsulation & Validation

> **Epic**: Character & Combat Config Data (`CharacterCombatConfigSO`)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/character-combat-config.md`
**Requirement**: `TR-config-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture
**ADR Decision Summary**: Encapsulates all combat and mobility constants in an immutable `CharacterCombatConfigSO : ScriptableObject` with `[SerializeField] private` fields, read-only public C# properties, and `OnValidate()` clamping (`0 B` runtime GC).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Standard `[CreateAssetMenu]`, `[SerializeField] private`, `OnValidate()`, and `OnEnable()` lifecycle methods are stable across Unity 2022 LTS through Unity 6.6 (`6000.6.x`).

**Control Manifest Rules (this layer)**:
- Required: Expose all gameplay tuning parameters via `[SerializeField] private` fields with read-only public C# properties on `CharacterCombatConfigSO : ScriptableObject`.
- Forbidden: Never declare `public` mutable fields on `CharacterCombatConfigSO` or mutate `ScriptableObject` fields at runtime during a match.
- Guardrail: `0 B` heap allocation on `60Hz` `FixedUpdateNetwork()` property reads.

---

## Acceptance Criteria

*From GDD `design/gdd/character-combat-config.md`, scoped to this story:*

- [x] `CharacterCombatConfigSO` exposes all 5 parameter groups (Health, Movement, Mobility Dash/Dodge, Combat Melee/Ranged, Presentation/Camera) exclusively via read-only public C# properties (`get => _field;`) backed by `[SerializeField] private` fields (`_camelCase`).
- [x] `OnValidate()` clamps out-of-range Inspector inputs (`_maxHealth >= 1`, `_moveSpeed > 0f`, `_verticalSlashAngleDeg` and `_horizontalSweepAngleDeg` in `[5f, 180f]`, `_rangedFireCooldownSec >= 0.05f`).
- [x] All runtime `MonoBehaviour` and `NetworkBehaviour` consumers verify `_combatConfig != null` in `Awake()`/`Start()` (`CharacterCombatConfigSO.EnsureValidConfig`) and read `CharacterCombatConfigSO` properties in `60Hz` `FixedUpdateNetwork()` with `0 B` managed heap allocation.

---

## Implementation Notes

*Derived from ADR-0001 Implementation Guidelines:*

Create `Assets/Scripts/Core/Config/CharacterCombatConfigSO.cs` decorated with `[CreateAssetMenu(fileName = "DefaultCapsuleCombatConfig", menuName = "CapsuleArena/Config/CharacterCombatConfig", order = 1)]`. Define `[SerializeField] private` fields for all 5 groups (`_maxHealth`, `_moveSpeed`, `_sprintMultiplier`, `_jumpHeight`, `_gravity`, `_dashSpeed`, `_dashDurationSec`, `_dashCooldownSec`, `_dodgeSpeed`, `_dodgeDurationSec`, `_dodgeCooldownSec`, `_verticalSlashDamage`, `_verticalSlashRange`, `_verticalSlashAngleDeg`, `_verticalSlashCooldownSec`, `_horizontalSweepDamage`, `_horizontalSweepRange`, `_horizontalSweepAngleDeg`, `_horizontalSweepTurnDeg`, `_horizontalSweepTurnDurationSec`, `_horizontalSweepCooldownSec`, `_rangedDamage`, `_rangedProjectileSpeed`, `_rangedMaxDistance`, `_rangedFireCooldownSec`, `_tpsCameraDistance`, `_tpsCameraHeight`, `_hitFlashDurationSec`, `_squashStretchDurationSec`) and expose read-only public properties. Implement `OnValidate()` clamping so invalid negative or zero values entered in the Inspector are immediately corrected.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-precomputed-tick-and-cosine-cache.md`): Precomputing derived tick counts and cosine half-angle thresholds in `OnEnable()`/`OnValidate()`.
- Story 003 (`story-003-default-combat-config-asset.md`): Authoring the `DefaultCapsuleCombatConfig.asset` instance with canonical balance values.

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

- **AC-1**: `CharacterCombatConfigSO` exposes read-only public C# properties backed by `[SerializeField] private` fields with `OnValidate()` clamping and `0 B` runtime allocation.
  - Given: An in-memory `CharacterCombatConfigSO` instance created via `ScriptableObject.CreateInstance<CharacterCombatConfigSO>()` in EditMode.
  - When: Reflection inspects all `FieldInfo` and `PropertyInfo` members, and `OnValidate()` is invoked with out-of-range negative/zero serialized values (`_maxHealth = -10`, `_moveSpeed = 0f`, `_verticalSlashAngleDeg = 200f`).
  - Then: Zero public mutable fields exist (`CanWrite == false` on all public properties), `OnValidate()` clamps `_maxHealth >= 1`, `_moveSpeed > 0f`, angles within `[5f, 180f]`, and 1,000 sequential property reads allocate `0 B` GC.
  - Edge cases: `NaN` or `float.NegativeInfinity` serialized values; missing reference in consumer `Awake()`/`Start()` logs descriptive error and disables component cleanly.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/CharacterCombatConfigTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: None
- Unlocks: `production/epics/character-combat-config/story-002-precomputed-tick-and-cosine-cache.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/CharacterCombatConfigTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
