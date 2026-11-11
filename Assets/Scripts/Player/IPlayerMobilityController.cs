using System;
using CapsuleArena.Core.Config;

namespace CapsuleArena.Player
{
    /// <summary>
    /// Mutually exclusive locomotion states for a Capsule combatant (Stories 5-1, 5-2, 5-3).
    /// </summary>
    public enum MobilityState : byte
    {
        /// <summary>Standard planar WASD walk, jump, and gravity locomotion.</summary>
        Normal = 0,

        /// <summary>Locked-direction 18.0 m/s forward burst dash for 12 ticks (Key 2).</summary>
        Dashing = 1,

        /// <summary>Locked-direction 12.0 m/s left dodge roll for 15 ticks (Key Q).</summary>
        DodgingLeft = 2,

        /// <summary>Locked-direction 12.0 m/s right dodge roll for 15 ticks (Key E).</summary>
        DodgingRight = 3
    }

    /// <summary>
    /// Contract exposing mobility cooldown events, state queries, and death/ghost cancellation hooks.
    /// </summary>
    public interface IPlayerMobilityController
    {
        /// <summary>Raised when ForwardDash or DodgeRoll enters cooldown: (skillId, remainingSec, totalSec).</summary>
        event Action<CombatSkillId, float, float> OnCooldownUpdated;

        /// <summary>Gets the current locomotion state.</summary>
        MobilityState CurrentMobilityState { get; }

        /// <summary>Gets a value indicating whether combat bursts (Dash/Dodge/Jump) are suppressed by death/ghost mode.</summary>
        bool IsGhostMode { get; }

        /// <summary>
        /// Immediately cancels any active Dash/Dodge burst, resets VisualRoot rotation to identity,
        /// and toggles Ghost Spectator locomotion mode.
        /// </summary>
        void SetGhostSpectatorMode(bool isGhost);
    }
}
