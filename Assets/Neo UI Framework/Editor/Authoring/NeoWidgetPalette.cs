using System.Collections.Generic;
using Neo.EditorUI;
using UnityEngine;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// One entry in the widget palette — a kind the designer can drag onto the canvas or tree. Carries
    /// the spec <see cref="kind"/> id, the category it groups under, a human <see cref="label"/>, an
    /// optional Lucide <see cref="icon"/> name (a hint for the tile / tooltip), and a sort <see cref="order"/>
    /// within its category (lower first; ties fall back to label).
    /// </summary>
    public readonly struct PaletteEntry
    {
        /// <summary> The <see cref="ElementSpec"/> kind id this tile creates (e.g. "button", "vstack"). </summary>
        public readonly string kind;
        /// <summary> Category section the tile groups under ("Layout" | "Input" | "Display" | "Data" | "Menus" | "Custom" | …). </summary>
        public readonly string category;
        /// <summary> Human label shown on the tile. </summary>
        public readonly string label;
        /// <summary> Lucide icon name (hint for the tile / tooltip; the palette is text-first in IMGUI). </summary>
        public readonly string icon;
        /// <summary> Sort order within the category (lower first). </summary>
        public readonly int order;
        /// <summary> Optional <see cref="NeoWidgetPreset"/> name this tile applies on drop (null = a bare kind).
        /// A preset tile creates an element of <see cref="kind"/> (the preset's target kind) with its
        /// <c>preset</c> field set — so you drag "Primary Button", not generic "button". </summary>
        public readonly string preset;

        public PaletteEntry(string kind, string category, string label = null, string icon = null,
            int order = 0, string preset = null)
        {
            this.kind = kind;
            this.category = string.IsNullOrEmpty(category) ? "Custom" : category;
            this.label = string.IsNullOrEmpty(label) ? kind : label;
            this.icon = icon;
            this.order = order;
            this.preset = preset;
        }

        /// <summary> True when this tile applies a widget preset on drop (vs creating a bare kind). </summary>
        public bool IsPreset => !string.IsNullOrEmpty(preset);
    }

    /// <summary>
    /// The single source of truth for the widget palette — feeding the native
    /// <c>GameObject → Neo UI → More Widgets…</c> create menu — the package built-ins plus anything
    /// a consuming project registers. Built-ins register every <see cref="ElementSpec.Kinds"/> entry into
    /// a sensible category; any project-registered <see cref="NeoElementKinds"/> kind not already covered
    /// is auto-synthesized into a "Custom" entry using its <see cref="INeoElementKind.Accent"/>, so a
    /// project's custom kind appears in the palette with zero extra wiring. This is the key extensibility
    /// win — the palette is never a sealed list.
    ///
    /// <para>Mirrors <see cref="NeoCatalogKinds"/> / <see cref="NeoElementKinds"/> (Pattern R):
    /// <see cref="Register"/> replaces-by-kind else appends; <see cref="All"/> returns built-ins +
    /// project kinds; <see cref="Categories"/> lists the distinct categories in display order.</para>
    /// </summary>
    public static class NeoWidgetPalette
    {
        /// <summary> The <see cref="UnityEditor.DragAndDrop.SetGenericData"/> key a palette drag carries
        /// its element-kind string under, for a drag-and-drop drop handler to read. </summary>
        public const string DragKey = "Neo.Composer.PaletteKind";

        /// <summary> The generic-data key a preset tile carries its <see cref="NeoWidgetPreset"/> name under
        /// (alongside <see cref="DragKey"/> = the preset's target kind), for a drop handler to read and
        /// set the created element's <c>preset</c> field from. Null for a bare-kind tile. </summary>
        public const string PresetDragKey = "Neo.Composer.PalettePreset";

        /// <summary> The generic-data key a dragged <see cref="NeoLayoutTemplates"/> scaffold carries its
        /// template id under (INSTEAD of <see cref="DragKey"/> — a template inserts a whole element tree,
        /// not one kind), for a drop handler to route into
        /// <see cref="NeoSceneAuthoring.InsertTemplate"/>. </summary>
        public const string TemplateDragKey = "Neo.Composer.PaletteTemplate";

        /// <summary> The category preset tiles group under (the design-system "component" layer). </summary>
        public const string ComponentsCategory = "Components";

        // Display order for the canonical built-in categories; unknown categories sort after, alpha.
        // Components (the preset layer) lead — they're the highest-level thing a designer reaches for.
        private static readonly string[] CategoryOrder =
            { "Components", "Layout", "Input", "Display", "Data", "Menus", "Custom" };

        // Pattern R (Wave 4 Task 4.2): explicit registrations (built-ins + project Register calls) live
        // on the shared keyed-registry base instead of a hand-rolled replace-by-kind loop.
        private static readonly NeoKeyedRegistry<PaletteEntry> _registry = new NeoKeyedRegistry<PaletteEntry>(
            e => e.kind,
            builtins: Builtins,
            registryName: "NeoWidgetPalette");

        // ---- composed-`All` cache (audit registry bug 7: a fresh sorted list — with a Humanize
        // StringBuilder alloc per auto-included custom kind — was rebuilt on every single access).
        // NeoElementKinds (Wave 4 Task 4.4) and NeoWidgetPresets (Wave 4 Task 4.1) are now themselves
        // NeoKeyedRegistry/NeoAssetRegistry-backed, so all three sources hand back a cached snapshot
        // whose reference only changes on a real mutation (see NeoKeyedRegistryTests.All_IsACachedSnapshot_
        // ThatRebuildsOnlyOnMutation) — comparing the three snapshot references is therefore a true O(1)
        // "did anything change" check, with no re-scan needed on the unchanged fast path.
        private static List<PaletteEntry> _cachedAll;
        private static IReadOnlyList<PaletteEntry> _cachedRegistrySnapshot;
        private static IReadOnlyList<INeoElementKind> _cachedKindsSnapshot;
        private static IReadOnlyList<NeoWidgetPreset> _cachedPresetsSnapshot;
        private static bool _cacheValid;

        private static IEnumerable<PaletteEntry> Builtins()
        {
            // Layout containers / spacing.
            yield return new PaletteEntry("vstack", "Layout", "Vertical Stack", "layout", 0);
            yield return new PaletteEntry("hstack", "Layout", "Horizontal Stack", "layout", 1);
            yield return new PaletteEntry("grid", "Layout", "Grid", "grid", 2);
            yield return new PaletteEntry("scroll", "Layout", "Scroll", "scroll-text", 3);
            yield return new PaletteEntry("panel", "Layout", "Panel", "square", 4);
            yield return new PaletteEntry("overlay", "Layout", "Overlay", "layers", 5);
            yield return new PaletteEntry("safearea", "Layout", "Safe Area", "smartphone", 6);
            yield return new PaletteEntry("spacer", "Layout", "Spacer", "move-horizontal", 7);

            // Interactive input widgets.
            yield return new PaletteEntry("button", "Input", "Button", "square", 0);
            yield return new PaletteEntry("toggle", "Input", "Toggle", "toggle-left", 1);
            yield return new PaletteEntry("switch", "Input", "Switch", "toggle-right", 2);
            yield return new PaletteEntry("slider", "Input", "Slider", "sliders-horizontal", 3);
            yield return new PaletteEntry("stepper", "Input", "Stepper", "plus", 4);
            yield return new PaletteEntry("input", "Input", "Input Field", "text-cursor-input", 5);
            yield return new PaletteEntry("dropdown", "Input", "Dropdown", "chevron-down", 6);
            yield return new PaletteEntry("tab", "Input", "Tab", "square", 7);
            yield return new PaletteEntry("tabbar", "Input", "Tab Bar", "layout", 8);

            // Static / display widgets.
            yield return new PaletteEntry("text", "Display", "Text", "type", 0);
            yield return new PaletteEntry("image", "Display", "Image", "image", 1);
            yield return new PaletteEntry("icon", "Display", "Icon", "star", 2);
            yield return new PaletteEntry("shape", "Display", "Shape", "circle", 3);
            yield return new PaletteEntry("progress", "Display", "Progress", "loader", 4);
            yield return new PaletteEntry("counter", "Display", "Counter", "hash", 5);

            // Data-bound.
            yield return new PaletteEntry("list", "Data", "List", "list", 0);

            // Menu catalogs (settings / cheats are element kinds too).
            yield return new PaletteEntry("settings", "Menus", "Settings Menu", "settings", 0);
            yield return new PaletteEntry("cheats", "Menus", "Cheats Menu", "bug", 1);
        }

        /// <summary>
        /// Every palette entry: the explicit registrations (built-ins + project <see cref="Register"/>
        /// calls) plus an auto-synthesized "Custom" entry for every <see cref="NeoElementKinds"/> kind not
        /// already covered. Cached — rebuilt only when the explicit registrations, the project element
        /// kinds, or the discovered presets actually changed (see the cache fields above). Sorted by
        /// category display order, then per-entry <see cref="PaletteEntry.order"/>, then label.
        /// </summary>
        public static IReadOnlyList<PaletteEntry> All
        {
            get
            {
                IReadOnlyList<PaletteEntry> registrySnapshot = _registry.All;
                IReadOnlyList<INeoElementKind> kindsSnapshot = NeoElementKinds.All;
                IReadOnlyList<NeoWidgetPreset> presetsSnapshot = NeoWidgetPresets.All;

                if (_cacheValid
                    && ReferenceEquals(registrySnapshot, _cachedRegistrySnapshot)
                    && ReferenceEquals(kindsSnapshot, _cachedKindsSnapshot)
                    && ReferenceEquals(presetsSnapshot, _cachedPresetsSnapshot))
                {
                    return _cachedAll;
                }

                var list = new List<PaletteEntry>(registrySnapshot);

                // auto-include project kinds not already registered (the extensibility win)
                foreach (INeoElementKind kind in kindsSnapshot)
                {
                    if (kind == null || string.IsNullOrEmpty(kind.Kind)) continue;
                    if (HasKind(list, kind.Kind)) continue;
                    list.Add(new PaletteEntry(kind.Kind, "Custom", Humanize(kind.Kind)));
                }

                // The design-system "component" layer: one tile per discovered NeoWidgetPreset, so a
                // designer drags "Primary Button" (kind = the preset's target kind, preset field set) rather
                // than a bare "button". Discovery is lazy + cached — All is fetched when the pane opens, not
                // per OnGUI — so the registry scan here is cheap. A project preset asset appears for free.
                int presetOrder = 0;
                foreach (NeoWidgetPreset preset in presetsSnapshot)
                {
                    if (preset == null || string.IsNullOrEmpty(preset.presetName)
                        || string.IsNullOrEmpty(preset.targetKind)) continue;
                    list.Add(new PaletteEntry(preset.targetKind, ComponentsCategory,
                        preset.presetName, preset.icon, presetOrder++, preset.presetName));
                }

                list.Sort(Compare);

                _cachedAll = list;
                _cachedRegistrySnapshot = registrySnapshot;
                _cachedKindsSnapshot = kindsSnapshot;
                _cachedPresetsSnapshot = presetsSnapshot;
                _cacheValid = true;
                return list;
            }
        }

        /// <summary> The distinct categories present in <see cref="All"/>, in display order. </summary>
        public static IEnumerable<string> Categories
        {
            get
            {
                var seen = new List<string>();
                foreach (PaletteEntry e in All)
                    if (!seen.Contains(e.category)) seen.Add(e.category);
                return seen;
            }
        }

        /// <summary>
        /// Registers (or replaces, by <see cref="PaletteEntry.kind"/>) a palette entry. The extension
        /// seam: a project calls this once (e.g. from an <c>[InitializeOnLoad]</c> static ctor) to slot a
        /// kind into a specific category/order — otherwise an unregistered project kind still appears for
        /// free in "Custom" via <see cref="All"/>.
        /// </summary>
        public static void Register(PaletteEntry entry) => _registry.Register(entry);

        /// <summary> Resolves the accent color for a palette entry — a project kind's own accent, else the
        /// category default (so a tile reads the same as its tree row). </summary>
        public static Color AccentFor(PaletteEntry entry)
        {
            if (entry.IsPreset) return NeoColors.Theming; // the design-system "component" layer (pink)
            if (NeoElementKinds.TryGet(entry.kind, out INeoElementKind ext)) return ext.Accent;
            switch (entry.category)
            {
                case "Input": return NeoColors.Interactive;
                case "Layout": return NeoColors.Containers;
                case "Display": return NeoColors.Rendering;
                case "Data":
                case "Menus": return NeoColors.Data;
                default: return NeoColors.TextSubtle;
            }
        }

        /// <summary> Test/seam hook: clears project <see cref="Register"/> additions, re-seeds the
        /// built-ins, and invalidates the composed-<see cref="All"/> cache (static state survives a test
        /// run). </summary>
        internal static void ResetForTests()
        {
            _registry.ResetForTests();
            _cacheValid = false;
            _cachedAll = null;
            _cachedRegistrySnapshot = null;
            _cachedKindsSnapshot = null;
            _cachedPresetsSnapshot = null;
        }

        private static bool HasKind(List<PaletteEntry> list, string kind)
        {
            foreach (PaletteEntry e in list) if (e.kind == kind) return true;
            return false;
        }

        private static int Compare(PaletteEntry a, PaletteEntry b)
        {
            int ca = CategoryRank(a.category), cb = CategoryRank(b.category);
            if (ca != cb) return ca.CompareTo(cb);
            int byCat = string.CompareOrdinal(a.category, b.category);
            if (byCat != 0) return byCat;
            if (a.order != b.order) return a.order.CompareTo(b.order);
            return string.CompareOrdinal(a.label, b.label);
        }

        private static int CategoryRank(string category)
        {
            int idx = System.Array.IndexOf(CategoryOrder, category);
            return idx < 0 ? CategoryOrder.Length : idx;
        }

        // "myCustomKind" → "My Custom Kind" for a tidy default label on an auto-included project kind.
        private static string Humanize(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return kind;
            var sb = new System.Text.StringBuilder(kind.Length + 4);
            for (int i = 0; i < kind.Length; i++)
            {
                char c = kind[i];
                if (i == 0) sb.Append(char.ToUpperInvariant(c));
                else { if (char.IsUpper(c)) sb.Append(' '); sb.Append(c); }
            }
            return sb.ToString();
        }
    }
}
