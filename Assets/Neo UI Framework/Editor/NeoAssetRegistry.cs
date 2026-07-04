using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Pattern R's "Shape B" base: a <see cref="NeoKeyedRegistry{T}"/> whose entries are discovered from
    /// <typeparamref name="TAsset"/> assets in the project, on top of any code-seeded built-ins and
    /// explicit <see cref="Register"/> calls. Fixes audit A5 (deleted/renamed assets were never evicted
    /// because discovery only ever folded new assets in) by making <see cref="EnsureDiscovered"/> a full
    /// REBUILD from a fresh <see cref="AssetDatabase.FindAssets(string)"/> pass every time it runs, while
    /// still preserving in-memory registrations that aren't backed by an asset at all (a project handing
    /// the registry an instance directly, or a test probe).
    /// <para>
    /// Three tiers of entries, low precedence to high: discovered assets (fully re-scanned, so a deleted
    /// asset simply doesn't reappear) are the base layer; manual registrations — everything that reached
    /// the registry through <see cref="Register"/>, including code-seeded built-ins — replace a
    /// same-key discovered entry and always survive a rebuild, because they live in their own list that
    /// <see cref="EnsureDiscovered"/> never clears.
    /// </para>
    /// </summary>
    /// <typeparam name="TAsset">The <see cref="ScriptableObject"/> asset type discovery scans for.</typeparam>
    /// <typeparam name="TEntry">The registry's entry type (identity-projected, or mapped from the asset).</typeparam>
    public class NeoAssetRegistry<TAsset, TEntry> : NeoKeyedRegistry<TEntry> where TAsset : ScriptableObject
    {
        private readonly Func<TAsset, TEntry> _project;
        private readonly string _searchFilter;

        // Registrations that must survive a discovery rebuild: code-seeded built-ins (seeded once, lazily,
        // the same way the base class would) plus every explicit Register() call. Kept separate from the
        // base class's own entry list so EnsureDiscovered can freely rebuild that list from scratch.
        private readonly List<TEntry> _manual = new List<TEntry>();
        private readonly Func<IEnumerable<TEntry>> _builtins;
        private bool _builtinsSeeded;
        private bool _discovered;

        /// <param name="key">Extracts the lookup key from an entry. Never null.</param>
        /// <param name="project">Projects a loaded <typeparamref name="TAsset"/> to an entry (identity for a registry whose entry IS the asset).</param>
        /// <param name="comparison">String comparison used for key matching. Defaults to ordinal.</param>
        /// <param name="builtins">Optional factory for code-seeded built-in entries (lazy, once).</param>
        /// <param name="validate">Optional extra guard a candidate entry must pass to be registered.</param>
        /// <param name="registryName">Name used in shared warning messages. Defaults to the entry type's name.</param>
        public NeoAssetRegistry(
            Func<TEntry, string> key,
            Func<TAsset, TEntry> project,
            StringComparison comparison = StringComparison.Ordinal,
            Func<IEnumerable<TEntry>> builtins = null,
            Func<TEntry, bool> validate = null,
            string registryName = null)
            : base(key, comparison, builtins: null, validate: validate, registryName: registryName)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _builtins = builtins;
            _searchFilter = "t:" + typeof(TAsset).Name;
            NeoAssetRegistryPostprocessor.Enroll(typeof(TAsset), InvalidateDiscovery);
        }

        /// <inheritdoc/>
        public override IReadOnlyList<TEntry> All
        {
            get { EnsureDiscovered(); return base.All; }
        }

        /// <inheritdoc/>
        public override bool TryGet(string key, out TEntry value)
        {
            EnsureDiscovered();
            return base.TryGet(key, out value);
        }

        /// <summary>
        /// Registers an entry that survives every future discovery rebuild (a manual/in-memory
        /// registration, exactly like the base class's contract) and is immediately visible even before
        /// the next discovery pass runs.
        /// </summary>
        public override void Register(TEntry value)
        {
            base.Register(value); // shared null/empty-key/validate warning + immediate visibility
            if (IsValid(value)) UpsertInto(_manual, value);
        }

        /// <inheritdoc/>
        internal override bool Remove(string key)
        {
            _manual.RemoveAll(e => string.Equals(KeyOf(e), key, Comparison));
            return base.Remove(key);
        }

        /// <inheritdoc/>
        internal override void ResetForTests()
        {
            _manual.Clear();
            _builtinsSeeded = false;
            _discovered = false;
            base.ResetForTests();
        }

        /// <summary>
        /// Marks the discovered set stale so the next <see cref="All"/>/<see cref="TryGet"/> call re-scans
        /// the project for <typeparamref name="TAsset"/> assets. Cheap — it only flips a bool; the actual
        /// rescan is lazy. Called automatically by the shared <see cref="NeoAssetRegistryPostprocessor"/>.
        /// </summary>
        public void InvalidateDiscovery() => _discovered = false;

        /// <summary>
        /// Rebuilds the merged (discovered + manual) view from a fresh asset scan, once per discovery
        /// generation. Discovered assets ALWAYS reflect what's currently on disk — a deleted or renamed
        /// asset simply doesn't reappear (fixing A5) — while every manual registration (built-ins and
        /// explicit <see cref="Register"/> calls) is re-applied on top and wins on a key clash.
        /// </summary>
        private void EnsureDiscovered()
        {
            if (_discovered) return;
            _discovered = true; // set first so a re-entrant access during projection can't recurse
            SeedBuiltins();

            var discovered = new List<TEntry>();
            var discoveredFrom = new List<string>(); // parallel to `discovered`, for the duplicate warning

            foreach (string guid in AssetDatabase.FindAssets(_searchFilter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TAsset asset = AssetDatabase.LoadAssetAtPath<TAsset>(path);
                if (asset == null) continue;
                TEntry entry = _project(asset);
                if (!IsValid(entry)) continue;

                string key = KeyOf(entry);
                int existing = discovered.FindIndex(e => string.Equals(KeyOf(e), key, Comparison));
                if (existing >= 0)
                {
                    Debug.LogWarning($"[Neo.UI] {RegistryName}: duplicate discovered key '{key}' at " +
                                      $"'{discoveredFrom[existing]}' and '{path}' — keeping the latter.");
                    discovered[existing] = entry;
                    discoveredFrom[existing] = path;
                }
                else
                {
                    discovered.Add(entry);
                    discoveredFrom.Add(path);
                }
            }

            foreach (TEntry manual in _manual) UpsertInto(discovered, manual);
            ReplaceAll(discovered);
        }

        /// <summary> Seeds the constructor's built-ins into the manual (rebuild-surviving) list, once. </summary>
        private void SeedBuiltins()
        {
            if (_builtinsSeeded) return;
            _builtinsSeeded = true;
            if (_builtins == null) return;
            foreach (TEntry item in _builtins())
            {
                if (!IsValid(item)) continue;
                UpsertInto(_manual, item);
            }
        }
    }

    /// <summary>
    /// The ONE shared <see cref="AssetPostprocessor"/> for every <see cref="NeoAssetRegistry{TAsset,TEntry}"/>
    /// instance in the domain. Each registry enrolls its asset type + <see cref="NeoAssetRegistry{TAsset,TEntry}.InvalidateDiscovery"/>
    /// callback at construction time; on any asset import/move this postprocessor invalidates only the
    /// registries whose enrolled type matches a changed path's <see cref="AssetDatabase.GetMainAssetTypeAtPath"/>
    /// — replacing the four copy-pasted postprocessors that each blanket-invalidated on ANY <c>.asset</c>
    /// import (audit A4). Deletion is the one case a changed path's type can no longer be queried (the
    /// asset is already gone), so any <c>.asset</c> deletion conservatively invalidates every enrolled
    /// registry — deletions are rare, and this is exactly what fixes the A5 "deleted asset never evicted"
    /// bug (the next discovery pass on each registry does a fresh scan and simply won't find it).
    /// </summary>
    internal sealed class NeoAssetRegistryPostprocessor : AssetPostprocessor
    {
        private static readonly List<(Type assetType, Action invalidate)> _enrolled =
            new List<(Type assetType, Action invalidate)>();

        internal static void Enroll(Type assetType, Action invalidate) => _enrolled.Add((assetType, invalidate));

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_enrolled.Count == 0) return;

            bool anyDeleted = HasAssetFile(deleted);

            HashSet<Type> changedTypes = null;
            CollectAssetTypes(imported, ref changedTypes);
            CollectAssetTypes(moved, ref changedTypes);

            if (!anyDeleted && changedTypes == null) return;

            foreach ((Type assetType, Action invalidate) in _enrolled)
            {
                if (anyDeleted || (changedTypes != null && changedTypes.Contains(assetType))) invalidate();
            }
        }

        private static bool HasAssetFile(string[] paths)
        {
            foreach (string p in paths)
                if (p != null && p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void CollectAssetTypes(string[] paths, ref HashSet<Type> into)
        {
            foreach (string p in paths)
            {
                if (p == null || !p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                Type t = AssetDatabase.GetMainAssetTypeAtPath(p);
                if (t == null) continue;
                (into ??= new HashSet<Type>()).Add(t);
            }
        }
    }
}
