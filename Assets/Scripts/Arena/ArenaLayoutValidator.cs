using System.Collections.Generic;
using CapsuleArena.Core.Config;
using UnityEngine;

namespace CapsuleArena.Arena
{
    /// <summary>
    /// Validates structural geometry, physics layer assignments, zero MeshCollider usage,
    /// minimum 2.5m cover heights, and minimum 2.5m passage clearances for the 40m x 40m Arena courtyard
    /// (Story 2-1; ADR-0001; TR-arena-001).
    /// </summary>
    public static class ArenaLayoutValidator
    {
        /// <summary>Canonical playable courtyard width and length in meters.</summary>
        public const float CourtyardSizeMeters = 40.0f;

        /// <summary>Minimum wall and pillar cover height in meters to prevent jump-escapes.</summary>
        public const float MinCoverHeightMeters = 2.5f;

        /// <summary>Minimum horizontal gap between distinct tactical obstacles so capsules never wedge.</summary>
        public const float MinPassageClearanceMeters = 2.5f;

        /// <summary>
        /// Inspects a root Arena hierarchy and reports whether all Story 2-1 structural invariants hold.
        /// </summary>
        public static bool ValidateArenaHierarchy(GameObject arenaRoot, out string failureReason)
        {
            if (arenaRoot == null)
            {
                failureReason = "Arena root GameObject is null.";
                return false;
            }

            if (arenaRoot.GetComponentsInChildren<MeshCollider>(includeInactive: true).Length > 0)
            {
                failureReason = "Forbidden MeshCollider detected inside Arena hierarchy (ADR-0001 requires primitive colliders).";
                return false;
            }

            if (!ValidateFloorAndAltar(arenaRoot.transform, out failureReason))
            {
                return false;
            }

            return ValidateWallsPillarsAndClearances(arenaRoot.transform, out failureReason);
        }

