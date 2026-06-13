using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlterEyes.UI.Menus
{
    /// <summary>
    /// Wraps the New Input System interactive-rebinding flow and persists binding overrides through the
    /// settings <see cref="UserSettingsService.Store"/> (JSON per asset), so rebinds survive a restart and
    /// share the same swappable backend as every other setting. New Input System only.
    /// </summary>
    public static class InputRebindService
    {
        public const string OverridesCategory = "InputRebinds";

        private static string Key(InputActionAsset asset) => $"{OverridesCategory}/{asset.name}";

        /// <summary> Applies any persisted overrides for the asset. Call once on startup. </summary>
        public static void LoadOverrides(InputActionAsset asset)
        {
            if (asset == null) return;
            if (UserSettingsService.Store.TryGet(Key(asset), out string json) && !string.IsNullOrEmpty(json))
                asset.LoadBindingOverridesFromJson(json);
        }

        /// <summary> Persists the asset's current overrides. </summary>
        public static void SaveOverrides(InputActionAsset asset)
        {
            if (asset == null) return;
            UserSettingsService.Store.Set(Key(asset), asset.SaveBindingOverridesAsJson());
            UserSettingsService.Store.Save();
        }

        /// <summary> Human-readable display string for a binding (e.g. "Space", "Left Button"). </summary>
        public static string DisplayString(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return string.Empty;
            return action.GetBindingDisplayString(bindingIndex);
        }

        /// <summary>
        /// Starts an interactive rebind. Disables the action for the duration, invokes
        /// <paramref name="onComplete"/> (or <paramref name="onCancel"/>) on the main thread, and persists.
        /// Returns the operation so the caller can cancel it.
        /// </summary>
        public static InputActionRebindingExtensions.RebindingOperation StartRebind(
            InputAction action, int bindingIndex, InputActionAsset persistTo,
            Action onComplete = null, Action onCancel = null)
        {
            if (action == null) return null;
            action.Disable();
            return action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op =>
                {
                    op.Dispose();
                    action.Enable();
                    if (persistTo != null) SaveOverrides(persistTo);
                    onComplete?.Invoke();
                })
                .OnCancel(op =>
                {
                    op.Dispose();
                    action.Enable();
                    onCancel?.Invoke();
                })
                .Start();
        }

        /// <summary> Removes the override on a binding, restoring its default. </summary>
        public static void ResetBinding(InputAction action, int bindingIndex, InputActionAsset persistTo)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return;
            action.RemoveBindingOverride(bindingIndex);
            if (persistTo != null) SaveOverrides(persistTo);
        }
    }
}
