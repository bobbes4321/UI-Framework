using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Menus
{
    /// <summary>
    /// A flat, force-text ScriptableObject describing a menu's controls and their grouping. The
    /// declarative source of truth a presenter builds from and the spec pipeline round-trips.
    /// Base class for <see cref="SettingsCatalog"/> and <see cref="CheatCatalog"/>.
    /// </summary>
    public abstract class MenuCatalog : ScriptableObject
    {
        [Tooltip("Addressing category for this catalog (e.g. \"Settings\").")]
        public string category = "Menu";
        [Tooltip("Addressing name for this catalog (e.g. \"Audio\").")]
        public string menuName = "Main";

        [Tooltip("Group / tab names, in display order. Items reference these by their 'group' field.")]
        public List<string> groups = new List<string>();
        [Tooltip("Group shown first; blank = first group (or all items when there are no groups).")]
        public string startGroup;

        [Tooltip("Editor/spec round-trip only: project path of the InputActionAsset rebind rows target.")]
        public string inputActionAssetPath;

        public List<MenuItemDefinition> items = new List<MenuItemDefinition>();

        public string Id => $"{category}/{menuName}";

        /// <summary> The signal stream category change events fire on for this catalog's controls. </summary>
        public abstract string ChangeSignalCategory { get; }

        /// <summary> Items belonging to <paramref name="group"/> (or all items when group is blank). </summary>
        public IEnumerable<MenuItemDefinition> ItemsInGroup(string group)
        {
            foreach (MenuItemDefinition item in items)
            {
                if (item == null) continue;
                if (string.IsNullOrEmpty(group) || string.Equals(item.group, group, System.StringComparison.Ordinal))
                    yield return item;
            }
        }

        public MenuItemDefinition Find(string category, string name)
        {
            foreach (MenuItemDefinition item in items)
                if (item != null && item.Matches(category, name)) return item;
            return null;
        }
    }

    internal static class MenuItemDefinitionExtensions
    {
        public static bool Matches(this MenuItemDefinition item, string category, string name) =>
            string.Equals(item.Category, category, System.StringComparison.Ordinal) &&
            string.Equals(item.Name, name, System.StringComparison.Ordinal);
    }
}
