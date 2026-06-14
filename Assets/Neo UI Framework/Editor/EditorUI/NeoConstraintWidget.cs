using System;
using UnityEditor;
using UnityEngine;

namespace Neo.EditorUI
{
    /// <summary>
    /// A small, kit-local plain-data record of a Figma-style placement: the horizontal/vertical
    /// constraint ids plus their offset values. Lives IN the EditorUI kit (no <c>Neo.UI</c> types) so
    /// <see cref="NeoConstraintWidget"/> stays liftable. The host inspector translates this to/from its
    /// own spec model (the constraint ids are opaque strings here; <see cref="hPrimary"/> /
    /// <see cref="hSecondary"/> carry whatever the host's constraint convention assigns — an edge inset,
    /// a stretch [start,end] pair, a scale [startFraction,endFraction], or a center displacement).
    /// </summary>
    public struct ConstraintModel
    {
        public string h;          // horizontal constraint id (e.g. "left","right","leftRight","center","scale")
        public string v;          // vertical constraint id   (e.g. "top","bottom","topBottom","center","scale")
        public float hPrimary;    // axis-0 offset value (meaning per the host's constraint convention)
        public float hSecondary;  // axis-0 second value (used by stretch / scale)
        public float vPrimary;    // axis-1 offset value
        public float vSecondary;  // axis-1 second value

        public ConstraintModel WithH(string id) { var c = this; c.h = id; return c; }
        public ConstraintModel WithV(string id) { var c = this; c.v = id; return c; }
    }

    /// <summary>
    /// The Figma-style constraint control: per axis a row of mode buttons (Min / Max / Both / Center /
    /// Scale) plus the offset field(s) the active mode needs. "Both" is the stretch mode (two insets);
    /// "Scale" is the proportional mode (two 0..1 fractions). Pure IMGUI, cached styles + a cached 1px
    /// texture, no per-OnGUI allocation, no animation. It edits values and reports the new
    /// <see cref="ConstraintModel"/> through <see cref="Draw(Rect,ConstraintModel,Action{ConstraintModel})"/>;
    /// rect-preservation when switching modes on a live canvas is the host's concern (Pillar D).
    /// </summary>
    public static class NeoConstraintWidget
    {
        // Kit-local constraint ids (mirror the host's built-in ids so the strings round-trip). Kept as
        // consts so the control has no dependency on the host registry.
        public const string Left = "left";
        public const string Right = "right";
        public const string LeftRight = "leftRight";
        public const string Top = "top";
        public const string Bottom = "bottom";
        public const string TopBottom = "topBottom";
        public const string Center = "center";
        public const string Scale = "scale";

        private static readonly string[] HModes = { Left, Right, LeftRight, Center, Scale };
        private static readonly string[] VModes = { Top, Bottom, TopBottom, Center, Scale };

        // compact button glyphs (kept ASCII so they render in any editor font)
        private static readonly GUIContent[] HLabels =
        {
            new GUIContent("L", "Left edge"),
            new GUIContent("R", "Right edge"),
            new GUIContent("LR", "Stretch left↔right"),
            new GUIContent("C", "Center horizontally"),
            new GUIContent("S", "Scale proportionally")
        };
        private static readonly GUIContent[] VLabels =
        {
            new GUIContent("T", "Top edge"),
            new GUIContent("B", "Bottom edge"),
            new GUIContent("TB", "Stretch top↔bottom"),
            new GUIContent("C", "Center vertically"),
            new GUIContent("S", "Scale proportionally")
        };

        private const float RowHeight = 18f;
        private const float Gap = 2f;
        private const float LabelWidth = 16f;

        private static GUIStyle s_button;
        private static GUIStyle s_buttonOn;
        private static GUIStyle s_axisLabel;

        private static GUIStyle Button => s_button ?? (s_button = new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 9, padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter
        });

        private static GUIStyle ButtonOn => s_buttonOn ?? (s_buttonOn = new GUIStyle(Button)
        {
            normal = { textColor = NeoColors.Interactive },
            onNormal = { textColor = NeoColors.Interactive },
            fontStyle = FontStyle.Bold
        });

