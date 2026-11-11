using System;
using System.Collections.Generic;
using UnityEngine;

namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Single serialized action-to-control-path pair inside keybindings.json (ADR-0002).
    /// Encapsulates fields via <see cref="SerializeField"/> with read-only C# properties.
    /// </summary>
    [Serializable]
    public sealed class KeybindingEntry
    {
        [SerializeField] private string _action = string.Empty;
        [SerializeField] private string _bindingPath = string.Empty;

        /// <summary>Initializes an empty <see cref="KeybindingEntry"/> for Unity JsonUtility deserialization.</summary>
        public KeybindingEntry()
        {
        }

        /// <summary>Initializes a populated <see cref="KeybindingEntry"/>.</summary>
        public KeybindingEntry(string action, string bindingPath)
        {
            _action = action ?? string.Empty;
            _bindingPath = bindingPath ?? string.Empty;
        }

        /// <summary>Gets the serialized enum name of <see cref="GameInputAction"/>.</summary>
        public string Action => _action;

        /// <summary>Gets the Unity Input System control path (e.g., "&lt;Keyboard&gt;/q").</summary>
        public string BindingPath => _bindingPath;
    }

    /// <summary>
    /// Root JSON schema persisted to Application.persistentDataPath/keybindings.json (Story 3-1; TR-input-001).
    /// </summary>
    [Serializable]
    public sealed class KeybindingSaveData
    {
        /// <summary>Current schema version for forward-compatible migrations.</summary>
        public const int CurrentSchemaVersion = 1;

        [SerializeField] private int _schemaVersion = CurrentSchemaVersion;
        [SerializeField] private int _mobilePadMode = (int)MobilePadVisibilityMode.Auto;
        [SerializeField] private string _mobileControlMode = nameof(MobileControlMode.Joystick);
        [SerializeField] private float _mouseSensitivity = 1.0f;
        [SerializeField] private List<KeybindingEntry> _bindings = new List<KeybindingEntry>();

        /// <summary>Initializes an empty <see cref="KeybindingSaveData"/> for Unity JsonUtility deserialization.</summary>
        public KeybindingSaveData()
        {
        }

        /// <summary>Initializes a populated <see cref="KeybindingSaveData"/> snapshot.</summary>
        public KeybindingSaveData(
            int schemaVersion,
            int mobilePadMode,
            string mobileControlMode,
            float mouseSensitivity,
            List<KeybindingEntry> bindings)
        {
            _schemaVersion = schemaVersion;
            _mobilePadMode = mobilePadMode;
            _mobileControlMode = mobileControlMode ?? nameof(MobileControlMode.Joystick);
            _mouseSensitivity = mouseSensitivity;
            _bindings = bindings ?? new List<KeybindingEntry>();
        }

        /// <summary>Gets the schema version integer stored on disk.</summary>
        public int SchemaVersion => _schemaVersion;

        /// <summary>Gets the Mobile Action Pad visibility mode (0=Auto, 1=ForceOn, 2=ForceOff).</summary>
        public int MobilePadMode => _mobilePadMode;

        /// <summary>Gets the serialized <see cref="MobileControlMode"/> name ("Joystick" or "DPad").</summary>
        public string MobileControlModeName => _mobileControlMode;

        /// <summary>Gets the mouse/touch look sensitivity stored on disk.</summary>
        public float MouseSensitivity => _mouseSensitivity;

        /// <summary>Gets the serialized list of action-to-path bindings.</summary>
        public IReadOnlyList<KeybindingEntry> Bindings => _bindings;
    }
}
