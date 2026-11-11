# Story 001: World-Space Overhead Billboard UI (`OverheadBillboardUI`)

> **Epic**: Combat HUD, Overhead Billboard & Mobile Action Pad
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/combat-hud-mobile-action-pad.md`
**Requirement**: `TR-hud-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Observer-Driven Combat HUD, Mobile 7-Button Action Pad & Persistent RPC Chat
**ADR Decision Summary**: Rotates each player's world-space overhead canvas (`Y = +2.35m`) to face `MainCamera` in `LateUpdate()` via `Quaternion.LookRotation(transform.position - camPos)`, scales smoothly by distance (`clamp(d / 8.0m, 0.75, 1.35)`), and animates dual-fill HP bars (`Instant` + `0.35s` trailing damage bar) on `OnHealthChanged`.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: World-Space `Canvas` with `GraphicRaycaster` disabled (`enabled = false`) and cached `Camera.main` transform in `LateUpdate()` avoids per-frame UI raycast overhead.

**Control Manifest Rules (this layer)**:
- Required: Cache the active `Camera` transform reference, update billboard rotation and distance scaling inside `LateUpdate()`, and update HP bar fill amounts exclusively when `PlayerHealth.OnHealthChanged` fires.
- Forbidden: Never call `Camera.main` inside `Update()` without caching or leave `GraphicRaycaster` enabled on non-interactive world-space overhead canvases.
- Guardrail: Hide the local player's own overhead billboard when `CameraController` is in `FPS` mode (`Mode == FPS`), and hide the HP bar (`append "[GHOST]"` at `50%` opacity) when `IsDead == true`.

---

## Acceptance Criteria

*From GDD `design/gdd/combat-hud-mobile-action-pad.md`, scoped to this story:*

- [x] `OverheadBillboardUI` anchors at `localPosition = (0, 2.35f, 0)` above each capsule, faces the active camera in `LateUpdate()`, and applies distance scale `s_billboard = Mathf.Clamp(dist / 8.0f, 0.75f, 1.35f)`.
- [x] Subscribing to `PlayerHealth.OnHealthChanged`, `OverheadBillboardUI` snaps `Img_HPFill_Instant.fillAmount = currentHP / 100f` immediately and tweens `Img_HPFill_Delayed.fillAmount` (`#FFB703`) over `0.35s` (`Ease.OutQuad`).
- [x] When `IsDead == true`, the overhead HP bar hides and `Txt_PlayerName` displays `"[PlayerName] [GHOST]"` at `alpha = 0.50`; when the local player switches to `FPS` mode, their own overhead billboard hides so it never blocks top-of-screen vision.

---

## Implementation Notes

*Derived from ADR-0007 Implementation Guidelines:*

Create `Assets/Scripts/UI/HUD/OverheadBillboardUI.cs`. Provide `CalculateBillboardScale(float distance, float refDist = 8.0f, float minScale = 0.75f, float maxScale = 1.35f)` as a pure static helper for unit testing, and wire `OnHealthChanged`, `OnPlayerDied`, and `CameraController.OnPerspectiveChanged` event subscriptions in `OnEnable()`/`OnDisable()`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-seven-button-mobile-action-pad.md`): Bottom-right 7-button Mobile Action Pad and virtual joystick.
- Story 003 (`story-003-event-driven-screen-space-combat-hud.md`): Screen-space HP bar, cooldown slots, match timer, kill feed, and Victory/Defeat overlay.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/HUD/OverheadBillboardUI.cs`
- `Assets/Scripts/UI/Common/UiThemeAndFontProvider.cs`
- `Assets/Scripts/Core/Tweening/DOTweenCompat.cs`
- `Assets/Prefabs/Player/PlayerCapsule.prefab`
- `Assets/Tests/EditMode/CombatHudAndPadTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `OverheadBillboardUI` rotates toward `MainCamera` in `LateUpdate()`, scales by `clamp(d / 8.0m, 0.75, 1.35)`, updates dual-fill HP bars via `OnHealthChanged`, and hides in `FPS` mode or upon Ghost transition.
  - Setup: Enter Play Mode in `Arena.unity` with 2 capsules at `4m`, `8m`, and `20m` distances, deal `25` damage, toggle `Key V` (`FPS`/`TPS`), and eliminate the target.
  - Verify: Overhead billboard faces camera at all angles, scales clamped within `[0.75, 1.35]`, instant HP bar drops to `0.75` while yellow delayed bar trails over `0.35s`, local billboard hides in `FPS` mode, and dead target hides HP bar and shows `"[GHOST]"` at `50%` opacity.
  - Pass condition: Retained screenshot in `production/qa/evidence/story-001-world-space-overhead-billboard-ui.png` + passing unit test in `CombatHudPresenterTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-001-world-space-overhead-billboard-ui.png`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/health-damage-rpc-system/story-001-victim-authoritative-networked-health.md`
- Unlocks: `production/epics/combat-hud-mobile-action-pad/story-003-event-driven-screen-space-combat-hud.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-001-world-space-overhead-billboard-ui-evidence.md` + `Assets/Tests/EditMode/CombatHudAndPadTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
