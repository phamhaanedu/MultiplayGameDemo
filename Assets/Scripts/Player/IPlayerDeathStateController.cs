namespace CapsuleArena.Player
{
    /// <summary>
    /// Contract governing capsule death-to-ghost spectator presentation and layer transitions
    /// without despawning the underlying <see cref="Fusion.NetworkObject"/> (Story 6-3; TR-health-003).
    /// </summary>
    public interface IPlayerDeathStateController
    {
        /// <summary>Gets a value indicating whether the capsule is currently in Ghost Spectator state.</summary>
        bool IsInGhostState { get; }

        /// <summary>Gets a value indicating whether combat inputs (Slash/Sweep/Fire/Dash/Dodge/Jump) are locked.</summary>
        bool IsCombatInputLocked { get; }

        /// <summary>
        /// Switches capsule hierarchy to Layer 10 (GhostSpectator), swaps to translucent ghost material,
        /// cancels mobility bursts/tilts, disables combat hitboxes, hides overhead HP bar and local Mobile Action Pad,
        /// sets nameplate opacity to 50% with "[GHOST]", and optionally triggers elimination VFX/SFX.
        /// </summary>
        /// <param name="isAuthoritativeSimulation">True when called from simulation/RPC; false when called from Render()/Spawned().</param>
        /// <param name="playEliminationEffects">True for combat eliminations; false when entering Ghost Spectator via mid-match late join.</param>
        void ApplyDeathState(bool isAuthoritativeSimulation = true, bool playEliminationEffects = true);

        /// <summary>
        /// Restores capsule hierarchy to Layer 8 (PlayerCharacter), restores opaque combatant material,
        /// re-enables overhead HP bar, full-opacity base nameplate, combat hitboxes, and local Action Pad.
        /// </summary>
        /// <param name="overrideBaseName">Optional base player name override.</param>
        /// <param name="isAuthoritativeSimulation">True when called from simulation/RPC; false when called from Render()/Spawned().</param>
        void RestoreAliveState(string overrideBaseName = null, bool isAuthoritativeSimulation = true);
    }
}
