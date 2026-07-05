using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Central UI color scheme: named color tokens (Primary, Background, …) with multiple named
    /// variants (Dark / Light / seasonal). One variant is active; switching it (runtime or editor)
    /// re-applies every bound <see cref="ThemeColorTarget"/> instantly.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Theme", fileName = "Theme")]
    public class Theme : ScriptableObject
    {
        [Serializable]
        public class TokenColor
        {
            public string token;
            public Color color = Color.white;

            public TokenColor() { }

            public TokenColor(string tokenName, Color tokenColor)
            {
                token = tokenName;
                color = tokenColor;
            }
        }

        [Serializable]
        public class ThemeVariant
        {
            public string name = "Default";
            public List<TokenColor> colors = new List<TokenColor>();

            public bool TryGetColor(string token, out Color color)
            {
                foreach (TokenColor entry in colors)
                {
                    if (!string.Equals(entry.token, token, StringComparison.Ordinal)) continue;
                    color = entry.color;
                    return true;
                }
                color = Color.white;
                return false;
            }
        }

        [SerializeField] private List<ThemeVariant> variants = new List<ThemeVariant> { new ThemeVariant() };
        [SerializeField] private string activeVariantName = "Default";

        [Tooltip("Named surface styles (radius/border/softness) applied to NeoShapes by ThemeShapeStyleTarget")]
        [SerializeField] private List<ShapeStyle> shapeStyles = new List<ShapeStyle>();

        [Tooltip("Named typographic styles (font/size/spacing) applied to TMP texts by ThemeTextStyleTarget")]
        [SerializeField] private List<TextStyle> textStyles = new List<TextStyle>();

        public IReadOnlyList<ThemeVariant> Variants => variants;

        public IReadOnlyList<ShapeStyle> ShapeStyles => shapeStyles;

        public IReadOnlyList<TextStyle> TextStyles => textStyles;

        public string ActiveVariantName
        {
            get => activeVariantName;
            set
            {
                if (activeVariantName == value) return;
                activeVariantName = value;
                RaiseChanged();
            }
        }

        public ThemeVariant activeVariant =>
            variants.FirstOrDefault(v => v.name == activeVariantName) ?? variants.FirstOrDefault();

        /// <summary> All token names known to this theme (union across variants). </summary>
        public IEnumerable<string> GetTokenNames() =>
            variants.SelectMany(v => v.colors.Select(c => c.token)).Where(t => !string.IsNullOrEmpty(t)).Distinct();

        public bool HasToken(string token) => GetTokenNames().Contains(token);

        public bool TryGetColor(string token, out Color color)
        {
            ThemeVariant variant = activeVariant;
            if (variant != null && variant.TryGetColor(token, out color)) return true;
            color = Color.white;
            return false;
        }

        /// <summary> Resolves a token color in a SPECIFIC named variant (not the active one). </summary>
        public bool TryGetColor(string token, out Color color, string variantName)
        {
            ThemeVariant variant = GetVariant(variantName);
            if (variant != null && variant.TryGetColor(token, out color)) return true;
            color = Color.white;
            return false;
        }

        /// <summary> Resolves a token color in the active variant; white (with a warning) when missing. </summary>
        public Color GetColor(string token)
        {
            if (TryGetColor(token, out Color color)) return color;
            Debug.LogWarning($"[Neo.UI] Theme '{name}' has no token '{token}' in variant '{activeVariantName}'", this);
            return Color.white;
        }

        public ThemeVariant GetVariant(string variantName) => variants.FirstOrDefault(v => v.name == variantName);

        public ThemeVariant AddVariant(string variantName)
        {
            ThemeVariant existing = GetVariant(variantName);
            if (existing != null) return existing;
            var variant = new ThemeVariant { name = variantName };
            // seed with the tokens of the first variant so every variant covers the full set
            ThemeVariant template = variants.FirstOrDefault();
            if (template != null)
                variant.colors.AddRange(template.colors.Select(c => new TokenColor(c.token, c.color)));
            variants.Add(variant);
            RaiseChanged();
            return variant;
        }

        /// <summary> Sets a token color in the given variant (default: all variants missing it get it too). </summary>
        public void SetToken(string token, Color color, string variantName = null)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            token = token.Trim();
            foreach (ThemeVariant variant in variants)
            {
                bool targeted = variantName == null || variant.name == variantName;
                TokenColor entry = variant.colors.FirstOrDefault(c => c.token == token);
                if (entry == null)
                {
                    variant.colors.Add(new TokenColor(token, color));
                }
                else if (targeted)
                {
                    entry.color = color;
                }
            }
            RaiseChanged();
        }

        public bool RemoveToken(string token)
        {
            bool removed = false;
            foreach (ThemeVariant variant in variants)
                removed |= variant.colors.RemoveAll(c => c.token == token) > 0;
            if (removed) RaiseChanged();
            return removed;
        }

        // ------------------------------------------------------------------ shape styles

        /// <summary> All shape style names known to this theme. </summary>
        public IEnumerable<string> GetShapeStyleNames() =>
            shapeStyles.Select(s => s.name).Where(n => !string.IsNullOrEmpty(n));

        public ShapeStyle GetShapeStyle(string styleName) =>
            shapeStyles.FirstOrDefault(s => string.Equals(s.name, styleName, StringComparison.Ordinal));

        public bool TryGetShapeStyle(string styleName, out ShapeStyle style)
        {
            style = GetShapeStyle(styleName);
            return style != null;
        }

        /// <summary> Adds or replaces a shape style by name. </summary>
        public ShapeStyle SetShapeStyle(ShapeStyle style)
        {
            if (style == null || string.IsNullOrWhiteSpace(style.name)) return null;
            style.name = style.name.Trim();
            int index = shapeStyles.FindIndex(s => s.name == style.name);
            if (index >= 0) shapeStyles[index] = style;
            else shapeStyles.Add(style);
            RaiseChanged();
            return style;
        }

        public bool RemoveShapeStyle(string styleName)
        {
            bool removed = shapeStyles.RemoveAll(s => s.name == styleName) > 0;
            if (removed) RaiseChanged();
            return removed;
        }

        // ------------------------------------------------------------------ text styles

        /// <summary> All text style names known to this theme. </summary>
        public IEnumerable<string> GetTextStyleNames() =>
            textStyles.Select(s => s.name).Where(n => !string.IsNullOrEmpty(n));

        public TextStyle GetTextStyle(string styleName) =>
            textStyles.FirstOrDefault(s => string.Equals(s.name, styleName, StringComparison.Ordinal));

        public bool TryGetTextStyle(string styleName, out TextStyle style)
        {
            style = GetTextStyle(styleName);
            return style != null;
        }

        /// <summary> Adds or replaces a text style by name. </summary>
        public TextStyle SetTextStyle(TextStyle style)
        {
            if (style == null || string.IsNullOrWhiteSpace(style.name)) return null;
            style.name = style.name.Trim();
            int index = textStyles.FindIndex(s => s.name == style.name);
            if (index >= 0) textStyles[index] = style;
            else textStyles.Add(style);
            RaiseChanged();
            return style;
        }

        public bool RemoveTextStyle(string styleName)
        {
            bool removed = textStyles.RemoveAll(s => s.name == styleName) > 0;
            if (removed) RaiseChanged();
            return removed;
        }

        /// <summary> Notifies all bound targets that this theme changed (live edit-mode recolor). </summary>
        public void RaiseChanged() => ThemeService.NotifyThemeChanged(this);

        private void OnValidate() => RaiseChanged();
    }
}
