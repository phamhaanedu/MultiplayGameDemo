using UnityEngine;

namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Canonical physics layer indices, bitmasks, and collision matrix rules for the Arena (ADR-0001, TR-arena-003).
    /// </summary>
    public static class ArenaLayers
    {
        /// <summary>Layer 6: Perimeter walls, L-walls, and cover pillars blocking living movement, projectiles, and LoS.</summary>
        public const int EnvironmentWall = 6;

        /// <summary>Layer 8: Living player capsules participating in combat and movement collisions.</summary>
        public const int PlayerCharacter = 8;

        /// <summary>Layer 9: Arena floor, central altar, and ramps supporting both living and ghost players.</summary>
        public const int ArenaFloor = 9;

        /// <summary>Layer 10: Eliminated ghost spectator capsules that collide ONLY with Layer 9 (ArenaFloor).</summary>
        public const int GhostSpectator = 10;

        /// <summary>Canonical TagManager name for Layer 6.</summary>
        public const string EnvironmentWallName = "EnvironmentWall";

        /// <summary>Canonical TagManager name for Layer 8.</summary>
        public const string PlayerCharacterName = "PlayerCharacter";

        /// <summary>Canonical TagManager name for Layer 9.</summary>
        public const string ArenaFloorName = "ArenaFloor";

        /// <summary>Canonical TagManager name for Layer 10.</summary>
        public const string GhostSpectatorName = "GhostSpectator";

        /// <summary>Bitmask matching only Layer 6 (EnvironmentWall) for melee and ranged LoS occlusion.</summary>
        public const int LineOfSightMask = 1 << EnvironmentWall;

        /// <summary>Bitmask matching only Layer 6 (EnvironmentWall).</summary>
        public const int EnvironmentWallMask = 1 << EnvironmentWall;

        /// <summary>Bitmask matching only Layer 8 (PlayerCharacter) for hit-detection overlap queries.</summary>
        public const int PlayerCharacterMask = 1 << PlayerCharacter;

        /// <summary>Bitmask matching only Layer 9 (ArenaFloor).</summary>
        public const int ArenaFloorMask = 1 << ArenaFloor;

        /// <summary>Bitmask matching only Layer 10 (GhostSpectator).</summary>
        public const int GhostSpectatorMask = 1 << GhostSpectator;

        /// <summary>Bitmask matching walkable/solid geometry (EnvironmentWall | ArenaFloor).</summary>
        public const int SolidGroundMask = (1 << EnvironmentWall) | (1 << ArenaFloor);

        /// <summary>Bitmask matching valid projectile impact layers (EnvironmentWall | PlayerCharacter).</summary>
        public const int ProjectileCollisionMask = (1 << EnvironmentWall) | (1 << PlayerCharacter);

        /// <summary>
        /// Enforces the canonical 4-layer physics collision matrix prior to scene load so runtime
        /// PlayMode, EditMode tests, and standalone builds share identical collision filtering.
        /// Layer 10 (GhostSpectator) collides ONLY with Layer 9 (ArenaFloor) and ignores Layers 6, 8, and 10.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyCollisionMatrix()
        {
            // EnvironmentWall (6) blocks living players, floor contacts, and walls, but ignores GhostSpectator (10).
            Physics.IgnoreLayerCollision(EnvironmentWall, EnvironmentWall, false);
            Physics.IgnoreLayerCollision(EnvironmentWall, PlayerCharacter, false);
            Physics.IgnoreLayerCollision(EnvironmentWall, ArenaFloor, false);
            Physics.IgnoreLayerCollision(EnvironmentWall, GhostSpectator, true);

            // Living PlayerCharacter (8) collides with living players and floor, NOT GhostSpectator (10).
            Physics.IgnoreLayerCollision(PlayerCharacter, PlayerCharacter, false);
            Physics.IgnoreLayerCollision(PlayerCharacter, ArenaFloor, false);
            Physics.IgnoreLayerCollision(PlayerCharacter, GhostSpectator, true);

            // ArenaFloor (9) supports both living players and ghost spectators.
            Physics.IgnoreLayerCollision(ArenaFloor, ArenaFloor, false);
            Physics.IgnoreLayerCollision(ArenaFloor, GhostSpectator, false);

            // GhostSpectator (10) passes through other ghosts, walls, and living players.
            Physics.IgnoreLayerCollision(GhostSpectator, GhostSpectator, true);
        }

        /// <summary>
        /// Performs a zero-allocation linecast against Layer 6 (EnvironmentWall) to verify
        /// unobstructed line-of-sight between attacker and target chest centers.
        /// </summary>
        /// <param name="origin">World-space ray start position.</param>
        /// <param name="target">World-space ray end position.</param>
        /// <returns>True when no EnvironmentWall collider obstructs the segment; otherwise false.</returns>
        public static bool IsLineOfSightClear(Vector3 origin, Vector3 target)
        {
            return !Physics.Linecast(origin, target, LineOfSightMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Recursively assigns a physics layer to a root GameObject and all of its child transforms.
        /// </summary>
        /// <param name="root">Root GameObject to update.</param>
        /// <param name="layer">Target Unity layer index (0..31).</param>
        public static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            root.layer = layer;
            Transform rootTransform = root.transform;
            int childCount = rootTransform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                SetLayerRecursively(rootTransform.GetChild(i).gameObject, layer);
            }
        }
    }
}
