using System;

namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Contract for querying, rebinding, and atomically persisting player keybindings and control preferences.
    /// </summary>
    public interface IKeybindingService
    {
        /// <summary>Raised whenever bindings, mouse sensitivity, or mobile control preferences change or reload.</summary>
        event Action OnBindingsChanged;

        /// <summary>Gets or sets look sensitivity, automatically clamped to [0.1, 5.0].</summary>
        float MouseSensitivity { get; set; }

        /// <summary>Gets or sets the active mobile virtual movement control mode.</summary>
        MobileControlMode ActiveMobileControlMode { get; set; }

        /// <summary>Gets or sets the Mobile Action Pad visibility mode (0=Auto, 1=ForceOn, 2=ForceOff).</summary>
        MobilePadVisibilityMode MobilePadMode { get; set; }

        /// <summary>Gets the full path to keybindings.json.</summary>
        string SaveFilePath { get; }

        /// <summary>Returns the currently bound Input System control path for the given action.</summary>
        string GetBindingPath(GameInputAction action);

        /// <summary>
        /// Rebinds the specified action to a new control path, automatically swapping with any conflicting action.
        /// Rejects reserved system keys (Escape, F1-F12, OS Meta keys).
        /// </summary>
        bool TryRebindAction(GameInputAction action, string newBindingPath);

        /// <summary>Restores all 14 actions, sensitivity, and mobile mode to canonical defaults and saves.</summary>
        void ResetToDefaults();

        /// <summary>Loads bindings from disk, recovering gracefully to defaults if missing or corrupt.</summary>
        void Load();

        /// <summary>Atomically writes current bindings to disk via a temporary .tmp file.</summary>
        void Save();
    }
}
