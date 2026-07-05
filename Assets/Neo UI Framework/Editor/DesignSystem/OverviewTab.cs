using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Overview" tab (Phase 2.1 of the cohesion plan) — the missing dashboard
    /// answering "what does my design system currently consist of?": one card per subsystem (Theme,
    /// Buttons, Widget Presets, Motion, Theme Bundles, Showcases) with counts pulled straight from
    /// already-cached registries/lists (no <see cref="AssetDatabase"/> scans in <see cref="Draw"/>) plus
    /// a one-line description and a jump button into the tab that owns that subsystem. Registered at
    /// order -10 so it sorts first and is the window's default tab. Stateless (no state factory) — every
    /// value drawn is a cheap <c>.Count</c> or lookup, not worth caching.
    /// </summary>
    internal static class OverviewTab
    {
        /// <summary> The registry entry the orchestrator (<see cref="NeoDesignSystemTabs"/>) wires in. </summary>
        internal static DesignSystemTabDescriptor Descriptor =>
            new DesignSystemTabDescriptor("overview", "Overview", -10, createState: null, Draw);

        internal static void Draw(DesignSystemTabContext ctx)
        {
            NeoUISettings settings = ctx.settings;
            Theme theme = ctx.theme;

            EditorGUILayout.LabelField("What is my design system?", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "A snapshot of every subsystem this window authors. Jump into a tab to edit it.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(NeoGUI.Spacing);

            DrawTheme(theme);
            DrawButtons(settings);
            DrawPresets();
            DrawMotion(settings);
            DrawBundles();
            DrawTemplates();
            DrawShowcases();
        }

        private static void DrawTheme(Theme theme)
        {
            using (new NeoGUI.SectionScope(null))
            {
                SectionHeader("Theme", NeoColors.Theming);
                EditorGUILayout.LabelField(
                    "Named color tokens grouped into variants (e.g. Dark/Light), plus reusable text and " +
                    "shape styles applied across the project.", EditorStyles.wordWrappedMiniLabel);

                string active = string.IsNullOrEmpty(theme.ActiveVariantName) ? "(none)" : theme.ActiveVariantName;
                Row("Active variant", active);
                Row("Variants", theme.Variants.Count.ToString());
                Row("Tokens", theme.GetTokenNames().Count().ToString());
                Row("Text styles", theme.TextStyles.Count.ToString());
                Row("Shape styles", theme.ShapeStyles.Count.ToString());

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Colors", GUILayout.Width(90f))) NeoDesignSystemWindow.OpenTab("colors");
                    if (GUILayout.Button("Typography", GUILayout.Width(90f))) NeoDesignSystemWindow.OpenTab("typography");
                    if (GUILayout.Button("Shapes", GUILayout.Width(90f))) NeoDesignSystemWindow.OpenTab("shapes");
                }
            }
        }

        private static void DrawButtons(NeoUISettings settings)
        {
            using (new NeoGUI.SectionScope(null))
            {
                SectionHeader("Buttons", NeoColors.Interactive);
                EditorGUILayout.LabelField(
                    "Per-state color variants (primary/secondary/ghost/danger/…) and named sizes a button " +
                    "picks by string.", EditorStyles.wordWrappedMiniLabel);

                int variantCount = settings.buttonVariants?.Count ?? 0;
                int sizeCount = settings.buttonSizes?.Count ?? 0;
                Row("Variants", variantCount > 0 ? variantCount.ToString()
                    : "0 — run Setup → Create or Repair Starter Kit to seed primary/secondary/ghost/danger/success");
                Row("Sizes", sizeCount.ToString());

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Buttons", GUILayout.Width(90f))) NeoDesignSystemWindow.OpenTab("buttons");
                    if (GUILayout.Button(new GUIContent("See it live", "Open the buttons showcase in the Hub"),
                            EditorStyles.linkLabel, GUILayout.Width(70f)))
                        NeoUIHubWindow.OpenShowcase("buttons");
                }
            }
        }

        private static void DrawPresets()
        {
            using (new NeoGUI.SectionScope(null))
            {
                SectionHeader("Widget Presets", NeoColors.Containers);
                EditorGUILayout.LabelField(
                    "Named component styles an element references via \"preset\" (e.g. \"Primary Button\") " +
                    "— the Figma-style component layer.", EditorStyles.wordWrappedMiniLabel);

                var byCategory = NeoWidgetPresets.All
                    .GroupBy(p => string.IsNullOrEmpty(p.category) ? "(uncategorized)" : p.category)
                    .OrderBy(g => g.Key);

                int total = 0;
                foreach (var group in byCategory)
                {
                    Row(group.Key, group.Count().ToString());
                    total += group.Count();
                }
                if (total == 0)
                    EditorGUILayout.LabelField("No presets yet — create one in the Presets tab, right-click " +
                        "in Project → Create → Neo UI → Widget Preset, or run Setup → Create or Repair " +
                        "Widget Presets.", EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Presets", GUILayout.Width(90f))) NeoDesignSystemWindow.OpenTab("presets");
                    if (GUILayout.Button(new GUIContent("See it live", "Open the presets showcase in the Hub"),
                            EditorStyles.linkLabel, GUILayout.Width(70f)))
                        NeoUIHubWindow.OpenShowcase("presets");
                }
            }
        }

        private static void DrawMotion(NeoUISettings settings)
        {
            using (new NeoGUI.SectionScope(null))
            {
                SectionHeader("Motion", NeoColors.Animation);
                EditorGUILayout.LabelField(
                    "Default animation preset per animator role (view show/hide, button hover/press, …) plus " +
                    "the full discoverable preset library.", EditorStyles.wordWrappedMiniLabel);

                int rolesWithDefault = NeoAnimatorRoles.All.Count(r =>
                    settings.TryGetDefaultAnimation(r.Id, out UIAnimationPreset p) && p != null);
                Row("Roles with a default", $"{rolesWithDefault} / {NeoAnimatorRoles.All.Count}");
                Row("Animation presets", AnimationPresetRegistry.All.Count.ToString());

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Motion", GUILayout.Width(90f))) NeoDesignSystemWindow.OpenTab("motion");
                    if (GUILayout.Button(new GUIContent("See it live", "Open the animations showcase in the Hub"),
                            EditorStyles.linkLabel, GUILayout.Width(70f)))
                        NeoUIHubWindow.OpenShowcase("animations");
                    // Shape effects/particles (glow, spinners, dissolve, …) live outside this tab set — the
                    // effects showcase is their nearest home, motion-wise, so it links from here too.
                    if (GUILayout.Button(new GUIContent("Effects", "Open the effects showcase in the Hub"),
                            EditorStyles.linkLabel, GUILayout.Width(50f)))
                        NeoUIHubWindow.OpenShowcase("effects");
                }
            }
        }

        private static void DrawBundles()
        {
            using (new NeoGUI.SectionScope(null))
            {
                SectionHeader("Theme Bundles", NeoColors.Theming);
                EditorGUILayout.LabelField(
                    "Complete token/type/shape/motion look-and-feel packages (e.g. CleanSlate, NeonArcade) " +
                    "applied in one click.", EditorStyles.wordWrappedMiniLabel);

                Row("Discovered bundles", ThemeBundleRegistry.All.Count.ToString());

                if (GUILayout.Button("Bundles", GUILayout.Width(90f))) NeoDesignSystemWindow.OpenTab("bundles");
            }
        }

        private static void DrawTemplates()
        {
            using (new NeoGUI.SectionScope(null))
            {
                SectionHeader("Layout Templates", NeoColors.Containers);
                EditorGUILayout.LabelField(
                    "Curated screen scaffolds (main menu, HUD, settings, popup) you stamp into a scene from " +
                    "GameObject → Neo UI → Insert Template… — extend by dropping a NeoLayoutTemplateDefinition asset.",
                    EditorStyles.wordWrappedMiniLabel);

                Row("Templates", Authoring.NeoLayoutTemplates.All.Count.ToString());
            }
        }

        private static void DrawShowcases()
        {
            using (new NeoGUI.SectionScope(null))
            {
                SectionHeader("Showcases", NeoColors.Flow);
                EditorGUILayout.LabelField(
                    "Self-contained demo scenes — the package's living, browsable catalog of what it can do.",
                    EditorStyles.wordWrappedMiniLabel);

                Row("Registered showcases", ShowcaseRegistry.All.Count.ToString());

                if (GUILayout.Button("Open Hub", GUILayout.Width(90f))) NeoUIHubWindow.Open();
            }
        }

        private static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(150f));
                EditorGUILayout.LabelField(value, EditorStyles.miniBoldLabel);
            }
        }

        // A small colored dot + bold title, standing in for a per-card NeoColors accent (SectionScope's
        // own title has no color param, and it's a shared file this tab must not edit) — matches the
        // family-accent convention ComponentHeader/AnimationPreviewEditor use elsewhere (Interactive=blue,
        // Containers=cyan, Animation=orange, Flow=purple, Theming=pink). Repaint-gated DrawRect only, no
        // per-frame style allocation.
        private static void SectionHeader(string title, Color accent)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 4f, 10f, 10f), accent);
            GUI.Label(new Rect(rect.x + 18f, rect.y, rect.width - 18f, rect.height), title, EditorStyles.boldLabel);
        }
    }
}
