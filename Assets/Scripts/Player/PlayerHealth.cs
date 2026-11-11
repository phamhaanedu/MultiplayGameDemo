using System;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Player
{
    /// <summary>
    /// Victim-authoritative networked health, tick-idempotent damage RPC validator, and immediate
    /// Layer 10 Ghost death trigger using Photon Fusion 2 <see cref="ChangeDetector"/>
    /// (Stories 6-1, 6-2, 6-3; ADR-0004; TR-health-001..003).
    /// </summary>
    public sealed class PlayerHealth : NetworkBehaviour, IDamageReceiver
    {
        /// <summary>Maximum plausible attacker-to-victim distance in meters for melee RPCs (3.0m + lag margin).</summary>
        public const float MaxMeleeSanityDistance = 5.0f;

        /// <summary>Squared maximum melee sanity distance (25.0 m^2).</summary>
        public const float MaxMeleeSanityDistanceSqr = MaxMeleeSanityDistance * MaxMeleeSanityDistance;

        /// <summary>Maximum plausible launch-to-victim distance in meters for ranged RPCs (30.0m + lag margin).</summary>
        public const float MaxRangedSanityDistance = 34.0f;

        /// <summary>Squared maximum ranged sanity distance (1156.0 m^2).</summary>
        public const float MaxRangedSanityDistanceSqr = MaxRangedSanityDistance * MaxRangedSanityDistance;

        private const int RecentHitHistoryCapacity = 8;

        [Header("Configuration & Death Handler")]
        [Tooltip("Shared immutable combat config asset providing MaxHealth and canonical attack damages.")]
        [SerializeField] private CharacterCombatConfigSO _combatConfig;

        [Tooltip("Component managing Layer 10 GhostSpectator transition on elimination.")]
        [SerializeField] private PlayerDeathStateController _deathStateController;

        /// <summary>Gets the replicated victim hit points in [0, MaxHP].</summary>
        [Networked] public int NetworkedCurrentHP { get; private set; }

        /// <summary>Gets a replicated flag indicating whether this combatant has been eliminated.</summary>
        [Networked] public NetworkBool NetworkedIsDead { get; private set; }

        /// <summary>Gets the replicated <see cref="PlayerRef"/> credited with the killing blow.</summary>
        [Networked] public PlayerRef NetworkedLastKiller { get; private set; }

        private readonly PlayerRef[] _recentHitAttackers = new PlayerRef[RecentHitHistoryCapacity];
        private readonly int[] _recentHitTicks = new int[RecentHitHistoryCapacity];
        private int _recentHitWriteCursor;

        private ChangeDetector _changeDetector;
        private Func<bool> _matchPlayingPredicate;
        private int _localCurrentHp = 100;
        private bool _localIsDead;
        private bool _hasAppliedDeathPresentation;
        private PlayerRef _localLastKiller = PlayerRef.None;
        private PlayerRef _standaloneOwner = PlayerRef.None;
        private bool _standaloneHasStateAuthority = true;
        private bool _isMatchPlaying = true;

        /// <summary>Raised whenever CurrentHP changes: (currentHp, maxHp).</summary>
        public event Action<int, int> OnHealthChanged;

        /// <summary>Raised strictly once per elimination: (victim, killer).</summary>
        public event Action<PlayerRef, PlayerRef> OnPlayerDied;

        /// <summary>Raised on all peers when a kill is authoritatively confirmed: (killer, victim).</summary>
        public event Action<PlayerRef, PlayerRef> OnKillConfirmed;

        /// <summary>Raised on all peers when a validated hit lands: (hitWorldPos, damage, isLethal, attacker).</summary>
        public event Action<Vector3, int, bool, PlayerRef> OnHitFeedbackReceived;

        /// <summary>Raised on all peers when a living player receives healing: (healWorldPos, healedAmount).</summary>
        public event Action<Vector3, int> OnHealFeedbackReceived;

        /// <summary>Global observer event emitted on all peers when any player takes a validated hit: (victim, hitPos, damage, isLethal, attacker).</summary>
        public static event Action<PlayerHealth, Vector3, int, bool, PlayerRef> OnAnyHitFeedbackBroadcast;

        /// <summary>Global observer event emitted on all peers when any living player gains HP (e.g., health potion): (target, healPos, healedAmount).</summary>
        public static event Action<PlayerHealth, Vector3, int> OnAnyHealFeedbackBroadcast;

        /// <summary>Global observer event emitted on all peers when any elimination is confirmed: (killer, victim).</summary>
        public static event Action<PlayerRef, PlayerRef> OnAnyKillConfirmedBroadcast;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticHealthEvents()
        {
            OnAnyHitFeedbackBroadcast = null;
            OnAnyHealFeedbackBroadcast = null;
            OnAnyKillConfirmedBroadcast = null;
        }

        /// <summary>Gets the maximum hit points from config (canonical: 100).</summary>
        public int MaxHP => _combatConfig != null ? _combatConfig.MaxHealth : 100;

        /// <summary>Gets the current clamped hit points in [0, MaxHP].</summary>
        public int CurrentHP => IsSpawnedOnRunner ? NetworkedCurrentHP : _localCurrentHp;

        /// <inheritdoc />
        public bool IsDead => IsSpawnedOnRunner ? (bool)NetworkedIsDead : _localIsDead;

        /// <inheritdoc />
        public PlayerRef OwnerPlayer => IsSpawnedOnRunner ? Object.InputAuthority : _standaloneOwner;

        /// <inheritdoc />
        public Transform ReceiverTransform => transform;

        /// <summary>Gets the <see cref="PlayerRef"/> credited with the killing blow.</summary>
        public PlayerRef LastKiller => IsSpawnedOnRunner ? NetworkedLastKiller : _localLastKiller;

        private bool IsSpawnedOnRunner => Object != null && Runner != null;
        private bool HasEffectiveStateAuthority => IsSpawnedOnRunner ? Object.HasStateAuthority : _standaloneHasStateAuthority;
        private bool IsMatchCurrentlyPlaying => _matchPlayingPredicate != null ? _matchPlayingPredicate() : _isMatchPlaying;

        private void Awake()
        {
            if (_deathStateController == null)
            {
                TryGetComponent(out _deathStateController);
            }

            _localCurrentHp = MaxHP;
            DamageReceiverRegistry.Register(gameObject, this);
        }

        private void Start()
        {
            ValidateConfigOrDisable();
        }

        private void OnDestroy()
        {
            DamageReceiverRegistry.Unregister(gameObject, this);
        }

        /// <summary>
        /// Validates that <see cref="CharacterCombatConfigSO"/> is assigned, logging an error and disabling if null (Story 1-1 AC-3).
        /// </summary>
        public bool ValidateConfigOrDisable()
        {
            return CharacterCombatConfigSO.EnsureValidConfig(_combatConfig, this);
        }

        /// <summary>Binds an external match-playing state provider (e.g., MatchManager in Sprint 002).</summary>
        public void BindMatchPlayingPredicate(Func<bool> matchPlayingPredicate)
        {
            _matchPlayingPredicate = matchPlayingPredicate;
        }

        /// <summary>Configures dependencies and authority flags for standalone EditMode unit tests.</summary>
        public void ConfigureForStandaloneTest(
            CharacterCombatConfigSO config,
            PlayerRef ownerPlayer,
            bool hasStateAuthority = true,
            bool isMatchPlaying = true,
            PlayerDeathStateController deathController = null)
        {
            _combatConfig = config;
            _standaloneOwner = ownerPlayer;
            _standaloneHasStateAuthority = hasStateAuthority;
            _isMatchPlaying = isMatchPlaying;
            if (deathController != null)
            {
                _deathStateController = deathController;
            }

            DamageReceiverRegistry.Register(gameObject, this);
            DamageReceiverRegistry.RegisterPlayerOwner(ownerPlayer, this);
            ResetHealthToFull();
        }

        /// <summary>Updates whether the match state currently permits combat damage (Playing == true).</summary>
        public void SetMatchPlayingState(bool isPlaying)
        {
            _isMatchPlaying = isPlaying;
        }

        /// <inheritdoc />
        public override void Spawned()
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            _deathStateController?.SetIsLocalPlayerAvatar(Object.InputAuthority == Runner.LocalPlayer);
            DamageReceiverRegistry.Register(gameObject, this);
            DamageReceiverRegistry.RegisterPlayerOwner(Object.InputAuthority, this);

            if (Object.HasStateAuthority)
            {
                NetworkedCurrentHP = MaxHP;
                NetworkedIsDead = false;
                NetworkedLastKiller = PlayerRef.None;
                _hasAppliedDeathPresentation = false;
            }

            _localCurrentHp = CurrentHP;
            _localIsDead = IsDead;
            OnHealthChanged?.Invoke(CurrentHP, MaxHP);

            // Synchronize initial Layer 10 Ghost state for late-joining peers if combatant died earlier.
            if (IsDead && !_hasAppliedDeathPresentation)
            {
                _hasAppliedDeathPresentation = true;
                _deathStateController?.ApplyDeathState(isAuthoritativeSimulation: false, playEliminationEffects: false);
            }
        }

        /// <inheritdoc />
        public override void Render()
        {
            foreach (string propertyName in _changeDetector.DetectChanges(this))
            {
                if (propertyName == nameof(NetworkedCurrentHP))
                {
                    OnHealthChanged?.Invoke(NetworkedCurrentHP, MaxHP);
                }
                else if (propertyName == nameof(NetworkedIsDead))
                {
                    ApplyRenderDeathTransition(NetworkedIsDead, NetworkedLastKiller);
                }
            }
        }

        /// <inheritdoc />
        public void RequestDamageFromAttacker(
            PlayerRef attacker,
            int damage,
            AttackType attackType,
            Vector3 attackerOrigin,
            int attackTick = 0,
            Vector3 hitPoint = default)
        {
            if (IsSpawnedOnRunner)
            {
                int tick = attackTick > 0 ? attackTick : Runner.Tick.Raw;
                Rpc_RequestApplyDamage(attacker, damage, attackType, attackerOrigin, tick, hitPoint);
                return;
            }

            TryProcessAuthoritativeDamage(attacker, damage, attackType, attackerOrigin, attackTick, hitPoint);
        }

        /// <summary>
        /// RPC sent from an attacker's hit detector to this victim's StateAuthority peer.
        /// Validates match state, alive state, self-hit guard, tick idempotency, canonical damage, and distance sanity.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void Rpc_RequestApplyDamage(
            PlayerRef attacker,
            int damage,
            AttackType attackType,
            Vector3 attackerOrigin,
            int attackTick,
            Vector3 hitPoint)
        {
            TryProcessAuthoritativeDamage(attacker, damage, attackType, attackerOrigin, attackTick, hitPoint);
        }

        /// <summary>
        /// RPC broadcast from the victim's StateAuthority to all peers to trigger hit flash, sound, and VFX.
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void Rpc_BroadcastHitFeedback(
            Vector3 hitPosition,
            int appliedDamage,
            NetworkBool isLethal,
            PlayerRef attacker)
        {
            OnHitFeedbackReceived?.Invoke(hitPosition, appliedDamage, isLethal, attacker);
            OnAnyHitFeedbackBroadcast?.Invoke(this, hitPosition, appliedDamage, isLethal, attacker);
        }

        /// <summary>
        /// RPC broadcast from StateAuthority to all peers when this combatant recovers HP (e.g., health potion).
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void Rpc_BroadcastHealFeedback(Vector3 healPosition, int healedAmount)
        {
            OnHealFeedbackReceived?.Invoke(healPosition, healedAmount);
            OnAnyHealFeedbackBroadcast?.Invoke(this, healPosition, healedAmount);
        }

        /// <summary>
        /// Validates and applies positive HP recovery strictly on StateAuthority for a living player,
        /// clamping to MaxHP and broadcasting green floating heal feedback to all peers.
        /// </summary>
        public bool TryProcessAuthoritativeHeal(int healAmount, Vector3 healPoint = default)
        {
            if (!HasEffectiveStateAuthority || !IsMatchCurrentlyPlaying || IsDead || healAmount <= 0)
            {
                return false;
            }

            int prevHp = CurrentHP;
            int nextHp = Mathf.Clamp(prevHp + healAmount, 0, MaxHP);
            int actualHealed = nextHp - prevHp;
            if (actualHealed <= 0)
            {
                return false;
            }

            _localCurrentHp = nextHp;
            if (IsSpawnedOnRunner)
            {
                NetworkedCurrentHP = nextHp;
            }

            OnHealthChanged?.Invoke(nextHp, MaxHP);

            Vector3 resolvedPos = healPoint.sqrMagnitude > 0.0001f
                ? healPoint
                : transform.position + Vector3.up * 1.0f;

            if (IsSpawnedOnRunner)
            {
                Rpc_BroadcastHealFeedback(resolvedPos, actualHealed);
            }
            else
            {
                OnHealFeedbackReceived?.Invoke(resolvedPos, actualHealed);
                OnAnyHealFeedbackBroadcast?.Invoke(this, resolvedPos, actualHealed);
            }

            return true;
        }

        /// <summary>
        /// RPC broadcast from the victim's StateAuthority to all peers confirming an elimination for HUD/MatchManager.
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void Rpc_NotifyKillConfirmed(PlayerRef killer, PlayerRef victim)
        {
            OnKillConfirmed?.Invoke(killer, victim);
            OnAnyKillConfirmedBroadcast?.Invoke(killer, victim);
        }

        /// <summary>
        /// Validates and applies damage strictly on StateAuthority, clamping HP to [0, MaxHP],
        /// transitioning immediately to Layer 10 GhostSpectator on lethal hits, and broadcasting feedback.
        /// </summary>
        public bool TryProcessAuthoritativeDamage(
            PlayerRef attacker,
            int damage,
            AttackType attackType,
            Vector3 attackerOrigin,
            int attackTick = 0,
            Vector3 hitPoint = default)
        {
            if (!HasEffectiveStateAuthority || IsDuplicateAttackTick(attacker, attackTick))
            {
                return false;
            }

            Vector3 verifiedOrigin = ResolveVerifiedAttackerOrigin(attacker, attackType, attackerOrigin);
            if (!ValidateDamageRequest(
                IsMatchCurrentlyPlaying, IsDead, attacker, OwnerPlayer,
                damage, attackType, verifiedOrigin, transform.position, _combatConfig))
            {
                return false;
            }

            RecordProcessedAttackTick(attacker, attackTick);
            int nextHp = Mathf.Clamp(CurrentHP - damage, 0, MaxHP);
            bool becameLethal = nextHp == 0;
            CommitAuthoritativeHealthMutation(nextHp, becameLethal, attacker);
            DispatchHitAndKillFeedback(damage, becameLethal, attacker, hitPoint);
            return true;
        }

        /// <summary>
        /// Places a mid-match late-joining avatar (joining strictly after the configured active combatant grace window)
        /// directly into Layer 10 Ghost Spectator mode without playing death burst VFX/SFX or crediting a kill.
        /// </summary>
        public void EnterLateJoinGhostSpectator()
        {
            if (!HasEffectiveStateAuthority || IsDead)
            {
                return;
            }

            _localCurrentHp = 0;
            _localIsDead = true;
            _localLastKiller = PlayerRef.None;
            _hasAppliedDeathPresentation = true;

            if (IsSpawnedOnRunner)
            {
                NetworkedCurrentHP = 0;
                NetworkedLastKiller = PlayerRef.None;
                NetworkedIsDead = true;
            }

            OnHealthChanged?.Invoke(0, MaxHP);
            _deathStateController?.ApplyDeathState(isAuthoritativeSimulation: true, playEliminationEffects: false);
            OnPlayerDied?.Invoke(OwnerPlayer, PlayerRef.None);
        }

        /// <summary>
        /// Restores full health and alive Layer 8 state on StateAuthority at rematch start.
        /// </summary>
        public void ResetHealthToFull()
        {
            if (!HasEffectiveStateAuthority)
            {
                return;
            }

            _localCurrentHp = MaxHP;
            _localIsDead = false;
            _localLastKiller = PlayerRef.None;
            _hasAppliedDeathPresentation = false;
            ClearRecentHitHistory();

            if (IsSpawnedOnRunner)
            {
                NetworkedCurrentHP = MaxHP;
                NetworkedIsDead = false;
                NetworkedLastKiller = PlayerRef.None;
            }

            _deathStateController?.RestoreAliveState(isAuthoritativeSimulation: true);
            OnHealthChanged?.Invoke(MaxHP, MaxHP);
        }

        /// <summary>
        /// Pure victim-side sanity validator rejecting self-hits, dead targets, non-playing match phases,
        /// forged damage numbers, NaN coordinates, and implausible attacker distances (Story 6-2).
        /// </summary>
        public static bool ValidateDamageRequest(
            bool isMatchPlaying,
            bool isVictimDead,
            PlayerRef attacker,
            PlayerRef victimOwner,
            int damage,
            AttackType attackType,
            Vector3 attackerOrigin,
            Vector3 victimPosition,
            ICharacterCombatConfig config)
        {
            if (!isMatchPlaying || isVictimDead || damage <= 0)
            {
                return false;
            }

            if (attacker != PlayerRef.None && attacker == victimOwner)
            {
                return false;
            }

            if (!IsCanonicalDamage(damage, attackType, config))
            {
                return false;
            }

            return IsWithinPlausibleDistance(attackType, attackerOrigin, victimPosition);
        }

        private Vector3 ResolveVerifiedAttackerOrigin(PlayerRef attacker, AttackType attackType, Vector3 reportedOrigin)
        {
            // For melee attacks, prefer the attacker avatar's authoritative world transform when registered
            // so a modified client cannot spoof melee proximity from across the map.
            if (attackType != AttackType.RangedProjectile &&
                DamageReceiverRegistry.TryGetByPlayer(attacker, out IDamageReceiver attackerReceiver) &&
                attackerReceiver.ReceiverTransform != null)
            {
                return attackerReceiver.ReceiverTransform.position;
            }

            return reportedOrigin;
        }

        private void CommitAuthoritativeHealthMutation(int nextHp, bool becameLethal, PlayerRef attacker)
        {
            _localCurrentHp = nextHp;
            if (IsSpawnedOnRunner)
            {
                NetworkedCurrentHP = nextHp;
            }

            OnHealthChanged?.Invoke(nextHp, MaxHP);
            if (!becameLethal)
            {
                return;
            }

            _localIsDead = true;
            _localLastKiller = attacker;
            _hasAppliedDeathPresentation = true;

            if (IsSpawnedOnRunner)
            {
                NetworkedLastKiller = attacker;
                NetworkedIsDead = true;
            }

            // Transition onto Layer 10 (GhostSpectator) IMMEDIATELY inside the lethal simulation/RPC tick!
            _deathStateController?.ApplyDeathState(isAuthoritativeSimulation: true);
            OnPlayerDied?.Invoke(OwnerPlayer, attacker);
        }

        private void DispatchHitAndKillFeedback(int damage, bool becameLethal, PlayerRef attacker, Vector3 hitPoint)
        {
            Vector3 resolvedHitPos = hitPoint.sqrMagnitude > 0.0001f
                ? hitPoint
                : transform.position + Vector3.up * 1.0f;

            if (IsSpawnedOnRunner)
            {
                Rpc_BroadcastHitFeedback(resolvedHitPos, damage, becameLethal, attacker);
                if (becameLethal)
                {
                    Rpc_NotifyKillConfirmed(attacker, OwnerPlayer);
                }

                return;
            }

            OnHitFeedbackReceived?.Invoke(resolvedHitPos, damage, becameLethal, attacker);
            OnAnyHitFeedbackBroadcast?.Invoke(this, resolvedHitPos, damage, becameLethal, attacker);
            if (becameLethal)
            {
                OnKillConfirmed?.Invoke(attacker, OwnerPlayer);
                OnAnyKillConfirmedBroadcast?.Invoke(attacker, OwnerPlayer);
            }
        }

        private void ApplyRenderDeathTransition(bool isDeadNow, PlayerRef killer)
        {
            if (isDeadNow)
            {
                if (_hasAppliedDeathPresentation)
                {
                    return;
                }

                _hasAppliedDeathPresentation = true;
                bool playEffects = killer != PlayerRef.None;
                _deathStateController?.ApplyDeathState(isAuthoritativeSimulation: false, playEliminationEffects: playEffects);
                OnPlayerDied?.Invoke(OwnerPlayer, killer);
            }
            else if (_hasAppliedDeathPresentation)
            {
                _hasAppliedDeathPresentation = false;
                _deathStateController?.RestoreAliveState(isAuthoritativeSimulation: false);
            }
        }

        private bool IsDuplicateAttackTick(PlayerRef attacker, int attackTick)
        {
            if (attackTick <= 0 || attacker == PlayerRef.None)
            {
                return false;
            }

            for (int i = 0; i < RecentHitHistoryCapacity; i++)
            {
                if (_recentHitTicks[i] == attackTick && _recentHitAttackers[i] == attacker)
                {
                    return true;
                }
            }

            return false;
        }

        private void RecordProcessedAttackTick(PlayerRef attacker, int attackTick)
        {
            if (attackTick <= 0 || attacker == PlayerRef.None)
            {
                return;
            }

            _recentHitAttackers[_recentHitWriteCursor] = attacker;
            _recentHitTicks[_recentHitWriteCursor] = attackTick;
            _recentHitWriteCursor = (_recentHitWriteCursor + 1) % RecentHitHistoryCapacity;
        }

        private void ClearRecentHitHistory()
        {
            Array.Clear(_recentHitAttackers, 0, RecentHitHistoryCapacity);
            Array.Clear(_recentHitTicks, 0, RecentHitHistoryCapacity);
            _recentHitWriteCursor = 0;
        }

        private static bool IsCanonicalDamage(int damage, AttackType attackType, ICharacterCombatConfig cfg)
        {
            int expected = attackType switch
            {
                AttackType.VerticalSlash => cfg != null ? cfg.VerticalSlashDamage : 25,
                AttackType.HorizontalSweep => cfg != null ? cfg.HorizontalSweepDamage : 30,
                AttackType.RangedProjectile => cfg != null ? cfg.RangedDamage : 15,
                _ => -1
            };

            return expected > 0 && damage == expected;
        }

        private static bool IsWithinPlausibleDistance(
            AttackType attackType,
            Vector3 attackerOrigin,
            Vector3 victimPosition)
        {
            if (HasInvalidFloat(attackerOrigin) || HasInvalidFloat(victimPosition))
            {
                return false;
            }

            float distSqr = (attackerOrigin - victimPosition).sqrMagnitude;
            float maxDistSqr = attackType == AttackType.RangedProjectile
                ? MaxRangedSanityDistanceSqr
                : MaxMeleeSanityDistanceSqr;

            return distSqr <= maxDistSqr;
        }

        private static bool HasInvalidFloat(Vector3 v)
        {
            return float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                   || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z);
        }
    }
}
