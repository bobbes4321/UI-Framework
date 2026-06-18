using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    // ------------------------------------------------------------------ rendering (lime)

    [CustomEditor(typeof(NeoShape)), CanEditMultipleObjects]
    public class NeoShapeEditor : NeoUIEditor
    {
        private static readonly GUIContent CornersLabel = new GUIContent("Corners",
            "Per-corner radius: top-left, top-right, bottom-right, bottom-left");
        private static readonly GUIContent StrokeLabel = new GUIContent("Stroke Width",
            "Glyph stroke thickness in px (0 = auto, scales with the rect)");
        private static readonly GUIContent[] CornerLabels =
            { new GUIContent("TL"), new GUIContent("TR"), new GUIContent("BR"), new GUIContent("BL") };
        private static readonly string[] CornerFields = { "x", "y", "z", "w" };

        protected override string HeaderTitle => "Shape";
        protected override string HeaderSubtitle => ((NeoShape)target).shape.ToString();
        protected override Color Accent => NeoColors.Rendering;

        protected override void DrawBody()
        {
            SerializedProperty shapeTypeProperty = serializedObject.FindProperty("shapeType");
            EditorGUILayout.PropertyField(shapeTypeProperty);

            // mixed multi-edit selection: only the always-valid fields, no conditionals
            bool known = !shapeTypeProperty.hasMultipleDifferentValues && shapeTypeProperty.enumValueIndex >= 0;
            var shapeType = known ? (ShapeType)shapeTypeProperty.enumValueIndex : ShapeType.RoundedRect;
            bool isGlyph = known && (shapeType == ShapeType.Checkmark
                || shapeType == ShapeType.Chevron || shapeType == ShapeType.Cross);
            bool isArc = known && (shapeType == ShapeType.Ring || shapeType == ShapeType.Arc);

            if (known && shapeType == ShapeType.RoundedRect)
                DrawRadius();
            if (isArc)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("thickness"));
                if (shapeType == ShapeType.Arc)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("arcStartAngle"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("arcSweepAngle"));
                }
            }

            GUILayout.Space(NeoGUI.Spacing);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Color"));
            SerializedProperty fillModeProperty = serializedObject.FindProperty("fillMode");
            EditorGUILayout.PropertyField(fillModeProperty);
            if (!fillModeProperty.hasMultipleDifferentValues && fillModeProperty.enumValueIndex > 0)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fillColorB"));
                if (fillModeProperty.enumValueIndex == (int)ShapeFillMode.LinearGradient)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("gradientAngle"));
            }

            // texture fill: meaningless for stroke glyphs (the stroke IS the fill)
            if (!isGlyph)
            {
                SerializedProperty fillSpriteProperty = serializedObject.FindProperty("fillSprite");
                EditorGUILayout.PropertyField(fillSpriteProperty);
                if (fillSpriteProperty.hasMultipleDifferentValues || fillSpriteProperty.objectReferenceValue != null)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("textureFit"));
            }

            GUILayout.Space(NeoGUI.Spacing);
            if (isGlyph)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("borderWidth"), StrokeLabel);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("borderWidth"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("borderColor"));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("softness"));

            GUILayout.Space(NeoGUI.Spacing);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_RaycastTarget"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Maskable"));
        }

        private void DrawRadius()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("radiusUnit"));
            SerializedProperty uniformProperty = serializedObject.FindProperty("uniformRadius");
            EditorGUILayout.PropertyField(uniformProperty);
            if (uniformProperty.hasMultipleDifferentValues) return;

            if (uniformProperty.boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("radius"));
                return;
            }

            SerializedProperty perCorner = serializedObject.FindProperty("radiusPerCorner");
            Rect line = EditorGUILayout.GetControlRect();
            line = EditorGUI.PrefixLabel(line, CornersLabel);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 24f;
            float fieldWidth = (line.width - 3f * 4f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var fieldRect = new Rect(line.x + i * (fieldWidth + 4f), line.y, fieldWidth, line.height);
                EditorGUI.PropertyField(fieldRect, perCorner.FindPropertyRelative(CornerFields[i]), CornerLabels[i]);
            }
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }
    }

    [CustomEditor(typeof(NeoGradient)), CanEditMultipleObjects]
    public class NeoGradientEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Gradient";
        protected override Color Accent => NeoColors.Rendering;
    }

    // ------------------------------------------------------------------ theming (pink)

    [CustomEditor(typeof(ThemeTextStyleTarget)), CanEditMultipleObjects]
    public class ThemeTextStyleTargetEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Theme Text Style Target";
        protected override string HeaderSubtitle => ((ThemeTextStyleTarget)target).style;
        protected override Color Accent => NeoColors.Theming;

        protected override void DrawBody()
        {
            SerializedProperty styleProperty = serializedObject.FindProperty("style");
            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent styleLabel = EditorGUI.BeginProperty(rect, new GUIContent("Style", styleProperty.tooltip), styleProperty);
            rect = EditorGUI.PrefixLabel(rect, styleLabel);
            NeoDropdown.StringPopup(rect, styleProperty, StyleOptions, "(no style)", AddStyleToTheme);
            EditorGUI.EndProperty();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("themeOverride"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applyColor"));
        }

        private Theme BoundTheme()
        {
            var targetComponent = (ThemeTextStyleTarget)target;
            return targetComponent.themeOverride != null
                ? targetComponent.themeOverride
                : NeoUISettings.instance != null ? NeoUISettings.instance.theme : null;
        }

        private List<string> StyleOptions()
        {
            var options = new List<string>();
            Theme theme = BoundTheme();
            if (theme != null) options.AddRange(theme.GetTextStyleNames());
            return options;
        }

        private void AddStyleToTheme(string styleName)
        {
            Theme theme = BoundTheme();
            if (theme == null) return;
            Undo.RecordObject(theme, "Add Text Style");
            theme.SetTextStyle(new TextStyle { name = styleName });
            EditorUtility.SetDirty(theme);
        }
    }

    [CustomEditor(typeof(ThemeShapeStyleTarget)), CanEditMultipleObjects]
    public class ThemeShapeStyleTargetEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Theme Shape Style Target";
        protected override string HeaderSubtitle => ((ThemeShapeStyleTarget)target).style;
        protected override Color Accent => NeoColors.Theming;

        protected override void DrawBody()
        {
            SerializedProperty styleProperty = serializedObject.FindProperty("style");
            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent styleLabel = EditorGUI.BeginProperty(rect, new GUIContent("Style", styleProperty.tooltip), styleProperty);
            rect = EditorGUI.PrefixLabel(rect, styleLabel);
            NeoDropdown.StringPopup(rect, styleProperty, StyleOptions, "(no style)", AddStyleToTheme);
            EditorGUI.EndProperty();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("themeOverride"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applyFillColor"));
        }

        private Theme BoundTheme()
        {
            var targetComponent = (ThemeShapeStyleTarget)target;
            return targetComponent.themeOverride != null
                ? targetComponent.themeOverride
                : NeoUISettings.instance != null ? NeoUISettings.instance.theme : null;
        }

        private List<string> StyleOptions()
        {
            var options = new List<string>();
            Theme theme = BoundTheme();
            if (theme != null) options.AddRange(theme.GetShapeStyleNames());
            return options;
        }

        private void AddStyleToTheme(string styleName)
        {
            Theme theme = BoundTheme();
            if (theme == null) return;
            Undo.RecordObject(theme, "Add Shape Style");
            theme.SetShapeStyle(new ShapeStyle { name = styleName });
            EditorUtility.SetDirty(theme);
        }
    }
}
