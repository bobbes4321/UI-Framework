using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The one implementation of a <see cref="NeoWidgetPreset"/>'s editing form — Identity
    /// (name / category / target kind / description), Style references (the string references INTO the
    /// lower design-system layers — variant / size / text &amp; shape style / motion / tokens / icon), and
    /// Direct defaults (the -1-sentinel radius/padding/spacing floats). Rendered as EditorUI-kit foldout
    /// sections so it reads like every other Neo authoring surface.
    ///
    /// <para>
    /// Deliberately welded to NOTHING: it takes only a <see cref="SerializedObject"/> wrapping a
    /// <see cref="NeoWidgetPreset"/> and draws its properties. It caches no per-OnGUI GUIStyles (the one
    /// "(unset)" hint style is built lazily and reused), makes no <see cref="Selection"/> assumptions, and
    /// does NOT call <see cref="SerializedObject.Update"/> /
    /// <see cref="SerializedObject.ApplyModifiedProperties"/> — the caller owns the change transaction
    /// (same contract as <see cref="AnimationPresetGUI"/>). So <see cref="NeoWidgetPresetEditor"/> (the
    /// custom inspector) and the Design System window's Presets tab both embed the exact same form.
    /// </para>
    ///
    /// <para>
    /// Allocation-light: option lists are gathered only when a dropdown opens (the providers below are
    /// one-shot gathers, exactly like <see cref="NeoWidgetOptions"/>), and every edit goes through
    /// <c>serializedObject</c> + <see cref="EditorGUI.BeginChangeCheck"/> so multi-edit and undo behave
    /// (CLAUDE.md IMGUI rules).
    /// </para>
    /// </summary>
    public static class WidgetPresetGUI
    {
        /// <summary> Default foldout session-state key prefix — matches the historical inspector keys so
        /// the custom inspector keeps its exact collapse state. Pass a distinct value from a window host
        /// that shouldn't share collapse state with the inspector. </summary>
        public const string DefaultSectionKeyPrefix = "NeoUI.NeoWidgetPreset";

        // The dim "(unset)" hint drawn next to a negative direct-default float. Built lazily and reused —
        // never allocated per OnGUI pass (cache GUIStyles, per the IMGUI rules).
        private static GUIStyle _hintStyle;

        private static GUIStyle HintStyle =>
            _hintStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Italic,
                normal = { textColor = NeoColors.TextDim }
            };

        /// <summary>
        /// Draws the preset's identity, style references and direct defaults into the current IMGUI layout.
        /// </summary>
        /// <param name="presetObject">A <see cref="SerializedObject"/> wrapping a <see cref="NeoWidgetPreset"/>
        /// (single- or multi-target). The caller must have called <see cref="SerializedObject.Update"/>
        /// beforehand and is responsible for <see cref="SerializedObject.ApplyModifiedProperties"/> after.</param>
        /// <param name="sectionKeyPrefix">Foldout session-state key prefix — see
        /// <see cref="DefaultSectionKeyPrefix"/>.</param>
        public static void Draw(SerializedObject presetObject, string sectionKeyPrefix = DefaultSectionKeyPrefix)
        {
            if (presetObject == null) return;

            // -------------------------------------------------------------- Identity
            if (NeoGUI.BeginFoldoutSection(sectionKeyPrefix + ".Identity", "Identity", defaultOpen: true))
            {
                PropertyField(presetObject, "presetName");
                PropertyField(presetObject, "category");
                StringPicker(presetObject, "targetKind", "Target Kind", KindOptions, "button");
                PropertyField(presetObject, "description");
            }
            NeoGUI.EndFoldoutSection();

            // -------------------------------------------------------------- Style references
            // Each field is optional and resolves at generate time. The dropdown options come from the
            // live theme/settings; a blank selection means the preset does not govern that field. Which
            // fields actually apply depends on `targetKind` (e.g. variant/size are button-ish, textStyle
            // is for text-bearing widgets) — we draw them all rather than hide by kind, so a multi-edit
            // selection of mixed kinds stays predictable; the labels carry the intent.
            if (NeoGUI.BeginFoldoutSection(sectionKeyPrefix + ".Style", "Style references", defaultOpen: true))
            {
                StringPicker(presetObject, "variant", "Variant", VariantOptions);            // button-ish
                StringPicker(presetObject, "sizeVariant", "Size", SizeOptions);              // button-ish
                StringPicker(presetObject, "textStyle", "Text Style", TextStyleOptions);     // text-bearing
                StringPicker(presetObject, "shapeStyle", "Shape Style", ShapeStyleOptions);  // surface
                StringPicker(presetObject, "motion", "Motion", MotionOptions);               // on-start / loop animation
                StringPicker(presetObject, "background", "Background Token", TokenOptions);  // theme token
                StringPicker(presetObject, "labelColor", "Label Color Token", TokenOptions); // theme token
                StringPicker(presetObject, "icon", "Icon", IconOptions);                     // Lucide glyph
            }
            NeoGUI.EndFoldoutSection();

            // -------------------------------------------------------------- Direct defaults
            // Float sentinels: negative == "not set". We draw the raw field and append a dim "(unset)"
            // hint when the value reads as unset, so authors see the difference between "0" and "ignored".
            if (NeoGUI.BeginFoldoutSection(sectionKeyPrefix + ".Defaults", "Direct defaults"))
            {
                UnsetAwareFloat(presetObject, "radius", "Radius");
                UnsetAwareFloat(presetObject, "padding", "Padding");
                UnsetAwareFloat(presetObject, "spacing", "Spacing");
                // padding4 wins over uniform padding when authored; draw the raw [l,t,r,b] array.
                EditorGUILayout.PropertyField(presetObject.FindProperty("padding4"),
                    new GUIContent("Padding (L,T,R,B)", "Per-side container padding. Empty = not set; wins over uniform Padding."),
                    includeChildren: true);
            }
            NeoGUI.EndFoldoutSection();
        }

        // ------------------------------------------------------------------ field helpers

        private static void PropertyField(SerializedObject so, string propertyName)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property != null) EditorGUILayout.PropertyField(property);
        }

        /// <summary>
        /// Draws a label + searchable string dropdown bound to a string property, wrapped in
        /// <see cref="EditorGUI.BeginProperty"/> so the prefab-override bar and multi-edit mixed-value
        /// dash render correctly. The "+ Add" row lets the author type a value the option list doesn't
        /// know yet (a layer entry authored later); the option provider runs only when the popup opens.
        /// </summary>
        private static void StringPicker(SerializedObject so, string propertyName, string label,
            Func<List<string>> options, string emptyLabel = "(not set)")
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null) return;
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
        private static void UnsetAwareFloat(SerializedObject so, string propertyName, string label)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null) return;
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
        // One-shot gathers, invoked only when a dropdown opens (NeoDropdown semantics). They read the
        // same NeoWidgetOptions sources every other spec-authoring surface (scene-view overlay, agent
        // spec tooling) uses, so this form always agrees with them.

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
