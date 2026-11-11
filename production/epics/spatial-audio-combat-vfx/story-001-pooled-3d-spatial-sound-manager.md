# Story 001: Pooled 3D Spatial Sound Manager (`SoundManager`)

> **Epic**: 3D Spatial Audio & Combat VFX Feedback
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/spatial-audio-combat-vfx.md`
**Requirement**: `TR-audio-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: PlayFab Async Identity/Leaderboard Resilience & Pooled URP Audio/VFX Feedback
**ADR Decision Summary**: Pre-warms `16` pooled 3D `AudioSource` emitters (`spatialBlend = 1.0f`, `minDistance = 2.5f`, `maxDistance = 28.0f`, `Linear` rolloff, pitch jitter `+-0.07f`) with oldest-voice stealing when all `16` emitters are busy (`0` runtime `Instantiate`/`Destroy`).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Avoiding `AudioSource.PlayClipAtPoint()` (which instantiates and destroys a temporary GameObject on every call) eliminates audio GC spikes during 4-player rapid fire.

**Control Manifest Rules (this layer)**:
- Required: Pre-warm `16` pooled 3D `AudioSource` emitters (`spatialBlend = 1.0f`, `minDistance = 2.5f`, `maxDistance = 28.0f`, `AudioRolloffMode.Linear`), apply random pitch variation `Random.Range(0.93f, 1.07f)`, and steal the oldest active emitter if all `16` are busy.
- Forbidden: Never call `AudioSource.PlayClipAtPoint()` or `Object.Instantiate` for combat SFX at runtime.
- Guardrail: Distance culling — skip playing 3D spatial combat sounds if `Vector3.SqrMagnitude(soundPos - listenerPos) > 30.0f * 30.0f` (`900.0f`).

---

## Acceptance Criteria

*From GDD `design/gdd/spatial-audio-combat-vfx.md`, scoped to this story:*

- [x] `SoundManager` pre-warms a fixed pool of `16` 3D `AudioSource` emitters (`spatialBlend = 1.0f`, `minDistance = 2.5f`, `maxDistance = 28.0f`, `rolloffMode = AudioRolloffMode.Linear`, `SFX_3D` mixer group) plus `2` 2D UI `AudioSource` channels (`UI_2D` mixer group) at startup.
- [x] `Play3D(SoundId id, Vector3 worldPos)` culls requests beyond `30.0m` from the active `AudioListener`, applies pitch jitter in `[0.93f, 1.07f]`, and recycles idle emitters back to the pool when playback completes.
- [x] If all `16` 3D emitters are simultaneously playing (`17th` request), `SoundManager` steals the oldest active voice (`argmin_k startTime_k`) and re-triggers it at the new position with `0` `Instantiate`/`Destroy` calls.

---

## Implementation Notes

*Derived from ADR-0008 Implementation Guidelines:*

Create `Assets/Scripts/Presentation/Audio/SoundManager.cs` and `Assets/Scripts/Presentation/Audio/SoundBankSO.cs` (or procedural `AudioClip` synthesizer fallback so all 9 combat/UI sound cues work out-of-the-box in tests and Play Mode). Expose `AcquireEmitterIndex()` and voice-stealing logic for EditMode unit tests.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-pooled-urp-combat-vfx-manager.md`): Pooled URP `VfxManager` (`50deg`/`90deg` slash arcs and impact sparks).
- Story 003 (`story-003-mpb-hitflash-and-dotween-squash-feedback.md`): `MaterialPropertyBlock` Hit-Flash and DOTween Squash & Stretch.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/Presentation/Audio/SoundManager.cs`
- `Assets/Audio/ArenaAudioMixer.mixer`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/AudioVfxPoolTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `SoundManager` pre-warms `16` pooled 3D `AudioSource` emitters (`spatialBlend=1.0`, `minDistance=2.5m`, `maxDistance=28.0m`, `Linear` rolloff, pitch jitter `+-0.07`) with oldest-voice stealing and `30.0m` distance culling.
  - Given: `SoundManager` initialized with `16` pooled 3D `AudioSource` instances and listener at `(0, 0, 0)`.
  - When: `20` 3D sound requests are triggered within `(5, 0, 5)` and `1` request is triggered at `(35, 0, 0)` (`> 30.0m`).
  - Then: Request at `35.0m` is culled; requests `1..16` occupy the `16` pre-warmed emitters with `spatialBlend == 1.0f`, `minDistance == 2.5f`, `maxDistance == 28.0f`, and pitch in `[0.93f, 1.07f]`; requests `17..20` steal the oldest active emitter (`0` `Instantiate` calls).
  - Edge cases: `AudioClip == null` logs a warning once and returns safely without throwing `NullReferenceException`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/AudioVfxPoolTests.cs` — must exist and pass

**Status**: [x] Verified (`Assets/Tests/EditMode/AudioVfxPoolTests.cs`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/combat-melee-ranged-system/story-003-ranged-projectile-fire-and-hit.md`
- Unlocks: `production/epics/spatial-audio-combat-vfx/story-002-pooled-urp-combat-vfx-manager.md`

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: `AudioVfxPoolTests.cs` verifying `16` 3D `SFX_3D` voices (`spatialBlend=1.0`, `2.5m..28.0m` `Linear` rolloff, `[0.93, 1.07]` pitch jitter, `29.9m` vs `30.1m` culling, idle voice recycling, `17th..20th` oldest-voice stealing) + `2` `UI_2D` channels + `OnDestroy` procedural clip cleanup.
