using System;
using System.Collections.Generic;
using CapsuleArena.Arena;
using CapsuleArena.CameraSystem;
using CapsuleArena.Cloud;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Networking;
using CapsuleArena.Networking.Pooling;
using CapsuleArena.Player;
using CapsuleArena.Presentation.Audio;
using CapsuleArena.Presentation.VFX;
using CapsuleArena.UI.Chat;
using CapsuleArena.UI.HUD;
using CapsuleArena.UI.Leaderboard;
using CapsuleArena.UI.Lobby;
using CapsuleArena.UI.Settings;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Core.Bootstrap
{
    /// <summary>
    /// Scene-level Composition Root for <c>Arena.unity</c> that constructs the shared
    /// <see cref="KeybindingManager"/> and <see cref="InputFocusManager"/> once in <c>Awake()</c>,
    /// injects dependencies into session, match, HUD, mobile pad, lobby, settings, chat, PlayFab cloud,
    /// and 3D audio/VFX controllers, and binds spawned local/remote <c>PlayerCapsule</c> instances
    /// strictly via events with zero runtime <c>FindObjectsByType</c> calls (ADR-0002, ADR-0006, ADR-0007, ADR-0008).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ArenaSceneBootstrapper : MonoBehaviour
    {
        [Header("Core Scene Systems")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private CharacterCombatConfigSO _combatConfig;
        [SerializeField] private MatchRulesConfigSO _matchRulesConfig;
        [SerializeField] private LocalInputPoller _inputPoller;
        [SerializeField] private MobileInputBridge _mobileBridge;
        [SerializeField] private FusionSessionManager _sessionManager;
        [SerializeField] private ArenaSpawnManager _spawnManager;
        [SerializeField] private FusionObjectPoolProvider _poolProvider;
        [SerializeField] private MatchManager _matchManager;

        [Header("UI Views & Presenters")]
        [SerializeField] private CombatHudPresenter _combatHud;
        [SerializeField] private MobileActionPadView _mobilePad;
        [SerializeField] private LobbyViewController _lobbyView;
        [SerializeField] private KeybindingRebindModal _rebindModal;
        [SerializeField] private ChatViewController _chatView;
        [SerializeField] private ChatNetworkRouter _chatRouter;
        [SerializeField] private LeaderboardModalView _leaderboardModal;

        [Header("Cloud & Sensory Presentation")]
        [SerializeField] private PlayFabAuthService _playFabAuth;
        [SerializeField] private PlayFabStatsService _playFabStats;
        [SerializeField] private PlayFabLeaderboardService _playFabLeaderboard;
        [SerializeField] private SoundManager _soundManager;
        [SerializeField] private VfxManager _vfxManager;
        [SerializeField] private FloatingDamageTextConfigSO _floatingDamageConfig;
        [SerializeField] private FloatingDamageTextManager _floatingDamageManager;

        private readonly List<PlayerHealth> _scratchReceivers = new List<PlayerHealth>(4);
        private KeybindingManager _keybindingManager;
        private InputFocusManager _focusManager;
        private SpectatorController _localSpectatorController;
        private PlayerHealth _boundLocalHealth;
        private CameraController _boundLocalCamera;
        private Func<bool> _cachedCombatAllowedPredicate;
        private bool _isBootstrapped;

        /// <summary>Gets the shared <see cref="IKeybindingService"/> instance owned by this scene.</summary>
        public IKeybindingService KeybindingService => _keybindingManager;

        /// <summary>Gets the shared <see cref="IInputFocusService"/> instance owned by this scene.</summary>
        public IInputFocusService FocusService => _focusManager;

        /// <summary>Raised when the user clicks the Leaderboard button on the Main Lobby.</summary>
        public event Action OnLeaderboardOpenRequested;

        private void Awake()
        {
            BootstrapSceneServices();
        }

        private void OnEnable()
        {
            SubscribeRuntimeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeRuntimeEvents();
        }

        /// <summary>Initializes and injects all scene-level services deterministically.</summary>
        public void BootstrapSceneServices()
        {
            if (_isBootstrapped)
            {
                return;
            }

            _isBootstrapped = true;
            _keybindingManager = new KeybindingManager();
            _focusManager = new InputFocusManager();
            _cachedCombatAllowedPredicate = EvaluateIsMatchCombatAllowed;

            WireInputAndSessionSystems();
            WireUiPresenters();
            WireCloudAndSensorySystems();
        }

        private void WireInputAndSessionSystems()
        {
            if (_inputPoller != null)
            {
                _inputPoller.Initialize(_keybindingManager, _focusManager, _mobileBridge);
            }

            if (_sessionManager != null)
            {
                _sessionManager.ConfigureDependencies(_poolProvider, _spawnManager, _inputPoller, _focusManager, _matchManager);
            }

            if (_matchManager != null)
            {
                if (_matchRulesConfig != null)
                {
                    _matchManager.SetMatchRulesConfig(_matchRulesConfig);
                }

                _matchManager.ConfigureServices(_focusManager);
            }
        }

        private void WireUiPresenters()
        {
            WireCombatHudEvents();

            if (_mobilePad != null)
            {
                _mobilePad.Initialize(_mobileBridge, _keybindingManager, _focusManager);
            }

            if (_rebindModal != null)
            {
                _rebindModal.Initialize(_keybindingManager, _focusManager, _matchManager);
            }

            if (_lobbyView != null)
            {
                _lobbyView.Initialize(_sessionManager, _matchManager, OpenSettingsModal, RequestLeaderboardOpen);
            }

            if (_chatView != null)
            {
                _chatView.Initialize(_focusManager, _chatRouter, null, _matchManager, _keybindingManager, _inputPoller);
            }
        }

        private void WireCombatHudEvents()
        {
            if (_combatHud == null)
            {
                return;
            }

            _combatHud.BindMatchManager(_matchManager);
            _combatHud.OnReturnToLobbyRequested -= HandleReturnToLobbyRequested;
            _combatHud.OnReturnToLobbyRequested += HandleReturnToLobbyRequested;
            _combatHud.OnPlayAgainRequested -= HandlePlayAgainRequested;
            _combatHud.OnPlayAgainRequested += HandlePlayAgainRequested;
        }

        private void WireCloudAndSensorySystems()
        {
            IPlayFabClientAdapter sharedAdapter = _playFabAuth != null ? _playFabAuth.ResolveClientAdapter() : null;
            if (_playFabLeaderboard != null)
            {
                _playFabLeaderboard.Initialize(_playFabAuth, sharedAdapter);
            }

            if (_playFabStats != null)
            {
                _playFabStats.Initialize(_playFabAuth, sharedAdapter, _playFabLeaderboard, _matchManager);
            }

            if (_leaderboardModal != null)
            {
                _leaderboardModal.Initialize(_playFabAuth, _playFabLeaderboard, _focusManager, _matchManager);
            }

            if (_soundManager != null)
            {
                Transform listener = _mainCamera != null ? _mainCamera.transform : null;
                _soundManager.SetListenerTransform(listener);
            }

            if (_vfxManager != null)
            {
                _vfxManager.EnsurePoolInitialized();
            }

            if (_floatingDamageManager != null)
            {
                _floatingDamageManager.Initialize(_floatingDamageConfig, _mainCamera);
            }
        }

        private void SubscribeRuntimeEvents()
        {
            DamageReceiverRegistry.OnReceiverRegistered -= HandleReceiverRegistered;
            DamageReceiverRegistry.OnReceiverRegistered += HandleReceiverRegistered;
            DamageReceiverRegistry.OnReceiverOwnerAssigned -= HandleReceiverOwnerAssigned;
            DamageReceiverRegistry.OnReceiverOwnerAssigned += HandleReceiverOwnerAssigned;

            if (_sessionManager != null)
            {
                _sessionManager.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
                _sessionManager.OnLocalPlayerSpawned += HandleLocalPlayerSpawned;
                _sessionManager.OnPeerDisconnected -= HandlePeerDisconnected;
                _sessionManager.OnPeerDisconnected += HandlePeerDisconnected;
            }

            if (_matchManager != null)
            {
                _matchManager.OnRosterCountsChanged -= HandleRosterCountsChanged;
                _matchManager.OnRosterCountsChanged += HandleRosterCountsChanged;
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
                _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
            }
        }

        private void UnsubscribeRuntimeEvents()
        {
            DamageReceiverRegistry.OnReceiverRegistered -= HandleReceiverRegistered;
            DamageReceiverRegistry.OnReceiverOwnerAssigned -= HandleReceiverOwnerAssigned;

            if (_sessionManager != null)
            {
                _sessionManager.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
                _sessionManager.OnPeerDisconnected -= HandlePeerDisconnected;
            }

            if (_matchManager != null)
            {
                _matchManager.OnRosterCountsChanged -= HandleRosterCountsChanged;
                _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
            }

            if (_combatHud != null)
            {
                _combatHud.OnReturnToLobbyRequested -= HandleReturnToLobbyRequested;
                _combatHud.OnPlayAgainRequested -= HandlePlayAgainRequested;
            }

            UnbindLocalCameraTrauma();
        }

        private void HandleReceiverRegistered(IDamageReceiver receiver)
        {
            if (receiver is PlayerHealth health)
            {
                WireSpawnedCapsuleCommon(health);
            }
        }

        private void HandleReceiverOwnerAssigned(PlayerRef owner, IDamageReceiver receiver)
        {
            if (receiver is PlayerHealth health)
            {
                WireSpawnedCapsuleCommon(health);
            }
        }

        private void WireSpawnedCapsuleCommon(PlayerHealth health)
        {
            if (health == null)
            {
                return;
            }

            health.BindMatchPlayingPredicate(_cachedCombatAllowedPredicate);
            if (health.TryGetComponent(out PlayerCombatController combat))
            {
                combat.BindMatchPlayingPredicate(_cachedCombatAllowedPredicate);
            }

            RefreshCapsuleBillboard(health);
        }

        private void RefreshCapsuleBillboard(PlayerHealth health)
        {
            PlayerRef localRef = ResolveLocalPlayerRef();
            bool isLocal = localRef != PlayerRef.None && health.OwnerPlayer == localRef;
            OverheadBillboardUI billboard = health.GetComponentInChildren<OverheadBillboardUI>(includeInactive: true);
            if (billboard != null)
            {
                string displayName = ResolveCapsuleDisplayName(health.OwnerPlayer, isLocal);
                Transform camTransform = _mainCamera != null ? _mainCamera.transform : null;
                billboard.Initialize(health, displayName, isLocal, camTransform);
            }
        }

        private void HandleRosterCountsChanged(int aliveCount, int totalConnected)
        {
            DamageReceiverRegistry.CopyTypedReceiversNonAlloc(_scratchReceivers);
            for (int i = 0; i < _scratchReceivers.Count; i++)
            {
                if (_scratchReceivers[i] != null)
                {
                    RefreshCapsuleBillboard(_scratchReceivers[i]);
                }
            }

            EnforceLateJoinSpectatorStateIfRequired();
        }

        private void HandleMatchStateChanged(MatchState state)
        {
            if (state == MatchState.Countdown || state == MatchState.Playing)
            {
                _focusManager?.SetFocusFlag(InputFocusFlag.RebindModalOpen, false);
                _focusManager?.SetFocusFlag(InputFocusFlag.LeaderboardOpen, false);
                _focusManager?.SetFocusFlag(InputFocusFlag.MatchEnded, false);
            }

            if (state == MatchState.Playing)
            {
                EnforceLateJoinSpectatorStateIfRequired();
            }
        }

        /// <summary>
        /// Wires local camera, input, combat, HUD, and session services when the local player's avatar spawns.
        /// Binds critical local gameplay systems first so non-critical UI/Auth callbacks can never abort camera or input binding.
        /// </summary>
        public void HandleLocalPlayerSpawned(PlayerRef localPlayer, NetworkObject explicitAvatarOverride = null)
        {
            if (_focusManager != null)
            {
                _focusManager.SetFocusFlag(InputFocusFlag.RebindModalOpen, false);
                _focusManager.SetFocusFlag(InputFocusFlag.LeaderboardOpen, false);
                if (_matchManager == null || _matchManager.CurrentMatchState != MatchState.Ended)
                {
                    _focusManager.SetFocusFlag(InputFocusFlag.MatchEnded, false);
                }
            }

            NetworkObject avatarObj = explicitAvatarOverride != null
                ? explicitAvatarOverride
                : (_sessionManager != null ? _sessionManager.LocalPlayerAvatar : null);
            BindLocalAvatarObject(localPlayer, avatarObj);

            ConfigureSessionAndLobbyForLocalSpawnSafely(localPlayer);
            EnforceLateJoinSpectatorStateIfRequired();
        }

        private void HandleLocalPlayerSpawned(PlayerRef localPlayer)
        {
            HandleLocalPlayerSpawned(localPlayer, explicitAvatarOverride: null);
        }

        private void ConfigureSessionAndLobbyForLocalSpawnSafely(PlayerRef localPlayer)
        {
            try
            {
                string localName = ResolveCapsuleDisplayName(localPlayer, isLocal: true);
                if (_lobbyView != null)
                {
                    _lobbyView.NotifyLocalPlayerSpawned(localPlayer);
                }

                if (_playFabAuth != null)
                {
                    _playFabAuth.SetDisplayName(localName);
                }

                if (_matchManager != null)
                {
                    string activeSessionName = _sessionManager != null
                        && _sessionManager.ActiveRunner != null
                        && _sessionManager.ActiveRunner.SessionInfo.IsValid
                        && !string.IsNullOrEmpty(_sessionManager.ActiveRunner.SessionInfo.Name)
                        ? _sessionManager.ActiveRunner.SessionInfo.Name
                        : null;
                    _matchManager.ConfigureServices(_focusManager, activeSessionName, localPlayer);
                }

                if (_chatRouter != null)
                {
                    _chatRouter.ConfigureRosterForSession(localPlayer, localName, null, _matchManager);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ArenaSceneBootstrapper] Non-fatal warning while syncing lobby/session metadata on local spawn: {ex.Message}");
            }
        }

        private void EnforceLateJoinSpectatorStateIfRequired()
        {
            if (_matchManager == null || _matchManager.CurrentMatchState != MatchState.Playing)
            {
                return;
            }

            if (_boundLocalHealth == null || _boundLocalHealth.IsDead)
            {
                return;
            }

            PlayerRef localRef = ResolveLocalPlayerRef();
            if (localRef == PlayerRef.None)
            {
                localRef = _boundLocalHealth.OwnerPlayer;
            }

            bool shouldEnterGhost;
            if (localRef != PlayerRef.None && _matchManager.TryGetPlayerStats(localRef, out PlayerMatchStats stats))
            {
                shouldEnterGhost = !stats.IsAlive;
            }
            else if (_matchManager.IsReadyOnNetwork || (_sessionManager != null && _sessionManager.ActiveRunner != null))
            {
                // Wait for MasterClient's authoritative MatchRoster replication (which invokes HandleRosterCountsChanged)
                // rather than prematurely eliminating a joining combatant before their roster slot arrives.
                return;
            }
            else
            {
                shouldEnterGhost = !_matchManager.CanJoinCurrentMatchAlive();
            }

            if (shouldEnterGhost)
            {
                _boundLocalHealth.EnterLateJoinGhostSpectator();
                RefreshCapsuleBillboard(_boundLocalHealth);
            }
        }

        /// <summary>Binds camera, mobility, combat, spectator, and HUD to the local player's <see cref="NetworkObject"/>.</summary>
        public void BindLocalAvatarObject(PlayerRef localPlayer, NetworkObject avatarObj)
        {
            if (avatarObj != null && avatarObj.TryGetComponent(out PlayerHealth localHealth))
            {
                WireSpawnedCapsuleCommon(localHealth);
                WireLocalPlayerComponents(localPlayer, avatarObj, localHealth);
            }
        }

        private void WireLocalPlayerComponents(PlayerRef localPlayer, NetworkObject avatarObj, PlayerHealth localHealth)
        {
            avatarObj.TryGetComponent(out PlayerCombatController localCombat);
            avatarObj.TryGetComponent(out PlayerMobilityController localMobility);
            if (localMobility != null)
            {
                localMobility.BindLocalInputPoller(_inputPoller);
            }

            if (localCombat != null)
            {
                localCombat.BindLocalServices(_inputPoller, _cachedCombatAllowedPredicate);
            }

            if (avatarObj.TryGetComponent(out CameraController camCtrl))
            {
                camCtrl.BindLocalCamera(_mainCamera, _inputPoller, _focusManager, _combatConfig);
                camCtrl.StepCameraFrame(0f);
                BindLocalCameraTrauma(localHealth, camCtrl);
            }

            if (avatarObj.TryGetComponent(out SpectatorController specCtrl))
            {
                _localSpectatorController = specCtrl;
                specCtrl.ConfigureForLocalPlayer(localHealth, _mainCamera, _inputPoller, _focusManager, _keybindingManager, _mobileBridge);
            }

            BindHudToLocalAvatar(localPlayer, localHealth, localCombat, localMobility);
        }

        private void BindHudToLocalAvatar(
            PlayerRef localPlayer,
            PlayerHealth localHealth,
            PlayerCombatController localCombat,
            PlayerMobilityController localMobility)
        {
            if (_combatHud != null)
            {
                _combatHud.BindLocalPlayer(localHealth, localCombat, localPlayer);
            }

            if (_mobilePad != null)
            {
                _mobilePad.BindLocalPlayer(localHealth, localCombat, localMobility);
            }

            if (_rebindModal != null)
            {
                _rebindModal.BindLocalPlayerHealth(localHealth);
            }

            if (_floatingDamageManager != null)
            {
                _floatingDamageManager.BindLocalPlayer(localHealth, localPlayer);
            }
        }

        private PlayerRef ResolveLocalPlayerRef()
        {
            if (_sessionManager != null && _sessionManager.ActiveRunner != null)
            {
                return _sessionManager.ActiveRunner.LocalPlayer;
            }

            return PlayerRef.None;
        }

        private void HandlePeerDisconnected(PlayerRef player)
        {
            if (_localSpectatorController != null)
            {
                _localSpectatorController.NotifyPeerDisconnected(player);
            }
        }

        private void HandleReturnToLobbyRequested()
        {
            if (_lobbyView != null)
            {
                _ = _lobbyView.LeaveWaitingRoomAsync();
            }
        }

        private void HandlePlayAgainRequested()
        {
            if (_soundManager != null)
            {
                _soundManager.Play2D(SoundId.UiClick);
            }

            if (_matchManager != null)
            {
                _matchManager.RequestImmediateRematchIfEnded();
            }
        }

        private void BindLocalCameraTrauma(PlayerHealth localHealth, CameraController camCtrl)
        {
            UnbindLocalCameraTrauma();
            _boundLocalHealth = localHealth;
            _boundLocalCamera = camCtrl;
            if (_boundLocalHealth != null)
            {
                _boundLocalHealth.OnHitFeedbackReceived += HandleLocalAvatarHitTrauma;
            }
        }

        private void UnbindLocalCameraTrauma()
        {
            if (_boundLocalHealth != null)
            {
                _boundLocalHealth.OnHitFeedbackReceived -= HandleLocalAvatarHitTrauma;
            }

            _boundLocalHealth = null;
            _boundLocalCamera = null;
        }

        private void HandleLocalAvatarHitTrauma(Vector3 hitPos, int damage, bool isLethal, PlayerRef attacker)
        {
            if (_boundLocalCamera != null)
            {
                _boundLocalCamera.AddTrauma(isLethal ? 0.6f : 0.3f);
            }
        }

        private void OpenSettingsModal()
        {
            if (_rebindModal != null)
            {
                _rebindModal.OpenModal();
            }
        }

        private void RequestLeaderboardOpen()
        {
            if (_leaderboardModal != null)
            {
                _leaderboardModal.OpenModal();
            }

            OnLeaderboardOpenRequested?.Invoke();
        }

        private bool EvaluateIsMatchCombatAllowed()
        {
            return _matchManager == null || _matchManager.IsCombatAllowed;
        }

        private string ResolveCapsuleDisplayName(PlayerRef owner, bool isLocal)
        {
            if (isLocal && _lobbyView != null && !string.IsNullOrEmpty(_lobbyView.LocalDisplayName))
            {
                return _lobbyView.LocalDisplayName;
            }

            if (_matchManager != null && owner != PlayerRef.None && _matchManager.TryGetPlayerStats(owner, out PlayerMatchStats stats))
            {
                string synced = stats.DisplayName.ToString();
                if (!string.IsNullOrEmpty(synced))
                {
                    return synced;
                }
            }

            return owner != PlayerRef.None ? $"Player_{Mathf.Max(1, owner.PlayerId)}" : "Player";
        }
    }
}
