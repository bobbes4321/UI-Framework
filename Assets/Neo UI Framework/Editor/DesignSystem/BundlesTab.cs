using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Bundles" tab (Phase 2.8): lists every registered theme bundle — the three
    /// code-seeded built-ins PLUS any discovered <see cref="ThemeBundleDefinition"/> assets
    /// (<see cref="ThemeBundleRegistry"/>) — with an <b>Apply</b> per bundle gated behind a structural
    /// <see cref="ThemeBundles.PreviewDiff"/> confirm (the B6 fix: a re-apply that would stomp Design
    /// System token edits now NAMES what changes instead of silently overwriting), and a <b>Save Current
    /// Look As Bundle…</b> action that captures the live theme into a reusable definition
    /// (<see cref="ThemeBundles.SaveDefinitionFromTheme"/>).
    /// </summary>
    internal static class BundlesTab
    {
        /// <summary> Per-window UI state for the Bundles tab. </summary>
        internal sealed class State
        {
            public string newName = "MyLook";
        }

        internal static object CreateState() => new State();

        /// <summary> The descriptor the orchestrator registers (id "bundles", between Presets(30) and Motion(40+)). </summary>
        internal static DesignSystemTabDescriptor Descriptor =>
            new DesignSystemTabDescriptor("bundles", "Bundles", 50, CreateState, Draw);

        internal static void Draw(DesignSystemTabContext ctx)
        {
            EditorGUILayout.LabelField("Theme bundles", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "A bundle is a complete look — token palette, shape radii, type tracking and motion. " +
                "Applying one recolors the theme (all variants) and reseeds the widget-preset library.",
                EditorStyles.wordWrappedMiniLabel);

            var bundles = ThemeBundleRegistry.All.ToList();
            if (bundles.Count == 0)
                EditorGUILayout.HelpBox("No bundles registered. Save the current look below, drop a Theme " +
                    "Bundle Definition asset (right-click in Project → Create → Neo UI → Theme Bundle " +
                    "Definition), or tick \"Save as reusable Theme Bundle Definition\" in Setup → New " +
                    "Project Setup.", MessageType.Info);

            foreach (ThemeBundles.Bundle bundle in bundles)
                DrawBundleCard(ctx, bundle);

            NeoGUI.Splitter();
            DrawSaveCurrentLook(ctx);
        }

        private static void DrawBundleCard(DesignSystemTabContext ctx, ThemeBundles.Bundle bundle)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(bundle.name, EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(bundle.description))
                        EditorGUILayout.LabelField(bundle.description, EditorStyles.wordWrappedMiniLabel);
                    int variantCount = bundle.palettes?.Count ?? 0;
                    EditorGUILayout.LabelField(
                        $"{variantCount} variant(s) · radius {bundle.cardRadius:0} · {bundle.motionEase} {bundle.motionDuration:0.##}s",
                        EditorStyles.miniLabel);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Apply", GUILayout.Width(72f), GUILayout.Height(24f)))
                    ApplyWithDiff(ctx, bundle);
            }
        }

        // The B6 fix: preview the structural diff, then confirm with the real consequences named.
        private static void ApplyWithDiff(DesignSystemTabContext ctx, ThemeBundles.Bundle bundle)
        {
            ThemeBundles.BundleDiff diff = ThemeBundles.PreviewDiff(bundle, ctx.theme, ctx.settings);
            if (diff.IsEmpty)
            {
                EditorUtility.DisplayDialog($"Apply '{bundle.name}'",
                    "This bundle is already applied — no tokens or styles would change.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                $"Apply '{bundle.name}'?",
                diff.Summarize() + "\n\nEdits you made in the Design System window to these tokens/styles " +
                "will be overwritten.",
                "Apply", "Cancel");
            if (!confirmed) return;

            var report = new GenerateReport();
            ThemeBundles.Apply(bundle, ctx.settings, report);
            Debug.Log($"[Neo.UI] {report}");
            ctx.window?.Repaint();
        }

        private static void DrawSaveCurrentLook(DesignSystemTabContext ctx)
        {
            var s = ctx.State<State>();
            EditorGUILayout.LabelField("Save current look as bundle", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Captures the live theme — every variant's tokens plus the card/panel/control radii, " +
                "shadow softness, headline tracking and default motion — into a reusable Theme Bundle " +
                "Definition asset. (Per-corner radii, per-style fills and text fonts/sizes aren't part of " +
                "the bundle model and won't be captured.)",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                s.newName = EditorGUILayout.TextField("Name", s.newName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(s.newName)))
                    if (GUILayout.Button("Save…", GUILayout.Width(72f)))
                        SaveCurrentLook(ctx, s.newName.Trim());
            }
        }

        private static void SaveCurrentLook(DesignSystemTabContext ctx, string name)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Current Look As Bundle", name, "asset",
                "Capture the live theme as a reusable Theme Bundle Definition.",
                ThemeBundles.DefaultThemesFolder);
            if (string.IsNullOrEmpty(path)) return;

            string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileNameWithoutExtension(path);
            ThemeBundleDefinition def =
                ThemeBundles.SaveDefinitionFromTheme(leaf, ctx.theme, ctx.settings, folder);
            AssetDatabase.SaveAssets();
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
            ctx.window?.Repaint();
        }
    }
}
