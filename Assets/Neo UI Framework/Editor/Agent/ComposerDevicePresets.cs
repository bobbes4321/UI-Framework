using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A single device preset the headless agent <c>preview</c>/<c>screenshot</c> matrix (and the
    /// Gallery window's resolution picker) can render at — a named width×height in portrait device
    /// pixels. The package seeds a useful spread (phone S/M/L, tablet, desktop, ultrawide, square,
    /// plus landscape variants) but a consuming project adds its own device — a specific handheld, a
    /// kiosk aspect, a watch face — with one <see cref="ComposerDevicePresets.Register"/> call from
    /// its own assembly, no fork.
    ///
    /// <para>This mirrors the shape of <see cref="CatalogKind"/> / <see cref="NeoCatalogKinds"/>
    /// (Pattern R — the Kinds Registry): a readonly value type plus a static replace-by-id registry,
    /// so a caller iterates <see cref="ComposerDevicePresets.All"/> instead of switching over a
    /// fixed enum of aspect ratios — which was the user's #1 complaint.</para>
    /// </summary>
    public readonly struct DevicePreset
    {
        /// <summary> Stable id used to address the preset (and persist a resolution-picker selection). </summary>
        public readonly string id;

        /// <summary> Human label shown in a resolution dropdown ("iPhone 15", "Desktop 16:9"). </summary>
        public readonly string label;

        /// <summary> Portrait width in device px (the canvas render width). </summary>
        public readonly int width;

        /// <summary> Portrait height in device px (the canvas render height). </summary>
        public readonly int height;

        /// <summary> Optional reference-resolution / density hint (1 = no hint). Reserved for a future
        /// per-device CanvasScaler reference; the built-ins leave it at 1. </summary>
        public readonly float dpiScale;

        /// <summary>
        /// An empty/null <paramref name="id"/> is NOT validated here — this is a plain value type, not
        /// the registration seam. <see cref="ComposerDevicePresets.Register"/> is where an invalid id
        /// gets warned-and-ignored (audit A6: a throw from a constructor called inside a project's own
        /// <c>[InitializeOnLoad]</c> static ctor would poison the whole registering type).
        /// </summary>
        public DevicePreset(string id, string label, int width, int height, float dpiScale = 1f)
        {
            this.id = id;
            this.label = string.IsNullOrEmpty(label) ? id : label;
            this.width = width;
            this.height = height;
            this.dpiScale = dpiScale <= 0f ? 1f : dpiScale;
        }
    }

    /// <summary>
    /// The single source of truth for the set of device presets the resolution matrix offers. Seeded
    /// with the package built-ins through <see cref="Register"/> (never a hardcoded switch); a
    /// consuming project registers its own device once (e.g. from an <c>[InitializeOnLoad]</c> static
    /// ctor). The headless <c>preview</c>/<c>screenshot</c> matrix sources its resolution list from
    /// here (see <see cref="UISpecPreview.DefaultResolutions"/>), so every consumer of that list agrees
    /// on the device spread.
    /// <para>
    /// Wave 9: migrated onto <see cref="NeoKeyedRegistry{T}"/> (Pattern R's shared base) — this registry
    /// was mistakenly left off the Wave 4 migration list (the plan assumed it had been deleted with the
    /// retired Composer; the Wave 2 kill-list had already established it survives as a real dependency of
    /// <see cref="UISpecPreview.DefaultResolutions"/>). <see cref="Register"/> now warns-and-ignores an
    /// invalid (empty-id) entry instead of throwing — the exact audit A6 policy every sibling registry
    /// already had (a throw from a project's own <c>[InitializeOnLoad]</c> static ctor would poison the
    /// whole registering type with a <c>TypeInitializationException</c>).
    /// </para>
    /// </summary>
    public static class ComposerDevicePresets
    {
        private static readonly NeoKeyedRegistry<DevicePreset> _registry = new NeoKeyedRegistry<DevicePreset>(
            p => p.id,
            builtins: Builtins,
            registryName: "ComposerDevicePresets");

        // Built-ins seeded THROUGH the registry (seam-first): a useful spread from small phones to
        // ultrawide. Portrait px; landscape variants are separate entries with w/h swapped. The first
        // three intentionally match the old UISpecPreview.DefaultResolutions tuple so the agent
        // matrix and committed renders are unchanged.
        private static IEnumerable<DevicePreset> Builtins()
        {
            // phones — the legacy trio first (phone-portrait / phone-landscape kept as ids so the
            // headless matrix and any saved references resolve identically)
            yield return new DevicePreset("phone-portrait", "Phone Portrait", 1080, 1920);
            yield return new DevicePreset("phone-landscape", "Phone Landscape", 1920, 1080);
            yield return new DevicePreset("tablet-portrait", "Tablet Portrait", 1536, 2048);

            // a denser device spread (point logical sizes — DevTools style)
            yield return new DevicePreset("phone-s", "Phone S", 320, 568);    // small handset
            yield return new DevicePreset("phone-m", "Phone M", 375, 667);    // common handset
            yield return new DevicePreset("phone-l", "Phone L", 414, 896);    // large handset
            yield return new DevicePreset("phone-s-landscape", "Phone S Landscape", 568, 320);
            yield return new DevicePreset("phone-m-landscape", "Phone M Landscape", 667, 375);
            yield return new DevicePreset("phone-l-landscape", "Phone L Landscape", 896, 414);
            yield return new DevicePreset("tablet-landscape", "Tablet Landscape", 2048, 1536);
            yield return new DevicePreset("desktop-16-9", "Desktop 16:9", 1920, 1080);
            yield return new DevicePreset("ultrawide-21-9", "Ultrawide 21:9", 2560, 1080);
            yield return new DevicePreset("square", "Square 1:1", 1080, 1080);
        }

        /// <summary> Every registered preset, in registration order (built-ins first). </summary>
        public static IReadOnlyList<DevicePreset> All => _registry.All;

        /// <summary> Resolves a preset by id. Returns false when nothing is registered for it. </summary>
        public static bool TryGet(string id, out DevicePreset preset) => _registry.TryGet(id, out preset);

        /// <summary>
        /// Registers (or replaces, by id) a device preset — the extension seam. A consuming project
        /// calls this once to make a new device appear in the agent matrix and any resolution picker
        /// that reads <see cref="All"/>. An entry with an empty id is warned-and-ignored, never thrown
        /// (see the type doc above).
        /// </summary>
        public static void Register(DevicePreset preset) => _registry.Register(preset);

        /// <summary> Test-only: clears project registrations and re-seeds the built-ins on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }
}
