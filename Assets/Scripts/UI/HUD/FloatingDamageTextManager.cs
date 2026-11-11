using System.Collections.Generic;
using CapsuleArena.CameraSystem;
using CapsuleArena.Core.Config;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// Pre-warms and manages pooled world-space <see cref="PooledFloatingDamageText"/> instances
    /// driven by <see cref="FloatingDamageTextConfigSO"/>:
    /// - Spawns red numbers (no minus sign) above any combatant taking damage (<see cref="PlayerHealth.OnAnyHitFeedbackBroadcast"/>)
    /// - Spawns green numbers above any combatant recovering HP (<see cref="PlayerHealth.OnAnyHealFeedbackBroadcast"/>)
    /// - Visible to all players including self in TPS/Spectator, hidden strictly for local self when local camera is in <c>FPS</c>
    /// - Zero runtime <c>Instantiate</c>/<c>Destroy</c> or string allocations.
    /// </summary>
    public sealed class FloatingDamageTextManager : MonoBehaviour
    {
        /// <summary>Default pre-warmed pool size when no config asset is assigned (16).</summary>
        public const int DefaultPoolCapacity = 16;

        private static readonly float[] SpreadPatternMultipliers = { 0f, -0.75f, 0.75f, -0.40f, 0.40f };

        [Header("Configuration & Camera")]
        [Tooltip("Designer-configurable colors (Red damage / Green heal), scale ratios, rise height, and pool size.")]
        [SerializeField] private FloatingDamageTextConfigSO _config;

        [Tooltip("Scene camera used for world-space billboard orientation and distance scaling.")]
        [SerializeField] private Camera _targetCamera;

        private readonly Queue<PooledFloatingDamageText> _idlePool = new Queue<PooledFloatingDamageText>(DefaultPoolCapacity);
        private readonly List<PooledFloatingDamageText> _activeItems = new List<PooledFloatingDamageText>(DefaultPoolCapacity);
        private readonly List<PooledFloatingDamageText> _allPrewarmed = new List<PooledFloatingDamageText>(DefaultPoolCapacity);

        private IFloatingDamageTextConfig _configOverride;
        private Transform _cachedCameraTransform;
        private PlayerHealth _boundLocalHealth;
        private PlayerRef _boundLocalPlayerRef = PlayerRef.None;
        private CameraPerspectiveMode _localPerspective = CameraPerspectiveMode.TPS;
        private int _spreadCursor;
        private bool _isInitialized;

        /// <summary>Gets the active configuration (<see cref="FloatingDamageTextConfigSO"/> or test override).</summary>
        public IFloatingDamageTextConfig ActiveConfig => _configOverride != null ? _configOverride : _config;

        /// <summary>Gets all pre-warmed floating text instances.</summary>
        public IReadOnlyList<PooledFloatingDamageText> AllPrewarmedInstances
        {
            get
            {
                EnsurePoolInitialized();
                return _allPrewarmed;
            }
        }

        /// <summary>Gets the count of currently flying numbers in world space.</summary>
        public int ActiveCount => _activeItems.Count;

        /// <summary>Gets the count of idle numbers waiting in the pool.</summary>
        public int IdleCount => _idlePool.Count;

        /// <summary>Gets the currently tracked local camera perspective mode.</summary>
        public CameraPerspectiveMode CurrentLocalPerspective => _localPerspective;

        private void Awake()
        {
            CacheCameraTransform();
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

        /// <summary>Subscribes idempotently to global health hit/heal and local camera perspective events.</summary>
        public void SubscribeEvents()
        {
            UnsubscribeEvents();
            PlayerHealth.OnAnyHitFeedbackBroadcast += HandleAnyHitFeedback;
            PlayerHealth.OnAnyHealFeedbackBroadcast += HandleAnyHealFeedback;
            CameraController.OnAnyLocalPerspectiveChanged += HandleLocalPerspectiveChanged;
        }

        /// <summary>Unsubscribes from global health hit/heal and local camera perspective events.</summary>
        public void UnsubscribeEvents()
        {
            PlayerHealth.OnAnyHitFeedbackBroadcast -= HandleAnyHitFeedback;
            PlayerHealth.OnAnyHealFeedbackBroadcast -= HandleAnyHealFeedback;
            CameraController.OnAnyLocalPerspectiveChanged -= HandleLocalPerspectiveChanged;
        }

        private void LateUpdate()
        {
            if (_activeItems.Count == 0)
            {
                return;
            }

            if (_cachedCameraTransform == null)
            {
                CacheCameraTransform();
            }

            for (int i = 0; i < _activeItems.Count; i++)
            {
                PooledFloatingDamageText item = _activeItems[i];
                if (item != null && item.IsActiveInScene)
                {
                    item.UpdateBillboardAndVisibility(_cachedCameraTransform, _localPerspective);
                }
            }
        }

        /// <summary>Injects configuration and camera dependencies from <c>ArenaSceneBootstrapper</c> or unit tests.</summary>
        public void Initialize(IFloatingDamageTextConfig config = null, Camera targetCamera = null)
        {
            if (config != null)
            {
                _configOverride = config;
                if (config is FloatingDamageTextConfigSO so)
                {
                    _config = so;
                }
            }

            if (targetCamera != null)
            {
                _targetCamera = targetCamera;
                _cachedCameraTransform = targetCamera.transform;
            }
            else
            {
                CacheCameraTransform();
            }

            EnsurePoolInitialized();
        }

        /// <summary>Binds the local player's avatar so local self numbers hide exclusively in FPS perspective.</summary>
        public void BindLocalPlayer(PlayerHealth localHealth, PlayerRef localPlayerRef = default)
        {
            _boundLocalHealth = localHealth;
            _boundLocalPlayerRef = localPlayerRef != PlayerRef.None
                ? localPlayerRef
                : (localHealth != null ? localHealth.OwnerPlayer : PlayerRef.None);
        }

        /// <summary>Pre-warms the configured number of pooled world-space TMP instances idempotently.</summary>
        public void EnsurePoolInitialized()
        {
            int desiredCount = ActiveConfig != null ? Mathf.Max(4, ActiveConfig.PoolSize) : DefaultPoolCapacity;
            if (_isInitialized && _allPrewarmed.Count >= desiredCount)
            {
                return;
            }

            _isInitialized = true;
            while (_allPrewarmed.Count < desiredCount)
            {
                int idx = _allPrewarmed.Count;
                GameObject go = new GameObject($"PooledFloatingDamageText_{idx:00}");
                go.transform.SetParent(transform, worldPositionStays: false);
                PooledFloatingDamageText item = go.AddComponent<PooledFloatingDamageText>();
                item.InitializePoolItem(HandleItemCompleted);
                _idlePool.Enqueue(item);
                _allPrewarmed.Add(item);
            }
        }

        /// <summary>
        /// Spawns a floating damage (red) or heal (green) number above <paramref name="targetHealth"/>.
        /// Returns <c>null</c> when <paramref name="targetHealth"/> is the local player and local perspective is <c>FPS</c>.
        /// </summary>
        public PooledFloatingDamageText SpawnForTarget(PlayerHealth targetHealth, int amount, bool isHeal)
        {
            if (targetHealth == null || amount <= 0)
            {
                return null;
            }

            bool isLocalTarget = IsLocalPlayerAvatar(targetHealth);
            if (isLocalTarget && _localPerspective == CameraPerspectiveMode.FPS)
            {
                return null;
            }

            EnsurePoolInitialized();
            PooledFloatingDamageText inst = AcquireInstance();
            Vector3 spawnOrigin = ResolveHeadSpawnPosition(targetHealth.transform);

            inst.PlayFloatingNumber(spawnOrigin, amount, isHeal, isLocalTarget, ActiveConfig, _localPerspective);
            if (_cachedCameraTransform != null)
            {
                inst.UpdateBillboardAndVisibility(_cachedCameraTransform, _localPerspective);
            }

            _activeItems.Add(inst);
            return inst;
        }

        /// <summary>Updates tracked camera perspective and immediately refreshes local-self visibility on in-flight texts.</summary>
        public void HandleLocalPerspectiveChanged(CameraPerspectiveMode mode)
        {
            _localPerspective = mode;
            for (int i = 0; i < _activeItems.Count; i++)
            {
                if (_activeItems[i] != null && _activeItems[i].IsActiveInScene)
                {
                    _activeItems[i].UpdateBillboardAndVisibility(_cachedCameraTransform, _localPerspective);
                }
            }
        }

        private void HandleAnyHitFeedback(PlayerHealth victim, Vector3 hitPos, int damage, bool isLethal, PlayerRef attacker)
        {
            SpawnForTarget(victim, damage, isHeal: false);
        }

        private void HandleAnyHealFeedback(PlayerHealth target, Vector3 healPos, int healedAmount)
        {
            SpawnForTarget(target, healedAmount, isHeal: true);
        }

        private PooledFloatingDamageText AcquireInstance()
        {
            if (_idlePool.Count > 0)
            {
                return _idlePool.Dequeue();
            }

            if (_activeItems.Count > 0 && _activeItems[0] != null)
            {
                PooledFloatingDamageText oldest = _activeItems[0];
                _activeItems.RemoveAt(0);
                oldest.DeactivateToPool();
                return oldest;
            }

            return _allPrewarmed[0];
        }

        private void HandleItemCompleted(PooledFloatingDamageText completedItem)
        {
            if (completedItem == null)
            {
                return;
            }

            _activeItems.Remove(completedItem);
            if (!_idlePool.Contains(completedItem))
            {
                _idlePool.Enqueue(completedItem);
            }
        }

        private Vector3 ResolveHeadSpawnPosition(Transform targetRoot)
        {
            IFloatingDamageTextConfig cfg = ActiveConfig;
            float yOffset = cfg != null ? cfg.SpawnHeightOffsetMeters : 2.65f;
            float spread = cfg != null ? cfg.HorizontalSpreadMeters : 0.16f;

            float mult = SpreadPatternMultipliers[_spreadCursor % SpreadPatternMultipliers.Length];
            _spreadCursor++;

            Vector3 rightAxis = _cachedCameraTransform != null ? _cachedCameraTransform.right : Vector3.right;
            rightAxis.y = 0f;
            if (rightAxis.sqrMagnitude < 0.0001f)
            {
                rightAxis = Vector3.right;
            }
            else
            {
                rightAxis.Normalize();
            }

            return targetRoot.position + Vector3.up * yOffset + rightAxis * (spread * mult);
        }

        private bool IsLocalPlayerAvatar(PlayerHealth candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (_boundLocalHealth != null && ReferenceEquals(candidate, _boundLocalHealth))
            {
                return true;
            }

            if (_boundLocalPlayerRef != PlayerRef.None && candidate.OwnerPlayer == _boundLocalPlayerRef)
            {
                return true;
            }

            return candidate.Object != null
                   && candidate.Runner != null
                   && candidate.Runner.LocalPlayer != PlayerRef.None
                   && candidate.Object.InputAuthority == candidate.Runner.LocalPlayer;
        }

        private void CacheCameraTransform()
        {
            if (_targetCamera != null)
            {
                _cachedCameraTransform = _targetCamera.transform;
            }
            else if (_cachedCameraTransform == null && Camera.main != null)
            {
                _cachedCameraTransform = Camera.main.transform;
            }
        }
    }
}
