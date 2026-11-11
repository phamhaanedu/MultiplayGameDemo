# UI Evidence — Story 001: CanvasGroup Persistent Chat Window & Focus Toggle

- **Story**: `production/epics/in-game-chat-system/story-001-canvasgroup-persistent-chat-window.md`
- **Requirement**: `TR-chat-001` (`ADR-0007`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP + Persistent `ChatCanvas`)

## Retained Screenshots
- `production/qa/evidence/story-001-canvasgroup-persistent-chat-window.png`
- `production/qa/evidence/story-001-canvasgroup-persistent-chat-window/01-chat-focused-public-and-whisper.png`
- `production/qa/evidence/story-001-canvasgroup-persistent-chat-window/02-chat-passive-minimized.png`

## Visual & UI Observations
1. **Focused Chat Window (`01-chat-focused-public-and-whisper.png`)**: Bottom-left `520x280px` `ChatViewController` renders at `CanvasGroup.alpha = 1.0`, `interactable = true`, `blocksRaycasts = true` while holding `InputFocusFlag.ChatFocused`. Public messages render in `#F8F9FA`, incoming/outgoing whispers (`[From Valkyrie_2]` / `[To Valkyrie_2]`) render in `#C77DFF`, system feedback renders in `#FFB703`, and the input bar actively edits `/w Valkyrie_2 Pushing Altar now!` alongside the `SEND` button.
2. **Passive Minimized State (`02-chat-passive-minimized.png`)**: After `Escape` and `> 6.0s` idle decay, `ChatViewController` fades to `CanvasGroup.alpha = 0.35`, `interactable = false`, `blocksRaycasts = false` while keeping `gameObject.activeSelf == true` permanently so Photon Fusion 2 RPC handlers never drop incoming messages.

## Sign-Off
- [x] Approved — UI Designer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
