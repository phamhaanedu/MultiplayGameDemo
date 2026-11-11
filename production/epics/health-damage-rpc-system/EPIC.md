# Epic: Health, Damage RPC & Death State Machine

> **Layer**: Core
> **GDD**: design/gdd/health-damage-rpc-system.md
> **Architecture Module**: PlayerHealthRPC
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này triển khai hệ thống máu và sát thương ủy quyền tại nạn nhân (`Victim-Authoritative Health` trong `PlayerHealthController`) nhằm giải quyết triệt để bài toán tranh chấp quyền ghi trạng thái trong Photon Fusion 2 Shared Mode. Theo quyết định tại `ADR-0004`, chỉ duy nhất máy nắm giữ `Object.HasStateAuthority` của nạn nhân mới được phép ghi vào `[Networked] int CurrentHP` (`0..100`) và `[Networked] NetworkBool IsAlive`. Khi người tấn công phát hiện va chạm, họ gửi `Rpc_RequestTakeDamage(int rawDamage, byte attackType, PlayerRef attacker, int attackTick)` tới `RpcTargets.StateAuthority`; máy của nạn nhân kiểm tra trạng thái sống (`IsAlive`), trạng thái trận đấu (`MatchState == Playing`) và ngưỡng khoảng cách hợp lệ trước khi trừ máu. Khi `CurrentHP == 0`, nhân vật lập tức chuyển sang `IsAlive = false`, đổi tầng vật lý sang `Layer 10 (GhostSpectator)` để không cản đòn đánh của người còn sống, và phát `Rpc_NotifyKillConfirmed` tới toàn phòng.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0004: Tick-Accurate Mobility (Dash/Dodge) & Victim-Authoritative Health RPC | Enforces victim `StateAuthority` write ownership over `CurrentHP` and `IsAlive` via `Rpc_RequestTakeDamage` validation and deterministic transition to `Layer 10 (GhostSpectator)` on death. | HIGH |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-health-001 | PlayerHealthController must synchronize [Networked] CurrentHP (0..100) and [Networked] IsAlive with write authority restricted strictly to the victim's Object.HasStateAuthority | ADR-0004 ✅ |
| TR-health-002 | Rpc_RequestTakeDamage(int rawDamage, byte attackType, PlayerRef attacker, int attackTick) must route from [RpcSources.All] to [RpcTargets.StateAuthority] and validate IsAlive, MatchState==Playing, and distance tolerance before deducting HP | ADR-0004 ✅ |
| TR-health-003 | Transitioning to CurrentHP==0 must set IsAlive=false, switch physics layer to Layer 10 (GhostSpectator), disable combat hitboxes, and broadcast Rpc_NotifyKillConfirmed to all peers | ADR-0004 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/health-damage-rpc-system.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Victim-Authoritative Networked Health (`PlayerHealth`)](story-001-victim-authoritative-networked-health.md) | Logic | S | Ready | TR-health-001 |
| 002 | [Damage Request RPC & Distance Sanity Validation](story-002-damage-request-rpc-validation.md) | Integration | M | Ready | TR-health-002 |
| 003 | [Death State & Ghost Spectator Layer Transition](story-003-death-state-and-ghost-layer-transition.md) | Integration | M | Ready | TR-health-003 |
