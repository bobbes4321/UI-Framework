using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Asset-backed registry of <see cref="ViewTransitionAsset"/>s — the designer seam for view
    /// transitions: drop an asset anywhere under <c>Assets/</c> and it appears in every transition
    /// picker (the flow edge drawer, the Connect-to popup, the command palette), no database entry
    /// needed. Mirrors <see cref="AnimationPresetRegistry"/> (discovery invalidated on asset
    /// import/delete/move by the shared <see cref="NeoAssetRegistryPostprocessor"/>).
    /// <para>
    /// Keyed by FULL name ("Push/SlideLeft") because that's the string flow edges store — unlike
    /// animation presets, transitions are never referenced by bare name. Editor-only: at runtime an
    /// edge resolves through <see cref="NeoUISettings.viewTransitions"/> (the explicit list the
    /// generator keeps in step with what specs use — see <see cref="EnsureRuntimeResolvable"/>).
    /// </para>
    /// </summary>
    public static class ViewTransitionRegistry
    {
        private static readonly NeoAssetRegistry<ViewTransitionAsset, ViewTransitionAsset> _registry =
            new NeoAssetRegistry<ViewTransitionAsset, ViewTransitionAsset>(
                key: t => t.fullName,
                project: t => t,
                registryName: "ViewTransitionRegistry");

        /// <summary> Every discovered transition asset in the project. </summary>
        public static IReadOnlyList<ViewTransitionAsset> All => _registry.All;

        /// <summary> Discovered full names ("Category/Name"), sorted — the shared picker option source. </summary>
        public static List<string> FullNames()
        {
            List<string> names = _registry.All
                .Where(t => t != null && !string.IsNullOrEmpty(t.transitionName))
                .Select(t => t.fullName)
                .ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary> Ordinal lookup by full name ("Push/SlideLeft"). </summary>
        public static bool TryGet(string fullName, out ViewTransitionAsset transition) =>
            _registry.TryGet(fullName, out transition);

        /// <summary>
        /// Registers a transition directly (project code seeding, test probes). Same-key replaces
        /// in place; discovery-time duplicates follow last-discovered-wins-with-a-warning.
        /// </summary>
        public static void Register(ViewTransitionAsset transition) => _registry.Register(transition);

        /// <summary>
        /// Resolves for the generator/editor: an explicitly-wired <see cref="NeoUISettings.viewTransitions"/>
        /// entry wins (a project can override a discovered asset by name), else any discovered asset.
        /// </summary>
        public static ViewTransitionAsset Resolve(NeoUISettings settings, string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            if (settings != null && settings.TryGetViewTransition(fullName, out ViewTransitionAsset wired))
                return wired;
            return TryGet(fullName, out ViewTransitionAsset found) ? found : null;
        }

        /// <summary>
        /// Guarantees the named transition resolves AT RUNTIME by appending the discovered asset to
        /// <see cref="NeoUISettings.viewTransitions"/> when it isn't already listed — called by the
        /// generator and the flow-wiring writer for every transition name an edge uses (editor
        /// discovery doesn't exist in a player build, so the explicit list is the bake step).
        /// Returns the resolved asset, or null (caller warns with its own context) on a miss.
        /// </summary>
        public static ViewTransitionAsset EnsureRuntimeResolvable(NeoUISettings settings, string fullName)
        {
            ViewTransitionAsset transition = Resolve(settings, fullName);
            if (transition == null || settings == null) return transition;
            if (!settings.TryGetViewTransition(fullName, out _))
            {
                settings.viewTransitions.Add(transition);
                UnityEditor.EditorUtility.SetDirty(settings);
            }
            return transition;
        }

        /// <summary> Marks the discovered set stale (the shared postprocessor calls this on import). </summary>
        public static void InvalidateDiscovery() => _registry.InvalidateDiscovery();

        /// <summary> Test-only: clears the registry and forces a fresh discovery on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }
}
