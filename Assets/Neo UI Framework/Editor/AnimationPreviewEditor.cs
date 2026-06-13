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

    [CustomEditor(typeof(UIAnimator))]
    [CanEditMultipleObjects]
    public class UIAnimatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("UIAnimator", null, NeoColors.Animation);
            DrawDefaultInspector();
            var animator = (UIAnimator)target;
            if (targets.Length == 1)
                AnimationPreview.DrawControls(animator.animation, animator.GetComponent<RectTransform>());
        }
    }

    [CustomEditor(typeof(UIContainerUIAnimator))]
    [CanEditMultipleObjects]
    public class UIContainerUIAnimatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("Container UI Animator", null, NeoColors.Animation);
            DrawDefaultInspector();
            var animator = (UIContainerUIAnimator)target;
            if (targets.Length != 1) return;
            var rectTransform = animator.GetComponent<RectTransform>();
            AnimationPreview.DrawControls(animator.showAnimation, rectTransform, "Show");
            AnimationPreview.DrawControls(animator.hideAnimation, rectTransform, "Hide");
        }
    }

    [CustomEditor(typeof(UISelectableUIAnimator))]
    [CanEditMultipleObjects]
    public class UISelectableUIAnimatorEditor : UnityEditor.Editor
    {
        private static readonly string[] StateNames = { "Normal", "Highlighted", "Pressed", "Selected", "Disabled" };
        private int _stateIndex;

        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("Selectable UI Animator", null, NeoColors.Animation);
            DrawDefaultInspector();
            var animator = (UISelectableUIAnimator)target;
            if (targets.Length != 1) return;
            _stateIndex = GUILayout.Toolbar(_stateIndex, StateNames);
            AnimationPreview.DrawControls(
                animator.GetAnimation((UISelectionState)_stateIndex),
                animator.GetComponent<RectTransform>(),
                StateNames[_stateIndex]);
        }
    }

    [CustomEditor(typeof(UIToggleUIAnimator))]
    [CanEditMultipleObjects]
    public class UIToggleUIAnimatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            NeoGUI.ComponentHeader("Toggle UI Animator", null, NeoColors.Animation);
            DrawDefaultInspector();
            var animator = (UIToggleUIAnimator)target;
            if (targets.Length != 1) return;
            var rectTransform = animator.GetComponent<RectTransform>();
            AnimationPreview.DrawControls(animator.onAnimation, rectTransform, "On");
            AnimationPreview.DrawControls(animator.offAnimation, rectTransform, "Off");
        }
    }
}
