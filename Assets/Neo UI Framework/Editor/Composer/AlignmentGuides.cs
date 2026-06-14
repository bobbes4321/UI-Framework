using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Figma/Sketch-style smart guides for the Composer canvas: while dragging, the moving rect's
    /// edges/centers are compared against every sibling's same anchors (within a pixel threshold) and,
    /// separately, equal gaps among three+ siblings are detected. The result is a snap offset (added to
    /// the drag) plus a set of guide lines / distribution pips to DRAW IN <c>OnGUI</c> — no editor-tick
    /// animation, no allocation beyond the per-gesture result list.
    ///
    /// <para>Pure screen-space geometry (the same screen px the canvas hit-tests in). It does not touch
    /// the spec — the canvas applies the returned snap to the gesture, then commits through
    /// <see cref="ConstraintWriteback"/> on mouse-up. Edge/center guides take priority over the 8px grid
    /// fallback (the canvas applies the grid only on the axes this returns no hit for).</para>
    /// </summary>
    public sealed class AlignmentGuides
    {
        /// <summary> Snap distance, screen px (matches the canvas' sibling-edge threshold). </summary>
        public const float Threshold = 6f;

        /// <summary> A guide line to draw: a full vertical (x set, y NaN) or horizontal (y set) rule,
        /// spanning the union of the moving rect and the sibling it aligned to. </summary>
        public struct Guide
        {
            public bool vertical;   // true = vertical rule at X, false = horizontal rule at Y
            public float pos;       // screen X (vertical) or Y (horizontal)
            public float from;      // span start (screen Y for vertical, screen X for horizontal)
            public float to;        // span end
        }

        /// <summary> An equal-spacing distribution pip pair drawn between two equally-gapped siblings. </summary>
        public struct Pip
        {
            public bool vertical;   // gap runs vertically (stacked) vs horizontally (side-by-side)
            public float center;    // the cross-axis center the pip sits on (screen px)
            public float a;         // gap start edge (screen px on the gap axis)
            public float b;         // gap end edge
        }

        private readonly List<Guide> _guides = new List<Guide>();
        private readonly List<Pip> _pips = new List<Pip>();

        public IReadOnlyList<Guide> Guides => _guides;
        public IReadOnlyList<Pip> Pips => _pips;

        /// <summary>
        /// Given the moving rect (already offset by the raw drag) and the sibling rects, computes the
        /// snap offset to align edges/centers and to maintain equal spacing, and records the guides/pips
        /// to draw. Returns the offset to ADD to the drag delta. Clears and refills the guide/pip lists.
        /// </summary>
        public Vector2 Compute(Rect moved, IReadOnlyList<Rect> siblings)
        {
            _guides.Clear();
            _pips.Clear();
            if (siblings == null || siblings.Count == 0) return Vector2.zero;

            float dx = SnapAxisHorizontal(moved, siblings, out bool hitX);
            float dy = SnapAxisVertical(moved, siblings, out bool hitY);

            // equal-spacing only contributes when not already pinned to an edge on that axis
            Rect adjusted = new Rect(moved.x + dx, moved.y + dy, moved.width, moved.height);
            if (!hitX) dx += EqualSpacingHorizontal(adjusted, siblings);
            if (!hitY) dy += EqualSpacingVertical(adjusted, siblings);

            return new Vector2(dx, dy);
        }

        // ------------------------------------------------------------------ edge / center snap

        private float SnapAxisHorizontal(Rect moved, IReadOnlyList<Rect> siblings, out bool hit)
        {
            float[] cands = { moved.xMin, moved.center.x, moved.xMax };
            float best = Threshold, offset = 0f, guideX = 0f;
            hit = false;
            foreach (Rect s in siblings)
            {
                float[] targets = { s.xMin, s.center.x, s.xMax };
                foreach (float c in cands)
                    foreach (float t in targets)
                    {
                        float d = t - c;
                        if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); offset = d; guideX = t; hit = true; }
                    }
            }
            if (hit)
            {
                Rect snapped = new Rect(moved.x + offset, moved.y, moved.width, moved.height);
                AddVerticalGuide(guideX, snapped, siblings);
            }
            return offset;
        }

        private float SnapAxisVertical(Rect moved, IReadOnlyList<Rect> siblings, out bool hit)
        {
            float[] cands = { moved.yMin, moved.center.y, moved.yMax };
            float best = Threshold, offset = 0f, guideY = 0f;
            hit = false;
            foreach (Rect s in siblings)
            {
                float[] targets = { s.yMin, s.center.y, s.yMax };
                foreach (float c in cands)
                    foreach (float t in targets)
                    {
                        float d = t - c;
                        if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); offset = d; guideY = t; hit = true; }
                    }
            }
            if (hit)
            {
                Rect snapped = new Rect(moved.x, moved.y + offset, moved.width, moved.height);
                AddHorizontalGuide(guideY, snapped, siblings);
            }
            return offset;
        }

        private void AddVerticalGuide(float x, Rect moved, IReadOnlyList<Rect> siblings)
        {
            float from = moved.yMin, to = moved.yMax;
            foreach (Rect s in siblings)
                if (Mathf.Abs(s.xMin - x) < 0.5f || Mathf.Abs(s.center.x - x) < 0.5f || Mathf.Abs(s.xMax - x) < 0.5f)
                { from = Mathf.Min(from, s.yMin); to = Mathf.Max(to, s.yMax); }
            _guides.Add(new Guide { vertical = true, pos = x, from = from, to = to });
        }

        private void AddHorizontalGuide(float y, Rect moved, IReadOnlyList<Rect> siblings)
        {
            float from = moved.xMin, to = moved.xMax;
            foreach (Rect s in siblings)
                if (Mathf.Abs(s.yMin - y) < 0.5f || Mathf.Abs(s.center.y - y) < 0.5f || Mathf.Abs(s.yMax - y) < 0.5f)
                { from = Mathf.Min(from, s.xMin); to = Mathf.Max(to, s.xMax); }
            _guides.Add(new Guide { vertical = false, pos = y, from = from, to = to });
        }

        // ------------------------------------------------------------------ equal spacing

        // If two siblings sit on the same column (overlap horizontally) and the moved rect would extend
        // the stack, snap so the moved↔nearest gap equals the nearest sibling↔sibling gap.
        private float EqualSpacingVertical(Rect moved, IReadOnlyList<Rect> siblings)
        {
            // collect siblings roughly in the same column as the moved rect, ordered by Y
            var column = new List<Rect>();
            foreach (Rect s in siblings)
                if (OverlapsX(moved, s)) column.Add(s);
            if (column.Count < 2) return 0f;
            column.Sort((a, b) => a.yMin.CompareTo(b.yMin));

            // existing pairwise gaps (screen y grows down: gap = next.yMin - cur.yMax)
            float refGap = NearestGap(column, out float gapA, out float gapB, out float center);
            if (float.IsNaN(refGap)) return 0f;

            // moved rect just below the bottom-most sibling: snap its top gap to refGap
            Rect last = column[column.Count - 1];
            Rect first = column[0];
            float belowDelta = (last.yMax + refGap) - moved.yMin;   // place moved under the stack
            float aboveDelta = (first.yMin - refGap) - moved.yMax;  // place moved above the stack
            float delta = Mathf.Abs(belowDelta) <= Mathf.Abs(aboveDelta) ? belowDelta : aboveDelta;
            if (Mathf.Abs(delta) <= Threshold)
            {
                _pips.Add(new Pip { vertical = true, center = center, a = gapA, b = gapB });
                return delta;
            }
            return 0f;
        }

        private float EqualSpacingHorizontal(Rect moved, IReadOnlyList<Rect> siblings)
        {
            var row = new List<Rect>();
            foreach (Rect s in siblings)
                if (OverlapsY(moved, s)) row.Add(s);
            if (row.Count < 2) return 0f;
            row.Sort((a, b) => a.xMin.CompareTo(b.xMin));

            float refGap = NearestGapX(row, out float gapA, out float gapB, out float center);
            if (float.IsNaN(refGap)) return 0f;

            Rect last = row[row.Count - 1];
            Rect first = row[0];
            float rightDelta = (last.xMax + refGap) - moved.xMin;
            float leftDelta = (first.xMin - refGap) - moved.xMax;
            float delta = Mathf.Abs(rightDelta) <= Mathf.Abs(leftDelta) ? rightDelta : leftDelta;
            if (Mathf.Abs(delta) <= Threshold)
            {
                _pips.Add(new Pip { vertical = false, center = center, a = gapA, b = gapB });
                return delta;
            }
            return 0f;
        }

        private static float NearestGap(List<Rect> column, out float a, out float b, out float center)
        {
            a = b = center = 0f;
            float gap = float.NaN;
            for (int i = 0; i + 1 < column.Count; i++)
            {
                float g = column[i + 1].yMin - column[i].yMax;
                if (g <= 0f) continue;
                if (float.IsNaN(gap)) { gap = g; a = column[i].yMax; b = column[i + 1].yMin; center = column[i].center.x; }
            }
            return gap;
        }

        private static float NearestGapX(List<Rect> row, out float a, out float b, out float center)
        {
            a = b = center = 0f;
            float gap = float.NaN;
            for (int i = 0; i + 1 < row.Count; i++)
            {
                float g = row[i + 1].xMin - row[i].xMax;
                if (g <= 0f) continue;
                if (float.IsNaN(gap)) { gap = g; a = row[i].xMax; b = row[i + 1].xMin; center = row[i].center.y; }
            }
            return gap;
        }

        private static bool OverlapsX(Rect a, Rect b) => a.xMin < b.xMax && b.xMin < a.xMax;
        private static bool OverlapsY(Rect a, Rect b) => a.yMin < b.yMax && b.yMin < a.yMax;

        // ------------------------------------------------------------------ drawing (OnGUI repaint)

        /// <summary> Draws the recorded guides (red rules) and equal-spacing pips. Call only on Repaint. </summary>
        public void Draw(Rect clip)
        {
            Color guideColor = NeoColors.Remove;          // Figma's magenta-red guide
            Color pipColor = NeoColors.Add;
            foreach (Guide g in _guides)
            {
                if (g.vertical)
                    EditorGUI.DrawRect(new Rect(g.pos, Mathf.Min(g.from, g.to), 1f, Mathf.Abs(g.to - g.from)), guideColor);
                else
                    EditorGUI.DrawRect(new Rect(Mathf.Min(g.from, g.to), g.pos, Mathf.Abs(g.to - g.from), 1f), guideColor);
            }
            foreach (Pip p in _pips)
            {
                if (p.vertical)
                {
                    // two short horizontal ticks bounding the equal gap
                    EditorGUI.DrawRect(new Rect(p.center - 4f, p.a, 8f, 1f), pipColor);
                    EditorGUI.DrawRect(new Rect(p.center - 4f, p.b, 8f, 1f), pipColor);
                    EditorGUI.DrawRect(new Rect(p.center, Mathf.Min(p.a, p.b), 1f, Mathf.Abs(p.b - p.a)), pipColor);
                }
                else
                {
                    EditorGUI.DrawRect(new Rect(p.a, p.center - 4f, 1f, 8f), pipColor);
                    EditorGUI.DrawRect(new Rect(p.b, p.center - 4f, 1f, 8f), pipColor);
                    EditorGUI.DrawRect(new Rect(Mathf.Min(p.a, p.b), p.center, Mathf.Abs(p.b - p.a), 1f), pipColor);
                }
            }
        }

        public void Clear() { _guides.Clear(); _pips.Clear(); }
    }
}
