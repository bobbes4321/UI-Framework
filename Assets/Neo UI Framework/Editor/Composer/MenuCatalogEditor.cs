using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Authoring UI for settings/cheats catalogs and their items — the biggest "JSON-only island"
    /// the Composer closes for designers. A catalog is id + groups + a flat list of typed controls;
    /// each item maps 1:1 to a runtime MenuItemDefinition. All edits route through
    /// <see cref="SpecDocument.ApplyEdit"/>.
    /// </summary>
    public static class MenuCatalogEditor
    {
        public static void DrawCatalog(SpecDocument document, MenuCatalogSpec catalog)
        {
            bool known = ComposerCatalogKinds.TryGet(catalog.kind, out CatalogKind kind);
            string title = known ? $"{kind.label} Catalog" : $"{catalog.kind} Catalog";
            NeoGUI.ComponentHeader(title, catalog.id, NeoColors.Data);

            DelayedText(document, "Category", catalog.category, v => catalog.category = NonEmpty(v, catalog.category));
            DelayedText(document, "Name", catalog.menuName, v => catalog.menuName = NonEmpty(v, catalog.menuName));
            DelayedText(document, "Start Group", catalog.start, v => catalog.start = Empty(v));
            if (known && kind.showFavourites)
                Bool(document, "Favourites", catalog.favourites, v => catalog.favourites = v);
            DelayedText(document, "Input Action Asset", catalog.inputActionAsset, v => catalog.inputActionAsset = Empty(v));

            EditorGUILayout.Space(4f);
            DrawGroups(document, catalog);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"Items ({catalog.items.Count})", EditorStyles.boldLabel);
            int removeAt = -1;
            for (int i = 0; i < catalog.items.Count; i++)
            {
                MenuItemSpec item = catalog.items[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{item.kind}", GUILayout.Width(64f));
                    EditorGUILayout.LabelField(item.id);
                    if (GUILayout.Button("−", GUILayout.Width(24f))) removeAt = i;
                }
            }
            if (removeAt >= 0)
            {
                int captured = removeAt;
                document.ApplyEdit(() => catalog.items.RemoveAt(captured), "Delete Menu Item");
                return;
            }

            Rect rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("Add Item"));
            NeoDropdown.ValuePopup(rect, "", () => new List<string>(MenuItemSpec.Kinds),
                kind => document.ApplyEdit(() => catalog.items.Add(ComposerFactory.NewMenuItem(kind)), "Add Menu Item"),
                "+ control");
        }

        private static void DrawGroups(SpecDocument document, MenuCatalogSpec catalog)
        {
            EditorGUILayout.LabelField("Groups", EditorStyles.miniBoldLabel);
            int removeAt = -1;
            for (int i = 0; i < catalog.groups.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string v = EditorGUILayout.DelayedTextField(catalog.groups[i]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        int captured = i;
                        document.ApplyEdit(() => catalog.groups[captured] = v, "Edit Group");
                        return;
                    }
                    if (GUILayout.Button("−", GUILayout.Width(24f))) removeAt = i;
                }
            }
            if (removeAt >= 0)
            {
                int captured = removeAt;
                document.ApplyEdit(() => catalog.groups.RemoveAt(captured), "Delete Group");
                return;
            }
            if (GUILayout.Button("+ Add Group", GUILayout.Width(110f)))
                document.ApplyEdit(() => catalog.groups.Add("Group"), "Add Group");
        }

        public static void DrawItem(SpecDocument document, MenuCatalogSpec catalog, MenuItemSpec item)
        {
            NeoGUI.ComponentHeader("Menu Item", $"{item.kind}: {item.id}", NeoColors.Data);

            DelayedText(document, "Category", item.category, v => item.category = NonEmpty(v, item.category));
            DelayedText(document, "Name", item.name, v => item.name = NonEmpty(v, item.name));
            DelayedText(document, "Label", item.label, v => item.label = Empty(v));
            DelayedText(document, "Tooltip", item.tooltip, v => item.tooltip = Empty(v));

            // group picker from the catalog's groups
            Rect groupRect = EditorGUILayout.GetControlRect();
            groupRect = EditorGUI.PrefixLabel(groupRect, new GUIContent("Group"));
            NeoDropdown.ValuePopup(groupRect, item.group, () => new List<string>(catalog.groups),
                v => document.ApplyEdit(() => item.group = Empty(v), "Edit Group"), "None",
                g => document.ApplyEdit(() => { if (!catalog.groups.Contains(g)) catalog.groups.Add(g); item.group = g; }, "Add Group"));

            Bool(document, "Persisted", item.persisted, v => item.persisted = v);

            switch (item.kind)
            {
                case "toggle":
                case "switch":
                    Bool(document, "Default On", string.Equals(item.value, "True", StringComparison.OrdinalIgnoreCase),
                        v => item.value = v ? "True" : "False");
                    break;
                case "slider":
                    NullableFloat(document, "Min", item.min, v => item.min = v);
                    NullableFloat(document, "Max", item.max, v => item.max = v);
                    DelayedText(document, "Default", item.value, v => item.value = Empty(v));
                    Bool(document, "Whole Numbers", item.wholeNumbers, v => item.wholeNumbers = v);
                    break;
                case "stepper":
                    NullableFloat(document, "Min", item.min, v => item.min = v);
                    NullableFloat(document, "Max", item.max, v => item.max = v);
                    NullableFloat(document, "Step", item.step, v => item.step = v);
                    DelayedText(document, "Default", item.value, v => item.value = Empty(v));
                    Bool(document, "Whole Numbers", item.wholeNumbers, v => item.wholeNumbers = v);
                    break;
                case "dropdown":
                    DelayedText(document, "Default Index", item.value, v => item.value = Empty(v));
                    DrawOptions(document, item);
                    break;
                case "rebind":
                    DelayedText(document, "Input Action", item.inputAction, v => item.inputAction = Empty(v));
                    NullableInt(document, "Binding Index", item.bindingIndex, v => item.bindingIndex = v);
                    break;
            }
        }

        private static void DrawOptions(SpecDocument document, MenuItemSpec item)
        {
            EditorGUILayout.LabelField("Options", EditorStyles.miniBoldLabel);
            var options = item.options ?? new List<string>();
            int removeAt = -1;
            for (int i = 0; i < options.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string v = EditorGUILayout.DelayedTextField(options[i]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        int captured = i;
                        document.ApplyEdit(() => { item.options = options; item.options[captured] = v; }, "Edit Option");
                        return;
                    }
                    if (GUILayout.Button("−", GUILayout.Width(24f))) removeAt = i;
                }
            }
            if (removeAt >= 0)
            {
                int captured = removeAt;
                document.ApplyEdit(() => { options.RemoveAt(captured); item.options = options.Count > 0 ? options : null; }, "Delete Option");
                return;
            }
            if (GUILayout.Button("+ Add Option", GUILayout.Width(110f)))
                document.ApplyEdit(() => { options.Add("Option"); item.options = options; }, "Add Option");
        }

        // ------------------------------------------------------------------ small field helpers

        private static void DelayedText(SpecDocument document, string label, string current, Action<string> set)
        {
            EditorGUI.BeginChangeCheck();
            string v = EditorGUILayout.DelayedTextField(label, current ?? "");
            if (EditorGUI.EndChangeCheck()) document.ApplyEdit(() => set(v), "Edit " + label);
        }

        private static void Bool(SpecDocument document, string label, bool current, Action<bool> set)
        {
            EditorGUI.BeginChangeCheck();
            bool v = EditorGUILayout.Toggle(label, current);
            if (EditorGUI.EndChangeCheck()) document.ApplyEdit(() => set(v), "Edit " + label);
        }

        private static void NullableFloat(SpecDocument document, string label, float? current, Action<float?> set)
        {
            Rect content = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(label));
            var toggleRect = new Rect(content.x, content.y, 14f, content.height);
            var fieldRect = new Rect(content.x + 18f, content.y, content.width - 18f, content.height);
            EditorGUI.BeginChangeCheck();
            bool has = EditorGUI.Toggle(toggleRect, current.HasValue);
            using (new EditorGUI.DisabledScope(!has))
            {
                float value = EditorGUI.DelayedFloatField(fieldRect, current ?? 0f);
                if (EditorGUI.EndChangeCheck()) document.ApplyEdit(() => set(has ? (float?)value : null), "Edit " + label);
            }
        }

        private static void NullableInt(SpecDocument document, string label, int current, Action<int> set)
        {
            EditorGUI.BeginChangeCheck();
            int v = EditorGUILayout.DelayedIntField(label, current);
            if (EditorGUI.EndChangeCheck()) document.ApplyEdit(() => set(v), "Edit " + label);
        }

        private static string Empty(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        private static string NonEmpty(string v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
    }
}