        private static GUIStyle AxisLabel => s_axisLabel ?? (s_axisLabel = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft, normal = { textColor = NeoColors.TextSubtle }
        });

        /// <summary> The total height this control occupies (two axis rows + their offset rows). </summary>
        public static float Height => (RowHeight + Gap) * 4f;

        /// <summary>
        /// Draws the control inside <paramref name="rect"/> for the given <paramref name="current"/>
        /// model and calls <paramref name="onChange"/> with the edited model whenever a value changes.
        /// </summary>
        public static void Draw(Rect rect, ConstraintModel current, Action<ConstraintModel> onChange)
        {
            float y = rect.y;
            ConstraintModel next = current;
            bool changed = false;

            // ---- horizontal ----
            changed |= DrawModeRow(new Rect(rect.x, y, rect.width, RowHeight), "H", HLabels, HModes, current.h,
                id => next = next.WithH(id));
            y += RowHeight + Gap;
            changed |= DrawOffsetRow(new Rect(rect.x, y, rect.width, RowHeight), next.h, true,
                ref next.hPrimary, ref next.hSecondary);
            y += RowHeight + Gap;

            // ---- vertical ----
            changed |= DrawModeRow(new Rect(rect.x, y, rect.width, RowHeight), "V", VLabels, VModes, current.v,
                id => next = next.WithV(id));
            y += RowHeight + Gap;
            changed |= DrawOffsetRow(new Rect(rect.x, y, rect.width, RowHeight), next.v, false,
                ref next.vPrimary, ref next.vSecondary);

            if (changed) onChange?.Invoke(next);
        }

        // a row of mode buttons; selecting one reports through onPick. Returns true on a pick.
        private static bool DrawModeRow(Rect row, string axisLabel, GUIContent[] labels, string[] ids,
            string current, Action<string> onPick)
        {
            GUI.Label(new Rect(row.x, row.y, LabelWidth, row.height), axisLabel, AxisLabel);
            float x = row.x + LabelWidth;
            float w = (row.width - LabelWidth - Gap * (ids.Length - 1)) / ids.Length;
            bool picked = false;
            for (int i = 0; i < ids.Length; i++)
            {
                var btnRect = new Rect(x, row.y, w, row.height);
                bool on = string.Equals(current, ids[i], StringComparison.Ordinal);
                if (GUI.Button(btnRect, labels[i], on ? ButtonOn : Button) && !on)
                {
                    onPick(ids[i]);
                    picked = true;
                }
                x += w + Gap;
            }
            return picked;
        }

        // the offset field(s) for the active mode: edges/center take one value, stretch/scale take two.
        private static bool DrawOffsetRow(Rect row, string mode, bool horizontal, ref float primary, ref float secondary)
        {
            bool two = IsStretch(mode) || IsScale(mode);
            string a = PrimaryLabel(mode, horizontal);
            string b = SecondaryLabel(mode, horizontal);

            float fieldX = row.x + LabelWidth;
            float fieldW = row.width - LabelWidth;
            bool changed = false;

            if (!two)
            {
                EditorGUI.BeginChangeCheck();
                float v = LabeledFloat(new Rect(fieldX, row.y, fieldW, row.height), a, primary);
                if (EditorGUI.EndChangeCheck()) { primary = v; changed = true; }
                return changed;
            }

            float half = (fieldW - Gap) * 0.5f;
            EditorGUI.BeginChangeCheck();
            float p = LabeledFloat(new Rect(fieldX, row.y, half, row.height), a, primary);
            float s = LabeledFloat(new Rect(fieldX + half + Gap, row.y, half, row.height), b, secondary);
            if (EditorGUI.EndChangeCheck()) { primary = p; secondary = s; changed = true; }
            return changed;
        }

        private static float LabeledFloat(Rect rect, string label, float value)
        {
            const float lblW = 26f;
            GUI.Label(new Rect(rect.x, rect.y, lblW, rect.height), label, AxisLabel);
            return EditorGUI.DelayedFloatField(new Rect(rect.x + lblW, rect.y, rect.width - lblW, rect.height), value);
        }

        private static bool IsStretch(string mode) =>
            string.Equals(mode, LeftRight, StringComparison.Ordinal) ||
            string.Equals(mode, TopBottom, StringComparison.Ordinal);

        private static bool IsScale(string mode) => string.Equals(mode, Scale, StringComparison.Ordinal);

        private static string PrimaryLabel(string mode, bool horizontal)
        {
            if (IsScale(mode)) return "from";
            if (IsStretch(mode)) return horizontal ? "left" : "top";
            if (string.Equals(mode, Center, StringComparison.Ordinal)) return "offset";
            return mode; // edge name (left/right/top/bottom)
        }

        private static string SecondaryLabel(string mode, bool horizontal)
        {
            if (IsScale(mode)) return "to";
            return horizontal ? "right" : "bottom";
        }
    }
}
