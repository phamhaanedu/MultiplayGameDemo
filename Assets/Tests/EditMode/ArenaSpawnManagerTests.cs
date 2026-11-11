using System;
using CapsuleArena.Arena;
using NUnit.Framework;
using UnityEngine;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying Max-Min Euclidean Spawn selection, deterministic tie-breaking,
    /// empty-arena fallback, inward rotation, MonoBehaviour GetSpawnPose instance behavior,
    /// and zero-GC hot-path execution (Story 2-2; TR-arena-002).
    /// </summary>
    [TestFixture]
    public class ArenaSpawnManagerTests
    {
        private Vector3[] _canonicalSpawns;

        [SetUp]
        public void SetUp()
        {
            _canonicalSpawns = ArenaSpawnManager.CalculateCanonicalSpawnPositions();
        }

        [Test]
        public void CalculateCanonicalSpawnPositions_DefaultRadius_Produces8RadialPointsAt16Meters()
        {
            Assert.AreEqual(8, _canonicalSpawns.Length);
            for (int i = 0; i < _canonicalSpawns.Length; i++)
            {
                Vector2 flat = new Vector2(_canonicalSpawns[i].x, _canonicalSpawns[i].z);
                Assert.AreEqual(16.0f, flat.magnitude, 0.001f);
                Assert.AreEqual(0.05f, _canonicalSpawns[i].y, 0.0001f);
            }
        }

        [Test]
        public void SelectBestSpawnIndex_ThreeClusteredOpponents_SelectsOppositeSpawnIndex4()
        {
            Vector3[] living =
            {
                _canonicalSpawns[0],
                _canonicalSpawns[1],
                _canonicalSpawns[7]
            };

            int chosen = ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, living, fallbackPlayerIndex: 0);
            Assert.AreEqual(4, chosen, "Opposite perimeter spawn (180 deg, index 4) maximizes minimum Euclidean distance.");
        }

        [Test]
        public void GetSpawnPose_InstanceWithCustomSpawnTransforms_SelectsFurthestPoseFacingOrigin()
        {
            GameObject go = new GameObject("ArenaSpawnManager_InstanceTest");
            Transform[] markers = new Transform[8];
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    GameObject m = new GameObject($"Spawn_{i}");
                    m.transform.SetParent(go.transform, false);
                    m.transform.position = _canonicalSpawns[i];
                    markers[i] = m.transform;
                }

                ArenaSpawnManager mgr = go.AddComponent<ArenaSpawnManager>();
                mgr.SetSpawnPoints(markers);
                Assert.AreEqual(8, mgr.SpawnPointCount);

                Vector3[] eastCluster = { _canonicalSpawns[0], _canonicalSpawns[1], _canonicalSpawns[7] };
                Pose pose = mgr.GetSpawnPose(0, new ReadOnlySpan<Vector3>(eastCluster));

                Assert.AreEqual(_canonicalSpawns[4].x, pose.position.x, 0.01f);
                Assert.AreEqual(_canonicalSpawns[4].z, pose.position.z, 0.01f);
                Vector3 forward = pose.rotation * Vector3.forward;
                Assert.AreEqual(1f, forward.x, 0.001f, "West spawn (-16, 0, 0) must face +X toward Arena center.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SelectBestSpawnIndex_ZeroLivingPlayers_UsesDeterministicEvenIndexFallback()
        {
            ReadOnlySpan<Vector3> empty = ReadOnlySpan<Vector3>.Empty;

            Assert.AreEqual(0, ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, empty, 0));
            Assert.AreEqual(2, ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, empty, 1));
            Assert.AreEqual(4, ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, empty, 2));
            Assert.AreEqual(6, ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, empty, 3));
            Assert.AreEqual(6, ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, empty, -1));
        }

        [Test]
        public void SelectBestSpawnIndex_EquidistantOpponents_BreaksTiesDeterministicallyByLowestIndex()
        {
            Vector3[] centerPlayer = { new Vector3(0f, 0.05f, 0f) };
            int chosen = ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, centerPlayer, fallbackPlayerIndex: 3);
            Assert.AreEqual(0, chosen, "Tie-break must deterministically select lowest index 0.");
        }

        [Test]
        public void SelectBestSpawnIndex_EmptySpawnArray_ReturnsZeroSafely()
        {
            int chosen = ArenaSpawnManager.SelectBestSpawnIndex(
                ReadOnlySpan<Vector3>.Empty,
                ReadOnlySpan<Vector3>.Empty,
                fallbackPlayerIndex: 2);
            Assert.AreEqual(0, chosen);
        }

        [Test]
        public void ComputeInwardLookRotation_PerimeterPoint_FacesArenaOrigin()
        {
            Vector3 eastSpawn = new Vector3(16f, 0.05f, 0f);
            Quaternion rot = ArenaSpawnManager.ComputeInwardLookRotation(eastSpawn);
            Vector3 forward = rot * Vector3.forward;

            Assert.AreEqual(-1f, forward.x, 0.001f);
            Assert.AreEqual(0f, forward.y, 0.001f);
            Assert.AreEqual(0f, forward.z, 0.001f);
        }

        [Test]
        public void SelectBestSpawnIndex_OneThousandIterations_AllocatesZeroManagedBytes()
        {
            Vector3[] living = { _canonicalSpawns[0], _canonicalSpawns[2], _canonicalSpawns[5] };
            int sink = ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, living, 3, 0);

            GC.Collect();
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
            {
                sink += ArenaSpawnManager.SelectBestSpawnIndex(_canonicalSpawns, living, 3, i);
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            Assert.AreEqual(0L, allocatedBytes, $"Expected 0B GC allocation across 1000 calls, got {allocatedBytes}B (sink={sink}).");
        }
    }
}
