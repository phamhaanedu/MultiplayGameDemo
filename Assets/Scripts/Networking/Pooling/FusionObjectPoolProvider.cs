using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Networking.Pooling
{
    /// <summary>
    /// Prewarmed zero-allocation FIFO <see cref="INetworkObjectProvider"/> for Photon Fusion 2 Shared Mode
    /// (Story 4-2; ADR-0003; TR-session-002). Pre-allocates 32 projectiles at startup and caps expansion at 48.
    /// </summary>
    public sealed class FusionObjectPoolProvider : NetworkObjectProviderDefault
    {
        /// <summary>Canonical number of pre-instantiated projectile instances at session startup.</summary>
        public const int DefaultPrewarmCount = 32;

        /// <summary>Hard upper bound on pooled instances per prefab to guard mobile memory.</summary>
        public const int DefaultMaxPoolCap = 48;

        [Header("Projectile Pool Configuration")]
        [Tooltip("Primary pooled NetworkProjectile prefab prewarmed before match start.")]
        [SerializeField] private NetworkObject _projectilePrefab;

        [Tooltip("Number of inactive instances pre-instantiated on Prewarm (canonical: 32).")]
        [SerializeField] private int _prewarmCount = DefaultPrewarmCount;

        [Tooltip("Maximum allowed instances including fallback burst expansion (canonical: 48).")]
        [SerializeField] private int _maxPoolCap = DefaultMaxPoolCap;

        private readonly Dictionary<EntityId, Queue<NetworkObject>> _inactivePools =
            new Dictionary<EntityId, Queue<NetworkObject>>(4);

        private readonly Dictionary<EntityId, EntityId> _instanceToPrefabKey =
            new Dictionary<EntityId, EntityId>(64);

        private readonly Dictionary<EntityId, int> _totalCreatedPerPrefab =
            new Dictionary<EntityId, int>(4);

        private readonly Dictionary<EntityId, IPooledResettable[]> _cachedResettables =
            new Dictionary<EntityId, IPooledResettable[]>(64);

        private Transform _poolRoot;

        /// <summary>Gets the count of runtime Instantiate calls triggered after Prewarm completed.</summary>
        public int RuntimeInstantiateCallsAfterPrewarm { get; private set; }

        /// <summary>Gets the count of runtime Destroy calls triggered during active pool operation.</summary>
        public int RuntimeDestroyCalls { get; private set; }

        /// <summary>
        /// Pre-instantiates <paramref name="count"/> inactive copies of <paramref name="prefab"/>
        /// into a FIFO queue and caches their <see cref="IPooledResettable"/> hooks for zero-GC runtime reuse.
        /// </summary>
        public void Prewarm(NetworkObject prefab, int count = DefaultPrewarmCount)
        {
            if (prefab == null)
            {
                return;
            }

            EnsurePoolRoot();
            EntityId prefabKey = prefab.GetEntityId();
            int targetCount = Mathf.Clamp(count, 0, _maxPoolCap);

            if (!_inactivePools.TryGetValue(prefabKey, out Queue<NetworkObject> queue))
            {
                queue = new Queue<NetworkObject>(_maxPoolCap);
                _inactivePools[prefabKey] = queue;
            }

            int alreadyCreated = GetTotalCreatedCount(prefabKey);
            for (int i = alreadyCreated; i < targetCount; i++)
            {
                NetworkObject instance = CreatePooledCopy(prefab, prefabKey, isPrewarm: true);
                NotifyReleaseHooks(instance);
                instance.gameObject.SetActive(false);
                queue.Enqueue(instance);
            }

            RuntimeInstantiateCallsAfterPrewarm = 0;
            RuntimeDestroyCalls = 0;
        }

        /// <summary>
        /// Prewarms the serialized projectile prefab configured in the Inspector, if assigned.
        /// </summary>
        public void PrewarmConfiguredPrefabs()
        {
            if (_projectilePrefab != null)
            {
                Prewarm(_projectilePrefab, _prewarmCount);
            }
        }

        /// <summary>
        /// Acquires a pooled <see cref="NetworkObject"/> in O(1) FIFO order, positioning it prior to activation
        /// and invoking <see cref="IPooledResettable.OnPoolAcquire"/>. Expands up to <see cref="DefaultMaxPoolCap"/> if depleted.
        /// </summary>
        public NetworkObject AcquireFromPool(NetworkObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                return null;
            }

            EntityId prefabKey = prefab.GetEntityId();
            if (!_inactivePools.TryGetValue(prefabKey, out Queue<NetworkObject> queue))
            {
                return Instantiate(prefab, position, rotation);
            }

            NetworkObject instance = DequeueValidInstance(queue) ?? TryExpandPoolInstance(prefab, prefabKey);
            if (instance == null)
            {
                return null;
            }

            instance.transform.SetParent(null, worldPositionStays: false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            NotifyAcquireHooks(instance);
            return instance;
        }

        /// <summary>
        /// Scrubs state via <see cref="IPooledResettable.OnPoolRelease"/>, deactivates the object,
        /// and enqueues it back onto its FIFO prefab pool without calling <c>Object.Destroy</c>.
        /// </summary>
        public void ReleaseToPool(NetworkObject instance)
        {
            if (instance == null)
            {
                return;
            }

            EntityId instanceId = instance.GetEntityId();
            if (!_instanceToPrefabKey.TryGetValue(instanceId, out EntityId prefabKey) ||
                !_inactivePools.TryGetValue(prefabKey, out Queue<NetworkObject> queue))
            {
                RuntimeDestroyCalls++;
                Destroy(instance.gameObject);
                return;
            }

            NotifyReleaseHooks(instance);
            instance.gameObject.SetActive(false);
            EnsurePoolRoot();
            instance.transform.SetParent(_poolRoot, worldPositionStays: false);
            queue.Enqueue(instance);
        }

        /// <summary>Returns the current number of inactive instances ready in the pool for <paramref name="prefab"/>.</summary>
        public int GetInactiveCount(NetworkObject prefab)
        {
            if (prefab == null)
            {
                return 0;
            }

            return _inactivePools.TryGetValue(prefab.GetEntityId(), out Queue<NetworkObject> queue) ? queue.Count : 0;
        }

        /// <summary>Returns the total number of instances ever allocated for <paramref name="prefab"/>.</summary>
        public int GetTotalCreatedCount(NetworkObject prefab)
        {
            return prefab == null ? 0 : GetTotalCreatedCount(prefab.GetEntityId());
        }

        /// <summary>Destroys all pooled instances and clears lookup tables on session shutdown or test teardown.</summary>
        public void ClearAndDestroyAllImmediate()
        {
            foreach (KeyValuePair<EntityId, Queue<NetworkObject>> kvp in _inactivePools)
            {
                while (kvp.Value.Count > 0)
                {
                    NetworkObject obj = kvp.Value.Dequeue();
                    if (obj != null)
                    {
                        DestroyImmediate(obj.gameObject);
                    }
                }
            }

            _inactivePools.Clear();
            _instanceToPrefabKey.Clear();
            _totalCreatedPerPrefab.Clear();
            _cachedResettables.Clear();

            if (_poolRoot != null)
            {
                DestroyImmediate(_poolRoot.gameObject);
                _poolRoot = null;
            }
        }

        /// <inheritdoc />
        protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
        {
            return AcquireFromPool(prefab, Vector3.zero, Quaternion.identity);
        }

        /// <inheritdoc />
        protected override void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
        {
            ReleaseToPool(instance);
        }

        private NetworkObject TryExpandPoolInstance(NetworkObject prefab, EntityId prefabKey)
        {
            int created = GetTotalCreatedCount(prefabKey);
            if (created >= _maxPoolCap)
            {
                Debug.LogWarning($"[FusionObjectPoolProvider] Pool hard cap ({_maxPoolCap}) reached for '{prefab.name}'.");
                return null;
            }

            Debug.LogWarning($"[FusionObjectPoolProvider] Pool exhausted ({created}/{_maxPoolCap}) for '{prefab.name}'; expanding.");
            return CreatePooledCopy(prefab, prefabKey, isPrewarm: false);
        }

        private NetworkObject CreatePooledCopy(NetworkObject prefab, EntityId prefabKey, bool isPrewarm)
        {
            EnsurePoolRoot();
            NetworkObject copy = Instantiate(prefab, _poolRoot);
            EntityId copyId = copy.GetEntityId();

            _instanceToPrefabKey[copyId] = prefabKey;
            _cachedResettables[copyId] = copy.GetComponentsInChildren<IPooledResettable>(includeInactive: true);
            _totalCreatedPerPrefab[prefabKey] = GetTotalCreatedCount(prefabKey) + 1;

            if (!isPrewarm)
            {
                RuntimeInstantiateCallsAfterPrewarm++;
            }

            return copy;
        }

        private static NetworkObject DequeueValidInstance(Queue<NetworkObject> queue)
        {
            while (queue.Count > 0)
            {
                NetworkObject candidate = queue.Dequeue();
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private int GetTotalCreatedCount(EntityId prefabKey)
        {
            return _totalCreatedPerPrefab.TryGetValue(prefabKey, out int count) ? count : 0;
        }

        private void NotifyAcquireHooks(NetworkObject instance)
        {
            if (!_cachedResettables.TryGetValue(instance.GetEntityId(), out IPooledResettable[] hooks))
            {
                return;
            }

            for (int i = 0; i < hooks.Length; i++)
            {
                hooks[i]?.OnPoolAcquire();
            }
        }

        private void NotifyReleaseHooks(NetworkObject instance)
        {
            if (!_cachedResettables.TryGetValue(instance.GetEntityId(), out IPooledResettable[] hooks))
            {
                return;
            }

            for (int i = 0; i < hooks.Length; i++)
            {
                hooks[i]?.OnPoolRelease();
            }
        }

        private void EnsurePoolRoot()
        {
            if (_poolRoot == null)
            {
                GameObject container = new GameObject("FusionObjectPool_Inactive");
                container.transform.SetParent(transform, worldPositionStays: false);
                _poolRoot = container.transform;
            }
        }
    }
}
