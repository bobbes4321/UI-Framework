using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Lazy-discovery registry of <see cref="UIAnimationPreset"/> assets — the designer-friendly seam that
    /// removes the one manual step the old flow demanded: dropping a preset asset anywhere under
    /// <c>Assets/</c> is now enough to reference it by name from a spec, with no need to also add it to
    /// <see cref="AnimationPresetDatabase.presets"/>. Mirrors <see cref="NeoWidgetPresets"/> /
    /// <c>ShowcaseRegistry</c> (discovery invalidated on asset import via <see cref="AnimationPresetPostprocessor"/>).
    /// <para>
    /// Editor-only: animation presets are resolved at generate time (the resolved <see cref="UIAnimation"/>
    /// is baked into the prefab), never at runtime, so discovery can live in the editor assembly.
    /// </para>
    /// </summary>
    public static class AnimationPresetRegistry
    {
        private static readonly List<UIAnimationPreset> _presets = new List<UIAnimationPreset>();
        private static bool _discovered;

        /// <summary> Every discovered preset asset in the project. </summary>
        public static IReadOnlyList<UIAnimationPreset> All { get { EnsureDiscovered(); return _presets; } }

        /// <summary> The names of every discovered preset. </summary>
        public static IEnumerable<string> Names
        {
            get { EnsureDiscovered(); return _presets.Where(p => p != null).Select(p => p.presetName); }
        }

        /// <summary> Case-sensitive (ordinal) lookup by <see cref="UIAnimationPreset.presetName"/>. </summary>
        public static bool TryGet(string presetName, out UIAnimationPreset preset)
        {
            EnsureDiscovered();
            preset = string.IsNullOrEmpty(presetName) ? null
                : _presets.FirstOrDefault(p => p != null
                    && string.Equals(p.presetName, presetName, StringComparison.Ordinal));
            return preset != null;
        }

        /// <summary>
        /// Resolves a preset by name for the generator: an explicitly-wired <see cref="NeoUISettings.animationPresets"/>
        /// entry wins (so a project can override a discovered asset by name), else any discovered asset.
        /// </summary>
        public static UIAnimationPreset Resolve(NeoUISettings settings, string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return null;
            UIAnimationPreset wired = settings != null && settings.animationPresets != null
                ? settings.animationPresets.Get(presetName) : null;
            return wired != null ? wired : (TryGet(presetName, out UIAnimationPreset found) ? found : null);
        }

        /// <summary>
        /// Discovered preset full-names ("Category/Name") ordered so those whose category suits the given
        /// animator role (<see cref="NeoAnimatorRoles"/>) come first — the SHARED option source for the
        /// per-state inspector picker, the Setup wizard and the Design System motion tab (so all three
        /// surface the same, role-relevant presets). A null/empty role lists every preset alphabetically.
        /// </summary>
        public static List<string> FullNamesForRole(string role)
        {
            NeoAnimatorRole info = null;
            if (!string.IsNullOrEmpty(role)) NeoAnimatorRoles.TryGet(role, out info);

            EnsureDiscovered();
            var suggested = new List<string>();
            var others = new List<string>();
            foreach (UIAnimationPreset preset in _presets)
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
            EnsureDiscovered();
            foreach (UIAnimationPreset preset in _presets)
                if (preset != null && preset.fullName == fullName) return preset;
            return null;
        }

        public static void InvalidateDiscovery() => _discovered = false;

        internal static void ResetForTests() { _presets.Clear(); _discovered = false; }

        private static void EnsureDiscovered()
        {
            if (_discovered) return;
            _discovered = true;
            _presets.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:UIAnimationPreset"))
            {
                var preset = AssetDatabase.LoadAssetAtPath<UIAnimationPreset>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset != null && !string.IsNullOrEmpty(preset.presetName)) _presets.Add(preset);
            }
        }
    }

    /// <summary> Invalidates animation-preset discovery on any <c>.asset</c> import, so a freshly created/
    /// edited <see cref="UIAnimationPreset"/> resolves without a domain reload. </summary>
    internal sealed class AnimationPresetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (Has(imported) || Has(deleted) || Has(moved)) AnimationPresetRegistry.InvalidateDiscovery();
        }

        private static bool Has(string[] paths)
        {
            foreach (string p in paths)
                if (p != null && p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
