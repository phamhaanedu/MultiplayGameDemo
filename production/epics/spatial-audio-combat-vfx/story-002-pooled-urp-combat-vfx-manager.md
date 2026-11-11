# Story 002: Pooled URP Combat VFX Manager (`VfxManager`)

> **Epic**: 3D Spatial Audio & Combat VFX Feedback
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/spatial-audio-combat-vfx.md`
**Requirement**: `TR-audio-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: PlayFab Async Identity/Leaderboard Resilience & Pooled URP Audio/VFX Feedback
**ADR Decision Summary**: Pre-warms `20` pooled URP particle/mesh VFX instances (`8` melee slash arcs matching the exact `50 deg / 2.5m` Vertical and `90 deg / 3.0m` Horizontal cones, `8` impact spark bursts, and `4` dash/death bursts) with automatic timer return to pool (`0` runtime `Instantiate`/`Destroy`).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Calling `ParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear)` before `ParticleSystem.Play(true)` ensures pooled VFX instances never emit leftover particles from their previous world location.

**Control Manifest Rules (this layer)**:
- Required: Pre-warm `20` pooled VFX instances (`8` Slash Arcs, `8` Impact Sparks, `4` Dash/Elimination Bursts) at startup and clear particles (`StopEmittingAndClear`) on pool acquire/release.
- Forbidden: Never call `Object.Instantiate` or `Object.Destroy` for combat VFX during gameplay.
- Guardrail: Visual melee arc geometry must match the exact gameplay hit sectors (`50 deg`, `2.5m` `#00F5D4` Cyan for Vertical Slash `LMB`; `90 deg` offset `+-45 deg`, `3.0m` `#FF9F1C` Amber for Horizontal Sweep `Keys 1/3`).

---

## Acceptance Criteria

*From GDD `design/gdd/spatial-audio-combat-vfx.md`, scoped to this story:*

- [x] `VfxManager` pre-warms `20` pooled VFX instances at startup (`8` `VFX_SlashArc`, `8` `VFX_HitSpark`, `4` `VFX_DashAndDeathBurst`) and recycles them automatically when their duration (`0.18s..0.60s`) expires.
- [x] `SpawnSlashArc(SlashArcType type, Vector3 origin, Quaternion rotation)` renders a `50 deg`, `2.5m` Electric Cyan (`#00F5D4`) vertical `YZ`-plane fan for `LMB` and a `90 deg`, `3.0m` Solar Amber (`#FF9F1C`) horizontal `XZ`-plane sweep fan (`[-90 deg, 0 deg]` for `Key 1`, `[0 deg, +90 deg]` for `Key 3`).
- [x] `SpawnImpactSpark(Vector3 hitPoint, bool isPlayerHit)` spawns a pooled `#FFB703` / `#E2E8F0` burst (`14` particles on player hit, `8` sparks on `Layer 6` wall hit) with `0` runtime `Instantiate`/`Destroy` calls.

---

## Implementation Notes

*Derived from ADR-0008 Implementation Guidelines:*

Create `Assets/Scripts/Presentation/VFX/VfxManager.cs` and `Assets/Scripts/Presentation/VFX/PooledVfxInstance.cs`. Generate procedural sector arc meshes (`50 deg` radius `2.5m` and `90 deg` radius `3.0m`) if external meshes are not assigned so the exact sector cones are always visible and verifiable in Play Mode and tests.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-pooled-3d-spatial-sound-manager.md`): Pooled 3D `SoundManager`.
- Story 003 (`story-003-mpb-hitflash-and-dotween-squash-feedback.md`): `MaterialPropertyBlock` Hit-Flash and DOTween Squash & Stretch on the capsule mesh.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Presentation/VFX/PooledVfxInstance.cs`
- `Assets/Scripts/Presentation/VFX/VfxManager.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/AudioVfxPoolTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `VfxManager` pre-warms `20` pooled URP VFX instances (`50 deg / 2.5m` Vertical Slash arc `#00F5D4`, `90 deg / 3.0m` Horizontal Sweep arc `#FF9F1C`, and impact sparks) and recycles them with `0` runtime `Instantiate`/`Destroy`.
  - Setup: Enter Play Mode in `Arena.unity`, perform Vertical Slash (`LMB`), Horizontal Sweep Left/Right (`Keys 1/3`), and Ranged Projectile hits (`RMB`) on an opponent and a wall.
  - Verify: Vertical Slash displays a `50 deg`, `2.5m` `#00F5D4` cyan arc; Horizontal Sweep displays a `90 deg`, `3.0m` `#FF9F1C` amber horizontal fan matching the hit sector; impact sparks burst at the hit point and return to the `20`-item pool after completion.
  - Pass condition: Retained screenshot of Vertical/Horizontal slash arcs and impact sparks in `production/qa/evidence/story-002-pooled-urp-combat-vfx-manager.png` + lead sign-off in `production/qa/evidence/story-002-pooled-urp-combat-vfx-manager-evidence.md` + passing unit test in `AudioVfxPoolTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: a retained screenshot in `production/qa/evidence/` + sign-off in `production/qa/evidence/story-002-pooled-urp-combat-vfx-manager-evidence.md`

**Status**: [x] Verified (`production/qa/evidence/story-002-pooled-urp-combat-vfx-manager.png`, `production/qa/evidence/story-002-pooled-urp-combat-vfx-manager-evidence.md`, `production/qa/evidence/story-002-pooled-urp-combat-vfx-manager/stamp.txt`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/spatial-audio-combat-vfx/story-001-pooled-3d-spatial-sound-manager.md`
- Unlocks: `production/epics/spatial-audio-combat-vfx/story-003-mpb-hitflash-and-dotween-squash-feedback.md`

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: Retained `1280x720` URP screenshot (`01-vertical-slash-cyan-and-horizontal-sweep-amber-arcs.png`) + SHA-1 `stamp.txt` (`UNCHANGED`) + `AudioVfxPoolTests.cs` (`20` pooled instances `8+8+4`, orthogonal `YZ` `50 deg / 2.5m` Cyan & `XZ` `90 deg / 3.0m` Amber meshes, `9th` arc stealing, `14` `#FFB703` player vs `8` `#E2E8F0` wall sparks, `OnDestroy` asset cleanup).
