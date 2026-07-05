using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Editor
{
    /// <summary>
    /// "Tools → Neo UI → Create or Repair Starter Kit": expands the default theme into a
    /// full design-system palette (Dark + Light variants, surface shape styles) and generates the
    /// reusable widget prefab library under <c>Assets/Neo UI Framework/Starter</c> — every prefab
    /// built from NeoShape (zero sprites) through <see cref="UIWidgetFactory"/>. Idempotent like
    /// the spec generator: prefabs carrying <see cref="GeneratedMarker"/> are rebuilt in place,
    /// hand-made files at those paths are left untouched and reported.
    /// </summary>
    public static class StarterKitBootstrap
    {
        public const string StarterFolder = "Assets/Neo UI Framework/Starter";
        public const string DarkVariant = "Dark";
        public const string LightVariant = "Light";

        [MenuItem("Tools/Neo UI/Setup/Create or Repair Starter Kit", priority = 101)]
        public static void CreateOrRepairMenu()
        {
            GenerateReport report = CreateOrRepair();
            Debug.Log($"[Neo.UI] Starter kit:\n{report}");
        }

        public static GenerateReport CreateOrRepair()
        {
            var report = new GenerateReport();
            NeoUISettings settings = NeoUISettingsBootstrap.EnsureSettings();

            ExpandTheme(settings.theme, report);
            EnsureButtonVariants(settings, report);

            EnsureFolder(StarterFolder);
            SavePrefab(report, settings, "Button",
                () => UIWidgetFactory.CreateButton(null, "Starter", "Button", "Button"));
            SavePrefab(report, settings, "Toggle",
                () => UIWidgetFactory.CreateToggle(null, "Starter", "Toggle", "Toggle"));
            SavePrefab(report, settings, "Switch",
                () => UIWidgetFactory.CreateSwitch(null, "Starter", "Switch"));
            SavePrefab(report, settings, "Slider",
                () => UIWidgetFactory.CreateSlider(null, "Starter", "Slider"));
            SavePrefab(report, settings, "ProgressBar",
                () => UIWidgetFactory.CreateProgressBar(null));
            SavePrefab(report, settings, "Card",
                () => UIWidgetFactory.CreateCard(null, new Vector2(360f, 240f)));
            SavePrefab(report, settings, "TabBar",
                () => UIWidgetFactory.CreateTabBar(null, "Starter", new List<(string, string)>
                    { ("Tab1", "First"), ("Tab2", "Second"), ("Tab3", "Third") }));
            SavePrefab(report, settings, "ListView",
                () => UIWidgetFactory.CreateListView(null));
            SavePrefab(report, settings, "Tooltip",
                () => UIWidgetFactory.CreateTooltip());

            GameObject popupPrefab = SavePrefab(report, settings, "Popup",
                () => UIWidgetFactory.CreatePopup("Starter", "Title", "Message body."));
            if (popupPrefab != null && settings.popupDatabase != null)
            {
                settings.popupDatabase.AddOrUpdate("Starter", popupPrefab);
                EditorUtility.SetDirty(settings.popupDatabase);
            }

            SavePrefab(report, settings, "Showcase", () => BuildShowcase(settings));
            settings.viewIds.Add("Starter", "Showcase");
            EditorUtility.SetDirty(settings.viewIds);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return report;
        }

        // ------------------------------------------------------------------ theme

        /// <summary> The full starter palette: token → (dark, light). </summary>
        private static readonly (string token, Color dark, Color light)[] Palette =
        {
            (UIWidgetFactory.TokenBackground, Hex(0x0F1115), Hex(0xF2F4F7)),
            (UIWidgetFactory.TokenSurface, Hex(0x1A1D24), Hex(0xFFFFFF)),
            (UIWidgetFactory.TokenSurfaceElevated, Hex(0x252A34), Hex(0xE9EDF2)),
            (UIWidgetFactory.TokenOutline, Hex(0x39404E), Hex(0xD4DAE3)),
            // dark primary darkened from 4A9EFF so white button labels clear WCAG 3:1 (large text)
            (UIWidgetFactory.TokenPrimary, Hex(0x3B82F6), Hex(0x1B6FD4)),
            (UIWidgetFactory.TokenPrimaryHover, Hex(0x639AF8), Hex(0x3D87E0)),
            (UIWidgetFactory.TokenPrimaryPressed, Hex(0x2563D4), Hex(0x1259AE)),
            (UIWidgetFactory.TokenTextOnPrimary, Hex(0xFFFFFF), Hex(0xFFFFFF)),
            (UIWidgetFactory.TokenTextStrong, Hex(0xF2F4F8), Hex(0x14181F)),
            (UIWidgetFactory.TokenTextDefault, Hex(0xC9CED6), Hex(0x2A313C)),
            (UIWidgetFactory.TokenTextMuted, Hex(0x8A919E), Hex(0x6A7280)),
            (UIWidgetFactory.TokenSuccess, Hex(0x51CF66), Hex(0x2E933C)),
            (UIWidgetFactory.TokenSuccessHover, ColorUtils.DeriveHover(Hex(0x51CF66)), ColorUtils.DeriveHover(Hex(0x2E933C))),
            (UIWidgetFactory.TokenSuccessPressed, ColorUtils.DerivePressed(Hex(0x51CF66)), ColorUtils.DerivePressed(Hex(0x2E933C))),
            ("Warning", Hex(0xFFD43B), Hex(0xB35C00)),
            ("Error", Hex(0xFF6B6B), Hex(0xC73E3E)),
            (UIWidgetFactory.TokenDanger, Hex(0xFF6B6B), Hex(0xC73E3E)),
            (UIWidgetFactory.TokenDangerHover, ColorUtils.DeriveHover(Hex(0xFF6B6B)), ColorUtils.DeriveHover(Hex(0xC73E3E))),
            (UIWidgetFactory.TokenDangerPressed, ColorUtils.DerivePressed(Hex(0xFF6B6B)), ColorUtils.DerivePressed(Hex(0xC73E3E))),
            (UIWidgetFactory.TokenShadow, new Color(0f, 0f, 0f, 0.5f), new Color(0f, 0f, 0f, 0.25f))
        };

        /// <summary>
        /// Adds dark-palette defaults for any factory-referenced token the theme is missing.
        /// The spec generator calls this before building widgets so a minimal spec theme never
        /// produces dangling token references (validation stays clean); spec-defined tokens win
        /// because the generator applies them first and this only fills gaps.
        /// </summary>
        public static void EnsureFactoryTokens(Theme theme)
        {
            if (theme == null) return;
            bool changed = false;
            foreach ((string token, Color dark, Color light) entry in Palette)
            {
                if (theme.HasToken(entry.token)) continue;
                theme.SetToken(entry.token, entry.dark);
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(theme);
        }

        // ------------------------------------------------------------------ text styles

        /// <summary>
        /// The curated type scale (Inter): style name → (font weight slot, size, color token).
        /// Built lazily because the font assets come from <see cref="FontAssetBootstrap"/>.
        /// </summary>
        private static TextStyle[] BuildTextStyles()
        {
            TMPro.TMP_FontAsset regular = FontAssetBootstrap.InterRegular;
            TMPro.TMP_FontAsset semiBold = FontAssetBootstrap.InterSemiBold;
            TMPro.TMP_FontAsset bold = FontAssetBootstrap.InterBold;
            return new[]
            {
                new TextStyle { name = UIWidgetFactory.TextStyleDisplay, font = bold, size = 72f,
                    characterSpacing = -1f, color = new ThemeColorRef(UIWidgetFactory.TokenTextStrong) },
                new TextStyle { name = UIWidgetFactory.TextStyleTitle, font = semiBold, size = 44f,
                    characterSpacing = -0.5f, color = new ThemeColorRef(UIWidgetFactory.TokenTextStrong) },
                new TextStyle { name = UIWidgetFactory.TextStyleHeading, font = semiBold, size = 30f,
                    color = new ThemeColorRef(UIWidgetFactory.TokenTextStrong) },
                new TextStyle { name = UIWidgetFactory.TextStyleBody, font = regular, size = 24f,
                    color = new ThemeColorRef(UIWidgetFactory.TokenTextDefault) },
                new TextStyle { name = UIWidgetFactory.TextStyleCaption, font = regular, size = 18f,
                    color = new ThemeColorRef(UIWidgetFactory.TokenTextMuted) },
                new TextStyle { name = UIWidgetFactory.TextStyleButtonLabel, font = semiBold, size = 24f,
                    color = new ThemeColorRef(UIWidgetFactory.TokenTextOnPrimary) },
                new TextStyle { name = UIWidgetFactory.TextStyleButtonLabelSmall, font = semiBold, size = 18f,
                    color = new ThemeColorRef(UIWidgetFactory.TokenTextOnPrimary) },
                new TextStyle { name = UIWidgetFactory.TextStyleButtonLabelLarge, font = semiBold, size = 30f,
                    color = new ThemeColorRef(UIWidgetFactory.TokenTextOnPrimary) }
            };
        }

        /// <summary>
        /// Adds the curated text styles the factory references when the theme is missing them —
        /// the typographic mirror of <see cref="EnsureFactoryTokens"/>: fills gaps only, so
        /// spec/user-defined styles win.
        /// </summary>
        public static void EnsureTextStyles(Theme theme)
        {
            if (theme == null) return;
            bool changed = false;
            foreach (TextStyle style in BuildTextStyles())
            {
                if (theme.TryGetTextStyle(style.name, out _)) continue;
                theme.SetTextStyle(style);
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(theme);
        }

        public static void ExpandTheme(Theme theme, GenerateReport report)
        {
            if (theme == null)
            {
                report.issues.Add("No theme on the settings asset — run Create or Repair Settings first");
                return;
            }

            theme.AddVariant(DarkVariant);
            theme.AddVariant(LightVariant);
            foreach ((string token, Color dark, Color light) entry in Palette)
            {
                theme.SetToken(entry.token, entry.dark, DarkVariant);
                theme.SetToken(entry.token, entry.light, LightVariant);
                // legacy variants (e.g. "Default") get the dark value so nothing renders white
                foreach (Theme.ThemeVariant variant in theme.Variants)
                    if (variant.name != DarkVariant && variant.name != LightVariant)
                        theme.SetToken(entry.token, entry.dark, variant.name);
            }
            theme.ActiveVariantName = DarkVariant;

            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StyleCard,
                radius = 16f,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenSurface)
            });
            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StylePanel,
                radius = 14f,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenBackground)
            });
            theme.SetShapeStyle(new ShapeStyle
            {
                name = UIWidgetFactory.StyleControl,
                radius = 12f,
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
                radius = 18f,
                softness = 18f,
                fillColor = new ThemeColorRef(UIWidgetFactory.TokenShadow)
            });

            TextStyle[] textStyles = BuildTextStyles();
            foreach (TextStyle style in textStyles)
                theme.SetTextStyle(style);

            EditorUtility.SetDirty(theme);
            report.updated.Add($"Theme '{theme.name}': {Palette.Length} tokens × Dark/Light, " +
                               $"5 shape styles, {textStyles.Length} text styles");
        }

        // ------------------------------------------------------------------ button variants

        /// <summary>
        /// Seeds the five canonical button variants (primary/secondary/ghost/danger/success) into
        /// <see cref="NeoUISettings.buttonVariants"/> as first-class, editable data (design-system-
        /// cohesion-plan.md Phase 2.6 — root cause of "built-ins can't be edited": they previously
        /// existed ONLY inside <c>UIWidgetFactory.VariantColors</c>'s fallback switch, so a fresh
        /// project's `buttonVariants` list was empty and the Design System window's Buttons tab had
        /// nothing to show). ADDITIVE, create-missing-only BY NAME (case-insensitive): an entry the
        /// project already has — whether it's one of these five with hand-edited colors, or a
        /// renamed/custom variant — is never touched, so both user edits and the committed repo
        /// settings asset survive a repair.
        /// <para/>
        /// The four legacy entries mirror <c>UIWidgetFactory.VariantColors</c>'s switch exactly,
        /// state for state. Most states are token-bound (<c>ThemeColorRef(token)</c>); the switch's
        /// handful of raw, non-token colors have no matching theme token to bind to, so they are
        /// reproduced as the same raw <c>ThemeColorRef(Color)</c> values instead (documented per
        /// case below) rather than invented a token that doesn't exist. <c>success</c> is NEW — it
        /// has no switch case at all, so it only ever resolves from this seeded data, proving the
        /// data path (not the switch) is now the source of truth.
        /// </summary>
        public static void EnsureButtonVariants(NeoUISettings settings, GenerateReport report)
        {
            if (settings == null) return;
            if (settings.buttonVariants == null) settings.buttonVariants = new List<ButtonVariantAsset>();

            bool changed = false;
            foreach (ButtonVariantAsset seed in BuiltInButtonVariants())
            {
                bool exists = false;
                foreach (ButtonVariantAsset existing in settings.buttonVariants)
                {
                    if (existing != null && string.Equals(existing.name, seed.name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;

                settings.buttonVariants.Add(seed);
                report?.created.Add($"Button variant '{seed.name}'");
                changed = true;
            }

            if (changed) EditorUtility.SetDirty(settings);
        }

        private static IEnumerable<ButtonVariantAsset> BuiltInButtonVariants()
        {
            yield return new ButtonVariantAsset
            {
                name = UIWidgetFactory.VariantPrimary,
                contentToken = UIWidgetFactory.TokenTextOnPrimary,
                colors = new SelectableColorSet
                {
                    normal = new ThemeColorRef(UIWidgetFactory.TokenPrimary),
                    highlighted = new ThemeColorRef(UIWidgetFactory.TokenPrimaryHover),
                    pressed = new ThemeColorRef(UIWidgetFactory.TokenPrimaryPressed),
                    selected = new ThemeColorRef(UIWidgetFactory.TokenPrimaryHover),
                    // no "disabled" token exists yet — same raw 50%-gray-at-50%-alpha the switch uses
                    disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.5f))
                }
            };
            yield return new ButtonVariantAsset
            {
                name = UIWidgetFactory.VariantSecondary,
                contentToken = UIWidgetFactory.TokenTextStrong,
                colors = new SelectableColorSet
                {
                    normal = new ThemeColorRef(UIWidgetFactory.TokenSurface),
                    highlighted = new ThemeColorRef(UIWidgetFactory.TokenSurfaceElevated),
                    pressed = new ThemeColorRef(UIWidgetFactory.TokenBackground),
                    selected = new ThemeColorRef(UIWidgetFactory.TokenSurfaceElevated),
                    // raw, lighter-alpha disabled tint — no matching token, same value as the switch
                    disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.3f))
                }
            };
            yield return new ButtonVariantAsset
            {
                name = UIWidgetFactory.VariantGhost,
                contentToken = UIWidgetFactory.TokenPrimary,
                colors = new SelectableColorSet
                {
                    // the switch's rest state is a raw fully-transparent white (no surface to token-
                    // bind — ghost has no fill at rest); reproduced as the same raw color
                    normal = new ThemeColorRef(new Color(1f, 1f, 1f, 0f)),
                    highlighted = new ThemeColorRef(UIWidgetFactory.TokenSurfaceElevated),
                    pressed = new ThemeColorRef(UIWidgetFactory.TokenSurface),
                    selected = new ThemeColorRef(UIWidgetFactory.TokenSurfaceElevated),
                    // raw, faint disabled tint — no matching token, same value as the switch
                    disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.15f))
                }
            };
            yield return new ButtonVariantAsset
            {
                name = UIWidgetFactory.VariantDanger,
                contentToken = UIWidgetFactory.TokenTextOnPrimary,
                colors = new SelectableColorSet
                {
                    normal = new ThemeColorRef(UIWidgetFactory.TokenDanger),
                    highlighted = new ThemeColorRef(UIWidgetFactory.TokenDangerHover),
                    pressed = new ThemeColorRef(UIWidgetFactory.TokenDangerPressed),
                    selected = new ThemeColorRef(UIWidgetFactory.TokenDangerHover),
                    // no "disabled" token exists yet — same raw 50%-gray-at-50%-alpha the switch uses
                    disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.5f))
                }
            };
            // NEW: no fallback-switch case at all — resolves purely from this seeded data.
            yield return new ButtonVariantAsset
            {
                name = "success",
                contentToken = UIWidgetFactory.TokenTextOnPrimary,
                colors = new SelectableColorSet
                {
                    normal = new ThemeColorRef(UIWidgetFactory.TokenSuccess),
                    highlighted = new ThemeColorRef(UIWidgetFactory.TokenSuccessHover),
                    pressed = new ThemeColorRef(UIWidgetFactory.TokenSuccessPressed),
                    selected = new ThemeColorRef(UIWidgetFactory.TokenSuccessHover),
                    disabled = new ThemeColorRef(UIWidgetFactory.TokenOutline)
                }
            };
        }

        // ------------------------------------------------------------------ showcase view

        /// <summary> A demo view: card with every widget in a vertical stack, on the background. </summary>
        private static GameObject BuildShowcase(NeoUISettings settings)
        {
            GameObject root = UIWidgetFactory.CreateRect(null, "Starter_Showcase", Vector2.zero, "Stretch");
            root.AddComponent<CanvasGroup>();
            var view = root.AddComponent<UIView>();
            view.id = new ViewId("Starter", "Showcase");

            GameObject background = UIWidgetFactory.CreateRect((RectTransform)root.transform,
                "Background", Vector2.zero, "Stretch");
            UIWidgetFactory.AddShape(background, ShapeType.RoundedRect, 0f, UIWidgetFactory.TokenBackground);

            GameObject card = UIWidgetFactory.CreateCard((RectTransform)root.transform, new Vector2(440f, 620f));
            var content = (RectTransform)card.transform.Find(UIWidgetFactory.ContentName);

            UIWidgetFactory.CreateLabel(content, "Starter Kit", UIWidgetFactory.TokenTextStrong, 32f, "Title",
                TextAlignmentOptions.MidlineLeft, textStyle: UIWidgetFactory.TextStyleHeading);
            UIWidgetFactory.CreateLabel(content, "Every widget, themed and functional.",
                UIWidgetFactory.TokenTextMuted, 20f, "Subtitle", TextAlignmentOptions.MidlineLeft,
                textStyle: UIWidgetFactory.TextStyleCaption);
            UIWidgetFactory.CreateButton(content, "Starter", "ShowcaseButton", "Button");
            UIWidgetFactory.CreateToggle(content, "Starter", "ShowcaseToggle", "Toggle");
            UIWidgetFactory.CreateSwitch(content, "Starter", "ShowcaseSwitch");
            UIWidgetFactory.CreateSlider(content, "Starter", "ShowcaseSlider");
            UIWidgetFactory.CreateProgressBar(content);
            UIWidgetFactory.CreateTabBar(content, "Starter", new List<(string, string)>
                { ("ShowTab1", "One"), ("ShowTab2", "Two"), ("ShowTab3", "Three") });
            return root;
        }

        // ------------------------------------------------------------------ prefab plumbing

        private static GameObject SavePrefab(GenerateReport report, NeoUISettings settings,
            string name, System.Func<GameObject> builder)
        {
            string path = $"{StarterFolder}/{name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool created = existing == null;
            if (!created && existing.GetComponent<GeneratedMarker>() == null)
            {
                report.collisions.Add($"'{path}' exists but was not generated — starter prefab '{name}' skipped");
                return existing;
            }

            GameObject root = builder();
            GeneratedMarker marker = root.AddComponent<GeneratedMarker>();
            marker.specSource = $"starter:{name}";

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            (created ? report.created : report.updated).Add($"Starter prefab '{name}' → {path}");
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f);
    }
}
