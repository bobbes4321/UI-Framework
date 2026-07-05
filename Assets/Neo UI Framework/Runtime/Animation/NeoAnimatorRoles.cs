using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// One animator "slot" a project can assign a default animation preset to — e.g. a view's Show,
    /// a button's Hover, a toggle's On. The id is the stable key stored in
    /// <see cref="NeoUISettings.animatorDefaults"/>; <see cref="SuggestedCategories"/> drives which
    /// presets surface first in the inspector's per-state picker (a preset whose
    /// <see cref="UIAnimationPreset.category"/> matches is "for" this role).
    /// </summary>
    public sealed class NeoAnimatorRole
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly string[] SuggestedCategories;

        public NeoAnimatorRole(string id, string displayName, string description, params string[] suggestedCategories)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            SuggestedCategories = suggestedCategories ?? System.Array.Empty<string>();
        }
    }

    /// <summary>
    /// The set of animator roles a project can wire a default preset to. The built-ins cover the
    /// shipped animator components (view show/hide, button hover/press, toggle on/off, loops,
    /// one-shots); a consuming project adds its own role for a custom animator by calling
    /// <see cref="Register"/> once — the New Project Setup wizard, the per-state picker and
    /// <see cref="NeoUISettings.animatorDefaults"/> all read THIS list, so a new role appears in the
    /// UI with no fork (mirrors the drop-an-asset seams like <c>NeoWidgetPresets</c>, but the role set
    /// is code-defined data rather than discovered assets).
    /// <para>
    /// A thin public-static facade over the Runtime-asmdef <see cref="NeoRuntimeKeyedRegistry{T}"/> base
    /// (Wave 4 Task 4.4 — see <c>neo-ui-remediation-plan.md</c>). Unlike most Pattern R registries, a
    /// same-id <see cref="Register"/> call here WARNS before replacing: two custom animator components
    /// racing to own the same role id is more likely a project bug worth surfacing than a deliberate
    /// override, and roles (unlike widget presets) are looked up by animator components at Reset() time,
    /// so a silently-swapped role could change every future widget's default feel with no trace.
    /// </para>
    /// </summary>
    public static class NeoAnimatorRoles
    {
        // Stable ids — referenced by the animator components' Reset() and the factory.
        public const string ViewShow = "View/Show";
        public const string ViewHide = "View/Hide";
        public const string ButtonHover = "Button/Hover";
        public const string ButtonPress = "Button/Press";
        public const string SelectableNormal = "Selectable/Normal";
        public const string SelectableSelected = "Selectable/Selected";
        public const string SelectableDisabled = "Selectable/Disabled";
        public const string ToggleOn = "Toggle/On";
        public const string ToggleOff = "Toggle/Off";
        public const string Loop = "Loop";
        public const string OneShot = "OneShot";

        private static readonly NeoRuntimeKeyedRegistry<NeoAnimatorRole> _registry =
            new NeoRuntimeKeyedRegistry<NeoAnimatorRole>(
                r => r.Id,
                builtins: Builtins,
                registryName: "NeoAnimatorRoles");

        private static IEnumerable<NeoAnimatorRole> Builtins()
        {
            yield return new NeoAnimatorRole(ViewShow, "View — Show", "Plays when a view/container becomes visible.", "Show");
            yield return new NeoAnimatorRole(ViewHide, "View — Hide", "Plays when a view/container is hidden.", "Hide");
            yield return new NeoAnimatorRole(ButtonHover, "Button — Hover", "Plays while a button/selectable is hovered (highlighted).", "Hover");
            yield return new NeoAnimatorRole(ButtonPress, "Button — Press", "Plays when a button/selectable is pressed or clicked.", "Press", "Click");
            yield return new NeoAnimatorRole(SelectableNormal, "Selectable — Normal", "Plays when a selectable returns to its rest state (un-hover, release).", "Hover");
            yield return new NeoAnimatorRole(SelectableSelected, "Selectable — Selected", "Plays when a selectable becomes selected (focus, active tab).", "Toggle", "Hover");
            yield return new NeoAnimatorRole(SelectableDisabled, "Selectable — Disabled", "Plays when a selectable becomes non-interactable.", "Toggle", "Hide");
            yield return new NeoAnimatorRole(ToggleOn, "Toggle — On", "Plays when a toggle/switch turns on.", "Toggle", "Show");
            yield return new NeoAnimatorRole(ToggleOff, "Toggle — Off", "Plays when a toggle/switch turns off.", "Toggle", "Hide");
            yield return new NeoAnimatorRole(Loop, "Loop", "Continuous ambient motion (idle pulse, spinner).", "Loop");
            yield return new NeoAnimatorRole(OneShot, "One-shot", "A standalone animator played on demand from code.", "Click", "Show", "Loop");
        }

        /// <summary> Every known role (built-ins + project-registered). </summary>
        public static IReadOnlyList<NeoAnimatorRole> All => _registry.All;

        /// <summary>
        /// Registers a project-defined role. A same-id registration REPLACES the existing role (unlike
        /// most Pattern R registries, this logs a warning first — see the type doc) rather than silently
        /// winning or losing. Call once at editor/init time.
        /// </summary>
        public static void Register(NeoAnimatorRole role)
        {
            if (role != null && !string.IsNullOrEmpty(role.Id) && _registry.TryGet(role.Id, out _))
            {
                Debug.LogWarning($"[Neo.UI] NeoAnimatorRoles: role '{role.Id}' is already registered — replacing it.");
            }
            _registry.Register(role);
        }

        public static bool TryGet(string id, out NeoAnimatorRole role) => _registry.TryGet(id, out role);

        public static string DisplayName(string id) => TryGet(id, out NeoAnimatorRole r) ? r.DisplayName : id;

        /// <summary> Test-only: clears every registration and forces a fresh built-ins seed on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }
}
