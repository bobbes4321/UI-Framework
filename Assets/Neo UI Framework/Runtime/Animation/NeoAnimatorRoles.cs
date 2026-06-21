using System.Collections.Generic;

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
    /// </summary>
    public static class NeoAnimatorRoles
    {
        // Stable ids — referenced by the animator components' Reset() and the factory.
        public const string ViewShow = "View/Show";
        public const string ViewHide = "View/Hide";
        public const string ButtonHover = "Button/Hover";
        public const string ButtonPress = "Button/Press";
        public const string ToggleOn = "Toggle/On";
        public const string ToggleOff = "Toggle/Off";
        public const string Loop = "Loop";
        public const string OneShot = "OneShot";

        private static readonly List<NeoAnimatorRole> _roles = new List<NeoAnimatorRole>
        {
            new NeoAnimatorRole(ViewShow, "View — Show", "Plays when a view/container becomes visible.", "Show"),
            new NeoAnimatorRole(ViewHide, "View — Hide", "Plays when a view/container is hidden.", "Hide"),
            new NeoAnimatorRole(ButtonHover, "Button — Hover", "Plays while a button/selectable is hovered (highlighted).", "Hover"),
            new NeoAnimatorRole(ButtonPress, "Button — Press", "Plays when a button/selectable is pressed or clicked.", "Press", "Click"),
            new NeoAnimatorRole(ToggleOn, "Toggle — On", "Plays when a toggle/switch turns on.", "Toggle", "Show"),
            new NeoAnimatorRole(ToggleOff, "Toggle — Off", "Plays when a toggle/switch turns off.", "Toggle", "Hide"),
            new NeoAnimatorRole(Loop, "Loop", "Continuous ambient motion (idle pulse, spinner).", "Loop"),
            new NeoAnimatorRole(OneShot, "One-shot", "A standalone animator played on demand from code.", "Click", "Show", "Loop"),
        };

        /// <summary> Every known role (built-ins + project-registered). </summary>
        public static IReadOnlyList<NeoAnimatorRole> All => _roles;

        /// <summary> Registers a project-defined role (idempotent on id). Call once at editor/init time. </summary>
        public static void Register(NeoAnimatorRole role)
        {
            if (role == null || string.IsNullOrEmpty(role.Id)) return;
            if (TryGet(role.Id, out _)) return;
            _roles.Add(role);
        }

        public static bool TryGet(string id, out NeoAnimatorRole role)
        {
            for (int i = 0; i < _roles.Count; i++)
                if (string.Equals(_roles[i].Id, id, System.StringComparison.Ordinal)) { role = _roles[i]; return true; }
            role = null;
            return false;
        }

        public static string DisplayName(string id) => TryGet(id, out NeoAnimatorRole r) ? r.DisplayName : id;
    }
}
