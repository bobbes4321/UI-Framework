using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Neo.EditorUI
{
    /// <summary>
    /// Cached ReorderableList wrapper: lists are built once per (SerializedObject, propertyPath)
    /// and reused every frame — building a ReorderableList per OnGUI pass is the classic IMGUI
    /// inspector perf mistake. Elements draw through their PropertyDrawers, so dropdown-enhanced
    /// types (ids, triggers, view refs) automatically render with their custom UI inside lists.
    /// </summary>
    public static class NeoListView
    {
        private static readonly ConditionalWeakTable<SerializedObject, Dictionary<string, ReorderableList>> Cache =
            new ConditionalWeakTable<SerializedObject, Dictionary<string, ReorderableList>>();

        /// <summary> Draws a reorderable list for an array/list property with a titled header. </summary>
        public static void Draw(SerializedProperty listProperty, string title = null)
        {
            Get(listProperty, title ?? listProperty.displayName).DoLayoutList();
        }

        public static ReorderableList Get(SerializedProperty listProperty, string title)
        {
            SerializedObject serializedObject = listProperty.serializedObject;
            Dictionary<string, ReorderableList> byPath = Cache.GetOrCreateValue(serializedObject);
            string path = listProperty.propertyPath;

            if (byPath.TryGetValue(path, out ReorderableList cached)) return cached;

            var list = new ReorderableList(serializedObject, listProperty.Copy(),
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

            list.drawHeaderCallback = rect =>
            {
                GUI.Label(rect, title, EditorStyles.boldLabel);
                var countRect = new Rect(rect.xMax - 40f, rect.y, 40f, rect.height);
                GUI.Label(countRect, list.serializedProperty.arraySize.ToString(), NeoStyles.MiniDim);
            };

            list.elementHeightCallback = index =>
            {
                if (index >= list.serializedProperty.arraySize) return EditorGUIUtility.singleLineHeight;
                SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(element, includeChildren: true) + 4f;
            };

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                if (index >= list.serializedProperty.arraySize) return;
                SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
                rect.y += 2f;
                rect.height -= 4f;
                EditorGUI.PropertyField(rect, element, includeChildren: true);
            };

            byPath[path] = list;
            return list;
        }
    }
}
