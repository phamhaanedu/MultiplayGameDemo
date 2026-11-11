# Story 001: Victim-Authoritative Networked Health (`PlayerHealth`)

> **Epic**: Networked Health, Damage RPC & Death State System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/health-damage-rpc-system.md`
**Requirement**: `TR-health-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Client-Authoritative Mobility (`Dash Key 2` / `Dodge Q/E`) & Victim-Authoritative Damage RPC
**ADR Decision Summary**: Stores `[Networked] public int NetworkedCurrentHP { get; private set; }` (`[0, 100]`) and `[Networked] public NetworkBool NetworkedIsDead { get; private set; }` on `PlayerHealth : NetworkBehaviour`, allowing only the victim's `StateAuthority` to mutate `CurrentHP` and broadcasting changes via Fusion 2 `ChangeDetector` in `Render()`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Fusion 2 replaced `[Networked(OnChanged = ...)]` with `ChangeDetector` (`GetChangeDetector(ChangeDetector.Source.SimulationState)`) iterated in `Render()`.

**Control Manifest Rules (this layer)**:
- Required: Mutate `CurrentHP` and `IsDead` exclusively on the victim client (`Object.HasStateAuthority == true`) and use Fusion 2 `ChangeDetector` in `Render()` to fire `OnHealthChanged(int currentHp, int maxHp)`.
- Forbidden: Never use deprecated Fusion 1 `[Networked(OnChanged = ...)]` attributes or allow an attacker client to write directly to a victim's `CurrentHP`.
- Guardrail: `CurrentHP` must always be clamped via `Mathf.Clamp(CurrentHP - damage, 0, MaxHP)` (`[0, 100]`).

---

## Acceptance Criteria

*From GDD `design/gdd/health-damage-rpc-system.md`, scoped to this story:*

- [x] `PlayerHealth` initializes `CurrentHP = _combatConfig.MaxHealth` (`100`) and `IsDead = false` in `Spawned()` when `Object.HasStateAuthority == true` (and syncs late-joining proxy state).
- [x] `ApplyAuthoritativeDamage(int damage, PlayerRef attacker)` clamps `CurrentHP = Mathf.Clamp(CurrentHP - damage, 0, MaxHealth)` (`25` Vertical Slash -> `75`, `30` Horizontal Sweep -> `45`, `15` Ranged -> `30`, `30` Horizontal Sweep -> `0`) and sets `IsDead = true` on the exact tick `CurrentHP == 0`.
- [x] `Render()` uses Fusion 2 `ChangeDetector` to detect `NetworkedCurrentHP` and `NetworkedIsDead` state transitions and invokes C# events `OnHealthChanged(int current, int max)` and `OnPlayerDied(PlayerRef victim, PlayerRef killer)` with `0 B` per-frame GC allocation.

---

## Implementation Notes

*Derived from ADR-0004 Implementation Guidelines:*

Create `Assets/Scripts/Player/PlayerHealth.cs` inheriting from `NetworkBehaviour` and implementing `IDamageReceiver`. Initialize `_changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState)` in `Spawned()`. Enforce `HasStateAuthority`, `IsDead == false`, `isMatchPlaying == true`, and `damage > 0`, clamping `CurrentHP` to `[0, 100]` and firing `OnHealthChanged` / `OnPlayerDied`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-damage-request-rpc-validation.md`): `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` distance sanity check and lethal kill attribution RPC.
- Story 003 (`story-003-death-state-and-ghost-layer-transition.md`): Switching dead capsule to `Layer 10 (GhostSpectator)` and Ghost material transparency.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Player/PlayerHealth.cs`
- `Assets/Tests/EditMode/PlayerHealthTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `PlayerHealth` initializes `CurrentHP = 100`, `IsDead = false`, clamps `CurrentHP` to `[0, 100]` only on `StateAuthority`, and dispatches `OnHealthChanged` via `ChangeDetector`.
  - Given: `PlayerHealth` initialized at `CurrentHP = 100`, `IsDead = false` with `DefaultCapsuleCombatConfig`.
  - When: Sequential validated hits of `25` (Vertical), `30` (Horizontal), `15` (Ranged), and `35` (overkill) are applied.
  - Then: `CurrentHP` transitions `100 -> 75 -> 45 -> 30 -> 0`, `IsDead` becomes `true` at `0 HP`, `OnHealthChanged` fires for each transition, and `CurrentHP` never drops below `0`.
  - Edge cases: Damage applied after `IsDead == true` or when `MatchState != Playing` is ignored (`0` HP change, no duplicate `OnPlayerDied`).

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/PlayerHealthTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-combat-config/story-003-default-combat-config-asset.md`
- Unlocks: `production/epics/health-damage-rpc-system/story-002-damage-request-rpc-validation.md`, `production/epics/health-damage-rpc-system/story-003-death-state-and-ghost-layer-transition.md`, `production/epics/combat-hud-mobile-action-pad/story-001-world-space-overhead-billboard-ui.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/PlayerHealthTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
