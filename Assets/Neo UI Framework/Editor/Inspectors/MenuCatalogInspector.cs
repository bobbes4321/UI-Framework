using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Neo.EditorUI;
using Neo.UI.Menus;
using UnityEditor;
using UnityEditorInternal;
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
    /// no bespoke window required.
    /// </summary>
    [CustomEditor(typeof(MenuCatalog), true)]
    public class MenuCatalogInspector : NeoUIEditor
    {
        protected override string HeaderTitle => target is CheatCatalog ? "Cheat Catalog" : "Settings Catalog";
        protected override string HeaderSubtitle => ((MenuCatalog)target).Id;
        protected override Color Accent => NeoColors.Data;

        // The "items" ReorderableList is cached by NeoListView (keyed on the SerializedObject +
        // propertyPath), but its draw/height callbacks default to generic recursive PropertyField
        // drawing. We swap in the kind-aware callbacks once per list instance — never rebuild the list,
        // never reassign the callbacks every OnGUI pass (IMGUI rule: no per-frame allocation churn).
        private static readonly ConditionalWeakTable<ReorderableList, object> CustomizedItemLists =
            new ConditionalWeakTable<ReorderableList, object>();

        private static float LineHeight => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

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
                DrawItems();
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

        private void DrawItems()
        {
            SerializedProperty itemsProperty = serializedObject.FindProperty("items");
            ReorderableList list = NeoListView.Get(itemsProperty, "Items");
            if (!CustomizedItemLists.TryGetValue(list, out _))
            {
                CustomizedItemLists.Add(list, null);
                list.elementHeightCallback = index => ElementHeight(itemsProperty, index);
                list.drawElementCallback = (rect, index, active, focused) => DrawElement(itemsProperty, rect, index);
            }
            list.DoLayoutList();
        }

        private float ElementHeight(SerializedProperty itemsProperty, int index)
        {
            if (index >= itemsProperty.arraySize) return EditorGUIUtility.singleLineHeight;
            SerializedProperty item = itemsProperty.GetArrayElementAtIndex(index);
            string kind = CurrentKind(item);
            return LineHeight * 3f + ExtraHeight(item, kind) + 8f;
        }

        private float ExtraHeight(SerializedProperty item, string kind)
        {
            switch (kind)
            {
                case "toggle":
                case "switch":
                    return 0f; // Default On toggle already lives in the base "persisted" row
                case "slider":
                case "stepper":
                    return LineHeight * 2f;
                case "dropdown":
                    SerializedProperty options = item.FindPropertyRelative("options");
                    return options != null
                        ? EditorGUI.GetPropertyHeight(options, includeChildren: true)
                        : EditorGUIUtility.singleLineHeight;
                case "rebind":
                    return LineHeight;
                default:
                    return 0f;
            }
        }

        private void DrawElement(SerializedProperty itemsProperty, Rect rect, int index)
        {
            if (index >= itemsProperty.arraySize) return;
            SerializedProperty item = itemsProperty.GetArrayElementAtIndex(index);
            string kind = CurrentKind(item);

            rect.y += 2f;
            Rect line = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);

            // Row 1: kind | category | name
            NeoGUI.SplitHorizontal(line, out Rect kindRect, out Rect idRect, 0.28f);
            DrawKindPopup(kindRect, item);
            NeoGUI.SplitHorizontal(idRect, out Rect categoryRect, out Rect nameRect);
            EditorGUI.PropertyField(categoryRect, item.FindPropertyRelative("category"), GUIContent.none);
            EditorGUI.PropertyField(nameRect, item.FindPropertyRelative("name"), GUIContent.none);
            NeoGUI.NextLine(ref line);

            // Row 2: label | group
            NeoGUI.SplitHorizontal(line, out Rect labelRect, out Rect groupRect);
            EditorGUI.PropertyField(labelRect, item.FindPropertyRelative("label"), new GUIContent("Label"));
            SerializedProperty groupProperty = item.FindPropertyRelative("group");
            Rect groupField = EditorGUI.PrefixLabel(groupRect, new GUIContent("Group"));
            NeoDropdown.StringPopup(groupField, groupProperty, GroupOptions, "(none)", onAddNew: AddGroup);
            NeoGUI.NextLine(ref line);

            // Row 3: persisted | default-value (kind-dependent control)
            NeoGUI.SplitHorizontal(line, out Rect persistedRect, out Rect defaultRect);
            EditorGUI.PropertyField(persistedRect, item.FindPropertyRelative("persisted"), new GUIContent("Persisted"));
            DrawDefaultValue(defaultRect, item, kind);
            NeoGUI.NextLine(ref line);

            // Row 4+: kind-specific fields
            switch (kind)
            {
                case "slider":
                    NeoGUI.SplitHorizontal(line, out Rect minRect, out Rect maxRect);
                    EditorGUI.PropertyField(minRect, item.FindPropertyRelative("min"), new GUIContent("Min"));
                    EditorGUI.PropertyField(maxRect, item.FindPropertyRelative("max"), new GUIContent("Max"));
                    NeoGUI.NextLine(ref line);
                    EditorGUI.PropertyField(line, item.FindPropertyRelative("wholeNumbers"), new GUIContent("Whole Numbers"));
                    break;
                case "stepper":
                    NeoGUI.SplitHorizontal(line, out Rect sMinRect, out Rect sMaxRect);
                    EditorGUI.PropertyField(sMinRect, item.FindPropertyRelative("min"), new GUIContent("Min"));
                    EditorGUI.PropertyField(sMaxRect, item.FindPropertyRelative("max"), new GUIContent("Max"));
                    NeoGUI.NextLine(ref line);
                    NeoGUI.SplitHorizontal(line, out Rect stepRect, out Rect wholeRect);
                    EditorGUI.PropertyField(stepRect, item.FindPropertyRelative("step"), new GUIContent("Step"));
                    EditorGUI.PropertyField(wholeRect, item.FindPropertyRelative("wholeNumbers"), new GUIContent("Whole Numbers"));
                    break;
                case "dropdown":
                    SerializedProperty options = item.FindPropertyRelative("options");
                    float optionsHeight = options != null
                        ? EditorGUI.GetPropertyHeight(options, includeChildren: true)
                        : EditorGUIUtility.singleLineHeight;
                    Rect optionsRect = new Rect(line.x, line.y, line.width, optionsHeight);
                    EditorGUI.PropertyField(optionsRect, options, new GUIContent("Options"), includeChildren: true);
                    break;
                case "rebind":
                    NeoGUI.SplitHorizontal(line, out Rect actionRect, out Rect bindingRect);
                    EditorGUI.PropertyField(actionRect, item.FindPropertyRelative("inputAction"), new GUIContent("Input Action"));
                    EditorGUI.PropertyField(bindingRect, item.FindPropertyRelative("bindingIndex"), new GUIContent("Binding Index"));
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
                    EditorGUI.PropertyField(rect, defaultValue, new GUIContent("Default"));
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
