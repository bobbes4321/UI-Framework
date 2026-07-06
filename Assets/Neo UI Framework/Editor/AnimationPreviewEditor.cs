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

        // After-Effects-style scrub: presence of a target's key IS the "scrub session active" flag
        // (mirrors how Snapshots doubles as the Play/Stop session flag above), value is the last
        // scrubbed progress (0..1) so the slider/playhead redraw at the same spot between repaints.
        private static readonly Dictionary<RectTransform, float> ScrubProgress = new Dictionary<RectTransform, float>();

        public static bool IsPreviewing(RectTransform target) => target != null && Snapshots.ContainsKey(target);

        public static bool IsScrubbing(RectTransform target) => target != null && ScrubProgress.ContainsKey(target);

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
                        ScrubProgress.Remove(target);
                    }
                }
            }

            DrawScrubber(animation, target);
            DrawChannelLanes(animation, target);
        }

        // ------------------------------------------------------------------ scrub bar

        /// <summary> Horizontal 0..1 progress slider that poses the animation without playing it. </summary>
        private static void DrawScrubber(UIAnimation animation, RectTransform target)
        {
            bool disabled = target == null || !animation.hasEnabledChannels;
            float total = animation.totalDuration;
            float progress = ScrubProgress.TryGetValue(target, out float stored) ? stored : 0f;

            using (new EditorGUI.DisabledScope(disabled))
            using (new EditorGUILayout.HorizontalScope())
            {
                s_scrubLabelContent.tooltip = disabled
                    ? "Enable at least one channel to scrub."
                    : "Drag to pose the animation at any point in time — stays posed until Stop or a new scrub.";
                GUILayout.Label(s_scrubLabelContent, GUILayout.Width(38f));

                EditorGUI.BeginChangeCheck();
                float next = GUILayout.HorizontalSlider(progress, 0f, 1f);
                bool changed = EditorGUI.EndChangeCheck();

                GUILayout.Label($"{next * total:0.00}s / {total:0.00}s", EditorStyles.miniLabel, GUILayout.Width(90f));

                if (changed && !disabled) BeginOrContinueScrub(animation, target, next);
            }
        }

        /// <summary>
        /// First value-change of a scrub session halts any running tween and prepares the animation
        /// exactly like the Play/Reverse/From/To buttons (<see cref="PreparePreview"/>) so the scrub
        /// starts from a known, snapshot-restorable pose; every later change just re-poses.
        /// </summary>
        private static void BeginOrContinueScrub(UIAnimation animation, RectTransform target, float progress)
        {
            if (!ScrubProgress.ContainsKey(target))
            {
                if (animation.isActive) animation.Stop(silent: true);
                PreparePreview(animation, target);
            }
            ScrubProgress[target] = progress;
            animation.SetProgressAt(progress);
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ channel lanes

        private static readonly GUIContent s_scrubLabelContent = new GUIContent("Scrub");
        private static readonly GUIContent s_laneHoverContent = new GUIContent(string.Empty);
        private static GUIStyle s_laneLabelStyle;

        private static GUIStyle LaneLabelStyle => s_laneLabelStyle ?? (s_laneLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        });

        /// <summary>
        /// Read-only strip below the scrubber: one thin bar per enabled channel spanning
        /// [startDelay, startDelay+duration] over the animation's total duration, plus a playhead
        /// while a scrub session is active. Draws nothing when no channel is enabled.
        /// </summary>
        private static void DrawChannelLanes(UIAnimation animation, RectTransform target)
        {
            // This overload reads the playhead from the shared scrub-session dictionary (the inspector's
            // own preview lifecycle); the target-free overload below does the drawing so a host that owns
            // its own preview/scrub lifecycle can reuse the exact same lane strip.
            bool showPlayhead = IsScrubbing(target);
            float progress = showPlayhead && ScrubProgress.TryGetValue(target, out float p) ? p : 0f;
            DrawChannelLanes(animation, progress, showPlayhead);
        }

        /// <summary>
        /// Read-only M/R/S/F/C channel-lane strip for one animation, decoupled from the scrub-session
        /// dictionary so a host that owns its own preview/scrub lifecycle (the Design System Motion tab)
        /// renders the identical lanes. One thin bar per enabled channel spanning
        /// [startDelay, startDelay+duration] over the total duration; <paramref name="scrubProgress01"/>
        /// (0..1) positions a playhead when <paramref name="showPlayhead"/> is true. Draws nothing when
        /// no channel is enabled. (Behaviour-neutral extraction — the inspector overload above delegates
        /// here, so the animator inspectors are unchanged.)
        /// </summary>
        public static void DrawChannelLanes(UIAnimation animation, float scrubProgress01, bool showPlayhead)
        {
            if (animation == null || !animation.hasEnabledChannels) return;

            float total = animation.totalDuration;
            float scrubSeconds = showPlayhead ? scrubProgress01 * total : 0f;

            GUILayout.Space(2f);
            DrawChannelLane('M', animation.move.enabled, animation.move.settings, NeoColors.Animation, total, scrubSeconds, showPlayhead);
            DrawChannelLane('R', animation.rotate.enabled, animation.rotate.settings, NeoColors.Data, total, scrubSeconds, showPlayhead);
            DrawChannelLane('S', animation.scale.enabled, animation.scale.settings, NeoColors.Rendering, total, scrubSeconds, showPlayhead);
            DrawChannelLane('F', animation.fade.enabled, animation.fade.settings, NeoColors.Interactive, total, scrubSeconds, showPlayhead);
            DrawChannelLane('C', animation.color.enabled, animation.color.settings, NeoColors.Theming, total, scrubSeconds, showPlayhead);
        }

        private static void DrawChannelLane(char label, bool enabled, TweenSettings settings, Color tint,
            float total, float scrubSeconds, bool showPlayhead)
        {
            if (!enabled) return;

            const float labelWidth = 14f;
            Rect row = GUILayoutUtility.GetRect(10f, 12f, GUILayout.ExpandWidth(true));
            var labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            var barRect = new Rect(row.x + labelWidth, row.y + 1f, row.width - labelWidth, row.height - 2f);

            GUI.Label(labelRect, label.ToString(), LaneLabelStyle);
            EditorGUI.DrawRect(barRect, NeoColors.TextDim.WithAlpha(0.18f));

            float delay = settings.startDelay;
            float duration = settings.duration;
            if (total > 0f)
            {
                float startFrac = Mathf.Clamp01(delay / total);
                float endFrac = Mathf.Clamp01((delay + duration) / total);
                if (endFrac > startFrac)
                {
                    var segment = new Rect(barRect.x + barRect.width * startFrac, barRect.y,
                        barRect.width * (endFrac - startFrac), barRect.height);
                    EditorGUI.DrawRect(segment, tint);
                }
            }

            if (showPlayhead && total > 0f)
            {
                float x = barRect.x + barRect.width * Mathf.Clamp01(scrubSeconds / total);
                EditorGUI.DrawRect(new Rect(x, row.y, 1.5f, row.height), NeoColors.TextTitle);
            }

            // Invisible label over the bar purely to surface a tooltip on hover — cheap (no texture/
            // style allocation) and the GUIContent instance is reused across every lane/repaint.
            s_laneHoverContent.tooltip = $"delay {delay:0.00}s · duration {duration:0.00}s · {settings.ease}";
            GUI.Label(barRect, s_laneHoverContent);
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
        /// The "seed this slot from a library preset" row. The button shows the slot's applied preset
        /// (<see cref="UIAnimation.sourcePreset"/>, stamped by <see cref="UIAnimationPreset.CopyTo"/>)
        /// and opens <see cref="AnimationPresetBrowserPopup"/> — presets grouped by category with the
        /// role's suggested categories expanded first, a "None" row that clears the slot, and
        /// hover-to-preview on the actual selected widget (single selection only). The ✕ button clears
        /// the slot without opening the popup. <paramref name="getSlot"/> picks the UIAnimation for a
        /// given target (the currently-selected state/show-hide/on-off slot); <paramref name="role"/>
        /// may be null (no suggested grouping).
        /// </summary>
        public static void PresetPicker(SerializedObject so, string role, System.Func<Object, UIAnimation> getSlot)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string tooltip = string.IsNullOrEmpty(role)
                    ? "Seed this slot from a library preset, then tweak it freely."
                    : $"Seed this slot ({NeoAnimatorRoles.DisplayName(role)}) from a library preset, then tweak it freely.";
                GUILayout.Label(new GUIContent("Preset", tooltip), GUILayout.Width(52f));

                UIAnimation first = so.targetObjects.Length > 0 ? getSlot(so.targetObjects[0]) : null;
                string current = string.IsNullOrEmpty(first?.sourcePreset) ? null : first.sourcePreset;
                bool mixed = false;
                for (int i = 1; i < so.targetObjects.Length && !mixed; i++)
                {
                    string other = getSlot(so.targetObjects[i])?.sourcePreset;
                    mixed = !string.Equals(string.IsNullOrEmpty(other) ? null : other, current, System.StringComparison.Ordinal);
                }

                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                string label = mixed ? "—" : current ?? "Apply preset…";
                if (GUI.Button(rect, new GUIContent(label, tooltip), EditorStyles.popup))
                {
                    // Live hover-preview only makes sense on a single, concrete widget.
                    RectTransform previewTarget = so.targetObjects.Length == 1 && so.targetObject is Component component
                        ? component.transform as RectTransform : null;
                    PopupWindow.Show(rect, new AnimationPresetBrowserPopup(role, mixed ? null : current,
                        chosen =>
                        {
                            if (chosen == null) ClearSlot(so, getSlot);
                            else ApplyPreset(so, getSlot, chosen);
                        }, previewTarget));
                }

                bool clearable = mixed || (first != null && (first.hasEnabledChannels || current != null));
                using (new EditorGUI.DisabledScope(!clearable))
                {
                    if (GUILayout.Button(new GUIContent("✕", "Clear this slot — disables every channel."),
                            EditorStyles.miniButton, GUILayout.Width(22f)))
                        ClearSlot(so, getSlot);
                }
            }
            GUILayout.Space(NeoGUI.Spacing);
        }

        private static void ApplyPreset(SerializedObject so, System.Func<Object, UIAnimation> getSlot, UIAnimationPreset chosen)
        {
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

        // A cached, never-saved blank preset: "clear" = copy fresh defaults (every channel disabled)
        // through the ONE copy path, then drop the stamp CopyTo just wrote — a cleared slot has no source.
        private static UIAnimationPreset s_emptyPreset;

        private static void ClearSlot(SerializedObject so, System.Func<Object, UIAnimation> getSlot)
        {
            if (s_emptyPreset == null)
            {
                s_emptyPreset = ScriptableObject.CreateInstance<UIAnimationPreset>();
                s_emptyPreset.hideFlags = HideFlags.HideAndDontSave;
            }

            try
            {
                foreach (Object target in so.targetObjects)
                {
                    UIAnimation slot = getSlot(target);
                    if (slot == null) continue;
                    Undo.RecordObject(target, "Clear Animation Slot");
                    s_emptyPreset.CopyTo(slot);
                    slot.sourcePreset = null;
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
            // Every state maps to a project role, so the browser can suggest and defaults can seed.
            string role = index == 1 ? NeoAnimatorRoles.ButtonHover
                : index == 2 ? NeoAnimatorRoles.ButtonPress
                : index == 3 ? NeoAnimatorRoles.SelectableSelected
                : index == 4 ? NeoAnimatorRoles.SelectableDisabled
                : NeoAnimatorRoles.SelectableNormal;
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
