# Visual Evidence — Story 003: `MaterialPropertyBlock` Hit-Flash & DOTween Squash/Stretch Feedback

- **Story**: `production/epics/spatial-audio-combat-vfx/story-003-mpb-hitflash-and-dotween-squash-feedback.md`
- **Requirement**: `TR-audio-003` (`ADR-0008`)
- **Date**: 2026-11-11
- **Resolution**: `1280x720` (Unity 6.6 URP Close-Up Impact Verification)

## Retained Screenshots
- `production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback.png`
- `production/qa/evidence/story-003-mpb-hitflash-and-dotween-squash-feedback/01-mpb-emissive-white-hitflash-and-squash.png`

## Visual & Technical Art Observations
1. **MPB Emissive White Hit-Flash & Child `VisualRoot` Squash (`01-mpb-emissive-white-hitflash-and-squash.png`)**: Close-up comparison on the Central Altar captures the struck center capsule (`DemoHitVictim`) mid-impact (`t = 0.052s` inside the `0.12s` flash / `0.15s` squash window), glowing pure HDR white via `MeshRenderer.SetPropertyBlock` (`0` material clones; URP SRP Batcher intact) while its child `VisualRoot` compresses toward `(1.22, 0.78, 1.22)` next to un-hit Cyan (`1, 1, 1`) and Amber (`1, 1, 1`) reference capsules, leaving root `CapsuleCollider` scale strictly `(1, 1, 1)`.

## Sign-Off
- [x] Approved — Art Director / Technical Artist (`2026-11-11`)
- [x] Approved — QA Lead (`2026-11-11`)
