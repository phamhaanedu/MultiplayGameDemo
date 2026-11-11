using System;
using System.Collections.Generic;
using System.Text;
using CapsuleArena.Combat;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CapsuleArena.CameraSystem
{
    /// <summary>
    /// Post-death camera modes controlled by <see cref="SpectatorController"/> (Story 8-3).
    /// </summary>
    public enum SpectatorMode : byte
    {
        /// <summary>Local player is alive; SpectatorController is dormant.</summary>
        Disabled = 0,

        /// <summary>Free-roaming Ghost camera at 9.0 m/s clamped to [-22m, +22m] X/Z and [0.5m, 8.0m] Y.</summary>
        GhostFreeRoam = 1,

        /// <summary>Orbital TPS camera locked onto a living Layer 8 survivor with SphereCast anti-clip.</summary>
        FollowTarget = 2
    }

    /// <summary>
    /// Activates upon local player elimination (<c>IsDead == true</c>), providing Ghost Free-Roam (<c>9.0 m/s</c>,
    /// passing cleanly through <c>Layer 6</c> walls within <c>[-22m, +22m]</c>) and Follow-Target survivor cycling
    /// (<c>Next/Prev</c> via bound Attack/Ranged controls, <c>Jump</c> return to Free-Roam) with 1-frame automatic
    /// failover if the followed survivor dies or disconnects (Story 8-3; ADR-0005; TR-camera-003).
    /// </summary>
    public sealed class SpectatorController : MonoBehaviour
    {
        /// <summary>Ghost Free-Roam horizontal/vertical flight speed in m/s (1.5x base 6.0 m/s walk speed).</summary>
        public const float GhostMoveSpeed = 9.0f;

        /// <summary>Arena horizontal spectator clamp extent in meters (+-22.0m).</summary>
        public const float MaxHorizontalBounds = 22.0f;

        /// <summary>Minimum Ghost Free-Roam elevation in meters (0.5m).</summary>
        public const float MinGhostElevationY = 0.5f;

        /// <summary>Maximum Ghost Free-Roam elevation in meters (8.0m).</summary>
        public const float MaxGhostElevationY = 8.0f;

        private static readonly string[] FallbackPlayerNames =
        {
            "Player_0", "Player_1", "Player_2", "Player_3", "Player_4", "Player_5", "Player_6", "Player_7", "Player_8"
        };

        [Header("Dependencies")]
        [Tooltip("Local player's PlayerHealth triggering spectator activation on death.")]
        [SerializeField] private PlayerHealth _localPlayerHealth;

        private readonly List<PlayerHealth> _survivorCandidates = new List<PlayerHealth>(4);
        private readonly Dictionary<PlayerRef, string> _displayNames = new Dictionary<PlayerRef, string>(4);
        private readonly StringBuilder _bannerBuilder = new StringBuilder(96);

        private Camera _targetCamera;
        private LocalInputPoller _inputPoller;
        private IInputFocusService _focusService;
        private IKeybindingService _keybindingService;
        private MobileInputBridge _mobileBridge;

        private ButtonControl _moveForwardCtrl;
        private ButtonControl _moveBackwardCtrl;
        private ButtonControl _moveLeftCtrl;
        private ButtonControl _moveRightCtrl;
        private ButtonControl _nextTargetCtrl;
        private ButtonControl _prevTargetCtrl;
        private ButtonControl _freeRoamReturnCtrl;

        private SpectatorMode _mode = SpectatorMode.Disabled;
        private PlayerHealth _followedSurvivor;
        private PlayerHealth _lastBannerTarget;
        private int _lastBannerHp = -1;
        private Vector3 _ghostPosition = new Vector3(0f, 3.5f, -12f);
        private int _followedIndex = -1;
        private bool _hasExplicitSurvivorPool;

        /// <summary>Raised whenever the spectator mode or followed target banner text updates.</summary>
        public event Action<string> OnSpectatorBannerChanged;

        /// <summary>Global observer event consumed by <c>CombatHudPresenter</c> to render the top spectator banner.</summary>
        public static event Action<bool, string> OnAnyLocalSpectatorStateChanged;

        /// <summary>Gets the current spectator camera mode.</summary>
        public SpectatorMode CurrentMode => _mode;

        /// <summary>Gets the currently followed living survivor (null when in GhostFreeRoam or Disabled).</summary>
        public PlayerHealth FollowedSurvivor => _followedSurvivor;

        /// <summary>Gets the clamped world position of the Ghost Free-Roam rig.</summary>
        public Vector3 GhostPosition => _ghostPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticSpectatorEvents()
        {
            OnAnyLocalSpectatorStateChanged = null;
        }

        private void Awake()
        {
            if (_localPlayerHealth == null)
            {
                TryGetComponent(out _localPlayerHealth);
            }
        }

        private void OnEnable()
        {
            PlayerHealth.OnAnyKillConfirmedBroadcast += HandleAnyKillConfirmed;
            if (_localPlayerHealth != null)
            {
                _localPlayerHealth.OnPlayerDied += HandleLocalPlayerDied;
                _localPlayerHealth.OnHealthChanged += HandleLocalHealthChanged;
            }
        }

        private void OnDisable()
        {
            UnbindAllEvents();
        }

        private void OnDestroy()
        {
            UnbindAllEvents();
        }

        /// <summary>Configures dependencies and candidate survivors for runtime or deterministic EditMode tests.</summary>
        public void ConfigureForLocalPlayer(
            PlayerHealth localHealth,
            Camera sceneCamera,
            LocalInputPoller inputPoller = null,
            IInputFocusService focusService = null,
            IKeybindingService keybindingService = null,
            MobileInputBridge mobileBridge = null)
        {
            UnbindAllEvents();

            _localPlayerHealth = localHealth;
            _targetCamera = sceneCamera != null ? sceneCamera : Camera.main;
            _inputPoller = inputPoller;
            _focusService = focusService;
            _keybindingService = keybindingService;
            _mobileBridge = mobileBridge;

            PlayerHealth.OnAnyKillConfirmedBroadcast += HandleAnyKillConfirmed;
            if (_keybindingService != null)
            {
                _keybindingService.OnBindingsChanged += RefreshBoundControls;
            }

            RefreshBoundControls();
            BindLocalHealthEvents();
        }

        /// <summary>Registers or updates the explicit survivor pool used by <see cref="CycleTarget"/>.</summary>
        public void SetSurvivorPool(IReadOnlyList<PlayerHealth> players, IReadOnlyDictionary<PlayerRef, string> names = null)
        {
            _survivorCandidates.Clear();
            _hasExplicitSurvivorPool = players != null;
            if (players != null)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] != null)
                    {
                        _survivorCandidates.Add(players[i]);
                    }
                }
            }

            _displayNames.Clear();
            if (names != null)
            {
                foreach (KeyValuePair<PlayerRef, string> kvp in names)
                {
                    _displayNames[kvp.Key] = kvp.Value;
                }
            }

            InvalidateBannerCache();
        }

        /// <summary>Updates a single player's display name for zero-alloc spectator banners.</summary>
        public void SetPlayerDisplayName(PlayerRef player, string displayName)
        {
            if (player != PlayerRef.None && !string.IsNullOrWhiteSpace(displayName))
            {
                _displayNames[player] = displayName;
                InvalidateBannerCache();
            }
        }

        /// <summary>Activates Ghost Free-Roam mode at the clamped initial position.</summary>
        public void ActivateGhostFreeRoam(Vector3 startPosition)
        {
            _mode = SpectatorMode.GhostFreeRoam;
            _followedSurvivor = null;
            _followedIndex = -1;
            InvalidateBannerCache();
            _ghostPosition = ClampGhostPosition(startPosition);
            EmitBanner("GHOST SPECTATOR — FREE ROAM ([LMB/RMB] Follow Survivor)");
        }

        /// <summary>Deactivates spectator mode (e.g., on rematch respawn).</summary>
        public void DeactivateSpectator()
        {
            _mode = SpectatorMode.Disabled;
            _followedSurvivor = null;
            _followedIndex = -1;
            InvalidateBannerCache();
            OnSpectatorBannerChanged?.Invoke(string.Empty);
            OnAnyLocalSpectatorStateChanged?.Invoke(false, string.Empty);
        }

        /// <summary>Returns from <see cref="SpectatorMode.FollowTarget"/> to <see cref="SpectatorMode.GhostFreeRoam"/>.</summary>
        public void ReturnToFreeRoam()
        {
            if (_mode == SpectatorMode.Disabled)
            {
                return;
            }

            Vector3 anchor = _followedSurvivor != null
                ? _followedSurvivor.transform.position + Vector3.up * 2.2f
                : _ghostPosition;
            ActivateGhostFreeRoam(anchor);
        }

        /// <summary>
        /// Advances Ghost Free-Roam position by <paramref name="moveAxes"/> (X=strafe, Y=forward) and optional vertical axis
        /// at 9.0 m/s without <c>Layer 6</c> wall pull-in, clamping within <c>[-22m, +22m]</c> and <c>[0.5m, 8.0m]</c>.
        /// </summary>
        public Vector3 StepGhostFreeRoam(Vector2 moveAxes, float yawDeg, float pitchDeg, float deltaTime, float verticalElevate = 0f)
        {
            if (_mode != SpectatorMode.GhostFreeRoam)
            {
                return _ghostPosition;
            }

            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                moveAxes = Vector2.zero;
                verticalElevate = 0f;
            }

            Vector2 clampedPlanar = Vector2.ClampMagnitude(moveAxes, 1.0f);
            float clampedVertical = Mathf.Clamp(verticalElevate, -1.0f, 1.0f);
            Quaternion lookRot = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            Vector3 velocity = lookRot * new Vector3(clampedPlanar.x, 0f, clampedPlanar.y) + Vector3.up * clampedVertical;
            if (velocity.sqrMagnitude > 1.0f)
            {
                velocity.Normalize();
            }

            _ghostPosition = ClampGhostPosition(_ghostPosition + velocity * (GhostMoveSpeed * deltaTime));
            if (_targetCamera != null)
            {
                _targetCamera.transform.SetPositionAndRotation(_ghostPosition, lookRot);
            }

            return _ghostPosition;
        }

        /// <summary>
        /// Cycles to the next (<c>+1</c>) or previous (<c>-1</c>) living survivor (<c>IsDead == false</c>).
        /// Stays cleanly in <see cref="SpectatorMode.GhostFreeRoam"/> if 0 living survivors remain.
        /// </summary>
        public bool CycleTarget(int direction)
        {
            if (_mode == SpectatorMode.Disabled)
            {
                return false;
            }

            RefreshCandidatesFromRegistryIfDynamic();
            int count = _survivorCandidates.Count;
            if (count == 0)
            {
                ActivateGhostFreeRoam(_ghostPosition);
                return false;
            }

            int step = direction >= 0 ? 1 : -1;
            int startIdx = _followedIndex < 0 ? (step > 0 ? -1 : 0) : _followedIndex;

            for (int attempt = 0; attempt < count; attempt++)
            {
                int idx = ((startIdx + step * (attempt + 1)) % count + count) % count;
                PlayerHealth candidate = _survivorCandidates[idx];
                if (IsValidLivingSurvivor(candidate))
                {
                    _followedIndex = idx;
                    _followedSurvivor = candidate;
                    _mode = SpectatorMode.FollowTarget;
                    InvalidateBannerCache();
                    EmitFollowedTargetBannerIfChanged(candidate);
                    return true;
                }
            }

            ActivateGhostFreeRoam(_ghostPosition);
            return false;
        }

        /// <summary>
        /// Immediately switches to the next living survivor within 1 frame if the currently followed player dies or leaves.
        /// </summary>
        public void OnFollowedTargetEliminated(PlayerRef victim)
        {
            if (_mode != SpectatorMode.FollowTarget)
            {
                return;
            }

            if (_followedSurvivor == null || _followedSurvivor.IsDead || _followedSurvivor.OwnerPlayer == victim)
            {
                CycleTarget(+1);
            }
        }

        /// <summary>
        /// Removes a disconnected peer from the survivor pool and fails over immediately if currently followed.
        /// </summary>
        public void NotifyPeerDisconnected(PlayerRef leavingPlayer)
        {
            bool wasFollowing = _followedSurvivor != null && _followedSurvivor.OwnerPlayer == leavingPlayer;
            for (int i = _survivorCandidates.Count - 1; i >= 0; i--)
            {
                PlayerHealth candidate = _survivorCandidates[i];
                if (candidate == null || candidate.OwnerPlayer == leavingPlayer)
                {
                    _survivorCandidates.RemoveAt(i);
                }
            }

            if (_mode == SpectatorMode.FollowTarget && (wasFollowing || !IsValidLivingSurvivor(_followedSurvivor)))
            {
                _followedIndex = -1;
                CycleTarget(+1);
            }
        }

        /// <summary>Evaluates orbital follow-target camera placement and banner updates for a single frame.</summary>
        public void UpdateFollowTargetCamera(float yawDeg, float pitchDeg)
        {
            if (!IsValidLivingSurvivor(_followedSurvivor))
            {
                CycleTarget(+1);
                return;
            }

            EmitFollowedTargetBannerIfChanged(_followedSurvivor);
            if (_targetCamera == null)
            {
                return;
            }

            float clampedPitch = Mathf.Clamp(pitchDeg, CameraController.TpsMinPitchDeg, CameraController.TpsMaxPitchDeg);
            Quaternion orbitRot = Quaternion.Euler(clampedPitch, yawDeg, 0f);
            Vector3 pivot = _followedSurvivor.transform.position + Vector3.up * CameraController.CanonicalTpsHeight;
            Vector3 boomDir = -(orbitRot * Vector3.forward);
            float safeDist = CameraController.ComputeSafeBoomDistance(pivot, boomDir, CameraController.CanonicalTpsDistance);
            _targetCamera.transform.SetPositionAndRotation(pivot + boomDir * safeDist, orbitRot);
        }

        /// <summary>Pure boundary clamp keeping Ghost spectators inside <c>[-22m, +22m]</c> X/Z and <c>[0.5m, 8.0m]</c> Y.</summary>
        public static Vector3 ClampGhostPosition(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, -MaxHorizontalBounds, MaxHorizontalBounds),
                Mathf.Clamp(position.y, MinGhostElevationY, MaxGhostElevationY),
                Mathf.Clamp(position.z, -MaxHorizontalBounds, MaxHorizontalBounds));
        }

        private void LateUpdate()
        {
            if (_mode == SpectatorMode.Disabled)
            {
                return;
            }

            bool inputAllowed = _focusService == null || _focusService.IsGameplayInputAllowed;
            if (inputAllowed)
            {
                PollSpectatorModeShortcuts();
            }

            float yaw = _inputPoller != null ? _inputPoller.CurrentYaw : 0f;
            float pitch = _inputPoller != null ? _inputPoller.CurrentPitch : 15f;

            if (_mode == SpectatorMode.FollowTarget)
            {
                UpdateFollowTargetCamera(yaw, pitch);
            }
            else if (_mode == SpectatorMode.GhostFreeRoam)
            {
                Vector2 move = inputAllowed ? ReadFreeRoamAxes() : Vector2.zero;
                StepGhostFreeRoam(move, yaw, pitch, Time.deltaTime);
            }
        }

        private void PollSpectatorModeShortcuts()
        {
            bool pointerOverUi = (_inputPoller != null && _inputPoller.SimulatePointerOverUi) ||
                                 (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

            if (!pointerOverUi && WasControlPressed(_nextTargetCtrl, Mouse.current != null ? Mouse.current.leftButton : null))
            {
                CycleTarget(+1);
                return;
            }

            if (WasControlPressed(_prevTargetCtrl, Mouse.current != null ? Mouse.current.rightButton : null))
            {
                CycleTarget(-1);
                return;
            }

            if (_mode == SpectatorMode.FollowTarget &&
                WasControlPressed(_freeRoamReturnCtrl, Keyboard.current != null ? Keyboard.current.spaceKey : null))
            {
                ReturnToFreeRoam();
            }
        }

        private Vector2 ReadFreeRoamAxes()
        {
            float x = 0f;
            float y = 0f;
            Keyboard kb = Keyboard.current;

            if (IsControlHeld(_moveRightCtrl, kb != null ? kb.dKey : null)) x += 1f;
            if (IsControlHeld(_moveLeftCtrl, kb != null ? kb.aKey : null)) x -= 1f;
            if (IsControlHeld(_moveForwardCtrl, kb != null ? kb.wKey : null)) y += 1f;
            if (IsControlHeld(_moveBackwardCtrl, kb != null ? kb.sKey : null)) y -= 1f;

            Vector2 mobileMove = _mobileBridge != null ? _mobileBridge.MoveVector : Vector2.zero;
            return Vector2.ClampMagnitude(new Vector2(x, y) + mobileMove, 1.0f);
        }

        private void RefreshBoundControls()
        {
            if (_keybindingService == null)
            {
                return;
            }

            _moveForwardCtrl = ResolveButtonControl(GameInputAction.MoveForward);
            _moveBackwardCtrl = ResolveButtonControl(GameInputAction.MoveBackward);
            _moveLeftCtrl = ResolveButtonControl(GameInputAction.MoveLeft);
            _moveRightCtrl = ResolveButtonControl(GameInputAction.MoveRight);
            _nextTargetCtrl = ResolveButtonControl(GameInputAction.VerticalSlash);
            _prevTargetCtrl = ResolveButtonControl(GameInputAction.RangedFire);
            _freeRoamReturnCtrl = ResolveButtonControl(GameInputAction.Jump);
        }

        private ButtonControl ResolveButtonControl(GameInputAction action)
        {
            string path = _keybindingService.GetBindingPath(action);
            return string.IsNullOrEmpty(path) ? null : InputSystem.FindControl(path) as ButtonControl;
        }

        private static bool WasControlPressed(ButtonControl boundControl, ButtonControl fallback)
        {
            ButtonControl active = boundControl ?? fallback;
            return active != null && active.wasPressedThisFrame;
        }

        private static bool IsControlHeld(ButtonControl boundControl, ButtonControl fallback)
        {
            ButtonControl active = boundControl ?? fallback;
            return active != null && active.isPressed;
        }

        private void RefreshCandidatesFromRegistryIfDynamic()
        {
            if (!_hasExplicitSurvivorPool)
            {
                DamageReceiverRegistry.CopyTypedReceiversNonAlloc(_survivorCandidates, _localPlayerHealth);
            }
        }

        private bool IsValidLivingSurvivor(PlayerHealth candidate)
        {
            return candidate != null
                   && !ReferenceEquals(candidate, _localPlayerHealth)
                   && !candidate.IsDead
                   && candidate.gameObject.activeInHierarchy;
        }

        private void BindLocalHealthEvents()
        {
            if (_localPlayerHealth == null)
            {
                return;
            }

            _localPlayerHealth.OnPlayerDied += HandleLocalPlayerDied;
            _localPlayerHealth.OnHealthChanged += HandleLocalHealthChanged;
            if (_localPlayerHealth.IsDead)
            {
                ActivateGhostFreeRoam(_localPlayerHealth.transform.position + Vector3.up * 2.0f);
            }
        }

        private void UnbindAllEvents()
        {
            PlayerHealth.OnAnyKillConfirmedBroadcast -= HandleAnyKillConfirmed;
            if (_keybindingService != null)
            {
                _keybindingService.OnBindingsChanged -= RefreshBoundControls;
            }

            if (_localPlayerHealth != null)
            {
                _localPlayerHealth.OnPlayerDied -= HandleLocalPlayerDied;
                _localPlayerHealth.OnHealthChanged -= HandleLocalHealthChanged;
            }
        }

        private void HandleLocalPlayerDied(PlayerRef victim, PlayerRef killer)
        {
            Vector3 start = _localPlayerHealth != null
                ? _localPlayerHealth.transform.position + Vector3.up * 2.0f
                : new Vector3(0f, 3.0f, 0f);
            ActivateGhostFreeRoam(start);
        }

        private void HandleLocalHealthChanged(int currentHp, int maxHp)
        {
            if (_mode != SpectatorMode.Disabled && currentHp > 0 && _localPlayerHealth != null && !_localPlayerHealth.IsDead)
            {
                DeactivateSpectator();
            }
        }

        private void HandleAnyKillConfirmed(PlayerRef killer, PlayerRef victim)
        {
            OnFollowedTargetEliminated(victim);
        }

        private void EmitFollowedTargetBannerIfChanged(PlayerHealth target)
        {
            if (target == null)
            {
                return;
            }

            int currentHp = target.CurrentHP;
            if (ReferenceEquals(target, _lastBannerTarget) && currentHp == _lastBannerHp)
            {
                return;
            }

            _lastBannerTarget = target;
            _lastBannerHp = currentHp;

            _bannerBuilder.Clear();
            _bannerBuilder.Append("SPECTATING: ");
            _bannerBuilder.Append(ResolveDisplayName(target.OwnerPlayer));
            _bannerBuilder.Append(" (");
            _bannerBuilder.Append(currentHp);
            _bannerBuilder.Append('/');
            _bannerBuilder.Append(target.MaxHP);
            _bannerBuilder.Append(" HP)");
            EmitBanner(_bannerBuilder.ToString());
        }

        private void InvalidateBannerCache()
        {
            _lastBannerTarget = null;
            _lastBannerHp = -1;
        }

        private string ResolveDisplayName(PlayerRef player)
        {
            if (_displayNames.TryGetValue(player, out string custom) && !string.IsNullOrEmpty(custom))
            {
                return custom;
            }

            int id = player.PlayerId > 0 ? player.PlayerId : Mathf.Abs(player.RawEncoded);
            if (id >= 0 && id < FallbackPlayerNames.Length)
            {
                return FallbackPlayerNames[id];
            }

            return "Player";
        }

        private void EmitBanner(string bannerText)
        {
            OnSpectatorBannerChanged?.Invoke(bannerText);
            OnAnyLocalSpectatorStateChanged?.Invoke(true, bannerText);
        }
    }
}
