using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Pins this RectTransform to the device safe area (notches, rounded corners, home bars).
    /// Anchors are recomputed from <see cref="Screen.safeArea"/> only when the screen or
    /// orientation actually changes — no per-frame work beyond two int compares. In the editor the
    /// safe area equals the full screen, so authored layouts are unaffected.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("AlterEyes/UI/Safe Area Fitter")]
    public class SafeAreaFitter : MonoBehaviour
    {
        private Rect _appliedSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private int _appliedWidth;
        private int _appliedHeight;

        private void OnEnable() => Apply();

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled) Apply();
        }

        public void Apply()
        {
            // edit mode (including headless preview scenes): Screen describes the editor window,
            // not the canvas being rendered — applying its safe area skews authored layouts
            // (and writing rect values from OnRectTransformDimensionsChange recurses). The
            // factory authors the safe area full-stretch; leave it untouched until play mode.
            if (!Application.isPlaying) return;

            Rect safeArea = Screen.safeArea;
            if (safeArea == _appliedSafeArea && Screen.width == _appliedWidth && Screen.height == _appliedHeight)
                return;
            if (Screen.width <= 0 || Screen.height <= 0) return;

            _appliedSafeArea = safeArea;
            _appliedWidth = Screen.width;
            _appliedHeight = Screen.height;

            var rect = (RectTransform)transform;
            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
