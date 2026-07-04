using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Inspector for <see cref="NeoWidgetPreset"/> — the design-system "component" layer. Reads like
    /// every other Neo inspector: a Theming-pink accent header, then three persistent foldout sections
    /// (Identity · Style references · Direct defaults) of flat IMGUI. Every styling field is a string
    /// reference INTO a lower layer, so each gets a searchable <see cref="NeoDropdown.StringPopup"/>
    /// sourced from the live theme/settings where the options are knowable (text/shape styles, button
    /// variant/size, theme tokens, icons, animation presets) — never a modal dialog. The picker still
    /// lets the author type a free value (the inline "+ Add" row) because a preset may reference a layer
    /// entry the project authors later; an empty selection means "this preset does not set this field".
    /// <para>
    /// Drawing is allocation-light: option lists are gathered only when a dropdown opens (the providers
    /// below are one-shot gathers, exactly like <see cref="NeoWidgetOptions"/>), the one cached
    /// <see cref="GUIStyle"/> is built lazily and reused, and there are no editor-tick subscriptions.
    /// All edits go through <c>serializedObject</c> + <see cref="EditorGUI.BeginChangeCheck"/> /
    /// <c>ApplyModifiedProperties</c> so multi-edit and undo behave (CLAUDE.md IMGUI rules).
    /// </para>
    /// </summary>
    [CustomEditor(typeof(NeoWidgetPreset)), CanEditMultipleObjects]
    public class NeoWidgetPresetEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Widget Preset";

        protected override string HeaderSubtitle
        {
            get
            {
                var preset = (NeoWidgetPreset)target;
                string name = string.IsNullOrEmpty(preset.presetName) ? "(unnamed)" : preset.presetName;
                return $"{name} · {preset.category} / {preset.targetKind}";
            }
        }

        // Presets live in the design-system tier alongside themes/text/shape styles — pink.
        protected override Color Accent => NeoColors.Theming;

        // The dim "(unset)" hint drawn next to a negative direct-default float. Built lazily and reused
        // — never allocated per OnGUI pass (cache GUIStyles, per the IMGUI rules).
        private static GUIStyle _hintStyle;

        private static GUIStyle HintStyle =>
            _hintStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Italic,
                normal = { textColor = NeoColors.TextDim }
            };

        protected override void DrawBody()
        {
            // -------------------------------------------------------------- Identity
            if (NeoGUI.BeginFoldoutSection("NeoUI.NeoWidgetPreset.Identity", "Identity", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("presetName"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("category"));
                StringPicker("targetKind", "Target Kind", KindOptions, "button");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            }
            NeoGUI.EndFoldoutSection();

            // -------------------------------------------------------------- Style references
            // Each field is optional and resolves at generate time. The dropdown options come from the
            // live theme/settings; a blank selection means the preset does not govern that field. Which
            // fields actually apply depends on `targetKind` (e.g. variant/size are button-ish, textStyle
            // is for text-bearing widgets) — we draw them all rather than hide by kind, so a multi-edit
            // selection of mixed kinds stays predictable; the labels carry the intent.
            if (NeoGUI.BeginFoldoutSection("NeoUI.NeoWidgetPreset.Style", "Style references", defaultOpen: true))
            {
                StringPicker("variant", "Variant", VariantOptions);            // button-ish
                StringPicker("sizeVariant", "Size", SizeOptions);              // button-ish
                StringPicker("textStyle", "Text Style", TextStyleOptions);     // text-bearing
                StringPicker("shapeStyle", "Shape Style", ShapeStyleOptions);  // surface
                StringPicker("motion", "Motion", MotionOptions);               // on-start / loop animation
                StringPicker("background", "Background Token", TokenOptions);  // theme token
                StringPicker("labelColor", "Label Color Token", TokenOptions); // theme token
                StringPicker("icon", "Icon", IconOptions);                     // Lucide glyph
            }
            NeoGUI.EndFoldoutSection();

            // -------------------------------------------------------------- Direct defaults
            // Float sentinels: negative == "not set". We draw the raw field and append a dim "(unset)"
            // hint when the value reads as unset, so authors see the difference between "0" and "ignored".
            if (NeoGUI.BeginFoldoutSection("NeoUI.NeoWidgetPreset.Defaults", "Direct defaults"))
            {
                UnsetAwareFloat("radius", "Radius");
                UnsetAwareFloat("padding", "Padding");
                UnsetAwareFloat("spacing", "Spacing");
                // padding4 wins over uniform padding when authored; draw the raw [l,t,r,b] array.
                EditorGUILayout.PropertyField(serializedObject.FindProperty("padding4"),
                    new GUIContent("Padding (L,T,R,B)", "Per-side container padding. Empty = not set; wins over uniform Padding."),
                    includeChildren: true);
            }
            NeoGUI.EndFoldoutSection();
        }

        // ------------------------------------------------------------------ field helpers

        /// <summary>
        /// Draws a label + searchable string dropdown bound to a string property, wrapped in
        /// <see cref="EditorGUI.BeginProperty"/> so the prefab-override bar and multi-edit mixed-value
        /// dash render correctly. The "+ Add" row lets the author type a value the option list doesn't
        /// know yet (a layer entry authored later); the option provider runs only when the popup opens.
        /// </summary>
        private void StringPicker(string propertyName, string label, System.Func<List<string>> options, string emptyLabel = "(not set)")
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent content = EditorGUI.BeginProperty(rect, new GUIContent(label, property.tooltip), property);
            rect = EditorGUI.PrefixLabel(rect, content);
            // onAddNew simply commits the typed value to the property — there is no central registry to
            // mutate here (the value is just a name that resolves at generate), so the picker stays a
            // pure free-or-pick field with no modal.
            NeoDropdown.StringPopup(rect, property, options, emptyLabel, onAddNew: _ => { });
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Draws a float property and, when it reads as the "unset" sentinel (negative), appends a dim
        /// "(unset)" hint to the right of the field. Wrapped in a change check so a multi-edit write
        /// never stomps unrelated targets.
        /// </summary>
        private void UnsetAwareFloat(string propertyName, string label)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Rect rect = EditorGUILayout.GetControlRect();

            // Reserve a slice on the right for the hint when the value is unset on a single target.
            bool unset = !property.hasMultipleDifferentValues && property.floatValue < 0f;
            Rect fieldRect = rect;
            if (unset)
            {
                const float hintWidth = 56f;
                fieldRect = new Rect(rect.x, rect.y, rect.width - hintWidth - 4f, rect.height);
                var hintRect = new Rect(fieldRect.xMax + 4f, rect.y, hintWidth, rect.height);
                GUI.Label(hintRect, "(unset)", HintStyle);
            }

            EditorGUI.BeginChangeCheck();
            GUIContent content = EditorGUI.BeginProperty(fieldRect, new GUIContent(label, property.tooltip), property);
            float value = EditorGUI.FloatField(fieldRect, content, property.floatValue);
            EditorGUI.EndProperty();
            if (EditorGUI.EndChangeCheck())
                property.floatValue = value;
        }

        // ------------------------------------------------------------------ option providers
        // One-shot gathers, invoked only when a dropdown opens (NeoDropdown semantics). They mirror the
        // Composer's option sources so the inspector and the Composer picker always agree.

        private static List<string> KindOptions() => new List<string>(ElementSpec.KnownKinds);

        private static List<string> VariantOptions() => new List<string>(NeoWidgetOptions.ButtonVariants);

        private static List<string> SizeOptions() => new List<string>(NeoWidgetOptions.ButtonSizes);

        private static List<string> TextStyleOptions() => NeoWidgetOptions.TextStyles();

        private static List<string> ShapeStyleOptions() => NeoWidgetOptions.ShapeStyles();

        private static List<string> IconOptions() => NeoWidgetOptions.Icons();

        // Theme tokens — no live document here, so pass null (project theme only).
        private static List<string> TokenOptions() => NeoWidgetOptions.Tokens(null);

        // Animation preset names from the settings' AnimationPresetDatabase (the motion seam).
        private static List<string> MotionOptions()
        {
            var list = new List<string>();
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.animationPresets != null)
                list.AddRange(settings.animationPresets.GetPresetNames());
            return list;
        }
    }
}
