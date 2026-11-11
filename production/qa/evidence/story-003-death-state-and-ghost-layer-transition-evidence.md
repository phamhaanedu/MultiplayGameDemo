# Visual & Integration Evidence — Story 003: Death State & Ghost Spectator Layer Transition

- **Story**: `production/epics/health-damage-rpc-system/story-003-death-state-and-ghost-layer-transition.md`
- **Requirement**: `TR-health-003` (`ADR-0004`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP 17 Perspective Render)

## Retained Screenshots
- `production/qa/evidence/story-003-death-state-and-ghost-layer-transition/01-ghost-spectator-capsule.png`
- `production/qa/evidence/story-003-death-state-and-ghost-layer-transition-01.png`
- `production/qa/evidence/story-003-death-state-and-ghost-layer-transition.png`

## Visual & Layer Transition Observations
1. **Ghost Material Transparency (`M_Capsule_Ghost.mat`)**: Translucent cyan-grey `#A8DADC` (`Alpha = 0.35`) URP Transparent surface clearly reveals both the `#F4A261` altar edge and the `#3A3D56` metric grid floor lines behind the eliminated capsule while keeping the silhouette legible for spectators.
2. **Immediate Layer 10 (`GhostSpectator`) Non-Despawn Transition**: `PlayerDeathStateController.ApplyDeathState()` (invoked immediately inside `PlayerHealth.CommitAuthoritativeHealthMutation` on the lethal tick, synchronized in `Spawned()` for late joiners and `Render()` `ChangeDetector` for proxies) recursively switches the avatar hierarchy from `Layer 8 (PlayerCharacter)` to `Layer 10 (GhostSpectator)` (which collides **only** with `Layer 9 ArenaFloor` per `ArenaLayers.ApplyCollisionMatrix`) without calling `Runner.Despawn(Object)`.
3. **HUD, Hitbox & Nameplate Scrubbing**: Hides the overhead HP bar and local Mobile Action Pad (`alpha = 0`), disables `_combatHitboxRoot`, dims nameplate opacity to `50%` (`alpha = 0.50f`) with `" [GHOST]"` via reusable `StringBuilder`, and fires `OnEliminationEffectsRequested` + `OnLocalSpectatorModeEntered`. Verified by `PlayerHealthTests.LethalSequence_ConcurrentKillingBlows_ClampsZeroFiresKillConfirmedAndAppliesFullGhostStateImmediately`.

## Sign-Off
- [x] Approved — Lead Programmer (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
