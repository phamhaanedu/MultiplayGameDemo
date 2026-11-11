using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Manages PC keybindings, mouse sensitivity, and mobile control preferences with atomic .tmp JSON
    /// persistence, .corrupt.bak recovery, missing-action backfill, and conflict-swapping rebinds
    /// (Story 3-1; ADR-0002; TR-input-001).
    /// </summary>
    public sealed class KeybindingManager : IKeybindingService
    {
        /// <summary>Canonical file name written inside the persistence directory.</summary>
        public const string FileName = "keybindings.json";

        /// <summary>Temporary staging file name used for atomic write-then-replace.</summary>
        public const string TempFileName = "keybindings.json.tmp";

        /// <summary>Backup file name written when a corrupt keybindings.json is detected.</summary>
        public const string CorruptBackupFileName = "keybindings.json.corrupt.bak";

        /// <summary>Minimum allowed mouse/touch look sensitivity.</summary>
        public const float MinSensitivity = 0.1f;

        /// <summary>Maximum allowed mouse/touch look sensitivity.</summary>
        public const float MaxSensitivity = 5.0f;

        private static readonly GameInputAction[] AllActions =
            (GameInputAction[])Enum.GetValues(typeof(GameInputAction));

        private static readonly string[] DefaultBindingPaths =
        {
            "<Keyboard>/w",
            "<Keyboard>/s",
            "<Keyboard>/a",
            "<Keyboard>/d",
            "<Keyboard>/space",
            "<Keyboard>/q",
            "<Keyboard>/e",
            "<Keyboard>/2",
            "<Mouse>/leftButton",
            "<Keyboard>/1",
            "<Keyboard>/3",
            "<Mouse>/rightButton",
            "<Keyboard>/v",
            "<Keyboard>/enter"
        };

        private static readonly HashSet<string> ReservedSystemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "<Keyboard>/escape", "<Keyboard>/esc",
            "<Keyboard>/f1", "<Keyboard>/f2", "<Keyboard>/f3", "<Keyboard>/f4",
            "<Keyboard>/f5", "<Keyboard>/f6", "<Keyboard>/f7", "<Keyboard>/f8",
            "<Keyboard>/f9", "<Keyboard>/f10", "<Keyboard>/f11", "<Keyboard>/f12",
            "<Keyboard>/leftMeta", "<Keyboard>/rightMeta",
            "<Keyboard>/leftWindows", "<Keyboard>/rightWindows",
            "<Keyboard>/leftCommand", "<Keyboard>/rightCommand",
            "<Keyboard>/printScreen"
        };

        private readonly Dictionary<GameInputAction, string> _bindings =
            new Dictionary<GameInputAction, string>(16);

        private readonly string _directoryPath;
        private float _mouseSensitivity = 1.0f;
        private MobileControlMode _mobileControlMode = MobileControlMode.Joystick;
        private MobilePadVisibilityMode _mobilePadMode = MobilePadVisibilityMode.Auto;

        /// <inheritdoc />
        public event Action OnBindingsChanged;

        /// <summary>
        /// Initializes a new <see cref="KeybindingManager"/> targeting the given persistence directory
        /// (or <see cref="Application.persistentDataPath"/> when null/empty) and loads saved bindings.
        /// </summary>
        public KeybindingManager(string saveDirectoryPath = null)
        {
            _directoryPath = string.IsNullOrWhiteSpace(saveDirectoryPath)
                ? Application.persistentDataPath
                : saveDirectoryPath;

            ApplyDefaultMapInMemory();
            Load();
        }

        /// <inheritdoc />
        public string SaveFilePath => Path.Combine(_directoryPath, FileName);

        /// <summary>Gets the full path to the temporary atomic staging file.</summary>
        public string TempSaveFilePath => Path.Combine(_directoryPath, TempFileName);

        /// <summary>Gets the full path to the corrupt-file backup copy.</summary>
        public string CorruptBackupFilePath => Path.Combine(_directoryPath, CorruptBackupFileName);

        /// <inheritdoc />
        public float MouseSensitivity
        {
            get => _mouseSensitivity;
            set
            {
                float sanitized = float.IsNaN(value) || float.IsInfinity(value) ? 1.0f : value;
                float clamped = Mathf.Clamp(sanitized, MinSensitivity, MaxSensitivity);
                if (!Mathf.Approximately(_mouseSensitivity, clamped))
                {
                    _mouseSensitivity = clamped;
                    Save();
                }
            }
        }

        /// <inheritdoc />
        public MobileControlMode ActiveMobileControlMode
        {
            get => _mobileControlMode;
            set
            {
                if (_mobileControlMode != value)
                {
                    _mobileControlMode = value;
                    Save();
                }
            }
        }

        /// <inheritdoc />
        public MobilePadVisibilityMode MobilePadMode
        {
            get => _mobilePadMode;
            set
            {
                MobilePadVisibilityMode valid = Enum.IsDefined(typeof(MobilePadVisibilityMode), value)
                    ? value
                    : MobilePadVisibilityMode.Auto;
                if (_mobilePadMode != valid)
                {
                    _mobilePadMode = valid;
                    Save();
                }
            }
        }

        /// <inheritdoc />
        public string GetBindingPath(GameInputAction action)
        {
            return _bindings.TryGetValue(action, out string path) ? path : GetDefaultBindingPath(action);
        }

        /// <inheritdoc />
        public bool TryRebindAction(GameInputAction action, string newBindingPath)
        {
            if (string.IsNullOrWhiteSpace(newBindingPath) || IsReservedSystemPath(newBindingPath))
            {
                return false;
            }

            string normalized = newBindingPath.Trim();
            string previousPath = GetBindingPath(action);
            if (string.Equals(previousPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (TryFindActionWithBinding(normalized, out GameInputAction conflictingAction) && conflictingAction != action)
            {
                _bindings[conflictingAction] = previousPath;
            }

            _bindings[action] = normalized;
            Save();
            return true;
        }

        /// <inheritdoc />
        public void ResetToDefaults()
        {
            ApplyDefaultMapInMemory();
            Save();
        }

        /// <inheritdoc />
        public void Load()
        {
            try
            {
                if (!File.Exists(SaveFilePath))
                {
                    ApplyDefaultMapInMemory();
                    TrySaveSafely();
                    return;
                }

                string rawJson = File.ReadAllText(SaveFilePath, Encoding.UTF8);
                if (!TryApplyLoadedJson(rawJson, out bool needsRepairWrite))
                {
                    Debug.LogWarning("[KeybindingManager] Corrupt or incompatible keybindings.json detected; backing up and restoring defaults.");
                    BackupCorruptFileSafely();
                    ApplyDefaultMapInMemory();
                    TrySaveSafely();
                    return;
                }

                if (needsRepairWrite)
                {
                    Debug.LogWarning("[KeybindingManager] Backfilled missing action IDs in keybindings.json.");
                    TrySaveSafely();
                    return;
                }

                OnBindingsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KeybindingManager] Failed reading keybindings.json ({ex.Message}); backing up and restoring defaults.");
                BackupCorruptFileSafely();
                ApplyDefaultMapInMemory();
                TrySaveSafely();
            }
        }

        /// <inheritdoc />
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_directoryPath);
                KeybindingSaveData dto = BuildSaveDataDto();
                string json = JsonUtility.ToJson(dto, prettyPrint: true);

                File.WriteAllText(TempSaveFilePath, json, Encoding.UTF8);
                CommitTempFileAtomically();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KeybindingManager] Could not write keybindings.json ({ex.Message}); keeping in-memory bindings.");
            }

            OnBindingsChanged?.Invoke();
        }

        /// <summary>
        /// Returns the canonical default Input System control path for any <see cref="GameInputAction"/>.
        /// </summary>
        public static string GetDefaultBindingPath(GameInputAction action)
        {
            int index = (int)action;
            return index >= 0 && index < DefaultBindingPaths.Length ? DefaultBindingPaths[index] : string.Empty;
        }

        private void ApplyDefaultMapInMemory()
        {
            _mouseSensitivity = 1.0f;
            _mobileControlMode = MobileControlMode.Joystick;
            _mobilePadMode = MobilePadVisibilityMode.Auto;
            _bindings.Clear();

            for (int i = 0; i < AllActions.Length; i++)
            {
                GameInputAction action = AllActions[i];
                _bindings[action] = GetDefaultBindingPath(action);
            }
        }

        private bool TryApplyLoadedJson(string rawJson, out bool backfilledMissing)
        {
            backfilledMissing = false;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            KeybindingSaveData dto = JsonUtility.FromJson<KeybindingSaveData>(rawJson);
            if (dto == null || dto.SchemaVersion != KeybindingSaveData.CurrentSchemaVersion ||
                dto.Bindings == null || dto.Bindings.Count == 0)
            {
                return false;
            }

            ApplyScalarPreferences(dto);
            return PopulateAndBackfillBindings(dto.Bindings, out backfilledMissing);
        }

        private void ApplyScalarPreferences(KeybindingSaveData dto)
        {
            float sens = float.IsNaN(dto.MouseSensitivity) || float.IsInfinity(dto.MouseSensitivity)
                ? 1.0f
                : dto.MouseSensitivity;
            _mouseSensitivity = Mathf.Clamp(sens, MinSensitivity, MaxSensitivity);

            if (Enum.TryParse(dto.MobileControlModeName, ignoreCase: true, out MobileControlMode parsedControl))
            {
                _mobileControlMode = parsedControl;
            }

            _mobilePadMode = Enum.IsDefined(typeof(MobilePadVisibilityMode), dto.MobilePadMode)
                ? (MobilePadVisibilityMode)dto.MobilePadMode
                : MobilePadVisibilityMode.Auto;
        }

        private bool PopulateAndBackfillBindings(IReadOnlyList<KeybindingEntry> entries, out bool backfilledMissing)
        {
            backfilledMissing = false;
            HashSet<string> usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<GameInputAction, string> parsedMap = new Dictionary<GameInputAction, string>(16);

            for (int i = 0; i < entries.Count; i++)
            {
                if (!TryReadSingleEntry(entries[i], usedPaths, parsedMap))
                {
                    return false;
                }
            }

            backfilledMissing = BackfillMissingActions(parsedMap, usedPaths);
            _bindings.Clear();
            foreach (KeyValuePair<GameInputAction, string> kvp in parsedMap)
            {
                _bindings[kvp.Key] = kvp.Value;
            }

            return _bindings.Count == AllActions.Length;
        }

        private static bool TryReadSingleEntry(
            KeybindingEntry entry,
            HashSet<string> usedPaths,
            Dictionary<GameInputAction, string> parsedMap)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Action) || string.IsNullOrWhiteSpace(entry.BindingPath))
            {
                return false;
            }

            if (!Enum.TryParse(entry.Action, ignoreCase: true, out GameInputAction parsedAction))
            {
                return true;
            }

            string trimmedPath = entry.BindingPath.Trim();
            if (IsReservedSystemPath(trimmedPath) || parsedMap.ContainsKey(parsedAction) || !usedPaths.Add(trimmedPath))
            {
                return false;
            }

            parsedMap[parsedAction] = trimmedPath;
            return true;
        }

        private static bool BackfillMissingActions(
            Dictionary<GameInputAction, string> parsedMap,
            HashSet<string> usedPaths)
        {
            bool anyMissing = false;
            for (int i = 0; i < AllActions.Length; i++)
            {
                GameInputAction action = AllActions[i];
                if (parsedMap.ContainsKey(action))
                {
                    continue;
                }

                anyMissing = true;
                string candidate = FindUnusedDefaultPath(action, usedPaths);
                usedPaths.Add(candidate);
                parsedMap[action] = candidate;
            }

            return anyMissing;
        }

        private static string FindUnusedDefaultPath(GameInputAction action, HashSet<string> usedPaths)
        {
            string preferred = GetDefaultBindingPath(action);
            if (!usedPaths.Contains(preferred))
            {
                return preferred;
            }

            for (int i = 0; i < DefaultBindingPaths.Length; i++)
            {
                if (!usedPaths.Contains(DefaultBindingPaths[i]))
                {
                    return DefaultBindingPaths[i];
                }
            }

            return preferred;
        }

        private KeybindingSaveData BuildSaveDataDto()
        {
            List<KeybindingEntry> entries = new List<KeybindingEntry>(AllActions.Length);
            for (int i = 0; i < AllActions.Length; i++)
            {
                GameInputAction action = AllActions[i];
                entries.Add(new KeybindingEntry(action.ToString(), GetBindingPath(action)));
            }

            return new KeybindingSaveData(
                KeybindingSaveData.CurrentSchemaVersion,
                (int)_mobilePadMode,
                _mobileControlMode.ToString(),
                _mouseSensitivity,
                entries);
        }

        private void TrySaveSafely()
        {
            Save();
        }

        private void BackupCorruptFileSafely()
        {
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    File.Copy(SaveFilePath, CorruptBackupFilePath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KeybindingManager] Could not create .corrupt.bak backup ({ex.Message}).");
            }
        }

        private void CommitTempFileAtomically()
        {
            if (File.Exists(SaveFilePath))
            {
                File.Replace(TempSaveFilePath, SaveFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(TempSaveFilePath, SaveFilePath);
            }

            if (File.Exists(TempSaveFilePath))
            {
                File.Delete(TempSaveFilePath);
            }
        }

        private bool TryFindActionWithBinding(string bindingPath, out GameInputAction foundAction)
        {
            for (int i = 0; i < AllActions.Length; i++)
            {
                GameInputAction candidate = AllActions[i];
                if (_bindings.TryGetValue(candidate, out string existing) &&
                    string.Equals(existing, bindingPath, StringComparison.OrdinalIgnoreCase))
                {
                    foundAction = candidate;
                    return true;
                }
            }

            foundAction = default;
            return false;
        }

        private static bool IsReservedSystemPath(string bindingPath)
        {
            return ReservedSystemPaths.Contains(bindingPath.Trim());
        }
    }
}
