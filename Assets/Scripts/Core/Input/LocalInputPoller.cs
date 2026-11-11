using System;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Polls PC New Input System controls, accumulates render-frame mouse/touch look angles in <c>Update()</c>,
    /// and merges them with <see cref="MobileInputBridge"/> into a deterministic 16-byte <see cref="NetworkInputData"/>
    /// payload gated by <see cref="IInputFocusService"/> (Stories 3-2, 3-3; ADR-0002; TR-input-002, TR-input-003).
    /// </summary>
    public sealed class LocalInputPoller : MonoBehaviour
    {
        /// <summary>Minimum vertical camera pitch angle in degrees.</summary>
        public const float MinPitchDeg = -80.0f;

        /// <summary>Maximum vertical camera pitch angle in degrees.</summary>
        public const float MaxPitchDeg = 80.0f;

        private const float BaseMouseDegreesPerPixel = 0.12f;

        [Header("Optional Scene Bridges")]
        [Tooltip("Optional mobile touch input aggregator.")]
        [SerializeField] private MobileInputBridge _mobileBridge;

        private IKeybindingService _keybindingService;
        private IInputFocusService _focusService;
        private NetworkButtons _pcLatchedButtons;
        private NetworkButtons _simTickLatchedButtons;
        private NetworkButtons _simMobileLatchedButtons;
        private int _lastSampledSimulationTick = int.MinValue;
        private NetworkInputData _lastSampledSimulationInput;
        private float _currentYaw;
        private float _currentPitch;
        private bool _isHorizontalYawLocked;
        private bool _pointerOverUiBlockedMouseLeft;

        private readonly ButtonControl[] _resolvedControls = new ButtonControl[14];
        private readonly bool[] _isMouseLeftControl = new bool[14];

        /// <summary>Raised locally when ToggleCameraView (V) is pressed while gameplay input is allowed.</summary>
        public event Action OnToggleCameraViewRequested;

        /// <summary>Raised locally when ToggleChatFocus (Enter) is pressed.</summary>
        public event Action OnToggleChatFocusRequested;

        /// <summary>Gets the current accumulated horizontal aim yaw in degrees.</summary>
        public float CurrentYaw => _currentYaw;

        /// <summary>Gets the current clamped vertical aim pitch in degrees [-80, +80].</summary>
        public float CurrentPitch => _currentPitch;

        /// <summary>Gets a value indicating whether horizontal mouse/touch yaw is locked by an active 90-deg Horizontal Sweep.</summary>
        public bool IsHorizontalYawLocked => _isHorizontalYawLocked;

        /// <summary>Locks or unlocks manual horizontal yaw changes while updating the authoritative sweep yaw (Story 7-2).</summary>
        public void SetHorizontalYawLock(bool locked, float authoritativeYawDegrees)
        {
            _isHorizontalYawLocked = locked;
            _currentYaw = NormalizeYaw(authoritativeYawDegrees);
        }

        /// <summary>Gets or sets a test override simulating <c>EventSystem.IsPointerOverGameObject()</c>.</summary>
        public bool SimulatePointerOverUi { get; set; }

        /// <summary>
        /// Injects the keybinding and focus services and caches resolved Input System controls.
        /// </summary>
        public void Initialize(
            IKeybindingService keybindingService,
            IInputFocusService focusService,
            MobileInputBridge mobileBridge = null)
        {
            UnbindServices();

            _keybindingService = keybindingService ?? new KeybindingManager();
            _focusService = focusService ?? new InputFocusManager();
            if (mobileBridge != null)
            {
                _mobileBridge = mobileBridge;
            }

            if (_mobileBridge != null)
            {
                _mobileBridge.BindFocusService(_focusService);
            }

            _keybindingService.OnBindingsChanged += RefreshControlCache;
            _focusService.OnFocusStateChanged += HandleFocusChanged;
            RefreshControlCache();
        }

        private void Start()
        {
            EnsureDefaultServicesInitialized();
        }

        private void OnDestroy()
        {
            UnbindServices();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _pcLatchedButtons = default;
                _simTickLatchedButtons = default;
                _simMobileLatchedButtons = default;
                _mobileBridge?.ClearAllInputs();
            }
        }

        private void Update()
        {
            EnsureDefaultServicesInitialized();
            _pointerOverUiBlockedMouseLeft = SimulatePointerOverUi ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

            PollSubTickLatchesAndLocalShortcuts();
            if (_focusService == null || _focusService.IsGameplayInputAllowed)
            {
                AccumulateLookAnglesFromFrameDevices();
            }
        }

        /// <summary>
        /// Sets the initial yaw and pitch (e.g., on player spawn facing Arena center).
        /// </summary>
        public void SetLookAngles(float yawDegrees, float pitchDegrees)
        {
            _currentYaw = NormalizeYaw(yawDegrees);
            _currentPitch = Mathf.Clamp(pitchDegrees, MinPitchDeg, MaxPitchDeg);
        }

        /// <summary>
        /// Applies an explicit look delta in degrees (used by touch bridges and deterministic tests)
        /// when gameplay input focus is allowed.
        /// </summary>
        public void ApplyLookDeltaDegrees(Vector2 lookDeltaDegrees)
        {
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                return;
            }

            float sensitivity = _keybindingService != null ? _keybindingService.MouseSensitivity : 1.0f;
            if (!_isHorizontalYawLocked)
            {
                _currentYaw = NormalizeYaw(_currentYaw + lookDeltaDegrees.x * sensitivity);
            }

            _currentPitch = Mathf.Clamp(_currentPitch - lookDeltaDegrees.y * sensitivity, MinPitchDeg, MaxPitchDeg);
        }

        /// <summary>
        /// Builds the 60Hz <see cref="NetworkInputData"/> struct for Fusion's <c>OnInput</c> callback
        /// with zero heap allocations.
        /// </summary>
        public NetworkInputData CollectNetworkInput()
        {
            EnsureDefaultServicesInitialized();
            bool allowed = _focusService == null || _focusService.IsGameplayInputAllowed;
            if (!allowed)
            {
                _pcLatchedButtons = default;
                _mobileBridge?.ClearAllInputs();
                return MergeInputs(Vector2.zero, default, Vector2.zero, default, _currentYaw, _currentPitch, false);
            }

            if (_mobileBridge != null)
            {
                Vector2 touchLook = _mobileBridge.ConsumeLookDelta();
                if (touchLook.sqrMagnitude > 0f)
                {
                    ApplyLookDeltaDegrees(touchLook);
                }
            }

            Vector2 pcMove = ReadPcMoveVector();
            NetworkButtons pcButtons = ReadPcButtonsFromLatch(_pcLatchedButtons);
            _pcLatchedButtons = default;

            Vector2 mobileMove = _mobileBridge != null ? _mobileBridge.MoveVector : Vector2.zero;
            NetworkButtons mobileButtons = _mobileBridge != null ? _mobileBridge.ConsumeButtons() : default;
            if (mobileButtons.Bits != 0)
            {
                _simMobileLatchedButtons = new NetworkButtons(_simMobileLatchedButtons.Bits | mobileButtons.Bits);
            }

            return MergeInputs(pcMove, pcButtons, mobileMove, mobileButtons, _currentYaw, _currentPitch, true);
        }

        /// <summary>
        /// Samples deterministic 60Hz tick input directly for a Shared Mode <c>StateAuthority</c> avatar.
        /// Multiple controllers on the same local avatar (e.g., mobility and combat) calling this within the same
        /// <paramref name="simulationTick"/> receive the identical <see cref="NetworkInputData"/> snapshot without
        /// losing single-press button latches to <c>Simulation+Client</c> multi-tick <c>OnInput</c> polling.
        /// </summary>
        public NetworkInputData SampleSimulationTickInput(int simulationTick)
        {
            EnsureDefaultServicesInitialized();
            if (simulationTick == _lastSampledSimulationTick)
            {
                return _lastSampledSimulationInput;
            }

            _lastSampledSimulationTick = simulationTick;
            bool allowed = _focusService == null || _focusService.IsGameplayInputAllowed;
            if (!allowed)
            {
                _simTickLatchedButtons = default;
                _simMobileLatchedButtons = default;
                _mobileBridge?.ClearAllInputs();
                _lastSampledSimulationInput = MergeInputs(Vector2.zero, default, Vector2.zero, default, _currentYaw, _currentPitch, false);
                return _lastSampledSimulationInput;
            }

            if (_mobileBridge != null)
            {
                Vector2 touchLook = _mobileBridge.ConsumeLookDelta();
                if (touchLook.sqrMagnitude > 0f)
                {
                    ApplyLookDeltaDegrees(touchLook);
                }
            }

            Vector2 pcMove = ReadPcMoveVector();
            NetworkButtons pcButtons = ReadPcButtonsFromLatch(_simTickLatchedButtons);
            _simTickLatchedButtons = default;

            Vector2 mobileMove = _mobileBridge != null ? _mobileBridge.MoveVector : Vector2.zero;
            NetworkButtons directMobile = _mobileBridge != null ? _mobileBridge.ConsumeButtons() : default;
            NetworkButtons mobileButtons = new NetworkButtons(_simMobileLatchedButtons.Bits | directMobile.Bits);
            _simMobileLatchedButtons = default;

            _lastSampledSimulationInput = MergeInputs(pcMove, pcButtons, mobileMove, mobileButtons, _currentYaw, _currentPitch, true);
            return _lastSampledSimulationInput;
        }

        /// <summary>
        /// Resets cached simulation tick sampling state and button latches on local avatar spawn or session reset.
        /// </summary>
        public void ResetSimulationTickSampler()
        {
            _lastSampledSimulationTick = int.MinValue;
            _lastSampledSimulationInput = default;
            _pcLatchedButtons = default;
            _simTickLatchedButtons = default;
            _simMobileLatchedButtons = default;
        }

        /// <summary>
        /// Explicitly latches a <see cref="NetworkInputButtons"/> bit as if pressed during <c>Update()</c>
        /// when gameplay focus allows it (supports deterministic sub-tick latch testing).
        /// </summary>
        public void LatchButton(NetworkInputButtons targetBit)
        {
            EnsureDefaultServicesInitialized();
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                return;
            }

            _pcLatchedButtons.Set((int)targetBit, true);
            _simTickLatchedButtons.Set((int)targetBit, true);
        }

        /// <summary>
        /// Pure deterministic combiner merging PC and Mobile move vectors and button bitmasks
        /// while clamping magnitude to 1.0, clamping pitch to [-80, +80], and honoring focus gating.
        /// </summary>
        public static NetworkInputData MergeInputs(
            Vector2 pcMove,
            NetworkButtons pcButtons,
            Vector2 mobileMove,
            NetworkButtons mobileButtons,
            float lookYaw,
            float lookPitch,
            bool isGameplayAllowed)
        {
            float clampedPitch = Mathf.Clamp(lookPitch, MinPitchDeg, MaxPitchDeg);
            float normalizedYaw = NormalizeYaw(lookYaw);

            if (!isGameplayAllowed)
            {
                return new NetworkInputData
                {
                    MoveDirection = Vector2.zero,
                    LookYaw = normalizedYaw,
                    LookPitch = clampedPitch,
                    Buttons = default
                };
            }

            Vector2 combinedMove = Vector2.ClampMagnitude(pcMove + mobileMove, 1.0f);
            NetworkButtons combinedButtons = new NetworkButtons(pcButtons.Bits | mobileButtons.Bits);

            return new NetworkInputData
            {
                MoveDirection = combinedMove,
                LookYaw = normalizedYaw,
                LookPitch = clampedPitch,
                Buttons = combinedButtons
            };
        }

        private void EnsureDefaultServicesInitialized()
        {
            if (_keybindingService == null || _focusService == null)
            {
                Initialize(_keybindingService ?? new KeybindingManager(), _focusService ?? new InputFocusManager(), _mobileBridge);
            }
        }

        private void PollSubTickLatchesAndLocalShortcuts()
        {
            if (WasPressedThisFrame(GameInputAction.ToggleChatFocus))
            {
                OnToggleChatFocusRequested?.Invoke();
            }

            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                _pcLatchedButtons = default;
                _simTickLatchedButtons = default;
                _simMobileLatchedButtons = default;
                return;
            }

            if (WasPressedThisFrame(GameInputAction.ToggleCameraView))
            {
                OnToggleCameraViewRequested?.Invoke();
            }

            LatchIfPressed(GameInputAction.Jump, NetworkInputButtons.Jump);
            LatchIfPressed(GameInputAction.DodgeLeft, NetworkInputButtons.DodgeLeft);
            LatchIfPressed(GameInputAction.DodgeRight, NetworkInputButtons.DodgeRight);
            LatchIfPressed(GameInputAction.ForwardDash, NetworkInputButtons.ForwardDash);
            LatchIfPressed(GameInputAction.VerticalSlash, NetworkInputButtons.VerticalSlash);
            LatchIfPressed(GameInputAction.HorizontalSweepLeft, NetworkInputButtons.HorizontalSweepLeft);
            LatchIfPressed(GameInputAction.HorizontalSweepRight, NetworkInputButtons.HorizontalSweepRight);
            LatchIfPressed(GameInputAction.RangedFire, NetworkInputButtons.RangedFire);
        }

        private Vector2 ReadPcMoveVector()
        {
            float x = 0f;
            float y = 0f;
            if (IsPressed(GameInputAction.MoveRight)) x += 1f;
            if (IsPressed(GameInputAction.MoveLeft)) x -= 1f;
            if (IsPressed(GameInputAction.MoveForward)) y += 1f;
            if (IsPressed(GameInputAction.MoveBackward)) y -= 1f;
            return Vector2.ClampMagnitude(new Vector2(x, y), 1.0f);
        }

        private NetworkButtons ReadPcButtonsFromLatch(NetworkButtons initialLatch)
        {
            NetworkButtons buttons = initialLatch;
            SetIfPressed(ref buttons, GameInputAction.Jump, NetworkInputButtons.Jump);
            SetIfPressed(ref buttons, GameInputAction.DodgeLeft, NetworkInputButtons.DodgeLeft);
            SetIfPressed(ref buttons, GameInputAction.DodgeRight, NetworkInputButtons.DodgeRight);
            SetIfPressed(ref buttons, GameInputAction.ForwardDash, NetworkInputButtons.ForwardDash);
            SetIfPressed(ref buttons, GameInputAction.VerticalSlash, NetworkInputButtons.VerticalSlash);
            SetIfPressed(ref buttons, GameInputAction.HorizontalSweepLeft, NetworkInputButtons.HorizontalSweepLeft);
            SetIfPressed(ref buttons, GameInputAction.HorizontalSweepRight, NetworkInputButtons.HorizontalSweepRight);
            SetIfPressed(ref buttons, GameInputAction.RangedFire, NetworkInputButtons.RangedFire);
            return buttons;
        }

        private void AccumulateLookAnglesFromFrameDevices()
        {
            Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            Vector2 touchDelta = _mobileBridge != null ? _mobileBridge.ConsumeLookDelta() : Vector2.zero;
            Vector2 combinedDegrees = mouseDelta * BaseMouseDegreesPerPixel + touchDelta;
            if (combinedDegrees.sqrMagnitude > 0f)
            {
                ApplyLookDeltaDegrees(combinedDegrees);
            }
        }

        private void RefreshControlCache()
        {
            if (_keybindingService == null)
            {
                return;
            }

            for (int i = 0; i < _resolvedControls.Length; i++)
            {
                string path = _keybindingService.GetBindingPath((GameInputAction)i);
                _isMouseLeftControl[i] = !string.IsNullOrEmpty(path) &&
                    string.Equals(path.Trim(), "<Mouse>/leftButton", StringComparison.OrdinalIgnoreCase);
                _resolvedControls[i] = string.IsNullOrEmpty(path)
                    ? null
                    : InputSystem.FindControl(path) as ButtonControl;
            }
        }

        private void HandleFocusChanged(bool isGameplayAllowed)
        {
            if (!isGameplayAllowed)
            {
                _pcLatchedButtons = default;
                _simTickLatchedButtons = default;
                _simMobileLatchedButtons = default;
            }
        }

        private void LatchIfPressed(GameInputAction action, NetworkInputButtons targetBit)
        {
            if (WasPressedThisFrame(action))
            {
                _pcLatchedButtons.Set((int)targetBit, true);
                _simTickLatchedButtons.Set((int)targetBit, true);
            }
        }

        private void SetIfPressed(ref NetworkButtons buttons, GameInputAction action, NetworkInputButtons targetBit)
        {
            if (IsPressed(action))
            {
                buttons.Set((int)targetBit, true);
            }
        }

        private bool IsPressed(GameInputAction action)
        {
            int idx = (int)action;
            if (_pointerOverUiBlockedMouseLeft && _isMouseLeftControl[idx])
            {
                return false;
            }

            ButtonControl control = _resolvedControls[idx];
            return control != null && control.isPressed;
        }

        private bool WasPressedThisFrame(GameInputAction action)
        {
            int idx = (int)action;
            if (_pointerOverUiBlockedMouseLeft && _isMouseLeftControl[idx])
            {
                return false;
            }

            ButtonControl control = _resolvedControls[idx];
            return control != null && control.wasPressedThisFrame;
        }

        private void UnbindServices()
        {
            if (_keybindingService != null)
            {
                _keybindingService.OnBindingsChanged -= RefreshControlCache;
            }

            if (_focusService != null)
            {
                _focusService.OnFocusStateChanged -= HandleFocusChanged;
            }
        }

        private static float NormalizeYaw(float yaw)
        {
            if (float.IsNaN(yaw) || float.IsInfinity(yaw))
            {
                return 0f;
            }

            return Mathf.Repeat(yaw + 180f, 360f) - 180f;
        }
    }
}
