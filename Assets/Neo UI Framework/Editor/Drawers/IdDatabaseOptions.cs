using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Option providers for the searchable category/name dropdowns, backed by the ID databases in
    /// NeoUISettings. Lists are built when a dropdown opens — never per frame — and "add new" writes
    /// straight into the database asset.
    /// </summary>
    public static class IdDatabaseOptions
    {
        public static IdDatabase For(Type idType)
        {
            NeoUISettings settings = NeoUISettings.instance;
            return settings != null ? settings.GetDatabaseFor(idType) : null;
        }

        public static IdDatabase ForTrigger(FlowTrigger.TriggerType type) => ForTrigger(type, null);

        /// <summary>
        /// The id database a trigger's category/name dropdown should offer. Built-ins map by enum;
        /// a <see cref="FlowTrigger.TriggerType.Custom"/> trigger consults its registered kind's
        /// <see cref="ITriggerKindIdDatabase.PreferredIdType"/> (when it implements that seam).
        /// </summary>
        public static IdDatabase ForTrigger(FlowTrigger.TriggerType type, string customKind)
        {
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null) return null;
            switch (type)
            {
                case FlowTrigger.TriggerType.ButtonClick: return settings.buttonIds;
                case FlowTrigger.TriggerType.ToggleOn:
                case FlowTrigger.TriggerType.ToggleOff: return settings.toggleIds;
                case FlowTrigger.TriggerType.ViewShown:
                case FlowTrigger.TriggerType.ViewHidden: return settings.viewIds;
                case FlowTrigger.TriggerType.Signal: return settings.streamIds;
                case FlowTrigger.TriggerType.Custom:
                    if (NeoTriggerKinds.TryGet(customKind, out INeoTriggerKind kind)
                        && kind is ITriggerKindIdDatabase withDb
                        && withDb.PreferredIdType != null)
                        return settings.GetDatabaseFor(withDb.PreferredIdType);
                    return null;
                default: return null;
            }
        }

        /// <summary>
        /// The ID database a spec element's Category/Name picker should offer, by element kind — an
        /// element's <c>id</c> field dropdown uses this so it autocompletes against the same database the
        /// generator registers that kind into (button/stepper → buttonIds, toggle/switch/tab → toggleIds,
        /// slider → sliderIds, dropdown → dropdownIds). A project-registered kind that implements
        /// <see cref="IElementKindIdDatabase"/> carries its own preference (the extension seam, exactly as
        /// <see cref="ForTrigger"/> consults <see cref="ITriggerKindIdDatabase"/>). Anything else returns
        /// null — the picker still lets you type and add a Category/Name, it just doesn't persist a reusable entry.
        /// </summary>
        public static IdDatabase ForElementKind(string kind)
        {
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null) return null;
            switch (kind)
            {
                case "button":
                case "stepper":  return settings.buttonIds;
                case "toggle":
                case "switch":
                case "tab":      return settings.toggleIds;
                case "slider":   return settings.sliderIds;
                case "dropdown": return settings.dropdownIds;
                default:
                    if (NeoElementKinds.TryGet(kind, out INeoElementKind ek)
                        && ek is IElementKindIdDatabase withDb
                        && withDb.PreferredIdType != null)
                        return settings.GetDatabaseFor(withDb.PreferredIdType);
                    return null;
            }
        }

        public static List<string> Categories(IdDatabase database)
        {
            var options = new List<string> { CategoryNameId.DefaultCategory };
            if (database == null) return options;
            foreach (string category in database.GetCategories())
                if (category != CategoryNameId.DefaultCategory)
                    options.Add(category);
            return options;
        }

        public static List<string> Names(IdDatabase database, string category)
        {
            var options = new List<string> { CategoryNameId.DefaultName };
            if (database == null) return options;
            foreach (string name in database.GetNames(string.IsNullOrEmpty(category) ? CategoryNameId.DefaultCategory : category))
                if (name != CategoryNameId.DefaultName)
                    options.Add(name);
            return options;
        }

        public static void AddCategory(IdDatabase database, string category)
        {
            if (database == null) return;
            Undo.RecordObject(database, "Add Id Category");
            database.Add(category, CategoryNameId.DefaultName);
            EditorUtility.SetDirty(database);
        }

        public static void AddName(IdDatabase database, string category, string name)
        {
            if (database == null) return;
            Undo.RecordObject(database, "Add Id Name");
            database.Add(string.IsNullOrEmpty(category) ? CategoryNameId.DefaultCategory : category, name);
            EditorUtility.SetDirty(database);
        }

        /// <summary>
        /// Draws the standard category/name dropdown pair into <paramref name="rect"/>, with inline
        /// add-new rows writing to <paramref name="database"/> (when assigned).
        /// </summary>
        public static void DrawCategoryNamePair(Rect rect, SerializedProperty categoryProperty,
            SerializedProperty nameProperty, IdDatabase database)
        {
            NeoGUI.SplitHorizontal(rect, out Rect categoryRect, out Rect nameRect);

            NeoDropdown.StringPopup(categoryRect, categoryProperty,
                () => Categories(database),
                CategoryNameId.DefaultCategory,
                database == null ? (Action<string>)null : newCategory => AddCategory(database, newCategory));

            string category = categoryProperty.stringValue;
            NeoDropdown.StringPopup(nameRect, nameProperty,
                () => Names(database, category),
                CategoryNameId.DefaultName,
                database == null ? (Action<string>)null : newName => AddName(database, category, newName));
        }
    }
}
