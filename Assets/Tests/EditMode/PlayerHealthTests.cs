using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit and integration tests verifying victim-authoritative PlayerHealth clamping,
    /// StateAuthority mutation guard, mixed canonical attack sequence, exact 5.0m/34.0m distance
    /// sanity boundaries, tick-idempotent RPC deduplication, OnHitFeedbackReceived &amp; OnKillConfirmed
    /// dispatch, and immediate Layer 8 -&gt; Layer 10 GhostSpectator transition (including 50% nameplate
    /// alpha, disabled hitbox root, hidden local Mobile Action Pad, and locked combat bursts)
    /// (Stories 6-1, 6-2, 6-3; ADR-0004; TR-health-001..003).
    /// </summary>
    [TestFixture]
    public class PlayerHealthTests
    {
        private CharacterCombatConfigSO _config;
        private GameObject _victimGo;
        private Transform _visualRoot;
        private MeshRenderer _meshRenderer;
        private GameObject _hpBarGo;
        private GameObject _hitboxRootGo;
        private GameObject _mobilePadGo;
        private CanvasGroup _mobilePadCanvasGroup;
        private Material _aliveMat;
        private Material _ghostMat;
        private PlayerMobilityController _mobility;
        private PlayerDeathStateController _deathController;
        private PlayerHealth _health;

        private PlayerRef _victimPlayer;
        private PlayerRef _attackerA;
        private PlayerRef _attackerB;

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            ArenaLayers.ApplyCollisionMatrix();
            _config = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();

            _victimPlayer = PlayerRef.FromEncoded(1);
            _attackerA = PlayerRef.FromEncoded(2);
            _attackerB = PlayerRef.FromEncoded(3);

            _victimGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _victimGo.name = "VictimCapsule_Test";
            _victimGo.layer = ArenaLayers.PlayerCharacter;

            _visualRoot = new GameObject("VisualRoot").transform;
            _visualRoot.SetParent(_victimGo.transform, false);
            _meshRenderer = _victimGo.GetComponent<MeshRenderer>();

            _hpBarGo = new GameObject("OverheadHPBar");
            _hpBarGo.transform.SetParent(_victimGo.transform, false);

            _hitboxRootGo = new GameObject("CombatHitboxRoot");
            _hitboxRootGo.transform.SetParent(_victimGo.transform, false);

            _mobilePadGo = new GameObject("LocalMobileActionPad");
            _mobilePadCanvasGroup = _mobilePadGo.AddComponent<CanvasGroup>();
            _mobilePadCanvasGroup.alpha = 1f;
            _mobilePadCanvasGroup.interactable = true;
            _mobilePadCanvasGroup.blocksRaycasts = true;

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _aliveMat = new Material(litShader) { color = new Color(0.90f, 0.22f, 0.27f, 1.0f) };
            _ghostMat = new Material(litShader) { color = new Color(0.659f, 0.855f, 0.863f, 0.35f) };

            _mobility = _victimGo.AddComponent<PlayerMobilityController>();
            _mobility.ConfigureReferences(_config, null, _visualRoot, null);

            _deathController = _victimGo.AddComponent<PlayerDeathStateController>();
            _deathController.ConfigureReferences(
                new Renderer[] { _meshRenderer },
                _aliveMat,
                _ghostMat,
                _hpBarGo,
                nameplateText: null,
                _mobility,
                basePlayerName: "Duelist_01",
                combatHitboxRoot: _hitboxRootGo,
                localMobileActionPad: _mobilePadCanvasGroup,
                isLocalPlayerAvatar: true);

            _health = _victimGo.AddComponent<PlayerHealth>();
            _health.ConfigureForStandaloneTest(_config, _victimPlayer, hasStateAuthority: true, isMatchPlaying: true, _deathController);
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (_mobilePadGo != null) Object.DestroyImmediate(_mobilePadGo);
            if (_victimGo != null) Object.DestroyImmediate(_victimGo);
            if (_aliveMat != null) Object.DestroyImmediate(_aliveMat);
            if (_ghostMat != null) Object.DestroyImmediate(_ghostMat);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void TryProcessAuthoritativeDamage_WithoutStateAuthority_RejectsMutationCompletely()
        {
            _health.ConfigureForStandaloneTest(_config, _victimPlayer, hasStateAuthority: false);

            bool accepted = _health.TryProcessAuthoritativeDamage(
                _attackerA,
                25,
                AttackType.VerticalSlash,
                new Vector3(0f, 0f, 1.5f));

            Assert.IsFalse(accepted, "Proxy without StateAuthority must never mutate CurrentHP.");
            Assert.AreEqual(100, _health.CurrentHP);
            Assert.IsFalse(_health.IsDead);
        }

        [Test]
        public void ValidateDamageRequest_ExactBoundariesInvalidEnumsNegativeAmountsAndForgedPayloads_EnforcesStrictSanity()
        {
            Vector3 victimPos = Vector3.zero;

            // Self-damage, zero/negative damage, invalid AttackType, forged damage rejected:
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _victimPlayer, _victimPlayer, 25, AttackType.VerticalSlash, new Vector3(0f, 0f, 1f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 0, AttackType.VerticalSlash, new Vector3(0f, 0f, 1f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, -25, AttackType.VerticalSlash, new Vector3(0f, 0f, 1f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 25, (AttackType)99, new Vector3(0f, 0f, 1f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 999, AttackType.VerticalSlash, new Vector3(0f, 0f, 1.5f), victimPos, _config));

            // Exact Melee sanity boundary: 5.00m accepted, 5.01m rejected!
            Assert.IsTrue(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 25, AttackType.VerticalSlash, new Vector3(0f, 0f, 5.00f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 25, AttackType.VerticalSlash, new Vector3(0f, 0f, 5.01f), victimPos, _config));

            // Exact Ranged sanity boundary: 34.00m accepted, 34.01m rejected!
            Assert.IsTrue(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 15, AttackType.RangedProjectile, new Vector3(0f, 0f, 34.00f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 15, AttackType.RangedProjectile, new Vector3(0f, 0f, 34.01f), victimPos, _config));

            // MatchEnded (!isMatchPlaying) and NaN/Infinity origin rejected:
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                false, false, _attackerA, _victimPlayer, 25, AttackType.VerticalSlash, new Vector3(0f, 0f, 1.5f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 25, AttackType.VerticalSlash, new Vector3(float.NaN, 0f, 1f), victimPos, _config));
            Assert.IsFalse(PlayerHealth.ValidateDamageRequest(
                true, false, _attackerA, _victimPlayer, 25, AttackType.VerticalSlash, new Vector3(float.PositiveInfinity, 0f, 1f), victimPos, _config));
        }

        [Test]
        public void MixedCanonicalDamageAndDuplicateAttackTick_DeduplicatesRetransmissionsAndForwardsHitFeedback()
        {
            int feedbackCount = 0;
            Vector3 lastHitPos = Vector3.zero;
            int lastDamage = 0;
            _health.OnHitFeedbackReceived += (pos, dmg, _, _) =>
            {
                feedbackCount++;
                lastHitPos = pos;
                lastDamage = dmg;
            };

            Vector3 meleeOrigin = new Vector3(0f, 0f, 2.0f);
            Vector3 customImpact = new Vector3(0.2f, 1.1f, 0.3f);

            // 100 -> 75 (VerticalSlash 25 @ tick 100):
            Assert.IsTrue(_health.TryProcessAuthoritativeDamage(_attackerA, 25, AttackType.VerticalSlash, meleeOrigin, attackTick: 100, hitPoint: customImpact));
            Assert.AreEqual(75, _health.CurrentHP);
            Assert.AreEqual(1, feedbackCount);
            Assert.AreEqual(customImpact, lastHitPos);
            Assert.AreEqual(25, lastDamage);

            // Duplicate RPC retransmission for same (_attackerA, attackTick: 100) MUST be ignored!
            Assert.IsFalse(_health.TryProcessAuthoritativeDamage(_attackerA, 25, AttackType.VerticalSlash, meleeOrigin, attackTick: 100, hitPoint: customImpact));
            Assert.AreEqual(75, _health.CurrentHP, "Duplicate (attacker, attackTick) RPC must be idempotently dropped.");

            // 75 -> 45 (HorizontalSweep 30 @ tick 140), then 45 -> 30 (RangedProjectile 15 @ tick 180):
            Assert.IsTrue(_health.TryProcessAuthoritativeDamage(_attackerA, 30, AttackType.HorizontalSweep, meleeOrigin, attackTick: 140));
            Assert.AreEqual(45, _health.CurrentHP);

            Assert.IsTrue(_health.TryProcessAuthoritativeDamage(_attackerB, 15, AttackType.RangedProjectile, new Vector3(0f, 1f, 20f), attackTick: 180));
            Assert.AreEqual(30, _health.CurrentHP);
            Assert.AreEqual(3, feedbackCount);
        }

        [Test]
        public void LethalSequence_ConcurrentKillingBlows_ClampsZeroFiresKillConfirmedAndAppliesFullGhostStateImmediately()
        {
            int healthEvents = 0;
            int deathEvents = 0;
            int killConfirmedEvents = 0;
            int eliminationFxEvents = 0;
            int localSpectatorEvents = 0;
            PlayerRef recordedKiller = PlayerRef.None;

            _health.OnHealthChanged += (_, _) => healthEvents++;
            _health.OnPlayerDied += (_, killer) =>
            {
                deathEvents++;
                recordedKiller = killer;
            };
            _health.OnKillConfirmed += (killer, victim) =>
            {
                killConfirmedEvents++;
                Assert.AreEqual(_attackerA, killer);
                Assert.AreEqual(_victimPlayer, victim);
            };
            _deathController.OnEliminationEffectsRequested += _ => eliminationFxEvents++;
            _deathController.OnLocalSpectatorModeEntered += () => localSpectatorEvents++;

            // Start an active Dodge Roll so we prove death cancels mobility and snaps VisualRoot immediately!
            NetworkInputData dodgeInput = new NetworkInputData { LookYaw = 0f };
            dodgeInput.Buttons.Set((int)NetworkInputButtons.DodgeLeft, true);
            _mobility.SimulateAuthoritativeTick(in dodgeInput, 1f / 60f, _config, groundedOverride: true);
            Assert.AreEqual(MobilityState.DodgingLeft, _mobility.CurrentMobilityState);

            // 3x HorizontalSweep (90 dmg -> 10 HP remaining):
            Vector3 meleeOrigin = new Vector3(0f, 0f, 2.2f);
            Assert.IsTrue(_health.TryProcessAuthoritativeDamage(_attackerA, 30, AttackType.HorizontalSweep, meleeOrigin, attackTick: 10));
            Assert.IsTrue(_health.TryProcessAuthoritativeDamage(_attackerA, 30, AttackType.HorizontalSweep, meleeOrigin, attackTick: 90));
            Assert.IsTrue(_health.TryProcessAuthoritativeDamage(_attackerA, 30, AttackType.HorizontalSweep, meleeOrigin, attackTick: 170));
            Assert.AreEqual(10, _health.CurrentHP);

            // Same-tick simultaneous killing blows from Attacker A (25 dmg overkill) and Attacker B (30 dmg):
            bool firstLethal = _health.TryProcessAuthoritativeDamage(_attackerA, 25, AttackType.VerticalSlash, meleeOrigin, attackTick: 210);
            bool secondLethal = _health.TryProcessAuthoritativeDamage(_attackerB, 30, AttackType.HorizontalSweep, meleeOrigin, attackTick: 210);

            Assert.IsTrue(firstLethal);
            Assert.IsFalse(secondLethal, "Second lethal hit on same tick must be rejected once IsDead == true.");
            Assert.AreEqual(0, _health.CurrentHP, "CurrentHP must clamp strictly to 0, never negative.");
            Assert.IsTrue(_health.IsDead);
            Assert.AreEqual(1, deathEvents);
            Assert.AreEqual(1, killConfirmedEvents);
            Assert.AreEqual(1, eliminationFxEvents);
            Assert.AreEqual(1, localSpectatorEvents);
            Assert.AreEqual(_attackerA, recordedKiller);
            Assert.AreEqual(4, healthEvents);

            // Verify immediate Story 6-3 GhostSpectator state without Despawn:
            Assert.IsNotNull(_victimGo);
            Assert.AreEqual(ArenaLayers.GhostSpectator, _victimGo.layer, "Root capsule must transition to Layer 10 on lethal tick.");
            Assert.AreEqual(ArenaLayers.GhostSpectator, _visualRoot.gameObject.layer);
            Assert.AreSame(_ghostMat, _meshRenderer.sharedMaterial);
            Assert.IsFalse(_hpBarGo.activeSelf, "Overhead HP bar must hide upon elimination.");
            Assert.IsFalse(_hitboxRootGo.activeSelf, "Combat hitbox root must disable upon elimination.");
            Assert.AreEqual(0f, _mobilePadCanvasGroup.alpha, 0.001f, "Local Mobile Action Pad must hide on death.");
            Assert.IsFalse(_mobilePadCanvasGroup.interactable);
            Assert.IsTrue(_deathController.IsCombatInputLocked);
            Assert.AreEqual("Duelist_01 [GHOST]", _deathController.CurrentFormattedNameplate);
            Assert.AreEqual(0.50f, _deathController.CurrentNameplateAlpha, 0.001f, "Ghost nameplate opacity must dim to 50%.");
            Assert.AreEqual(MobilityState.Normal, _mobility.CurrentMobilityState);
            Assert.AreEqual(Quaternion.identity, _visualRoot.localRotation);

            // Rematch RestoreAliveState brings capsule back to Layer 8 opaque state cleanly!
            _health.ResetHealthToFull();
            Assert.AreEqual(100, _health.CurrentHP);
            Assert.IsFalse(_health.IsDead);
            Assert.AreEqual(ArenaLayers.PlayerCharacter, _victimGo.layer);
            Assert.AreSame(_aliveMat, _meshRenderer.sharedMaterial);
            Assert.IsTrue(_hpBarGo.activeSelf);
            Assert.IsTrue(_hitboxRootGo.activeSelf);
            Assert.AreEqual(1f, _mobilePadCanvasGroup.alpha, 0.001f);
            Assert.IsTrue(_mobilePadCanvasGroup.interactable);
            Assert.IsFalse(_deathController.IsCombatInputLocked);
            Assert.AreEqual("Duelist_01", _deathController.CurrentFormattedNameplate);
            Assert.AreEqual(1.0f, _deathController.CurrentNameplateAlpha, 0.001f);
        }
    }
}
