using System;
using System.Collections.Generic;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// A single device preset the Composer's viewport (and the headless agent
    /// <c>preview</c>/<c>screenshot</c> matrix) can render at — a named width×height in portrait
    /// device pixels. The package seeds a useful spread (phone S/M/L, tablet, desktop, ultrawide,
    /// square, plus landscape variants) but a consuming project adds its own device — a specific
    /// handheld, a kiosk aspect, a watch face — with one <see cref="ComposerDevicePresets.Register"/>
    /// call from its own assembly, no fork.
    ///
    /// <para>This mirrors the shape of <see cref="CatalogKind"/> / <see cref="NeoCatalogKinds"/>
    /// (Pattern R — the Kinds Registry): a readonly value type plus a static replace-by-id registry,
    /// so the viewport iterates <see cref="ComposerDevicePresets.All"/> instead of switching over a
    /// fixed enum of aspect ratios — which was the user's #1 complaint.</para>
    /// </summary>
    public readonly struct DevicePreset
    {
        /// <summary> Stable id used to address the preset (and persist a viewport selection). </summary>
        public readonly string id;

        /// <summary> Human label shown in the toolbar dropdown ("iPhone 15", "Desktop 16:9"). </summary>
        public readonly string label;

        /// <summary> Portrait width in device px (the canvas render width). </summary>
        public readonly int width;

        /// <summary> Portrait height in device px (the canvas render height). </summary>
        public readonly int height;

        /// <summary> Optional reference-resolution / density hint (1 = no hint). Reserved for a future
        /// per-device CanvasScaler reference; the built-ins leave it at 1. </summary>
        public readonly float dpiScale;

        public DevicePreset(string id, string label, int width, int height, float dpiScale = 1f)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A DevicePreset needs a non-empty id.", nameof(id));
            this.id = id;
            this.label = string.IsNullOrEmpty(label) ? id : label;
            this.width = width;
            this.height = height;
            this.dpiScale = dpiScale <= 0f ? 1f : dpiScale;
        }
    }

    /// <summary>
    /// The single source of truth for the set of device presets the viewport offers. Seeded with the
    /// package built-ins through <see cref="Register"/> (never a hardcoded switch); a consuming project
    /// registers its own device once (e.g. from an <c>[InitializeOnLoad]</c> static ctor). The headless
    /// <c>preview</c>/<c>screenshot</c> matrix re-sources its resolution list from here too
    /// (see <see cref="UISpecPreview.DefaultResolutions"/>), so the Composer and the agent path always
    /// agree on the device spread.
    /// </summary>
    public static class ComposerDevicePresets
    {
        // Built-ins seeded THROUGH Register (seam-first): a useful spread from small phones to
        // ultrawide. Portrait px; the viewport's rotate toggle swaps w/h for landscape. The first
        // three intentionally match the old UISpecPreview.DefaultResolutions tuple so the agent
        // matrix and committed renders are unchanged.
        private static readonly List<DevicePreset> _presets = new List<DevicePreset>();

        static ComposerDevicePresets()
        {
            // phones — the legacy trio first (phone-portrait / phone-landscape kept as ids so the
            // headless matrix and any saved references resolve identically)
            Register(new DevicePreset("phone-portrait", "Phone Portrait", 1080, 1920));
            Register(new DevicePreset("phone-landscape", "Phone Landscape", 1920, 1080));
            Register(new DevicePreset("tablet-portrait", "Tablet Portrait", 1536, 2048));

            // a denser device spread for the free viewport (point logical sizes — DevTools style)
            Register(new DevicePreset("phone-s", "Phone S", 320, 568));    // small handset
            Register(new DevicePreset("phone-m", "Phone M", 375, 667));    // common handset
            Register(new DevicePreset("phone-l", "Phone L", 414, 896));    // large handset
            Register(new DevicePreset("phone-s-landscape", "Phone S Landscape", 568, 320));
            Register(new DevicePreset("phone-m-landscape", "Phone M Landscape", 667, 375));
            Register(new DevicePreset("phone-l-landscape", "Phone L Landscape", 896, 414));
            Register(new DevicePreset("tablet-landscape", "Tablet Landscape", 2048, 1536));
            Register(new DevicePreset("desktop-16-9", "Desktop 16:9", 1920, 1080));
            Register(new DevicePreset("ultrawide-21-9", "Ultrawide 21:9", 2560, 1080));
            Register(new DevicePreset("square", "Square 1:1", 1080, 1080));
        }

        /// <summary> Every registered preset, in registration order (built-ins first). </summary>
        public static IReadOnlyList<DevicePreset> All => _presets;

        /// <summary> Resolves a preset by id. Returns false when nothing is registered for it. </summary>
        public static bool TryGet(string id, out DevicePreset preset)
        {
            if (!string.IsNullOrEmpty(id))
                for (int i = 0; i < _presets.Count; i++)
                    if (_presets[i].id == id) { preset = _presets[i]; return true; }
            preset = default;
            return false;
        }

        /// <summary>
        /// Registers (or replaces, by id) a device preset — the extension seam. A consuming project
        /// calls this once to make a new device appear in the viewport dropdown and the agent matrix.
        /// </summary>
        public static void Register(DevicePreset preset)
        {
            if (string.IsNullOrEmpty(preset.id))
                throw new ArgumentException("A DevicePreset needs a non-empty id.", nameof(preset));
            for (int i = 0; i < _presets.Count; i++)
                if (_presets[i].id == preset.id) { _presets[i] = preset; return; }
            _presets.Add(preset);
        }
    }
}