        private static bool ValidateFloorAndAltar(Transform root, out string failureReason)
        {
            Transform floor = root.Find("ArenaFloor");
            if (floor == null || floor.gameObject.layer != ArenaLayers.ArenaFloor)
            {
                failureReason = "Missing 'ArenaFloor' child or layer is not Layer 9 (ArenaFloor).";
                return false;
            }

            Collider floorCollider = floor.GetComponent<Collider>();
            if (floorCollider == null ||
                floorCollider.bounds.size.x < CourtyardSizeMeters - 0.1f ||
                floorCollider.bounds.size.z < CourtyardSizeMeters - 0.1f)
            {
                failureReason = "ArenaFloor collider bounds are smaller than 40m x 40m.";
                return false;
            }

            Transform altar = root.Find("CentralAltar");
            if (altar == null || altar.gameObject.layer != ArenaLayers.ArenaFloor || altar.childCount < 4)
            {
                failureReason = "Missing 'CentralAltar' on Layer 9 (ArenaFloor) or missing 4 sloped access ramps.";
                return false;
            }

            Collider altarCollider = altar.GetComponent<Collider>();
            if (altarCollider == null || altarCollider.bounds.size.x < 5.9f || altarCollider.bounds.size.z < 5.9f)
            {
                failureReason = "CentralAltar top platform must span at least 6m x 6m.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private static bool ValidateWallsPillarsAndClearances(Transform root, out string failureReason)
        {
            Transform wallsRoot = root.Find("PerimeterWalls");
            Transform lWallsRoot = root.Find("LShapedWalls");
            Transform pillarsRoot = root.Find("CoverPillars");

            if (wallsRoot == null || wallsRoot.childCount < 4 ||
                lWallsRoot == null || lWallsRoot.childCount < 4 ||
                pillarsRoot == null || pillarsRoot.childCount < 8)
            {
                failureReason = "Expected PerimeterWalls (4), LShapedWalls (4), and CoverPillars (8) containers.";
                return false;
            }

            if (!ValidateEnvironmentWallGroup(wallsRoot, out failureReason) ||
                !ValidateEnvironmentWallGroup(lWallsRoot, out failureReason) ||
                !ValidateEnvironmentWallGroup(pillarsRoot, out failureReason))
            {
                return false;
            }

            return ValidateInteriorObstacleClearances(root, lWallsRoot, pillarsRoot, out failureReason);
        }

        private static bool ValidateEnvironmentWallGroup(Transform groupRoot, out string failureReason)
        {
            Collider[] colliders = groupRoot.GetComponentsInChildren<Collider>(includeInactive: false);
            if (colliders.Length == 0)
            {
                failureReason = $"Obstacle group '{groupRoot.name}' contains zero active colliders.";
                return false;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col.gameObject.layer != ArenaLayers.EnvironmentWall)
                {
                    failureReason = $"Obstacle '{col.name}' must be assigned to Layer 6 (EnvironmentWall).";
                    return false;
                }

                if (col.bounds.size.y < MinCoverHeightMeters - 0.05f)
                {
                    failureReason = $"Obstacle '{col.name}' height ({col.bounds.size.y:F2}m) is below {MinCoverHeightMeters:F2}m.";
                    return false;
                }
            }

            failureReason = string.Empty;
            return true;
        }

        private static bool ValidateInteriorObstacleClearances(
            Transform arenaRoot,
            Transform lWallsRoot,
            Transform pillarsRoot,
            out string failureReason)
        {
            List<Bounds> obstacleGroups = CollectObstacleGroupBounds(arenaRoot, lWallsRoot, pillarsRoot);
            float minGap = MinPassageClearanceMeters - 0.05f;

            for (int i = 0; i < obstacleGroups.Count; i++)
            {
                Bounds bA = obstacleGroups[i];
                float perimeterGap = ComputeMinPerimeterWallGap(bA);
                if (perimeterGap < minGap)
                {
                    failureReason = $"Obstacle #{i} lies within {perimeterGap:F2}m of perimeter wall (< {MinPassageClearanceMeters:F2}m).";
                    return false;
                }

                for (int j = i + 1; j < obstacleGroups.Count; j++)
                {
                    float pairGap = ComputeHorizontalAabbGap(bA, obstacleGroups[j]);
                    if (pairGap < minGap)
                    {
                        failureReason = $"Corridor gap ({pairGap:F2}m) between obstacles #{i} and #{j} violates W_gap >= {MinPassageClearanceMeters:F2}m.";
                        return false;
                    }
                }
            }

            failureReason = string.Empty;
            return true;
        }

        private static List<Bounds> CollectObstacleGroupBounds(Transform arenaRoot, Transform lWallsRoot, Transform pillarsRoot)
        {
            List<Bounds> boundsList = new List<Bounds>(16);
            Transform altar = arenaRoot.Find("CentralAltar");
            if (altar != null && altar.TryGetComponent(out Collider altarCol))
            {
                boundsList.Add(altarCol.bounds);
            }

            for (int i = 0; i < lWallsRoot.childCount; i++)
            {
                Collider[] segs = lWallsRoot.GetChild(i).GetComponentsInChildren<Collider>();
                if (segs.Length > 0)
                {
                    Bounds combined = segs[0].bounds;
                    for (int k = 1; k < segs.Length; k++) combined.Encapsulate(segs[k].bounds);
                    boundsList.Add(combined);
                }
            }

            for (int i = 0; i < pillarsRoot.childCount; i++)
            {
                if (pillarsRoot.GetChild(i).TryGetComponent(out Collider pillarCol))
                {
                    boundsList.Add(pillarCol.bounds);
                }
            }

            return boundsList;
        }

        private static float ComputeMinPerimeterWallGap(Bounds obstacle)
        {
            float halfCourtyard = CourtyardSizeMeters * 0.5f;
            float gapEast = halfCourtyard - obstacle.max.x;
            float gapWest = obstacle.min.x - (-halfCourtyard);
            float gapNorth = halfCourtyard - obstacle.max.z;
            float gapSouth = obstacle.min.z - (-halfCourtyard);
            return Mathf.Min(Mathf.Min(gapEast, gapWest), Mathf.Min(gapNorth, gapSouth));
        }

        private static float ComputeHorizontalAabbGap(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
