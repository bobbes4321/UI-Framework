using System.Collections.Generic;
using UnityEngine;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Renders a spec's views WITHOUT committing any prefab/asset — builds each view in-memory and
    /// (with a graphics device) captures it to PNGs at one or more resolutions. The fast feedback
    /// loop: an agent can generate a spec, look at its own output across device sizes, critique and
    /// revise, without polluting the project with throwaway prefabs.
    ///
    /// Renders under the CURRENT project theme (it never mutates theme/assets) — use the generator +
    /// acceptance render when you want a specific bundle baked in.
    /// </summary>
    public static class UISpecPreview
    {
        public static readonly (string name, int width, int height)[] DefaultResolutions =
        {
            ("phone-portrait", 1080, 1920),
            ("phone-landscape", 1920, 1080),
            ("tablet-portrait", 1536, 2048),
        };

        /// <summary>
        /// Builds every view in the spec in-memory and returns the root GameObjects. The caller owns
        /// them — destroy when done. No assets are written. (Public so it's testable without a GPU.)
        /// </summary>
        public static List<GameObject> BuildViews(UISpec spec)
        {
            AEUISettings settings = AEUISettingsBootstrap.GetOrCreateSettings();
            var report = new GenerateReport();
            // make sure factory tokens/text styles exist so the build resolves, but never save
            StarterKitBootstrap.EnsureFactoryTokens(settings.theme);
            StarterKitBootstrap.EnsureTextStyles(settings.theme);

            var roots = new List<GameObject>();
            foreach (ViewSpec view in spec.views)
            {
                GameObject root = UISpecGenerator.BuildViewGameObject(view, settings, report);
                if (root != null) roots.Add(root);
            }
            return roots;
        }

        /// <summary>
        /// Renders each view to <c>&lt;outputDir&gt;/&lt;view&gt;/&lt;resolution&gt;.png</c> across the
        /// resolution matrix (defaults to phone-portrait/landscape + tablet). Returns the written
        /// paths. Needs a graphics device.
        /// </summary>
        public static List<string> Render(UISpec spec, string outputDir,
            (string name, int width, int height)[] resolutions = null)
        {
            resolutions ??= DefaultResolutions;
            var written = new List<string>();
            AEUISettings settings = AEUISettingsBootstrap.GetOrCreateSettings();
            StarterKitBootstrap.EnsureFactoryTokens(settings.theme);
            StarterKitBootstrap.EnsureTextStyles(settings.theme);

            foreach (ViewSpec view in spec.views)
            {
                string viewName = Sanitize($"{view.category}_{view.viewName}");
                foreach ((string name, int width, int height) res in resolutions)
                {
                    // build fresh per render — CaptureLive consumes (destroys) the root
                    GameObject root = UISpecGenerator.BuildViewGameObject(view, settings, new GenerateReport());
                    if (root == null) continue;
                    string path = $"{outputDir}/{viewName}/{res.name}.png";
                    written.Add(UIScreenshotter.CaptureLive(root, path, res.width, res.height));
                }
            }
            return written;
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }
    }
}
