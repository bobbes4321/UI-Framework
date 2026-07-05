using System;
using System.Collections.Generic;
using Neo.EditorUI;
using Neo.UI.Menus;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Native inspector for settings/cheats catalog ScriptableObjects (<see cref="SettingsCatalog"/>,
    /// <see cref="CheatCatalog"/>) — the standing replacement for the Composer's doomed
    /// <c>MenuCatalogEditor</c> pane (retired with the rest of the Composer in Wave 3). Covers the same
    /// ground: identity fields, group list add/remove/reorder, item list add/remove/reorder with a
    /// per-item kind picker + kind-specific value fields, and the cheats-only favourites toggle.
    /// Catalogs are plain <see cref="ScriptableObject"/>s, so this is a standard EditorUI-kit inspector —
    /// items render as <see cref="NeoListView.DrawForm"/> cards whose rows are described ONCE in
    /// <see cref="ItemRows"/> (height and drawing both derive from it via <see cref="NeoForm"/>).
    /// </summary>
    [CustomEditor(typeof(MenuCatalog), true)]
    public class MenuCatalogInspector : NeoUIEditor
    {
        protected override string HeaderTitle => target is CheatCatalog ? "Cheat Catalog" : "Settings Catalog";
        protected override string HeaderSubtitle => ((MenuCatalog)target).Id;
        protected override Color Accent => NeoColors.Data;

        protected override void DrawBody()
        {
            if (NeoGUI.BeginFoldoutSection("NeoUI.MenuCatalog.Identity", "Identity", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("category"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("menuName"));
                DrawStartGroup();
                if (target is CheatCatalog)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("favouritesEnabled"),
                        new GUIContent("Favourites"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("inputActionAssetPath"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.MenuCatalog.Groups", "Groups", defaultOpen: true))
                NeoListView.Draw(serializedObject.FindProperty("groups"), "Groups");
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.MenuCatalog.Items", "Items", defaultOpen: true))
                NeoListView.DrawForm(serializedObject.FindProperty("items"), "Items", ItemRows, NeoColors.Data);
            NeoGUI.EndFoldoutSection();
        }

        // ------------------------------------------------------------------ identity

        private void DrawStartGroup()
        {
            SerializedProperty property = serializedObject.FindProperty("startGroup");
            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent content = EditorGUI.BeginProperty(rect, new GUIContent("Start Group", property.tooltip), property);
            rect = EditorGUI.PrefixLabel(rect, content);
            NeoDropdown.StringPopup(rect, property, GroupOptions, "(first group)", onAddNew: AddGroup);
            EditorGUI.EndProperty();
        }

        private List<string> GroupOptions()
        {
            var list = new List<string>();
            SerializedProperty groups = serializedObject.FindProperty("groups");
            for (int i = 0; i < groups.arraySize; i++)
                list.Add(groups.GetArrayElementAtIndex(i).stringValue);
            return list;
        }

        private void AddGroup(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            serializedObject.Update();
            SerializedProperty groups = serializedObject.FindProperty("groups");
            groups.arraySize++;
            groups.GetArrayElementAtIndex(groups.arraySize - 1).stringValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ items

        /// <summary>
        /// The single description of an item card — <see cref="NeoForm"/> runs it once to measure and
        /// once per repaint to draw, so the element height can never drift from what's drawn.
        /// </summary>
        private void ItemRows(NeoForm form, SerializedProperty item)
        {
            string kind = CurrentKind(item);

            // Identity row: kind | category | name
            form.Line(rect =>
            {
                NeoGUI.SplitHorizontal(rect, out Rect kindRect, out Rect idRect, 0.28f);
                DrawKindPopup(kindRect, item);
                NeoGUI.SplitHorizontal(idRect, out Rect categoryRect, out Rect nameRect);
                EditorGUI.PropertyField(categoryRect, item.FindPropertyRelative("category"), GUIContent.none);
                EditorGUI.PropertyField(nameRect, item.FindPropertyRelative("name"), GUIContent.none);
            });
            form.Gap();

            // Label and group each get a full-width line: room to read/edit the text, long group
            // names aren't clipped.
            form.Field(item.FindPropertyRelative("label"), "Label");
            form.Line(rect =>
            {
                Rect field = EditorGUI.PrefixLabel(rect, new GUIContent("Group"));
                NeoDropdown.StringPopup(field, item.FindPropertyRelative("group"), GroupOptions, "(none)", onAddNew: AddGroup);
            });
            form.Gap();

            // Persisted | default-value (kind-dependent control)
            form.Line(rect =>
            {
                NeoGUI.SplitHorizontal(rect, out Rect persistedRect, out Rect defaultRect);
                NeoGUI.LabeledField(persistedRect, item.FindPropertyRelative("persisted"), "Persisted", 60f);
                DrawDefaultValue(defaultRect, item, kind);
            });

            // Kind-specific rows
            switch (kind)
            {
                case "slider":
                    form.Gap();
                    form.Pair(item.FindPropertyRelative("min"), "Min", 30f,
                        item.FindPropertyRelative("max"), "Max", 30f);
                    form.Line(rect =>
                        NeoGUI.LabeledField(rect, item.FindPropertyRelative("wholeNumbers"), "Whole Numbers", 90f));
                    break;
                case "stepper":
                    form.Gap();
                    form.Pair(item.FindPropertyRelative("min"), "Min", 30f,
                        item.FindPropertyRelative("max"), "Max", 30f);
                    form.Pair(item.FindPropertyRelative("step"), "Step", 30f,
                        item.FindPropertyRelative("wholeNumbers"), "Whole Numbers", 90f);
                    break;
                case "dropdown":
                    SerializedProperty options = item.FindPropertyRelative("options");
                    if (options != null)
                    {
                        form.Gap();
                        form.Field(options, "Options");
                    }
                    break;
                case "rebind":
                    form.Gap();
                    form.Pair(item.FindPropertyRelative("inputAction"), "Input Action", 80f,
                        item.FindPropertyRelative("bindingIndex"), "Binding Index", 90f);
                    break;
            }
        }

        private void DrawDefaultValue(Rect rect, SerializedProperty item, string kind)
        {
            SerializedProperty defaultValue = item.FindPropertyRelative("defaultValue");
            switch (kind)
            {
                case "toggle":
                case "switch":
                    bool current = string.Equals(defaultValue.stringValue, "True", StringComparison.OrdinalIgnoreCase);
                    EditorGUI.BeginChangeCheck();
                    bool value = EditorGUI.ToggleLeft(rect, "Default On", current);
                    if (EditorGUI.EndChangeCheck()) defaultValue.stringValue = value ? "True" : "False";
                    break;
                case "button":
                case "label":
                    // no persisted value for these kinds
                    break;
                default:
                    NeoGUI.LabeledField(rect, defaultValue, "Default", 45f);
                    break;
            }
        }

        /// <summary> The <see cref="MenuItemSpec.Kinds"/> name for an item's current <see cref="MenuControlKind"/>. </summary>
        private static string CurrentKind(SerializedProperty item)
        {
            SerializedProperty kindProperty = item.FindPropertyRelative("kind");
            int index = kindProperty.enumValueIndex;
            string[] kinds = MenuItemSpec.Kinds;
            return index >= 0 && index < kinds.Length ? kinds[index] : kinds[0];
        }

        /// <summary>
        /// Kind popup sourced from <see cref="MenuItemSpec.Kinds"/> (the same vocabulary the JSON spec
        /// pipeline uses) rather than Unity's default enum popup, so the label set stays in one place —
        /// <see cref="MenuControlKind"/>'s declaration order matches it 1:1. Re-resolves the property by
        /// path before writing, same as <see cref="NeoDropdown.StringPopup"/>: the popup can outlive this
        /// IMGUI pass and the inspector's SerializedObject may be gone by the time a value is picked.
        /// </summary>
        private static void DrawKindPopup(Rect rect, SerializedProperty item)
        {
            SerializedProperty kindProperty = item.FindPropertyRelative("kind");
            string[] kinds = MenuItemSpec.Kinds;
            int index = kindProperty.enumValueIndex;
            string current = index >= 0 && index < kinds.Length ? kinds[index] : kinds[0];

            SerializedObject serializedObject = kindProperty.serializedObject;
            string path = kindProperty.propertyPath;
            NeoDropdown.ValuePopup(rect, current, () => new List<string>(kinds), selected =>
            {
                int newIndex = Array.IndexOf(kinds, selected);
                if (newIndex < 0)
                {
                    Debug.LogWarning($"MenuCatalogInspector: unknown item kind '{selected}' — ignoring selection.");
                    return;
                }
                try
                {
                    if (serializedObject.targetObject == null) return;
                    serializedObject.Update();
                    SerializedProperty resolved = serializedObject.FindProperty(path);
                    if (resolved == null) return;
                    resolved.enumValueIndex = newIndex;
                    serializedObject.ApplyModifiedProperties();
                }
                catch (Exception)
                {
                    // disposed SerializedObject — selection changed while the popup was open.
                }
            });
        }
    }
}
