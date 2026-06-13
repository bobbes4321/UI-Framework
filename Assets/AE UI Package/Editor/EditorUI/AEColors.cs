using UnityEditor;
using UnityEngine;

namespace AlterEyes.EditorUI
{
    /// <summary>
    /// Semantic color palette for editor tooling, skin-aware (pro/light). Colors are grouped the
    /// Doozy way — one accent per component family — but resolved as plain constants: no
    /// ScriptableObject palettes, no generated lookups, no load cost.
    /// </summary>
    public static class AEColors
    {
        private static bool Dark => EditorGUIUtility.isProSkin;

        // ------------------------------------------------------------------ family accents

        /// <summary> Blue — interactive components (buttons, toggles, sliders). </summary>
        public static Color Interactive => Dark ? FromHex(0x4A9EFF) : FromHex(0x1B6FD4);

        /// <summary> Cyan — containers (views, popups, tooltips). </summary>
        public static Color Containers => Dark ? FromHex(0x41C9E2) : FromHex(0x0E8FA8);

        /// <summary> Orange — animation and animators. </summary>
        public static Color Animation => Dark ? FromHex(0xFFA94D) : FromHex(0xD9730D);

        /// <summary> Purple — flow graphs and controllers. </summary>
        public static Color Flow => Dark ? FromHex(0xB197FC) : FromHex(0x7048E8);

        /// <summary> Pink — theming. </summary>
        public static Color Theming => Dark ? FromHex(0xF783AC) : FromHex(0xD6336C);

        /// <summary> Teal — signals and streams. </summary>
        public static Color Signals => Dark ? FromHex(0x63E6BE) : FromHex(0x099268);

        /// <summary> Yellow — data assets (databases, settings). </summary>
        public static Color Data => Dark ? FromHex(0xFFD43B) : FromHex(0xB08D0B);

        /// <summary> Lime — rendering primitives (shapes, gradients, effects). </summary>
        public static Color Rendering => Dark ? FromHex(0xA9E34B) : FromHex(0x66A80F);

        // ------------------------------------------------------------------ intent colors

        public static Color Add => Dark ? FromHex(0x5FD068) : FromHex(0x2E933C);
        public static Color Remove => Dark ? FromHex(0xFF6B6B) : FromHex(0xC73E3E);
        public static Color Warning => Dark ? FromHex(0xFFC078) : FromHex(0xB35C00);

        // ------------------------------------------------------------------ chrome

        public static Color TextTitle => Dark ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.1f, 0.1f, 0.1f);
        public static Color TextSubtle => Dark ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.35f, 0.35f, 0.35f);
        public static Color TextDim => Dark ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.45f, 0.45f, 0.45f);

        public static Color HeaderBackground => Dark ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.82f, 0.82f, 0.82f);
        public static Color SectionBackground => Dark ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.03f);
        public static Color Separator => Dark ? new Color(0f, 0f, 0f, 0.4f) : new Color(0f, 0f, 0f, 0.15f);
        public static Color RowHover => Dark ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.06f);
        public static Color RowSelected => Dark ? new Color(0.24f, 0.42f, 0.69f, 0.6f) : new Color(0.24f, 0.49f, 0.91f, 0.35f);

        // ------------------------------------------------------------------ helpers

        public static Color WithAlpha(this Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color FromHex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);
    }
}
