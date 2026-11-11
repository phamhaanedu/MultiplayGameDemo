using System;
using CapsuleArena.Arena;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Player
{
    /// <summary>
    /// State-authoritative 60Hz character mobility controller handling WASD walk (6.0 m/s),
    /// single jump + Coyote Time + Jump Buffer + gravity (-19.62 m/s^2), Forward Dash (Key 2: 18 m/s,
    /// 12 ticks, CapsuleCastNonAlloc wall-slide), Lateral Dodge Roll (Keys Q/E: 12 m/s, 15 ticks),
    /// and interpolated VisualRoot pitch/roll tilt without rotating CapsuleCollider or CameraPivot
    /// (Stories 5-1, 5-2, 5-3; ADR-0004; TR-move-001..003).
    /// </summary>
    public sealed class PlayerMobilityController : NetworkBehaviour, IPlayerMobilityController
    {
        /// <summary>Y-threshold below which an out-of-bounds capsule is teleported back onto an Arena spawn point.</summary>
        public const float OutOfBoundsMinY = -5.0f;

        /// <summary>Forward X-axis pitch lean angle in degrees applied to VisualRoot during Forward Dash.</summary>
        public const float DashVisualPitchDeg = 15.0f;

        /// <summary>Coyote-time grace window in seconds after stepping off a ledge (6 ticks @ 60Hz).</summary>
        public const float CoyoteTimeSeconds = 0.10f;

        /// <summary>Jump-buffer grace window in seconds prior to touching ground (6 ticks @ 60Hz).</summary>
        public const float JumpBufferSeconds = 0.10f;

        private const int GraceWindowTicks = 6;
        private const float GroundedStickVelocityY = -0.5f;
        private const float CapsuleCastRadius = 0.48f;
        private const float WallProbeSkinPadding = 0.15f;
        private static readonly RaycastHit[] SharedWallHitBuffer = new RaycastHit[4];

        [Header("Configuration & Hierarchy")]
        [Tooltip("Immutable combat and mobility ScriptableObject asset.")]
        [SerializeField] private CharacterCombatConfigSO _combatConfig;

        [Tooltip("Unity CharacterController driving collision-resolved capsule motion.")]
        [SerializeField] private CharacterController _characterController;

        [Tooltip("Child mesh root rotated/scaled during Dash/Dodge while root collider stays upright.")]
        [SerializeField] private Transform _visualRoot;

        [Tooltip("Camera boom/eye pivot kept at 0-degree Z-roll at all times.")]
        [SerializeField] private Transform _cameraPivot;

        [Tooltip("Optional ArenaSpawnManager reference for Y < -5.0m out-of-bounds recovery.")]
        [SerializeField] private ArenaSpawnManager _spawnManager;

        /// <summary>Gets the replicated locomotion state (Normal, Dashing, DodgingLeft, DodgingRight).</summary>
        [Networked] public MobilityState NetworkedState { get; private set; }

        /// <summary>Gets the replicated active Dash/Dodge duration TickTimer.</summary>
        [Networked] public TickTimer ActiveMobilityTimer { get; private set; }

        /// <summary>Gets the replicated total ticks of the current burst skill for visual interpolation.</summary>
        [Networked] public int TotalMobilityTicks { get; private set; }

        /// <summary>Gets the replicated Forward Dash (Key 2) cooldown TickTimer.</summary>
        [Networked] public TickTimer DashCooldownTimer { get; private set; }

        /// <summary>Gets the replicated shared Lateral Dodge (Keys Q/E) cooldown TickTimer.</summary>
        [Networked] public TickTimer DodgeCooldownTimer { get; private set; }

        /// <summary>Gets the locked world-space horizontal direction vector during Dash or Dodge.</summary>
        [Networked] public Vector3 LockedBurstDirection { get; private set; }

        /// <summary>Gets the replicated vertical velocity in meters per second.</summary>
        [Networked] public float VerticalVelocityY { get; private set; }

        /// <summary>Gets a value indicating whether combat bursts are locked by Ghost Spectator mode.</summary>
        [Networked] public NetworkBool IsGhostNetworked { get; private set; }

        private readonly Vector3[] _livingPositionsBuffer = new Vector3[4];
        private NetworkTransform _networkTransform;
        private IDamageReceiver _selfDamageReceiver;
        private MobilityState _localState;
        private int _localActiveTicks;
        private int _localTotalTicks;
        private int _localDashCooldownTicks;
        private int _localDodgeCooldownTicks;
        private int _coyoteTicksRemaining;
        private int _jumpBufferTicksRemaining;
        private Vector3 _localLockedDirection;
        private float _localVerticalVelocity;
        private float _lastJumpLaunchVelocity;
        private bool _localIsGhost;
        private bool _hasSweepYawOverride;
        private float _sweepYawOverrideDeg;
        private MobilityState _lastPresentedBurstState;

        /// <inheritdoc />
        public event Action<CombatSkillId, float, float> OnCooldownUpdated;

        /// <summary>Raised locally when Forward Dash or Lateral Dodge Roll begins: (state, worldOrigin, worldRotation).</summary>
        public event Action<MobilityState, Vector3, Quaternion> OnMobilityBurstStarted;

        /// <summary>Global observer event emitted when any player capsule begins a Dash or Dodge burst.</summary>
        public static event Action<PlayerMobilityController, MobilityState, Vector3, Quaternion> OnAnyMobilityBurstStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticMobilityEvents()
        {
            OnAnyMobilityBurstStarted = null;
        }

        /// <inheritdoc />
        public MobilityState CurrentMobilityState => IsSpawnedOnRunner ? NetworkedState : _localState;

        /// <inheritdoc />
        public bool IsGhostMode => IsSpawnedOnRunner ? (bool)IsGhostNetworked : _localIsGhost;

        /// <summary>Gets remaining Forward Dash cooldown ticks.</summary>
        public int RemainingDashCooldownTicks =>
            IsSpawnedOnRunner ? (DashCooldownTimer.RemainingTicks(Runner) ?? 0) : _localDashCooldownTicks;

        /// <summary>Gets remaining Dodge Roll cooldown ticks.</summary>
        public int RemainingDodgeCooldownTicks =>
            IsSpawnedOnRunner ? (DodgeCooldownTimer.RemainingTicks(Runner) ?? 0) : _localDodgeCooldownTicks;

        /// <summary>Gets the exact upward launch velocity applied on the most recent jump impulse.</summary>
        public float LastJumpLaunchVelocity => _lastJumpLaunchVelocity;

        /// <summary>Gets the current local vertical velocity in m/s.</summary>
        public float CurrentVerticalVelocity => IsSpawnedOnRunner ? VerticalVelocityY : _localVerticalVelocity;

        private bool IsSpawnedOnRunner => Object != null && Runner != null;

        private void Awake()
        {
            if (_characterController == null)
            {
                TryGetComponent(out _characterController);
            }

            TryGetComponent(out _networkTransform);
            TryGetComponent(out _selfDamageReceiver);
        }

        private void Start()
        {
            ValidateConfigOrDisable();
        }

        /// <summary>
        /// Validates that <see cref="CharacterCombatConfigSO"/> is assigned, logging an error and disabling if null (Story 1-1 AC-3).
        /// </summary>
        public bool ValidateConfigOrDisable()
        {
            return CharacterCombatConfigSO.EnsureValidConfig(_combatConfig, this);
        }

        /// <summary>Assigns hierarchy and config references for runtime prefabs and unit tests.</summary>
        public void ConfigureReferences(
            CharacterCombatConfigSO config,
            CharacterController controller,
            Transform visualRoot,
            Transform cameraPivot,
            ArenaSpawnManager spawnManager = null)
        {
            _combatConfig = config;
            _characterController = controller;
            _visualRoot = visualRoot;
            _cameraPivot = cameraPivot;
            _spawnManager = spawnManager;
        }

        /// <summary>
        /// Sets or clears an authoritative character yaw override during the 11-tick Horizontal Sweep turn (Story 7-2).
        /// </summary>
        public void SetSweepYawOverride(bool active, float yawDegrees)
        {
            _hasSweepYawOverride = active;
            _sweepYawOverrideDeg = yawDegrees;
        }

        /// <summary>
        /// Clears Ghost mode and teleports the capsule onto a Max-Min safe Arena spawn point for a new match round.
        /// </summary>
        public void RespawnForRematch()
        {
            SetGhostSpectatorMode(false);
            TeleportToSafeSpawn();
        }

        /// <inheritdoc />
        public void SetGhostSpectatorMode(bool isGhost)
        {
            _localIsGhost = isGhost;
            _localState = MobilityState.Normal;
            _localActiveTicks = 0;
            _jumpBufferTicksRemaining = 0;
            _coyoteTicksRemaining = 0;

            if (IsSpawnedOnRunner && Object.HasStateAuthority)
            {
                IsGhostNetworked = isGhost;
                NetworkedState = MobilityState.Normal;
                ActiveMobilityTimer = TickTimer.None;
            }

            ApplyGhostVisualReset();
        }

        /// <summary>
        /// Resets local presentation state (VisualRoot rotation/scale) safely from <c>Render()</c>
        /// without writing any <c>[Networked]</c> simulation properties.
        /// </summary>
        public void ApplyGhostVisualReset()
        {
            if (_visualRoot != null)
            {
                _visualRoot.localRotation = Quaternion.identity;
                _visualRoot.localScale = Vector3.one;
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
                input.LookYaw = transform.eulerAngles.y;
            }

            SyncNetworkStateToLocals();
            SimulateAuthoritativeTick(in input, Runner.DeltaTime, _combatConfig);
            SyncLocalsToNetworkState();
        }

        /// <inheritdoc />
        public override void Render()
        {
            MobilityState stateNow = CurrentMobilityState;
            if (stateNow != _lastPresentedBurstState)
            {
                if (stateNow != MobilityState.Normal && (!IsSpawnedOnRunner || !Object.HasStateAuthority))
                {
                    EmitMobilityBurstPresentation(stateNow);
                }

                _lastPresentedBurstState = stateNow;
            }

            int remTicks = IsSpawnedOnRunner ? (ActiveMobilityTimer.RemainingTicks(Runner) ?? 0) : _localActiveTicks;
            int totTicks = IsSpawnedOnRunner ? TotalMobilityTicks : _localTotalTicks;
            float subTickAlpha = IsSpawnedOnRunner ? Runner.LocalAlpha : 0f;
            UpdateVisualTiltAndCameraPivot(stateNow, remTicks, totTicks, subTickAlpha);
        }

        /// <summary>
        /// Executes a single deterministic 60Hz locomotion tick on local/test or authoritative state.
        /// </summary>
        public void SimulateAuthoritativeTick(
            in NetworkInputData input,
            float deltaTime,
            ICharacterCombatConfig configOverride = null,
            bool? groundedOverride = null)
        {
            ICharacterCombatConfig cfg = configOverride != null ? configOverride : _combatConfig;
            if (cfg == null)
            {
                return;
            }

            float effectiveYaw = _hasSweepYawOverride ? _sweepYawOverrideDeg : input.LookYaw;
            ApplyFlatYawAndPitch(effectiveYaw, input.LookPitch);
            DecrementCooldownCounters();

            bool isGrounded = groundedOverride ?? (_characterController != null && _characterController.isGrounded);
            TryBeginBurstSkill(in input, effectiveYaw, cfg);

            Vector3 horizontalVel = ComputeHorizontalVelocity(in input, effectiveYaw, cfg);
            horizontalVel = ProbeAndProjectWallSlide(horizontalVel, deltaTime);

            bool jumpPressed = !_localIsGhost && input.Buttons.IsSet((int)NetworkInputButtons.Jump);
            float frameMoveVelY = StepVerticalKinematics(isGrounded, jumpPressed, cfg, deltaTime);

            MoveControllerAndRecoverBounds(new Vector3(horizontalVel.x, frameMoveVelY, horizontalVel.z), deltaTime);
            AdvanceActiveBurstTimer();
            UpdateVisualTiltAndCameraPivot(_localState, _localActiveTicks, _localTotalTicks, 0f);
        }

        /// <summary>
        /// Pure mathematical projection removing into-wall normal velocity while preserving tangential wall slide.
        /// </summary>
        public static Vector3 ComputeWallSlideVelocity(Vector3 desiredHorizontalVelocity, Vector3 wallNormal)
        {
            Vector3 flatNormal = new Vector3(wallNormal.x, 0f, wallNormal.z);
            if (flatNormal.sqrMagnitude < 0.0001f)
            {
                return desiredHorizontalVelocity;
            }

            flatNormal.Normalize();
            if (Vector3.Dot(desiredHorizontalVelocity, flatNormal) >= 0f)
            {
                return desiredHorizontalVelocity;
            }

            return Vector3.ProjectOnPlane(desiredHorizontalVelocity, flatNormal);
        }

        /// <summary>
        /// Pure calculation of VisualRoot local rotation (+15 deg X pitch on Dash, +-360 deg Z roll on Dodge)
        /// with optional sub-tick <paramref name="subTickAlpha"/> smoothing for high-refresh displays.
        /// </summary>
        public static Quaternion ComputeVisualRootLocalRotation(
            MobilityState state,
            int remainingTicks,
            int totalTicks,
            float subTickAlpha = 0f)
        {
            if (state == MobilityState.Normal || remainingTicks <= 0 || totalTicks <= 0)
            {
                return Quaternion.identity;
            }

            if (state == MobilityState.Dashing)
            {
                return Quaternion.Euler(DashVisualPitchDeg, 0f, 0f);
            }

            float continuousRemaining = Mathf.Max(0f, remainingTicks - Mathf.Clamp01(subTickAlpha));
            float progress = Mathf.Clamp01(1.0f - (continuousRemaining / totalTicks));
            float rollDegrees = state == MobilityState.DodgingLeft ? 360.0f * progress : -360.0f * progress;
            return Quaternion.Euler(0f, 0f, rollDegrees);
        }

        private void TryBeginBurstSkill(in NetworkInputData input, float effectiveYaw, ICharacterCombatConfig cfg)
        {
            if (_localIsGhost || _localState != MobilityState.Normal)
            {
                return;
            }

            if (input.Buttons.IsSet((int)NetworkInputButtons.ForwardDash) && IsDashReady())
            {
                StartForwardDash(effectiveYaw, cfg);
                return;
            }

            bool dodgeLeft = input.Buttons.IsSet((int)NetworkInputButtons.DodgeLeft);
            bool dodgeRight = input.Buttons.IsSet((int)NetworkInputButtons.DodgeRight);
            if ((dodgeLeft || dodgeRight) && IsDodgeReady())
            {
                StartDodgeRoll(effectiveYaw, goLeft: dodgeLeft, cfg);
            }
        }

        private void StartForwardDash(float lookYaw, ICharacterCombatConfig cfg)
        {
            Vector3 forward = Quaternion.Euler(0f, lookYaw, 0f) * Vector3.forward;
            _localLockedDirection = new Vector3(forward.x, 0f, forward.z).normalized;
            _localState = MobilityState.Dashing;
            _localActiveTicks = cfg.DashTicks;
            _localTotalTicks = cfg.DashTicks;
            _localDashCooldownTicks = cfg.DashCooldownTicks;

            if (IsSpawnedOnRunner)
            {
                ActiveMobilityTimer = TickTimer.CreateFromTicks(Runner, cfg.DashTicks);
                DashCooldownTimer = TickTimer.CreateFromTicks(Runner, cfg.DashCooldownTicks);
            }

            OnCooldownUpdated?.Invoke(CombatSkillId.ForwardDash, cfg.DashCooldownSec, cfg.DashCooldownSec);
            _lastPresentedBurstState = MobilityState.Dashing;
            EmitMobilityBurstPresentation(MobilityState.Dashing);
        }

        private void StartDodgeRoll(float lookYaw, bool goLeft, ICharacterCombatConfig cfg)
        {
            Vector3 localAxis = goLeft ? Vector3.left : Vector3.right;
            Vector3 worldStrafe = Quaternion.Euler(0f, lookYaw, 0f) * localAxis;
            _localLockedDirection = new Vector3(worldStrafe.x, 0f, worldStrafe.z).normalized;
            _localState = goLeft ? MobilityState.DodgingLeft : MobilityState.DodgingRight;
            _localActiveTicks = cfg.DodgeTicks;
            _localTotalTicks = cfg.DodgeTicks;
            _localDodgeCooldownTicks = cfg.DodgeCooldownTicks;

            if (IsSpawnedOnRunner)
            {
                ActiveMobilityTimer = TickTimer.CreateFromTicks(Runner, cfg.DodgeTicks);
                DodgeCooldownTimer = TickTimer.CreateFromTicks(Runner, cfg.DodgeCooldownTicks);
            }

            OnCooldownUpdated?.Invoke(CombatSkillId.DodgeRoll, cfg.DodgeCooldownSec, cfg.DodgeCooldownSec);
            _lastPresentedBurstState = _localState;
            EmitMobilityBurstPresentation(_localState);
        }

        private void EmitMobilityBurstPresentation(MobilityState burstState)
        {
            Quaternion rot = transform.rotation;
            Vector3 pos = transform.position;
            OnMobilityBurstStarted?.Invoke(burstState, pos, rot);
            OnAnyMobilityBurstStarted?.Invoke(this, burstState, pos, rot);
        }

        private Vector3 ComputeHorizontalVelocity(in NetworkInputData input, float effectiveYaw, ICharacterCombatConfig cfg)
        {
            if (_localState == MobilityState.Dashing)
            {
                return _localLockedDirection * cfg.DashSpeed;
            }

            if (_localState == MobilityState.DodgingLeft || _localState == MobilityState.DodgingRight)
            {
                return _localLockedDirection * cfg.DodgeSpeed;
            }

            Vector2 move = Vector2.ClampMagnitude(input.MoveDirection, 1.0f);
            Vector3 localMove = new Vector3(move.x, 0f, move.y);
            return (Quaternion.Euler(0f, effectiveYaw, 0f) * localMove) * cfg.MoveSpeed;
        }

        private Vector3 ProbeAndProjectWallSlide(Vector3 desiredHorizontal, float deltaTime)
        {
            if (_localIsGhost || desiredHorizontal.sqrMagnitude < 0.0001f)
            {
                return _localIsGhost ? desiredHorizontal : Vector3.zero;
            }

            Vector3 pBottom = transform.position + Vector3.up * 0.55f;
            Vector3 pTop = transform.position + Vector3.up * 1.45f;
            float castDistance = desiredHorizontal.magnitude * deltaTime + WallProbeSkinPadding;

            int hitCount = Physics.CapsuleCastNonAlloc(
                pBottom,
                pTop,
                CapsuleCastRadius,
                desiredHorizontal.normalized,
                SharedWallHitBuffer,
                castDistance,
                ArenaLayers.EnvironmentWallMask,
                QueryTriggerInteraction.Ignore);

            int closestIdx = FindClosestNonZeroHitIndex(hitCount);
            return closestIdx >= 0
                ? ComputeWallSlideVelocity(desiredHorizontal, SharedWallHitBuffer[closestIdx].normal)
                : desiredHorizontal;
        }

        private static int FindClosestNonZeroHitIndex(int hitCount)
        {
            int bestIdx = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                if (SharedWallHitBuffer[i].collider != null && SharedWallHitBuffer[i].distance < bestDist)
                {
                    bestDist = SharedWallHitBuffer[i].distance;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private float StepVerticalKinematics(
            bool isGrounded,
            bool jumpPressed,
            ICharacterCombatConfig cfg,
            float deltaTime)
        {
            _coyoteTicksRemaining = isGrounded ? GraceWindowTicks : Mathf.Max(0, _coyoteTicksRemaining - 1);
            _jumpBufferTicksRemaining = jumpPressed ? GraceWindowTicks : Mathf.Max(0, _jumpBufferTicksRemaining - 1);

            if (_coyoteTicksRemaining > 0 && _jumpBufferTicksRemaining > 0 && _localVerticalVelocity <= 0.01f)
            {
                _coyoteTicksRemaining = 0;
                _jumpBufferTicksRemaining = 0;
                _lastJumpLaunchVelocity = cfg.JumpInitialVelocity;
                float nextJumpVy = cfg.JumpInitialVelocity + cfg.Gravity * deltaTime;
                float avgJumpVy = 0.5f * (cfg.JumpInitialVelocity + nextJumpVy);
                _localVerticalVelocity = nextJumpVy;
                return avgJumpVy;
            }

            if (isGrounded && _localVerticalVelocity < GroundedStickVelocityY)
            {
                _localVerticalVelocity = GroundedStickVelocityY;
                return _localVerticalVelocity;
            }

            float prevVy = _localVerticalVelocity;
            _localVerticalVelocity = prevVy + cfg.Gravity * deltaTime;
            return 0.5f * (prevVy + _localVerticalVelocity);
        }

        private void MoveControllerAndRecoverBounds(Vector3 worldVelocity, float deltaTime)
        {
            if (_characterController != null && _characterController.enabled)
            {
                _characterController.Move(worldVelocity * deltaTime);
            }
            else
            {
                transform.position += worldVelocity * deltaTime;
            }

            if (transform.position.y < OutOfBoundsMinY)
            {
                TeleportToSafeSpawn();
            }
        }

        private void TeleportToSafeSpawn()
        {
            Pose safePose = ResolveSafeRecoveryPose();
            if (_networkTransform != null && IsSpawnedOnRunner)
            {
                _networkTransform.Teleport(safePose.position, safePose.rotation);
            }
            else if (_characterController != null)
            {
                _characterController.enabled = false;
                transform.SetPositionAndRotation(safePose.position, safePose.rotation);
                _characterController.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(safePose.position, safePose.rotation);
            }

            _localVerticalVelocity = 0f;
            _localState = MobilityState.Normal;
            _localActiveTicks = 0;
        }

        private Pose ResolveSafeRecoveryPose()
        {
            if (_spawnManager == null)
            {
                return new Pose(
                    new Vector3(16f, ArenaSpawnManager.CanonicalSpawnElevationY, 0f),
                    Quaternion.Euler(0f, -90f, 0f));
            }

            int livingCount = DamageReceiverRegistry.CopyLivingPositionsNonAlloc(_livingPositionsBuffer, _selfDamageReceiver);
            ReadOnlySpan<Vector3> span = new ReadOnlySpan<Vector3>(_livingPositionsBuffer, 0, livingCount);
            return _spawnManager.GetSpawnPose(0, span);
        }

        private bool IsDashReady()
        {
            return IsSpawnedOnRunner ? DashCooldownTimer.ExpiredOrNotRunning(Runner) : _localDashCooldownTicks <= 0;
        }

        private bool IsDodgeReady()
        {
            return IsSpawnedOnRunner ? DodgeCooldownTimer.ExpiredOrNotRunning(Runner) : _localDodgeCooldownTicks <= 0;
        }

        private void DecrementCooldownCounters()
        {
            if (_localDashCooldownTicks > 0) _localDashCooldownTicks--;
            if (_localDodgeCooldownTicks > 0) _localDodgeCooldownTicks--;
        }

        private void AdvanceActiveBurstTimer()
        {
            if (_localState == MobilityState.Normal)
            {
                return;
            }

            _localActiveTicks--;
            bool expired = IsSpawnedOnRunner ? ActiveMobilityTimer.ExpiredOrNotRunning(Runner) : _localActiveTicks <= 0;
            if (expired || _localActiveTicks <= 0)
            {
                _localActiveTicks = 0;
                _localState = MobilityState.Normal;
                if (IsSpawnedOnRunner) ActiveMobilityTimer = TickTimer.None;
            }
        }

        private void ApplyFlatYawAndPitch(float yawDeg, float pitchDeg)
        {
            transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            if (_cameraPivot != null)
            {
                float clampedPitch = Mathf.Clamp(pitchDeg, LocalInputPoller.MinPitchDeg, LocalInputPoller.MaxPitchDeg);
                _cameraPivot.localRotation = Quaternion.Euler(clampedPitch, 0f, 0f);
            }
        }

        private void UpdateVisualTiltAndCameraPivot(MobilityState state, int remainingTicks, int totalTicks, float alpha)
        {
            if (_visualRoot != null)
            {
                _visualRoot.localRotation = ComputeVisualRootLocalRotation(state, remainingTicks, totalTicks, alpha);
                if (!DG.Tweening.DOTween.IsTweening(_visualRoot))
                {
                    _visualRoot.localScale = state == MobilityState.Dashing && _combatConfig != null
                        ? Vector3.Lerp(Vector3.one, _combatConfig.VisualStretchScale, 0.35f)
                        : Vector3.one;
                }
            }

            if (_cameraPivot != null)
            {
                Vector3 euler = _cameraPivot.localEulerAngles;
                _cameraPivot.localRotation = Quaternion.Euler(euler.x, euler.y, 0f);
            }
        }

        private void SyncNetworkStateToLocals()
        {
            _localState = NetworkedState;
            _localActiveTicks = ActiveMobilityTimer.RemainingTicks(Runner) ?? 0;
            _localTotalTicks = TotalMobilityTicks;
            _localDashCooldownTicks = DashCooldownTimer.RemainingTicks(Runner) ?? 0;
            _localDodgeCooldownTicks = DodgeCooldownTimer.RemainingTicks(Runner) ?? 0;
            _localLockedDirection = LockedBurstDirection;
            _localVerticalVelocity = VerticalVelocityY;
            _localIsGhost = IsGhostNetworked;
        }

        private void SyncLocalsToNetworkState()
        {
            NetworkedState = _localState;
            TotalMobilityTicks = _localTotalTicks;
            LockedBurstDirection = _localLockedDirection;
            VerticalVelocityY = _localVerticalVelocity;
            IsGhostNetworked = _localIsGhost;
        }
    }
}
