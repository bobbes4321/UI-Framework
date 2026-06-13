using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Edit-mode tick source: drives <see cref="UITick"/> from EditorApplication.update while the
    /// editor is not in play mode — the enabler for previewing animations without entering play mode.
    /// Repaints scene views while anything is animating.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorHeartbeat
    {
        private static double s_lastUpdateTime;

        static EditorHeartbeat()
        {
            s_lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            var deltaTime = (float)(now - s_lastUpdateTime);
            s_lastUpdateTime = now;

            // In play mode the RuntimeHeartbeat drives the tick service.
            if (Application.isPlaying) return;
            if (UITick.count == 0) return;

            UITick.Tick(Mathf.Min(deltaTime, 0.1f));
            SceneView.RepaintAll();
            InternalEditorUtilityRepaintGameView();
        }

        private static void InternalEditorUtilityRepaintGameView()
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
