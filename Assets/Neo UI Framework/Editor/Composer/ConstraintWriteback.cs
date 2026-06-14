using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Pure math bridging a direct-manipulation gesture (a new device-space rect) and the Figma-style
    /// constraint+offset placement model (<see cref="LayoutSpec"/>). It is the correctness keystone of
    /// Pillar D: a drag/resize no longer writes an absolute <c>position</c>/<c>size</c>, it writes
    /// <c>layout</c> offsets STORED RELATIVE TO THE ELEMENT'S CURRENT CONSTRAINT against the live parent
    /// rect — so a right-glued element stays glued when the viewport changes aspect, a stretched element
    /// keeps its insets, a scaled element keeps its fractions. Mirrors the apply/detect semantics in
    /// <see cref="LayoutConstraints"/> / <see cref="ConstraintLayout"/> exactly (the same anchor model),
    /// but works directly on device-space rectangles instead of <c>RectTransform</c>s so the canvas can
    /// run it without building objects.
    ///
    /// <para><b>Coordinate convention.</b> All rects here are DEVICE space with the y axis growing UP
    /// (bottom-origin), the same space the constraint anchors live in. <c>xMin</c> is the left edge,
    /// <c>xMax</c> the right; <c>yMin</c> the bottom edge, <c>yMax</c> the top. A <see cref="ConstraintWriteback"/>
    /// caller converts the canvas' screen-px boxes (y-down, 0..1 normalized) into this space once
    /// (multiply normalized by device size; the preview already stores <c>norm</c> y-from-bottom, so a
    /// device rect is <c>norm * deviceSize</c> with no flip).</para>
    ///
    /// <para>Shared with Pillar F's constraint widget, which consumes it read-only.</para>
    /// </summary>
    public static class ConstraintWriteback
    {
        /// <summary>
        /// Writes <paramref name="element"/>'s new device-space rect into its <c>layout</c> offsets,
        /// honoring its CURRENT constraint (<c>layout.h</c>/<c>layout.v</c>, defaulting to left/top), and
        /// the fixed-axis size where the constraint doesn't stretch. The element keeps its constraint
        /// ids; only the offset/size numbers change. Mutate inside a <see cref="SpecDocument.ApplyEdit"/>.
        /// </summary>
        public static void Write(ElementSpec element, Rect elementDevice, Rect parentDevice)
        {
            if (element == null) return;
            LayoutSpec layout = element.layout ?? (element.layout = new LayoutSpec());
            string h = string.IsNullOrEmpty(layout.h) ? ConstraintLayout.DefaultH : layout.h;
            string v = string.IsNullOrEmpty(layout.v) ? ConstraintLayout.DefaultV : layout.v;
            layout.h = h;
            layout.v = v;

            LayoutOffset offset = layout.offset ?? (layout.offset = new LayoutOffset());
            LayoutSize size = layout.size ?? new LayoutSize();

            WriteAxis(h, LayoutAxis.Horizontal, elementDevice, parentDevice, offset, size);
            WriteAxis(v, LayoutAxis.Vertical, elementDevice, parentDevice, offset, size);

            layout.offset = offset.IsEmpty ? null : offset;
            layout.size = size.IsEmpty ? null : size;
        }

        /// <summary>
        /// The inverse of <see cref="Write"/>: resolves where <paramref name="layout"/>'s offsets place
        /// the element inside <paramref name="parentDevice"/>, as a device-space rect. The
        /// <paramref name="currentSize"/> supplies the extent on a stretched axis (stretch is
        /// layout-driven — the stored offsets are insets/fractions, not a size) and the fallback extent
        /// on a fixed axis when <c>layout.size</c> is absent. Round-trips with <see cref="Write"/> for
        /// every constraint × axis.
        /// </summary>
        public static Rect Resolve(LayoutSpec layout, Rect parentDevice, Vector2 currentSize)
        {
            string h = layout != null && !string.IsNullOrEmpty(layout.h) ? layout.h : ConstraintLayout.DefaultH;
            string v = layout != null && !string.IsNullOrEmpty(layout.v) ? layout.v : ConstraintLayout.DefaultV;
            LayoutOffset off = layout?.offset;
            LayoutSize size = layout?.size;

            ResolveAxis(h, LayoutAxis.Horizontal, parentDevice, off, size, currentSize.x,
                out float xMin, out float width);
            ResolveAxis(v, LayoutAxis.Vertical, parentDevice, off, size, currentSize.y,
                out float yMin, out float height);

            return new Rect(xMin, yMin, width, height);
        }

        // ------------------------------------------------------------------ per-axis writeback

        private static void WriteAxis(string constraint, LayoutAxis axis, Rect element, Rect parent,
            LayoutOffset offset, LayoutSize size)
        {
            // Per-axis device extents (y grows up, so "min" is bottom on the vertical axis).
            float eMin = axis == LayoutAxis.Horizontal ? element.xMin : element.yMin;
            float eMax = axis == LayoutAxis.Horizontal ? element.xMax : element.yMax;
            float pMin = axis == LayoutAxis.Horizontal ? parent.xMin : parent.yMin;
            float pMax = axis == LayoutAxis.Horizontal ? parent.xMax : parent.yMax;
            float pExtent = pMax - pMin;
            float eExtent = eMax - eMin;

            switch (constraint)
            {
                case LayoutConstraints.Left:
                    offset.Set("left", eMin - pMin);
                    SetSize(size, axis, eExtent);
                    break;
                case LayoutConstraints.Right:
                    offset.Set("right", pMax - eMax);
                    SetSize(size, axis, eExtent);
                    break;
                case LayoutConstraints.Top:
                    offset.Set("top", pMax - eMax);
                    SetSize(size, axis, eExtent);
                    break;
                case LayoutConstraints.Bottom:
                    offset.Set("bottom", eMin - pMin);
                    SetSize(size, axis, eExtent);
                    break;
                case LayoutConstraints.Center:
                    // signed center delta from the parent center
                    offset.Set(axis == LayoutAxis.Horizontal ? "h" : "v",
                        (eMin + eMax) * 0.5f - (pMin + pMax) * 0.5f);
                    SetSize(size, axis, eExtent);
                    break;
                case LayoutConstraints.LeftRight:
                    offset.Set("left", eMin - pMin);
                    offset.Set("right", pMax - eMax);
                    break;
                case LayoutConstraints.TopBottom:
                    offset.Set("top", pMax - eMax);
                    offset.Set("bottom", eMin - pMin);
                    break;
                case LayoutConstraints.Scale:
                    // fractions of the parent extent (guard a zero-size parent)
                    if (pExtent > Mathf.Epsilon)
                    {
                        float startFrac = (eMin - pMin) / pExtent;
                        float endFrac = (eMax - pMin) / pExtent;
                        if (axis == LayoutAxis.Horizontal)
                        {
                            offset.Set("left", startFrac);
                            offset.Set("right", endFrac);
                        }
                        else
                        {
                            offset.Set("bottom", startFrac);
                            offset.Set("top", endFrac);
                        }
                    }
                    break;
                default:
                    // project constraint: store generic edge insets so a custom constraint round-trips
                    if (axis == LayoutAxis.Horizontal)
                    {
                        offset.Set("left", eMin - pMin);
                        offset.Set("right", pMax - eMax);
                    }
                    else
                    {
                        offset.Set("bottom", eMin - pMin);
                        offset.Set("top", pMax - eMax);
                    }
                    break;
            }
        }

        private static void SetSize(LayoutSize size, LayoutAxis axis, float extent)
        {
            if (axis == LayoutAxis.Horizontal) size.w = extent;
            else size.h = extent;
        }

        // ------------------------------------------------------------------ per-axis resolve (inverse)

        private static void ResolveAxis(string constraint, LayoutAxis axis, Rect parent,
            LayoutOffset offset, LayoutSize size, float currentExtent, out float min, out float extent)
        {
            float pMin = axis == LayoutAxis.Horizontal ? parent.xMin : parent.yMin;
            float pMax = axis == LayoutAxis.Horizontal ? parent.xMax : parent.yMax;
            float pExtent = pMax - pMin;
            float pCenter = (pMin + pMax) * 0.5f;

            float storedSize = SizeOr(size, axis, currentExtent);

            switch (constraint)
            {
                case LayoutConstraints.Left:
                    extent = storedSize;
                    min = pMin + Get(offset, "left", 0f);
                    break;
                case LayoutConstraints.Right:
                    extent = storedSize;
                    min = pMax - Get(offset, "right", 0f) - extent;
                    break;
                case LayoutConstraints.Top:
                    extent = storedSize;
                    min = pMax - Get(offset, "top", 0f) - extent;
                    break;
                case LayoutConstraints.Bottom:
                    extent = storedSize;
                    min = pMin + Get(offset, "bottom", 0f);
                    break;
                case LayoutConstraints.Center:
                    extent = storedSize;
                    min = pCenter + Get(offset, axis == LayoutAxis.Horizontal ? "h" : "v", 0f) - extent * 0.5f;
                    break;
                case LayoutConstraints.LeftRight:
                    min = pMin + Get(offset, "left", 0f);
                    extent = (pMax - Get(offset, "right", 0f)) - min;
                    break;
                case LayoutConstraints.TopBottom:
                    min = pMin + Get(offset, "bottom", 0f);
                    extent = (pMax - Get(offset, "top", 0f)) - min;
                    break;
                case LayoutConstraints.Scale:
                    float startFrac = axis == LayoutAxis.Horizontal ? Get(offset, "left", 0f) : Get(offset, "bottom", 0f);
                    float endFrac = axis == LayoutAxis.Horizontal ? Get(offset, "right", 1f) : Get(offset, "top", 1f);
                    min = pMin + startFrac * pExtent;
                    extent = (endFrac - startFrac) * pExtent;
                    break;
                default:
                    min = pMin + Get(offset, axis == LayoutAxis.Horizontal ? "left" : "bottom", 0f);
                    extent = (pMax - Get(offset, axis == LayoutAxis.Horizontal ? "right" : "top", 0f)) - min;
                    break;
            }
        }

        private static float SizeOr(LayoutSize size, LayoutAxis axis, float fallback)
        {
            if (size != null)
            {
                float? v = axis == LayoutAxis.Horizontal ? size.w : size.h;
                if (v.HasValue) return v.Value;
            }
            return fallback;
        }

        private static float Get(LayoutOffset offset, string key, float fallback) =>
            offset != null ? offset.GetOr(key, fallback) : fallback;
    }
}
