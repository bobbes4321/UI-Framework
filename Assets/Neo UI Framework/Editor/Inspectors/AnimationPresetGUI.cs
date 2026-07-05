using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The one implementation of a <see cref="UIAnimationPreset"/>'s editing form — identity
    /// (category / name) plus the five-channel <see cref="UIAnimation"/> body drawn through the shared
    /// <see cref="UIAnimationDrawer"/> (Move / Rotate / Scale / Fade / Color). Rendered as EditorUI-kit
    /// foldout sections so it reads like every other Neo authoring surface.
    ///
    /// <para>
    /// It is deliberately welded to NOTHING: it takes only a <see cref="SerializedObject"/> wrapping a
    /// <see cref="UIAnimationPreset"/> and draws its properties. It caches no per-OnGUI GUIStyles, makes
    /// no <see cref="Selection"/> assumptions and does NOT call
    /// <see cref="SerializedObject.Update"/>/<see cref="SerializedObject.ApplyModifiedProperties"/> — the
    /// caller owns the change transaction. So <see cref="UIAnimationPresetEditor"/> (the custom inspector)
    /// and the Design System window's Motion tab (Phase 2.4) can both embed the exact same form.
    /// </para>
    /// </summary>
    public static class AnimationPresetGUI
    {
        /// <summary>
        /// Draws the preset's category/name and five animation channels into the current IMGUI layout.
        /// </summary>
        /// <param name="presetObject">A <see cref="SerializedObject"/> wrapping a <see cref="UIAnimationPreset"/>
        /// (single- or multi-target). The caller must have called <see cref="SerializedObject.Update"/>
        /// beforehand and is responsible for <see cref="SerializedObject.ApplyModifiedProperties"/> after.</param>
        /// <param name="sectionKeyPrefix">Foldout session-state key prefix — pass a distinct value per host
        /// (inspector vs window) if the two should not share collapse state; defaults to a shared key.</param>
        public static void Draw(SerializedObject presetObject, string sectionKeyPrefix = "NeoUI.AnimPreset")
        {
            if (presetObject == null) return;

            SerializedProperty category = presetObject.FindProperty("category");
            SerializedProperty presetName = presetObject.FindProperty("presetName");
            SerializedProperty animation = presetObject.FindProperty("animation");

            if (NeoGUI.BeginFoldoutSection(sectionKeyPrefix + ".Identity", "Identity", defaultOpen: true))
            {
                if (category != null) EditorGUILayout.PropertyField(category);
                if (presetName != null) EditorGUILayout.PropertyField(presetName);
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection(sectionKeyPrefix + ".Channels", "Channels", defaultOpen: true))
            {
                // UIAnimationDrawer renders the Move/Rotate/Scale/Fade/Color strip inline — no prefix label.
                if (animation != null) EditorGUILayout.PropertyField(animation, GUIContent.none);
            }
            NeoGUI.EndFoldoutSection();
        }
    }
}
