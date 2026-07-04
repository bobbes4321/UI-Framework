using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Pattern R registry of theme <see cref="ThemeBundles.Bundle"/>s — the extensibility seam for the
    /// curated bundle set. Seeded with the three package built-ins (CleanSlate / NeonArcade /
    /// SoftFantasy); a consuming project ships its own coherent look by calling
    /// <see cref="Register"/> once from an <c>[InitializeOnLoad]</c> type, OR by dropping a
    /// <see cref="ThemeBundleDefinition"/> asset (discovered by the shared
    /// <see cref="NeoAssetRegistry{TAsset,TEntry}"/> base) — and it appears everywhere a bundle can be
    /// picked (the Apply-Theme-Bundle menu, the inspector dropdown) and resolves through the spec
    /// <c>"theme":{"bundle":"…"}</c> path. Editor-only (the editor is a single domain), so the static
    /// seed is sufficient — no domain-reload survival needed.
    /// </summary>
    public static class ThemeBundleRegistry
    {
        // Bundle names are user-facing display strings picked from a menu/dropdown or authored in a spec's
        // "theme":{"bundle":"…"} — keep matching case-insensitive (unlike most Neo.UI registries, which are
        // ordinal) so "neonarcade" in a hand-typed spec still resolves to "NeonArcade".
        private static readonly NeoAssetRegistry<ThemeBundleDefinition, ThemeBundles.Bundle> _registry =
            new NeoAssetRegistry<ThemeBundleDefinition, ThemeBundles.Bundle>(
                key: b => b.name,
                project: def => def.ToBundle(),
                comparison: StringComparison.OrdinalIgnoreCase,
                builtins: () => new[] { ThemeBundles.CleanSlate, ThemeBundles.NeonArcade, ThemeBundles.SoftFantasy },
                registryName: "ThemeBundleRegistry");

        /// <summary> All registered bundles, built-ins first, in registration order. </summary>
        public static IReadOnlyList<ThemeBundles.Bundle> All => _registry.All;

        /// <summary> The names of every registered bundle — what the menu and inspector dropdown list. </summary>
        public static IEnumerable<string> Names => _registry.All.Select(b => b.name);

        /// <summary> Case-insensitive lookup by name. Returns false (and a null bundle) when nothing matches. </summary>
        public static bool TryGet(string name, out ThemeBundles.Bundle bundle) => _registry.TryGet(name, out bundle);

        /// <summary> Marks discovered <see cref="ThemeBundleDefinition"/> assets stale (asset post-processor hook). </summary>
        public static void InvalidateDiscovery() => _registry.InvalidateDiscovery();

        /// <summary>
        /// Registers a bundle. If one with the same name already exists (case-insensitive) it is
        /// replaced in place (so a project can override a built-in); otherwise the bundle is appended.
        /// A null/unnamed bundle is warned-and-ignored.
        /// </summary>
        public static void Register(ThemeBundles.Bundle bundle) => _registry.Register(bundle);

        /// <summary>
        /// Test-only: removes a registered bundle by name (case-insensitive). Not part of the public
        /// extensibility seam — exists so tests that register a probe can leave the static registry
        /// clean for sibling suites in the same domain. Returns true if a bundle was removed.
        /// </summary>
        internal static bool Remove(string name) => _registry.Remove(name);

        /// <summary>
        /// Test-only: restores the registry to exactly the three code-seeded built-ins and forces a
        /// fresh discovery on next access.
        /// </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }

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
            int success, int warning, int error, Color shadow) =>
            BuildPalette(Hex(background), Hex(surface), Hex(surfaceElevated), Hex(outline), Hex(primary),
                Hex(textOnPrimary), Hex(textStrong), Hex(textDefault), Hex(textMuted), Hex(success),
                Hex(warning), Hex(error), shadow);

        /// <summary>
        /// Expands one variant's base colors into the full factory token set, deriving hover/pressed
        /// states via <see cref="ColorUtils"/> so a caller authors ONE color per intent. Public so the
        /// New Project Setup wizard's "custom theme" builder produces a palette identical in structure to
        /// the built-in bundles (and can save it as a <see cref="ThemeBundleDefinition"/>).
        /// </summary>
        public static Dictionary<string, Color> BuildPalette(Color background, Color surface,
            Color surfaceElevated, Color outline, Color primary, Color textOnPrimary, Color textStrong,
            Color textDefault, Color textMuted, Color success, Color warning, Color error, Color shadow)
        {
            return new Dictionary<string, Color>
            {
                [UIWidgetFactory.TokenBackground] = background,
                [UIWidgetFactory.TokenSurface] = surface,
                [UIWidgetFactory.TokenSurfaceElevated] = surfaceElevated,
                [UIWidgetFactory.TokenOutline] = outline,
                [UIWidgetFactory.TokenPrimary] = primary,
                [UIWidgetFactory.TokenPrimaryHover] = ColorUtils.DeriveHover(primary),
                [UIWidgetFactory.TokenPrimaryPressed] = ColorUtils.DerivePressed(primary),
                [UIWidgetFactory.TokenTextOnPrimary] = textOnPrimary,
                [UIWidgetFactory.TokenTextStrong] = textStrong,
                [UIWidgetFactory.TokenTextDefault] = textDefault,
                [UIWidgetFactory.TokenTextMuted] = textMuted,
                [UIWidgetFactory.TokenSuccess] = success,
                [UIWidgetFactory.TokenSuccessHover] = ColorUtils.DeriveHover(success),
                [UIWidgetFactory.TokenSuccessPressed] = ColorUtils.DerivePressed(success),
                ["Warning"] = warning,
                ["Error"] = error,
                [UIWidgetFactory.TokenDanger] = error,
                [UIWidgetFactory.TokenDangerHover] = ColorUtils.DeriveHover(error),
                [UIWidgetFactory.TokenDangerPressed] = ColorUtils.DerivePressed(error),
                [UIWidgetFactory.TokenShadow] = shadow
            };
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);

        // ------------------------------------------------------------------ the bundles

        internal static readonly Bundle CleanSlate = new Bundle
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

        internal static readonly Bundle NeonArcade = new Bundle
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

        internal static readonly Bundle SoftFantasy = new Bundle
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

        // Bundles live in ThemeBundleRegistry (the extensibility seam). These stay as thin forwarders
        // so the spec "theme":{"bundle":"…"} path and every existing caller keep working unchanged.
        public static IEnumerable<string> Names => ThemeBundleRegistry.Names;

        public static bool TryGet(string name, out Bundle bundle) => ThemeBundleRegistry.TryGet(name, out bundle);

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
            ApplyPresets(bundle, report);

            EditorUtility.SetDirty(theme);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            report.updated.Add($"Theme bundle '{bundle.name}' applied " +
                               $"({bundle.palettes.Count} variant(s), radius {bundle.cardRadius:0})");
        }

        // ------------------------------------------------------------------ widget presets

        /// <summary>
        /// Seeds the built-in widget-preset library (so applying a bundle on a fresh project also
        /// installs the component layer) and overlays the bundle's PERSONALITY onto the relevant presets.
        /// A <see cref="NeoWidgetPreset"/> references tokens/styles BY NAME, and the token/shape/motion
        /// passes above already rewrote those — so the SAME "Primary Button" preset recolors under each
        /// bundle for free. The only personality the names don't carry is corner-radius and default
        /// motion, which is what we map here from the bundle's existing fields. Idempotent: each preset is
        /// loaded by name, its fields set, and dirtied; missing presets are skipped (never a throw), and
        /// re-applying the same bundle is a no-op writeback.
        /// </summary>
        private static void ApplyPresets(Bundle bundle, GenerateReport report)
        {
            // idempotent — installs the component library on a fresh project, repairs it otherwise
            GenerateReport presetReport = PresetLibraryBootstrap.CreateOrRepair();
            report.created.AddRange(presetReport.created);

            // The bundle's Show preset is the surface/view default motion; reuse the name ApplyMotion registers.
            const string showMotion = "ShowDefault";

            // Controls take the control radius; the FAB keeps its intentionally-pill radius (set in the library).
            SetRadius("Primary Button", bundle.controlRadius);
            SetRadius("Secondary Button", bundle.controlRadius);
            SetRadius("Ghost Button", bundle.controlRadius);
            SetRadius("Danger Button", bundle.controlRadius);
            SetRadius("Primary Button Large", bundle.controlRadius);
            SetRadius("Primary Button Small", bundle.controlRadius);
            SetRadius("Icon Button", bundle.controlRadius);
            SetRadius("Link Button", bundle.controlRadius);
            SetRadius("Default Dropdown", bundle.controlRadius);
            SetRadius("Text Input", bundle.controlRadius);
            SetRadius("Default Tab", bundle.controlRadius);
            SetRadius("Filled Tab", bundle.controlRadius);

            // Surfaces take their own radius personality + the bundle's default show motion.
            SetRadius("Card", bundle.cardRadius);
            SetRadius("Panel", bundle.panelRadius);
            SetMotion("Card", showMotion);
            SetMotion("Panel", showMotion);

            AssetDatabase.SaveAssets();
            NeoWidgetPresets.InvalidateDiscovery();
        }

        /// <summary> Sets a preset's corner radius if it exists; skips silently otherwise (graceful). </summary>
        private static void SetRadius(string presetName, float radius)
        {
            if (!NeoWidgetPresets.TryGet(presetName, out NeoWidgetPreset preset)) return;
            preset.radius = radius;
            EditorUtility.SetDirty(preset);
        }

        /// <summary> Sets a preset's default motion (animation-preset name) if it exists; skips silently otherwise. </summary>
        private static void SetMotion(string presetName, string motion)
        {
            if (!NeoWidgetPresets.TryGet(presetName, out NeoWidgetPreset preset)) return;
            preset.motion = motion;
            EditorUtility.SetDirty(preset);
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

        // One picker over the registry instead of one [MenuItem] per built-in — a project's registered
        // bundle shows up here automatically (no per-bundle fork point). [MenuItem] is attribute-driven
        // and can't enumerate at compile time, so we open a NeoSearchablePopup (EditorUI kit, non-modal)
        // over ThemeBundleRegistry.Names.
        [MenuItem("Tools/Neo UI/Setup/Apply Theme Bundle…", priority = 110)]
        public static void ApplyThemeBundleMenu()
        {
            var names = ThemeBundleRegistry.Names.ToList();
            if (names.Count == 0)
            {
                Debug.LogWarning("[Neo.UI] No theme bundles registered");
                return;
            }
            // anchor the popup at the mouse so it appears under the menu click
            Vector2 mouse = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
            Rect activator = new Rect(mouse.x, mouse.y, 220f, 0f);
            NeoSearchablePopup.Show(activator, null, names, name =>
            {
                if (ThemeBundleRegistry.TryGet(name, out Bundle bundle)) ApplyFromMenu(bundle);
                else Debug.LogWarning($"[Neo.UI] No theme bundle named '{name}'");
            });
        }

        private static void ApplyFromMenu(Bundle bundle)
        {
            var report = new GenerateReport();
            Apply(bundle, NeoUISettingsBootstrap.GetOrCreateSettings(), report);
            Debug.Log($"[Neo.UI] {report}");
        }
    }
}
