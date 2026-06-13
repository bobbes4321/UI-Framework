using System;
using System.Collections.Generic;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// A single catalog kind the Composer can author — the package's built-ins (settings, cheats)
    /// plus anything a consuming project registers. The Composer chrome (tree, toolbar picker,
    /// context menu) iterates <see cref="ComposerCatalogKinds.All"/> instead of hardcoding the two
    /// built-in cases, so adding a third kind (debug, accessibility, key-bindings…) is one
    /// <see cref="ComposerCatalogKinds.Register"/> call from a project's own assembly — no fork.
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
    /// </summary>
    public static class ComposerCatalogKinds
    {
        // built-in defaults — settings is near-universal, cheats opts into the favourites bit.
        private static readonly List<CatalogKind> _kinds = new List<CatalogKind>
        {
            new CatalogKind(MenuCatalogSpec.SettingsKind, "Settings", s => s.settings, "Settings"),
            new CatalogKind(MenuCatalogSpec.CheatKind,    "Cheats",   s => s.cheats,   "Cheats", showFavourites: true),
        };

        /// <summary> Every registered catalog kind, in registration order (built-ins first). </summary>
        public static IReadOnlyList<CatalogKind> All => _kinds;

        /// <summary> Resolves a kind by id. Returns false when no kind is registered for it. </summary>
        public static bool TryGet(string id, out CatalogKind kind)
        {
            for (int i = 0; i < _kinds.Count; i++)
                if (_kinds[i].id == id) { kind = _kinds[i]; return true; }
            kind = default;
            return false;
        }

        /// <summary>
        /// Registers (or replaces, by id) a catalog kind. The extension seam: a consuming project
        /// calls this once to make a new menu kind appear in the Composer's picker and tree.
        /// </summary>
        public static void Register(CatalogKind kind)
        {
            if (string.IsNullOrEmpty(kind.id) || kind.list == null)
                throw new ArgumentException("A CatalogKind needs a non-empty id and a list accessor.", nameof(kind));
            for (int i = 0; i < _kinds.Count; i++)
                if (_kinds[i].id == kind.id) { _kinds[i] = kind; return; }
            _kinds.Add(kind);
        }
    }
}
