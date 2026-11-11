using System;
using System.Collections.Generic;
using CapsuleArena.Combat.Projectile;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Combat
{
    /// <summary>
    /// Payload emitted whenever any combatant executes a melee swing so HUD, 3D audio, and URP slash VFX stay decoupled.
    /// </summary>
    public readonly struct MeleeSwingEvent
    {
        /// <summary>World position of the attacker at swing start.</summary>
        public readonly Vector3 Origin;

        /// <summary>World rotation of the attacker at swing start.</summary>
        public readonly Quaternion Rotation;

        /// <summary>Swing variant (VerticalSlash, HorizontalSweepLeft, HorizontalSweepRight).</summary>
        public readonly MeleeSwingKind Kind;

        /// <summary>Owning PlayerRef of the attacker.</summary>
        public readonly PlayerRef Attacker;

        /// <summary>Initializes an immutable <see cref="MeleeSwingEvent"/> payload.</summary>
        public MeleeSwingEvent(Vector3 origin, Quaternion rotation, MeleeSwingKind kind, PlayerRef attacker)
        {
            Origin = origin;
            Rotation = rotation;
            Kind = kind;
            Attacker = attacker;
        }
    }

    /// <summary>
    /// Tick-authoritative multidirectional combat controller executing:
    /// - <c>LMB</c> Vertical Slash (25 dmg, 2.5m, 50 deg cone, 0.6s / 36-tick CD)
    /// - <c>Keys 1/3</c> Horizontal Sweep Left/Right (30 dmg, 3.0m, 90 deg cone offset +-45 deg, 1.2s / 72-tick shared CD)
    ///   with 11-tick (0.18s) +-90 deg authoritative character + camera yaw snap-turn
    /// - <c>RMB</c> Ranged Fire (15 dmg, 0.4s / 24-tick CD, pooled <see cref="NetworkProjectile"/> with flush-wall Linecast guard)
    /// (Stories 7-1, 7-2, 7-3; ADR-0005; TR-combat-001..003).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class PlayerCombatController : NetworkBehaviour
    {
        private static readonly Collider[] OverlapBuffer = new Collider[16];

        [Header("Configuration & Dependencies")]
        [Tooltip("Immutable combat config asset supplying damage, sector cosines, ranges, and cooldowns.")]
        [SerializeField] private CharacterCombatConfigSO _combatConfig;

        [Tooltip("Pooled NetworkProjectile prefab spawned on RMB Ranged Fire.")]
        [SerializeField] private NetworkObject _projectilePrefab;

        [Tooltip("Sibling PlayerHealth component used to suppress attacks while dead and bind hit-markers.")]
        [SerializeField] private PlayerHealth _playerHealth;

        [Tooltip("Sibling PlayerMobilityController synchronized with the 90-deg Horizontal Sweep yaw lock.")]
        [SerializeField] private PlayerMobilityController _mobilityController;

        [Tooltip("Optional muzzle transform; defaults to canonical +1.1m up, +0.65m forward offset.")]
        [SerializeField] private Transform _muzzleTransform;

        /// <summary>Gets the replicated Vertical Slash (LMB) cooldown timer.</summary>
        [Networked] public TickTimer VerticalSlashCooldown { get; private set; }

        /// <summary>Gets the replicated shared Horizontal Sweep (Keys 1/3) cooldown timer.</summary>
        [Networked] public TickTimer HorizontalSweepCooldown { get; private set; }

        /// <summary>Gets the replicated Ranged Fire (RMB) cooldown timer.</summary>
        [Networked] public TickTimer RangedFireCooldown { get; private set; }

        /// <summary>Gets the replicated active 11-tick Horizontal Sweep turn timer.</summary>
        [Networked] public TickTimer ActiveSweepTurnTimer { get; private set; }

        /// <summary>Gets the replicated elapsed ticks of the active Horizontal Sweep turn for rollback determinism.</summary>
        [Networked] public int NetworkedSweepElapsedTicks { get; private set; }

        /// <summary>Gets the replicated yaw at the start of the active Horizontal Sweep.</summary>
        [Networked] public float SweepStartYaw { get; private set; }

        /// <summary>Gets the replicated signed turn delta (-90 or +90) of the active Horizontal Sweep.</summary>
        [Networked] public float SweepSignedDeltaYaw { get; private set; }

        private readonly HashSet<EntityId> _hitReceiversThisSwing = new HashSet<EntityId>(16);
        private NetworkRunner.OnBeforeSpawned _cachedProjectileBeforeSpawned;
        private LocalInputPoller _localInputPoller;
        private Func<bool> _matchPlayingPredicate;

        private Vector3 _pendingSpawnMuzzle;
        private Vector3 _pendingSpawnAimDir;
        private ICharacterCombatConfig _pendingSpawnConfig;

        private int _localVerticalCdTicks;
        private int _localSweepCdTicks;
        private int _localRangedCdTicks;
        private int _localSweepElapsedTicks;
        private bool _localIsSweepingTurn;
        private bool _mobilityYawOverrideActive;
        private float _localSweepStartYaw;
        private float _localSweepSignedDelta;
        private float _currentAuthoritativeYaw;
        private PlayerRef _standaloneOwner = PlayerRef.None;
        private int _standaloneSpawnedProjectileCount;
        private Vector3 _lastSpawnedAimDirection = Vector3.forward;

        /// <summary>Raised whenever any combat cooldown starts or ticks: (skillId, remainingSec, totalSec).</summary>
        public event Action<CombatSkillId, float, float> OnCooldownUpdated;

        /// <summary>Raised locally when this player lands a confirmed hit on an opponent: (isLethal).</summary>
        public event Action<bool> OnLocalHitMarkerTriggered;

        /// <summary>Raised locally when Horizontal Sweep starts so the HUD can flash the 90-deg arc indicator.</summary>
        public event Action<bool> OnHorizontalSweepIndicatorTriggered;

        /// <summary>Global presentation event emitted whenever any player executes a melee swing.</summary>
        public static event Action<MeleeSwingEvent> OnAnyMeleeSwingExecuted;

        /// <summary>Global presentation event emitted whenever a ranged shot fires or impacts a flush wall.</summary>
        public static event Action<Vector3, Vector3, bool> OnAnyRangedShotExecuted;

        /// <summary>Gets a value indicating whether an 11-tick Horizontal Sweep 90-deg turn is currently active.</summary>
        public bool IsPerformingHorizontalSweepTurn =>
            IsSpawnedOnRunner ? !ActiveSweepTurnTimer.ExpiredOrNotRunning(Runner) : _localIsSweepingTurn;

        /// <summary>Gets the current authoritative character yaw in degrees.</summary>
        public float CurrentAuthoritativeYaw => _currentAuthoritativeYaw;

        /// <summary>Gets the number of ranged projectiles spawned in standalone test mode.</summary>
        public int StandaloneSpawnedProjectileCount => _standaloneSpawnedProjectileCount;

        /// <summary>Gets the 3D world aim direction of the most recently fired ranged bolt.</summary>
        public Vector3 LastSpawnedAimDirection => _lastSpawnedAimDirection;

        private bool IsSpawnedOnRunner => Object != null && Runner != null;
        private PlayerRef EffectiveOwner => IsSpawnedOnRunner ? Object.InputAuthority : _standaloneOwner;
        private bool IsCombatAllowedByMatch => _matchPlayingPredicate == null || _matchPlayingPredicate();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEvents()
        {
            OnAnyMeleeSwingExecuted = null;
            OnAnyRangedShotExecuted = null;
        }

        private void Awake()
        {
            if (_playerHealth == null) TryGetComponent(out _playerHealth);
            if (_mobilityController == null) TryGetComponent(out _mobilityController);
            _cachedProjectileBeforeSpawned = HandleBeforeProjectileSpawned;
        }

        private void OnEnable()
        {
            if (_playerHealth != null)
            {
                _playerHealth.OnPlayerDied += HandleSelfPlayerDied;
            }
        }

        private void Start()
        {
            CharacterCombatConfigSO.EnsureValidConfig(_combatConfig, this);
        }

        private void OnDisable()
        {
            if (_playerHealth != null)
            {
                _playerHealth.OnPlayerDied -= HandleSelfPlayerDied;
            }

            ReleaseYawLocks();
        }

        /// <summary>Configures dependencies for standalone EditMode/PlayMode tests.</summary>
        public void ConfigureForTest(
            CharacterCombatConfigSO config,
            PlayerRef ownerPlayer,
            PlayerHealth health = null,
            PlayerMobilityController mobility = null,
            LocalInputPoller inputPoller = null,
            NetworkObject projectilePrefab = null)
        {
            _combatConfig = config;
            _standaloneOwner = ownerPlayer;
            if (health != null)
            {
                if (_playerHealth != null) _playerHealth.OnPlayerDied -= HandleSelfPlayerDied;
                _playerHealth = health;
                _playerHealth.OnPlayerDied += HandleSelfPlayerDied;
            }

            if (mobility != null) _mobilityController = mobility;
            if (inputPoller != null) _localInputPoller = inputPoller;
            if (projectilePrefab != null) _projectilePrefab = projectilePrefab;
            _currentAuthoritativeYaw = NormalizeSignedYaw(transform.eulerAngles.y);
        }

        /// <summary>Binds the local <see cref="LocalInputPoller"/> and optional match-playing gate predicate.</summary>
        public void BindLocalServices(LocalInputPoller inputPoller, Func<bool> matchPlayingPredicate = null)
        {
            if (inputPoller != null)
            {
                _localInputPoller = inputPoller;
            }

            if (matchPlayingPredicate != null)
            {
                _matchPlayingPredicate = matchPlayingPredicate;
            }
        }

        /// <summary>Binds the authoritative match-playing gate predicate (<c>MatchState == Playing</c>).</summary>
        public void BindMatchPlayingPredicate(Func<bool> matchPlayingPredicate)
        {
            _matchPlayingPredicate = matchPlayingPredicate;
        }

        /// <summary>Notifies this attacker's HUD when one of its attacks deals confirmed victim damage.</summary>
        public void NotifyConfirmedHitOnEnemy(bool isLethal)
        {
            OnLocalHitMarkerTriggered?.Invoke(isLethal);
        }

        /// <inheritdoc />
        public override void Spawned()
        {
            _currentAuthoritativeYaw = NormalizeSignedYaw(transform.eulerAngles.y);
            if (Object.HasStateAuthority)
            {
                NetworkedSweepElapsedTicks = 0;
            }
        }

        /// <inheritdoc />
        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.HasStateAuthority)
            {
                return;
            }

            if (!GetInput(out NetworkInputData input))
            {
                input = default;
                input.LookYaw = _currentAuthoritativeYaw;
            }

            SimulateCombatTick(in input, _combatConfig);
        }

        /// <summary>
        /// Executes one deterministic 60Hz combat simulation tick (callable directly in EditMode unit tests).
        /// </summary>
        public void SimulateCombatTick(in NetworkInputData input, ICharacterCombatConfig configOverride = null)
        {
            ICharacterCombatConfig cfg = configOverride != null ? configOverride : _combatConfig;
            if (cfg == null)
            {
                return;
            }

            DecrementLocalCooldownTicks();

            if (_playerHealth != null && _playerHealth.IsDead)
            {
                CancelActiveSweepTurn();
                return;
            }

            StepActiveHorizontalSweepTurn(in input, cfg);

            if (!IsCombatAllowedByMatch)
            {
                return;
            }

            TryExecuteSkillInputs(in input, cfg);
        }

        /// <summary>
        /// Evaluates a melee sector swing immediately at <paramref name="origin"/> facing <paramref name="forward"/>,
        /// filtering <see cref="ArenaLayers.PlayerCharacter"/> via <c>OverlapSphereNonAlloc</c>, planar dot-product,
        /// and <c>Physics.Linecast</c> Line-of-Sight, returning the number of distinct living victims hit.
        /// </summary>
        public int ExecuteMeleeSectorAttack(
            MeleeSwingKind swingKind,
            Vector3 origin,
            Vector3 forward,
            ICharacterCombatConfig configOverride = null,
            int attackTick = 0)
        {
            ICharacterCombatConfig cfg = configOverride != null ? configOverride : _combatConfig;
            if (cfg == null)
            {
                return 0;
            }

            ResolveMeleeParameters(swingKind, cfg, out int damage, out float range, out float cosHalf, out float yawOffset, out AttackType attackType);
            int overlapCount = Physics.OverlapSphereNonAlloc(
                origin + Vector3.up * MeleeSectorMath.ChestHeightOffsetMeters,
                range + 0.6f,
                OverlapBuffer,
                ArenaLayers.PlayerCharacterMask,
                QueryTriggerInteraction.Ignore);

            _hitReceiversThisSwing.Clear();
            int victimsHit = 0;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider col = OverlapBuffer[i];
                if (col == null || !DamageReceiverRegistry.TryGetByCollider(col, out IDamageReceiver victim))
                {
                    continue;
                }

                if (ShouldSkipMeleeCandidate(victim, origin, forward, range, cosHalf, yawOffset))
                {
                    continue;
                }

                Vector3 hitPos = victim.ReceiverTransform.position + Vector3.up * MeleeSectorMath.ChestHeightOffsetMeters;
                victim.RequestDamageFromAttacker(EffectiveOwner, damage, attackType, origin, attackTick, hitPos);
                victimsHit++;
            }

            EmitMeleeSwingPresentation(origin, forward, swingKind);
            return victimsHit;
        }

        /// <summary>Returns remaining cooldown in seconds for the given combat skill.</summary>
        public float GetRemainingCooldownSeconds(CombatSkillId skillId)
        {
            const float invTickRate = 1f / CharacterCombatConfigSO.DefaultTickRate;
            if (IsSpawnedOnRunner)
            {
                return skillId switch
                {
                    CombatSkillId.VerticalSlash => VerticalSlashCooldown.RemainingTime(Runner) ?? 0f,
                    CombatSkillId.HorizontalSweep => HorizontalSweepCooldown.RemainingTime(Runner) ?? 0f,
                    CombatSkillId.RangedFire => RangedFireCooldown.RemainingTime(Runner) ?? 0f,
                    _ => 0f
                };
            }

            return skillId switch
            {
                CombatSkillId.VerticalSlash => _localVerticalCdTicks * invTickRate,
                CombatSkillId.HorizontalSweep => _localSweepCdTicks * invTickRate,
                CombatSkillId.RangedFire => _localRangedCdTicks * invTickRate,
                _ => 0f
            };
        }

        private void TryExecuteSkillInputs(in NetworkInputData input, ICharacterCombatConfig cfg)
        {
            if (IsMobilitySuppressingCombat())
            {
                return;
            }

            int tick = IsSpawnedOnRunner ? Runner.Tick.Raw : 0;

            if (input.Buttons.IsSet((int)NetworkInputButtons.VerticalSlash) && IsVerticalSlashReady())
            {
                StartVerticalSlashCooldown(cfg);
                ExecuteMeleeSectorAttack(MeleeSwingKind.VerticalSlash, transform.position, GetPlanarForward(), cfg, tick);
                return;
            }

            bool sweepLeft = input.Buttons.IsSet((int)NetworkInputButtons.HorizontalSweepLeft);
            bool sweepRight = input.Buttons.IsSet((int)NetworkInputButtons.HorizontalSweepRight);
            if ((sweepLeft || sweepRight) && IsHorizontalSweepReady())
            {
                BeginHorizontalSweep(sweepLeft, cfg, tick);
                return;
            }

            if (input.Buttons.IsSet((int)NetworkInputButtons.RangedFire) && IsRangedFireReady())
            {
                ExecuteRangedFire(in input, cfg);
            }
        }

        private bool IsMobilitySuppressingCombat()
        {
            if (_mobilityController == null)
            {
                return false;
            }

            MobilityState mobState = _mobilityController.CurrentMobilityState;
            return _mobilityController.IsGhostMode
                   || mobState == MobilityState.DodgingLeft
                   || mobState == MobilityState.DodgingRight;
        }

        private void BeginHorizontalSweep(bool isLeftSweep, ICharacterCombatConfig cfg, int attackTick)
        {
            StartHorizontalSweepCooldown(cfg);
            MeleeSwingKind swingKind = isLeftSweep ? MeleeSwingKind.HorizontalSweepLeft : MeleeSwingKind.HorizontalSweepRight;
            Vector3 initialForward = GetPlanarForward();

            ExecuteMeleeSectorAttack(swingKind, transform.position, initialForward, cfg, attackTick);
            OnHorizontalSweepIndicatorTriggered?.Invoke(isLeftSweep);

            float signedDelta = isLeftSweep ? -cfg.HorizontalSweepTurnDeg : cfg.HorizontalSweepTurnDeg;
            int totalTicks = Mathf.Max(1, cfg.HorizontalTurnTicks);
            float startYaw = NormalizeSignedYaw(transform.eulerAngles.y);

            _localIsSweepingTurn = true;
            _localSweepElapsedTicks = 0;
            _localSweepStartYaw = startYaw;
            _localSweepSignedDelta = signedDelta;
            _currentAuthoritativeYaw = startYaw;
            _mobilityYawOverrideActive = true;
            _mobilityController?.SetSweepYawOverride(true, startYaw);
            _localInputPoller?.SetHorizontalYawLock(true, startYaw);

            if (IsSpawnedOnRunner)
            {
                SweepStartYaw = startYaw;
                SweepSignedDeltaYaw = signedDelta;
                NetworkedSweepElapsedTicks = 0;
                ActiveSweepTurnTimer = TickTimer.CreateFromTicks(Runner, totalTicks);
            }
        }

        private void StepActiveHorizontalSweepTurn(in NetworkInputData input, ICharacterCombatConfig cfg)
        {
            bool active = IsSpawnedOnRunner ? !ActiveSweepTurnTimer.ExpiredOrNotRunning(Runner) : _localIsSweepingTurn;
            if (!active)
            {
                if (_mobilityYawOverrideActive)
                {
                    ReleaseYawLocks();
                }

                _currentAuthoritativeYaw = NormalizeSignedYaw(input.LookYaw);
                return;
            }

            int totalTicks = Mathf.Max(1, cfg.HorizontalTurnTicks);
            AdvanceSweepTurnOneTick(totalTicks);
        }

        private void AdvanceSweepTurnOneTick(int totalTicks)
        {
            int elapsed = (IsSpawnedOnRunner ? NetworkedSweepElapsedTicks : _localSweepElapsedTicks) + 1;
            _localSweepElapsedTicks = elapsed;
            if (IsSpawnedOnRunner)
            {
                NetworkedSweepElapsedTicks = elapsed;
            }

            float startYaw = IsSpawnedOnRunner ? SweepStartYaw : _localSweepStartYaw;
            float signedDelta = IsSpawnedOnRunner ? SweepSignedDeltaYaw : _localSweepSignedDelta;
            float nextYaw = NormalizeSignedYaw(MeleeSectorMath.EvaluateSweepTurnYaw(startYaw, signedDelta, elapsed, totalTicks));

            _currentAuthoritativeYaw = nextYaw;
            transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            _mobilityYawOverrideActive = true;
            _mobilityController?.SetSweepYawOverride(true, nextYaw);

            bool completed = elapsed >= totalTicks;
            _localInputPoller?.SetHorizontalYawLock(!completed, nextYaw);

            if (completed)
            {
                _localIsSweepingTurn = false;
                if (IsSpawnedOnRunner)
                {
                    ActiveSweepTurnTimer = TickTimer.None;
                }
            }
        }

        private void CancelActiveSweepTurn()
        {
            bool active = IsSpawnedOnRunner ? !ActiveSweepTurnTimer.ExpiredOrNotRunning(Runner) : _localIsSweepingTurn;
            if (!active && !_mobilityYawOverrideActive)
            {
                return;
            }

            _localIsSweepingTurn = false;
            _localSweepElapsedTicks = 0;
            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                NetworkedSweepElapsedTicks = 0;
                ActiveSweepTurnTimer = TickTimer.None;
            }

            ReleaseYawLocks();
        }

        private void ReleaseYawLocks()
        {
            _mobilityYawOverrideActive = false;
            _mobilityController?.SetSweepYawOverride(false, _currentAuthoritativeYaw);
            if (_localInputPoller != null && _localInputPoller.IsHorizontalYawLocked)
            {
                _localInputPoller.SetHorizontalYawLock(false, _currentAuthoritativeYaw);
            }
        }

        private void ExecuteRangedFire(in NetworkInputData input, ICharacterCombatConfig cfg)
        {
            StartRangedFireCooldown(cfg);
            Vector3 aimDirection = ComputeAimRayDirection(_currentAuthoritativeYaw, input.LookPitch);
            Vector3 muzzlePos = ResolveMuzzleWorldPosition(aimDirection);
            _lastSpawnedAimDirection = aimDirection;

            if (MeleeSectorMath.IsMuzzleBlockedByWall(transform.position, muzzlePos, out RaycastHit flushWallHit))
            {
                Vector3 chestPos = transform.position + Vector3.up * MeleeSectorMath.MuzzleHeightOffsetMeters;
                OnAnyRangedShotExecuted?.Invoke(chestPos, flushWallHit.point, true);
                NetworkProjectile.RaiseWallOrPlayerImpact(flushWallHit.point, hitLivingPlayer: false);
                return;
            }

            OnAnyRangedShotExecuted?.Invoke(muzzlePos, aimDirection, false);

            if (IsSpawnedOnRunner && _projectilePrefab != null)
            {
                _pendingSpawnMuzzle = muzzlePos;
                _pendingSpawnAimDir = aimDirection;
                _pendingSpawnConfig = cfg;
                Quaternion rot = Quaternion.LookRotation(aimDirection, Vector3.up);
                Runner.Spawn(_projectilePrefab, muzzlePos, rot, Object.InputAuthority, _cachedProjectileBeforeSpawned);
                return;
            }

            _standaloneSpawnedProjectileCount++;
        }

        private void HandleBeforeProjectileSpawned(NetworkRunner runner, NetworkObject spawnedObj)
        {
            if (spawnedObj != null && spawnedObj.TryGetComponent(out NetworkProjectile proj))
            {
                proj.InitializeLaunch(_pendingSpawnMuzzle, _pendingSpawnAimDir, _pendingSpawnConfig);
            }
        }

        private Vector3 ResolveMuzzleWorldPosition(Vector3 aimDirection)
        {
            if (_muzzleTransform != null)
            {
                return _muzzleTransform.position;
            }

            Vector3 flatForward = new Vector3(aimDirection.x, 0f, aimDirection.z);
            flatForward = flatForward.sqrMagnitude < 0.0001f ? GetPlanarForward() : flatForward.normalized;

            return transform.position
                   + Vector3.up * MeleeSectorMath.MuzzleHeightOffsetMeters
                   + flatForward * MeleeSectorMath.MuzzleForwardOffsetMeters;
        }

        /// <summary>Computes a normalized 3D world aim vector incorporating both yaw and clamped vertical pitch.</summary>
        public static Vector3 ComputeAimRayDirection(float yawDeg, float pitchDeg)
        {
            float clampedPitch = Mathf.Clamp(pitchDeg, LocalInputPoller.MinPitchDeg, LocalInputPoller.MaxPitchDeg);
            return (Quaternion.Euler(clampedPitch, yawDeg, 0f) * Vector3.forward).normalized;
        }

        private static float NormalizeSignedYaw(float yawDeg)
        {
            if (float.IsNaN(yawDeg) || float.IsInfinity(yawDeg))
            {
                return 0f;
            }

            return Mathf.Repeat(yawDeg + 180f, 360f) - 180f;
        }

        private bool ShouldSkipMeleeCandidate(
            IDamageReceiver victim,
            Vector3 origin,
            Vector3 forward,
            float range,
            float cosHalf,
            float yawOffset)
        {
            if (victim == null || victim.IsDead || victim.ReceiverTransform == null)
            {
                return true;
            }

            if (ReferenceEquals(victim.ReceiverTransform, transform) ||
                (EffectiveOwner != PlayerRef.None && victim.OwnerPlayer == EffectiveOwner))
            {
                return true;
            }

            EntityId receiverEntityId = victim.ReceiverTransform.gameObject.GetEntityId();
            if (!_hitReceiversThisSwing.Add(receiverEntityId))
            {
                return true;
            }

            Vector3 targetPos = victim.ReceiverTransform.position;
            return !MeleeSectorMath.IsTargetInsideSector(origin, forward, targetPos, range, cosHalf, yawOffset)
                   || !MeleeSectorMath.HasClearLineOfSight(origin, targetPos);
        }

        private static void ResolveMeleeParameters(
            MeleeSwingKind kind,
            ICharacterCombatConfig cfg,
            out int damage,
            out float range,
            out float cosHalfAngle,
            out float yawOffsetDeg,
            out AttackType attackType)
        {
            if (kind == MeleeSwingKind.VerticalSlash)
            {
                damage = cfg.VerticalSlashDamage;
                range = cfg.VerticalSlashRange;
                cosHalfAngle = cfg.VerticalSlashCosHalfAngle;
                yawOffsetDeg = 0f;
                attackType = AttackType.VerticalSlash;
                return;
            }

            damage = cfg.HorizontalSweepDamage;
            range = cfg.HorizontalSweepRange;
            cosHalfAngle = cfg.HorizontalSweepCosHalfAngle;
            yawOffsetDeg = kind == MeleeSwingKind.HorizontalSweepLeft ? -45.0f : 45.0f;
            attackType = AttackType.HorizontalSweep;
        }

        private void EmitMeleeSwingPresentation(Vector3 origin, Vector3 forward, MeleeSwingKind kind)
        {
            Quaternion rot = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : transform.rotation;
            OnAnyMeleeSwingExecuted?.Invoke(new MeleeSwingEvent(origin, rot, kind, EffectiveOwner));

            if (IsSpawnedOnRunner)
            {
                Rpc_BroadcastMeleeSwingVisual(origin, rot, (byte)kind);
            }
        }

        /// <summary>Broadcasts melee slash arc VFX/SFX cues to remote non-authority peers.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        public void Rpc_BroadcastMeleeSwingVisual(Vector3 origin, Quaternion rotation, byte kindByte)
        {
            OnAnyMeleeSwingExecuted?.Invoke(new MeleeSwingEvent(origin, rotation, (MeleeSwingKind)kindByte, EffectiveOwner));
        }

        private Vector3 GetPlanarForward()
        {
            Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
            return fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
        }

        private bool IsVerticalSlashReady() =>
            IsSpawnedOnRunner ? VerticalSlashCooldown.ExpiredOrNotRunning(Runner) : _localVerticalCdTicks <= 0;

        private bool IsHorizontalSweepReady() =>
            IsSpawnedOnRunner ? HorizontalSweepCooldown.ExpiredOrNotRunning(Runner) : _localSweepCdTicks <= 0;

        private bool IsRangedFireReady() =>
            IsSpawnedOnRunner ? RangedFireCooldown.ExpiredOrNotRunning(Runner) : _localRangedCdTicks <= 0;

        private void StartVerticalSlashCooldown(ICharacterCombatConfig cfg)
        {
            int ticks = Mathf.Max(1, Mathf.RoundToInt(cfg.VerticalSlashCooldownSec * CharacterCombatConfigSO.DefaultTickRate));
            _localVerticalCdTicks = ticks;
            if (IsSpawnedOnRunner) VerticalSlashCooldown = TickTimer.CreateFromTicks(Runner, ticks);
            OnCooldownUpdated?.Invoke(CombatSkillId.VerticalSlash, cfg.VerticalSlashCooldownSec, cfg.VerticalSlashCooldownSec);
        }

        private void StartHorizontalSweepCooldown(ICharacterCombatConfig cfg)
        {
            int ticks = Mathf.Max(1, Mathf.RoundToInt(cfg.HorizontalSweepCooldownSec * CharacterCombatConfigSO.DefaultTickRate));
            _localSweepCdTicks = ticks;
            if (IsSpawnedOnRunner) HorizontalSweepCooldown = TickTimer.CreateFromTicks(Runner, ticks);
            OnCooldownUpdated?.Invoke(CombatSkillId.HorizontalSweep, cfg.HorizontalSweepCooldownSec, cfg.HorizontalSweepCooldownSec);
        }

        private void StartRangedFireCooldown(ICharacterCombatConfig cfg)
        {
            int ticks = Mathf.Max(1, Mathf.RoundToInt(cfg.RangedFireCooldownSec * CharacterCombatConfigSO.DefaultTickRate));
            _localRangedCdTicks = ticks;
            if (IsSpawnedOnRunner) RangedFireCooldown = TickTimer.CreateFromTicks(Runner, ticks);
            OnCooldownUpdated?.Invoke(CombatSkillId.RangedFire, cfg.RangedFireCooldownSec, cfg.RangedFireCooldownSec);
        }

        private void DecrementLocalCooldownTicks()
        {
            if (_localVerticalCdTicks > 0) _localVerticalCdTicks--;
            if (_localSweepCdTicks > 0) _localSweepCdTicks--;
            if (_localRangedCdTicks > 0) _localRangedCdTicks--;
        }

        private void HandleSelfPlayerDied(PlayerRef victim, PlayerRef killer)
        {
            CancelActiveSweepTurn();
        }
    }
}
