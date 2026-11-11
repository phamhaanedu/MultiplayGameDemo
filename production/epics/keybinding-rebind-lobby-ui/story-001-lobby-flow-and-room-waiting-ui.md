# Story 001: Lobby Flow, Room Code Validation & Waiting Room UI

> **Epic**: In-Game Keybinding Rebind UI & Lobby Flow
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/keybinding-rebind-lobby-ui.md`
**Requirement**: `TR-rebind-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Keybinding JSON Persistence, Dual PC/Mobile Input Polling & InputFocusManager
**ADR Decision Summary**: Implements `LobbyViewController` for DisplayName entry (`3..16` alphanumeric/underscore chars), Room Code entry (`3..16` chars), Quick Join / Create or Join Room buttons, and the 4-slot Room Waiting overlay with `10.0s` timeout error recovery.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: LOW
**Engine Notes**: Immediate button lock (`interactable = false`) on click prevents duplicate `NetworkRunner.StartGame` calls during async Photon handshake.

**Control Manifest Rules (this layer)**:
- Required: Validate `DisplayName` (`^[a-zA-Z0-9_]{3,16}$`, fallback `"Player_" + Random(1000,9999)`) and `RoomCode` (`^[a-zA-Z0-9_-]{3,16}$`), and lock action buttons immediately upon initiating a connection.
- Forbidden: Never allow duplicate `StartGame` requests while a connection handshake is in progress.
- Guardrail: Display a non-blocking toast error (`ToastNotificationView`) and re-enable Lobby controls if connection fails or times out (`10.0s`).

---

## Acceptance Criteria

*From GDD `design/gdd/keybinding-rebind-lobby-ui.md`, scoped to this story:*

- [x] `LobbyViewController` provides `InputField_DisplayName` (`3..16` chars, auto-generating `"Player_XXXX"` if blank), `InputField_RoomCode` (auto-uppercase `3..16` chars), `BTN_QuickJoin`, `BTN_CreateOrJoinRoom`, `BTN_OpenLeaderboard`, and `BTN_OpenSettings`.
- [x] Clicking `BTN_QuickJoin` or `BTN_CreateOrJoinRoom` validates inputs, locks Lobby buttons (`interactable = false`), displays the connecting spinner, and delegates to `FusionSessionManager`.
- [x] Once connected, the 4-slot Room Waiting view displays connected players (`1/4..4/4`), color swatches, `[HOST]` badge, and `"Waiting for players (1/4)..."` or `"Match starting in 3... 2... 1..."` countdown banner.

---

## Implementation Notes

*Derived from ADR-0002 Implementation Guidelines:*

Create `Assets/Scripts/UI/Lobby/LobbyViewController.cs` and `Assets/Scripts/UI/Lobby/ToastNotificationView.cs`. Expose static validation helpers `ValidateOrGenerateDisplayName(string rawName)` and `IsValidRoomCode(string roomCode)` for EditMode unit tests.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-interactive-keybinding-rebind-modal.md`): Interactive 13-action keybinding rebind modal.
- Story 003 (`story-003-mobile-pad-visibility-mode-toggle.md`): `MobileControlMode` (`Auto / ForceOn / ForceOff`) toggle.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/Lobby/LobbyViewController.cs`
- `Assets/Scripts/UI/Lobby/ToastNotificationView.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/LobbyAndRebindUiTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `LobbyViewController` validates `DisplayName` (`3..16` chars) and `RoomCode`, locks buttons during connection, displays the 4-slot waiting roster, and surfaces toast errors on failure.
  - Setup: Open Lobby scene/panel in Play Mode, test empty/invalid/valid `DisplayName` and `RoomCode`, click `BTN_QuickJoin`, and observe transition to the 4-slot Room Waiting overlay.
  - Verify: Blank `DisplayName` generates `"Player_XXXX"`; invalid `RoomCode` (`"a!"`) shows validation toast; valid click disables buttons immediately and transitions to the 4-slot waiting room showing `[HOST]` badge and countdown banner.
  - Pass condition: Retained screenshots of Main Lobby and Room Waiting views in `production/qa/evidence/story-001-lobby-flow-and-room-waiting-ui.png` + passing unit test in `LobbyValidationTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-001-lobby-flow-and-room-waiting-ui.png`

**Status**: [x] Verified

---

## Dependencies

- Depends on: `production/epics/shared-mode-session-pool/story-001-fusion-shared-mode-session-bootstrap.md`
- Unlocks: `production/epics/keybinding-rebind-lobby-ui/story-002-interactive-keybinding-rebind-modal.md`

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 3/3 passing
**Deviations**: None
**Test Evidence**: `production/qa/evidence/story-001-lobby-flow-and-room-waiting-ui-evidence.md` + `Assets/Tests/EditMode/LobbyAndRebindUiTests.cs`
**Run result**: OBSERVED — verified in Unity 6.6 (`6000.6.8f1`) EditMode (`69/69 PASS`), PlayMode (`8/8 PASS`), retained `1280x720` URP screenshots + SHA-1 `stamp.txt` receipts, and `/smoke-check sprint` (`production/qa/smoke-sprint-002-2026-11-11.md`)
**Reviewed by**: lead-programmer (`LP-CODE-REVIEW: APPROVE`), qa-lead (`QL-TEST-COVERAGE: ADEQUATE`)
