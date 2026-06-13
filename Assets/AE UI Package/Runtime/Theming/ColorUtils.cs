using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Color space conversion and adjustment helpers (HSL/HSV/RGB, lighten/darken). </summary>
    public static class ColorUtils
    {
        /// <summary> RGB → HSL. All components in [0,1]. </summary>
        public static void RgbToHsl(Color color, out float h, out float s, out float l)
        {
            float r = color.r, g = color.g, b = color.b;
            float max = Mathf.Max(r, Mathf.Max(g, b));
            float min = Mathf.Min(r, Mathf.Min(g, b));
            l = (max + min) * 0.5f;

            if (Mathf.Approximately(max, min))
            {
                h = 0f;
                s = 0f;
                return;
            }

            float delta = max - min;
            s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

            if (Mathf.Approximately(max, r)) h = (g - b) / delta + (g < b ? 6f : 0f);
            else if (Mathf.Approximately(max, g)) h = (b - r) / delta + 2f;
            else h = (r - g) / delta + 4f;
            h /= 6f;
        }

        /// <summary> HSL → RGB. All components in [0,1]. </summary>
        public static Color HslToRgb(float h, float s, float l, float alpha = 1f)
        {
            h = Mathf.Repeat(h, 1f);
            s = Mathf.Clamp01(s);
            l = Mathf.Clamp01(l);

            if (Mathf.Approximately(s, 0f)) return new Color(l, l, l, alpha);

            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            return new Color(
                HueToRgb(p, q, h + 1f / 3f),
                HueToRgb(p, q, h),
                HueToRgb(p, q, h - 1f / 3f),
                alpha);
        }

        private static float HueToRgb(float p, float q, float t)
        {
            t = Mathf.Repeat(t, 1f);
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        /// <summary> RGB → HSV (wraps Unity's implementation for API symmetry). </summary>
        public static void RgbToHsv(Color color, out float h, out float s, out float v) =>
            Color.RGBToHSV(color, out h, out s, out v);

        public static Color HsvToRgb(float h, float s, float v, float alpha = 1f)
        {
            Color c = Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), Mathf.Clamp01(v));
            c.a = alpha;
            return c;
        }

        /// <summary> Increases lightness by the given amount (HSL space, 0..1). </summary>
        public static Color Lighten(Color color, float amount)
        {
            RgbToHsl(color, out float h, out float s, out float l);
            return HslToRgb(h, s, Mathf.Clamp01(l + amount), color.a);
        }

        /// <summary> Decreases lightness by the given amount (HSL space, 0..1). </summary>
        public static Color Darken(Color color, float amount) => Lighten(color, -amount);

        /// <summary>
        /// Hover state from a base intent color (HSL lift, mirroring how Doozy derived selection
        /// state colors) — theme bundles define ONE base per intent and derive the rest.
        /// </summary>
        public static Color DeriveHover(Color baseColor) => Lighten(baseColor, 0.07f);

        /// <summary> Pressed state from a base intent color (HSL drop). </summary>
        public static Color DerivePressed(Color baseColor) => Darken(baseColor, 0.09f);

        /// <summary> WCAG relative luminance (sRGB linearized). </summary>
        public static float RelativeLuminance(Color color)
        {
            float Linear(float channel) =>
                channel <= 0.03928f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Linear(color.r) + 0.7152f * Linear(color.g) + 0.0722f * Linear(color.b);
        }

        /// <summary> WCAG contrast ratio between two colors, 1..21 (4.5+ = AA body text). </summary>
        public static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float lighter = Mathf.Max(la, lb);
            float darker = Mathf.Min(la, lb);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        /// <summary> Parses "#RRGGBB" / "#RRGGBBAA" (leading '#' optional). </summary>
        public static bool TryParseHex(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out color);
        }

        public static string ToHex(Color color) =>
            color.a < 1f ? $"#{ColorUtility.ToHtmlStringRGBA(color)}" : $"#{ColorUtility.ToHtmlStringRGB(color)}";
    }
}
