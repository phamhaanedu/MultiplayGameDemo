using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CapsuleArena.Arena;
using CapsuleArena.Combat;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Networking.Pooling;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CapsuleArena.Networking
{
    /// <summary>
    /// Abstraction over Photon Fusion 2 Shared Mode session lifecycle, Room Code validation,
    /// and disconnect notifications.
    /// </summary>
    public interface INetworkSessionService
    {
        /// <summary>Raised strictly once per session failure, timeout, or clean disconnect.</summary>
        event Action<string> OnSessionDisconnected;

        /// <summary>Raised when the local peer joins the Shared Mode room and spawns its avatar.</summary>
        event Action<PlayerRef> OnLocalPlayerSpawned;

        /// <summary>Gets a value indicating whether an active Shared Mode session is currently running.</summary>
        bool IsSessionActive { get; }

        /// <summary>Starts or joins a Shared Mode room (empty code = Quick Join random room).</summary>
        Task<bool> StartSharedSessionAsync(string roomCode);

        /// <summary>Cleanly shuts down the active <see cref="NetworkRunner"/> and returns to lobby state.</summary>
        Task ShutdownSessionAsync(string reason = "UserLeft");
    }

    /// <summary>
    /// Bootstraps Photon Fusion 2 in <see cref="GameMode.Shared"/> (max 4 players, 60Hz tick rate),
    /// hosts each single-use <see cref="NetworkRunner"/> on a disposable child GameObject, wires
    /// <see cref="FusionObjectPoolProvider"/>, spawns strictly the local peer's capsule via Max-Min
    /// <see cref="ArenaSpawnManager"/>, and enforces a 10-second connection watchdog
    /// (Story 4-1; ADR-0003; TR-session-001).
    /// </summary>
    public sealed class FusionSessionManager : MonoBehaviour, INetworkRunnerCallbacks, INetworkSessionService
    {
        /// <summary>Maximum concurrent combatants per Arena match.</summary>
        public const int MaxSessionPlayers = 4;

        /// <summary>Maximum connection handshake wait time in seconds before aborting cleanly.</summary>
        public const float ConnectionTimeoutSeconds = 10.0f;

        /// <summary>Regex pattern enforcing 3..16 alphanumeric, underscore, or hyphen Room Codes.</summary>
        public const string RoomCodeRegexPattern = "^[A-Za-z0-9_-]{3,16}$";

        private static readonly Regex RoomCodeRegex = new Regex(RoomCodeRegexPattern, RegexOptions.Compiled);

        [Header("Prefabs & Scene Dependencies")]
        [Tooltip("Networked Player Capsule prefab spawned once per local peer.")]
        [SerializeField] private NetworkObject _playerAvatarPrefab;

        [Tooltip("Zero-allocation network object pool provider attached to the session manager.")]
        [SerializeField] private FusionObjectPoolProvider _poolProvider;

        [Tooltip("Arena spawn point selector maximizing distance from living opponents.")]
        [SerializeField] private ArenaSpawnManager _spawnManager;

        [Tooltip("Local PC/Mobile input poller supplying 60Hz NetworkInputData.")]
        [SerializeField] private LocalInputPoller _inputPoller;

        [Tooltip("Authoritative MatchManager notified when peers disconnect.")]
        [SerializeField] private MatchManager _matchManager;

        private readonly Vector3[] _livingPositionsBuffer = new Vector3[MaxSessionPlayers];
        private NetworkRunner _runner;
        private GameObject _runnerHostObject;
        private NetworkObject _localPlayerAvatar;
        private IInputFocusService _focusService;
        private Func<StartGameArgs, Task<StartGameResult>> _startGameRunnerOverride;
        private float _timeoutSecondsOverride = ConnectionTimeoutSeconds;
        private bool _localAvatarSpawned;
        private bool _disconnectNotified;

        /// <inheritdoc />
        public event Action<string> OnSessionDisconnected;

        /// <inheritdoc />
        public event Action<PlayerRef> OnLocalPlayerSpawned;

        /// <summary>Raised whenever any peer disconnects from the active Shared Mode session.</summary>
        public event Action<PlayerRef> OnPeerDisconnected;

        /// <inheritdoc />
        public bool IsSessionActive => _runner != null && _runner.IsRunning;

        /// <summary>Gets the active <see cref="NetworkRunner"/> instance when connected.</summary>
        public NetworkRunner ActiveRunner => _runner;

        /// <summary>Gets the spawned local player <see cref="NetworkObject"/> when present.</summary>
        public NetworkObject LocalPlayerAvatar => _localPlayerAvatar;

        /// <summary>Injects optional focus/input/spawn/match dependencies for runtime wiring and integration tests.</summary>
        public void ConfigureDependencies(
            FusionObjectPoolProvider poolProvider,
            ArenaSpawnManager spawnManager,
            LocalInputPoller inputPoller,
            IInputFocusService focusService,
            MatchManager matchManager = null)
        {
            if (poolProvider != null) _poolProvider = poolProvider;
            if (spawnManager != null) _spawnManager = spawnManager;
            if (inputPoller != null) _inputPoller = inputPoller;
            if (matchManager != null) _matchManager = matchManager;
            _focusService = focusService;
        }

        /// <summary>
        /// Injects deterministic start-task and timeout overrides so watchdog timeouts and room-full
        /// errors can be verified in fast automated tests without live cloud latency.
        /// </summary>
        public void ConfigureSessionTestHooks(
            Func<StartGameArgs, Task<StartGameResult>> startGameRunnerOverride,
            float timeoutSecondsOverride = ConnectionTimeoutSeconds)
        {
            _startGameRunnerOverride = startGameRunnerOverride;
            _timeoutSecondsOverride = Mathf.Max(0.01f, timeoutSecondsOverride);
        }

        /// <summary>
        /// Validates a user-entered Room Code. Empty/null is valid when <paramref name="allowEmptyQuickJoin"/> is true.
        /// </summary>
        public static bool ValidateRoomCode(string roomCode, bool allowEmptyQuickJoin = true)
        {
            if (string.IsNullOrEmpty(roomCode))
            {
                return allowEmptyQuickJoin;
            }

            return RoomCodeRegex.IsMatch(roomCode.Trim());
        }

        /// <summary>
        /// Constructs deterministic <see cref="StartGameArgs"/> for Photon Fusion 2 Shared Mode (4 players max).
        /// </summary>
        public static StartGameArgs BuildStartGameArgs(
            string roomCode,
            SceneRef sceneRef,
            INetworkObjectProvider objectProvider,
            INetworkSceneManager sceneManager)
        {
            string normalizedRoom = string.IsNullOrWhiteSpace(roomCode)
                ? null
                : roomCode.Trim().ToUpperInvariant();

            return new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = normalizedRoom,
                PlayerCount = MaxSessionPlayers,
                Scene = sceneRef,
                ObjectProvider = objectProvider,
                SceneManager = sceneManager
            };
        }

        /// <summary>
        /// Pure guard verifying that a peer in <see cref="GameMode.Shared"/> spawns ONLY its own local avatar once.
        /// </summary>
        public static bool ShouldSpawnLocalAvatar(
            PlayerRef joinedPlayer,
            PlayerRef localPlayer,
            bool alreadySpawnedForLocal)
        {
            return joinedPlayer != PlayerRef.None
                   && joinedPlayer == localPlayer
                   && !alreadySpawnedForLocal;
        }

        /// <inheritdoc />
        public async Task<bool> StartSharedSessionAsync(string roomCode)
        {
            _disconnectNotified = false;
            if (!ValidateRoomCode(roomCode, allowEmptyQuickJoin: true))
            {
                NotifyDisconnectOnce("InvalidRoomCode");
                return false;
            }

            PrepareChildRunnerAndPool();
            SceneRef currentScene = SceneRef.FromIndex(Mathf.Max(0, SceneManager.GetActiveScene().buildIndex));
            if (!_runnerHostObject.TryGetComponent(out INetworkSceneManager sceneMgr))
            {
                sceneMgr = _runnerHostObject.AddComponent<NetworkSceneManagerDefault>();
            }

            StartGameArgs args = BuildStartGameArgs(roomCode, currentScene, _poolProvider, sceneMgr);
            Task<StartGameResult> startTask = _startGameRunnerOverride != null
                ? _startGameRunnerOverride(args)
                : _runner.StartGame(args);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(_timeoutSecondsOverride));

            Task finished = await Task.WhenAny(startTask, timeoutTask);
            if (finished != startTask)
            {
                await ShutdownSessionAsync("ConnectionTimeout");
                return false;
            }

            StartGameResult result = await startTask;
            if (!result.Ok)
            {
                await ShutdownSessionAsync(result.ShutdownReason.ToString());
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public async Task ShutdownSessionAsync(string reason = "UserLeft")
        {
            ResetLocalSessionState();
            if (_runner != null)
            {
                NetworkRunner activeRunner = _runner;
                GameObject runnerHost = _runnerHostObject;
                _runner = null;
                _runnerHostObject = null;

                if (activeRunner.IsRunning)
                {
                    await activeRunner.Shutdown(destroyGameObject: true);
                }
                else if (runnerHost != null)
                {
                    Destroy(runnerHost);
                }
            }

            NotifyDisconnectOnce(reason);
        }

        /// <inheritdoc />
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            PlayerRef local = runner != null ? runner.LocalPlayer : PlayerRef.None;
            if (!ShouldSpawnLocalAvatar(player, local, _localAvatarSpawned))
            {
                return;
            }

            _localAvatarSpawned = true;
            if (_playerAvatarPrefab == null)
            {
                return;
            }

            int fallbackIndex = Mathf.Abs(player.RawEncoded) % MaxSessionPlayers;
            Pose spawnPose = SelectOptimalSpawnPose(fallbackIndex);

            _localPlayerAvatar = runner.Spawn(_playerAvatarPrefab, spawnPose.position, spawnPose.rotation, player);
            _inputPoller?.SetLookAngles(spawnPose.rotation.eulerAngles.y, 0f);
            OnLocalPlayerSpawned?.Invoke(player);
        }

        /// <inheritdoc />
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            _matchManager?.NotifyPlayerLeft(player);
            OnPeerDisconnected?.Invoke(player);

            if (runner != null && player == runner.LocalPlayer &&
                _localPlayerAvatar != null && _localPlayerAvatar.HasStateAuthority)
            {
                runner.Despawn(_localPlayerAvatar);
                _localPlayerAvatar = null;
                _localAvatarSpawned = false;
            }
        }

        /// <inheritdoc />
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (_inputPoller != null)
            {
                NetworkInputData payload = _inputPoller.CollectNetworkInput();
                input.Set(payload);
            }
        }

        /// <inheritdoc />
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            ResetLocalSessionState();
            if (_runner == runner)
            {
                _runner = null;
            }

            if (_runnerHostObject != null)
            {
                Destroy(_runnerHostObject);
                _runnerHostObject = null;
            }

            NotifyDisconnectOnce(shutdownReason.ToString());
        }

        /// <inheritdoc />
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            NotifyDisconnectOnce(reason.ToString());
        }

        /// <inheritdoc />
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            NotifyDisconnectOnce(reason.ToString());
        }

        /// <inheritdoc />
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        /// <inheritdoc />
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        /// <inheritdoc />
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }

