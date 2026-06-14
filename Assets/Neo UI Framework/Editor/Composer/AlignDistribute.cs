using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Pure align/distribute geometry for the multi-select toolbar. Takes a set of device-space rects
    /// (y up, bottom-origin — the same space <see cref="ConstraintWriteback"/> works in) and returns the
    /// repositioned rects; the canvas then writes each back through the constraint model in ONE
    /// <see cref="SpecDocument.ApplyEdit"/> (a single undo). Kept separate from <see cref="ComposerCanvas"/>
    /// so the math is unit-testable without a live preview.
    /// </summary>
    public static class AlignDistribute
    {
        public enum Op { Left, CenterX, Right, Top, CenterY, Bottom, DistributeH, DistributeV }

        /// <summary>
        /// Applies <paramref name="op"/> to <paramref name="rects"/>, returning a new rect per key.
        /// Align ops snap an edge/center to the selection's bounding box; distribute ops equalize the
        /// gaps between three+ elements (the two extremes stay put, Figma's "distribute spacing").
        /// Fewer than two rects (or fewer than three for distribute) is a no-op pass-through.
        /// </summary>
        public static Dictionary<TKey, Rect> Apply<TKey>(Op op, IReadOnlyDictionary<TKey, Rect> rects)
        {
            var result = new Dictionary<TKey, Rect>(rects.Count);
            if (rects.Count < 2) { foreach (var kv in rects) result[kv.Key] = kv.Value; return result; }

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Rect r in rects.Values)
            {
                minX = Mathf.Min(minX, r.xMin); maxX = Mathf.Max(maxX, r.xMax);
                minY = Mathf.Min(minY, r.yMin); maxY = Mathf.Max(maxY, r.yMax);
            }

            switch (op)
            {
                case Op.Left:
                    foreach (var kv in rects) result[kv.Key] = MoveTo(kv.Value, minX, kv.Value.yMin); break;
                case Op.Right:
                    foreach (var kv in rects) result[kv.Key] = MoveTo(kv.Value, maxX - kv.Value.width, kv.Value.yMin); break;
                case Op.CenterX:
                    float cx = (minX + maxX) * 0.5f;
                    foreach (var kv in rects) result[kv.Key] = MoveTo(kv.Value, cx - kv.Value.width * 0.5f, kv.Value.yMin); break;
                case Op.Bottom:
                    foreach (var kv in rects) result[kv.Key] = MoveTo(kv.Value, kv.Value.xMin, minY); break;
                case Op.Top:
                    foreach (var kv in rects) result[kv.Key] = MoveTo(kv.Value, kv.Value.xMin, maxY - kv.Value.height); break;
                case Op.CenterY:
                    float cy = (minY + maxY) * 0.5f;
                    foreach (var kv in rects) result[kv.Key] = MoveTo(kv.Value, kv.Value.xMin, cy - kv.Value.height * 0.5f); break;
                case Op.DistributeH:
                    Distribute(rects, result, horizontal: true, minX, maxX, minY, maxY); break;
                case Op.DistributeV:
                    Distribute(rects, result, horizontal: false, minX, maxX, minY, maxY); break;
            }
            return result;
        }

        private static Rect MoveTo(Rect r, float x, float y) => new Rect(x, y, r.width, r.height);

        private static void Distribute<TKey>(IReadOnlyDictionary<TKey, Rect> rects,
            Dictionary<TKey, Rect> result, bool horizontal, float minX, float maxX, float minY, float maxY)
        {
            var ordered = new List<KeyValuePair<TKey, Rect>>(rects);
            ordered.Sort((a, b) => horizontal
                ? a.Value.center.x.CompareTo(b.Value.center.x)
                : a.Value.center.y.CompareTo(b.Value.center.y));

            if (ordered.Count < 3)
            {
                foreach (var kv in rects) result[kv.Key] = kv.Value; // nothing to distribute
                return;
            }

            float span = horizontal ? (maxX - minX) : (maxY - minY);
            float sumExtent = 0f;
            foreach (var kv in ordered) sumExtent += horizontal ? kv.Value.width : kv.Value.height;
            float gap = (span - sumExtent) / (ordered.Count - 1);

            float cursor = horizontal ? minX : minY;
            foreach (var kv in ordered)
            {
                Rect r = kv.Value;
                result[kv.Key] = horizontal ? MoveTo(r, cursor, r.yMin) : MoveTo(r, r.xMin, cursor);
                cursor += (horizontal ? r.width : r.height) + gap;
            }
        }

        public static string Label(Op op)
        {
            switch (op)
            {
                case Op.Left: return "Align Left";
                case Op.Right: return "Align Right";
                case Op.CenterX: return "Align Center X";
                case Op.Top: return "Align Top";
                case Op.Bottom: return "Align Bottom";
                case Op.CenterY: return "Align Center Y";
                case Op.DistributeH: return "Distribute Horizontally";
                case Op.DistributeV: return "Distribute Vertically";
                default: return "Align";
            }
        }
    }
}
