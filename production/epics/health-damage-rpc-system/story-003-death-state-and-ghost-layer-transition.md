# Story 003: Death State & Ghost Spectator Layer Transition

> **Epic**: Networked Health, Damage RPC & Death State System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/health-damage-rpc-system.md`
**Requirement**: `TR-health-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Client-Authoritative Mobility (`Dash Key 2` / `Dodge Q/E`) & Victim-Authoritative Damage RPC
**ADR Decision Summary**: Transitions eliminated players (`IsDead == true`) from `Layer 8 (PlayerCharacter)` to `Layer 10 (GhostSpectator)` without despawning their `NetworkObject`, switches their renderer to translucent `M_Capsule_Ghost` (`#A8DADC`, `Alpha = 0.35`), and disables combat/dash actions.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Keeping the `NetworkObject` spawned while switching its GameObject hierarchy to `Layer 10` preserves `NetworkTransform` synchronization and local camera ownership in Shared Mode.

**Control Manifest Rules (this layer)**:
- Required: Switch the eliminated player's collider/hierarchy to `Layer 10 (GhostSpectator)` on all peers when `IsDead` becomes `true`, apply `M_Capsule_Ghost` (`Alpha = 0.35`), and hide the world-space HP bar.
- Forbidden: Never call `Runner.Despawn` or `Destroy` on an eliminated player's `NetworkObject` while the match is still in `Playing` or `Ended` state.
- Guardrail: Eliminated players on `Layer 10` must have zero physical collision with living players (`Layer 8`), projectiles, and `Layer 6` walls.

---

## Acceptance Criteria

*From GDD `design/gdd/health-damage-rpc-system.md`, scoped to this story:*

- [x] When `IsDead` transitions `false -> true` (immediately on the lethal simulation/RPC tick on `StateAuthority` and via `ChangeDetector` in `Render()`), `PlayerDeathStateController` switches the player root and collider GameObjects from `Layer 8 (PlayerCharacter)` to `Layer 10 (GhostSpectator)` without calling `Runner.Despawn`.
- [x] The capsule mesh renderer swaps to translucent `M_Capsule_Ghost` (`#A8DADC`, `Alpha = 0.35`), the overhead HP bar hides while the nameplate appends `" [GHOST]"` at `50%` opacity via `StringBuilder`, and the elimination burst VFX + SFX event (`OnEliminationPresentationTriggered`) fires at the death position.
- [x] On the local eliminated player, combat hitboxes and dash/dodge inputs are locked, the Mobile Action Pad hides (`Alpha = 0`, `interactable = false`), and `OnLocalSpectatorModeChanged(true)` activates for Ghost spectator modes.

---

## Implementation Notes

*Derived from ADR-0004 Implementation Guidelines:*

Create `Assets/Scripts/Player/IPlayerDeathStateController.cs`, `Assets/Scripts/Player/PlayerDeathStateController.cs`, `Assets/Materials/Arena/M_Capsule_Ghost.mat`, and `Assets/Prefabs/Player/PlayerCapsule.prefab`. Ensure `CommitAuthoritativeHealthMutation` calls `_deathStateController.ApplyDeathState()` synchronously on the lethal simulation tick so subsequent same-tick physics queries immediately treat the eliminated capsule as `Layer 10 (GhostSpectator)`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-victim-authoritative-networked-health.md`): `CurrentHP` state clamping.
- Story 002 (`story-002-damage-request-rpc-validation.md`): `Rpc_RequestApplyDamage` validation logic.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Player/IPlayerDeathStateController.cs`
- `Assets/Scripts/Player/PlayerDeathStateController.cs`
- `Assets/Scripts/Player/PlayerHealth.cs`
- `Assets/Materials/Arena/M_Capsule_Ghost.mat`
- `Assets/Prefabs/Player/PlayerCapsule.prefab`
- `Assets/Tests/EditMode/PlayerHealthTests.cs`
- `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: On `CurrentHP == 0`, `PlayerDeathStateController` switches collider to `Layer 10 (GhostSpectator)`, swaps mesh to `M_Capsule_Ghost` (`Alpha=0.35`), disables combat/dash inputs, and preserves the `NetworkObject`.
  - Given: Living player on `Layer 8 (PlayerCharacter)` taking lethal damage (`CurrentHP -> 0`).
  - When: `IsDead` transitions `false -> true` and `ApplyDeathState()` executes.
  - Then: GameObject remains active (`activeInHierarchy == true`), collider layer equals `10 (GhostSpectator)`, combat/mobility actions are disabled, and material swaps to `M_Capsule_Ghost`.
  - Edge cases: Dying mid-dash or mid-dodge immediately cancels the burst and resets `VisualRoot` rotation to `Quaternion.identity`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/PlayerHealthTests.cs`, `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`, and retained screenshot `production/qa/evidence/story-003-death-state-and-ghost-layer-transition/01-ghost-spectator-capsule.png` + `stamp.txt`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/arena-level-architecture/story-003-physics-layer-collision-matrix.md`, `production/epics/health-damage-rpc-system/story-002-damage-request-rpc-validation.md`
- Unlocks: `production/epics/camera-fps-tps-spectator/story-003-post-death-ghost-spectator-controller.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/PlayerHealthTests.cs`, `Assets/Tests/PlayMode/ArenaMatchSmokeTests.cs`, and `production/qa/evidence/story-003-death-state-and-ghost-layer-transition/01-ghost-spectator-capsule.png`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `1280x720` URP Ghost Spectator rende
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
