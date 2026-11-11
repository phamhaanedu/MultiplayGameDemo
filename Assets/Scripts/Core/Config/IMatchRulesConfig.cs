namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Read-only contract exposing configurable Last Man Standing match timing and late-join rules
    /// so <c>MatchManager</c> and unit tests can consume rules via dependency injection without singletons.
    /// </summary>
    public interface IMatchRulesConfig
    {
        /// <summary>Gets the minimum connected players required to start and sustain pre-match countdown.</summary>
        int MinPlayersToStart { get; }

        /// <summary>Gets the maximum concurrent players supported per Shared Mode room.</summary>
        int MaxPlayersPerMatch { get; }

        /// <summary>Gets the pre-match countdown duration in seconds (default: 5.0s).</summary>
        float CountdownDurationSeconds { get; }

        /// <summary>Gets the precomputed pre-match countdown duration in 60Hz ticks (default: 300).</summary>
        int CountdownDurationTicks { get; }

        /// <summary>Gets the active combat round duration in seconds (default: 180.0s).</summary>
        float PlayingDurationSeconds { get; }

        /// <summary>Gets the precomputed active combat round duration in 60Hz ticks (default: 10,800).</summary>
        int PlayingDurationTicks { get; }

        /// <summary>
        /// Gets the opening window of <c>Playing</c> in seconds during which late-joining players (3rd/4th)
        /// still spawn alive as active combatants; joining strictly after this window places them in Ghost Spectator (default: 60.0s).
        /// </summary>
        float LateJoinActiveWindowSeconds { get; }

        /// <summary>Gets the precomputed late-join active combatant window in 60Hz ticks (default: 3,600).</summary>
        int LateJoinActiveWindowTicks { get; }

        /// <summary>Gets the post-match summary modal duration in seconds before rematch transition (default: 8.0s).</summary>
        float EndedDurationSeconds { get; }

        /// <summary>Gets the precomputed post-match summary duration in 60Hz ticks (default: 480).</summary>
        int EndedDurationTicks { get; }
    }
}
