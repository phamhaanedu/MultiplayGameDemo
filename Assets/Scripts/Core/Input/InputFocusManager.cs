using System;

namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Bitmask flags representing modal overlays or match states that suppress gameplay input (Story 3-3; TR-input-003).
    /// </summary>
    [Flags]
    public enum InputFocusFlag : byte
    {
        /// <summary>No UI overlay active; gameplay input is enabled.</summary>
        None = 0,

        /// <summary>In-match chat input field has keyboard focus.</summary>
        ChatFocused = 1 << 0,

        /// <summary>Settings keybinding modal is listening for a key press.</summary>
        RebindModalOpen = 1 << 1,

        /// <summary>Match victory/summary screen is active.</summary>
        MatchEnded = 1 << 2,

        /// <summary>Leaderboard overlay is open and blocking gameplay input.</summary>
        LeaderboardOpen = 1 << 3
    }

    /// <summary>
    /// Contract for exclusive focus gating between UI overlays and gameplay movement/combat polling.
    /// </summary>
    public interface IInputFocusService
    {
        /// <summary>Raised only when <see cref="IsGameplayInputAllowed"/> transitions between true and false.</summary>
        event Action<bool> OnFocusStateChanged;

        /// <summary>Gets the currently active UI/modal suppression flags.</summary>
        InputFocusFlag ActiveFlags { get; }

        /// <summary>Gets a value indicating whether all suppression flags are clear (<c>ActiveFlags == None</c>).</summary>
        bool IsGameplayInputAllowed { get; }

        /// <summary>Sets or clears an individual focus suppression flag.</summary>
        void SetFocusFlag(InputFocusFlag flag, bool active);

        /// <summary>Clears all active flags immediately (invoked on scene unload or session disconnect).</summary>
        void ResetAllFlags();
    }

    /// <summary>
    /// Bitmask state tracker gating PC and mobile gameplay inputs whenever chat, settings rebind,
    /// leaderboard, or match-end overlays are active (Story 3-3; ADR-0005; TR-input-003).
    /// </summary>
    public sealed class InputFocusManager : IInputFocusService
    {
        private InputFocusFlag _activeFlags = InputFocusFlag.None;

        /// <inheritdoc />
        public event Action<bool> OnFocusStateChanged;

        /// <inheritdoc />
        public InputFocusFlag ActiveFlags => _activeFlags;

        /// <inheritdoc />
        public bool IsGameplayInputAllowed => _activeFlags == InputFocusFlag.None;

        /// <inheritdoc />
        public void SetFocusFlag(InputFocusFlag flag, bool active)
        {
            if (flag == InputFocusFlag.None)
            {
                return;
            }

            bool wasAllowed = IsGameplayInputAllowed;
            if (active)
            {
                _activeFlags |= flag;
            }
            else
            {
                _activeFlags &= ~flag;
            }

            bool isNowAllowed = IsGameplayInputAllowed;
            if (wasAllowed != isNowAllowed)
            {
                OnFocusStateChanged?.Invoke(isNowAllowed);
            }
        }

        /// <inheritdoc />
        public void ResetAllFlags()
        {
            bool wasAllowed = IsGameplayInputAllowed;
            _activeFlags = InputFocusFlag.None;

            if (!wasAllowed)
            {
                OnFocusStateChanged?.Invoke(true);
            }
        }
    }
}
