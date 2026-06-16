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
    /// created by <c>PresetLibraryBootstrap</c>) PLUS a lazy <see cref="EnsureDiscovered"/> pass that folds
    /// in every <see cref="NeoWidgetPreset"/> asset in the project. A consuming project therefore adds a
    /// preset by dropping one asset — no fork, no C#.
    /// <para>
    /// Editor-only and single-domain, so a static list suffices. Discovery is invalidated on asset import
    /// (see <see cref="NeoWidgetPresetPostprocessor"/>) so a freshly created/edited preset shows up without
    /// a domain reload.
    /// </para>
    /// </summary>
    public static class NeoWidgetPresets
    {
        /// <summary> Root folder the package's built-in preset library lives under. </summary>
        public const string PresetsRoot = "Assets/Neo UI Framework/Presets";

        private static readonly List<NeoWidgetPreset> _presets = new List<NeoWidgetPreset>();

        private static bool _discovered;

        /// <summary> All registered presets (built-ins + discovered assets), in registration order. </summary>
        public static IReadOnlyList<NeoWidgetPreset> All
        {
            get { EnsureDiscovered(); return _presets; }
        }

        /// <summary> The names of every registered preset. </summary>
        public static IEnumerable<string> Names
        {
            get { EnsureDiscovered(); return _presets.Where(p => p != null).Select(p => p.presetName); }
        }

        /// <summary> Presets whose <see cref="NeoWidgetPreset.targetKind"/> matches (ordinal), for kind-scoped pickers. </summary>
        public static IEnumerable<NeoWidgetPreset> ForKind(string kind)
        {
            EnsureDiscovered();
            return _presets.Where(p => p != null && string.Equals(p.targetKind, kind, StringComparison.Ordinal));
        }

        /// <summary> Case-sensitive (ordinal) lookup by <see cref="NeoWidgetPreset.presetName"/>. </summary>
        public static bool TryGet(string presetName, out NeoWidgetPreset preset)
        {
            EnsureDiscovered();
            preset = string.IsNullOrEmpty(presetName)
                ? null
                : _presets.FirstOrDefault(p => p != null && string.Equals(p.presetName, presetName, StringComparison.Ordinal));
            return preset != null;
        }

        /// <summary>
        /// Registers a preset. If one with the same <see cref="NeoWidgetPreset.presetName"/> exists
        /// (ordinal) it is replaced in place (a discovered asset or project preset overrides a built-in);
        /// otherwise it is appended. Null / name-less presets are ignored with a warning.
        /// </summary>
        public static void Register(NeoWidgetPreset preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.presetName))
            {
                Debug.LogWarning("[Neo.UI] NeoWidgetPresets.Register ignored a null/name-less preset");
                return;
            }
            int existing = _presets.FindIndex(p => p != null
                && string.Equals(p.presetName, preset.presetName, StringComparison.Ordinal));
            if (existing >= 0) _presets[existing] = preset;
            else _presets.Add(preset);
        }

        /// <summary> Test-only: removes a registered preset by name (ordinal). Returns true if one was removed. </summary>
        internal static bool Remove(string presetName) =>
            _presets.RemoveAll(p => p != null && string.Equals(p.presetName, presetName, StringComparison.Ordinal)) > 0;

        /// <summary>
        /// Test-only: clears the registry and forces a fresh discovery on next access, so a suite that
        /// registers in-memory presets leaves the static registry clean for sibling suites.
        /// </summary>
        internal static void ResetForTests()
        {
            _presets.Clear();
            _discovered = false;
        }

        /// <summary>
        /// Marks the discovered set stale so the next access re-scans for <see cref="NeoWidgetPreset"/>
        /// assets. Called by the asset post-processor on any <c>.asset</c> import.
        /// </summary>
        public static void InvalidateDiscovery() => _discovered = false;

        /// <summary>
        /// Lazily folds every <see cref="NeoWidgetPreset"/> asset in the project into the registry, once
        /// per discovery generation. A discovered asset overrides a built-in of the same name (it routes
        /// through <see cref="Register"/>'s replace-by-name). Cheap and idempotent.
        /// </summary>
        private static void EnsureDiscovered()
        {
            if (_discovered) return;
            _discovered = true; // set first so a re-entrant Register can't recurse into discovery
            foreach (string guid in AssetDatabase.FindAssets("t:NeoWidgetPreset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var preset = AssetDatabase.LoadAssetAtPath<NeoWidgetPreset>(path);
                if (preset != null && !string.IsNullOrEmpty(preset.presetName)) Register(preset);
            }
        }
    }

    /// <summary>
    /// Invalidates preset discovery whenever a <c>.asset</c> is imported/deleted/moved, so a freshly
    /// created or edited <see cref="NeoWidgetPreset"/> surfaces without a domain reload. Cheap — it only
    /// flips a bool; the rescan is lazy.
    /// </summary>
    internal sealed class NeoWidgetPresetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (HasAsset(imported) || HasAsset(deleted) || HasAsset(moved))
                NeoWidgetPresets.InvalidateDiscovery();
        }

        private static bool HasAsset(string[] paths)
        {
            foreach (string p in paths)
                if (p != null && p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
