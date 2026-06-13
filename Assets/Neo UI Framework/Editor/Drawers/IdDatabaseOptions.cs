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

        public static IdDatabase ForTrigger(FlowTrigger.TriggerType type)
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
                default: return null;
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
