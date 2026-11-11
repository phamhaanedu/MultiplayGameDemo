using CapsuleArena.Core.Config;
using UnityEngine;

namespace CapsuleArena.Combat
{
    /// <summary>
    /// Identifier distinguishing the three melee swing arcs for math, HUD indicators, and URP slash VFX.
    /// </summary>
    public enum MeleeSwingKind : byte
    {
        /// <summary>LMB: 50-deg frontal cone (bisector 0 deg, 2.5m, 25 dmg).</summary>
        VerticalSlash = 0,

        /// <summary>Key 1: 90-deg left-quadrant sweep [-90 deg, 0 deg] (bisector -45 deg, 3.0m, 30 dmg).</summary>
        HorizontalSweepLeft = 1,

        /// <summary>Key 3: 90-deg right-quadrant sweep [0 deg, +90 deg] (bisector +45 deg, 3.0m, 30 dmg).</summary>
        HorizontalSweepRight = 2
    }

    /// <summary>
    /// Zero-allocation static geometry and Line-of-Sight helpers for multidirectional melee hit evaluation
    /// and Horizontal Sweep 90-degree snap-turn stepping (Stories 7-1, 7-2; ADR-0005; TR-combat-001, TR-combat-002).
    /// </summary>
    public static class MeleeSectorMath
    {
        /// <summary>Canonical chest elevation offset above capsule base for Line-of-Sight checks (1.0m).</summary>
        public const float ChestHeightOffsetMeters = 1.0f;

        /// <summary>Canonical muzzle elevation offset above capsule base for ranged bolts (1.1m).</summary>
        public const float MuzzleHeightOffsetMeters = 1.1f;

        /// <summary>Canonical forward muzzle offset ahead of capsule center (0.65m).</summary>
        public const float MuzzleForwardOffsetMeters = 0.65f;

        /// <summary>Maximum vertical center-to-center elevation delta allowed for planar melee hits (2.2m).</summary>
        public const float MaxMeleeVerticalDeltaMeters = 2.2f;

        private const float CosineTolerance = 0.00025f;

        /// <summary>
        /// Computes the normalized XZ-planar sector bisector rotated by <paramref name="yawOffsetDegrees"/>
        /// relative to <paramref name="attackerForward"/>.
        /// </summary>
        public static Vector3 ComputePlanarBisector(Vector3 attackerForward, float yawOffsetDegrees)
        {
            Vector3 flatForward = new Vector3(attackerForward.x, 0f, attackerForward.z);
            if (flatForward.sqrMagnitude < 0.0001f)
            {
                flatForward = Vector3.forward;
            }
            else
            {
                flatForward.Normalize();
            }

            if (Mathf.Abs(yawOffsetDegrees) < 0.0001f)
            {
                return flatForward;
            }

            float rad = yawOffsetDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            float cos = Mathf.Cos(rad);
            return new Vector3(
                flatForward.x * cos + flatForward.z * sin,
                0f,
                -flatForward.x * sin + flatForward.z * cos).normalized;
        }

        /// <summary>
        /// Evaluates whether <paramref name="targetPosition"/> lies within the XZ circular sector defined by
        /// <paramref name="maxRange"/>, precomputed <paramref name="cosHalfAngle"/>, and <paramref name="bisectorYawOffsetDeg"/>.
        /// Allocates 0 B and avoids <c>Mathf.Acos</c> / <c>Vector3.Angle</c>.
        /// </summary>
        public static bool IsTargetInsideSector(
            Vector3 attackerPosition,
            Vector3 attackerForward,
            Vector3 targetPosition,
            float maxRange,
            float cosHalfAngle,
            float bisectorYawOffsetDeg = 0f,
            float maxVerticalDelta = MaxMeleeVerticalDeltaMeters)
        {
            if (maxRange <= 0f || Mathf.Abs(targetPosition.y - attackerPosition.y) > maxVerticalDelta)
            {
                return false;
            }

            float dx = targetPosition.x - attackerPosition.x;
            float dz = targetPosition.z - attackerPosition.z;
            float distSqr = dx * dx + dz * dz;
            if (distSqr > maxRange * maxRange)
            {
                return false;
            }

            if (distSqr <= 0.0001f)
            {
                return true;
            }

            float invDist = 1.0f / Mathf.Sqrt(distSqr);
            Vector3 planarToTarget = new Vector3(dx * invDist, 0f, dz * invDist);
            Vector3 bisector = ComputePlanarBisector(attackerForward, bisectorYawOffsetDeg);
            float dot = planarToTarget.x * bisector.x + planarToTarget.z * bisector.z;
            return dot >= (cosHalfAngle - CosineTolerance);
        }

        /// <summary>
        /// Returns true when no <see cref="ArenaLayers.EnvironmentWall"/> obstacle blocks the chest-to-chest segment.
        /// </summary>
        public static bool HasClearLineOfSight(Vector3 attackerPosition, Vector3 targetPosition, int lineOfSightMask = 0)
        {
            int mask = lineOfSightMask != 0 ? lineOfSightMask : ArenaLayers.LineOfSightMask;
            Vector3 start = attackerPosition + Vector3.up * ChestHeightOffsetMeters;
            Vector3 end = targetPosition + Vector3.up * ChestHeightOffsetMeters;
            return !Physics.Linecast(start, end, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Probes from chest center to forward muzzle position so flush-against-wall shots never spawn behind a thin wall.
        /// </summary>
        public static bool IsMuzzleBlockedByWall(
            Vector3 shooterPosition,
            Vector3 muzzlePosition,
            out RaycastHit wallHit,
            int lineOfSightMask = 0)
        {
            int mask = lineOfSightMask != 0 ? lineOfSightMask : ArenaLayers.LineOfSightMask;
            Vector3 chestStart = new Vector3(shooterPosition.x, muzzlePosition.y, shooterPosition.z);
            return Physics.Linecast(chestStart, muzzlePosition, out wallHit, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Computes the exact authoritative yaw at tick <paramref name="elapsedTicks"/> of <paramref name="totalTicks"/>
        /// during a Horizontal Sweep snap-turn (<c>-90 deg</c> left or <c>+90 deg</c> right), snapping cleanly on the final tick.
        /// </summary>
        public static float EvaluateSweepTurnYaw(
            float startYawDeg,
            float signedTotalTurnDeg,
            int elapsedTicks,
            int totalTicks)
        {
            if (totalTicks <= 0 || elapsedTicks >= totalTicks)
            {
                return NormalizeAngle180(startYawDeg + signedTotalTurnDeg);
            }

            if (elapsedTicks <= 0)
            {
                return NormalizeAngle180(startYawDeg);
            }

            float fraction = (float)elapsedTicks / totalTicks;
            return NormalizeAngle180(startYawDeg + signedTotalTurnDeg * fraction);
        }

        /// <summary>Normalizes any degree angle into <c>[-180, +180)</c>.</summary>
        public static float NormalizeAngle180(float angleDeg)
        {
            if (float.IsNaN(angleDeg) || float.IsInfinity(angleDeg))
            {
                return 0f;
            }

            return Mathf.Repeat(angleDeg + 180f, 360f) - 180f;
        }
    }
}
