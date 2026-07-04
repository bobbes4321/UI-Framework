using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The beautification plan's acceptance render: generates the demo spec, validates, then
    /// captures every generated view under the starter theme AND each curated bundle — the same
    /// spec, four distinct looks. Batch entry point (needs a graphics device — no -nographics):
    /// <c>-executeMethod Neo.UI.Editor.BeautificationAcceptance.Run</c>.
    /// Screenshots land in <c>neo-screenshots/beautification/&lt;look&gt;/</c> (outside Temp).
    /// </summary>
    public static class BeautificationAcceptance
    {
        private const string SpecFile = "Assets/Showcases/Specs/game-ui.json";
        private const string OutputRoot = "neo-screenshots/beautification";

        /// <summary>
        /// Resolution matrix the acceptance render sweeps — the button-teleport class of bug was
        /// aspect-ratio dependent and invisible in portrait-only renders. Phone portrait/landscape
        /// + tablet portrait covers the aspect ratios real layouts break at.
        /// </summary>
        private static readonly (string name, int width, int height)[] Resolutions =
        {
            ("phone-portrait", 1080, 1920),
            ("phone-landscape", 1920, 1080),
            ("tablet-portrait", 1536, 2048),
        };

        /// <summary>
        /// Batch entry: regenerate the demo spec and build the playable scene in one run —
        /// <c>-executeMethod Neo.UI.Editor.BeautificationAcceptance.GenerateDemoAndBuildScene</c>.
        /// </summary>
        public static void GenerateDemoAndBuildScene()
        {
            bool ok = false;
            try
            {
                GenerateReport report = UISpecGenerator.GenerateFromSpecFile(Path.GetFullPath(SpecFile));
                Debug.Log($"[Neo.UI] Demo generation:\n{report}");
                if (!report.hasProblems)
                {
                    // name the flow — a second spec sharing GeneratedRoot would otherwise make the
                    // build ambiguous (SpecFile is the GameUI demo). Resolve the showcase by id so the
                    // flow name comes from the registry (Phase C seeds 'game-ui'); fall back to "GameUI"
                    // so this batch entry still works before the showcase is seeded.
                    string flow = ShowcaseRegistry.TryGet("game-ui", out Showcase gameUi)
                        ? gameUi.flowName : "GameUI";
                    string scenePath = GeneratedSceneBuilder.Build(flow);
                    Debug.Log($"[Neo.UI] Scene ready: {scenePath}");
                    ok = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        public static void Run()
        {
            bool ok = false;
            var summary = new List<string>();
            try
            {
                ok = Execute(summary);
            }
            catch (Exception e)
            {
                summary.Add($"FAILED: {e}");
                Debug.LogException(e);
            }
            finally
            {
                Directory.CreateDirectory(OutputRoot);
                File.WriteAllLines($"{OutputRoot}/summary.txt", summary);
                Debug.Log($"[Neo.UI] Beautification acceptance:\n{string.Join("\n", summary)}");
                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        private static bool Execute(List<string> summary)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                summary.Add("No graphics device — run without -nographics");
                return false;
            }

            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            bool ok = true;

            // baseline: canonical starter system
            StarterKitBootstrap.ExpandTheme(settings.theme, new GenerateReport());
            ok &= GenerateAndCapture("starter", settings, summary);

            foreach (string bundleName in ThemeBundles.Names.ToList())
            {
                ThemeBundles.TryGet(bundleName, out ThemeBundles.Bundle bundle);
                var report = new GenerateReport();
                ThemeBundles.Apply(bundle, settings, report);
                if (report.issues.Count > 0)
                {
                    summary.AddRange(report.issues.Select(i => $"{bundleName}: ISSUE {i}"));
                    ok = false;
                }
                ok &= GenerateAndCapture(bundleName, settings, summary);
            }

            // leave the project on the canonical starter system
            StarterKitBootstrap.ExpandTheme(settings.theme, new GenerateReport());
            UISpecGenerator.GenerateFromSpecFile(Path.GetFullPath(SpecFile));
            return ok;
        }

        /// <summary>
        /// Regenerates the demo spec (so baked widget colors pick up the active theme) and
        /// captures every generated view. Validation issues fail the run; design warnings are
        /// reported but soft.
        /// </summary>
        private static bool GenerateAndCapture(string look, NeoUISettings settings, List<string> summary)
        {
            GenerateReport report = UISpecGenerator.GenerateFromSpecFile(Path.GetFullPath(SpecFile));
            if (report.hasProblems)
            {
                summary.Add($"{look}: generation problems:\n{report}");
                return false;
            }

            List<string> issues = AgentValidation.ValidateAll();
            if (issues.Count > 0)
            {
                summary.Add($"{look}: validation FAILED:\n  {string.Join("\n  ", issues)}");
                return false;
            }
            List<string> design = AgentValidation.ValidateDesign();
            foreach (string warning in design) summary.Add($"{look}: design: {warning}");

            int captured = 0;
            string folder = UISpecGenerator.ViewsFolder;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<UIView>() == null) continue;
                // sweep the resolution matrix — grouped by resolution so a given device's renders
                // sit side by side for eyeballing (and future golden diffs)
                foreach ((string resName, int width, int height) in Resolutions)
                {
                    string path = $"{OutputRoot}/{look}/{resName}/{prefab.name}.png";
                    UIScreenshotter.Capture(prefab, path, width, height);
                    captured++;
                }
            }
            summary.Add($"{look}: {captured} renders ({Resolutions.Length} resolutions), {design.Count} design warning(s)");
            return captured > 0;
        }
    }
}
