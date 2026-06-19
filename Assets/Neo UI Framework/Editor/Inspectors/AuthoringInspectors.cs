using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// EditorUI-kit inspectors for the authoring ScriptableObjects that previously fell back to Unity's
    /// raw default — so creating/editing a showcase or animation database reads like the rest of the
    /// package (accent header + grouped foldout sections via <see cref="NeoGUI"/>).
    /// </summary>
    [CustomEditor(typeof(ShowcaseDefinition))]
    public class ShowcaseDefinitionEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Showcase Definition";
        protected override string HeaderSubtitle =>
            string.IsNullOrEmpty(((ShowcaseDefinition)target).id) ? "drop into the Hub gallery"
                : ((ShowcaseDefinition)target).id;
        protected override Color Accent => NeoColors.Theming;

        protected override void DrawBody()
        {
            if (NeoGUI.BeginFoldoutSection("NeoUI.Showcase.Identity", "Identity", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("title"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("category"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.Showcase.Content", "Content", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("specJson"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("specPathOverride"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("flowName"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.Showcase.Media", "Media"))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("thumbnail"));
            NeoGUI.EndFoldoutSection();
        }
    }

    [CustomEditor(typeof(AnimationPresetDatabase))]
    public class AnimationPresetDatabaseEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Animation Preset Database";
        protected override string HeaderSubtitle =>
            $"{((AnimationPresetDatabase)target).Presets.Count} explicit preset(s)";
        protected override Color Accent => NeoColors.Animation;

        protected override void DrawBody()
        {
            EditorGUILayout.HelpBox(
                "Animation presets auto-discover: drop a UIAnimationPreset asset anywhere and reference it " +
                "by name from a spec — no need to list it here. This list is an explicit override/curation " +
                "(an entry here wins over a discovered asset of the same name).", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("presets"), true);
        }
    }
}
