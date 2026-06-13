using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// FlowTrigger picker: trigger type dropdown, then only the fields that type actually uses —
    /// category/name dropdowns backed by the matching ID database (buttons, toggles, views,
    /// streams), or a duration for timers. Back/None need nothing.
    /// </summary>
    [CustomPropertyDrawer(typeof(FlowTrigger))]
    public class FlowTriggerDrawer : PropertyDrawer
    {
        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int typeIndex = property.FindPropertyRelative("type").enumValueIndex;
            var type = (FlowTrigger.TriggerType)Mathf.Max(0, typeIndex); // -1 = mixed values → treat as None
            bool hasSecondLine = type != FlowTrigger.TriggerType.None && type != FlowTrigger.TriggerType.Back;
            return EditorGUIUtility.singleLineHeight + (hasSecondLine ? Line : 0f);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty typeProperty = property.FindPropertyRelative("type");

            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(NeoGUI.NextLine(ref line), typeProperty, label);

            var type = (FlowTrigger.TriggerType)Mathf.Max(0, typeProperty.enumValueIndex);
            switch (type)
            {
                case FlowTrigger.TriggerType.Timer:
                    EditorGUI.PropertyField(EditorGUI.IndentedRect(NeoGUI.NextLine(ref line)),
                        property.FindPropertyRelative("timerDuration"));
                    break;

                case FlowTrigger.TriggerType.None:
                case FlowTrigger.TriggerType.Back:
                    break;

                default:
                    Rect pair = EditorGUI.IndentedRect(NeoGUI.NextLine(ref line));
                    IdDatabaseOptions.DrawCategoryNamePair(pair,
                        property.FindPropertyRelative("category"),
                        property.FindPropertyRelative("name"),
                        IdDatabaseOptions.ForTrigger(type));
                    break;
            }
            EditorGUI.EndProperty();
        }
    }

    /// <summary>
    /// UINode view reference: one line, category + view name dropdowns from the view ID database —
    /// the same picker UIView's id field uses, so show/hide view lists are consistent with it.
    /// </summary>
    [CustomPropertyDrawer(typeof(UINode.ViewRef))]
    public class ViewRefDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            // inside lists the per-element "Element N" label is noise — use the full width
            Rect content = label.text.StartsWith("Element", StringComparison.Ordinal)
                ? position
                : EditorGUI.PrefixLabel(position, label);

            NeoUISettings settings = NeoUISettings.instance;
            IdDatabaseOptions.DrawCategoryNamePair(content,
                property.FindPropertyRelative("category"),
                property.FindPropertyRelative("viewName"),
                settings != null ? settings.viewIds : null);
            EditorGUI.EndProperty();
        }
    }

    /// <summary>
    /// Flow edge: collapsed to a "port → target (trigger)" summary line; expanded, the target node
    /// is a dropdown of the graph's executable nodes and the weight only appears on RandomNode
    /// outputs (the only node type that reads it).
    /// </summary>
    [CustomPropertyDrawer(typeof(FlowEdge))]
    public class FlowEdgeDrawer : PropertyDrawer
    {
        private const string NotConnected = "(not connected)";

        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;

            height += Line * 3f; // portName, toNode, allowsBack
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("trigger")) +
                      EditorGUIUtility.standardVerticalSpacing;
            if (OwnerIsRandomNode(property)) height += Line;
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty portNameProperty = property.FindPropertyRelative("portName");
            SerializedProperty toNodeProperty = property.FindPropertyRelative("toNode");
            SerializedProperty triggerProperty = property.FindPropertyRelative("trigger");

            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(NeoGUI.NextLine(ref line), property.isExpanded,
                Summarize(portNameProperty, toNodeProperty, triggerProperty), toggleOnLabelClick: true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(NeoGUI.NextLine(ref line), portNameProperty);
                DrawTargetNodeDropdown(NeoGUI.NextLine(ref line), toNodeProperty);
                EditorGUI.PropertyField(NeoGUI.NextLine(ref line), property.FindPropertyRelative("allowsBack"));

                float triggerHeight = EditorGUI.GetPropertyHeight(triggerProperty);
                EditorGUI.PropertyField(new Rect(line.x, line.y, line.width, triggerHeight), triggerProperty);
                line.y += triggerHeight + EditorGUIUtility.standardVerticalSpacing;

                if (OwnerIsRandomNode(property))
                    EditorGUI.PropertyField(NeoGUI.NextLine(ref line), property.FindPropertyRelative("weight"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        private static void DrawTargetNodeDropdown(Rect rect, SerializedProperty toNodeProperty)
        {
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("To Node", toNodeProperty.tooltip));

            if (!(toNodeProperty.serializedObject.targetObject is FlowGraph graph))
            {
                EditorGUI.PropertyField(rect, toNodeProperty, GUIContent.none);
                return;
            }

            string current = toNodeProperty.stringValue;
            SerializedObject serializedObject = toNodeProperty.serializedObject;
            string path = toNodeProperty.propertyPath;

            NeoDropdown.ValuePopup(rect, current, () => TargetNodeOptions(graph), selected =>
            {
                serializedObject.Update();
                SerializedProperty resolved = serializedObject.FindProperty(path);
                if (resolved == null) return;
                resolved.stringValue = selected == NotConnected ? "" : selected;
                serializedObject.ApplyModifiedProperties();
            }, emptyLabel: NotConnected);
        }

        private static List<string> TargetNodeOptions(FlowGraph graph)
        {
            var options = new List<string> { NotConnected };
            foreach (FlowNode node in graph.nodes)
                if (node != null && node.isExecutable && !string.IsNullOrEmpty(node.name))
                    options.Add(node.name);
            return options;
        }

        private static readonly GUIContent SummaryContent = new GUIContent();

        private static GUIContent Summarize(SerializedProperty portName, SerializedProperty toNode,
            SerializedProperty trigger)
        {
            string port = string.IsNullOrEmpty(portName.stringValue) ? "Out" : portName.stringValue;
            string target = string.IsNullOrEmpty(toNode.stringValue) ? NotConnected : toNode.stringValue;
            var type = (FlowTrigger.TriggerType)Mathf.Max(0, trigger.FindPropertyRelative("type").enumValueIndex);
            string suffix = type == FlowTrigger.TriggerType.None ? "" : $"  [{type}]";
            SummaryContent.text = $"{port} → {target}{suffix}";
            return SummaryContent;
        }

        /// <summary> True when this edge sits in a RandomNode's outputs (path: nodes.Array.data[i].outputs...). </summary>
        private static bool OwnerIsRandomNode(SerializedProperty property)
        {
            if (!(property.serializedObject.targetObject is FlowGraph graph)) return false;
            string path = property.propertyPath;
            const string prefix = "nodes.Array.data[";
            int start = path.IndexOf(prefix, StringComparison.Ordinal);
            if (start != 0) return false;
            int end = path.IndexOf(']', prefix.Length);
            if (end < 0 || !int.TryParse(path.Substring(prefix.Length, end - prefix.Length), out int index)) return false;
            return index >= 0 && index < graph.nodes.Count && graph.nodes[index] is RandomNode;
        }
    }
}
