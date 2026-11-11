namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Enumerates all cooldown-gated combat and mobility skills for a Capsule combatant.
    /// </summary>
    public enum CombatSkillId : byte
    {
        /// <summary>Forward burst dash bound to Key 2 by default.</summary>
        ForwardDash = 0,

        /// <summary>Lateral dodge roll bound to Keys Q and E by default.</summary>
        DodgeRoll = 1,

        /// <summary>Primary narrow-arc melee slash bound to Left Mouse Button.</summary>
        VerticalSlash = 2,

        /// <summary>Wide-arc melee sweep with 90-degree turn bound to Keys 1 and 3.</summary>
        HorizontalSweep = 3,

        /// <summary>Pooled linear energy bolt bound to Right Mouse Button.</summary>
        RangedFire = 4
    }
}
