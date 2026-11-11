# Story 003: Token-Bucket Chat Rate Limiter & 50-Row Ring Buffer Pool

> **Epic**: In-Game Public & Whisper Chat System
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/in-game-chat-system.md`
**Requirement**: `TR-chat-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Observer-Driven Combat HUD, Mobile 7-Button Action Pad & Persistent RPC Chat
**ADR Decision Summary**: Protects RPC bandwidth with a Token-Bucket rate limiter (`Capacity = 5.0` tokens, `RefillRate = 5.0 / 3.0s = 1.667 tokens/s`) and renders message history using a pre-warmed `50`-row `ChatMessageRow` ring buffer so chat scrolling never calls `Object.Instantiate` or `Object.Destroy` at runtime.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) | **Risk**: MEDIUM
**Engine Notes**: Recycling `50` pre-warmed `ChatMessageRow` elements via `transform.SetAsLastSibling()` eliminates layout instantiation spikes during rapid chat bursts.

**Control Manifest Rules (this layer)**:
- Required: Gate outgoing chat RPCs with `ChatRateLimiter.TryConsumeToken(currentTime)` (`5` tokens per `3.0s` window) and pre-warm `50` `ChatMessageRow` UI instances at scene load.
- Forbidden: Never `Instantiate` or `Destroy` `ChatMessageRow` GameObjects when a message arrives.
- Guardrail: On the `51st` message, recycle row index `(totalCount - 1) % 50` in `O(1)` time with `0 B` GameObject allocation.

---

## Acceptance Criteria

*From GDD `design/gdd/in-game-chat-system.md`, scoped to this story:*

- [x] `ChatRateLimiter` implements token-bucket math `tokens = min(5.0f, tokens + dt * (5.0f / 3.0f))`: if `tokens >= 1.0f`, `TryConsumeToken` deducts `1.0f` and returns `true`; if `tokens < 1.0f`, it returns `false` and appends a local `[SYSTEM]` rate-limit warning (`#FFB703`) without sending an RPC.
- [x] `ChatViewController` pre-warms `50` `ChatMessageRow` instances at initialization (`0` active initially).
- [x] Appending messages `1..50` activates pre-warmed rows sequentially, and appending message `51+` overwrites and moves the oldest row (`index = (count - 1) % 50`) to the bottom via `transform.SetAsLastSibling()` with `0` `Instantiate`/`Destroy` calls.

---

## Implementation Notes

*Derived from ADR-0007 Implementation Guidelines:*

Create `Assets/Scripts/UI/Chat/ChatRateLimiter.cs` and `Assets/Scripts/UI/Chat/ChatMessageRingBuffer.cs`. Make `ChatRateLimiter` take an explicit `float currentTimeSec` parameter in `TryConsume(float currentTimeSec)` so unit tests can simulate bursty messages (`6` messages at `t = 0.0s`) and token recovery (`t = 0.6s`) deterministically.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-canvasgroup-persistent-chat-window.md`): `CanvasGroup` visibility and focus management.
- Story 002 (`story-002-public-and-whisper-chat-rpc-router.md`): `/w` whisper parser and `[RpcTarget]` RPC transport.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/Chat/ChatRateLimiter.cs`
- `Assets/Scripts/UI/Chat/ChatMessageRingBuffer.cs`
- `Assets/Tests/EditMode/ChatSystemTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `ChatRateLimiter` blocks messages exceeding `5 msgs / 3.0s`, and `ChatMessageRingBuffer` recycles `50` pre-warmed UI rows in `O(1)` without runtime `Instantiate`/`Destroy`.
  - Given: `ChatRateLimiter(capacity: 5f, windowSec: 3.0f)` at `t = 0.0s` and a `ChatMessageRingBuffer` pre-warmed with `50` row objects.
  - When: `6` messages are submitted at `t = 0.0s`, `1` message is submitted at `t = 0.65s`, and `60` total messages are pushed into the ring buffer.
  - Then: Messages `1..5` at `t = 0.0s` return `true`, message `6` at `t = 0.0s` returns `false` (rate-limited), message at `t = 0.65s` returns `true` (`0.65 * 1.667 >= 1.0` token refilled); after `60` messages, active row count is capped at `50` with row `0..9` recycled as the newest bottom children and `0` `Instantiate`/`Destroy` calls.
  - Edge cases: Large idle gap (`t = 300s`) clamps tokens at `5.0f` (never accumulates above capacity).

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Logic
**Required evidence**:
- Logic: `Assets/Tests/EditMode/ChatSystemTests.cs` — must exist and pass (`/story-done` checks that it EXISTS; pass/fail is established by `/gate-check` and `/smoke-check`, both later)

**Status**: [x] Verified (`Assets/Tests/EditMode/ChatSystemTests.cs`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/in-game-chat-system/story-002-public-and-whisper-chat-rpc-router.md`
- Unlocks: None

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: `ChatSystemTests.cs` verifying `5 msgs / 3.0s` token bucket (`6th` blocked with `#FFB703` `[SYSTEM]` warning, `0.65s` refill, `300s` cap) and `50`-row `ChatMessageRingBuffer` `O(1)` modulo recycling across `75` messages (`0` `Instantiate`/`Destroy`).
