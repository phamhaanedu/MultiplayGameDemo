using UnityEngine;

namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Read-only contract supplying color, trajectory, two-phase scale (<c>2/3</c> enlarge, <c>1/3</c> shrink),
    /// fade-out, and object pool parameters for overhead floating damage and heal numbers.
    /// </summary>
    public interface IFloatingDamageTextConfig
    {
        /// <summary>Gets the text color used when HP is lost (default: vibrant red).</summary>
        Color DamageTextColor { get; }

        /// <summary>Gets the text color used when HP is gained, e.g., health potions (default: healthy green).</summary>
        Color HealTextColor { get; }

        /// <summary>Gets the total upward flight lifetime in seconds before disappearing.</summary>
        float LifetimeSeconds { get; }

        /// <summary>Gets the normalized ratio of flight spent enlarging before shrinking (canonical: 2/3 = 0.6667).</summary>
        float GrowPhaseNormalizedRatio { get; }

        /// <summary>Gets the vertical spawn offset above the capsule root origin in meters (above head &amp; nameplate).</summary>
        float SpawnHeightOffsetMeters { get; }

        /// <summary>Gets the total vertical rise distance in meters over the flight lifetime.</summary>
        float RiseDistanceMeters { get; }

        /// <summary>Gets the horizontal alternating spread offset in meters so rapid hits remain legible.</summary>
        float HorizontalSpreadMeters { get; }

        /// <summary>Gets the initial scale multiplier at t = 0.</summary>
        float StartScale { get; }

        /// <summary>Gets the peak enlarged scale multiplier reached at t = GrowPhaseNormalizedRatio (2/3).</summary>
        float PeakScale { get; }

        /// <summary>Gets the final shrunken scale multiplier reached at t = 1.0.</summary>
        float EndScale { get; }

        /// <summary>Gets the intermediate alpha at t = GrowPhaseNormalizedRatio before rapid final fade-out to 0.</summary>
        float AlphaAtPeak { get; }

        /// <summary>Gets the base TextMeshPro font size.</summary>
        float FontSize { get; }

        /// <summary>Gets the number of pre-warmed pooled world-space floating text instances.</summary>
        int PoolSize { get; }
    }
}
