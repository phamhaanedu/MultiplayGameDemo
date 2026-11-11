# Story 002: Damage Request RPC & Distance Sanity Validation

> **Epic**: Networked Health, Damage RPC & Death State System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/health-damage-rpc-system.md`
**Requirement**: `TR-health-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Client-Authoritative Mobility (`Dash Key 2` / `Dodge Q/E`) & Victim-Authoritative Damage RPC
**ADR Decision Summary**: Routes damage requests via `[Rpc(RpcSources.All, RpcTargets.StateAuthority)] public void Rpc_RequestApplyDamage(int damage, byte attackTypeByte, PlayerRef attacker, Vector3 attackerOrigin, Vector3 hitPoint, int attackTick)` with latency-tolerant distance sanity (`5.0m` melee, `34.0m` ranged), `(attacker, attackTick)` ring-buffer deduplication, and single-winner lethal attribution.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: In Shared Mode, `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` executes directly on the victim client who owns `StateAuthority` over the target `NetworkObject`.

**Control Manifest Rules (this layer)**:
- Required: Validate `IsDead == false`, `MatchState == Playing`, `attacker != Object.InputAuthority` (no self-damage), canonical damage values (`25`, `30`, `15`), and generous distance bounds (`<= 5.0m` melee, `<= 34.0m` ranged) inside `Rpc_RequestApplyDamage`.
- Forbidden: Never trust arbitrary damage integers outside `{25, 30, 15}` or process a second lethal hit after `IsDead` flips to `true`.
- Guardrail: Exactly one attacker can be credited with a kill per victim life (`if (IsDead) return;`).

---

## Acceptance Criteria

*From GDD `design/gdd/health-damage-rpc-system.md`, scoped to this story:*

- [x] `Rpc_RequestApplyDamage(...)` validates that `MatchState == Playing` (`BindMatchPlayingPredicate`), `!IsDead`, `attacker != OwnerPlayer`, `(attacker, attackTick)` has not already been processed, and `damage` matches the canonical config value for `attackType` (`0=Vertical: 25`, `1=Horizontal: 30`, `2=Ranged: 15`).
- [x] Distance sanity validation checks squared distance against verified attacker origin (`<= 5.0m` for melee types `0/1` to accommodate `200ms` RTT + dash velocity, `<= 34.0m` for ranged type `2`), rejecting out-of-range or non-finite forged packets.
- [x] When a hit reduces `CurrentHP` to `0`, `PlayerHealth` immediately sets `IsDead = true`, rejects any subsequent same-tick RPCs, and broadcasts `Rpc_NotifyKillConfirmed` + `Rpc_BroadcastHitFeedback(hitPoint, attackTypeByte, isLethal)`.

---

## Implementation Notes

*Derived from ADR-0004 Implementation Guidelines:*

Extend `Assets/Scripts/Player/PlayerHealth.cs` with `ValidateDamageRequest(...)`, verified melee origin lookup via `DamageReceiverRegistry.TryGetByPlayer`, an 8-slot `(PlayerRef, int)` deduplication ring buffer, `[Rpc(RpcSources.All, RpcTargets.StateAuthority)] Rpc_RequestApplyDamage`, `[Rpc(RpcSources.StateAuthority, RpcTargets.All)] Rpc_BroadcastHitFeedback`, and `[Rpc(RpcSources.StateAuthority, RpcTargets.All)] Rpc_NotifyKillConfirmed`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-victim-authoritative-networked-health.md`): Base `CurrentHP` clamping and `ChangeDetector`.
- Story 003 (`story-003-death-state-and-ghost-layer-transition.md`): `Layer 10 (GhostSpectator)` collider & visual material swap.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Combat/AttackType.cs`
- `Assets/Scripts/Combat/IDamageReceiver.cs`
- `Assets/Scripts/Player/PlayerHealth.cs`
- `Assets/Tests/EditMode/PlayerHealthTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `Rpc_RequestApplyDamage` validates `MatchState == Playing`, `!IsDead`, canonical damage values, and latency-tolerant distance sanity (`5.0m` melee, `34.0m` ranged) and guarantees single-killer attribution.
  - Given: Victim at `(0, 0, 0)` with `CurrentHP = 25` in `MatchState.Playing`.
  - When: Two attackers send lethal `VerticalSlash (25)` RPCs in the same tick from `(2.0, 0, 0)`, and a third attacker sends a melee RPC from `(12.0, 0, 0)` or with forged damage `999`.
  - Then: Out-of-range (`12.0m > 5.0m`) and forged (`999`) RPCs are rejected; the first valid lethal RPC reduces `CurrentHP` to `0`, sets `IsDead = true`, and credits exactly 1 kill to the first attacker while the second lethal RPC is ignored.
  - Edge cases: Self-damage (`attacker == victim`) is rejected; ranged projectile hit when attacker has moved up to `34.0m` away is accepted (`34.01m` rejected).

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/PlayerHealthTests.cs` — must exist and pass

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/health-damage-rpc-system/story-001-victim-authoritative-networked-health.md`
- Unlocks: `production/epics/health-damage-rpc-system/story-003-death-state-and-ghost-layer-transition.md`, `production/epics/combat-melee-ranged-system/story-001-zero-alloc-melee-sector-detection.md`, `production/epics/match-manager-authority-failover/story-002-last-man-standing-win-evaluator.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/PlayerHealthTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
