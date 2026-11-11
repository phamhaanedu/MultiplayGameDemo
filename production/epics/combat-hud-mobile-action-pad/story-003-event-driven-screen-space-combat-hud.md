# Story 003: Event-Driven Screen-Space Combat HUD & Match End Overlay

> **Epic**: Combat HUD, Overhead Billboard & Mobile Action Pad
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/combat-hud-mobile-action-pad.md`
**Requirement**: `TR-hud-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Observer-Driven Combat HUD, Mobile 7-Button Action Pad & Persistent RPC Chat
**ADR Decision Summary**: Drives the screen-space HUD (Top Match Status Bar, Bottom-Center Local Health & Cooldown Bar, Center Crosshair + Hit-Marker, Top-Right 4-item Pooled Kill Feed, and Victory/Defeat/Draw End Modal) strictly via C# Observer events (`OnHealthChanged`, `OnMatchStateChanged`, `OnPlayerEliminated`) using a pre-allocated `StringBuilder(128)` (`0 B` GC in steady state).
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Updating `TMP_Text` only when an integer second or HP integer changes avoids per-frame string allocations and Canvas rebuilds.

**Control Manifest Rules (this layer)**:
- Required: Subscribe to C# Observer events (`OnHealthChanged`, `OnMatchStateChanged`, `OnPlayerEliminated`, `OnPerspectiveChanged`), format dynamic text with a cached `StringBuilder(128)`, and pool the 4 `KillFeedRow` UI items.
- Forbidden: Never poll `PlayerHealth.CurrentHP` or build strings with `+` / `string.Format` every frame inside `Update()`.
- Guardrail: Refresh the `MM:SS` match timer text only when `Mathf.CeilToInt(remainingSec)` changes (`1 Hz` text update).

---

## Acceptance Criteria

*From GDD `design/gdd/combat-hud-mobile-action-pad.md`, scoped to this story:*

- [x] `CombatHudPresenter` updates the Top Match Status Bar (`Alive: X/4`, `MM:SS` timer pulsing `#E63946` when `<= 30s`, and `[V] TPS / FPS` badge) and Bottom-Center Local Health Bar (`100 / 100` with low-HP red vignette at `<= 25 HP`) strictly on C# state change events.
- [x] The Center Crosshair expands on movement/attack, flashes a `#FFB703` (`#E63946` on lethal) `X` hit-marker for `0.15s` when the local player lands a hit, and displays a `90 deg` directional arc indicator during Horizontal Sweep (`Keys 1/3`).
- [x] The Top-Right Kill Feed recycles a fixed pool of `4` `KillFeedEntryView` rows (auto-fading after `4.0s`), and `MatchState.Ended` opens the Victory (`#FFB703`) / Defeat (`#E63946`) / Draw (`#A8DADC`) modal with `"Return to Lobby"` and `"Play Again"` buttons plus an `8.0s` countdown.

---

## Implementation Notes

*Derived from ADR-0007 Implementation Guidelines:*

Create `Assets/Scripts/UI/HUD/CombatHudPresenter.cs` and `Assets/Scripts/UI/HUD/KillFeedPoolView.cs`. Use a single cached `StringBuilder(128)` for formatting `"100 / 100"`, `"ALIVE: 3 / 4"`, and `"02:45"` (updating timer text only when `_lastDisplayedSecond != currentWholeSecond`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-world-space-overhead-billboard-ui.md`): World-space overhead capsule billboard.
- Story 002 (`story-002-seven-button-mobile-action-pad.md`): 7-button Mobile Action Pad and virtual joystick.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/HUD/CombatHudPresenter.cs`
- `Assets/Scripts/UI/HUD/KillFeedPoolView.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/CombatHudAndPadTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `CombatHudPresenter` updates local HP, cooldowns, alive counter, `MM:SS` timer, 4-row pooled kill feed, and Victory/Defeat modal strictly via C# events with `0 B` steady-state GC.
  - Setup: Enter Play Mode in `Arena.unity`, trigger damage (`100 -> 25 HP`), land a hit on an opponent, trigger 5 sequential kill feed events, and transition `MatchManager` to `MatchState.Ended`.
  - Verify: Local HP bar and low-HP vignette (`<= 25 HP`) update immediately; hit-marker flashes for `0.15s`; kill feed recycles 4 pooled rows without `Instantiate`; Victory/Defeat modal displays accurate winner and stats.
  - Pass condition: Retained screenshots of active Combat HUD and Match End Modal in `production/qa/evidence/story-003-event-driven-screen-space-combat-hud.png` + passing unit test in `CombatHudPresenterTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-003-event-driven-screen-space-combat-hud.png`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/camera-fps-tps-spectator/story-003-post-death-ghost-spectator-controller.md`, `production/epics/match-manager-authority-failover/story-003-masterclient-state-authority-failover.md`, `production/epics/combat-hud-mobile-action-pad/story-001-world-space-overhead-billboard-ui.md`
- Unlocks: None

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-003-event-driven-screen-space-combat-hud-evidence.md` + `Assets/Tests/EditMode/CombatHudAndPadTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
