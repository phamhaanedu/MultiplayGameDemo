# Story 001: CanvasGroup Persistent Chat Window & Focus Toggle

> **Epic**: In-Game Public & Whisper Chat System
> **Status**: Complete
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/in-game-chat-system.md`
**Requirement**: `TR-chat-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Observer-Driven Combat HUD, Mobile 7-Button Action Pad & Persistent RPC Chat
**ADR Decision Summary**: Controls the bottom-left Chat Panel (`520x280px`) exclusively via `CanvasGroup.alpha`, `interactable`, and `blocksRaycasts` (`alpha = 1.0` when focused, `0.35` passive fade after `6.0s` idle) while synchronizing `InputFocusFlag.ChatFocused` on `Enter` / `Escape` and keeping the GameObject active at all times so RPCs never drop.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: MEDIUM
**Engine Notes**: Keeping the `ChatPresenter` / `ChatNetworkRouter` GameObject permanently active (`gameObject.activeInHierarchy == true`) ensures Fusion 2 RPC handlers and message queues remain alive even when the panel is visually faded.

**Control Manifest Rules (this layer)**:
- Required: Control Chat visibility exclusively via `CanvasGroup` (`alpha`, `interactable`, `blocksRaycasts`) and set `InputFocusManager.SetFocusFlag(InputFocusFlag.ChatFocused, isFocused)` on `Enter`/`Escape`.
- Forbidden: Never call `chatPanelGameObject.SetActive(false)` to hide or collapse the chat UI.
- Guardrail: Focusing the chat input field must immediately block all `WASD`, `Q/E`, `1/2/3`, and mouse combat actions via `InputFocusFlag.ChatFocused`.

---

## Acceptance Criteria

*From GDD `design/gdd/in-game-chat-system.md`, scoped to this story:*

- [x] `ChatViewController` keeps its root GameObject active (`gameObject.activeSelf == true`) throughout the session and transitions between `Active/Focused` (`CanvasGroup.alpha = 1.0`, `interactable = true`, `blocksRaycasts = true`), `Passive/Unfocused` (`alpha = 0.65`, `interactable = false`), and `Minimized/Fade` (`alpha = 0.35` after `6.0s` without new messages).
- [x] Pressing `Enter` (or clicking `BTN_ChatToggle`) when unfocused activates `InputFocusFlag.ChatFocused` and focuses `TMP_InputField`; pressing `Enter` on an empty input or pressing `Escape` clears `InputFocusFlag.ChatFocused` and returns control to gameplay.
- [x] Incoming chat messages while unfocused temporarily raise `CanvasGroup.alpha` to `0.75` and reset the `6.0s` passive fade timer without stealing keyboard focus from gameplay.

---

## Implementation Notes

*Derived from ADR-0007 Implementation Guidelines:*

Create `Assets/Scripts/UI/Chat/ChatViewController.cs`. Manage `CanvasGroup` state transitions (`SetChatFocus(bool focused)`) and hook `InputFocusManager.SetFocusFlag(InputFocusFlag.ChatFocused, focused)`. Ensure `OnMatchEnded` or scene unload cleanly unfocuses the input field.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002 (`story-002-public-and-whisper-chat-rpc-router.md`): `/w <TargetName> <Message>` parser and targeted/public Fusion 2 RPC routing.
- Story 003 (`story-003-token-bucket-rate-limiter-and-row-pool.md`): Token-bucket rate limiter (`5 msgs / 3.0s`) and `50`-row UI ring buffer pool.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/Chat/ChatViewController.cs`
- `Assets/Scripts/Core/Bootstrap/ArenaSceneBootstrapper.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/ChatSystemTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `ChatViewController` uses `CanvasGroup` (`alpha`, `interactable`, `blocksRaycasts`) without ever calling `SetActive(false)` and synchronizes `InputFocusFlag.ChatFocused` on `Enter`/`Escape`.
  - Setup: Enter Play Mode in `Arena.unity`, press `Enter` to focus Chat, type `"wasd123qe"`, press `Escape` to unfocus, and wait `6.0s` for passive fade.
  - Verify: `chatViewController.gameObject.activeInHierarchy` remains `true` at all times; while focused (`alpha == 1.0`), typing `"wasd123qe"` does not move or attack with the capsule; pressing `Escape` sets `InputFocusFlag.ChatFocused = false` and fades `CanvasGroup.alpha` to `0.35` after `6.0s`.
  - Pass condition: Retained screenshot of Focused and Passive Chat Window states in `production/qa/evidence/story-001-canvasgroup-persistent-chat-window.png` + passing unit test in `ChatSystemTests.cs`.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched, in `production/qa/evidence/story-001-canvasgroup-persistent-chat-window.png`

**Status**: [x] Verified (`production/qa/evidence/story-001-canvasgroup-persistent-chat-window.png`, `production/qa/evidence/story-001-canvasgroup-persistent-chat-window-evidence.md`, `production/qa/evidence/story-001-canvasgroup-persistent-chat-window/stamp.txt`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/keybinding-input-system/story-003-input-focus-manager-gating.md`
- Unlocks: `production/epics/in-game-chat-system/story-002-public-and-whisper-chat-rpc-router.md`, `production/epics/in-game-chat-system/story-003-token-bucket-rate-limiter-and-row-pool.md`

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: Retained `1280x720` URP screenshots (`01-chat-focused-public-and-whisper.png`, `02-chat-passive-minimized.png`) + SHA-1 `stamp.txt` (`UNCHANGED`) + `ChatSystemTests.cs` (`80/80` EditMode PASS).
