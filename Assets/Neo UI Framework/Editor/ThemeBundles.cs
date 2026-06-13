using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Curated, designer-made theme bundles — the "shadcn move": one bundle defines the COMPLETE
    /// system (token palette with derived hover/pressed states, type scale, shape-style radius
    /// personality with gradients/elevation, and a motion personality) so an agent picks a look
    /// by name instead of inventing values. Applied via the spec
    /// (<c>"theme": { "bundle": "NeonArcade" }</c> — explicit tokens override after) or
    /// Tools → Neo UI → Apply Theme Bundle.
    /// </summary>
    public static class ThemeBundles
    {
        public class Bundle
        {
            public string name;
            public string description;
            /// <summary> Variant name → full token palette. The FIRST variant becomes active. </summary>
            public List<(string variant, Dictionary<string, Color> tokens)> palettes;
            /// <summary> Corner radius personality: card / panel / control radius px. </summary>
            public float cardRadius, panelRadius, controlRadius;
            /// <summary> Card surface gradient (NeonArcade-style); null = solid. </summary>
            public string cardGradientToToken;
            /// <summary> Shadow softness px (higher = glow). </summary>
            public float shadowSoftness = 18f;
            /// <summary> Motion personality for the ShowDefault/HideDefault presets. </summary>
            public float motionDuration = 0.25f;
            public string motionEase = "OutCubic";
            /// <summary> Tracking on Display/Title styles (arcade lettering vs book type). </summary>
            public float headlineSpacing = -0.5f;
        }

        // ------------------------------------------------------------------ palette helper

        /// <summary>
        /// Expands the bases of one variant into the full factory token set, deriving
        /// hover/pressed states via <see cref="ColorUtils"/> so a bundle authors ONE color per
        /// intent (exactly how Doozy generated selection-state colors).
        /// </summary>
        private static Dictionary<string, Color> Palette(int background, int surface, int surfaceElevated,
            int outline, int primary, int textOnPrimary, int textStrong, int textDefault, int textMuted,
            int success, int warning, int error, Color shadow)
        {
            Color primaryColor = Hex(primary);
            Color successColor = Hex(success);
            Color errorColor = Hex(error);
            return new Dictionary<string, Color>
            {
                [UIWidgetFactory.TokenBackground] = Hex(background),
                [UIWidgetFactory.TokenSurface] = Hex(surface),
                [UIWidgetFactory.TokenSurfaceElevated] = Hex(surfaceElevated),
                [UIWidgetFactory.TokenOutline] = Hex(outline),
                [UIWidgetFactory.TokenPrimary] = primaryColor,
                [UIWidgetFactory.TokenPrimaryHover] = ColorUtils.DeriveHover(primaryColor),
                [UIWidgetFactory.TokenPrimaryPressed] = ColorUtils.DerivePressed(primaryColor),
                [UIWidgetFactory.TokenTextOnPrimary] = Hex(textOnPrimary),
                [UIWidgetFactory.TokenTextStrong] = Hex(textStrong),
                [UIWidgetFactory.TokenTextDefault] = Hex(textDefault),
                [UIWidgetFactory.TokenTextMuted] = Hex(textMuted),
                [UIWidgetFactory.TokenSuccess] = successColor,
                [UIWidgetFactory.TokenSuccessHover] = ColorUtils.DeriveHover(successColor),
                [UIWidgetFactory.TokenSuccessPressed] = ColorUtils.DerivePressed(successColor),
                ["Warning"] = Hex(warning),
                ["Error"] = errorColor,
                [UIWidgetFactory.TokenDanger] = errorColor,
                [UIWidgetFactory.TokenDangerHover] = ColorUtils.DeriveHover(errorColor),
                [UIWidgetFactory.TokenDangerPressed] = ColorUtils.DerivePressed(errorColor),
                [UIWidgetFactory.TokenShadow] = shadow
            };
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);

        // ------------------------------------------------------------------ the bundles

        private static readonly Bundle CleanSlate = new Bundle
        {
            name = "CleanSlate",
            description = "SaaS-neutral, light + dark, radius 12, fast subtle motion",
            palettes = new List<(string, Dictionary<string, Color>)>
            {
                ("Dark", Palette(0x0B0D10, 0x16191E, 0x21252C, 0x343A44, 0x3B82F6, 0xFFFFFF,
                    0xF5F7FA, 0xC6CCD6, 0x8E96A4, 0x22C55E, 0xF59E0B, 0xEF4444, new Color(0f, 0f, 0f, 0.5f))),
                ("Light", Palette(0xF4F6F8, 0xFFFFFF, 0xEAEEF3, 0xD3DAE3, 0x2563EB, 0xFFFFFF,
                    0x111827, 0x303A49, 0x5D6675, 0x15803D, 0xB45309, 0xDC2626, new Color(0f, 0f, 0f, 0.22f)))
            },
            cardRadius = 12f, panelRadius = 12f, controlRadius = 10f,
            shadowSoftness = 16f,
            motionDuration = 0.18f, motionEase = "OutCubic",
            headlineSpacing = -1f
        };

        private static readonly Bundle NeonArcade = new Bundle
        {
            name = "NeonArcade",
            description = "Dark, saturated gradients, glow accents, radius 8, snappy springs",
            palettes = new List<(string, Dictionary<string, Color>)>
            {
                ("Dark", Palette(0x0B0716, 0x161028, 0x221A38, 0x3D2E63, 0x22D3EE, 0x06121A,
                    0xF6F3FF, 0xC9C3DC, 0x8E86AC, 0x34D399, 0xFBBF24, 0xFB7185,
                    new Color(0.13f, 0.83f, 0.93f, 0.35f))) // cyan-tinted shadow = neon glow
            },
            cardRadius = 8f, panelRadius = 8f, controlRadius = 6f,
            cardGradientToToken = UIWidgetFactory.TokenSurfaceElevated,
            shadowSoftness = 26f,
            motionDuration = 0.22f, motionEase = "OutBack",
            headlineSpacing = 2f
        };

        private static readonly Bundle SoftFantasy = new Bundle
        {
            name = "SoftFantasy",
            description = "Warm parchment / deep forest, radius 20, slower eased motion",
            palettes = new List<(string, Dictionary<string, Color>)>
            {
                ("Dark", Palette(0x101B14, 0x18261C, 0x24362A, 0x3A523F, 0xE8B04B, 0x241A05,
                    0xF3EEDC, 0xCFC9B4, 0x9D957C, 0x7BC47F, 0xE8B04B, 0xD9776B,
                    new Color(0f, 0.05f, 0f, 0.45f))),
                ("Light", Palette(0xF1E8D4, 0xFAF4E6, 0xE7DCC2, 0xCDBF9F, 0x7A5C2E, 0xFFF8E7,
                    0x2A2113, 0x44392A, 0x6E6147, 0x4C7A3F, 0x9A6A1B, 0xA84632,
                    new Color(0.24f, 0.16f, 0.04f, 0.3f)))
            },
            cardRadius = 20f, panelRadius = 18f, controlRadius = 16f,
            shadowSoftness = 22f,
            motionDuration = 0.32f, motionEase = "OutQuad",
            headlineSpacing = 0f
        };

        private static readonly Bundle[] All = { CleanSlate, NeonArcade, SoftFantasy };

        public static IEnumerable<string> Names => All.Select(b => b.name);

        public static bool TryGet(string name, out Bundle bundle)
        {
            bundle = All.FirstOrDefault(b =>
                string.Equals(b.name, name, System.StringComparison.OrdinalIgnoreCase));
            return bundle != null;
        }

        // ------------------------------------------------------------------ application

        /// <summary>
        /// Applies the complete bundle system to the project theme: tokens (all variants),
        /// shape styles, text styles and the ShowDefault/HideDefault motion presets.
        /// Explicit spec tokens still win — the generator applies them AFTER the bundle.
        /// </summary>
        public static void Apply(Bundle bundle, NeoUISettings settings, GenerateReport report)
        {
            Theme theme = settings.theme;
            if (theme == null)
            {
                report.issues.Add("No theme on the settings asset — run Create or Repair Settings first");
                return;
            }

            foreach ((string variant, Dictionary<string, Color> tokens) palette in bundle.palettes)
            {
                theme.AddVariant(palette.variant);
                foreach (KeyValuePair<string, Color> token in palette.tokens)
                    theme.SetToken(token.Key, token.Value, palette.variant);
            }
            // variants the bundle doesn't define get the first palette so nothing renders white
            (string firstVariant, Dictionary<string, Color> firstTokens) = bundle.palettes[0];
            foreach (Theme.ThemeVariant variant in theme.Variants)
            {
                if (bundle.palettes.Any(p => p.variant == variant.name)) continue;
                foreach (KeyValuePair<string, Color> token in firstTokens)
                    theme.SetToken(token.Key, token.Value, variant.name);
            }
            theme.ActiveVariantName = firstVariant;

            ApplyShapeStyles(bundle, theme);
            ApplyTextStyles(bundle, theme);
            ApplyMotion(bundle, settings, report);

            EditorUtility.SetDirty(theme);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            report.updated.Add($"Theme bundle '{bundle.name}' applied " +
                               $"({bundle.palettes.Count} variant(s), radius {bundle.cardRadius:0})");
        }

        private static void ApplyShapeStyles(Bundle bundle, Theme theme)
        {
            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StyleCard,
                radius = bundle.cardRadius,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenSurface),
                fillMode = bundle.cardGradientToToken != null ? ShapeFillMode.LinearGradient : ShapeFillMode.Solid,
                fillColorB = new ThemeColorRef(bundle.cardGradientToToken ?? UIWidgetFactory.TokenSurface),
                gradientAngle = 90f,
                elevation = 2
            });
            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StylePanel,
                radius = bundle.panelRadius,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenBackground)
            });
            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StyleControl,
                radius = bundle.controlRadius,
                borderWidth = 1f,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenSurfaceElevated),
                borderColor = new ThemeColorRef(UIWidgetFactory.TokenOutline)
            });
            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StyleControlPill,
                radiusUnit = ShapeRadiusUnit.Percent,
                radius = 100f,
                borderWidth = 1f,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenSurfaceElevated),
                borderColor = new ThemeColorRef(UIWidgetFactory.TokenOutline)
            });
            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StyleShadow,
                radius = bundle.cardRadius + 2f,
                softness = bundle.shadowSoftness,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenShadow)
            });
        }

        private static void ApplyTextStyles(Bundle bundle, Theme theme)
        {
            TMPro.TMP_FontAsset regular = FontAssetBootstrap.InterRegular;
            TMPro.TMP_FontAsset semiBold = FontAssetBootstrap.InterSemiBold;
            TMPro.TMP_FontAsset bold = FontAssetBootstrap.InterBold;
            float spacing = bundle.headlineSpacing;
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleDisplay, font = bold, size = 72f,
                characterSpacing = spacing, color = new ThemeColorRef(UIWidgetFactory.TokenTextStrong) });
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleTitle, font = semiBold, size = 44f,
                characterSpacing = spacing * 0.5f, color = new ThemeColorRef(UIWidgetFactory.TokenTextStrong) });
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleHeading, font = semiBold, size = 30f,
                color = new ThemeColorRef(UIWidgetFactory.TokenTextStrong) });
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleBody, font = regular, size = 24f,
                color = new ThemeColorRef(UIWidgetFactory.TokenTextDefault) });
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleCaption, font = regular, size = 18f,
                color = new ThemeColorRef(UIWidgetFactory.TokenTextMuted) });
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleButtonLabel, font = semiBold, size = 24f,
                color = new ThemeColorRef(UIWidgetFactory.TokenTextOnPrimary) });
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleButtonLabelSmall, font = semiBold, size = 18f,
                color = new ThemeColorRef(UIWidgetFactory.TokenTextOnPrimary) });
            theme.SetTextStyle(new TextStyle { name = UIWidgetFactory.TextStyleButtonLabelLarge, font = semiBold, size = 30f,
                color = new ThemeColorRef(UIWidgetFactory.TokenTextOnPrimary) });
        }

        /// <summary>
        /// The motion personality lands as two presets (ShowDefault/HideDefault) in the preset
        /// database, so specs reference them like any authored preset. Built inline (not via a
        /// nested generator run — Apply may already be running inside one).
        /// </summary>
        private static void ApplyMotion(Bundle bundle, NeoUISettings settings, GenerateReport report)
        {
            if (settings.animationPresets == null) return;
            ApplyMotionPreset(settings, report, new PresetSpec
            {
                name = "ShowDefault", type = "Show",
                duration = bundle.motionDuration, ease = bundle.motionEase,
                fade = new PresetChannelSpec { enabled = true, from = "0", to = "1" },
                scale = new PresetChannelSpec { enabled = true, from = "0.96", to = "1" }
            });
            ApplyMotionPreset(settings, report, new PresetSpec
            {
                name = "HideDefault", type = "Hide",
                duration = bundle.motionDuration, ease = bundle.motionEase,
                fade = new PresetChannelSpec { enabled = true, from = "1", to = "0" },
                scale = new PresetChannelSpec { enabled = true, from = "1", to = "0.96" }
            });
        }

        private static void ApplyMotionPreset(NeoUISettings settings, GenerateReport report, PresetSpec presetSpec)
        {
            string folder = UISpecGenerator.GeneratedRoot + "/Presets";
            if (!AssetDatabase.IsValidFolder(UISpecGenerator.GeneratedRoot))
                AssetDatabase.CreateFolder("Assets", UISpecGenerator.GeneratedRoot.Substring("Assets/".Length));
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(UISpecGenerator.GeneratedRoot, "Presets");

            string path = $"{folder}/{presetSpec.name}.asset";
            var preset = AssetDatabase.LoadAssetAtPath<UIAnimationPreset>(path);
            bool created = preset == null;
            if (created) preset = ScriptableObject.CreateInstance<UIAnimationPreset>();

            preset.presetName = presetSpec.name;
            preset.category = presetSpec.type;
            UISpecGenerator.ApplyPresetToAnimation(presetSpec, preset.animation, report);

            // configure BEFORE CreateAsset — the create-import can reload the object
            if (created) AssetDatabase.CreateAsset(preset, path);
            settings.animationPresets.AddOrUpdate(preset);
            EditorUtility.SetDirty(preset);
            EditorUtility.SetDirty(settings.animationPresets);
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/Neo UI/Apply Theme Bundle/Clean Slate", priority = 110)]
        public static void ApplyCleanSlateMenu() => ApplyFromMenu(CleanSlate);

        [MenuItem("Tools/Neo UI/Apply Theme Bundle/Neon Arcade", priority = 111)]
        public static void ApplyNeonArcadeMenu() => ApplyFromMenu(NeonArcade);

        [MenuItem("Tools/Neo UI/Apply Theme Bundle/Soft Fantasy", priority = 112)]
        public static void ApplyFantasyMenu() => ApplyFromMenu(SoftFantasy);

        private static void ApplyFromMenu(Bundle bundle)
        {
            var report = new GenerateReport();
            Apply(bundle, NeoUISettingsBootstrap.GetOrCreateSettings(), report);
            Debug.Log($"[Neo.UI] {report}");
        }
    }
}
