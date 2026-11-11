namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Enumerates all rebindable PC keyboard and mouse actions in Capsule Arena Duel.
    /// </summary>
    public enum GameInputAction : byte
    {
        /// <summary>Move forward (+Z local; default: W).</summary>
        MoveForward = 0,

        /// <summary>Move backward (-Z local; default: S).</summary>
        MoveBackward = 1,

        /// <summary>Strafe left (-X local; default: A).</summary>
        MoveLeft = 2,

        /// <summary>Strafe right (+X local; default: D).</summary>
        MoveRight = 3,

        /// <summary>Single vertical jump (default: Space).</summary>
        Jump = 4,

        /// <summary>Lateral dodge roll left (default: Q).</summary>
        DodgeLeft = 5,

        /// <summary>Lateral dodge roll right (default: E).</summary>
        DodgeRight = 6,

        /// <summary>Forward burst dash (default: Key 2).</summary>
        ForwardDash = 7,

        /// <summary>Vertical melee slash (default: Mouse Left Button).</summary>
        VerticalSlash = 8,

        /// <summary>Horizontal melee sweep + 90-deg left turn (default: Key 1).</summary>
        HorizontalSweepLeft = 9,

        /// <summary>Horizontal melee sweep + 90-deg right turn (default: Key 3).</summary>
        HorizontalSweepRight = 10,

        /// <summary>Ranged projectile bolt fire (default: Mouse Right Button).</summary>
        RangedFire = 11,

        /// <summary>Toggle TPS / FPS camera view (default: V).</summary>
        ToggleCameraView = 12,

        /// <summary>Toggle in-match chat input focus (default: Enter).</summary>
        ToggleChatFocus = 13
    }

    /// <summary>
    /// Selects the virtual left-thumb movement control layout on mobile touch screens.
    /// </summary>
    public enum MobileControlMode : byte
    {
        /// <summary>Floating virtual analog joystick.</summary>
        Joystick = 0,

        /// <summary>4-directional discrete D-Pad buttons.</summary>
        DPad = 1
    }

    /// <summary>
    /// Controls visibility override of the 7-button Mobile Action Pad (Story 3-1: 0=Auto, 1=ForceOn, 2=ForceOff).
    /// </summary>
    public enum MobilePadVisibilityMode : int
    {
        /// <summary>Auto-detect touch/mobile platform (0).</summary>
        Auto = 0,

        /// <summary>Force Action Pad visible even on PC/Editor (1).</summary>
        ForceOn = 1,

        /// <summary>Force Action Pad hidden (2).</summary>
        ForceOff = 2
    }
}
