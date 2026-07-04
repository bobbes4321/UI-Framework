using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Pattern R registry of <see cref="NeoWidgetPreset"/>s — the catalog of reusable widget styles
    /// ("Primary Button", "Section Header"). Mirrors <c>ShowcaseRegistry</c>: code-seeded built-ins (none
    /// by default — the shipped library lives as discoverable assets under <see cref="PresetsRoot"/>,
    /// created by <c>PresetLibraryBootstrap</c>) PLUS a <see cref="NeoAssetRegistry{TAsset,TEntry}"/>
    /// discovery pass that folds in every <see cref="NeoWidgetPreset"/> asset in the project. A
    /// consuming project therefore adds a preset by dropping one asset — no fork, no C#.
    /// <para>
    /// Editor-only and single-domain. Discovery invalidation on asset import/delete/move is handled by
    /// the shared <see cref="NeoAssetRegistryPostprocessor"/> so a freshly created/edited preset shows up
    /// without a domain reload.
    /// </para>
    /// </summary>
    public static class NeoWidgetPresets
    {
        /// <summary> Root folder the package's built-in preset library lives under. </summary>
        public const string PresetsRoot = "Assets/Neo UI Framework/Presets";

        // Identity project: the registry entry IS the discovered asset. No code-seeded built-ins — the
        // shipped library ships as assets on disk (PresetLibraryBootstrap), not a code list.
        private static readonly NeoAssetRegistry<NeoWidgetPreset, NeoWidgetPreset> _registry =
            new NeoAssetRegistry<NeoWidgetPreset, NeoWidgetPreset>(
                key: p => p.presetName,
                project: p => p,
                registryName: "NeoWidgetPresets");

        /// <summary>
        /// All registered presets (discovered assets + manual registrations), in registration order,
        /// with any destroyed/fake-null <see cref="ScriptableObject"/> filtered out — a preset can be
        /// <c>DestroyImmediate</c>d (e.g. by a test) without a corresponding asset reimport that would
        /// otherwise invalidate discovery and evict it from the cached snapshot.
        /// </summary>
        public static IReadOnlyList<NeoWidgetPreset> All
        {
            get
            {
                IReadOnlyList<NeoWidgetPreset> all = _registry.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == null) return all.Where(p => p != null).ToArray();
                }
                return all;
            }
        }

        /// <summary> The names of every registered preset. </summary>
        public static IEnumerable<string> Names => All.Select(p => p.presetName);

        /// <summary> Presets whose <see cref="NeoWidgetPreset.targetKind"/> matches (ordinal), for kind-scoped pickers. </summary>
        public static IEnumerable<NeoWidgetPreset> ForKind(string kind) =>
            All.Where(p => string.Equals(p.targetKind, kind, StringComparison.Ordinal));

        /// <summary> Case-sensitive (ordinal) lookup by <see cref="NeoWidgetPreset.presetName"/>. </summary>
        public static bool TryGet(string presetName, out NeoWidgetPreset preset) => _registry.TryGet(presetName, out preset);

        /// <summary>
        /// Registers a preset. If one with the same <see cref="NeoWidgetPreset.presetName"/> exists
        /// (ordinal) it is replaced in place (a discovered asset or project preset overrides a built-in);
        /// otherwise it is appended. Null / name-less presets are warned-and-ignored.
        /// </summary>
        public static void Register(NeoWidgetPreset preset) => _registry.Register(preset);

        /// <summary> Test-only: removes a registered preset by name (ordinal). Returns true if one was removed. </summary>
        internal static bool Remove(string presetName) => _registry.Remove(presetName);

        /// <summary>
        /// Test-only: clears the registry and forces a fresh discovery on next access, so a suite that
        /// registers in-memory presets leaves the static registry clean for sibling suites.
        /// </summary>
        internal static void ResetForTests() => _registry.ResetForTests();

        /// <summary>
        /// Marks the discovered set stale so the next access re-scans for <see cref="NeoWidgetPreset"/>
        /// assets. The shared <see cref="NeoAssetRegistryPostprocessor"/> already calls this
        /// automatically on asset import/delete/move; exposed publicly too since existing call sites
        /// invalidate explicitly.
        /// </summary>
        public static void InvalidateDiscovery() => _registry.InvalidateDiscovery();
    }
}
