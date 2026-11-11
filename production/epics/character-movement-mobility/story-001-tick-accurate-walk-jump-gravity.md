# Story 001: Tick-Accurate Walk, Jump & Gravity (`NetworkCharacterController`)

> **Epic**: Character Movement & Tactical Mobility (`Dash Key 2` & `Dodge Q/E`)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/character-movement-mobility.md`
**Requirement**: `TR-move-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0004: Client-Authoritative Mobility (`Dash Key 2` / `Dodge Q/E`) & Victim-Authoritative Damage RPC
**ADR Decision Summary**: Executes all movement inside `FixedUpdateNetwork()` on `Object.HasStateAuthority` using `Runner.DeltaTime` (`1/60s`) and `CharacterCombatConfigSO` parameters (`MoveSpeed = 6.0 m/s`, `JumpHeight = 1.5 m`, `Gravity = -19.62 m/s^2`).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: HIGH
**Engine Notes**: Fusion 2 `CharacterController` + `NetworkTransform` uses tick-accurate trapezoidal vertical integration (`6`-tick Coyote Time + `6`-tick Jump Buffer) in `FixedUpdateNetwork()` using `Runner.DeltaTime` and `GetInput(out NetworkInputData input)`.

**Control Manifest Rules (this layer)**:
- Required: Guard all movement simulation with `if (!Object.HasStateAuthority) return;` inside `FixedUpdateNetwork()` and multiply velocities by `Runner.DeltaTime`.
- Forbidden: Never move networked characters inside `Update()` or `FixedUpdate()` using `Time.deltaTime`.
- Guardrail: Diagonal `WASD` input must be clamped to `<= 1.0f` so base walk speed never exceeds `6.0 m/s`.

---

## Acceptance Criteria

*From GDD `design/gdd/character-movement-mobility.md`, scoped to this story:*

- [x] `PlayerMobilityController` simulates planar `WASD` movement at `MoveSpeed = 6.0 m/s` (`0.10 m/tick` at `60Hz`) relative to the player's horizontal yaw inside `FixedUpdateNetwork()`.
- [x] Pressing `Jump` while `IsGrounded == true` (or within `6`-tick Coyote Time / Jump Buffer) applies vertical impulse `v_y0 = sqrt(2 * |Gravity| * JumpHeight) = 7.672 m/s` (`JumpHeight = 1.5m`, `Gravity = -19.62 m/s^2`) reaching `1.50m` apex and prevents double-jumping mid-air.
- [x] If the player's position ever falls below `Y < -5.0m` (out-of-bounds safety floor), `PlayerMobilityController` immediately teleports the capsule via `NetworkTransform.Teleport` back to `ArenaSpawnManager`'s safest spawn point with zeroed velocity.

---

## Implementation Notes

*Derived from ADR-0004 Implementation Guidelines:*

Create `Assets/Scripts/Player/MobilityState.cs`, `Assets/Scripts/Player/IPlayerMobilityController.cs`, and `Assets/Scripts/Player/PlayerMobilityController.cs` inheriting from `NetworkBehaviour`. Expose deterministic tick step helpers alongside `FixedUpdateNetwork()` so EditMode unit tests can verify exact `60Hz` tick displacement (`6.0m` over `60` ticks), jump impulse (`7.672 m/s`, `1.50m` apex), `6`-tick Coyote Time / Jump Buffer, and out-of-bounds recovery (`Y < -5.0m`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-forward-dash-key2-and-wall-slide.md`): Forward Dash (`Key 2`) 12-tick state override and wall-sliding projection.
- Story 003 (`story-003-dodge-roll-qe-and-visual-tilt.md`): Lateral Dodge Roll (`Keys Q/E`) 15-tick state override and visual `360 deg` roll / `15 deg` dash pitch tilt.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Player/MobilityState.cs`
- `Assets/Scripts/Player/IPlayerMobilityController.cs`
- `Assets/Scripts/Player/PlayerMobilityController.cs`
- `Assets/Prefabs/Player/PlayerCapsule.prefab`
- `Assets/Tests/EditMode/PlayerMobilityControllerTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `PlayerMobilityController` executes `WASD` walk (`6.0 m/s`), jump (`1.5m`, `v_y0 = 7.672 m/s`), and gravity (`-19.62 m/s^2`) in `FixedUpdateNetwork()`.
  - Given: `PlayerMobilityController` initialized with `DefaultCapsuleCombatConfig` at `60Hz` (`dt = 1f/60f`).
  - When: `MoveDirection = (0, 1)` is simulated for `60` ticks, and `Jump` is triggered when `IsGrounded == true`.
  - Then: Planar displacement after `60` ticks equals `6.0m +- 0.02m`, jump impulse sets `v_y == 7.672f +- 0.01f` reaching apex `1.50m +- 0.03m`, and `Y < -5.0m` triggers immediate teleport recovery to a valid spawn point.
  - Edge cases: Pressing `Jump` while `IsGrounded == false` is ignored; diagonal input `(1, 1)` is normalized so speed never exceeds `6.0 m/s`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/PlayerMobilityControllerTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/character-combat-config/story-002-precomputed-tick-and-cosine-cache.md`, `production/epics/keybinding-input-system/story-002-dual-pc-mobile-input-poller.md`
- Unlocks: `production/epics/character-movement-mobility/story-002-forward-dash-key2-and-wall-slide.md`, `production/epics/character-movement-mobility/story-003-dodge-roll-qe-and-visual-tilt.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `Assets/Tests/EditMode/PlayerMobilityControllerTests.cs` (`49/49` EditMode PASS)
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`49/49 PASS`), PlayMode (`7/7 PASS`), and `/smoke-check sprint` (`production/qa/smoke-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
