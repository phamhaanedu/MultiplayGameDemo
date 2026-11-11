using System.Reflection;
using CapsuleArena.CameraSystem;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using CapsuleArena.UI.HUD;
using DG.Tweening;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// Unit and integration tests for <see cref="FloatingDamageTextConfigSO"/>,
    /// <see cref="PooledFloatingDamageText"/>, <see cref="FloatingDamageTextManager"/>,
    /// and <see cref="PlayerHealth"/> damage/heal floating number feedback:
    /// - Encapsulated ScriptableObject colors (Red damage, Green heal, custom overrides)
    /// - Unsigned text formatting ("30", "25", "15" with no minus sign)
    /// - Exact 2/3 enlarge + 1/3 shrink-and-fade upward trajectory and zero-allocation pool return
    /// - Shown to everyone including local self in TPS, strictly hidden for local self in FPS.
    /// </summary>
    [TestFixture]
    public sealed class FloatingDamageTextTests
    {
        private GameObject _rootGo;
        private CharacterCombatConfigSO _combatConfig;
        private FloatingDamageTextConfigSO _floatingConfig;

        [SetUp]
        public void SetUp()
        {
            DOTween.ClearAll();
            DamageReceiverRegistry.ClearAll();
            _rootGo = new GameObject("FloatingDamageTestRoot");
            _combatConfig = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            _floatingConfig = ScriptableObject.CreateInstance<FloatingDamageTextConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            DOTween.ClearAll();
            DamageReceiverRegistry.ClearAll();
            if (_floatingConfig != null)
            {
                Object.DestroyImmediate(_floatingConfig);
            }

            if (_combatConfig != null)
            {
                Object.DestroyImmediate(_combatConfig);
            }

            if (_rootGo != null)
            {
                Object.DestroyImmediate(_rootGo);
            }
        }

        [Test]
        public void FloatingDamageTextConfigSO_EncapsulationAndCanonicalAsset_ExposesRedDamageGreenHealAndZeroPublicFields()
        {
            FieldInfo[] publicFields = typeof(FloatingDamageTextConfigSO).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Assert.That(publicFields.Length, Is.EqualTo(0), "FloatingDamageTextConfigSO must not expose public fields.");

            PropertyInfo[] props = typeof(FloatingDamageTextConfigSO).GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (PropertyInfo prop in props)
            {
                Assert.That(prop.GetSetMethod(nonPublic: false), Is.Null, $"Property {prop.Name} must not have a public setter.");
            }

            var savedAsset = AssetDatabase.LoadAssetAtPath<FloatingDamageTextConfigSO>(
                "Assets/Data/Config/DefaultFloatingDamageConfig.asset");
            Assert.That(savedAsset, Is.Not.Null, "DefaultFloatingDamageConfig.asset must exist under Assets/Data/Config/.");
            Assert.That(savedAsset.DamageTextColor.r, Is.GreaterThan(0.85f), "Default damage color must be Red.");
            Assert.That(savedAsset.DamageTextColor.g, Is.LessThan(0.40f));
            Assert.That(savedAsset.HealTextColor.g, Is.GreaterThan(0.65f), "Default heal color must be Green.");
            Assert.That(savedAsset.GrowPhaseNormalizedRatio, Is.EqualTo(2f / 3f).Within(0.001f));
        }

        [Test]
        public void PooledFloatingDamageText_TwoThirdsGrowOneThirdShrinkAndFade_NoMinusSign_AndReturnsToPool()
        {
            GameObject camGo = new GameObject("TestCam");
            camGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            camGo.transform.position = new Vector3(0f, 3f, -8f);
            Camera cam = camGo.AddComponent<Camera>();

            GameObject enemyGo = new GameObject("DummyEnemy");
            enemyGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            enemyGo.transform.position = Vector3.zero;
            PlayerHealth enemyHealth = enemyGo.AddComponent<PlayerHealth>();
            enemyHealth.ConfigureForStandaloneTest(_combatConfig, PlayerRef.FromEncoded(2), hasStateAuthority: true);

            GameObject mgrGo = new GameObject("FloatingMgr");
            mgrGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            FloatingDamageTextManager mgr = mgrGo.AddComponent<FloatingDamageTextManager>();
            mgr.Initialize(_floatingConfig, cam);

            int childCountAfterPrewarm = mgr.transform.childCount;
            Assert.That(childCountAfterPrewarm, Is.EqualTo(_floatingConfig.PoolSize));
            Assert.That(mgr.IdleCount, Is.EqualTo(_floatingConfig.PoolSize));

            // Spawn damage 30 above an enemy at (0, 0, 0)
            PooledFloatingDamageText item = mgr.SpawnForTarget(enemyHealth, 30, isHeal: false);
            Assert.That(item, Is.Not.Null);
            Assert.That(mgr.ActiveCount, Is.EqualTo(1));
            Assert.That(item.RenderedText, Is.EqualTo("30"), "Floating damage text must be unsigned without a minus sign.");
            Assert.That(item.RenderedText.Contains("-"), Is.False);
            Assert.That(item.IsHealNumber, Is.False);
            Assert.That(item.CurrentTextColor.r, Is.EqualTo(_floatingConfig.DamageTextColor.r).Within(0.01f));

            float yStart = item.transform.position.y;
            float scaleStart = item.CurrentScaleMultiplier;
            Assert.That(yStart, Is.EqualTo(_floatingConfig.SpawnHeightOffsetMeters).Within(0.01f));
            Assert.That(scaleStart, Is.EqualTo(_floatingConfig.StartScale).Within(0.01f));

            // Advance 1/3 lifetime -> enlarging phase (StartScale < current < PeakScale), rising upward
            float lifetime = _floatingConfig.LifetimeSeconds;
            DOTween.ManualUpdate(lifetime * (1f / 3f));
            float yOneThird = item.transform.position.y;
            float scaleOneThird = item.CurrentScaleMultiplier;
            Assert.That(yOneThird, Is.GreaterThan(yStart));
            Assert.That(scaleOneThird, Is.GreaterThan(scaleStart));
            Assert.That(scaleOneThird, Is.LessThan(_floatingConfig.PeakScale));

            // Advance to exact 2/3 lifetime -> peak scale reached!
            DOTween.ManualUpdate(lifetime * (1f / 3f));
            float yTwoThirds = item.transform.position.y;
            float scalePeak = item.CurrentScaleMultiplier;
            float alphaAtTwoThirds = item.CurrentAlpha;
            Assert.That(yTwoThirds, Is.GreaterThan(yOneThird));
            Assert.That(scalePeak, Is.EqualTo(_floatingConfig.PeakScale).Within(0.02f), "Scale must peak at 2/3 of lifetime.");
            Assert.That(alphaAtTwoThirds, Is.EqualTo(_floatingConfig.AlphaAtPeak).Within(0.03f));

            // Advance halfway into final 1/3 (u = 5/6) -> shrinking AND fading simultaneously while still rising!
            DOTween.ManualUpdate(lifetime * (1f / 6f));
            float yFiveSixths = item.transform.position.y;
            float scaleShrinking = item.CurrentScaleMultiplier;
            float alphaShrinking = item.CurrentAlpha;
            Assert.That(yFiveSixths, Is.GreaterThan(yTwoThirds), "Text must continue rising during final 1/3.");
            Assert.That(scaleShrinking, Is.LessThan(scalePeak), "Final 1/3 must shrink text scale.");
            Assert.That(scaleShrinking, Is.GreaterThan(_floatingConfig.EndScale));
            Assert.That(alphaShrinking, Is.LessThan(alphaAtTwoThirds), "Final 1/3 must fade alpha toward zero.");

            // Complete remaining 1/6 (+ tiny epsilon) -> disappears and returns cleanly to pool with 0 new GameObjects!
            DOTween.ManualUpdate(lifetime * (1f / 6f) + 0.02f);
            Assert.That(item.IsActiveInScene, Is.False);
            Assert.That(mgr.ActiveCount, Is.EqualTo(0));
            Assert.That(mgr.IdleCount, Is.EqualTo(_floatingConfig.PoolSize));
            Assert.That(mgr.transform.childCount, Is.EqualTo(childCountAfterPrewarm));
        }

        [Test]
        public void FloatingDamageTextManager_AllDamageTypesHealGreenCustomColors_AndLocalFpsHideRule()
        {
            // Customize ScriptableObject colors to verify designer SO configurability end-to-end
            Color customDamageRed = new Color(0.95f, 0.12f, 0.18f, 1f);
            Color customHealGreen = new Color(0.10f, 0.92f, 0.42f, 1f);
            SerializedObject so = new SerializedObject(_floatingConfig);
            so.FindProperty("_damageTextColor").colorValue = customDamageRed;
            so.FindProperty("_healTextColor").colorValue = customHealGreen;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject camGo = new GameObject("ArenaCam");
            camGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            Camera cam = camGo.AddComponent<Camera>();

            GameObject pollerGo = new GameObject("Poller");
            pollerGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            LocalInputPoller poller = pollerGo.AddComponent<LocalInputPoller>();

            GameObject localGo = new GameObject("LocalCapsule");
            localGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            localGo.transform.position = new Vector3(0f, 0f, 0f);
            PlayerHealth localHealth = localGo.AddComponent<PlayerHealth>();
            localHealth.ConfigureForStandaloneTest(_combatConfig, PlayerRef.FromEncoded(1), hasStateAuthority: true);
            CameraController localCamCtrl = localGo.AddComponent<CameraController>();
            localCamCtrl.BindLocalCamera(cam, poller, null, _combatConfig);
            localCamCtrl.SetPerspective(CameraPerspectiveMode.TPS);

            GameObject enemyGo = new GameObject("EnemyCapsule");
            enemyGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            enemyGo.transform.position = new Vector3(0f, 0f, 1.8f);
            PlayerHealth enemyHealth = enemyGo.AddComponent<PlayerHealth>();
            enemyHealth.ConfigureForStandaloneTest(_combatConfig, PlayerRef.FromEncoded(2), hasStateAuthority: true);

            GameObject mgrGo = new GameObject("FloatingMgr_Visibility");
            mgrGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            FloatingDamageTextManager mgr = mgrGo.AddComponent<FloatingDamageTextManager>();
            mgr.Initialize(_floatingConfig, cam);
            mgr.BindLocalPlayer(localHealth, PlayerRef.FromEncoded(1));
            mgr.SubscribeEvents();

            try
            {
                // 1. Enemy hit by VerticalSlash (25), HorizontalSweep (30), RangedProjectile (15) -> all spawn unsigned custom Red text!
                enemyHealth.TryProcessAuthoritativeDamage(PlayerRef.FromEncoded(1), 25, AttackType.VerticalSlash, Vector3.zero, 1, enemyGo.transform.position);
                enemyHealth.TryProcessAuthoritativeDamage(PlayerRef.FromEncoded(1), 30, AttackType.HorizontalSweep, Vector3.zero, 2, enemyGo.transform.position);
                enemyHealth.TryProcessAuthoritativeDamage(PlayerRef.FromEncoded(1), 15, AttackType.RangedProjectile, Vector3.zero, 3, enemyGo.transform.position);
                Assert.That(mgr.ActiveCount, Is.EqualTo(3));
                Assert.That(mgr.AllPrewarmedInstances[0].RenderedText, Is.EqualTo("25"));
                Assert.That(mgr.AllPrewarmedInstances[1].RenderedText, Is.EqualTo("30"));
                Assert.That(mgr.AllPrewarmedInstances[2].RenderedText, Is.EqualTo("15"));
                Assert.That(mgr.AllPrewarmedInstances[0].CurrentTextColor.r, Is.EqualTo(customDamageRed.r).Within(0.01f));
                Assert.That(mgr.AllPrewarmedInstances[0].IsVisibleToLocalCamera, Is.True);

                // 2. Enemy heals (+20 HP potion) -> spawns unsigned custom Green text ("20")!
                bool healed = enemyHealth.TryProcessAuthoritativeHeal(20, enemyGo.transform.position);
                Assert.That(healed, Is.True);
                Assert.That(enemyHealth.CurrentHP, Is.EqualTo(50));
                PooledFloatingDamageText enemyHealItem = mgr.AllPrewarmedInstances[3];
                Assert.That(enemyHealItem.RenderedText, Is.EqualTo("20"));
                Assert.That(enemyHealItem.IsHealNumber, Is.True);
                Assert.That(enemyHealItem.CurrentTextColor.g, Is.EqualTo(customHealGreen.g).Within(0.01f));

                // 3. Local self takes damage in TPS -> VISIBLE to self!
                localHealth.TryProcessAuthoritativeDamage(PlayerRef.FromEncoded(2), 30, AttackType.HorizontalSweep, enemyGo.transform.position, 4, localGo.transform.position);
                PooledFloatingDamageText selfHitTps = mgr.AllPrewarmedInstances[4];
                Assert.That(selfHitTps.IsLocalPlayerTarget, Is.True);
                Assert.That(selfHitTps.IsVisibleToLocalCamera, Is.True, "Local self damage must be visible in TPS perspective.");

                // 4. Toggle camera TPS -> FPS mid-flight: local self number hides immediately, while enemy numbers stay visible!
                localCamCtrl.SetPerspective(CameraPerspectiveMode.FPS);
                Assert.That(selfHitTps.IsVisibleToLocalCamera, Is.False, "Switching to FPS must hide local self floating text.");
                Assert.That(selfHitTps.CurrentAlpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(mgr.AllPrewarmedInstances[0].IsVisibleToLocalCamera, Is.True, "Enemy floating text must remain visible in FPS.");

                // 5. While in FPS, local self takes Ranged hit (15) and heals (10) -> suppressed (not spawned in FPS), whereas enemy hit in FPS -> spawned visible!
                int activeBeforeFpsSelfHits = mgr.ActiveCount;
                localHealth.TryProcessAuthoritativeDamage(PlayerRef.FromEncoded(2), 15, AttackType.RangedProjectile, enemyGo.transform.position, 5, localGo.transform.position);
                localHealth.TryProcessAuthoritativeHeal(10, localGo.transform.position);
                Assert.That(mgr.ActiveCount, Is.EqualTo(activeBeforeFpsSelfHits), "Local self damage/heal in FPS must not display overhead numbers.");

                enemyHealth.TryProcessAuthoritativeDamage(PlayerRef.FromEncoded(1), 15, AttackType.RangedProjectile, localGo.transform.position, 6, enemyGo.transform.position);
                Assert.That(mgr.ActiveCount, Is.EqualTo(activeBeforeFpsSelfHits + 1), "Enemy damage in FPS must still spawn and be visible.");
                PooledFloatingDamageText enemyHitFps = mgr.AllPrewarmedInstances[5];
                Assert.That(enemyHitFps.IsVisibleToLocalCamera, Is.True);

                // 6. Toggle back FPS -> TPS mid-flight -> existing in-flight local self number becomes visible again!
                localCamCtrl.SetPerspective(CameraPerspectiveMode.TPS);
                Assert.That(selfHitTps.IsVisibleToLocalCamera, Is.True);
                Assert.That(selfHitTps.CurrentAlpha, Is.GreaterThan(0.5f));
            }
            finally
            {
                mgr.UnsubscribeEvents();
            }
        }
    }
}
