using System;
using UnityEngine;

namespace CapsuleArena.Arena
{
    /// <summary>
    /// Manages the 8 radial perimeter spawn points at R = 16.0m and implements the zero-allocation
    /// Max-Min Euclidean Distance spawn selection algorithm (Story 2-2; ADR-0002; TR-arena-002).
    /// </summary>
    public sealed class ArenaSpawnManager : MonoBehaviour, IArenaSpawnSelector
    {
        /// <summary>Canonical number of perimeter spawn points.</summary>
        public const int CanonicalSpawnCount = 8;

        /// <summary>Canonical radial distance from Arena center (0,0,0) in meters.</summary>
        public const float CanonicalSpawnRadius = 16.0f;

        /// <summary>Canonical vertical ground-clearance offset in meters.</summary>
        public const float CanonicalSpawnElevationY = 0.05f;

        private const float TieBreakEpsilon = 0.0001f;

        [Header("Perimeter Spawn Points (8 @ R = 16.0m)")]
        [Tooltip("Ordered 8 spawn transforms at 45-degree intervals facing (0, 0, 0).")]
        [SerializeField] private Transform[] _spawnPoints = Array.Empty<Transform>();

        private Vector3[] _cachedPositions = Array.Empty<Vector3>();

        /// <inheritdoc />
        public int SpawnPointCount => _cachedPositions.Length > 0 ? _cachedPositions.Length : CanonicalSpawnCount;

        private void Awake()
        {
            RefreshSpawnPointCache();
        }

        /// <summary>
        /// Rebuilds the cached world-space spawn coordinates from serialized Transforms,
        /// falling back to canonical R = 16.0m mathematical coordinates if unpopulated.
        /// </summary>
        public void RefreshSpawnPointCache()
        {
            if (_spawnPoints != null && _spawnPoints.Length > 0)
            {
                if (_cachedPositions.Length != _spawnPoints.Length)
                {
                    _cachedPositions = new Vector3[_spawnPoints.Length];
                }

                for (int i = 0; i < _spawnPoints.Length; i++)
                {
                    _cachedPositions[i] = _spawnPoints[i] != null
                        ? _spawnPoints[i].position
                        : CalculateSingleCanonicalPosition(i, _spawnPoints.Length);
                }

                return;
            }

            _cachedPositions = CalculateCanonicalSpawnPositions();
        }

        /// <summary>
        /// Assigns spawn point transforms (used by scene builder and integration fixtures) and caches positions.
        /// </summary>
        /// <param name="spawnPoints">Ordered spawn point transforms.</param>
        public void SetSpawnPoints(Transform[] spawnPoints)
        {
            _spawnPoints = spawnPoints ?? Array.Empty<Transform>();
            RefreshSpawnPointCache();
        }

        /// <inheritdoc />
        public Pose GetSpawnPose(int fallbackPlayerIndex, ReadOnlySpan<Vector3> livingPlayerPositions)
        {
            if (_cachedPositions == null || _cachedPositions.Length == 0)
            {
                RefreshSpawnPointCache();
            }

            int index = SelectBestSpawnIndex(_cachedPositions, livingPlayerPositions, fallbackPlayerIndex);
            Vector3 position = _cachedPositions[index];
            Quaternion rotation = ComputeInwardLookRotation(position);
            return new Pose(position, rotation);
        }

        /// <summary>
        /// Convenience overload accepting a preallocated array and active count of living player positions.
        /// </summary>
        public static int SelectBestSpawnIndex(
            Vector3[] spawnPositions,
            Vector3[] livingPlayers,
            int livingCount,
            int fallbackPlayerIndex)
        {
            if (spawnPositions == null || spawnPositions.Length == 0)
            {
                return 0;
            }

            int clampedCount = livingPlayers == null ? 0 : Mathf.Clamp(livingCount, 0, livingPlayers.Length);
            ReadOnlySpan<Vector3> livingSpan = livingPlayers == null
                ? ReadOnlySpan<Vector3>.Empty
                : new ReadOnlySpan<Vector3>(livingPlayers, 0, clampedCount);
            return SelectBestSpawnIndex(new ReadOnlySpan<Vector3>(spawnPositions), livingSpan, fallbackPlayerIndex);
        }

        /// <summary>
        /// Selects index i maximizing min_j ||spawnPositions[i] - livingPlayers[j]||^2 with zero GC allocations.
        /// When livingPlayers is empty, maps fallbackPlayerIndex deterministically via (index * 2) mod N.
        /// </summary>
        public static int SelectBestSpawnIndex(
            ReadOnlySpan<Vector3> spawnPositions,
            ReadOnlySpan<Vector3> livingPlayers,
            int fallbackPlayerIndex)
        {
            int spawnCount = spawnPositions.Length;
            if (spawnCount <= 0)
            {
                return 0;
            }

            if (livingPlayers.Length == 0)
            {
                int raw = (fallbackPlayerIndex * 2) % spawnCount;
                return raw < 0 ? raw + spawnCount : raw;
            }

            int bestIndex = 0;
            float bestMinDistSqr = -1.0f;

            for (int i = 0; i < spawnCount; i++)
            {
                float minDistSqr = ComputeMinDistanceSqr(spawnPositions[i], livingPlayers);
                if (minDistSqr > bestMinDistSqr + TieBreakEpsilon)
                {
                    bestMinDistSqr = minDistSqr;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Generates the 8 canonical perimeter spawn positions at 45-degree intervals on R = 16.0m.
        /// </summary>
        public static Vector3[] CalculateCanonicalSpawnPositions(
            float radius = CanonicalSpawnRadius,
            float elevationY = CanonicalSpawnElevationY)
        {
            Vector3[] points = new Vector3[CanonicalSpawnCount];
            for (int i = 0; i < CanonicalSpawnCount; i++)
            {
                points[i] = CalculateSingleCanonicalPosition(i, CanonicalSpawnCount, radius, elevationY);
            }

            return points;
        }

        /// <summary>
        /// Computes a flat yaw rotation looking from the given world position toward Arena center (0, y, 0).
        /// </summary>
        public static Quaternion ComputeInwardLookRotation(Vector3 spawnPosition)
        {
            Vector3 flatToCenter = new Vector3(-spawnPosition.x, 0f, -spawnPosition.z);
            if (flatToCenter.sqrMagnitude < 0.0001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(flatToCenter.normalized, Vector3.up);
        }

        private static Vector3 CalculateSingleCanonicalPosition(
            int index,
            int count,
            float radius = CanonicalSpawnRadius,
            float elevationY = CanonicalSpawnElevationY)
        {
            float angleRad = (index * (360f / Mathf.Max(1, count))) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angleRad) * radius, elevationY, Mathf.Sin(angleRad) * radius);
        }

        private static float ComputeMinDistanceSqr(Vector3 candidate, ReadOnlySpan<Vector3> livingPlayers)
        {
            float minDistSqr = float.MaxValue;
            for (int j = 0; j < livingPlayers.Length; j++)
            {
                float distSqr = (candidate - livingPlayers[j]).sqrMagnitude;
                if (distSqr < minDistSqr)
                {
                    minDistSqr = distSqr;
                }
            }

            return minDistSqr;
        }
    }
}
