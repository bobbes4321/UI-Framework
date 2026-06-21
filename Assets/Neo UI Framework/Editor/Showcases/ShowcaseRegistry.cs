using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Pattern R registry of <see cref="Showcase"/>s — the front-door catalog of every self-contained
    /// demo. Hybrid extensibility: code-seeded built-ins (<see cref="ShowcaseRegistryDefaults"/>) PLUS a
    /// lazy <see cref="EnsureDiscovered"/> pass that folds in every <see cref="ShowcaseDefinition"/>
    /// asset in the project (a discovered definition overrides a built-in of the same id). A consuming
    /// project therefore adds a showcase by dropping one asset — no fork, no C#.
    /// <para>
    /// Editor-only and single-domain, so a static list is sufficient. Discovery is invalidated on
    /// asset import (see <see cref="ShowcaseDefinitionPostprocessor"/>) so a freshly created/edited
    /// definition shows up without a domain reload.
    /// </para>
    /// </summary>
    public static class ShowcaseRegistry
    {
        /// <summary> Root folder every showcase lives under. Each showcase owns <c>{ShowcasesRoot}/{id}/</c>. </summary>
        public const string ShowcasesRoot = "Assets/Showcases";

        private static readonly List<Showcase> _showcases = new List<Showcase>(ShowcaseRegistryDefaults.Builtins());

        private static bool _discovered;

        /// <summary> All registered showcases (built-ins + discovered definitions), in registration order. </summary>
        public static IReadOnlyList<Showcase> All
        {
            get { EnsureDiscovered(); return _showcases; }
        }

        /// <summary> The ids of every registered showcase. </summary>
        public static IEnumerable<string> Ids
        {
            get { EnsureDiscovered(); return _showcases.Select(s => s.id); }
        }

        /// <summary> Case-sensitive (ordinal) lookup by id. Returns false (and a null showcase) when nothing matches. </summary>
        public static bool TryGet(string id, out Showcase showcase)
        {
            EnsureDiscovered();
            showcase = _showcases.FirstOrDefault(s => string.Equals(s.id, id, StringComparison.Ordinal));
            return showcase != null;
        }

        /// <summary>
        /// Registers a showcase. If one with the same id already exists (ordinal) it is replaced in
        /// place (so a discovered definition or a project can override a built-in); otherwise the
        /// showcase is appended. Null/blank-id showcases are ignored.
        /// </summary>
        public static void Register(Showcase showcase)
        {
            if (showcase == null || string.IsNullOrEmpty(showcase.id))
            {
                UnityEngine.Debug.LogWarning("[Neo.UI] ShowcaseRegistry.Register ignored a null/id-less showcase");
                return;
            }
            int existing = _showcases.FindIndex(s => string.Equals(s.id, showcase.id, StringComparison.Ordinal));
            if (existing >= 0) _showcases[existing] = showcase;
            else _showcases.Add(showcase);
        }

        /// <summary>
        /// Test-only: removes a registered showcase by id (ordinal). Returns true if one was removed.
        /// </summary>
        internal static bool Remove(string id) =>
            _showcases.RemoveAll(s => string.Equals(s.id, id, StringComparison.Ordinal)) > 0;

        /// <summary>
        /// Test-only: restores the registry to exactly the code-seeded built-ins and forces a fresh
        /// discovery on next access — so a suite that registers/discovers probes leaves the static
        /// registry clean for sibling suites in the same domain.
        /// </summary>
        internal static void ResetForTests()
        {
            _showcases.Clear();
            _showcases.AddRange(ShowcaseRegistryDefaults.Builtins());
            _discovered = false;
        }

        /// <summary>
        /// Marks the discovered set stale so the next <see cref="All"/>/<see cref="Ids"/>/
        /// <see cref="TryGet"/> re-scans for <see cref="ShowcaseDefinition"/> assets. Called by the
        /// asset post-processor on any <c>.asset</c> import.
        /// </summary>
        public static void InvalidateDiscovery() => _discovered = false;

        /// <summary>
        /// Lazily folds every <see cref="ShowcaseDefinition"/> asset in the project into the registry,
        /// once per discovery generation. A discovered definition overrides a built-in of the same id
        /// (it routes through <see cref="Register"/>'s replace-by-id). Cheap and idempotent.
        /// </summary>
        private static void EnsureDiscovered()
        {
            if (_discovered) return;
            _discovered = true; // set first so a re-entrant Register call can't recurse into discovery
            foreach (string guid in AssetDatabase.FindAssets("t:ShowcaseDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ShowcaseDefinition>(path);
                if (def == null) continue;
                Showcase showcase = def.ToShowcase();
                if (showcase != null && !string.IsNullOrEmpty(showcase.id)) Register(showcase);
            }
        }
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

    /// <summary>
    /// Invalidates showcase discovery whenever a <c>.asset</c> is imported/deleted/moved, so a freshly
    /// created or edited <see cref="ShowcaseDefinition"/> surfaces in the registry without a domain
    /// reload. Kept deliberately cheap — it only flips a bool; the actual rescan is lazy.
    /// </summary>
    internal sealed class ShowcaseDefinitionPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (HasAsset(imported) || HasAsset(deleted) || HasAsset(moved))
                ShowcaseRegistry.InvalidateDiscovery();
        }

        private static bool HasAsset(string[] paths)
        {
            foreach (string p in paths)
                if (p != null && p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
