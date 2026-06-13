using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Drives a fill rect's horizontal anchor span from a progressor — the sprite-free way to
    /// fill a bar with an NeoShape pill, so the fill keeps rounded caps that an Image fillAmount
    /// can't give. Below the cap width the fill hides (a pill thinner than it is tall reads as
    /// a glitch); at zero it hides too.
    /// </summary>
    [AddComponentMenu("Neo/UI/Progress Targets/Rect Fill Progress Target")]
    public class RectFillProgressTarget : ProgressTarget
    {
        public RectTransform fill;

        private void Reset() => fill = transform as RectTransform;

        public override void UpdateTarget(Progressor progressor)
        {
            if (fill == null) return;
            float progress = Mathf.Clamp01(Pick(progressor));

            // keep the pill at least as wide as it is tall so its caps stay circular
            var parent = fill.parent as RectTransform;
            if (progress > 0f && parent != null && parent.rect.width > 1f)
            {
                float minNormalized = Mathf.Min(1f, fill.rect.height / parent.rect.width);
                progress = Mathf.Max(progress, minNormalized);
            }

            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(progress, 1f);

            var graphic = fill.GetComponent<Graphic>();
            if (graphic != null) graphic.enabled = progress > 0f;
        }
    }
}
