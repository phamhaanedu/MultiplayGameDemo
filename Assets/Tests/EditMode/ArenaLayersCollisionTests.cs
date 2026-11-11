using CapsuleArena.Arena;
using CapsuleArena.Core.Config;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode integration tests verifying the 4-layer physics collision matrix (including GhostSpectator
    /// ignoring EnvironmentWall so Layer 10 collides ONLY with Layer 9 ArenaFloor), LineOfSightMask
    /// raycast occlusion, and Arena.unity courtyard structural/clearance invariants
    /// (Stories 2-1, 2-3; ADR-0001; TR-arena-001, TR-arena-003).
    /// </summary>
    [TestFixture]
    public class ArenaLayersCollisionTests
    {
        [SetUp]
        public void SetUp()
        {
            ArenaLayers.ApplyCollisionMatrix();
        }

        [Test]
        public void ApplyCollisionMatrix_EnforcesGhostCollidingOnlyWithArenaFloorAndLivingPlayerFullCollisions()
        {
            Assert.AreEqual(1 << 6, ArenaLayers.LineOfSightMask);

            // GhostSpectator (10) ignores EnvironmentWall (6), PlayerCharacter (8), and GhostSpectator (10).
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(ArenaLayers.GhostSpectator, ArenaLayers.EnvironmentWall));
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(ArenaLayers.GhostSpectator, ArenaLayers.PlayerCharacter));
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(ArenaLayers.GhostSpectator, ArenaLayers.GhostSpectator));

            // GhostSpectator (10) collides ONLY with ArenaFloor (9) so spectators walk without falling.
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(ArenaLayers.GhostSpectator, ArenaLayers.ArenaFloor));

            // PlayerCharacter (8) collides with EnvironmentWall (6), PlayerCharacter (8), and ArenaFloor (9).
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(ArenaLayers.PlayerCharacter, ArenaLayers.EnvironmentWall));
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(ArenaLayers.PlayerCharacter, ArenaLayers.PlayerCharacter));
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(ArenaLayers.PlayerCharacter, ArenaLayers.ArenaFloor));
        }

        [Test]
        public void IsLineOfSightClear_BlockedByWallAndLCornerGrazing_IgnoresPlayersGhostsAndFloor()
        {
            GameObject wallA = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject wallB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            GameObject livingPlayer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                // L-shaped corner at origin on Layer 6:
                wallA.layer = ArenaLayers.EnvironmentWall;
                wallA.transform.position = new Vector3(0f, 1.25f, 0f);
                wallA.transform.localScale = new Vector3(3f, 2.5f, 0.5f);

                wallB.layer = ArenaLayers.EnvironmentWall;
                wallB.transform.position = new Vector3(1.25f, 1.25f, 1.25f);
                wallB.transform.localScale = new Vector3(0.5f, 2.5f, 3f);

                ghost.layer = ArenaLayers.GhostSpectator;
                ghost.transform.position = new Vector3(8f, 1.0f, 0f);

                livingPlayer.layer = ArenaLayers.PlayerCharacter;
                livingPlayer.transform.position = new Vector3(12f, 1.0f, 0f);

                Physics.SyncTransforms();

                // Blocked directly by Wall A and diagonally across the L-corner seam:
                Assert.IsFalse(ArenaLayers.IsLineOfSightClear(new Vector3(0f, 1.0f, -3f), new Vector3(0f, 1.0f, 3f)));
                Assert.IsFalse(ArenaLayers.IsLineOfSightClear(new Vector3(0.5f, 1.0f, -1.5f), new Vector3(2.5f, 1.0f, 1.5f)));

                // Clear around the outside of the L-wall:
                Assert.IsTrue(ArenaLayers.IsLineOfSightClear(new Vector3(-3.0f, 1.0f, -2.0f), new Vector3(-3.0f, 1.0f, 3.0f)));

                // Segment passing through Layer 10 GhostSpectator or Layer 8 PlayerCharacter must NOT be blocked by LOS mask:
                Assert.IsTrue(ArenaLayers.IsLineOfSightClear(new Vector3(8f, 1.0f, -3f), new Vector3(8f, 1.0f, 3f)));
                Assert.IsTrue(ArenaLayers.IsLineOfSightClear(new Vector3(12f, 1.0f, -3f), new Vector3(12f, 1.0f, 3f)));
            }
            finally
            {
                Object.DestroyImmediate(wallA);
                Object.DestroyImmediate(wallB);
                Object.DestroyImmediate(ghost);
                Object.DestroyImmediate(livingPlayer);
            }
        }

        [Test]
        public void ArenaScene_LoadedInEditMode_PassesAllCourtyardClearanceAndSpawnInvariants()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            string previousPath = previousScene.path;

            Scene arenaScene = EditorSceneManager.OpenScene("Assets/Scenes/Arena.unity", OpenSceneMode.Single);
            try
            {
                Assert.IsTrue(arenaScene.IsValid(), "Assets/Scenes/Arena.unity must exist and open cleanly.");

                GameObject arenaRoot = GameObject.Find("ArenaCourtyard");
                Assert.IsNotNull(arenaRoot, "Expected root 'ArenaCourtyard' GameObject in Arena.unity.");

                bool valid = ArenaLayoutValidator.ValidateArenaHierarchy(arenaRoot, out string failureReason);
                Assert.IsTrue(valid, $"Arena hierarchy validation failed: {failureReason}");

                ArenaSpawnManager spawnManager = Object.FindAnyObjectByType<ArenaSpawnManager>();
                Assert.IsNotNull(spawnManager, "Expected ArenaSpawnManager in Arena.unity.");
                Assert.AreEqual(8, spawnManager.SpawnPointCount);
            }
            finally
            {
                // Restore previous scene or clean empty test scene so Arena colliders never pollute unrelated unit tests.
                if (!string.IsNullOrEmpty(previousPath) && previousPath != "Assets/Scenes/Arena.unity")
                {
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
                }
                else if (string.IsNullOrEmpty(previousPath))
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }
    }
}
