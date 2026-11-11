using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace CapsuleArena.Cloud
{
    /// <summary>
    /// Non-blocking PlayFab CustomID authentication service managing persistent device identity in
    /// <c>playfab_identity.json</c> (with <c>-profile &lt;id&gt;</c> multi-instance isolation), exponential retry
    /// backoff (<c>1s, 2s, 4s</c>, max 3 retries) on transient <c>5xx</c>/network failures, immediate non-retry on
    /// <c>4xx</c> client/configuration errors, PlayFab display-name sync, and seamless <see cref="AuthState.OfflineGuest"/>
    /// fallback that never blocks Photon Fusion 2 room creation or joining (Story 13-1; ADR-0008; TR-playfab-001).
    /// </summary>
    public sealed class PlayFabAuthService : MonoBehaviour
    {
        /// <summary>Default file name used under <see cref="Application.persistentDataPath"/> for identity &amp; pending stats.</summary>
        public const string IdentityFileName = "playfab_identity.json";

        /// <summary>Maximum number of transient network/5xx retry backoff steps (1s, 2s, 4s).</summary>
        public const int MaxRetryBackoffSteps = 3;

        [Header("PlayFab Configuration")]
        [Tooltip("Optional PlayFab TitleId. Leave blank in offline/local environments to enter OfflineGuest mode cleanly.")]
        [SerializeField] private string _titleId = string.Empty;

        [Tooltip("Automatically starts non-blocking authentication on Start() in Play Mode.")]
        [SerializeField] private bool _authenticateOnStart = true;

        private IPlayFabClientAdapter _clientAdapter;
        private PlayFabRestClientAdapter _cachedRestAdapter;
        private Func<float, Task> _delayAsyncOverride;
        private PlayFabIdentitySaveData _identityData;
        private string _storageFilePath;
        private AuthState _currentState = AuthState.Uninitialized;
        private string _playFabId = string.Empty;
        private string _sessionTicket = string.Empty;
        private int _lastAttemptCount;

        /// <summary>Raised whenever <see cref="CurrentState"/> transitions.</summary>
        public event Action<AuthState> OnAuthStateChanged;

        /// <summary>Raised when a valid PlayFab session ticket is acquired (triggers pending stats auto-flush).</summary>
        public event Action OnAuthenticated;

        /// <summary>Gets the configured PlayFab TitleId.</summary>
        public string TitleId => _titleId ?? string.Empty;

        /// <summary>Gets the current authentication lifecycle state.</summary>
        public AuthState CurrentState => _currentState;

        /// <summary>Gets a value indicating whether PlayFab REST calls currently hold a valid session ticket.</summary>
        public bool IsAuthenticated => _currentState == AuthState.Authenticated && !string.IsNullOrEmpty(_sessionTicket);

        /// <summary>Gets a value indicating whether Photon Fusion multiplayer rooms can start (ALWAYS true per ADR-0008).</summary>
        public bool CanStartMultiplayerMatch => true;

        /// <summary>Gets the authenticated PlayFabId (or empty in <see cref="AuthState.OfflineGuest"/>).</summary>
        public string PlayFabId => _playFabId;

        /// <summary>Gets the active PlayFab SessionTicket (never persisted to disk).</summary>
        public string SessionTicket => _sessionTicket;

        /// <summary>Gets the persistent 32-character hex CustomID.</summary>
        public string CustomId => EnsureIdentityLoaded().CustomId;

        /// <summary>Gets the sanitized local display name ("Guest_XXXX" fallback when unset).</summary>
        public string DisplayName => EnsureIdentityLoaded().DisplayName;

        /// <summary>Gets the number of login attempts performed during the most recent <see cref="AuthenticateAsync"/> call.</summary>
        public int LastAttemptCount => _lastAttemptCount;

        /// <summary>Gets the loaded <see cref="PlayFabIdentitySaveData"/> record.</summary>
        public PlayFabIdentitySaveData IdentityData => EnsureIdentityLoaded();

        /// <summary>
        /// Computes exponential retry backoff delay in seconds: <c>1.0s * 2^attemptIndex</c> (<c>1.0s, 2.0s, 4.0s</c>).
        /// </summary>
        public static float CalculateBackoffDelaySec(int attemptIndex)
        {
            int clamped = Mathf.Clamp(attemptIndex, 0, 6);
            return 1.0f * Mathf.Pow(2f, clamped);
        }

        /// <summary>
        /// Resolves the identity JSON file name, appending <c>_&lt;profileId&gt;</c> when launched with <c>-profile &lt;id&gt;</c>
        /// so multiple local standalone test windows never collide on disk or share the same <c>CustomId</c> (ADR-0008).
        /// </summary>
        public static string ResolveDefaultIdentityFileName(string[] commandLineArgs = null)
        {
            string[] args = commandLineArgs ?? Environment.GetCommandLineArgs();
            string profileId = ExtractSanitizedProfileArg(args);
            return string.IsNullOrEmpty(profileId) ? IdentityFileName : $"playfab_identity_{profileId}.json";
        }

        /// <summary>
        /// Sanitizes a player display name to 3–16 alphanumeric/underscore characters, falling back to <c>Guest_XXXX</c>.
        /// </summary>
        public static string SanitizeDisplayName(string rawName, string customIdForFallback)
        {
            if (!string.IsNullOrWhiteSpace(rawName))
            {
                char[] buf = new char[16];
                int len = 0;
                for (int i = 0; i < rawName.Length && len < 16; i++)
                {
                    char c = rawName[i];
                    if (char.IsLetterOrDigit(c) || c == '_')
                    {
                        buf[len++] = c;
                    }
                }

                if (len >= 3)
                {
                    return new string(buf, 0, len);
                }
            }

            string suffix = !string.IsNullOrEmpty(customIdForFallback) && customIdForFallback.Length >= 4
                ? customIdForFallback.Substring(0, 4).ToUpperInvariant()
                : "0001";
            return $"Guest_{suffix}";
        }

        /// <summary>
        /// Returns the injected <see cref="IPlayFabClientAdapter"/> or a cached <see cref="PlayFabRestClientAdapter"/>
        /// bound to <see cref="TitleId"/> so sibling cloud services share the exact same TitleId configuration.
        /// </summary>
        public IPlayFabClientAdapter ResolveClientAdapter()
        {
            if (_clientAdapter != null)
            {
                return _clientAdapter;
            }

            string currentTitle = TitleId.Trim();
            if (_cachedRestAdapter == null || !string.Equals(_cachedRestAdapter.BoundTitleId, currentTitle, StringComparison.Ordinal))
            {
                _cachedRestAdapter = new PlayFabRestClientAdapter(currentTitle);
            }

            return _cachedRestAdapter;
        }

        private void Awake()
        {
            EnsureDefaultStoragePath();
            EnsureIdentityLoaded();
        }

        private void Start()
        {
            if (_authenticateOnStart && Application.isPlaying && _currentState == AuthState.Uninitialized)
            {
                _ = AuthenticateAsync();
            }
        }

        /// <summary>
        /// Injects custom adapter, storage path, TitleId, and optional delay function for deterministic unit testing.
        /// </summary>
        public void ConfigureForSession(
            IPlayFabClientAdapter adapter,
            string storageFilePath = null,
            string titleId = null,
            Func<float, Task> delayOverride = null)
        {
            _clientAdapter = adapter;
            _cachedRestAdapter = null;
            _storageFilePath = !string.IsNullOrEmpty(storageFilePath) ? storageFilePath : null;
            EnsureDefaultStoragePath();

            if (titleId != null)
            {
                _titleId = titleId;
            }

            _delayAsyncOverride = delayOverride;
            _identityData = null;
            EnsureIdentityLoaded();
        }

        /// <summary>Updates, persists, and (when authenticated) synchronizes the player's sanitized display name to PlayFab.</summary>
        public void SetDisplayName(string displayName)
        {
            PlayFabIdentitySaveData data = EnsureIdentityLoaded();
            string nextName = SanitizeDisplayName(displayName, data.CustomId);
            data.DisplayName = nextName;
            SaveIdentityAtomically();

            if (IsAuthenticated)
            {
                _ = ResolveClientAdapter().UpdateUserTitleDisplayNameAsync(_sessionTicket, nextName);
            }
        }

        /// <summary>
        /// Executes non-blocking <c>LoginWithCustomID</c> with <c>1s, 2s, 4s</c> exponential backoff on transient errors
        /// and immediate <see cref="AuthState.OfflineGuest"/> fallback on <c>4xx</c> or exhausted retries.
        /// </summary>
        public async Task<AuthState> AuthenticateAsync()
        {
            PlayFabIdentitySaveData data = EnsureIdentityLoaded();
            IPlayFabClientAdapter adapter = ResolveClientAdapter();
            TransitionState(AuthState.Authenticating);
            _lastAttemptCount = 0;

            for (int attempt = 0; attempt <= MaxRetryBackoffSteps; attempt++)
            {
                _lastAttemptCount++;
                PlayFabApiResponse<PlayFabLoginResult> response = await adapter.LoginWithCustomIdAsync(_titleId, data.CustomId);
                if (response.IsSuccess && !string.IsNullOrEmpty(response.Data.SessionTicket))
                {
                    await ApplyAuthenticatedSessionAsync(adapter, response.Data);
                    return _currentState;
                }

                if (IsNonRetryableClientError(response.HttpStatusCode) || attempt >= MaxRetryBackoffSteps)
                {
                    EnterOfflineGuestMode(response.ErrorCode, response.ErrorMessage);
                    return _currentState;
                }

                await WaitBackoffOrDefaultAsync(CalculateBackoffDelaySec(attempt));
            }

            EnterOfflineGuestMode("ExhaustedRetries", "Maximum login retries reached.");
            return _currentState;
        }

        /// <summary>Atomically writes <see cref="IdentityData"/> to <c>playfab_identity.json</c> via a <c>.tmp</c> swap.</summary>
        public void SaveIdentityAtomically()
        {
            EnsureDefaultStoragePath();
            PlayFabIdentitySaveData data = EnsureIdentityLoaded();
            string directory = Path.GetDirectoryName(_storageFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tmpPath = _storageFilePath + ".tmp";
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(tmpPath, json);
            if (File.Exists(_storageFilePath))
            {
                File.Delete(_storageFilePath);
            }

            File.Move(tmpPath, _storageFilePath);
        }

        internal async Task WaitBackoffOrDefaultAsync(float seconds)
        {
            if (_delayAsyncOverride != null)
            {
                await _delayAsyncOverride(seconds);
                return;
            }

            int ms = Mathf.Max(1, Mathf.RoundToInt(seconds * 1000f));
            await Task.Delay(ms);
        }

        internal static bool IsNonRetryableClientError(int httpStatusCode)
        {
            return httpStatusCode >= 400 && httpStatusCode < 500 && httpStatusCode != 429;
        }

        private async Task ApplyAuthenticatedSessionAsync(IPlayFabClientAdapter adapter, PlayFabLoginResult result)
        {
            _playFabId = result.PlayFabId;
            _sessionTicket = result.SessionTicket;
            if (!string.IsNullOrWhiteSpace(result.DisplayName))
            {
                _identityData.DisplayName = SanitizeDisplayName(result.DisplayName, _identityData.CustomId);
                SaveIdentityAtomically();
            }
            else if (!string.IsNullOrEmpty(_identityData.DisplayName))
            {
                await adapter.UpdateUserTitleDisplayNameAsync(_sessionTicket, _identityData.DisplayName);
            }

            TransitionState(AuthState.Authenticated);
            OnAuthenticated?.Invoke();
        }

        private void EnterOfflineGuestMode(string errorCode, string reason)
        {
            _playFabId = string.Empty;
            _sessionTicket = string.Empty;
            PlayFabIdentitySaveData data = EnsureIdentityLoaded();
            data.DisplayName = SanitizeDisplayName(data.DisplayName, data.CustomId);
            SaveIdentityAtomically();
            Debug.Log($"[PlayFabAuthService] Entering OfflineGuest ({data.DisplayName}): {errorCode} - {reason}");
            TransitionState(AuthState.OfflineGuest);
        }

        private PlayFabIdentitySaveData EnsureIdentityLoaded()
        {
            if (_identityData != null)
            {
                return _identityData;
            }

            EnsureDefaultStoragePath();
            _identityData = TryReadIdentityFromDisk() ?? new PlayFabIdentitySaveData();
            bool dirty = false;

            if (!IsValidCustomId(_identityData.CustomId))
            {
                _identityData.CustomId = Guid.NewGuid().ToString("N");
                dirty = true;
            }

            string sanitized = SanitizeDisplayName(_identityData.DisplayName, _identityData.CustomId);
            if (!string.Equals(_identityData.DisplayName, sanitized, StringComparison.Ordinal))
            {
                _identityData.DisplayName = sanitized;
                dirty = true;
            }

            if (dirty || !File.Exists(_storageFilePath))
            {
                SaveIdentityAtomically();
            }

            return _identityData;
        }

        private PlayFabIdentitySaveData TryReadIdentityFromDisk()
        {
            if (string.IsNullOrEmpty(_storageFilePath) || !File.Exists(_storageFilePath))
            {
                return null;
            }

            try
            {
                string raw = File.ReadAllText(_storageFilePath);
                return string.IsNullOrWhiteSpace(raw) ? null : JsonUtility.FromJson<PlayFabIdentitySaveData>(raw);
            }
            catch (Exception ex)
            {
                BackupCorruptIdentityFile();
                Debug.LogWarning($"[PlayFabAuthService] Recovered from corrupt identity JSON: {ex.Message}");
                return null;
            }
        }

        private void BackupCorruptIdentityFile()
        {
            string corruptBackup = _storageFilePath + ".corrupt.bak";
            try
            {
                if (File.Exists(corruptBackup))
                {
                    File.Delete(corruptBackup);
                }

                File.Move(_storageFilePath, corruptBackup);
            }
            catch
            {
                // Ignore secondary IO backup failures during recovery.
            }
        }

        private static string ExtractSanitizedProfileArg(string[] args)
        {
            if (args == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-profile", StringComparison.OrdinalIgnoreCase))
                {
                    return SanitizeProfileSuffix(args[i + 1]);
                }
            }

            return string.Empty;
        }

        private static string SanitizeProfileSuffix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            char[] buf = new char[16];
            int len = 0;
            for (int i = 0; i < raw.Length && len < 16; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    buf[len++] = c;
                }
            }

            return len > 0 ? new string(buf, 0, len) : string.Empty;
        }

        private static bool IsValidCustomId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length >= 16 && id.Length <= 64;
        }

        private void EnsureDefaultStoragePath()
        {
            if (string.IsNullOrEmpty(_storageFilePath))
            {
                _storageFilePath = Path.Combine(Application.persistentDataPath, ResolveDefaultIdentityFileName());
            }
        }

        private void TransitionState(AuthState next)
        {
            if (_currentState == next)
            {
                return;
            }

            _currentState = next;
            OnAuthStateChanged?.Invoke(_currentState);
        }
    }
}
