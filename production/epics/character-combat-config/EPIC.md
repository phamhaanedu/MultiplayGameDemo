# Epic: Character & Combat Config Data (`CharacterCombatConfigSO`)

> **Layer**: Foundation
> **GDD**: design/gdd/character-combat-config.md
> **Architecture Module**: CombatConfig
> **Status**: Ready
> **Stories**: 3 stories

## Overview

Epic này xây dựng nền tảng dữ liệu bất biến lúc runtime (`CharacterCombatConfigSO : ScriptableObject`) và tài sản cấu hình chuẩn `DefaultCapsuleCombatConfig.asset` cho toàn bộ 14 hệ thống của **Capsule Arena: Last Man Standing**. Thay vì phân tán hằng số trên từng `MonoBehaviour` hoặc tính toán lại hàm lượng giác/quy đổi tick trên đường nóng mạng `60Hz` (`FixedUpdateNetwork`), mô-đun `CombatConfig` đóng gói toàn bộ 5 nhóm thông số (Sinh tồn, Di chuyển, Cơ động Dash/Dodge, Chiến đấu Cận chiến/Tầm xa, và Biểu diễn) vào các trường `[SerializeField] private` kèm cơ chế tự động kẹp biên an toàn trong `OnValidate()` và tiền tính toán sẵn (`DashTicks = 12`, `DodgeTicks = 15`, `HorizontalTurnTicks = 11`, `ProjectileLifetimeTicks = 120`, `VerticalSlashCosHalfAngle = 0.9063078`, `HorizontalSweepCosHalfAngle = 0.7071068`) trong `OnEnable()`. Rủi ro quên gán tài sản cấu hình trên Prefab được kiểm soát bằng kiểm tra bất biến trong `Awake()` và bộ kiểm thử tự động `EditMode`.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|------------------|-------------|
| ADR-0001: Immutable Combat Config SO & Arena Physics Layer Architecture | Encapsulates all combat and mobility constants in an immutable `CharacterCombatConfigSO` with precomputed tick and cosine caches (`0 B` runtime GC). | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-config-001 | CharacterCombatConfigSO must expose read-only public C# properties backed by [SerializeField] private fields with OnValidate() clamping and zero runtime heap allocation on 60Hz FixedUpdateNetwork reads | ADR-0001 ✅ |
| TR-config-002 | CharacterCombatConfigSO must precompute derived tick and cosine thresholds (DashTicks=12, DodgeTicks=15, VerticalSlashCosHalfAngle=0.9063078, HorizontalSweepCosHalfAngle=0.7071068) in OnEnable() and OnValidate() | ADR-0001 ✅ |
| TR-config-003 | DefaultCapsuleCombatConfig.asset must enforce canonical combat balance (MaxHP=100, MoveSpeed=6.0, DashSpeed=18.0, DodgeSpeed=12.0, VerticalDamage=25, HorizontalDamage=30, RangedDamage=15) | ADR-0001 ✅ |

## Definition of Done

- Every story implemented, reviewed and closed with `/story-done`
- Every acceptance criterion of design/gdd/character-combat-config.md verified
- Logic and Integration stories have passing tests in the test root
- Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/`
  (a lead sign-off too for Visual/Feel)

## Stories

| # | Story | Type | Estimate | Status | TR-ID |
|---|-------|------|----------|--------|-------|
| 001 | [Combat Config Encapsulation & Validation](story-001-combat-config-encapsulation.md) | Logic | S | Ready | TR-config-001 |
| 002 | [Precomputed Tick & Cosine Threshold Cache](story-002-precomputed-tick-and-cosine-cache.md) | Logic | S | Ready | TR-config-002 |
| 003 | [Default Capsule Combat Config Asset & Balance Baseline](story-003-default-combat-config-asset.md) | Config/Data | S | Ready | TR-config-003 |
