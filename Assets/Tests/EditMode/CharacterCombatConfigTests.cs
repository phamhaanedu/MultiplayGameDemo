using System;
using System.Reflection;
using CapsuleArena.Core.Config;
using CapsuleArena.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying CharacterCombatConfigSO encapsulation, validation clamping,
    /// cross-field velocity/cooldown guards, null-config consumer fail-safe, precomputed 60Hz
    /// tick/cosine/range caches, zero-GC hot-path reads, and single-source canonical asset values
    /// (Stories 1-1, 1-2, 1-3; ADR-0001; TR-config-001..003).
    /// </summary>
    [TestFixture]
    public class CharacterCombatConfigTests
    {
        private const string CanonicalAssetPath = "Assets/Data/Config/DefaultCapsuleCombatConfig.asset";
        private CharacterCombatConfigSO _runtimeInstance;

        [SetUp]
        public void SetUp()
        {
            _runtimeInstance = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_runtimeInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_runtimeInstance);
            }
        }

        [Test]
        public void Encapsulation_ReflectionInspection_ExposesZeroMutablePublicFieldsOrSetters()
        {
            Type type = typeof(CharacterCombatConfigSO);
            FieldInfo[] publicFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Assert.AreEqual(0, publicFields.Length, "CharacterCombatConfigSO must not expose mutable public instance fields.");

            PropertyInfo[] properties = type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Assert.GreaterOrEqual(properties.Length, 35, "Expected all combat & derived properties to be declared.");

            for (int i = 0; i < properties.Length; i++)
            {
                MethodInfo setter = properties[i].GetSetMethod(nonPublic: false);
                Assert.IsNull(setter, $"Property '{properties[i].Name}' must not expose a public setter.");
            }
        }

        [Test]
        public void ValidateAndClamp_WithOutOfRangeNaNAndInvertedInputs_ClampsToSafeBounds()
        {
            SetPrivateField(_runtimeInstance, "_maxHealth", -25);
            SetPrivateField(_runtimeInstance, "_moveSpeed", 0f);
            SetPrivateField(_runtimeInstance, "_dashSpeed", float.NaN);
            SetPrivateField(_runtimeInstance, "_dashCooldownSec", -4f);
            SetPrivateField(_runtimeInstance, "_verticalSlashAngleDeg", 720f);
            SetPrivateField(_runtimeInstance, "_horizontalSweepAngleDeg", -15f);
            SetPrivateField(_runtimeInstance, "_rangedProjectileSpeed", float.PositiveInfinity);
            SetPrivateField(_runtimeInstance, "_rangedFireCooldownSec", float.NegativeInfinity);
            SetPrivateField(_runtimeInstance, "_gravity", 5.0f);

            _runtimeInstance.ValidateAndClamp();

            Assert.AreEqual(1, _runtimeInstance.MaxHealth);
            Assert.GreaterOrEqual(_runtimeInstance.MoveSpeed, 0.05f);
            Assert.AreEqual(18.0f, _runtimeInstance.DashSpeed, 0.001f);
            Assert.GreaterOrEqual(_runtimeInstance.DashCooldownSec, 0.05f);
            Assert.AreEqual(180.0f, _runtimeInstance.VerticalSlashAngleDeg, 0.001f, "Story 1-1 AC-2 max cone angle is 180 deg.");
            Assert.AreEqual(5.0f, _runtimeInstance.HorizontalSweepAngleDeg, 0.001f, "Story 1-1 AC-2 min cone angle is 5 deg.");
            Assert.AreEqual(15.0f, _runtimeInstance.RangedProjectileSpeed, 0.001f);
            Assert.AreEqual(0.4f, _runtimeInstance.RangedFireCooldownSec, 0.001f);
            Assert.Less(_runtimeInstance.Gravity, 0f);

            // Cross-field velocity inversion & sweep turn-overlap guards:
            SetPrivateField(_runtimeInstance, "_moveSpeed", 10.0f);
            SetPrivateField(_runtimeInstance, "_dashSpeed", 8.0f);
            SetPrivateField(_runtimeInstance, "_dodgeSpeed", 6.0f);
            SetPrivateField(_runtimeInstance, "_horizontalSweepTurnDurationSec", 0.50f);
            SetPrivateField(_runtimeInstance, "_horizontalSweepCooldownSec", 0.20f);
            _runtimeInstance.ValidateAndClamp();

            Assert.Greater(_runtimeInstance.DashSpeed, _runtimeInstance.MoveSpeed, "DashSpeed must remain strictly faster than MoveSpeed.");
            Assert.Greater(_runtimeInstance.DodgeSpeed, _runtimeInstance.MoveSpeed, "DodgeSpeed must remain strictly faster than MoveSpeed.");
            Assert.Greater(_runtimeInstance.HorizontalSweepCooldownSec, _runtimeInstance.HorizontalSweepTurnDurationSec);
        }

        [Test]
        public void EnsureValidConfig_NullConfigReference_LogsDescriptiveErrorAndDisablesConsumerWithoutThrowing()
        {
            GameObject go = new GameObject("NullConfigConsumer_Test");
            try
            {
                PlayerMobilityController consumer = go.AddComponent<PlayerMobilityController>();
                Assert.IsTrue(consumer.enabled);

                LogAssert.Expect(
                    LogType.Error,
                    "[PlayerMobilityController] Missing CharacterCombatConfigSO reference on 'NullConfigConsumer_Test'; disabling component.");

                bool valid = consumer.ValidateConfigOrDisable();
                Assert.IsFalse(valid);
                Assert.IsFalse(consumer.enabled, "Consumer component must disable cleanly when config asset is null.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RecalculateDerivedValues_WithCanonical60HzBaseline_MatchesExactTicksAndCosines()
        {
            _runtimeInstance.RecalculateDerivedValues(60);

            Assert.AreEqual(12, _runtimeInstance.DashTicks);
            Assert.AreEqual(180, _runtimeInstance.DashCooldownTicks);
            Assert.AreEqual(15, _runtimeInstance.DodgeTicks);
            Assert.AreEqual(90, _runtimeInstance.DodgeCooldownTicks);
            Assert.AreEqual(11, _runtimeInstance.HorizontalTurnTicks);
            Assert.AreEqual(2.0f, _runtimeInstance.ProjectileLifetimeSec, 0.0001f);
            Assert.AreEqual(120, _runtimeInstance.ProjectileLifetimeTicks);

            Assert.AreEqual(0.9063078f, _runtimeInstance.VerticalSlashCosHalfAngle, 0.0001f);
            Assert.AreEqual(0.7071068f, _runtimeInstance.HorizontalSweepCosHalfAngle, 0.0001f);
            Assert.AreEqual(6.25f, _runtimeInstance.VerticalSlashRangeSqr, 0.0001f);
            Assert.AreEqual(9.0f, _runtimeInstance.HorizontalSweepRangeSqr, 0.0001f);
            Assert.AreEqual(7.672027f, _runtimeInstance.JumpInitialVelocity, 0.001f);
        }

        [Test]
        public void RecalculateDerivedValues_ZeroTickRateSubTickDurationsAndNearZeroSpeed_NeverProducesZeroOrInfiniteTicks()
        {
            SetPrivateField(_runtimeInstance, "_dashDurationSec", 0.001f);
            SetPrivateField(_runtimeInstance, "_rangedProjectileSpeed", 0.0f);
            _runtimeInstance.RecalculateDerivedValues(0);

            Assert.GreaterOrEqual(_runtimeInstance.DashTicks, 1);
            Assert.IsFalse(float.IsInfinity(_runtimeInstance.ProjectileLifetimeSec));
            Assert.IsFalse(float.IsNaN(_runtimeInstance.ProjectileLifetimeSec));
            Assert.GreaterOrEqual(_runtimeInstance.ProjectileLifetimeTicks, 1);

            SetPrivateField(_runtimeInstance, "_dashDurationSec", 0.20f);
            SetPrivateField(_runtimeInstance, "_rangedProjectileSpeed", 15.0f);
            _runtimeInstance.ValidateAndClamp();
            _runtimeInstance.RecalculateDerivedValues(30);
            Assert.AreEqual(6, _runtimeInstance.DashTicks);
            Assert.AreEqual(60, _runtimeInstance.ProjectileLifetimeTicks);
        }

        [Test]
        public void GetCooldown_ForEveryCombatSkillId_ReturnsCanonicalSeconds()
        {
            Assert.AreEqual(3.0f, _runtimeInstance.GetCooldown(CombatSkillId.ForwardDash), 0.0001f);
            Assert.AreEqual(1.5f, _runtimeInstance.GetCooldown(CombatSkillId.DodgeRoll), 0.0001f);
            Assert.AreEqual(0.6f, _runtimeInstance.GetCooldown(CombatSkillId.VerticalSlash), 0.0001f);
            Assert.AreEqual(1.2f, _runtimeInstance.GetCooldown(CombatSkillId.HorizontalSweep), 0.0001f);
            Assert.AreEqual(0.4f, _runtimeInstance.GetCooldown(CombatSkillId.RangedFire), 0.0001f);
            Assert.AreEqual(0f, _runtimeInstance.GetCooldown((CombatSkillId)255), 0.0001f);
        }

        [Test]
        public void HotPathPropertyReads_TenThousandIterations_AllocatesZeroManagedBytes()
        {
            float sink = ReadAllHotPathProperties(_runtimeInstance);
            GC.Collect();
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 10000; i++)
            {
                sink += ReadAllHotPathProperties(_runtimeInstance);
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            Assert.AreEqual(0L, allocatedBytes, $"Expected 0B GC allocation across 10k reads, got {allocatedBytes}B (sink={sink}).");
        }

        [Test]
        public void DefaultCapsuleCombatConfigAsset_LoadedFromAssetDatabase_MatchesCanonicalBaselineAndSingleSource()
        {
            CharacterCombatConfigSO asset = AssetDatabase.LoadAssetAtPath<CharacterCombatConfigSO>(CanonicalAssetPath);
            Assert.IsNotNull(asset, $"Expected canonical combat config asset at '{CanonicalAssetPath}'.");
            Assert.IsNull(
                AssetDatabase.LoadAssetAtPath<CharacterCombatConfigSO>("Assets/Resources/Config/DefaultCapsuleCombatConfig.asset"),
                "Duplicate Resources copy must not exist so Assets/Data/Config/DefaultCapsuleCombatConfig.asset stays single source of truth.");

            Assert.AreEqual(100, asset.MaxHealth);
            Assert.AreEqual(100, asset.MaxHP);
            Assert.AreEqual(6.0f, asset.MoveSpeed, 0.0001f);
            Assert.AreEqual(1.35f, asset.SprintMultiplier, 0.0001f);
            Assert.AreEqual(1.5f, asset.JumpHeight, 0.0001f);
            Assert.AreEqual(-19.62f, asset.Gravity, 0.0001f);
            Assert.AreEqual(1.0f, asset.RotationSensitivity, 0.0001f);
            Assert.AreEqual(18.0f, asset.DashSpeed, 0.0001f);
            Assert.AreEqual(0.20f, asset.DashDurationSec, 0.0001f);
            Assert.AreEqual(3.0f, asset.DashCooldownSec, 0.0001f);
            Assert.AreEqual(12.0f, asset.DodgeSpeed, 0.0001f);
            Assert.AreEqual(0.25f, asset.DodgeDurationSec, 0.0001f);
            Assert.AreEqual(1.5f, asset.DodgeCooldownSec, 0.0001f);

            Assert.AreEqual(25, asset.VerticalSlashDamage);
            Assert.AreEqual(2.5f, asset.VerticalSlashRange, 0.0001f);
            Assert.AreEqual(50.0f, asset.VerticalSlashAngleDeg, 0.0001f);
            Assert.AreEqual(0.6f, asset.VerticalSlashCooldownSec, 0.0001f);

            Assert.AreEqual(30, asset.HorizontalSweepDamage);
            Assert.AreEqual(3.0f, asset.HorizontalSweepRange, 0.0001f);
            Assert.AreEqual(90.0f, asset.HorizontalSweepAngleDeg, 0.0001f);
            Assert.AreEqual(90.0f, asset.HorizontalSweepTurnDeg, 0.0001f);
            Assert.AreEqual(0.18f, asset.HorizontalSweepTurnDurationSec, 0.0001f);
            Assert.AreEqual(1.2f, asset.HorizontalSweepCooldownSec, 0.0001f);

            Assert.AreEqual(15, asset.RangedDamage);
            Assert.AreEqual(15.0f, asset.RangedProjectileSpeed, 0.0001f);
            Assert.AreEqual(30.0f, asset.RangedMaxDistance, 0.0001f);
            Assert.AreEqual(0.20f, asset.RangedProjectileRadius, 0.0001f);
            Assert.AreEqual(0.4f, asset.RangedFireCooldownSec, 0.0001f);

            // Cached 60Hz ticks & TTK invariants:
            Assert.AreEqual(12, asset.DashTicks);
            Assert.AreEqual(15, asset.DodgeTicks);
            Assert.AreEqual(11, asset.HorizontalTurnTicks);
            Assert.AreEqual(120, asset.ProjectileLifetimeTicks);
            Assert.AreEqual(100, asset.VerticalSlashDamage * 4);
            Assert.GreaterOrEqual(asset.HorizontalSweepDamage * 4, asset.MaxHealth);
            Assert.Less(asset.RangedDamage * 6, asset.MaxHealth);
            Assert.GreaterOrEqual(asset.RangedDamage * 7, asset.MaxHealth);
        }

        private static float ReadAllHotPathProperties(ICharacterCombatConfig cfg)
        {
            return cfg.MoveSpeed
                   + cfg.DashTicks
                   + cfg.DodgeTicks
                   + cfg.ProjectileLifetimeTicks
                   + cfg.VerticalSlashCosHalfAngle
                   + cfg.HorizontalSweepCosHalfAngle
                   + cfg.VerticalSlashRangeSqr
                   + cfg.HorizontalSweepRangeSqr;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing expected private field '{fieldName}'.");
            field.SetValue(target, value);
        }
    }
}
