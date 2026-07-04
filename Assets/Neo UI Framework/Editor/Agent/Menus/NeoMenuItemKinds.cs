using System;
using System.Collections.Generic;
using Neo.UI.Menus;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Builds one control row for a menu-item kind. Bundles exactly the context the generator's old
    /// switch-based <c>BuildMenuRow</c> closed over, so a project-registered kind's build recipe reads
    /// like a normal generator method (see <see cref="UISpecGenerator.BuildToggleRow"/> for the built-in
    /// idiom a project can mirror).
    /// </summary>
    public delegate void MenuRowBuilder(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
        UnityEngine.InputSystem.InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report);

    /// <summary>
    /// One entry in the settings/cheats control vocabulary — the spec kind string
    /// ("toggle"/"slider"/…) plus everything the pipeline needs to parse, generate, export and bind it.
    /// Wave 7 Task 7.1 (audit E3, the sanctioned Phase-2 TODO at the old <c>BindingManifest.TypeForKind</c>):
    /// the largest fully-sealed subsystem the audit found, now a registry.
    /// <para>
    /// <see cref="controlKind"/> is the one place the closed <see cref="MenuControlKind"/> enum still
    /// shows through — the plan's explicit decision keeps that enum closed for the 8 built-ins rather
    /// than converting it to a string-keyed open type. A project-registered kind therefore registers
    /// with <c>controlKind: null</c>: it parses, round-trips through the JSON spec model, appears in
    /// <see cref="MenuItemSpec.Kinds"/> and the inspector's kind popup, and its <see cref="valueType"/>/
    /// <see cref="valueToTyped"/> work exactly like a built-in's — but <see cref="NeoMenuItemKinds.MapKind"/>
    /// has no runtime enum slot to give it, so a generated row falls back to a non-interactive Label
    /// (logged, never silent). Making a project's own kind fully interactive at runtime needs a Runtime/
    /// change (a way for <see cref="MenuItemDefinition"/> to carry a kind identity that survives without
    /// a <see cref="MenuControlKind"/> value, plus <c>MenuControlBinder</c>/<c>MenuWidgetLibrary</c>
    /// consulting it) — out of scope here per the plan's runtime-boundary caution; see CLAUDE.md /
    /// the Task 7.1 handoff notes for the exact shape of that follow-up.
    /// </para>
    /// </summary>
    public readonly struct MenuItemKindDescriptor
    {
        /// <summary> The spec kind string ("toggle", "slider", …) — also the JSON object key
        /// (<c>{ "toggle": { ... } }</c>) and the inspector popup's option text. </summary>
        public readonly string id;

        /// <summary> The runtime enum value this kind bakes into <see cref="MenuItemDefinition.kind"/>.
        /// Null for a project kind with no built-in enum slot (see the type doc's runtime-boundary
        /// note). </summary>
        public readonly MenuControlKind? controlKind;

        /// <summary> The C# value type a control of this kind reads/writes — "bool"/"float"/"int"/
        /// "none". Feeds both <see cref="UISpecGenerator"/>'s JSON typing (<see cref="valueToTyped"/>)
        /// and <c>BindingManifest.TypeForKind</c>. </summary>
        public readonly string valueType;

        /// <summary> Converts a stored default-value string to its canonical JSON value for export
        /// (e.g. "True" → <c>true</c>, "0.8" → <c>0.8</c>). Null for value-less kinds (button/label). </summary>
        public readonly Func<string, object> valueToTyped;

        /// <summary> Builds the actual row GameObject (+ attaches its binder) during generate. </summary>
        public readonly MenuRowBuilder buildRow;

        /// <summary> Optional: the <see cref="NeoUISettings"/> id database this kind's items pre-register
        /// into while a catalog is generated (e.g. toggle/switch → <c>toggleIds</c>), so the id picker
        /// sees them before the view section that embeds the catalog is built. Null = no pre-registration
        /// (label/rebind — rebind registers its own "_Rebind" button id from its row builder instead). </summary>
        public readonly Func<NeoUISettings, Neo.UI.IdDatabase> preRegisterDatabase;

        public MenuItemKindDescriptor(string id, MenuControlKind? controlKind, string valueType,
            Func<string, object> valueToTyped, MenuRowBuilder buildRow,
            Func<NeoUISettings, Neo.UI.IdDatabase> preRegisterDatabase = null)
        {
            this.id = id;
            this.controlKind = controlKind;
            this.valueType = valueType;
            this.valueToTyped = valueToTyped;
            this.buildRow = buildRow;
            this.preRegisterDatabase = preRegisterDatabase;
        }
    }

    /// <summary>
    /// The single source of truth for the settings/cheats control vocabulary — see
    /// <see cref="MenuItemKindDescriptor"/> for what an entry owns and the runtime-boundary caveat for
    /// project-registered kinds. Pattern R (<see cref="NeoKeyedRegistry{T}"/>), same shape as
    /// <see cref="NeoCatalogKinds"/>.
    /// </summary>
    public static class NeoMenuItemKinds
    {
        private static readonly NeoKeyedRegistry<MenuItemKindDescriptor> _registry =
            new NeoKeyedRegistry<MenuItemKindDescriptor>(
                d => d.id,
                builtins: Builtins,
                validate: d => d.buildRow != null,
                registryName: "NeoMenuItemKinds");

        // The 8 built-ins, in MenuControlKind's declaration order (Label=0 … KeyRebind=7) — this order
        // is load-bearing: MenuItemSpec.Kinds (registry-derived) feeds MenuCatalogInspector's kind popup,
        // which writes MenuControlKind by POSITIONAL index into this list for the built-in range.
        private static IEnumerable<MenuItemKindDescriptor> Builtins()
        {
            yield return new MenuItemKindDescriptor(
                "label", MenuControlKind.Label, "none", null, UISpecGenerator.BuildLabelRow);
            yield return new MenuItemKindDescriptor(
                "button", MenuControlKind.Button, "none", null, UISpecGenerator.BuildButtonRow, s => s.buttonIds);
            yield return new MenuItemKindDescriptor(
                "toggle", MenuControlKind.Toggle, "bool", ToBool, UISpecGenerator.BuildToggleRow, s => s.toggleIds);
            yield return new MenuItemKindDescriptor(
                "switch", MenuControlKind.Switch, "bool", ToBool, UISpecGenerator.BuildSwitchRow, s => s.toggleIds);
            yield return new MenuItemKindDescriptor(
                "slider", MenuControlKind.Slider, "float", ToFloat, UISpecGenerator.BuildSliderRow, s => s.sliderIds);
            yield return new MenuItemKindDescriptor(
                "stepper", MenuControlKind.Stepper, "float", ToFloat, UISpecGenerator.BuildStepperRow);
            yield return new MenuItemKindDescriptor(
                "dropdown", MenuControlKind.Dropdown, "int", ToInt, UISpecGenerator.BuildDropdownRow, s => s.dropdownIds);
            yield return new MenuItemKindDescriptor(
                "rebind", MenuControlKind.KeyRebind, "none", null, UISpecGenerator.BuildKeyRebindRow);
        }

        private static object ToBool(string value) =>
            string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || value == "1";

        private static object ToFloat(string value) =>
            double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) ? (object)d : null;

        private static object ToInt(string value) =>
            int.TryParse(value, out int i) ? (object)(double)i : null;

        /// <summary> Every registered menu-item kind, built-ins first. Backs <see cref="MenuItemSpec.Kinds"/>. </summary>
        public static IReadOnlyList<MenuItemKindDescriptor> All => _registry.All;

        /// <summary> Resolves a kind by its spec string. False (default) on miss. </summary>
        public static bool TryGet(string id, out MenuItemKindDescriptor descriptor) => _registry.TryGet(id, out descriptor);

        /// <summary> Reverse lookup: the descriptor whose <see cref="MenuItemKindDescriptor.controlKind"/>
        /// matches, for the generator's row-build dispatch. False (default) when nothing maps to it
        /// (should not happen for anything produced by <see cref="MapKind"/>). </summary>
        public static bool TryGetByControlKind(MenuControlKind controlKind, out MenuItemKindDescriptor descriptor)
        {
            foreach (MenuItemKindDescriptor d in All)
            {
                if (d.controlKind == controlKind) { descriptor = d; return true; }
            }
            descriptor = default;
            return false;
        }

        /// <summary>
        /// Registers (or replaces, by id) a menu-item kind. The extension seam: a consuming project
        /// calls this once (e.g. from an <c>[InitializeOnLoad]</c> static ctor) to add a settings/cheats
        /// control kind without forking the package. See the runtime-boundary note on
        /// <see cref="MenuItemKindDescriptor"/> for what "without forking" does and doesn't cover today.
        /// </summary>
        public static void Register(MenuItemKindDescriptor descriptor) => _registry.Register(descriptor);

        /// <summary> Test-only: clears project registrations and re-seeds the built-ins on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();

        /// <summary>
        /// Spec kind string → runtime <see cref="MenuControlKind"/> (replaces the old hand-written
        /// <c>MapKind</c> switch). A kind with no <see cref="MenuItemKindDescriptor.controlKind"/> (an
        /// unknown kind, or a project kind registered with <c>controlKind: null</c>) falls back to
        /// <see cref="MenuControlKind.Label"/> — logged, never silent — per the type's runtime-boundary
        /// note: extending the closed enum so a custom kind gets its own slot is a Runtime/ change this
        /// task does not make.
        /// </summary>
        public static MenuControlKind MapKind(string kind)
        {
            if (TryGet(kind, out MenuItemKindDescriptor descriptor) && descriptor.controlKind.HasValue)
                return descriptor.controlKind.Value;
            Debug.LogWarning($"[Neo.UI] NeoMenuItemKinds: menu item kind '{kind}' has no runtime " +
                "MenuControlKind mapping (MenuControlKind stays a closed enum for built-ins) — the " +
                "generated row falls back to a non-interactive Label.");
            return MenuControlKind.Label;
        }

        /// <summary> Runtime <see cref="MenuControlKind"/> → spec kind string (replaces the old
        /// hand-written <c>UnmapKind</c> switch). Unmapped (should not occur for a value produced by
        /// <see cref="MapKind"/>) exports as "label". </summary>
        public static string UnmapKind(MenuControlKind controlKind) =>
            TryGetByControlKind(controlKind, out MenuItemKindDescriptor descriptor) ? descriptor.id : "label";

        /// <summary> The C# value type a kind reads/writes (replaces the old <c>BindingManifest.TypeForKind</c>
        /// switch — the sanctioned Phase-2 TODO this task resolves). Unknown kind → "none". </summary>
        public static string TypeForKind(string kind) =>
            TryGet(kind, out MenuItemKindDescriptor descriptor) ? descriptor.valueType : "none";

        /// <summary> The canonical JSON value for a stored default-value string, per kind (replaces the
        /// old <c>MenuItemSpec.ValueToTyped</c> switch) — so export round-trips. </summary>
        public static object ValueToTyped(string kind, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return TryGet(kind, out MenuItemKindDescriptor descriptor) && descriptor.valueToTyped != null
                ? descriptor.valueToTyped(value)
                : null;
        }
    }
}
