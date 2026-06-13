using Neo.EditorUI;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Shared inspector base: accent header + scriptless property drawing. Every Neo UI
    /// component inspector goes through here so the whole package reads the same way: family color,
    /// title, subtitle (usually the id), then fields — flat IMGUI, zero per-frame allocations of
    /// styles or lists.
    /// </summary>
    public abstract class NeoUIEditor : UnityEditor.Editor
    {
        protected abstract string HeaderTitle { get; }
        protected virtual string HeaderSubtitle => null;
        protected abstract Color Accent { get; }

        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader(HeaderTitle, HeaderSubtitle, Accent);
            serializedObject.UpdateIfRequiredOrScript();
            DrawBody();
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawBody() => NeoGUI.DrawProperties(serializedObject);

        protected static string Describe(CategoryNameId id) =>
            id == null ? null : $"{id.Category} / {id.Name}";
    }

    // ------------------------------------------------------------------ containers (cyan)

    /// <summary> Container family base: callbacks tucked into a persistent foldout. </summary>
    public abstract class UIContainerFamilyEditor : NeoUIEditor
    {
        private static readonly string[] Callbacks =
            { "OnShowCallback", "OnVisibleCallback", "OnHideCallback", "OnHiddenCallback" };

        protected override Color Accent => NeoColors.Containers;

        protected override void DrawBody()
        {
            NeoGUI.DrawProperties(serializedObject, Callbacks);
            if (NeoGUI.BeginFoldoutSection($"NeoUI.{target.GetType().Name}.Callbacks", "Callbacks"))
                foreach (string callback in Callbacks)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(callback));
            NeoGUI.EndFoldoutSection();
        }
    }

    [CustomEditor(typeof(UIContainer)), CanEditMultipleObjects]
    public class UIContainerEditor : UIContainerFamilyEditor
    {
        protected override string HeaderTitle => "UIContainer";
    }

    [CustomEditor(typeof(UIView)), CanEditMultipleObjects]
    public class UIViewEditor : UIContainerFamilyEditor
    {
        protected override string HeaderTitle => "UIView";
        protected override string HeaderSubtitle => Describe(((UIView)target).id);
    }

    [CustomEditor(typeof(UIPopup)), CanEditMultipleObjects]
    public class UIPopupEditor : UIContainerFamilyEditor
    {
        protected override string HeaderTitle => "UIPopup";
        protected override string HeaderSubtitle => ((UIPopup)target).popupName;
    }

    [CustomEditor(typeof(UITooltip)), CanEditMultipleObjects]
    public class UITooltipEditor : UIContainerFamilyEditor
    {
        protected override string HeaderTitle => "UITooltip";
    }

    // ------------------------------------------------------------------ interactive (blue)

    [CustomEditor(typeof(UIButton)), CanEditMultipleObjects]
    public class UIButtonEditor : SelectableEditor
    {
        public override void OnInspectorGUI()
        {
            var button = (UIButton)target;
            NeoGUI.ComponentHeader("UIButton", $"{button.id.Category} / {button.id.Name}", NeoColors.Interactive);

            serializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));
            GUILayout.Space(NeoGUI.Spacing);
            if (targets.Length == 1)
                NeoListView.Draw(serializedObject.FindProperty("behaviours"), "Behaviours");
            else // ReorderableList doesn't multi-edit safely
                EditorGUILayout.PropertyField(serializedObject.FindProperty("behaviours"), includeChildren: true);
            serializedObject.ApplyModifiedProperties();

            if (NeoGUI.BeginFoldoutSection("NeoUI.UIButton.Selectable", "Selectable & Navigation"))
                base.OnInspectorGUI();
            NeoGUI.EndFoldoutSection();
        }
    }

    [CustomEditor(typeof(UIToggle)), CanEditMultipleObjects]
    public class UIToggleEditor : SelectableEditor
    {
        public override void OnInspectorGUI()
        {
            var toggle = (UIToggle)target;
            NeoGUI.ComponentHeader(target.GetType().Name, $"{toggle.id.Category} / {toggle.id.Name}", NeoColors.Interactive);

            serializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isOnValue"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("groupReference"));
            DrawExtraFields();

            if (NeoGUI.BeginFoldoutSection($"NeoUI.{target.GetType().Name}.Events", "Events"))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onValueChanged"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onToggleOn"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onToggleOff"));
            }
            NeoGUI.EndFoldoutSection();
            serializedObject.ApplyModifiedProperties();

            if (NeoGUI.BeginFoldoutSection($"NeoUI.{target.GetType().Name}.Selectable", "Selectable & Navigation"))
                base.OnInspectorGUI();
            NeoGUI.EndFoldoutSection();
        }

        protected virtual void DrawExtraFields() { }
    }

    [CustomEditor(typeof(UITab)), CanEditMultipleObjects]
    public class UITabEditor : UIToggleEditor
    {
        protected override void DrawExtraFields() =>
            EditorGUILayout.PropertyField(serializedObject.FindProperty("containerReference"));
    }

    [CustomEditor(typeof(UISlider)), CanEditMultipleObjects]
    public class UISliderEditor : SliderEditor
    {
        public override void OnInspectorGUI()
        {
            var slider = (UISlider)target;
            NeoGUI.ComponentHeader("UISlider", $"{slider.id.Category} / {slider.id.Name}", NeoColors.Interactive);

            serializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));

            if (NeoGUI.BeginFoldoutSection("NeoUI.UISlider.Events", "Value Events"))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnValueIncremented"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnValueDecremented"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnValueReachedMin"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnValueReachedMax"));
            }
            NeoGUI.EndFoldoutSection();
            serializedObject.ApplyModifiedProperties();

            if (NeoGUI.BeginFoldoutSection("NeoUI.UISlider.Base", "Slider"))
                base.OnInspectorGUI();
            NeoGUI.EndFoldoutSection();
        }
    }

    [CustomEditor(typeof(UIToggleGroup)), CanEditMultipleObjects]
    public class UIToggleGroupEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "UIToggle Group";
        protected override string HeaderSubtitle => Describe(((UIToggleGroup)target).id);
        protected override Color Accent => NeoColors.Interactive;
    }

    [CustomEditor(typeof(UIStepper)), CanEditMultipleObjects]
    public class UIStepperEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "UIStepper";
        protected override Color Accent => NeoColors.Interactive;
    }

    [CustomEditor(typeof(UITag)), CanEditMultipleObjects]
    public class UITagEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "UITag";
        protected override string HeaderSubtitle => Describe(((UITag)target).id);
        protected override Color Accent => NeoColors.Interactive;
    }

    [CustomEditor(typeof(WidgetStyleTag)), CanEditMultipleObjects]
    public class WidgetStyleTagEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Widget Style Tag";
        protected override string HeaderSubtitle =>
            $"{((WidgetStyleTag)target).variant} · {((WidgetStyleTag)target).size}";
        protected override Color Accent => NeoColors.Interactive;
    }

    [CustomEditor(typeof(UICounter)), CanEditMultipleObjects]
    public class UICounterEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Counter";
        protected override Color Accent => NeoColors.Interactive;
    }

    [CustomEditor(typeof(UIBadge)), CanEditMultipleObjects]
    public class UIBadgeEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Badge";
        protected override string HeaderSubtitle => ((UIBadge)target).count.ToString();
        protected override Color Accent => NeoColors.Interactive;
    }

    [CustomEditor(typeof(UISoundRelay)), CanEditMultipleObjects]
    public class UISoundRelayEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Sound Relay";
        protected override Color Accent => NeoColors.Signals;
    }

    [CustomEditor(typeof(UICascadeChildren)), CanEditMultipleObjects]
    public class UICascadeChildrenEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Cascade Children";
        protected override Color Accent => NeoColors.Containers;
    }

    // ------------------------------------------------------------------ animators (orange)

    [CustomEditor(typeof(UIContainerColorAnimator)), CanEditMultipleObjects]
    public class UIContainerColorAnimatorEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Container Color Animator";
        protected override Color Accent => NeoColors.Animation;
    }

    [CustomEditor(typeof(UISelectableColorAnimator)), CanEditMultipleObjects]
    public class UISelectableColorAnimatorEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Selectable Color Animator";
        protected override Color Accent => NeoColors.Animation;
    }

    [CustomEditor(typeof(UIToggleColorAnimator)), CanEditMultipleObjects]
    public class UIToggleColorAnimatorEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Toggle Color Animator";
        protected override Color Accent => NeoColors.Animation;
    }

    [CustomEditor(typeof(Progressor)), CanEditMultipleObjects]
    public class ProgressorEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Progressor";
        protected override Color Accent => NeoColors.Animation;
    }

    // ------------------------------------------------------------------ theming (pink)

    [CustomEditor(typeof(ThemeColorTarget)), CanEditMultipleObjects]
    public class ThemeColorTargetEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Theme Color Target";
        protected override string HeaderSubtitle => ((ThemeColorTarget)target).token;
        protected override Color Accent => NeoColors.Theming;

        protected override void DrawBody()
        {
            SerializedProperty tokenProperty = serializedObject.FindProperty("token");
            Rect rect = EditorGUILayout.GetControlRect();
            GUIContent tokenLabel = EditorGUI.BeginProperty(rect, new GUIContent("Token", tokenProperty.tooltip), tokenProperty);
            rect = EditorGUI.PrefixLabel(rect, tokenLabel);
            NeoDropdown.StringPopup(rect, tokenProperty, TokenOptions, "(no token)");
            EditorGUI.EndProperty();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("themeOverride"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tint"));
        }

        private System.Collections.Generic.List<string> TokenOptions()
        {
            var options = new System.Collections.Generic.List<string>();
            var targetComponent = (ThemeColorTarget)target;
            Theme theme = targetComponent.themeOverride != null
                ? targetComponent.themeOverride
                : NeoUISettings.instance != null ? NeoUISettings.instance.theme : null;
            if (theme != null) options.AddRange(theme.GetTokenNames());
            return options;
        }
    }

    [CustomEditor(typeof(Theme))]
    public class ThemeEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Theme";
        protected override string HeaderSubtitle =>
            $"{((Theme)target).Variants.Count} variant(s) · {((Theme)target).ShapeStyles.Count} shape · " +
            $"{((Theme)target).TextStyles.Count} text style(s)";
        protected override Color Accent => NeoColors.Theming;
    }

    // ------------------------------------------------------------------ settings / data (yellow)

    [CustomEditor(typeof(NeoUISettings))]
    public class NeoUISettingsEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Neo UI Settings";
        protected override string HeaderSubtitle => "single settings asset";
        protected override Color Accent => NeoColors.Data;
    }

    [CustomEditor(typeof(IdDatabase), true)]
    public class IdDatabaseEditor : NeoUIEditor
    {
        protected override string HeaderTitle => target.GetType().Name;
        protected override Color Accent => NeoColors.Data;
    }
}
