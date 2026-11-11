using System;
using UnityEngine;

namespace CapsuleArena.Arena
{
    /// <summary>
    /// Abstraction for selecting the safest unobstructed spawn pose in the Arena.
    /// </summary>
    public interface IArenaSpawnSelector
    {
        /// <summary>Gets the number of configured spawn points (canonical: 8).</summary>
        int SpawnPointCount { get; }

        /// <summary>
        /// Selects the optimal spawn pose maximizing minimum squared distance from living players.
        /// </summary>
        /// <param name="fallbackPlayerIndex">Deterministic player index [0..3] used when no living players exist.</param>
        /// <param name="livingPlayerPositions">World positions of all currently living players.</param>
        /// <returns>World-space position and inward-facing rotation.</returns>
        Pose GetSpawnPose(int fallbackPlayerIndex, ReadOnlySpan<Vector3> livingPlayerPositions);
    }
}
