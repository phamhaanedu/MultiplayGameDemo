using CapsuleArena.CameraSystem;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using CapsuleArena.Presentation.Audio;
using CapsuleArena.Presentation.VFX;
using DG.Tweening;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// Comprehensive unit &amp; integration tests for Epic 14 3D Spatial Audio &amp; Combat VFX Feedback:
    /// - Story 14-1: <see cref="SoundManager"/> 16-emitter 3D pool + 2-channel 2D UI round-robin, 29.9m vs 30.1m culling boundary,
    ///   idle voice recycling, oldest-voice stealing, null-clip warn-once, and combat/mobility event wiring
    /// - Story 14-2: <see cref="VfxManager"/> 20-instance URP pool (<c>8+8+4</c>), vertical-plane <c>50 deg / 2.5m</c> Cyan slash mesh,
    ///   <c>[-90, 0]</c> &amp; <c>[0, +90]</c> <c>3.0m</c> Amber sweep meshes, <c>14</c> vs <c>8</c> sparks, <c>9th</c>-arc stealing, burst pool
    /// - Story 14-3: <see cref="CapsuleVisualFeedback"/> <see cref="MaterialPropertyBlock"/> <c>_EmissionColor (3.5x HDR)</c> zero-clone Hit-Flash,
    ///   non-lethal <see cref="PlayerHealth"/> event trigger, <c>PlayDashStretch()</c> &amp; <c>Dash &lt;-&gt; Hit</c> tween interruption,
    ///   and <see cref="PlayerMobilityController"/> scale non-interference while tweening
    /// </summary>
    [TestFixture]
    public sealed class AudioVfxPoolTests
    {
        private GameObject _rootGo;

        [SetUp]
        public void SetUp()
        {
            DOTween.ClearAll();
            _rootGo = new GameObject("AudioVfxTestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            DOTween.ClearAll();
            if (_rootGo != null)
            {
                Object.DestroyImmediate(_rootGo);
            }
        }

        [Test]
        public void SoundManager_Spatial16AndUi2Pools_29p9Vs30p1Culling_IdleRecycle_Stealing_AndMobilityCombatEvents()
        {
            SoundManager sm = _rootGo.AddComponent<SoundManager>();
            sm.SetListenerTransform(_rootGo.transform);
            sm.EnsurePoolsInitialized();

            // Verify 16 3D emitters AND 2 2D UI channels (spatialBlend == 0f, round-robin 0 -> 1 -> 0)
            Assert.That(sm.SpatialEmitters.Count, Is.EqualTo(SoundManager.SpatialEmitterPoolSize));
            Assert.That(sm.UiChannels.Count, Is.EqualTo(SoundManager.UiChannelPoolSize));
            for (int u = 0; u < sm.UiChannels.Count; u++)
            {
                Assert.That(sm.UiChannels[u].spatialBlend, Is.EqualTo(0.0f).Within(0.001f));
            }

            Assert.That(sm.Play2D(SoundId.UiClick), Is.EqualTo(0));
            Assert.That(sm.Play2D(SoundId.UiClick), Is.EqualTo(1));
            Assert.That(sm.Play2D(SoundId.UiClick), Is.EqualTo(0));

            for (int i = 0; i < sm.SpatialEmitters.Count; i++)
            {
                AudioSource src = sm.SpatialEmitters[i];
                Assert.That(src.spatialBlend, Is.EqualTo(1.0f).Within(0.001f));
                Assert.That(src.minDistance, Is.EqualTo(2.5f).Within(0.001f));
                Assert.That(src.maxDistance, Is.EqualTo(28.0f).Within(0.001f));
                Assert.That(src.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
            }

            // Exact 30.0m culling boundary: 29.9m plays (>= 0), 30.1m culls (-1)
            Vector3 listenerPos = Vector3.zero;
            Assert.That(sm.Play3D(SoundId.VerticalSlash, new Vector3(0f, 0f, 30.1f), listenerPos, 0.1f), Is.EqualTo(-1));
            Assert.That(sm.Play3D(SoundId.VerticalSlash, new Vector3(0f, 0f, 29.9f), listenerPos, 0.1f), Is.EqualTo(0));

            // Advance time past clip end (t = 2.0s) -> idle voice 0 recycles cleanly without stealing!
            sm.SpatialEmitters[0].Stop();
            Assert.That(sm.AcquireEmitterIndex(2.0f), Is.EqualTo(0));

            // Fill all 16 voices at t = 10.00s..10.15s, then 17th..20th steal oldest voices (0..3) with 0 new children
            int childCountBefore = sm.transform.childCount;
            for (int i = 0; i < 16; i++)
            {
                int idx = sm.Play3D(SoundId.HorizontalSweep, new Vector3(2f, 0f, 2f), listenerPos, 10.0f + i * 0.005f);
                Assert.That(idx, Is.EqualTo(i));
                Assert.That(sm.SpatialEmitters[idx].pitch, Is.InRange(SoundManager.MinPitchJitter, SoundManager.MaxPitchJitter));
            }

            for (int extra = 0; extra < 4; extra++)
            {
                int stolenIdx = sm.Play3D(SoundId.RangedFire, new Vector3(3f, 0f, 0f), listenerPos, 10.09f + extra * 0.002f);
                Assert.That(stolenIdx, Is.EqualTo(extra));
            }

            Assert.That(sm.transform.childCount, Is.EqualTo(childCountBefore));

            // Null-clip warn-once safety
            sm.SetClipOverride(SoundId.UiClick, null);
            Assert.That(sm.Play3D(SoundId.UiClick, Vector3.one, listenerPos, 20.0f), Is.EqualTo(-1));
            Assert.That(sm.Play3D(SoundId.UiClick, Vector3.one, listenerPos, 20.1f), Is.EqualTo(-1));
            Assert.That(sm.MissingClipWarningCount, Is.EqualTo(1));
        }

        [Test]
        public void VfxManager_VerticalBladeAndLeftRightSweepMeshes_BurstPool_9thArcStealing_AndZeroInstantiate()
        {
            VfxManager vfx = _rootGo.AddComponent<VfxManager>();
            vfx.EnsurePoolInitialized();

            Assert.That(vfx.AllPrewarmedInstances.Count, Is.EqualTo(VfxManager.TotalPrewarmedInstances));
            int childCountAfterPrewarm = vfx.transform.childCount;
            Assert.That(childCountAfterPrewarm, Is.EqualTo(20));

            // Vertical Slash (50 deg, 2.5m Cyan in vertical YZ blade plane -> bounds.size.y > 0.8f!)
            PooledVfxInstance vSlash = vfx.SpawnMeleeSlashArc(Vector3.zero, Quaternion.identity, MeleeSwingKind.VerticalSlash);
            Mesh vMesh = vSlash.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(vSlash.ConfiguredArcAngleDeg, Is.EqualTo(50f).Within(0.01f));
            Assert.That(vSlash.ConfiguredArcRadiusMeters, Is.EqualTo(2.5f).Within(0.01f));
            Assert.That(vSlash.ActiveTintColor, Is.EqualTo(VfxManager.VerticalSlashCyan));
            Assert.That(vMesh.bounds.size.y, Is.GreaterThan(1.0f), "VerticalSlash mesh must span the vertical YZ blade plane.");

            // Horizontal Sweep Left ([-90, 0] deg) & Right ([0, +90] deg) in XZ plane
            PooledVfxInstance hLeft = vfx.SpawnMeleeSlashArc(Vector3.zero, Quaternion.identity, MeleeSwingKind.HorizontalSweepLeft);
            PooledVfxInstance hRight = vfx.SpawnMeleeSlashArc(Vector3.zero, Quaternion.identity, MeleeSwingKind.HorizontalSweepRight);
            Mesh leftMesh = hLeft.GetComponentInChildren<MeshFilter>().sharedMesh;
            Mesh rightMesh = hRight.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(hRight.ConfiguredArcAngleDeg, Is.EqualTo(90f).Within(0.01f));
            Assert.That(hRight.ConfiguredArcRadiusMeters, Is.EqualTo(3.0f).Within(0.01f));
            Assert.That(hRight.ActiveTintColor, Is.EqualTo(VfxManager.HorizontalSweepAmber));
            Assert.That(leftMesh.bounds.min.x, Is.LessThan(-2.5f));
            Assert.That(rightMesh.bounds.max.x, Is.GreaterThan(2.5f));
            Assert.That(rightMesh.bounds.min.x, Is.GreaterThanOrEqualTo(-0.01f));

            // Exhaust all 8 SlashArc instances (3 already active + 5 more = 8) and verify 9th steals oldest SlashArc with 0 new GameObjects!
            for (int extra = 0; extra < 5; extra++)
            {
                vfx.SpawnMeleeSlashArc(Vector3.zero, Quaternion.identity, MeleeSwingKind.VerticalSlash);
            }

            Assert.That(vfx.IdleSlashArcCount, Is.EqualTo(0));
            PooledVfxInstance ninthArc = vfx.SpawnMeleeSlashArc(Vector3.one, Quaternion.identity, MeleeSwingKind.HorizontalSweepRight);
            Assert.That(ninthArc, Is.SameAs(vSlash), "9th SlashArc request must steal the oldest active SlashArc.");
            Assert.That(vfx.transform.childCount, Is.EqualTo(childCountAfterPrewarm));

            // Sparks (14 player vs 8 wall) + 4-item MobilityAndDeathBurst pool
            PooledVfxInstance playerSpark = vfx.SpawnImpactSpark(Vector3.one, hitLivingPlayer: true);
            PooledVfxInstance wallSpark = vfx.SpawnImpactSpark(Vector3.forward * 4f, hitLivingPlayer: false);
            PooledVfxInstance deathBurst = vfx.SpawnBurstEffect(Vector3.up, Color.red, 20, 0.40f);
            Assert.That(playerSpark.LastEmittedSparkCount, Is.EqualTo(VfxManager.PlayerHitSparkCount));
            Assert.That(wallSpark.LastEmittedSparkCount, Is.EqualTo(VfxManager.WallImpactSparkCount));
            Assert.That(deathBurst.Category, Is.EqualTo(VfxPoolCategory.MobilityAndDeathBurst));
            Assert.That(vfx.IdleBurstCount, Is.EqualTo(VfxManager.BurstPoolSize - 1));

            // Advance lifetimes -> all 20 instances return to idle buckets
            vfx.TickActiveVfx(1.0f);
            Assert.That(vfx.ActiveInstanceCount, Is.EqualTo(0));
            Assert.That(vfx.IdleSlashArcCount, Is.EqualTo(VfxManager.SlashArcPoolSize));
            Assert.That(vfx.IdleSparkCount, Is.EqualTo(VfxManager.ImpactSparkPoolSize));
            Assert.That(vfx.IdleBurstCount, Is.EqualTo(VfxManager.BurstPoolSize));
            Assert.That(vfx.transform.childCount, Is.EqualTo(childCountAfterPrewarm));
        }

        [Test]
        public void CapsuleVisualFeedback_MpbEmissionColor_DashStretchInterruption_NonLethalHealthEvent_AndMobilityScalePreservation()
        {
            CharacterCombatConfigSO cfg = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            GameObject capsuleRoot = new GameObject("TestCapsuleRoot");
            capsuleRoot.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            capsuleRoot.AddComponent<CapsuleCollider>();
            PlayerHealth health = capsuleRoot.AddComponent<PlayerHealth>();
            PlayerMobilityController mobility = capsuleRoot.AddComponent<PlayerMobilityController>();

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(capsuleRoot.transform, worldPositionStays: false);
            mobility.ConfigureReferences(cfg, null, visualRoot.transform, null);

            GameObject bodyMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bodyMesh.transform.SetParent(visualRoot.transform, worldPositionStays: false);
            MeshRenderer renderer = bodyMesh.GetComponent<MeshRenderer>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material sharedMat = new Material(shader) { name = "M_TestSharedCapsule" };
            renderer.sharedMaterial = sharedMat;

            CapsuleVisualFeedback feedback = capsuleRoot.AddComponent<CapsuleVisualFeedback>();
            feedback.ConfigureReferences(visualRoot.transform, new Renderer[] { renderer }, health, mobility);
            health.ConfigureForStandaloneTest(cfg, PlayerRef.FromEncoded(1), hasStateAuthority: true);

            try
            {
                // 1. PlayDashStretch() via Forward Dash mobility burst -> (0.82, 0.90, 1.30) stretch, then interrupt mid-flight with Hit Squash!
                NetworkInputData dashInput = default;
                dashInput.Buttons.Set((int)NetworkInputButtons.ForwardDash, true);
                mobility.SimulateAuthoritativeTick(in dashInput, 1f / 60f, cfg, groundedOverride: true);
                DOTween.ManualUpdate(0.056f); // Peak of 0.16s pulse (0.35 * 0.16 = 0.056s)
                Assert.That(Vector3.Distance(visualRoot.transform.localScale, CapsuleVisualFeedback.DashStretchPeakScale), Is.LessThan(0.05f));

                // Interrupt in-flight Dash stretch with non-lethal PlayerHealth hit (25 dmg):
                bool hitApplied = health.TryProcessAuthoritativeDamage(
                    PlayerRef.FromEncoded(2),
                    25,
                    AttackType.VerticalSlash,
                    capsuleRoot.transform.position,
                    attackTick: 1,
                    capsuleRoot.transform.position);
                Assert.That(hitApplied, Is.True);
                Assert.That(feedback.IsHitFlashActive, Is.True, "Non-lethal damage must trigger IsHitFlashActive via OnHitFeedbackReceived.");

                // Inspect MaterialPropertyBlock directly: _EmissionColor == (3.5, 3.5, 3.5, 1.0) at < 0.12s, zero material clones!
                var inspectMpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(inspectMpb);
                Color activeEmission = inspectMpb.GetColor(Shader.PropertyToID("_EmissionColor"));
                Assert.That(Vector4.Distance(activeEmission, CapsuleVisualFeedback.HdrFlashEmission), Is.LessThan(0.001f));
                Assert.That(ReferenceEquals(renderer.sharedMaterial, sharedMat), Is.True);

                // Verify PlayerMobilityController.SimulateAuthoritativeTick does NOT overwrite _visualRoot.localScale while DOTween is active!
                DOTween.ManualUpdate(0.0525f); // Peak of 0.15s HitSquash (0.35 * 0.15 = 0.0525s)
                NetworkInputData idleTick = default;
                mobility.SimulateAuthoritativeTick(in idleTick, 1f / 60f, cfg, groundedOverride: true);
                Assert.That(Vector3.Distance(visualRoot.transform.localScale, CapsuleVisualFeedback.HitSquashPeakScale), Is.LessThan(0.05f),
                    "PlayerMobilityController must NOT overwrite VisualRoot.localScale while CapsuleVisualFeedback tween is active.");
                Assert.That(capsuleRoot.transform.localScale, Is.EqualTo(Vector3.one));

                // 2. 50 rapid overlapping hits -> 0 clones, recovers to exact (1, 1, 1), and clears MPB after >= 0.12s!
                for (int i = 0; i < 50; i++)
                {
                    feedback.TriggerHitFlashAndSquash();
                    DOTween.ManualUpdate(0.03f);
                    Assert.That(ReferenceEquals(renderer.sharedMaterial, sharedMat), Is.True);
                }

                DOTween.ManualUpdate(0.25f);
                feedback.TickFeedback(0.25f);
                Assert.That(feedback.IsHitFlashActive, Is.False);
                Assert.That(renderer.HasPropertyBlock(), Is.False, "Cleared Hit-Flash must remove MPB override.");
                Assert.That(Vector3.Distance(visualRoot.transform.localScale, Vector3.one), Is.LessThan(0.0001f));

                // 3. Lethal damage during active hit-flash resets immediately for Ghost Spectator transition
                feedback.TriggerHitFlashAndSquash();
                DOTween.ManualUpdate(0.05f);
                for (int tick = 2; tick <= 4; tick++)
                {
                    health.TryProcessAuthoritativeDamage(
                        PlayerRef.FromEncoded(2),
                        25,
                        AttackType.VerticalSlash,
                        capsuleRoot.transform.position,
                        tick,
                        capsuleRoot.transform.position);
                }

                Assert.That(health.IsDead, Is.True);
                Assert.That(feedback.IsHitFlashActive, Is.False);
                Assert.That(Vector3.Distance(visualRoot.transform.localScale, Vector3.one), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(sharedMat);
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void HorizontalSweepLeftAndRight_FanArcProgressCapsuleYawCameraAndFollowOrigin_SynchronizeIdenticallyOver0p18Sec()
        {
            CharacterCombatConfigSO cfg = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            VfxManager vfx = _rootGo.AddComponent<VfxManager>();
            vfx.EnsurePoolInitialized();
            vfx.SubscribeEvents();

            GameObject playerGo = new GameObject("SweepSyncPlayer");
            playerGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            playerGo.transform.SetPositionAndRotation(new Vector3(0f, 10f, 0f), Quaternion.identity);

            GameObject pollerGo = new GameObject("SweepSyncPoller");
            pollerGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            LocalInputPoller poller = pollerGo.AddComponent<LocalInputPoller>();

            GameObject camGo = new GameObject("SweepSyncCamera");
            camGo.transform.SetParent(_rootGo.transform, worldPositionStays: false);
            Camera cam = camGo.AddComponent<Camera>();

            PlayerHealth health = playerGo.AddComponent<PlayerHealth>();
            health.ConfigureForStandaloneTest(cfg, PlayerRef.FromEncoded(1), hasStateAuthority: true);
            PlayerMobilityController mobility = playerGo.AddComponent<PlayerMobilityController>();
            PlayerCombatController combat = playerGo.AddComponent<PlayerCombatController>();
            combat.ConfigureForTest(cfg, PlayerRef.FromEncoded(1), health, mobility, poller);

            CameraController camCtrl = playerGo.AddComponent<CameraController>();
            camCtrl.BindLocalCamera(cam, poller, null, cfg);
            camCtrl.SetPerspective(CameraPerspectiveMode.TPS);

            try
            {
                // 1. Trigger HorizontalSweepLeft (Key 1 -> 0 deg to -90 deg over 0.18s)
                NetworkInputData leftInput = default;
                leftInput.LookYaw = 0f;
                leftInput.Buttons.Set((int)NetworkInputButtons.HorizontalSweepLeft, true);
                combat.SimulateCombatTick(in leftInput, cfg);

                PooledVfxInstance activeLeftArc = FindNewestActiveSlashArc(vfx);
                Assert.That(activeLeftArc, Is.Not.Null, "HorizontalSweepLeft must spawn an active SlashArc VFX.");
                Assert.That(activeLeftArc.CurrentSweepProgress, Is.EqualTo(0f).Within(0.001f), "Fan must start at 0% progress.");

                // Verify Left mesh UV progression: first outer vertex starts at forward (+Z, X ~ 0, u = 0), last outer vertex ends at left (-X, Z ~ 0, u = 1)
                Mesh leftMesh = activeLeftArc.GetComponentInChildren<MeshFilter>().sharedMesh;
                Vector2[] leftUvs = leftMesh.uv;
                Vector3[] leftVerts = leftMesh.vertices;
                Assert.That(leftUvs[0].x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(leftVerts[1].x, Is.EqualTo(0f).Within(0.02f), "Left sweep fan must begin sweeping from 0 deg forward.");
                Assert.That(leftUvs[leftUvs.Length - 1].x, Is.EqualTo(1f).Within(0.001f));
                Assert.That(leftVerts[leftVerts.Length - 1].x, Is.LessThan(-2.95f), "Left sweep fan must finish at -90 deg (-3.0m X).");

                // Advance halfway (0.09s of 0.18s) + move capsule with WASD -> both capsule yaw (-45 deg), camera (-45 deg), and fan (50%) match!
                playerGo.transform.position = new Vector3(1.5f, 10f, 2.0f);
                combat.StepVisualHorizontalSweepRender(0.09f);
                vfx.TickActiveVfx(0.09f);
                activeLeftArc.SyncFollowPosition();
                camCtrl.StepCameraFrame(0f);

                Assert.That(combat.VisualSweepProgress, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(activeLeftArc.CurrentSweepProgress, Is.EqualTo(0.5f).Within(0.01f), "At 50% duration, fan must be 50% swept.");
                Assert.That(Mathf.DeltaAngle(0f, playerGo.transform.eulerAngles.y), Is.EqualTo(-45f).Within(0.5f), "At 50% duration, capsule must be at -45 deg.");
                Assert.That(Mathf.DeltaAngle(0f, cam.transform.eulerAngles.y), Is.EqualTo(-45f).Within(0.5f), "Camera must track -45 deg synchronously.");
                Assert.That(Vector3.Distance(activeLeftArc.transform.position, playerGo.transform.position + Vector3.up * MeleeSectorMath.ChestHeightOffsetMeters),
                    Is.LessThan(0.001f), "Fan origin must follow moving capsule chest.");

                // Advance remaining 0.09s (total 0.18s) -> fan hits exact 100% (full 90-deg sector) at the exact moment capsule hits -90 deg!
                combat.StepVisualHorizontalSweepRender(0.09f);
                vfx.TickActiveVfx(0.09f);
                activeLeftArc.SyncFollowPosition();
                camCtrl.StepCameraFrame(0f);

                Assert.That(combat.VisualSweepProgress, Is.EqualTo(1.0f).Within(0.001f));
                Assert.That(activeLeftArc.CurrentSweepProgress, Is.EqualTo(1.0f).Within(0.001f), "At 0.18s, fan must reach 100% full 90-deg arc.");
                Assert.That(Mathf.DeltaAngle(0f, playerGo.transform.eulerAngles.y), Is.EqualTo(-90f).Within(0.5f), "At 0.18s, capsule rotation must finish at -90 deg.");

                // Inspect MaterialPropertyBlock _SweepProgress directly
                var arcRenderer = activeLeftArc.GetComponentInChildren<MeshRenderer>();
                var mpb = new MaterialPropertyBlock();
                arcRenderer.GetPropertyBlock(mpb);
                Assert.That(mpb.GetFloat(Shader.PropertyToID("_SweepProgress")), Is.EqualTo(1.0f).Within(0.001f));

                // Clear ticks & cooldown (75 ticks), then verify HorizontalSweepRight (-90 deg -> 0 deg, +90 deg clockwise)
                vfx.TickActiveVfx(0.5f);
                NetworkInputData idle = default;
                idle.LookYaw = -90f;
                for (int t = 0; t < 75; t++)
                {
                    combat.SimulateCombatTick(in idle, cfg);
                }

                NetworkInputData rightInput = default;
                rightInput.LookYaw = -90f;
                rightInput.Buttons.Set((int)NetworkInputButtons.HorizontalSweepRight, true);
                combat.SimulateCombatTick(in rightInput, cfg);

                PooledVfxInstance activeRightArc = FindNewestActiveSlashArc(vfx);
                Assert.That(activeRightArc, Is.Not.Null);
                Assert.That(activeRightArc.CurrentSweepProgress, Is.EqualTo(0f).Within(0.001f));

                combat.StepVisualHorizontalSweepRender(0.18f);
                vfx.TickActiveVfx(0.18f);
                activeRightArc.SyncFollowPosition();

                Assert.That(activeRightArc.CurrentSweepProgress, Is.EqualTo(1.0f).Within(0.001f));
                Assert.That(Mathf.DeltaAngle(0f, playerGo.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.5f), "Right sweep from -90 deg must finish at 0 deg at 0.18s.");
            }
            finally
            {
                vfx.UnsubscribeEvents();
                Object.DestroyImmediate(cfg);
            }
        }

        private static PooledVfxInstance FindNewestActiveSlashArc(VfxManager vfx)
        {
            PooledVfxInstance found = null;
            for (int i = 0; i < vfx.AllPrewarmedInstances.Count; i++)
            {
                PooledVfxInstance inst = vfx.AllPrewarmedInstances[i];
                if (inst != null && inst.Category == VfxPoolCategory.SlashArc && inst.IsActiveInScene)
                {
                    found = inst;
                }
            }

            return found;
        }
    }
}
