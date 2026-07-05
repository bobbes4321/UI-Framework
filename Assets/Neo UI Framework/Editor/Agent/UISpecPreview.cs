using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Gates the device-accurate <see cref="UnityEngine.UI.CanvasScaler"/>-equivalent in the shared
    /// render path. The agent <c>preview</c>/<c>screenshot</c> matrix and <c>BeautificationAcceptance</c>
    /// pass the default (<see cref="None"/>, no scaler) so their renders stay byte-stable; a caller wanting
    /// a device-scaled preview (content scaled like the shipped game rather than pixel-for-pixel) passes
    /// <c>deviceScale = true</c>.
    /// </summary>
    public readonly struct RenderOptions
    {
        /// <summary>
        /// When true, the preview canvas scales its content like a real device (CanvasScaler-equivalent:
        /// ScaleWithScreenSize against the settings' reference resolution) so the same view at 320-wide
        /// and 1920-wide shows the same UI proportionally. Default false = pixel-for-pixel at the render
        /// size (the historical, byte-stable agent behavior).
        /// </summary>
        public readonly bool deviceScale;

        public RenderOptions(bool deviceScale) => this.deviceScale = deviceScale;

        /// <summary> The historical no-scaler behavior — the default for the agent render matrix. </summary>
        public static RenderOptions None => default;
    }

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
        /// <summary>
        /// The resolution matrix the headless <c>preview</c>/<c>screenshot</c> actions (and the Gallery
        /// window's resolution picker) render across. DERIVED from <see cref="ComposerDevicePresets.All"/>
        /// (the single source of truth) — a project that registers a device sees it here too. Returns a
        /// fresh array per call (the registry can grow at runtime); callers that index it should read it
        /// once. The legacy trio (phone-portrait/landscape, tablet-portrait) stays first because the
        /// built-in registry seeds them first, so existing renders/indices are unchanged.
        /// </summary>
        public static (string name, int width, int height)[] DefaultResolutions
        {
            get
            {
                IReadOnlyList<DevicePreset> presets = ComposerDevicePresets.All;
                var matrix = new (string name, int width, int height)[presets.Count];
                for (int i = 0; i < presets.Count; i++)
                    matrix[i] = (presets[i].id, presets[i].width, presets[i].height);
                return matrix;
            }
        }

        /// <summary>
        /// Builds every view in the spec in-memory and returns the root GameObjects. The caller owns
        /// them — destroy when done. No assets are written. (Public so it's testable without a GPU.)
        /// </summary>
        public static List<GameObject> BuildViews(UISpec spec)
        {
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            var report = new GenerateReport();
            // make sure factory tokens/text styles exist so the build resolves, but never save
            StarterKitBootstrap.EnsureFactoryTokens(settings.theme);
            StarterKitBootstrap.EnsureTextStyles(settings.theme);
            // build the spec's settings/cheats catalogs in memory so views that embed a "settings"/
            // "cheats" element render real rows — preview commits no catalog SOs, so without this the
            // menu element finds no catalog and shows nothing.
            UISpecGenerator.PrepareCatalogsInMemory(spec, settings);

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
            (string name, int width, int height)[] resolutions = null,
            RenderOptions options = default)
        {
            resolutions ??= DefaultResolutions;
            var written = new List<string>();
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            StarterKitBootstrap.EnsureFactoryTokens(settings.theme);
            StarterKitBootstrap.EnsureTextStyles(settings.theme);
            UISpecGenerator.PrepareCatalogsInMemory(spec, settings);

            foreach (ViewSpec view in spec.views)
            {
                string viewName = Sanitize($"{view.category}_{view.viewName}");
                foreach ((string name, int width, int height) res in resolutions)
                {
                    // build fresh per render — CaptureLive consumes (destroys) the root
                    GameObject root = UISpecGenerator.BuildViewGameObject(view, settings, new GenerateReport());
                    if (root == null) continue;
                    string path = $"{outputDir}/{viewName}/{res.name}.png";
                    written.Add(UIScreenshotter.CaptureLive(root, path, res.width, res.height, options));
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
