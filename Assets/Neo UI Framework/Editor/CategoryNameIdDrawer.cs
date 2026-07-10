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
            // A component's OWN identity gets a third tail button: rename the GameObject to the
            // id's canonical name (NeoIdNaming — the same convention the generator uses), so
            // hand-built and generated hierarchies read alike. The widget itself declares its
            // identity via INeoIdOwner; reference fields (triggers, signal streams,
            // containerReference, …) and any non-identity id never match.
            bool isOwnId = property.serializedObject.targetObject is Component component
                           && component is INeoIdOwner owner
                           && ReferenceEquals(fieldInfo.GetValue(component), owner.OwnId);
            if (isOwnId)
                IdDatabaseOptions.DrawCategoryNamePair(content, categoryProperty, nameProperty,
                    GetDatabase(), inlineTools: true, RenameButtonContent,
                    () => RenameTargetsToId(property));
            else
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

        private void RenameTargetsToId(SerializedProperty property)
        {
            // Multi-edit renames every selected target from its OWN id value, not the (possibly
            // mixed) serialized view — the field instance is read straight off each component.
            foreach (UnityEngine.Object target in property.serializedObject.targetObjects)
                if (target is Component component)
                    NeoIdNaming.RenameToId(component, fieldInfo.GetValue(component) as CategoryNameId);
        }

        private static GUIContent _renameButtonContent;
        private static GUIContent RenameButtonContent => _renameButtonContent ??= new GUIContent(
            EditorGUIUtility.IconContent("editicon.sml").image,
            "Rename the GameObject to match this id (\"Button - Category_Name\" — the generated-content naming)");
    }
}