#pragma warning disable CS0618
        /// <summary>Handles legacy user simulation messages (unused in Shared Mode RPC pipeline).</summary>
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
#pragma warning restore CS0618

        /// <inheritdoc />
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }

        /// <inheritdoc />
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

        /// <inheritdoc />
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

        /// <inheritdoc />
        public void OnConnectedToServer(NetworkRunner runner) { }

        /// <inheritdoc />
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

        /// <inheritdoc />
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

        /// <inheritdoc />
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

        /// <inheritdoc />
        public void OnSceneLoadDone(NetworkRunner runner) { }

        /// <inheritdoc />
        public void OnSceneLoadStart(NetworkRunner runner) { }

        internal Pose SelectOptimalSpawnPose(int fallbackIndex)
        {
            if (_spawnManager == null)
            {
                return new Pose(new Vector3(16f, ArenaSpawnManager.CanonicalSpawnElevationY, 0f), Quaternion.Euler(0f, -90f, 0f));
            }

            int livingCount = DamageReceiverRegistry.CopyLivingPositionsNonAlloc(_livingPositionsBuffer);
            ReadOnlySpan<Vector3> livingSpan = new ReadOnlySpan<Vector3>(_livingPositionsBuffer, 0, livingCount);
            return _spawnManager.GetSpawnPose(fallbackIndex, livingSpan);
        }

        private void PrepareChildRunnerAndPool()
        {
            if (_poolProvider == null && !TryGetComponent(out _poolProvider))
            {
                _poolProvider = gameObject.AddComponent<FusionObjectPoolProvider>();
            }

            _poolProvider.PrewarmConfiguredPrefabs();

            if (_runnerHostObject != null)
            {
                Destroy(_runnerHostObject);
            }

            _runnerHostObject = new GameObject("FusionSessionRunner");
            _runnerHostObject.transform.SetParent(transform, worldPositionStays: false);
            _runner = _runnerHostObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);
        }

        private void ResetLocalSessionState()
        {
            _localAvatarSpawned = false;
            _localPlayerAvatar = null;
            _focusService?.ResetAllFlags();
        }

        private void NotifyDisconnectOnce(string reason)
        {
            if (_disconnectNotified)
            {
                return;
            }

            _disconnectNotified = true;
            OnSessionDisconnected?.Invoke(string.IsNullOrEmpty(reason) ? "Disconnected" : reason);
        }
    }
}
