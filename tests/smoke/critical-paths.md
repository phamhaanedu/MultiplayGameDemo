# Smoke Test: Critical Paths

**Purpose**: Run these 10-15 checks in under 15 minutes before any QA hand-off.
**Run via**: `/smoke-check` (which reads this file)
**Update**: Add new entries when new core systems are implemented across Sprints 001–003.

## Core Stability (always run)

1. Game launches to Lobby / Main Menu scene without crash or unhandled console exceptions
2. Photon Fusion 2 Shared Mode session (`60Hz` tick rate) can be joined or created from the Lobby
3. Lobby UI and Keybinding Rebind modal respond to Keyboard/Mouse and Touch inputs without freezing

## Core Mechanic (update per sprint)

<!-- Add the primary mechanic for each sprint here as it is implemented -->
4. [Sprint 001 — Update as Epics 1–6 land]: Capsule spawns via max-min Euclidean spawn selector, moves/jumps/sprints, executes Forward Dash (`Key 2`, `12` ticks) & Dodge Roll (`Q/E`, `15` ticks), and processes Health/Damage RPCs
5. [Sprint 002 — Update as Epics 7–11 land]: Vertical Slash (`LMB`), Horizontal Sweep + `90°` turn (`RMB`), Ranged Fire (`Key 1` pooled projectile), FPS/TPS camera toggle (`V`), Ghost Spectator on elimination, and Last-Man-Standing MatchManager failover

## Data Integrity

6. Custom keybinding overrides persist to and reload cleanly from local JSON (`Application.persistentDataPath`) without corrupting default bindings (once Epic 3/11 is implemented)
7. PlayFab custom-ID silent login and end-of-match Leaderboard stat submission/retrieval complete with graceful offline fallback (once Epic 13 is implemented)

## Performance

8. Stable `60 FPS` (`<= 16.67 ms` frame budget, `<= 150` draw calls) on target PC and Mobile profiles with URP
9. Zero managed heap growth (`0 B` GC in `FixedUpdateNetwork()`) and `<= 512 MB` total memory over 5 minutes of active arena combat (pooled projectiles and VFX recycled cleanly)
