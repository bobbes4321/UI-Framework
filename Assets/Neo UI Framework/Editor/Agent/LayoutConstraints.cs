using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary> The axis a <see cref="ILayoutConstraint"/> operates on. </summary>
    public enum LayoutAxis
    {
        Horizontal = 0,
        Vertical = 1
    }

    /// <summary>
    /// A per-axis offset value in the constraint model. Edge constraints (left/right/top/bottom) and
    /// center constraints use only <see cref="primary"/>; stretch constraints (leftRight/topBottom/
    /// scale) also use <see cref="secondary"/> (the end inset, or the scale end fraction).
    /// </summary>
    public struct LayoutOffsetValue
    {
        public float primary;
        public float secondary;

        public LayoutOffsetValue(float primary, float secondary = 0f)
        {
            this.primary = primary;
            this.secondary = secondary;
        }
    }

    /// <summary>
    /// One Figma-style placement constraint for a single axis. Maps a declared intent (stick to the
    /// left edge, stretch both edges, scale proportionally, …) onto a definite RectTransform
    /// anchor/pivot/offset configuration — the structural fix for "moved it in portrait, it
    /// disappears in landscape", because the offset is stored relative to the constraint, not as
    /// absolute canvas pixels.
    /// </summary>
    public interface ILayoutConstraint
    {
        /// <summary> Spec id, e.g. "left","right","leftRight","center","scale" (h) / "top",…,"scale" (v). </summary>
        string Id { get; }

        /// <summary> Which axis this constraint configures. </summary>
        LayoutAxis Axis { get; }

        /// <summary> True for leftRight/topBottom/scale — the size on this axis is layout-driven, not authored. </summary>
        bool Stretches { get; }

        /// <summary> Applies the constraint to one axis of <paramref name="rect"/>. <paramref name="size"/>
        /// is the authored fixed-axis size (ignored on a stretched axis; pass null to leave unchanged). </summary>
        void Apply(RectTransform rect, LayoutOffsetValue offset, float? size);

        /// <summary> Reverse-maps the axis of <paramref name="rect"/> into an offset (+ fixed-axis size).
        /// Returns false when the rect's anchors don't match this constraint. </summary>
        bool TryDetect(RectTransform rect, out LayoutOffsetValue offset, out float? size);
    }

    /// <summary>
    /// Pattern-R registry of layout constraints (the documented extensibility seam — a project can add
    /// e.g. a "centerBias" constraint without forking the package). Built-ins register in the static
    /// ctor in a FIXED order; the exporter detects in <see cref="All"/> order, first match wins, so
    /// detection stays deterministic. Mirrors <see cref="NeoElementKinds"/>: All / Get / Register
    /// (replace-by-Id+Axis, else append).
    /// </summary>
    public static class LayoutConstraints
    {
        public const string Left = "left";
        public const string Right = "right";
        public const string LeftRight = "leftRight";
        public const string Top = "top";
        public const string Bottom = "bottom";
        public const string TopBottom = "topBottom";
        public const string Center = "center";
        public const string Scale = "scale";

        private static readonly List<ILayoutConstraint> _all = new List<ILayoutConstraint>();

        static LayoutConstraints()
        {
            RegisterBuiltins();
        }

        private static void RegisterBuiltins()
        {
            // Order matters for deterministic exporter detection: edges first (most specific anchors),
            // then center, then stretch, then scale. Per axis.
            Register(new EdgeConstraint(Left, LayoutAxis.Horizontal, atMax: false));
            Register(new EdgeConstraint(Right, LayoutAxis.Horizontal, atMax: true));
            Register(new CenterConstraint(Center, LayoutAxis.Horizontal));
            Register(new StretchConstraint(LeftRight, LayoutAxis.Horizontal));
            Register(new ScaleConstraint(Scale, LayoutAxis.Horizontal));

            Register(new EdgeConstraint(Top, LayoutAxis.Vertical, atMax: true));
            Register(new EdgeConstraint(Bottom, LayoutAxis.Vertical, atMax: false));
            Register(new CenterConstraint(Center, LayoutAxis.Vertical));
            Register(new StretchConstraint(TopBottom, LayoutAxis.Vertical));
            Register(new ScaleConstraint(Scale, LayoutAxis.Vertical));
        }

        /// <summary> Every registered constraint (built-ins first, in registration order). </summary>
        public static IReadOnlyList<ILayoutConstraint> All => _all;

        /// <summary> Finds the constraint with the given id on the given axis; null + warning when missing. </summary>
        public static ILayoutConstraint Get(string id, LayoutAxis axis)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (ILayoutConstraint c in _all)
                    if (c != null && c.Axis == axis && c.Id == id) return c;
            Debug.LogWarning($"LayoutConstraints.Get: no constraint '{id}' registered for {axis}; placement will fall back to the default. Register one in LayoutConstraints, or check the spec.");
            return null;
        }

        /// <summary> Registers a constraint, replacing any existing one with the same Id+Axis. </summary>
        public static void Register(ILayoutConstraint constraint)
        {
            if (constraint == null || string.IsNullOrEmpty(constraint.Id))
            {
                Debug.LogWarning("LayoutConstraints.Register ignored a constraint with a null/empty Id.");
                return;
            }
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == constraint.Id && _all[i].Axis == constraint.Axis) { _all[i] = constraint; return; }
            _all.Add(constraint);
        }

        /// <summary> Test/seam hook: clear and re-seed the built-ins (static state survives a test run). </summary>
        internal static void ResetForTests()
        {
            _all.Clear();
            RegisterBuiltins();
        }

        // ----------------------------------------------------------------- axis helpers

        internal static float AnchorMin(RectTransform r, LayoutAxis a) => a == LayoutAxis.Horizontal ? r.anchorMin.x : r.anchorMin.y;
        internal static float AnchorMax(RectTransform r, LayoutAxis a) => a == LayoutAxis.Horizontal ? r.anchorMax.x : r.anchorMax.y;

        internal static void SetAnchors(RectTransform r, LayoutAxis a, float min, float max, float pivot)
        {
            if (a == LayoutAxis.Horizontal)
            {
                r.anchorMin = new Vector2(min, r.anchorMin.y);
                r.anchorMax = new Vector2(max, r.anchorMax.y);
                r.pivot = new Vector2(pivot, r.pivot.y);
            }
            else
            {
                r.anchorMin = new Vector2(r.anchorMin.x, min);
                r.anchorMax = new Vector2(r.anchorMax.x, max);
                r.pivot = new Vector2(r.pivot.x, pivot);
            }
        }

        internal static void SetOffsets(RectTransform r, LayoutAxis a, float offsetMin, float offsetMax)
        {
            if (a == LayoutAxis.Horizontal)
            {
                r.offsetMin = new Vector2(offsetMin, r.offsetMin.y);
                r.offsetMax = new Vector2(offsetMax, r.offsetMax.y);
            }
            else
            {
                r.offsetMin = new Vector2(r.offsetMin.x, offsetMin);
                r.offsetMax = new Vector2(r.offsetMax.x, offsetMax);
            }
        }

        internal static float OffsetMin(RectTransform r, LayoutAxis a) => a == LayoutAxis.Horizontal ? r.offsetMin.x : r.offsetMin.y;
        internal static float OffsetMax(RectTransform r, LayoutAxis a) => a == LayoutAxis.Horizontal ? r.offsetMax.x : r.offsetMax.y;

        internal static bool Approx(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        // ----------------------------------------------------------------- built-in constraints

        /// <summary>
        /// Glue to a single edge. Horizontal left → anchors 0, pivot 0, offsetMin.x = distance.
        /// Horizontal right / vertical top → anchors 1, pivot 1, offsetMax.x = -distance. Vertical
        /// bottom → anchors 0, pivot 0. Size drives the fixed-axis extent.
        /// </summary>
        private sealed class EdgeConstraint : ILayoutConstraint
        {
            private readonly bool _atMax;
            public EdgeConstraint(string id, LayoutAxis axis, bool atMax) { Id = id; Axis = axis; _atMax = atMax; }
            public string Id { get; }
            public LayoutAxis Axis { get; }
            public bool Stretches => false;

            public void Apply(RectTransform rect, LayoutOffsetValue offset, float? size)
            {
                float anchor = _atMax ? 1f : 0f;
                SetAnchors(rect, Axis, anchor, anchor, anchor);
                float extent = ResolveExtent(rect, size);
                if (_atMax)
                {
                    // pivot at 1: offsetMax = -distance, offsetMin = -(distance + extent)
                    SetOffsets(rect, Axis, -(offset.primary + extent), -offset.primary);
                }
                else
                {
                    // pivot at 0: offsetMin = distance, offsetMax = distance + extent
                    SetOffsets(rect, Axis, offset.primary, offset.primary + extent);
                }
            }

            public bool TryDetect(RectTransform rect, out LayoutOffsetValue offset, out float? size)
            {
                offset = default;
                size = null;
                float anchor = _atMax ? 1f : 0f;
                if (!Approx(AnchorMin(rect, Axis), anchor) || !Approx(AnchorMax(rect, Axis), anchor)) return false;
                float min = OffsetMin(rect, Axis);
                float max = OffsetMax(rect, Axis);
                if (_atMax)
                {
                    offset = new LayoutOffsetValue(-max);
                    size = max - min;
                }
                else
                {
                    offset = new LayoutOffsetValue(min);
                    size = max - min;
                }
                return true;
            }

            private float ResolveExtent(RectTransform rect, float? size)
            {
                if (size.HasValue && size.Value > 0f) return size.Value;
                // anchors are equal on this axis (just set), so sizeDelta is the literal extent
                float current = Axis == LayoutAxis.Horizontal ? rect.sizeDelta.x : rect.sizeDelta.y;
                return current > 0f ? current : 0f;
            }
        }

        /// <summary>
        /// Center on the axis: anchors 0.5, pivot 0.5. Offset is the signed displacement of the
        /// element center from the parent center (anchoredPosition). Size drives the extent.
        /// </summary>
        private sealed class CenterConstraint : ILayoutConstraint
        {
            public CenterConstraint(string id, LayoutAxis axis) { Id = id; Axis = axis; }
            public string Id { get; }
            public LayoutAxis Axis { get; }
            public bool Stretches => false;

            public void Apply(RectTransform rect, LayoutOffsetValue offset, float? size)
            {
                SetAnchors(rect, Axis, 0.5f, 0.5f, 0.5f);
                float extent = size.HasValue && size.Value > 0f
                    ? size.Value
                    : (Axis == LayoutAxis.Horizontal ? rect.sizeDelta.x : rect.sizeDelta.y);
                // pivot 0.5, anchors 0.5: offsetMin = pos - extent/2, offsetMax = pos + extent/2
                float half = extent * 0.5f;
                SetOffsets(rect, Axis, offset.primary - half, offset.primary + half);
            }

            public bool TryDetect(RectTransform rect, out LayoutOffsetValue offset, out float? size)
            {
                offset = default;
                size = null;
                if (!Approx(AnchorMin(rect, Axis), 0.5f) || !Approx(AnchorMax(rect, Axis), 0.5f)) return false;
                float min = OffsetMin(rect, Axis);
                float max = OffsetMax(rect, Axis);
                float extent = max - min;
                float center = (min + max) * 0.5f;
                offset = new LayoutOffsetValue(center);
                size = extent;
                return true;
            }
        }

        /// <summary>
        /// Stretch across the axis: anchorMin 0, anchorMax 1, pivot 0.5. Offset is [startInset,endInset];
        /// offsetMin = start, offsetMax = -end. Size is ignored (layout-driven).
        /// </summary>
        private sealed class StretchConstraint : ILayoutConstraint
        {
            public StretchConstraint(string id, LayoutAxis axis) { Id = id; Axis = axis; }
            public string Id { get; }
            public LayoutAxis Axis { get; }
            public bool Stretches => true;

            public void Apply(RectTransform rect, LayoutOffsetValue offset, float? size)
            {
                SetAnchors(rect, Axis, 0f, 1f, 0.5f);
                SetOffsets(rect, Axis, offset.primary, -offset.secondary);
            }

            public bool TryDetect(RectTransform rect, out LayoutOffsetValue offset, out float? size)
            {
                offset = default;
                size = null;
                if (!Approx(AnchorMin(rect, Axis), 0f) || !Approx(AnchorMax(rect, Axis), 1f)) return false;
                offset = new LayoutOffsetValue(OffsetMin(rect, Axis), -OffsetMax(rect, Axis));
                return true;
            }
        }

        /// <summary>
        /// Proportional scale: anchorMin = startFraction, anchorMax = endFraction, pivot 0.5, zero
        /// offsets. The element occupies a fraction of the parent and resizes with it. Offset carries
        /// [start,end] fractions (0..1).
        /// </summary>
        private sealed class ScaleConstraint : ILayoutConstraint
        {
            public ScaleConstraint(string id, LayoutAxis axis) { Id = id; Axis = axis; }
            public string Id { get; }
            public LayoutAxis Axis { get; }
            public bool Stretches => true;

            public void Apply(RectTransform rect, LayoutOffsetValue offset, float? size)
            {
                SetAnchors(rect, Axis, offset.primary, offset.secondary, 0.5f);
                SetOffsets(rect, Axis, 0f, 0f);
            }

            public bool TryDetect(RectTransform rect, out LayoutOffsetValue offset, out float? size)
            {
                offset = default;
                size = null;
                float min = AnchorMin(rect, Axis);
                float max = AnchorMax(rect, Axis);
                // a genuine scale: anchors strictly between 0..1 and not equal (a 0..1 stretch is the
                // StretchConstraint's job; an equal pair is an edge/center). Require near-zero offsets.
                if (!Approx(OffsetMin(rect, Axis), 0f) || !Approx(OffsetMax(rect, Axis), 0f)) return false;
                if (Approx(min, max)) return false;
                if (Approx(min, 0f) && Approx(max, 1f)) return false; // that's leftRight/topBottom
                offset = new LayoutOffsetValue(min, max);
                return true;
            }
        }
    }
}
