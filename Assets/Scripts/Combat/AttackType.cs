namespace CapsuleArena.Combat
{
    /// <summary>
    /// Identifies the canonical combat attack source for victim-authoritative RPC sanity validation
    /// (Story 6-2; ADR-0004; TR-health-002).
    /// </summary>
    public enum AttackType : byte
    {
        /// <summary>LMB Vertical Slash: 25 HP damage, max sanity distance 5.0m.</summary>
        VerticalSlash = 0,

        /// <summary>Keys 1/3 Horizontal Sweep: 30 HP damage, max sanity distance 5.0m.</summary>
        HorizontalSweep = 1,

        /// <summary>RMB Ranged Projectile bolt: 15 HP damage, max sanity distance 34.0m.</summary>
        RangedProjectile = 2
    }
}
