using System.Collections.Generic;
using CapsuleArena.Combat;
using CapsuleArena.Combat.Projectile;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace CapsuleArena.Presentation.VFX
{
    /// <summary>
    /// Pre-warms <c>20</c> pooled URP combat VFX instances (<c>8</c> SlashArc, <c>8</c> ImpactSpark,
    /// <c>4</c> MobilityAndDeathBurst) matching exact <see cref="MeleeSectorMath"/> hit cones
    /// (<c>50 deg / 2.5m</c> <c>#00F5D4</c> vertical blade fan; <c>90 deg / 3.0m</c> <c>#FF9F1C</c> horizontal sweep fan)
    /// with <c>0</c> runtime <c>Instantiate</c>/<c>Destroy</c> allocations and deterministic native asset cleanup
    /// (Story 14-2; ADR-0008; TR-audio-002).
    /// </summary>
    public sealed class VfxManager : MonoBehaviour
    {
        /// <summary>Total pre-warmed VFX instances across all pools (20).</summary>
        public const int TotalPrewarmedInstances = 20;

        /// <summary>Pre-warmed SlashArc instances (8).</summary>
        public const int SlashArcPoolSize = 8;

        /// <summary>Pre-warmed ImpactSpark instances (8).</summary>
        public const int ImpactSparkPoolSize = 8;

        /// <summary>Pre-warmed MobilityAndDeathBurst instances (4).</summary>
        public const int BurstPoolSize = 4;

        /// <summary>Spark burst count emitted on living player hit (14).</summary>
        public const int PlayerHitSparkCount = 14;

        /// <summary>Spark burst count emitted on Layer 6 wall impact (8).</summary>
        public const int WallImpactSparkCount = 8;

        /// <summary>Vertical Slash Electric Cyan (#00F5D4).</summary>
        public static readonly Color VerticalSlashCyan = new Color32(0, 245, 212, 255);

        /// <summary>Horizontal Sweep Solar Amber (#FF9F1C).</summary>
        public static readonly Color HorizontalSweepAmber = new Color32(255, 159, 28, 255);

        /// <summary>Player impact spark gold (#FFB703).</summary>
        public static readonly Color PlayerHitSparkGold = new Color32(255, 183, 3, 255);

        /// <summary>Wall impact spark crisp ice white (#E2E8F0).</summary>
        public static readonly Color WallSparkWhite = new Color32(226, 232, 240, 255);

        /// <summary>Default Horizontal Sweep angular turn duration in seconds when not explicitly supplied (0.18s).</summary>
        public const float DefaultHorizontalSweepDurationSec = 0.18f;

        /// <summary>Short afterimage fade duration in seconds once the full 90-deg fan and capsule turn complete (0.06s).</summary>
        public const float SweepFadeTailDurationSec = 0.06f;

        [Header("Optional Prebuilt Assets")]
        [Tooltip("Shared URP SlashSectorArc material asset ensuring shader inclusion in Standalone builds.")]
        [SerializeField] private Material _slashArcMaterialAsset;

        private readonly Queue<PooledVfxInstance> _slashPool = new Queue<PooledVfxInstance>(SlashArcPoolSize);
        private readonly Queue<PooledVfxInstance> _sparkPool = new Queue<PooledVfxInstance>(ImpactSparkPoolSize);
        private readonly Queue<PooledVfxInstance> _burstPool = new Queue<PooledVfxInstance>(BurstPoolSize);
        private readonly List<PooledVfxInstance> _activeInstances = new List<PooledVfxInstance>(TotalPrewarmedInstances);
        private readonly List<PooledVfxInstance> _allPrewarmed = new List<PooledVfxInstance>(TotalPrewarmedInstances);

        private Mesh _verticalSlashMesh;
        private Mesh _horizontalLeftMesh;
        private Mesh _horizontalRightMesh;
        private Material _sharedArcMaterial;
        private Material _sharedParticleMaterial;
        private bool _ownsArcMaterial;
        private bool _isInitialized;

        /// <summary>Gets all 20 pre-warmed VFX instances for inspection and EditMode assertions.</summary>
        public IReadOnlyList<PooledVfxInstance> AllPrewarmedInstances
        {
            get
            {
                EnsurePoolInitialized();
                return _allPrewarmed;
            }
        }

        /// <summary>Gets the count of currently active world-space VFX instances.</summary>
        public int ActiveInstanceCount => _activeInstances.Count;

        /// <summary>Gets the number of idle SlashArc instances currently available in the pool.</summary>
        public int IdleSlashArcCount => _slashPool.Count;

        /// <summary>Gets the number of idle ImpactSpark instances currently available in the pool.</summary>
        public int IdleSparkCount => _sparkPool.Count;

        /// <summary>Gets the number of idle MobilityAndDeathBurst instances currently available in the pool.</summary>
        public int IdleBurstCount => _burstPool.Count;

        private void Awake()
        {
            EnsurePoolInitialized();
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        /// <summary>Subscribes idempotently to global combat, projectile, health, and mobility events.</summary>
        public void SubscribeEvents()
        {
            UnsubscribeEvents();
            PlayerCombatController.OnAnyMeleeSwingExecuted += HandleMeleeSwing;
            PlayerCombatController.OnAnyRangedShotExecuted += HandleRangedShot;
            NetworkProjectile.OnAnyProjectileImpact += HandleProjectileImpact;
            PlayerHealth.OnAnyHitFeedbackBroadcast += HandleHitFeedback;
            PlayerMobilityController.OnAnyMobilityBurstStarted += HandleMobilityBurst;
        }

        /// <summary>Unsubscribes from global combat, projectile, health, and mobility events.</summary>
        public void UnsubscribeEvents()
        {
            PlayerCombatController.OnAnyMeleeSwingExecuted -= HandleMeleeSwing;
            PlayerCombatController.OnAnyRangedShotExecuted -= HandleRangedShot;
            NetworkProjectile.OnAnyProjectileImpact -= HandleProjectileImpact;
            PlayerHealth.OnAnyHitFeedbackBroadcast -= HandleHitFeedback;
            PlayerMobilityController.OnAnyMobilityBurstStarted -= HandleMobilityBurst;
        }

        private void OnDestroy()
        {
            DestroyGeneratedAsset(_verticalSlashMesh);
            DestroyGeneratedAsset(_horizontalLeftMesh);
            DestroyGeneratedAsset(_horizontalRightMesh);
            if (_ownsArcMaterial)
            {
                DestroyGeneratedAsset(_sharedArcMaterial);
            }

            DestroyGeneratedAsset(_sharedParticleMaterial);
            _verticalSlashMesh = null;
            _horizontalLeftMesh = null;
            _horizontalRightMesh = null;
            _sharedArcMaterial = null;
            _sharedParticleMaterial = null;
            _ownsArcMaterial = false;
        }

        private void Update()
        {
            TickActiveVfx(Time.deltaTime);
        }

        private void LateUpdate()
        {
            for (int i = 0; i < _activeInstances.Count; i++)
            {
                if (_activeInstances[i] != null)
                {
                    _activeInstances[i].SyncFollowPosition();
                }
            }
        }

        /// <summary>Pre-warms all 20 pooled VFX instances and sector meshes idempotently.</summary>
        public void EnsurePoolInitialized()
        {
            if (_isInitialized && _allPrewarmed.Count == TotalPrewarmedInstances)
            {
                return;
            }

            _isInitialized = true;
            BuildSharedMaterialsAndMeshes();
            PrewarmCategory(VfxPoolCategory.SlashArc, SlashArcPoolSize, _slashPool);
            PrewarmCategory(VfxPoolCategory.ImpactSpark, ImpactSparkPoolSize, _sparkPool);
            PrewarmCategory(VfxPoolCategory.MobilityAndDeathBurst, BurstPoolSize, _burstPool);
        }

        /// <summary>Spawns a pooled <see cref="PooledVfxInstance"/> matching the exact melee swing cone and sweep duration.</summary>
        public PooledVfxInstance SpawnMeleeSlashArc(
            Vector3 origin,
            Quaternion rotation,
            MeleeSwingKind kind,
            float sweepDurationSec = 0f,
            Transform attackerTransform = null,
            PlayerCombatController syncCombatController = null)
        {
            EnsurePoolInitialized();
            PooledVfxInstance inst = AcquireFromBucket(_slashPool, VfxPoolCategory.SlashArc);
            Vector3 chestPos = origin + Vector3.up * MeleeSectorMath.ChestHeightOffsetMeters;

            if (kind == MeleeSwingKind.VerticalSlash)
            {
                inst.PlaySlashArc(chestPos, rotation, _verticalSlashMesh, 50f, 2.5f, VerticalSlashCyan, 0.22f, 0f, attackerTransform, null);
            }
            else
            {
                float revealDur = sweepDurationSec > 0.001f ? sweepDurationSec : DefaultHorizontalSweepDurationSec;
                float totalDur = revealDur + SweepFadeTailDurationSec;
                Mesh sweepMesh = kind == MeleeSwingKind.HorizontalSweepLeft ? _horizontalLeftMesh : _horizontalRightMesh;
                inst.PlaySlashArc(
                    chestPos,
                    rotation,
                    sweepMesh,
                    90f,
                    3.0f,
                    HorizontalSweepAmber,
                    totalDur,
                    revealDur,
                    attackerTransform,
                    syncCombatController);
            }

            _activeInstances.Add(inst);
            return inst;
        }

        /// <summary>Spawns a pooled impact spark burst (14 gold sparks on player hit, 8 white sparks on wall hit).</summary>
        public PooledVfxInstance SpawnImpactSpark(Vector3 worldPos, bool hitLivingPlayer)
        {
            EnsurePoolInitialized();
            PooledVfxInstance inst = AcquireFromBucket(_sparkPool, VfxPoolCategory.ImpactSpark);
            Color color = hitLivingPlayer ? PlayerHitSparkGold : WallSparkWhite;
            int count = hitLivingPlayer ? PlayerHitSparkCount : WallImpactSparkCount;
            inst.PlayParticleBurst(worldPos, Quaternion.identity, color, count, 0.28f);
            _activeInstances.Add(inst);
            return inst;
        }

        /// <summary>Spawns a pooled mobility dash streak or lethal elimination particle ring.</summary>
        public PooledVfxInstance SpawnBurstEffect(Vector3 worldPos, Color tintColor, int count = 18, float durationSec = 0.40f)
        {
            EnsurePoolInitialized();
            PooledVfxInstance inst = AcquireFromBucket(_burstPool, VfxPoolCategory.MobilityAndDeathBurst);
            inst.PlayParticleBurst(worldPos, Quaternion.identity, tintColor, count, durationSec);
            _activeInstances.Add(inst);
            return inst;
        }

        /// <summary>Advances active VFX lifetimes and returns expired instances to their category pools.</summary>
        public void TickActiveVfx(float deltaTime)
        {
            if (deltaTime <= 0f || _activeInstances.Count == 0)
            {
                return;
            }

            for (int i = _activeInstances.Count - 1; i >= 0; i--)
            {
                PooledVfxInstance inst = _activeInstances[i];
                if (inst == null)
                {
                    _activeInstances.RemoveAt(i);
                    continue;
                }

                if (inst.TickLifetime(deltaTime))
                {
                    _activeInstances.RemoveAt(i);
                    ReturnToBucket(inst);
                }
            }
        }

        private PooledVfxInstance AcquireFromBucket(Queue<PooledVfxInstance> pool, VfxPoolCategory category)
        {
            if (pool.Count > 0)
            {
                return pool.Dequeue();
            }

            for (int i = 0; i < _activeInstances.Count; i++)
            {
                if (_activeInstances[i] != null && _activeInstances[i].Category == category)
                {
                    PooledVfxInstance stolen = _activeInstances[i];
                    _activeInstances.RemoveAt(i);
                    stolen.DeactivateToPool();
                    return stolen;
                }
            }

            return _allPrewarmed[0];
        }

        private void ReturnToBucket(PooledVfxInstance inst)
        {
            inst.DeactivateToPool();
            switch (inst.Category)
            {
                case VfxPoolCategory.SlashArc:
                    _slashPool.Enqueue(inst);
                    break;
                case VfxPoolCategory.ImpactSpark:
                    _sparkPool.Enqueue(inst);
                    break;
                default:
                    _burstPool.Enqueue(inst);
                    break;
            }
        }

        private void PrewarmCategory(VfxPoolCategory category, int count, Queue<PooledVfxInstance> targetQueue)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject go = new GameObject($"PooledVfx_{category}_{i:00}");
                go.transform.SetParent(transform, worldPositionStays: false);

                MeshFilter mf = null;
                MeshRenderer mr = null;
                if (category == VfxPoolCategory.SlashArc)
                {
                    GameObject arcGo = new GameObject("SlashSectorArcMesh");
                    arcGo.transform.SetParent(go.transform, worldPositionStays: false);
                    mf = arcGo.AddComponent<MeshFilter>();
                    mr = arcGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _sharedArcMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }

                ParticleSystem ps = CreateConfiguredParticleSystem(go);
                PooledVfxInstance wrapper = go.AddComponent<PooledVfxInstance>();
                wrapper.InitializePoolItem(category, mf, mr, ps);
                targetQueue.Enqueue(wrapper);
                _allPrewarmed.Add(wrapper);
            }
        }

        private ParticleSystem CreateConfiguredParticleSystem(GameObject host)
        {
            ParticleSystem ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.25f;
            main.startLifetime = 0.22f;
            main.startSpeed = 5.5f;
            main.startSize = 0.14f;
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            if (host.TryGetComponent(out ParticleSystemRenderer psr))
            {
                psr.sharedMaterial = _sharedParticleMaterial;
                psr.shadowCastingMode = ShadowCastingMode.Off;
                psr.receiveShadows = false;
            }

            return ps;
        }

        private void BuildSharedMaterialsAndMeshes()
        {
            Shader slashShader = Shader.Find("CapsuleArena/VFX/SlashSectorArc");
            Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");

            Shader urpParticlesUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (urpParticlesUnlit == null)
            {
                urpParticlesUnlit = urpUnlit;
            }

            if (_slashArcMaterialAsset != null)
            {
                _sharedArcMaterial = _slashArcMaterialAsset;
                _ownsArcMaterial = false;
            }
            else
            {
                Shader chosenArcShader = slashShader != null ? slashShader : urpUnlit;
                _sharedArcMaterial = new Material(chosenArcShader) { name = "M_SharedVfxSlashArc", enableInstancing = true };
                _ownsArcMaterial = true;
            }

            _sharedParticleMaterial = new Material(urpParticlesUnlit) { name = "M_SharedVfxSpark" };

            // UVs run u = 0 -> 1 from swing start direction (0 deg) out to swing end (-90 deg Left / +90 deg Right)
            _verticalSlashMesh = BuildAnnularSectorMesh("Mesh_VerticalSlash_50deg_2p5m", 25f, -25f, 0.70f, 2.50f, 14, verticalPlane: true);
            _horizontalLeftMesh = BuildAnnularSectorMesh("Mesh_SweepLeft_90deg_3p0m", 0f, -90f, 0.85f, 3.00f, 18, verticalPlane: false);
            _horizontalRightMesh = BuildAnnularSectorMesh("Mesh_SweepRight_90deg_3p0m", 0f, 90f, 0.85f, 3.00f, 18, verticalPlane: false);
        }

        private static Mesh BuildAnnularSectorMesh(
            string meshName,
            float startAngleDeg,
            float endAngleDeg,
            float innerRadius,
            float outerRadius,
            int segments,
            bool verticalPlane)
        {
            Vector3[] verts = new Vector3[(segments + 1) * 2];
            Vector2[] uvs = new Vector2[(segments + 1) * 2];
            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float rad = Mathf.Lerp(startAngleDeg, endAngleDeg, u) * Mathf.Deg2Rad;
                Vector3 dir = verticalPlane
                    ? new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad))
                    : new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                verts[i * 2] = dir * innerRadius;
                verts[i * 2 + 1] = dir * outerRadius;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            Mesh mesh = new Mesh { name = meshName };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = BuildDoubleSidedStripTriangles(segments);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int[] BuildDoubleSidedStripTriangles(int segments)
        {
            int[] tris = new int[segments * 12];
            int ti = 0;
            for (int i = 0; i < segments; i++)
            {
                int v0 = i * 2;
                int v1 = v0 + 1;
                int v2 = v0 + 2;
                int v3 = v0 + 3;
                tris[ti++] = v0; tris[ti++] = v1; tris[ti++] = v3;
                tris[ti++] = v0; tris[ti++] = v3; tris[ti++] = v2;
                tris[ti++] = v0; tris[ti++] = v3; tris[ti++] = v1;
                tris[ti++] = v0; tris[ti++] = v2; tris[ti++] = v3;
            }

            return tris;
        }

        private static void DestroyGeneratedAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(asset);
            }
            else
            {
                DestroyImmediate(asset);
            }
        }

        private void HandleMeleeSwing(MeleeSwingEvent evt)
        {
            SpawnMeleeSlashArc(evt.Origin, evt.Rotation, evt.Kind, evt.SweepDurationSec, evt.AttackerTransform, evt.SourceController);
        }

        private void HandleRangedShot(Vector3 muzzlePos, Vector3 aimDir, bool blockedByFlushWall)
        {
            if (blockedByFlushWall)
            {
                SpawnImpactSpark(muzzlePos, hitLivingPlayer: false);
            }
        }

        private void HandleProjectileImpact(Vector3 impactPos, bool hitLivingPlayer)
        {
            if (!hitLivingPlayer)
            {
                SpawnImpactSpark(impactPos, hitLivingPlayer: false);
            }
        }

        private void HandleHitFeedback(PlayerHealth victim, Vector3 hitPos, int damage, bool isLethal, PlayerRef attacker)
        {
            SpawnImpactSpark(hitPos, hitLivingPlayer: true);
            if (isLethal)
            {
                SpawnBurstEffect(hitPos, new Color32(230, 57, 70, 255), 20, 0.45f);
            }
        }

        private void HandleMobilityBurst(PlayerMobilityController controller, MobilityState state, Vector3 origin, Quaternion rotation)
        {
            Color burstColor = state == MobilityState.Dashing ? VerticalSlashCyan : HorizontalSweepAmber;
            SpawnBurstEffect(origin + Vector3.up * 0.9f, burstColor, 14, 0.25f);
        }
    }
}
