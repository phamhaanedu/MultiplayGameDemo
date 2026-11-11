# Story 003: `MaterialPropertyBlock` Hit-Flash & DOTween Squash/Stretch Feedback

> **Epic**: 3D Spatial Audio & Combat VFX Feedback
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/spatial-audio-combat-vfx.md`
**Requirement**: `TR-audio-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: PlayFab Async Identity/Leaderboard Resilience & Pooled URP Audio/VFX Feedback
**ADR Decision Summary**: Applies `0.12s` white emissive Hit-Flash (`3.5x` HDR intensity) exclusively via cached `MaterialPropertyBlock` (`Shader.PropertyToID("_EmissionColor")`) to preserve URP SRP Batcher compatibility (`0` material clones), and drives `0.15s` Squash & Stretch on the child `VisualRoot` with `DOKill(true)` reset before each tween.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Accessing `renderer.material` duplicates the material instance and breaks URP batching; `renderer.GetPropertyBlock(_mpb)` / `renderer.SetPropertyBlock(_mpb)` with cached `static readonly int EmissionColorId` allocates `0 B` and clones `0` materials.

**Control Manifest Rules (this layer)**:
- Required: Use cached `MaterialPropertyBlock` and `Shader.PropertyToID` on `CapsuleVisualFeedback`, and call `_visualRoot.DOKill(true)` + `_visualRoot.localScale = Vector3.one` before starting any Squash & Stretch tween on the child `VisualRoot`.
- Forbidden: Never access `MeshRenderer.material` at runtime or tween `transform.localScale` on the root `NetworkObject` (which holds the physics `CapsuleCollider`).
- Guardrail: Even under rapid consecutive hits, `VisualRoot.localScale` must always return to exact `(1.0f, 1.0f, 1.0f)` after `0.15s` with zero scale drift.

---

## Acceptance Criteria

*From GDD `design/gdd/spatial-audio-combat-vfx.md`, scoped to this story:*

- [x] `CapsuleVisualFeedback` caches `MaterialPropertyBlock` and `static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor")` (and `_BaseColor`) in `Awake()` (`GetPropertyBlock`/`SetPropertyBlock`) and never accesses `MeshRenderer.material`.
- [x] On `PlayHitFeedback()`, the capsule flashes emissive white (`#FFFFFF` at `3.5x` HDR intensity) via `SetPropertyBlock` for `HitFlashDurationSec = 0.12s` and performs a Squash & Stretch scale pulse (`(1.22f, 0.78f, 1.22f) -> (1f, 1f, 1f)` over `SquashStretchDurationSec = 0.15s`) exclusively on the child `VisualRoot` (preserved across 60Hz mobility ticks via `!DOTween.IsTweening(_visualRoot)`).
- [x] Calling `PlayHitFeedback()`, `PlayDashStretch()` (`(0.82f, 0.90f, 1.30f)`), or `OnPlayerDied()` cancels any in-flight tween/flash immediately (`DOKill(true)`) and guarantees `_visualRoot.localScale == Vector3.one` (`(1, 1, 1)`) and root `Collider` scale `(1, 1, 1)`.

---

## Implementation Notes

*Derived from ADR-0008 Implementation Guidelines:*

Create `Assets/Scripts/Presentation/VFX/CapsuleVisualFeedback.cs`. Subscribe to `PlayerHealth.OnHealthChanged` (when `currentHP < previousHP`) and `PlayerHealth.OnPlayerDied`. Expose `TriggerHitFlashAndSquash()` and `ResetVisualState()` so EditMode unit tests can assert that `sharedMaterial` reference remains identical before/after 50 rapid hit flashes (`0` material clones) and `_visualRoot.localScale` returns to `Vector3.one` while root `transform.localScale` stays untouched.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-pooled-3d-spatial-sound-manager.md`): Pooled 3D `SoundManager`.
- Story 002 (`story-002-pooled-urp-combat-vfx-manager.md`): Pooled URP `VfxManager` slash arcs and sparks.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Presentation/VFX/CapsuleVisualFeedback.cs`
- `Assets/Scripts/Core/Tweening/DOTweenCompat.cs`
- `Assets/Scripts/Player/PlayerMobilityController.cs`
- `Assets/Prefabs/Player/PlayerCapsule.prefab`
- `Assets/Tests/EditMode/AudioVfxPoolTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `CapsuleVisualFeedback` applies `0.12s` white Hit-Flash via cached `MaterialPropertyBlock` (`0` material clones) and `0.15s` Squash & Stretch exclusively on child `VisualRoot` with `DOKill(true)` scale reset to `(1, 1, 1)`.
  - Setup: Enter Play Mode in `Arena.unity`, trigger rapid consecutive melee and ranged hits on a target capsule, and run the EditMode `AudioVfxPoolTests` verification.
  - Verify: Target capsule flashes bright white for `0.12s` via `MaterialPropertyBlock` (`sharedMaterial` count unchanged); child `VisualRoot` squashes to `(1.22, 0.78, 1.22)` and recovers to exact `(1.0, 1.0, 1.0)` after `0.15s` without mutating the root `CapsuleCollider` scale.
  - Pass condition: Retained screenshot of Hit-Flash & Squash/Stretch in `production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback.png` + lead sign-off in `production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback-evidence.md` + passing unit test in `AudioVfxPoolTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: a retained screenshot in `production/qa/evidence/` + sign-off in `production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback-evidence.md`

**Status**: [x] Verified (`production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback.png`, `production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback-evidence.md`, `production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback/stamp.txt`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/character-movement-mobility/story-003-dodge-roll-qe-and-visual-tilt.md`, `production/epics/spatial-audio-combat-vfx/story-002-pooled-urp-combat-vfx-manager.md`
- Unlocks: None

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: Retained `1280x720` URP screenshot (`01-mpb-emissive-white-hitflash-and-squash.png`) + SHA-1 `stamp.txt` (`UNCHANGED`) + `AudioVfxPoolTests.cs` (`3.5x` HDR `_EmissionColor` via `GetPropertyBlock`/`SetPropertyBlock`, `0` material clones, `PlayDashStretch` interrupted by hit squash, `50` overlapping hits recovering to `(1, 1, 1)`, `!DOTween.IsTweening` 60Hz scale preservation).
