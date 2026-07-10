using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Asset-backed registry (Pattern R / Task 4.1) of <see cref="UIAnimationPreset"/> assets — the
    /// designer-friendly seam that removes the one manual step the old flow demanded: dropping a preset
    /// asset anywhere under <c>Assets/</c> is now enough to reference it by name from a spec, with no
    /// need to also add it to <see cref="AnimationPresetDatabase.presets"/>. Mirrors
    /// <see cref="NeoWidgetPresets"/> / <c>ShowcaseRegistry</c> (discovery invalidated on asset
    /// import/delete/move by the shared <see cref="NeoAssetRegistryPostprocessor"/>). A duplicate
    /// discovered <see cref="UIAnimationPreset.presetName"/> is last-discovered-wins, with a warning
    /// naming both asset paths (the base class's built-in duplicate-key policy).
    /// <para>
    /// Editor-only: animation presets are resolved at generate time (the resolved <see cref="UIAnimation"/>
    /// is baked into the prefab), never at runtime, so discovery can live in the editor assembly.
    /// </para>
    /// </summary>
    public static class AnimationPresetRegistry
    {
        // Identity project: the registry entry IS the discovered asset. No code-seeded built-ins.
        private static readonly NeoAssetRegistry<UIAnimationPreset, UIAnimationPreset> _registry =
            new NeoAssetRegistry<UIAnimationPreset, UIAnimationPreset>(
                key: p => p.presetName,
                project: p => p,
                registryName: "AnimationPresetRegistry");

        /// <summary> Every discovered preset asset in the project. </summary>
        public static IReadOnlyList<UIAnimationPreset> All => _registry.All;

        /// <summary> The names of every discovered preset. </summary>
        public static IEnumerable<string> Names => _registry.All.Where(p => p != null).Select(p => p.presetName);

        /// <summary> Case-sensitive (ordinal) lookup by <see cref="UIAnimationPreset.presetName"/>. </summary>
        public static bool TryGet(string presetName, out UIAnimationPreset preset) => _registry.TryGet(presetName, out preset);

        /// <summary>
        /// Registers a preset directly (e.g. a project seeding one in code, or a test probe). A same-name
        /// registration replaces in place; a same-name clash found DURING asset discovery instead follows
        /// the base registry's last-discovered-wins-with-a-warning policy (naming both asset paths).
        /// </summary>
        public static void Register(UIAnimationPreset preset) => _registry.Register(preset);

        /// <summary>
        /// Resolves a preset by name for the generator: an explicitly-wired <see cref="NeoUISettings.animationPresets"/>
        /// entry wins (so a project can override a discovered asset by name), else any discovered asset.
        /// Probes the wired database with <see cref="AnimationPresetDatabase.Contains"/> before calling
        /// <see cref="AnimationPresetDatabase.Get"/> — the plain <c>Get</c> logs a warning on a miss, and
        /// the wired database is expected to miss for every name that only resolves via discovery (the
        /// common case now that the package has moved to auto-discovery), so calling it unconditionally
        /// would spam a spurious warning on every normal resolution.
        /// </summary>
        public static UIAnimationPreset Resolve(NeoUISettings settings, string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return null;
            if (settings != null && settings.animationPresets != null && settings.animationPresets.Contains(presetName))
                return settings.animationPresets.Get(presetName);
            return TryGet(presetName, out UIAnimationPreset found) ? found : null;
        }

        /// <summary>
        /// Discovered preset full-names ("Category/Name") ordered so those whose category suits the given
        /// animator role (<see cref="NeoAnimatorRoles"/>) come first. The package's own role pickers
        /// (animator inspectors, Setup wizard, Design System Motion tab) now browse through
        /// <c>AnimationPresetBrowserPopup</c> instead; this stays public as the flat option source for a
        /// consuming project's simpler pickers. A null/empty role lists every preset alphabetically.
        /// </summary>
        public static List<string> FullNamesForRole(string role)
        {
            NeoAnimatorRole info = null;
            if (!string.IsNullOrEmpty(role)) NeoAnimatorRoles.TryGet(role, out info);

            var suggested = new List<string>();
            var others = new List<string>();
            foreach (UIAnimationPreset preset in _registry.All)
            {
                if (preset == null || string.IsNullOrEmpty(preset.presetName)) continue;
                bool isSuggested = info != null && Array.IndexOf(info.SuggestedCategories, preset.category) >= 0;
                (isSuggested ? suggested : others).Add(preset.fullName);
            }
            suggested.Sort(StringComparer.Ordinal);
            others.Sort(StringComparer.Ordinal);
            suggested.AddRange(others);
            return suggested;
        }

        /// <summary> Resolves a preset by its "Category/Name" full name (the picker option value). </summary>
        public static UIAnimationPreset GetByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            foreach (UIAnimationPreset preset in _registry.All)
                if (preset != null && preset.fullName == fullName) return preset;
            return null;
        }

        /// <summary>
        /// Marks the discovered set stale so the next access re-scans for <see cref="UIAnimationPreset"/>
        /// assets. The shared <see cref="NeoAssetRegistryPostprocessor"/> already calls this automatically
        /// on asset import/delete/move; exposed publicly too since existing call sites invalidate explicitly.
        /// </summary>
        public static void InvalidateDiscovery() => _registry.InvalidateDiscovery();

        /// <summary> Test-only: clears the registry and forces a fresh discovery on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }
}
