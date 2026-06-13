using System;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Fast category/name picker for all CategoryNameId types: two searchable dropdowns backed by
    /// the matching ID database, with inline "+ Add 'name'" rows (type in the search field, click
    /// add — no modal dialogs). No codegen, no scanning — selecting a component stays instant.
    /// </summary>
    [CustomPropertyDrawer(typeof(CategoryNameId), useForChildren: true)]
    public class CategoryNameIdDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty categoryProperty = property.FindPropertyRelative("category");
            SerializedProperty nameProperty = property.FindPropertyRelative("name");
            if (categoryProperty == null || nameProperty == null)
            {
                EditorGUI.LabelField(position, label.text, "(invalid id)");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            Rect content = EditorGUI.PrefixLabel(position, label);
            IdDatabaseOptions.DrawCategoryNamePair(content, categoryProperty, nameProperty, GetDatabase());
            EditorGUI.EndProperty();
        }

        private IdDatabase GetDatabase()
        {
            Type idType = fieldInfo.FieldType;
            if (idType.IsGenericType) idType = idType.GetGenericArguments()[0]; // List<T> fields
            else if (idType.IsArray) idType = idType.GetElementType();
            return IdDatabaseOptions.For(idType);
        }
    }

    /// <summary> Minimal modal text-input dialog (kept for tools that need a one-off prompt). </summary>
    public class EditorInputDialog : EditorWindow
    {
        private string _value = "";
        private string _message;
        private bool _confirmed;
        private bool _focused;

        public static string Show(string title, string message)
        {
            var window = CreateInstance<EditorInputDialog>();
            window.titleContent = new GUIContent(title);
            window._message = message;
            window.minSize = new Vector2(300f, 80f);
            window.maxSize = new Vector2(300f, 80f);
            window.ShowModalUtility();
            return window._confirmed ? window._value : null;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(_message);
            GUI.SetNextControlName("InputField");
            _value = EditorGUILayout.TextField(_value);
            if (!_focused)
            {
                EditorGUI.FocusTextInControl("InputField");
                _focused = true;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("OK") ||
                (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
            {
                _confirmed = true;
                Close();
            }
            if (GUILayout.Button("Cancel")) Close();
            EditorGUILayout.EndHorizontal();
        }
    }
}
