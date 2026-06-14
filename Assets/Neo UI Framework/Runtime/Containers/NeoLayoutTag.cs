using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Round-trip marker stamped by the generator whenever an element is placed through the
    /// Figma-style constraint+offset <c>layout</c> model (as opposed to the legacy
    /// <c>anchor</c>/<c>position</c>/<c>size</c> path). The exporter reads this back to reverse-map
    /// the RectTransform into <c>layout</c> deterministically — raw anchors alias (e.g. a centered
    /// element and an equal-inset stretch can look identical), so the resolved constraint ids and
    /// offsets are recorded here rather than rediscovered. Absence of this component ⇒ legacy export.
    ///
    /// Force-text and trivially diffable. The per-axis offset uses the SAME convention as
    /// <c>LayoutSpec</c>: edge constraints store one value (distance from that edge / inset start),
    /// stretch constraints store two (start + end insets, or scale fractions), centers store a single
    /// signed value. <see cref="size"/> carries the authored fixed-axis size where the constraint is
    /// not stretched (negative = unset).
    /// </summary>
    [AddComponentMenu("Neo/UI/Containers/Neo Layout Tag")]
    public class NeoLayoutTag : MonoBehaviour
    {
        /// <summary> Horizontal constraint id ("left","right","leftRight","center","scale", …). </summary>
        public string h;
        /// <summary> Vertical constraint id ("top","bottom","topBottom","center","scale", …). </summary>
        public string v;

        /// <summary> Horizontal offset, primary value (edge distance / inset start / signed center / scale start). </summary>
        public float hOffset0;
        /// <summary> Horizontal offset, secondary value (inset end / scale end); only meaningful when h stretches. </summary>
        public float hOffset1;
        /// <summary> Vertical offset, primary value. </summary>
        public float vOffset0;
        /// <summary> Vertical offset, secondary value; only meaningful when v stretches. </summary>
        public float vOffset1;

        /// <summary> Authored width on a non-stretched horizontal axis; negative = unset. </summary>
        public float widthSize = -1f;
        /// <summary> Authored height on a non-stretched vertical axis; negative = unset. </summary>
        public float heightSize = -1f;

        /// <summary> Per-child sizing mode on the width axis ("fixed"/"hug"/"fill"); empty = unset. </summary>
        public string sizingW;
        /// <summary> Per-child sizing mode on the height axis; empty = unset. </summary>
        public string sizingH;
    }
}
