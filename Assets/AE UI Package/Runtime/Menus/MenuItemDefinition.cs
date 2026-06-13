using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlterEyes.UI.Menus
{
    /// <summary>
    /// One control in a settings or cheats menu — the declarative, flat, force-text record an agent
    /// authors (no delegates, no GUIDs). Addressed by <see cref="category"/>/<see cref="name"/>.
    /// The presenter builds a widget from it; <see cref="UserSettingsService"/> reads/writes its value
    /// and emits a change signal so game code can react with <c>Signals.On</c>.
    /// </summary>
    [Serializable]
    public class MenuItemDefinition
    {
        [Tooltip("Addressing category — the value's signal stream name is \"<category>/<name>\".")]
        public string category = CategoryNameId.DefaultCategory;
        [Tooltip("Addressing name within the category.")]
        public string name = CategoryNameId.DefaultName;

        public MenuControlKind kind = MenuControlKind.Toggle;

        [Tooltip("Display label shown next to the control.")]
        public string label;
        [Tooltip("Optional helper / tooltip text.")]
        public string tooltip;
        [Tooltip("Tab / sidebar group this control belongs to (one of the catalog's groups). Blank = ungrouped.")]
        public string group;

        [Tooltip("When true the value persists through the settings store. Set false for values the game owns (binding mode).")]
        public bool persisted = true;

        // ---- value config (only the relevant fields are used per kind) ----
        public float min = 0f;
        public float max = 1f;
        public float step = 1f;
        [Tooltip("Slider/Stepper: snap to whole numbers (value type becomes Int).")]
        public bool wholeNumbers;
        [Tooltip("Stringified default value (parsed per value kind). Bool: True/False. Dropdown: selected index.")]
        public string defaultValue;
        [Tooltip("Dropdown options, in order. The persisted value is the selected index.")]
        public List<string> options = new List<string>();

        // ---- slider event shaping (CBN parity: OnSliderValueChanged vs OnSliderRelease) ----
        [Tooltip("Slider: emit a preview signal continuously while dragging (does not persist).")]
        public bool emitOnDrag = true;
        [Tooltip("Slider: emit the committed signal (and persist) on release.")]
        public bool emitOnRelease = true;

        // ---- input rebinding (KeyRebind only) ----
        [Tooltip("KeyRebind: 'ActionMap/Action' path into the bound InputActionAsset.")]
        public string inputAction;
        [Tooltip("KeyRebind: binding index within the action (composite parts use sub-indices).")]
        public int bindingIndex;

        public string Id => $"{Category}/{Name}";
        public string Category => string.IsNullOrWhiteSpace(category) ? CategoryNameId.DefaultCategory : category.Trim();
        public string Name => string.IsNullOrWhiteSpace(name) ? CategoryNameId.DefaultName : name.Trim();

        /// <summary> The runtime value type this control reads/writes. </summary>
        public MenuValueKind ValueKind
        {
            get
            {
                switch (kind)
                {
                    case MenuControlKind.Toggle:
                    case MenuControlKind.Switch: return MenuValueKind.Bool;
                    case MenuControlKind.Slider: return wholeNumbers ? MenuValueKind.Int : MenuValueKind.Float;
                    case MenuControlKind.Stepper: return wholeNumbers ? MenuValueKind.Int : MenuValueKind.Float;
                    case MenuControlKind.Dropdown: return MenuValueKind.Int;
                    case MenuControlKind.KeyRebind: return MenuValueKind.String;
                    default: return MenuValueKind.None; // Button, Label
                }
            }
        }

        /// <summary> True for controls that hold a persisted value (everything but Button/Label). </summary>
        public bool HasValue => ValueKind != MenuValueKind.None;
    }
}
