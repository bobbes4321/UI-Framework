using System;
using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A single catalog kind the Composer can author — the package's built-ins (settings, cheats)
    /// plus anything a consuming project registers. The Composer chrome (tree, toolbar picker,
    /// context menu) iterates <see cref="NeoCatalogKinds.All"/> instead of hardcoding the two
    /// built-in cases, so adding a third kind (debug, accessibility, key-bindings…) is one
    /// <see cref="NeoCatalogKinds.Register"/> call from a project's own assembly — no fork.
    ///
    /// <para>This is the <b>reference implementation of Pattern R</b> (the Kinds Registry) from the
    /// extensibility-seams master plan — every other registry in that family mirrors this shape.</para>
    ///
    /// <para>Phase 1 keeps storage on the existing two <see cref="UISpec"/> fields
    /// (<c>spec.settings</c> / <c>spec.cheats</c>): the <see cref="list"/> accessor points each kind
    /// at where its catalogs already live, so no model change is needed yet. A novel registered kind
    /// surfaces in the chrome but full export/generate round-trip of a novel kind awaits Phase 2's
    /// model seam.</para>
    /// </summary>
    public readonly struct CatalogKind
    {
        /// <summary> Stable id (also the <c>section</c> in <see cref="SpecPath.Catalog"/>) —
        /// <see cref="MenuCatalogSpec.SettingsKind"/> / <see cref="MenuCatalogSpec.CheatKind"/> or a
        /// project's own. </summary>
        public readonly string id;

        /// <summary> Human label shown in the chrome ("Settings", "Cheats", …). </summary>
        public readonly string label;

        /// <summary> Where catalogs of this kind live on the spec (Phase 1: the existing fields). </summary>
        public readonly Func<UISpec, List<MenuCatalogSpec>> list;

        /// <summary> Default category seeded on a freshly added catalog of this kind. </summary>
        public readonly string defaultCategory;

        /// <summary> Whether the editor exposes the <c>favourites</c> field for this kind (cheats do). </summary>
        public readonly bool showFavourites;

        public CatalogKind(string id, string label, Func<UISpec, List<MenuCatalogSpec>> list,
            string defaultCategory = null, bool showFavourites = false)
        {
            this.id = id;
            this.label = label;
            this.list = list;
            this.defaultCategory = string.IsNullOrEmpty(defaultCategory) ? label : defaultCategory;
            this.showFavourites = showFavourites;
        }
    }

    /// <summary>
    /// The single source of truth the Composer reads for the set of catalog kinds. Seeded with the
    /// package built-ins; a consuming project registers its own kind once (e.g. from an
    /// <c>[InitializeOnLoad]</c> static ctor). See <see cref="CatalogKind"/>.
    /// <para>
    /// Wave 4 Task 4.2: migrated onto <see cref="NeoKeyedRegistry{T}"/> (Pattern R's shared base). This
    /// is also where the audit's pre-made policy for this registry applies: an invalid <see cref="Register"/>
    /// call (empty id or a null <see cref="CatalogKind.list"/> accessor) now warns-and-ignores instead of
    /// throwing — a thrown exception from an <c>[InitializeOnLoad]</c> static ctor poisons the whole
    /// registering type with a <see cref="TypeInitializationException"/> (audit A6).
    /// </para>
    /// </summary>
    public static class NeoCatalogKinds
    {
        private static readonly NeoKeyedRegistry<CatalogKind> _registry = new NeoKeyedRegistry<CatalogKind>(
            k => k.id,
            builtins: Builtins,
            validate: k => k.list != null,
            registryName: "NeoCatalogKinds");

        // built-in defaults — settings is near-universal, cheats opts into the favourites bit.
        private static IEnumerable<CatalogKind> Builtins()
        {
            yield return new CatalogKind(MenuCatalogSpec.SettingsKind, "Settings", s => s.settings, "Settings");
            yield return new CatalogKind(MenuCatalogSpec.CheatKind, "Cheats", s => s.cheats, "Cheats", showFavourites: true);
        }

        /// <summary> Every registered catalog kind, in registration order (built-ins first). </summary>
        public static IReadOnlyList<CatalogKind> All => _registry.All;

        /// <summary> Resolves a kind by id. Returns false when no kind is registered for it. </summary>
        public static bool TryGet(string id, out CatalogKind kind) => _registry.TryGet(id, out kind);

        /// <summary>
        /// Registers (or replaces, by id) a catalog kind. The extension seam: a consuming project
        /// calls this once to make a new menu kind appear in the Composer's picker and tree. An entry
        /// with an empty id or a null <see cref="CatalogKind.list"/> accessor is warned-and-ignored,
        /// never thrown (see the type doc above).
        /// </summary>
        public static void Register(CatalogKind kind) => _registry.Register(kind);

        /// <summary> Test-only: clears project registrations and re-seeds the built-ins on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }
}
