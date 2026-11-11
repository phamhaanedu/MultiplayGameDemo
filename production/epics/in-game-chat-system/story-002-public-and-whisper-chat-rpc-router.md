# Story 002: Public Broadcast & Private Whisper (`[RpcTarget]`) Chat Router

> **Epic**: In-Game Public & Whisper Chat System
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: 2026-11-10
> **Last Updated**: 2026-11-11

## Context

**GDD**: `design/gdd/in-game-chat-system.md`
**Requirement**: `TR-chat-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Observer-Driven Combat HUD, Mobile 7-Button Action Pad & Persistent RPC Chat
**ADR Decision Summary**: Routes Public messages (`#F8F9FA`) via `Rpc_BroadcastPublicChat` (`RpcTargets.All`) and Private Whispers (`#C77DFF` via `/w <TargetName> <Msg>` or target dropdown) strictly to the recipient using Fusion 2's `[RpcTarget] PlayerRef targetPlayer` parameter (`Rpc_SendTargetedWhisper`) so uninvolved clients never receive whisper packets over the wire.
**ADR Version**: 2026-11-10

**Engine**: Unity 6.6 (URP) + Photon Fusion 2 | **Risk**: MEDIUM
**Engine Notes**: In Photon Fusion 2, `[Rpc(RpcSources.All, RpcTargets.All)] public void Rpc_SendTargetedWhisper([RpcTarget] PlayerRef target, ...)` delivers the RPC exclusively to `target` at the transport layer instead of broadcasting to all peers.

**Control Manifest Rules (this layer)**:
- Required: Sanitize all outgoing/incoming chat strings (strip `<` and `>` rich-text tags, trim whitespace, clamp to `<= 120` chars) and use `[RpcTarget] PlayerRef target` for private whispers.
- Forbidden: Never broadcast private whispers to `RpcTargets.All` and filter them in client UI code, and never render unsanitized user strings into `TMP_Text` with `richText` injection enabled.
- Guardrail: Network payload per message uses `NetworkString<_128>` (`<= 128 bytes`).

---

## Acceptance Criteria

*From GDD `design/gdd/in-game-chat-system.md`, scoped to this story:*

- [x] `ChatCommandParser` parses `/w <TargetName> <Body>` (and `/whisper`) case-insensitively against the active session roster, resolving the target `PlayerRef` or returning a local system error (`[SYSTEM] Player '<Name>' not found` or `Cannot whisper to yourself`).
- [x] `ChatInputSanitizer` trims whitespace, strips `<` and `>` characters to prevent TMP rich-text injection (`<size=999>`, `<color=...>`), and clamps message length to `[1, 120]` characters (`<= 120` UTF-8 bytes for `NetworkString<_128>`).
- [x] `ChatNetworkRouter` sends Public messages via `Rpc_BroadcastPublicChat` (`#F8F9FA`) and Private Whispers via `Rpc_SendTargetedWhisper([RpcTarget] PlayerRef target, PlayerRef sender, NetworkString<_128> body)` (`#C77DFF` `"[From Name]: ..."` on recipient and local `"[To Name]: ..."` echo on sender).

---

## Implementation Notes

*Derived from ADR-0007 Implementation Guidelines:*

Create `Assets/Scripts/UI/Chat/ChatCommandParser.cs` and `Assets/Scripts/UI/Chat/ChatNetworkRouter.cs`. Make `ChatCommandParser.ParseAndSanitize(string rawInput, IReadOnlyDictionary<string, PlayerRef> roster, PlayerRef localPlayer)` a pure static method so EditMode unit tests can test `/w` parsing, rich-text tag stripping, `120`-char clamping, self-whisper rejection, and unknown player handling thoroughly.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 001 (`story-001-canvasgroup-persistent-chat-window.md`): `CanvasGroup` visibility and `InputFocusFlag.ChatFocused` toggling.
- Story 003 (`story-003-token-bucket-rate-limiter-and-row-pool.md`): Token-bucket rate limiter (`5 msgs / 3.0s`) and `50`-row UI pool.

---

## Files

*The code and test files this story creates or changes. `/create-stories` lists the
planned ones; `/dev-story` replaces the list with the files it changed; `/story-done`
checks scope against it.*

- `Assets/Scripts/UI/Chat/ChatCommandParser.cs`
- `Assets/Scripts/UI/Chat/ChatNetworkRouter.cs`
- `Assets/Scenes/Arena.unity`
- `Assets/Tests/EditMode/ChatSystemTests.cs`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation. (At tiers where the QL-STORY-READY gate is skipped — `minimal`, or `lean`/`solo` review mode — no qa-lead specs exist, and this section reads: N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.)*

- **AC-1**: `ChatCommandParser` and `ChatNetworkRouter` route Public chat (`#F8F9FA`) and targeted Private Whispers (`#C77DFF` via `[RpcTarget] PlayerRef`), strip `<`/`>` rich-text tags, and clamp messages to `<= 120` chars.
  - Given: Active player roster `{"Alpha": Player1, "Bravo": Player2, "Charlie": Player3}` with local player `Player1`.
  - When: Parsing and routing `"Hello <size=200>world</size>"`, `"/w Bravo Secret flank!"`, `"/w Alpha self"`, `"/w Unknown hi"`, and a `180`-character string.
  - Then: `"Hello <size=200>world</size>"` sanitizes to `"Hello size=200world/size"` (Public); `"/w Bravo Secret flank!"` resolves `Target = Player2` (`Whisper`); self-whisper and unknown target return local `[SYSTEM]` error without sending network traffic; `180`-char string is clamped to `120` chars.
  - Edge cases: Whitespace-only input (`"   "`) or `/w Bravo   ` (empty whisper body) is ignored without sending an RPC.

---

## Test Evidence

*Governed by `qa.level`: at `qa.level: minimal` tests are **waived** (advisory, never "must exist and pass"), but a Visual/Feel or UI story's retained screenshot is not.*

**Story Type**: Integration
**Required evidence**:
- Integration: `Assets/Tests/EditMode/ChatSystemTests.cs` — must exist and pass

**Status**: [x] Verified (`Assets/Tests/EditMode/ChatSystemTests.cs`, `80/80` EditMode + `8/8` PlayMode `PASS`)

---

## Dependencies

- Depends on: `production/epics/in-game-chat-system/story-001-canvasgroup-persistent-chat-window.md`
- Unlocks: `production/epics/in-game-chat-system/story-003-token-bucket-rate-limiter-and-row-pool.md`

---

## Completion Notes

- **Completed**: 2026-11-11
- **Gates**: `LP-CODE-REVIEW: APPROVE` | `QL-TEST-COVERAGE: ADEQUATE` | `/smoke-check sprint`: `PASS` (`production/qa/smoke-sprint-003-2026-11-11.md`)
- **Evidence**: `ChatSystemTests.cs` verifying `/w` case-insensitive parsing, `<`/`>` rich-text stripping, `120`-byte UTF-8 clamping, `[RpcTarget] PlayerRef` whisper privacy, and single `ChatNetworkRouter` serialized on `MatchSystems` (`NetworkObject`) in `Arena.unity`.
