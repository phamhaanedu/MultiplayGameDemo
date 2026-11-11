# Control Manifest: Capsule Arena: Last Man Standing

> **Generated From**: Accepted ADRs (`ADR-0001` through `ADR-0008`) & `docs/registry/architecture.yaml`
> **Engine**: Unity 6.6 (`6000.6.8f1`, URP) | **Language**: C# 9.0
> **Last Updated**: 2026-11-11

## 1. Global Must-Do Rules (All Systems)

1. **Encapsulation**: Always declare Inspector-tunable fields as `[SerializeField] private` using `_camelCase` naming and expose them via read-only `public` properties (`PascalCase`).
2. **Data-Driven Config (`ADR-0001`, `ADR-0006`, `ADR-0007`)**: Read all gameplay constants, match timing windows, and floating text visuals from `CharacterCombatConfigSO` (`DefaultCapsuleCombatConfig.asset`), `MatchRulesConfigSO` (`DefaultMatchRulesConfig.asset`), and `FloatingDamageTextConfigSO` (`DefaultFloatingDamageConfig.asset`) cached in `Awake()` or `Spawned()`.
3. **Zero-Alloc 60Hz Hot Path (`ADR-0001`, `ADR-0003`, `ADR-0005`)**: Use pre-allocated static buffers with `Physics.OverlapSphereNonAlloc`, `Physics.CapsuleCastNonAlloc`, and `Physics.SphereCastNonAlloc` inside `FixedUpdateNetwork()`.
4. **Shared Mode Safe Network Access & Tick Input (`ADR-0002`, `ADR-0003`)**: Guard all `[Networked]` property reads/writes outside `FixedUpdateNetwork()` with `IsSpawnedOnRunner` (`_isNetworkSpawned && Object != null && Runner != null && Object.IsValid`), resolve owner via `InputAuthority != PlayerRef.None ? InputAuthority : StateAuthority`, and sample local `StateAuthority` simulation input via `LocalInputPoller.SampleSimulationTickInput(Runner.Tick.Raw)`.

## 2. Global Never-Do Rules (Forbidden Patterns)

1. **NEVER** call `GetComponent`, `GameObject.Find`, `FindWithTag`, `Physics.OverlapSphere` (allocating overload), LINQ, or `+` string concatenation inside `Update()`, `LateUpdate()`, or `FixedUpdateNetwork()`.
2. **NEVER** call `UnityEngine.Object.Instantiate` or `Destroy` for projectiles, VFX, or 3D audio emitters while `MatchState == Playing`; always acquire and release through `FusionObjectPoolProvider` (`INetworkObjectProvider`) or local pre-warmed pools (`ADR-0003`, `ADR-0008`).
3. **NEVER** mutate `[Networked] CurrentHP` or `[Networked] IsAlive` from a non-authoritative peer; attackers must send `Rpc_RequestTakeDamage` to the victim's `StateAuthority` (`ADR-0004`).
4. **NEVER** call `gameObject.SetActive(false)` on `InGameChatController`; control visibility exclusively through `CanvasGroup` (`alpha`, `interactable`, `blocksRaycasts`) so Photon Fusion RPCs are never dropped (`ADR-0007`).
5. **NEVER** access `Renderer.material` at runtime for hit-flash effects; always use `Renderer.GetPropertyBlock` / `SetPropertyBlock` with a shared `MaterialPropertyBlock` (`ADR-0008`).
6. **NEVER** scale or tilt the root `NetworkObject` transform (which holds the `CapsuleCollider`); apply all `DOTween` Squash & Stretch and `35°` Dodge Roll tilt effects strictly to `_childVisualRoot` (`ADR-0004`, `ADR-0008`).

## 3. Per-Layer Mandates

| Layer | Governing ADRs | Must-Do Mandates |
|-------|---------------|------------------|
| `Foundation` | `ADR-0001`, `ADR-0002`, `ADR-0003` | Atomic `.tmp` write for `keybindings.json`; merge PC `InputAction` and `MobileInputBridge` via bitwise OR into `NetworkInputData` (`16B`); zero out input when `InputFocusManager` flags are active; pre-warm 32 `NetworkProjectiles` in `FusionObjectPoolProvider`. |
| `Core` | `ADR-0004` | Execute Walk (`6.0 m/s`), Jump (`7.5 m/s`), Dash (`Key 2`, `18.0 m/s`, `12 ticks`), and Dodge Roll (`Keys Q/E`, `12.0 m/s`, `15 ticks`) in `FixedUpdateNetwork()`; validate `IsAlive`, `MatchState == Playing`, and distance tolerance in `Rpc_RequestTakeDamage`; switch dead players to `Layer 10 (GhostSpectator)`. |
| `Feature` | `ADR-0005`, `ADR-0006` | Validate melee sectors (`50°` Vertical Slash `2.5m`, `90°` Asymmetric Horizontal Sweep `3.0m`) with `LineOfSightMask (1 << 6)`; synchronize 1:1 `90°` fan VFX expansion (`VisualSweepProgress`) with `±90°` body and camera yaw turn over `11 ticks` (`0.18s`); read `5.0s` countdown and `60.0s` active late-join window from `MatchRulesConfigSO`; configure `AllowStateAuthorityOverride = true` on `MatchManager` and call `RequestStateAuthority()` on `IsSharedModeMasterClient` failover. |
| `Presentation` | `ADR-0007`, `ADR-0008` | Bind local avatar (`CameraController`, `PlayerMobilityController`, `PlayerCombatController`, `CombatHudPresenter`) first on spawn before optional UI/cloud wiring; update HUD, Billboard, `FloatingDamageTextManager` (unsigned Red/Green, `2/3` grow -> `1/3` shrink+fade, hidden for self in `FPS`), and 7-button `MobileActionPadView` via C# Observer events and `OnChangedRender`; sanitize chat rich-text tags; guard PlayFab `UpdatePlayerStatistics` with `MatchSequenceId` idempotency. |
