using Fusion;
using UnityEngine;

namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Aggregates virtual joystick/D-Pad movement and touch skill buttons on mobile screens,
    /// latching sub-tick taps until consumed by <see cref="LocalInputPoller"/> (Stories 3-2, 3-3).
    /// </summary>
    public sealed class MobileInputBridge : MonoBehaviour
    {
        private Vector2 _moveVector;
        private Vector2 _lookDelta;
        private NetworkButtons _heldButtons;
        private NetworkButtons _latchedButtons;
        private IInputFocusService _focusService;

        /// <summary>Gets the current clamped mobile movement vector (<c>||v|| &lt;= 1.0</c>).</summary>
        public Vector2 MoveVector => _moveVector;

        private void OnDestroy()
        {
            UnbindFocusService();
        }

        /// <summary>
        /// Subscribes to <see cref="IInputFocusService"/> so active touch holds/latches release immediately
        /// whenever UI focus blocks gameplay input (even if PointerUp is swallowed by a modal).
        /// </summary>
        public void BindFocusService(IInputFocusService focusService)
        {
            UnbindFocusService();
            _focusService = focusService;
            if (_focusService != null)
            {
                _focusService.OnFocusStateChanged += HandleFocusStateChanged;
                if (!_focusService.IsGameplayInputAllowed)
                {
                    ClearAllInputs();
                }
            }
        }

        /// <summary>Updates the normalized 2D movement vector from virtual Joystick or 4-button D-Pad.</summary>
        public void SetMoveVector(Vector2 rawVector)
        {
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                _moveVector = Vector2.zero;
                return;
            }

            _moveVector = Vector2.ClampMagnitude(rawVector, 1.0f);
        }

        /// <summary>Accumulates touch drag look delta in degrees until consumed by the poller.</summary>
        public void AddLookDelta(Vector2 deltaDegrees)
        {
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                return;
            }

            _lookDelta += deltaDegrees;
        }

        /// <summary>Marks a virtual skill button as held and latched for at least one network tick.</summary>
        public void PressButton(NetworkInputButtons button)
        {
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                return;
            }

            _heldButtons.Set((int)button, true);
            _latchedButtons.Set((int)button, true);
        }

        /// <summary>Releases a held virtual skill button while preserving any unconsumed sub-tick tap latch.</summary>
        public void ReleaseButton(NetworkInputButtons button)
        {
            _heldButtons.Set((int)button, false);
        }

        /// <summary>Latches a single-frame tap so rapid PointerDown+PointerUp within one frame is never lost.</summary>
        public void TriggerTapButton(NetworkInputButtons button)
        {
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                return;
            }

            _latchedButtons.Set((int)button, true);
        }

        /// <summary>
        /// Returns the bitwise OR of held and latched buttons and clears the single-tick latch mask.
        /// </summary>
        public NetworkButtons ConsumeButtons()
        {
            if (_focusService != null && !_focusService.IsGameplayInputAllowed)
            {
                ClearAllInputs();
                return default;
            }

            NetworkButtons combined = new NetworkButtons(_heldButtons.Bits | _latchedButtons.Bits);
            _latchedButtons = default;
            return combined;
        }

        /// <summary>Returns and resets the accumulated touch look delta for this poll step.</summary>
        public Vector2 ConsumeLookDelta()
        {
            Vector2 delta = _lookDelta;
            _lookDelta = Vector2.zero;
            return delta;
        }

        /// <summary>Immediately clears all held buttons, latched taps, look deltas, and joystick vectors.</summary>
        public void ClearAllInputs()
        {
            _moveVector = Vector2.zero;
            _lookDelta = Vector2.zero;
            _heldButtons = default;
            _latchedButtons = default;
        }

        private void HandleFocusStateChanged(bool isGameplayAllowed)
        {
            if (!isGameplayAllowed)
            {
                ClearAllInputs();
            }
        }

        private void UnbindFocusService()
        {
            if (_focusService != null)
            {
                _focusService.OnFocusStateChanged -= HandleFocusStateChanged;
                _focusService = null;
            }
        }
    }
}
