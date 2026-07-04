using System.Collections.Generic;
using System.Linq;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Pattern R registry of <see cref="Showcase"/>s — the front-door catalog of every self-contained
    /// demo. Hybrid extensibility: code-seeded built-ins (<see cref="ShowcaseRegistryDefaults"/>) PLUS a
    /// <see cref="NeoAssetRegistry{TAsset,TEntry}"/> discovery pass that folds in every
    /// <see cref="ShowcaseDefinition"/> asset in the project (a discovered definition overrides a
    /// built-in of the same id). A consuming project therefore adds a showcase by dropping one asset —
    /// no fork, no C#.
    /// <para>
    /// Editor-only and single-domain. Discovery invalidation on asset import/delete/move is handled by
    /// the shared <see cref="NeoAssetRegistryPostprocessor"/> so a freshly created/edited definition
    /// shows up without a domain reload.
    /// </para>
    /// </summary>
    public static class ShowcaseRegistry
    {
        /// <summary> Root folder every showcase lives under. Each showcase owns <c>{ShowcasesRoot}/{id}/</c>. </summary>
        public const string ShowcasesRoot = "Assets/Showcases";

        // Thin forwarders over the shared asset-backed base (Task 4.1) — no caller changes. Built-ins are
        // seeded lazily (once) from ShowcaseRegistryDefaults; discovered ShowcaseDefinition assets are
        // rescanned fresh every discovery generation (a deleted/renamed definition is evicted, not stuck).
        private static readonly NeoAssetRegistry<ShowcaseDefinition, Showcase> _registry =
            new NeoAssetRegistry<ShowcaseDefinition, Showcase>(
                key: s => s.id,
                project: def => def.ToShowcase(),
                builtins: ShowcaseRegistryDefaults.Builtins,
                registryName: "ShowcaseRegistry");

        /// <summary> All registered showcases (built-ins + discovered definitions), in registration order. </summary>
        public static IReadOnlyList<Showcase> All => _registry.All;

        /// <summary> The ids of every registered showcase. </summary>
        public static IEnumerable<string> Ids => _registry.All.Select(s => s.id);

        /// <summary> Case-sensitive (ordinal) lookup by id. Returns false (and a null showcase) when nothing matches. </summary>
        public static bool TryGet(string id, out Showcase showcase) => _registry.TryGet(id, out showcase);

        /// <summary>
        /// Registers a showcase. If one with the same id already exists (ordinal) it is replaced in
        /// place (so a discovered definition or a project can override a built-in); otherwise the
        /// showcase is appended. Null/blank-id showcases are warned-and-ignored.
        /// </summary>
        public static void Register(Showcase showcase) => _registry.Register(showcase);

        /// <summary>
        /// Test-only: removes a registered showcase by id (ordinal). Returns true if one was removed.
        /// </summary>
        internal static bool Remove(string id) => _registry.Remove(id);

        /// <summary>
        /// Test-only: restores the registry to exactly the code-seeded built-ins and forces a fresh
        /// discovery on next access — so a suite that registers/discovers probes leaves the static
        /// registry clean for sibling suites in the same domain.
        /// </summary>
        internal static void ResetForTests() => _registry.ResetForTests();

        /// <summary>
        /// Marks the discovered set stale so the next <see cref="All"/>/<see cref="Ids"/>/
        /// <see cref="TryGet"/> re-scans for <see cref="ShowcaseDefinition"/> assets. The shared
        /// <see cref="NeoAssetRegistryPostprocessor"/> already calls this automatically on asset
        /// import/delete/move; exposed publicly too since existing call sites invalidate explicitly.
        /// </summary>
        public static void InvalidateDiscovery() => _registry.InvalidateDiscovery();
    }

    /// <summary>
    /// The code-seeded built-in showcases. Kept separate from the registry so the seed list is a single
    /// obvious edit point and tests can re-seed deterministically.
    /// </summary>
    internal static class ShowcaseRegistryDefaults
    {
        /// <summary> Root every built-in spec lives under (<c>Assets/Showcases/Specs</c>). </summary>
        private const string SpecsRoot = ShowcaseRegistry.ShowcasesRoot + "/Specs";

        /// <summary> Fresh built-in showcase instances (a new list each call so the registry owns its copies). </summary>
        public static IEnumerable<Showcase> Builtins()
        {
            // --- Apps: the two migrated, spec-based applications ---------------------------------
            yield return new Showcase
            {
                id = "game-ui",
                title = "Game UI (HUD + Shop)",
                description = "A full arcade-racing front end — menus, HUD, shop, settings, cheats and " +
                              "victory screen — all driven by one JSON spec and a flow graph.",
                category = "Apps",
                specPath = SpecsRoot + "/game-ui.json",
                flowName = "GameUI",
                postBuild = ShowcaseAugment.AttachGameUIDirector,
            };
            yield return new Showcase
            {
                id = "color-a-cube",
                title = "Color-A-Cube",
                description = "A casual-game store/collection mockup rebuilt from release screenshots — " +
                              "sprite card art, tabbed navigation and flexible grids.",
                category = "Apps",
                specPath = SpecsRoot + "/color-a-cube.json",
                flowName = "ColorACube",
            };

            // --- Menus ---------------------------------------------------------------------------
            yield return new Showcase
            {
                id = "settings-menu",
                title = "Settings Menu",
                description = "A tabbed settings + cheats menu bound to settings/cheats catalogs " +
                              "(sliders, switches, dropdowns, steppers, key rebinding).",
                category = "Menus",
                specPath = SpecsRoot + "/settings-menu.json",
                flowName = "UI",
            };
            yield return new Showcase
            {
                id = "popups",
                title = "Popups",
                description = "Plain confirm cards and rich popups with custom content, a close button " +
                              "and buttons that dismiss the popup they live on.",
                category = "Menus",
                specPath = SpecsRoot + "/popups.json",
                flowName = null,
            };

            // --- Widgets: focused single-aspect demos --------------------------------------------
            yield return new Showcase
            {
                id = "buttons",
                title = "Buttons",
                description = "Every button variant × size, plus icon and badge examples.",
                category = "Widgets",
                specPath = SpecsRoot + "/buttons.json",
                flowName = null,
            };
            yield return new Showcase
            {
                id = "toggles-switches",
                title = "Toggles & Switches",
                description = "Toggles and switches, including a domain-signal-wired toggle.",
                category = "Widgets",
                specPath = SpecsRoot + "/toggles-switches.json",
                flowName = null,
            };
            yield return new Showcase
            {
                id = "tabs-panels",
                title = "Tabs & Panels",
                description = "A tab bar whose tabs show/hide sibling content panels.",
                category = "Widgets",
                specPath = SpecsRoot + "/tabs-panels.json",
                flowName = null,
            };
            yield return new Showcase
            {
                id = "presets",
                title = "Widget Presets",
                description = "The design-system \"component\" layer — reusable widget styles (Primary " +
                              "Button, Section Header, Card) referenced by name, with per-element overrides " +
                              "on top. Run Setup → Create or Repair Widget Presets first to seed the library.",
                category = "Widgets",
                specPath = SpecsRoot + "/presets.json",
                flowName = null,
            };

            // --- Effects: the juice layer (shape effects + particles) ----------------------------
            yield return new Showcase
            {
                id = "effects",
                title = "Shape Effects",
                description = "Runtime-animated, batched SDF shape effects — glow pulse, sheen sweep, " +
                              "gradient cycle — plus a batch-breaking variant-material dissolve.",
                category = "Effects",
                specPath = SpecsRoot + "/effects.json",
                flowName = null,
            };
            yield return new Showcase
            {
                id = "particles",
                title = "UI Particles",
                description = "Pooled NeoShape particle bursts driven by signals — coin bursts, confetti " +
                              "and sparkles — rendered inside the canvas batch, not Unity's ParticleSystem.",
                category = "Effects",
                specPath = SpecsRoot + "/particles.json",
                flowName = null,
            };
            yield return new Showcase
            {
                id = "animations",
                title = "Animation Presets",
                description = "The motion library — hover/press/click feel, ambient loops and the new " +
                              "Color/tint channel — assigned per widget via the spec's \"animations\" block " +
                              "and the per-state inspector picker. Enter Play mode to feel hover and press.",
                category = "Effects",
                specPath = SpecsRoot + "/animations.json",
                flowName = null,
            };
        }
    }
}
