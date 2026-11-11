# Epic: 3D Spatial Audio & Combat VFX Feedback System

> **Layer**: Presentation
> **GDD**: design/gdd/spatial-audio-combat-vfx.md
> **Architecture Module**: SpatialAudioAndVFX
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này hoàn thiện lớp phản hồi thị giác và thính giác ("Game Feel" / Juiciness) thông qua `SoundManager`, `CombatVFXManager`, và `HitFeedbackController`. Theo quyết định tại `ADR-0008`, `SoundManager` định tuyến âm thanh qua 4 nhóm `AudioMixer` (`Master`, `BGM`, `SFX_3D`, `UI_2D`) và quản lý hồ chứa `16` nguồn phát âm thanh không gian 3D (`spatialBlend = 1.0`, khoảng cách suy giảm `2.0m..28.0m`, biến thiên cao độ ngẫu nhiên `±8%`). `CombatVFXManager` quản lý hồ chứa `20` hiệu ứng hạt/vệt kiếm URP tuân thủ chặt chẽ bảng màu Art Bible: Vàng Hổ Phách (`#F59E0B`) cho Chém dọc `LMB`, Xanh Băng (`#38BDF8`) cho Chém ngang Xoay `90°` `Keys 1/3`, và Vàng Chanh (`#FACC15`) cho Đạn tầm xa `RMB`. Hiệu ứng nháy sáng trúng đòn (`HitFlash 0.08s`) sử dụng `MaterialPropertyBlock` dùng chung (`_EmissionColor`) kết hợp `DOTween` Squash & Stretch `(1.28, 0.72, 1.28)` trên `ChildVisualRoot` để đảm bảo `0` bản sao vật liệu dư thừa và `0 byte` cấp phát rác.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0008: PlayFab Cloud Identity/Leaderboard Sync & Pooled 3D Spatial Audio/URP Combat VFX | Establishes the 16-emitter 3D `SoundManager` pool, 20-instance URP `CombatVFXManager` pool, and zero-leak `MaterialPropertyBlock` + `DOTween` hit feedback on `ChildVisualRoot`. | MEDIUM |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-audio-001 | SoundManager must route audio through 4 AudioMixer groups (Master, BGM, SFX_3D, UI_2D) using 16 pooled 3D AudioSources (spatialBlend=1.0, minDistance=2.0m, maxDistance=28.0m) with +/-8% random pitch variation | ADR-0008 ✅ |
| TR-audio-002 | CombatVFXManager must render pooled URP particle and trail effects for Vertical Slash (#F59E0B), Horizontal Sweep (#38BDF8), Ranged Projectile/Impact (#FACC15), and Dash/Dodge trails with zero runtime shader compilation or GC allocation | ADR-0008 ✅ |
| TR-audio-003 | HitStop and HitFlash feedback must apply a 0.08s MPB _EmissionColor flash, DOTween squash-and-stretch (1.28, 0.72, 1.28), and bounded camera trauma shake without mutating shared materials or collider transforms | ADR-0008 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/spatial-audio-combat-vfx.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Pooled 3D Spatial Sound Manager (`SoundManager`)](story-001-pooled-3d-spatial-sound-manager.md) | Integration | M | Ready | TR-audio-001 |
| 002 | [Pooled URP Combat VFX Manager (`VfxManager`)](story-002-pooled-urp-combat-vfx-manager.md) | Visual/Feel | M | Ready | TR-audio-002 |
| 003 | [`MaterialPropertyBlock` Hit-Flash & DOTween Squash/Stretch Feedback](story-003-mpb-hitflash-and-dotween-squash-feedback.md) | Visual/Feel | S | Ready | TR-audio-003 |
