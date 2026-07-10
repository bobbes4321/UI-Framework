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
        /// The id database a trigger's category/name dropdown should offer. Built-ins map by
        /// <see cref="FlowTrigger.TriggerType"/> enum value (a closed set, unlike element kinds — a
        /// built-in trigger has no string key a project could re-register under, so there's no cheap
        /// registry-first flip here); a <see cref="FlowTrigger.TriggerType.Custom"/> trigger — the one
        /// case that IS string-addressed (<c>customKind</c>) — consults its registered kind's
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
        /// generator registers that kind into. Resolution order: the <see cref="NeoElementKinds"/>
        /// registry is consulted FIRST — if <paramref name="kind"/> is registered and implements
        /// <see cref="IElementKindIdDatabase"/> with a non-null <see cref="IElementKindIdDatabase.PreferredIdType"/>,
        /// that database wins. This lets a project re-route a BUILT-IN kind too (e.g. re-register
        /// "button" with a different <c>PreferredIdType</c>) by re-registering its <c>Kind</c> string —
        /// <see cref="NeoKeyedRegistry{T}.Register"/> replaces same-key entries in place, and built-in
        /// kinds aren't pre-seeded here (Phase 1), so this is a pure addition with no behavior change for
        /// anyone who hasn't registered one of the built-in kind strings. Only when the registry has no
        /// (usable) entry does the hardcoded default mapping apply (button/stepper → buttonIds,
        /// toggle/switch/tab → toggleIds, slider → sliderIds, dropdown → dropdownIds) — mirrors how
        /// <see cref="ForTrigger"/> consults <see cref="ITriggerKindIdDatabase"/> for
        /// <see cref="FlowTrigger.TriggerType.Custom"/>. Anything unresolved returns null — the picker
        /// still lets you type and add a Category/Name, it just doesn't persist a reusable entry.
        /// </summary>
        public static IdDatabase ForElementKind(string kind)
        {
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null) return null;

            if (NeoElementKinds.TryGet(kind, out INeoElementKind ek)
                && ek is IElementKindIdDatabase withDb
                && withDb.PreferredIdType != null)
                return settings.GetDatabaseFor(withDb.PreferredIdType);

            switch (kind)
            {
                case "button":
                case "stepper":  return settings.buttonIds;
                case "toggle":
                case "switch":
                case "tab":      return settings.toggleIds;
                case "slider":   return settings.sliderIds;
                case "dropdown": return settings.dropdownIds;
                default:         return null;
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
        /// Splits a quick-add entry into its category/name halves: "Category/Name" (split on the FIRST
        /// slash) names both; a plain "Name" lands in <paramref name="currentCategory"/> (or the default
        /// category when none is set). A leading slash falls back to the default category; a trailing
        /// slash (no name) is rejected. Whitespace around either half is trimmed.
        /// </summary>
        public static bool ParseQuickAdd(string input, string currentCategory,
            out string category, out string name)
        {
            category = name = null;
            string trimmed = (input ?? "").Trim();
            if (trimmed.Length == 0) return false;

            int slash = trimmed.IndexOf('/');
            if (slash >= 0)
            {
                category = trimmed.Substring(0, slash).Trim();
                name = trimmed.Substring(slash + 1).Trim();
            }
            else
            {
                category = (currentCategory ?? "").Trim();
                name = trimmed;
            }
            if (string.IsNullOrEmpty(category)) category = CategoryNameId.DefaultCategory;
            return !string.IsNullOrEmpty(name);
        }

        /// <summary>
        /// Draws the standard category/name dropdown pair into <paramref name="rect"/>, with inline
        /// add-new rows writing to <paramref name="database"/> (when assigned). Unless the rect is too
        /// narrow, the pair carries two tail buttons: <c>+</c> opens a one-field quick-add popup
        /// (type <c>Name</c> or <c>Category/Name</c> — persists to the database AND assigns both
        /// halves in one step) and <c>…</c> jumps into the ID Database Manager pre-selected to this
        /// database/category/name.
        /// </summary>
        public static void DrawCategoryNamePair(Rect rect, SerializedProperty categoryProperty,
            SerializedProperty nameProperty, IdDatabase database) =>
            DrawCategoryNamePair(rect, categoryProperty, nameProperty, database, inlineTools: true);

        /// <summary> <see cref="DrawCategoryNamePair(Rect,SerializedProperty,SerializedProperty,IdDatabase)"/>
        /// with the quick-add / manager tail buttons optional (pass false where every pixel counts). </summary>
        public static void DrawCategoryNamePair(Rect rect, SerializedProperty categoryProperty,
            SerializedProperty nameProperty, IdDatabase database, bool inlineTools) =>
            DrawCategoryNamePair(rect, categoryProperty, nameProperty, database, inlineTools,
                extraTool: null, onExtraTool: null);

        /// <summary> <see cref="DrawCategoryNamePair(Rect,SerializedProperty,SerializedProperty,IdDatabase,bool)"/>
        /// plus an optional third tail button (the seam a caller uses to attach a context action to the
        /// pair — e.g. the id drawer's rename-GameObject-to-id button); drawn after <c>+</c>/<c>…</c>,
        /// hidden with them on very narrow rects. </summary>
        public static void DrawCategoryNamePair(Rect rect, SerializedProperty categoryProperty,
            SerializedProperty nameProperty, IdDatabase database, bool inlineTools,
            GUIContent extraTool, Action onExtraTool)
        {
            const float ButtonWidth = 20f;
            const float Gap = 2f;
            const float MinWidthForTools = 160f; // below this the dropdowns need every pixel

            bool tools = inlineTools && rect.width >= MinWidthForTools;
            bool extra = tools && extraTool != null && onExtraTool != null;
            Rect pairRect = rect;
            if (tools) pairRect.width -= (ButtonWidth + Gap) * (extra ? 3f : 2f);

            NeoGUI.SplitHorizontal(pairRect, out Rect categoryRect, out Rect nameRect);

            NeoDropdown.StringPopup(categoryRect, categoryProperty,
                () => Categories(database),
                CategoryNameId.DefaultCategory,
                database == null ? (Action<string>)null : newCategory => AddCategory(database, newCategory));

            string category = categoryProperty.stringValue;
            NeoDropdown.StringPopup(nameRect, nameProperty,
                () => Names(database, category),
                CategoryNameId.DefaultName,
                database == null ? (Action<string>)null : newName => AddName(database, category, newName));

            if (!tools) return;
            var addRect = new Rect(pairRect.xMax + Gap, rect.y, ButtonWidth, rect.height);
            var manageRect = new Rect(addRect.xMax + Gap, rect.y, ButtonWidth, rect.height);

            if (GUI.Button(addRect, AddButtonContent, EditorStyles.miniButton))
                ShowQuickAdd(addRect, categoryProperty, nameProperty, database);

            if (GUI.Button(manageRect, ManageButtonContent, EditorStyles.miniButton))
                IdDatabaseManagerWindow.Open(database, category, nameProperty.stringValue);

            if (!extra) return;
            var extraRect = new Rect(manageRect.xMax + Gap, rect.y, ButtonWidth, rect.height);
            if (GUI.Button(extraRect, extraTool, EditorStyles.miniButton))
                onExtraTool();
        }

        private static readonly GUIContent AddButtonContent =
            new GUIContent("+", "Add a new id in one step — type Name or Category/Name");
        private static readonly GUIContent ManageButtonContent =
            new GUIContent("…", "Open in the ID Database Manager");

        /// <summary>
        /// The one-step add: a single-field popup accepting <c>Name</c> (into the current category) or
        /// <c>Category/Name</c>; on commit the id is added to <paramref name="database"/> (when
        /// assigned) and both property halves are set. The popup outlives this IMGUI pass, so the
        /// properties are re-resolved from (serializedObject, path) at commit time — same pattern as
        /// <see cref="NeoDropdown.StringPopup"/>.
        /// </summary>
        private static void ShowQuickAdd(Rect activatorRect, SerializedProperty categoryProperty,
            SerializedProperty nameProperty, IdDatabase database)
        {
            SerializedObject serializedObject = categoryProperty.serializedObject;
            string categoryPath = categoryProperty.propertyPath;
            string namePath = nameProperty.propertyPath;
            string currentCategory = categoryProperty.stringValue;

            NeoInputPopup.Show(activatorRect, "Add id", "Name or Category/Name", value =>
            {
                if (!ParseQuickAdd(value, currentCategory, out string category, out string name)) return;
                AddName(database, category, name); // Undo + SetDirty; null database just skips persistence
                try
                {
                    if (serializedObject.targetObject == null) return;
                    serializedObject.Update();
                    SerializedProperty resolvedCategory = serializedObject.FindProperty(categoryPath);
                    SerializedProperty resolvedName = serializedObject.FindProperty(namePath);
                    if (resolvedCategory == null || resolvedName == null) return;
                    resolvedCategory.stringValue = category;
                    resolvedName.stringValue = name;
                    serializedObject.ApplyModifiedProperties();
                }
                catch (Exception)
                {
                    // disposed SerializedObject — selection changed while the popup was open
                }
            });
        }
    }
}
