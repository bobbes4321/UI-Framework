using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Shared edit-mode preview controls: Play / Play Reverse / Stop / jump-to-From / jump-to-To,
    /// driven by the editor heartbeat. Captures the object's state before previewing and reverts it
    /// cleanly when the preview stops.
    /// </summary>
    public static class AnimationPreview
    {
        private class Snapshot
        {
            public RectTransform rectTransform;
            public Vector3 anchoredPosition;
            public Vector3 eulerAngles;
            public Vector3 scale;
            public CanvasGroup canvasGroup;
            public float alpha;
        }

        private static readonly Dictionary<RectTransform, Snapshot> Snapshots = new Dictionary<RectTransform, Snapshot>();

        public static bool IsPreviewing(RectTransform target) => target != null && Snapshots.ContainsKey(target);

        public static void BeginPreview(RectTransform target)
        {
            if (target == null || Snapshots.ContainsKey(target)) return;
            Snapshots[target] = new Snapshot
            {
                rectTransform = target,
                anchoredPosition = target.anchoredPosition3D,
                eulerAngles = target.localEulerAngles,
                scale = target.localScale,
                canvasGroup = target.GetComponent<CanvasGroup>(),
                alpha = target.GetComponent<CanvasGroup>() != null ? target.GetComponent<CanvasGroup>().alpha : 1f
            };
        }

        public static void EndPreview(RectTransform target)
        {
            if (target == null || !Snapshots.TryGetValue(target, out Snapshot snapshot)) return;
            Snapshots.Remove(target);
            if (snapshot.rectTransform == null) return;
            snapshot.rectTransform.anchoredPosition3D = snapshot.anchoredPosition;
            snapshot.rectTransform.localEulerAngles = snapshot.eulerAngles;
            snapshot.rectTransform.localScale = snapshot.scale;
            if (snapshot.canvasGroup != null) snapshot.canvasGroup.alpha = snapshot.alpha;
            SceneView.RepaintAll();
        }

        /// <summary> Draws the preview toolbar for one UIAnimation. Returns true if any control was used. </summary>
        public static void DrawControls(UIAnimation animation, RectTransform target, string animationLabel = null)
        {
            if (animation == null || target == null) return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (!string.IsNullOrEmpty(animationLabel))
                    GUILayout.Label(animationLabel, EditorStyles.miniBoldLabel, GUILayout.Width(60f));

                bool wasPreviewing = IsPreviewing(target);

                if (GUILayout.Button("▶ Play", EditorStyles.miniButtonLeft))
                {
                    StartPreview(animation, target, PlayDirection.Forward);
                }
                if (GUILayout.Button("◀ Reverse", EditorStyles.miniButtonMid))
                {
                    StartPreview(animation, target, PlayDirection.Reverse);
                }
                if (GUILayout.Button("⏮ From", EditorStyles.miniButtonMid))
                {
                    PreparePreview(animation, target);
                    animation.SetProgressAtZero();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("⏭ To", EditorStyles.miniButtonMid))
                {
                    PreparePreview(animation, target);
                    animation.SetProgressAtOne();
                    SceneView.RepaintAll();
                }
                using (new EditorGUI.DisabledScope(!wasPreviewing))
                {
                    if (GUILayout.Button("■ Stop", EditorStyles.miniButtonRight))
                    {
                        StopPreview(animation, target);
                    }
                }
            }
        }

        private static void PreparePreview(UIAnimation animation, RectTransform target)
        {
            bool fresh = !IsPreviewing(target);
            if (fresh) BeginPreview(target);
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null && animation.fade.enabled) group = target.gameObject.AddComponent<CanvasGroup>();
            animation.SetTarget(target, group);
            if (fresh) animation.CaptureStartValues();
        }

        private static void StartPreview(UIAnimation animation, RectTransform target, PlayDirection direction)
        {
            PreparePreview(animation, target);
            animation.onFinish = null;
            animation.Play(direction);
        }

        private static void StopPreview(UIAnimation animation, RectTransform target)
        {
            animation.Stop(silent: true);
            EndPreview(target);
        }
    }

    /// <summary>
    /// Shared L1 tab bar for the multi-animation animators (states, show/hide, on/off). Each tab is
    /// tinted with the Animation accent and suffixed with a dot when that animation drives any
    /// channel, so the whole state machine's coverage reads at a glance — and the SAME selection
    /// drives both editing and the preview controls (no more separate edit-vs-preview navigation).
    /// </summary>
    internal static class AnimatorEditorGUI
    {
        public static int AnimationTabBar(string sessionKey, string[] labels, bool[] hasChannels)
        {
            int current = Mathf.Clamp(SessionState.GetInt(sessionKey, 0), 0, labels.Length - 1);
            int result = current;

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    bool isSelected = i == current;
                    GUIStyle style = labels.Length == 1 ? EditorStyles.miniButton
                        : i == 0 ? EditorStyles.miniButtonLeft
                        : i == labels.Length - 1 ? EditorStyles.miniButtonRight
                        : EditorStyles.miniButtonMid;

                    var content = new GUIContent(hasChannels[i] ? labels[i] + "  •" : labels[i],
                        hasChannels[i] ? "Drives one or more channels" : "No channels enabled");

                    Color previous = GUI.backgroundColor;
                    if (hasChannels[i] && !isSelected) GUI.backgroundColor = NeoColors.Animation;
                    bool pressed = GUILayout.Toggle(isSelected, content, style, GUILayout.Height(22f));
                    GUI.backgroundColor = previous;
                    // Only react to a tab flipping OFF→ON. These toggles are independent: clicking a
                    // new tab leaves the old one reporting `true` too, so a plain `if (pressed)` would
                    // let the last still-on tab (Disabled) overwrite the click and trap selection.
                    if (pressed && !isSelected) result = i;
                }
            }

            if (result != current) SessionState.SetInt(sessionKey, result);
            GUILayout.Space(NeoGUI.Spacing);
            return result;
        }

        /// <summary>
        /// A "copy a library preset into this slot" row: a searchable dropdown of every discovered
        /// <see cref="UIAnimationPreset"/> (those whose category suits the slot's <paramref name="role"/>
        /// listed first), applied via <see cref="UIAnimationPreset.CopyTo"/> to every selected target so
        /// the slot is seeded then freely tweaked. <paramref name="getSlot"/> picks the UIAnimation for a
        /// given target (the currently-selected state/show-hide/on-off slot). <paramref name="role"/> may
        /// be null (no suggested grouping — just shows every preset).
        /// </summary>
        public static void PresetPicker(SerializedObject so, string role, System.Func<Object, UIAnimation> getSlot)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string tooltip = string.IsNullOrEmpty(role)
                    ? "Copy a library preset into this slot, then tweak it."
                    : $"Copy a preset into this slot ({NeoAnimatorRoles.DisplayName(role)}), then tweak it.";
                GUILayout.Label(new GUIContent("Preset", tooltip), GUILayout.Width(52f));
                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                NeoDropdown.ValuePopup(rect, "", () => AnimationPresetRegistry.FullNamesForRole(role),
                    chosen => ApplyPreset(so, getSlot, chosen), emptyLabel: "Apply preset…");
            }
            GUILayout.Space(NeoGUI.Spacing);
        }

        private static void ApplyPreset(SerializedObject so, System.Func<Object, UIAnimation> getSlot, string fullName)
        {
            UIAnimationPreset chosen = AnimationPresetRegistry.GetByFullName(fullName);
            if (chosen == null) return;

            try
            {
                foreach (Object target in so.targetObjects)
                {
                    UIAnimation slot = getSlot(target);
                    if (slot == null) continue;
                    Undo.RecordObject(target, "Apply Animation Preset");
                    chosen.CopyTo(slot);
                    EditorUtility.SetDirty(target);
                }
                if (so.targetObject != null) so.Update();
            }
            catch (System.Exception)
            {
                // the inspector (and its SerializedObject) may have gone away while the popup was open
            }
        }
    }

    [CustomEditor(typeof(UIAnimator))]
    [CanEditMultipleObjects]
    public class UIAnimatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("UI Animator", null, NeoColors.Animation);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onStartBehaviour"));
            AnimatorEditorGUI.PresetPicker(serializedObject, NeoAnimatorRoles.OneShot,
                o => ((UIAnimator)o).animation);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animation"));
            serializedObject.ApplyModifiedProperties();

            var animator = (UIAnimator)target;
            if (targets.Length == 1)
                AnimationPreview.DrawControls(animator.animation, animator.GetComponent<RectTransform>());
        }
    }

    [CustomEditor(typeof(UIContainerUIAnimator))]
    [CanEditMultipleObjects]
    public class UIContainerUIAnimatorEditor : UnityEditor.Editor
    {
        private static readonly string[] Labels = { "Show", "Hide" };
        private static readonly string[] Props = { "showAnimation", "hideAnimation" };

        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("Container UI Animator", null, NeoColors.Animation);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"));

            var animator = (UIContainerUIAnimator)target;
            var hasChannels = new[] { animator.showAnimation.hasEnabledChannels, animator.hideAnimation.hasEnabledChannels };
            int index = AnimatorEditorGUI.AnimationTabBar("Neo.ContainerAnim.tab", Labels, hasChannels);
            AnimatorEditorGUI.PresetPicker(serializedObject,
                index == 0 ? NeoAnimatorRoles.ViewShow : NeoAnimatorRoles.ViewHide,
                o => index == 0 ? ((UIContainerUIAnimator)o).showAnimation : ((UIContainerUIAnimator)o).hideAnimation);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(Props[index]), GUIContent.none);
            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1) return;
            UIAnimation selected = index == 0 ? animator.showAnimation : animator.hideAnimation;
            AnimationPreview.DrawControls(selected, animator.GetComponent<RectTransform>(), Labels[index]);
        }
    }

    [CustomEditor(typeof(UISelectableUIAnimator))]
    [CanEditMultipleObjects]
    public class UISelectableUIAnimatorEditor : UnityEditor.Editor
    {
        private static readonly string[] StateNames = { "Normal", "Highlighted", "Pressed", "Selected", "Disabled" };
        private static readonly string[] Props =
            { "normalAnimation", "highlightedAnimation", "pressedAnimation", "selectedAnimation", "disabledAnimation" };

        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("Selectable UI Animator", null, NeoColors.Animation);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"));

            var animator = (UISelectableUIAnimator)target;
            var hasChannels = new bool[StateNames.Length];
            for (int i = 0; i < StateNames.Length; i++)
                hasChannels[i] = animator.GetAnimation((UISelectionState)i).hasEnabledChannels;

            int index = AnimatorEditorGUI.AnimationTabBar("Neo.SelectableAnim.tab", StateNames, hasChannels);
            // Hover/Press map to project roles; Normal/Selected/Disabled have no shipped role (null →
            // the picker just lists every preset).
            string role = index == 1 ? NeoAnimatorRoles.ButtonHover
                : index == 2 ? NeoAnimatorRoles.ButtonPress : null;
            AnimatorEditorGUI.PresetPicker(serializedObject, role,
                o => ((UISelectableUIAnimator)o).GetAnimation((UISelectionState)index));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(Props[index]), GUIContent.none);
            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1) return;
            AnimationPreview.DrawControls(
                animator.GetAnimation((UISelectionState)index),
                animator.GetComponent<RectTransform>(),
                StateNames[index]);
        }
    }

    [CustomEditor(typeof(UIToggleUIAnimator))]
    [CanEditMultipleObjects]
    public class UIToggleUIAnimatorEditor : UnityEditor.Editor
    {
        private static readonly string[] Labels = { "On", "Off" };
        private static readonly string[] Props = { "onAnimation", "offAnimation" };

        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("Toggle UI Animator", null, NeoColors.Animation);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"));

            var animator = (UIToggleUIAnimator)target;
            var hasChannels = new[] { animator.onAnimation.hasEnabledChannels, animator.offAnimation.hasEnabledChannels };
            int index = AnimatorEditorGUI.AnimationTabBar("Neo.ToggleAnim.tab", Labels, hasChannels);
            AnimatorEditorGUI.PresetPicker(serializedObject,
                index == 0 ? NeoAnimatorRoles.ToggleOn : NeoAnimatorRoles.ToggleOff,
                o => index == 0 ? ((UIToggleUIAnimator)o).onAnimation : ((UIToggleUIAnimator)o).offAnimation);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(Props[index]), GUIContent.none);
            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1) return;
            UIAnimation selected = index == 0 ? animator.onAnimation : animator.offAnimation;
            AnimationPreview.DrawControls(selected, animator.GetComponent<RectTransform>(), Labels[index]);
        }
    }
}
