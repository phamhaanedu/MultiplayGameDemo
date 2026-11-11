# Visual Evidence — Story 002: Pooled URP Combat VFX Manager (`VfxManager`)

- **Story**: `production/epics/spatial-audio-combat-vfx/story-002-pooled-urp-combat-vfx-manager.md`
- **Requirement**: `TR-audio-002` (`ADR-0008`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP World-Space Combat VFX)

## Retained Screenshots
- `production/qa/evidence/story-002-pooled-urp-combat-vfx-manager.png`
- `production/qa/evidence/story-002-pooled-urp-combat-vfx-manager/01-vertical-slash-cyan-and-horizontal-sweep-amber-arcs.png`

## Visual & Technical Art Observations
1. **Orthogonal Slash Planes & Distinct Spark Palettes (`01-vertical-slash-cyan-and-horizontal-sweep-amber-arcs.png`)**: Three-quarter courtyard view over the Central Altar demonstrates simultaneous pooled `PooledVfxInstance` playback from `VfxManager`'s 20-instance pre-warmed pool:
   - Left combatant projects the vertical `YZ`-plane `50 deg` / `2.5m` `#00F5D4` Electric Cyan `VerticalSlash` annular sector blade arc.
   - Right combatant projects the orthogonal horizontal `XZ`-plane `90 deg` / `3.0m` `#FF9F1C` Solar Amber `HorizontalSweepLeft` sector fan.
   - Center target emits `14` `#FFB703` golden-amber player impact sparks alongside `8` `#E2E8F0` cool-slate wall/pillar impact sparks via `Universal Render Pipeline/Particles/Unlit` with `ShadowCastingMode.Off` and `MaterialPropertyBlock` (`GetPropertyBlock`/`SetPropertyBlock`) tinting (`0` runtime `Instantiate`/`Destroy`).

## Sign-Off
- [x] Approved — Art Director / Technical Artist (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
