using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Inspector for <see cref="NeoWidgetPreset"/> — the design-system "component" layer. A thin shell:
    /// a Theming-pink accent header (see <see cref="NeoUIEditor"/>) over the shared
    /// <see cref="WidgetPresetGUI"/> form, so this inspector and the Design System window's Presets tab
    /// (Phase 2.3) render the SAME three sections (Identity · Style references · Direct defaults) from
    /// ONE implementation. The base's <see cref="NeoUIEditor.OnInspectorGUI"/> owns the
    /// <c>Update</c>/<c>ApplyModifiedProperties</c> transaction the drawer relies on.
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

        // Default key prefix keeps the historical inspector foldout collapse state.
        protected override void DrawBody() => WidgetPresetGUI.Draw(serializedObject);
    }
}
