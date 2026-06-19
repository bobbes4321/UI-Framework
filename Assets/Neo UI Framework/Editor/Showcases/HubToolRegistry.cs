using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Pattern R registry of <see cref="HubTool"/>s — the catalog behind the Hub's Tools tab. Built-in
    /// entries (<see cref="HubToolRegistryDefaults"/>) cover every Neo UI window and menu action; a
    /// consuming project folds in its own tools with a single <see cref="Register"/> call, no fork.
    /// <para>
    /// Mirrors <see cref="ShowcaseRegistry"/> exactly: a static list seeded with built-ins, replace-by-id
    /// registration, an ordinal lookup. Editor-only and single-domain, so a static list suffices.
    /// </para>
    /// </summary>
    public static class HubToolRegistry
    {
        private static readonly List<HubTool> _tools = new List<HubTool>(HubToolRegistryDefaults.Builtins());

        /// <summary> All registered tools (built-ins + project additions), in registration order. </summary>
        public static IReadOnlyList<HubTool> All => _tools;

        /// <summary> Case-sensitive (ordinal) lookup by id. False (and null) when nothing matches. </summary>
        public static bool TryGet(string id, out HubTool tool)
        {
            tool = _tools.FirstOrDefault(t => string.Equals(t.id, id, StringComparison.Ordinal));
            return tool != null;
        }

        /// <summary>
        /// Registers a tool. Replaces in place when one with the same id (ordinal) already exists — so a
        /// project can override a built-in — otherwise appends. Null / id-less / invoke-less tools are
        /// ignored with a warning (a launcher button that does nothing is the bug this guards against).
        /// </summary>
        public static void Register(HubTool tool)
        {
            if (tool == null || string.IsNullOrEmpty(tool.id) || tool.invoke == null)
            {
                UnityEngine.Debug.LogWarning("[Neo.UI] HubToolRegistry.Register ignored a null / id-less / invoke-less tool");
                return;
            }
            int existing = _tools.FindIndex(t => string.Equals(t.id, tool.id, StringComparison.Ordinal));
            if (existing >= 0) _tools[existing] = tool;
            else _tools.Add(tool);
        }

        /// <summary> Test-only: removes a registered tool by id (ordinal). True if one was removed. </summary>
        internal static bool Remove(string id) =>
            _tools.RemoveAll(t => string.Equals(t.id, id, StringComparison.Ordinal)) > 0;

        /// <summary> Test-only: restores the registry to exactly the code-seeded built-ins. </summary>
        internal static void ResetForTests()
        {
            _tools.Clear();
            _tools.AddRange(HubToolRegistryDefaults.Builtins());
        }
    }

    /// <summary>
    /// The code-seeded built-in Hub tools — one obvious edit point, kept in sync with the
    /// <c>[MenuItem("Tools/Neo UI/…")]</c> tree. Windows open through their own public Open() methods;
    /// menu-action tools route through <see cref="EditorApplication.ExecuteMenuItem"/> with the EXACT
    /// current menu paths so they never drift from the attribute that owns them.
    /// </summary>
    internal static class HubToolRegistryDefaults
    {
        // category buckets — order here drives the section order in the launcher
        public const string Author = "Author";
        public const string Setup = "Setup";
        public const string Data = "Data";
        public const string Advanced = "Advanced";

        public static IEnumerable<HubTool> Builtins()
        {
            // --- Author: the primary authoring / navigation windows ------------------------------
            yield return new HubTool
            {
                id = "composer", label = "Composer", category = Author,
                tooltip = "From-scratch, no-agent UI authoring surface — edits a spec in memory and " +
                          "regenerates the prefab as a live preview.",
                invoke = Composer.NeoComposerWindow.Open,
            };
            yield return new HubTool
            {
                id = "flow-graph", label = "Flow Graph Editor", category = Author,
                tooltip = "Visual flow graph editor — wire views, popups and triggers into app flow.",
                invoke = FlowGraphWindow.Open,
            };

            // --- Data: id databases ---------------------------------------------------------------
            yield return new HubTool
            {
                id = "id-database", label = "ID Database Manager", category = Data,
                tooltip = "Browse and edit the category/name id databases (views, signals, settings, …).",
                invoke = IdDatabaseManagerWindow.Open,
            };

            // --- Setup: bootstrap / one-shot creation wizards (Tools/Neo UI/Setup/*) -------------
            yield return Menu("setup-wizard", "New Project Setup…", Setup,
                "Guided first run: pick a theme bundle and what to include, then set up the whole " +
                "project in one click (orchestrates the create-or-repair steps below).",
                "Tools/Neo UI/Setup/New Project Setup…");
            yield return Menu("setup-settings", "Create or Repair Settings", Setup,
                "Create the single NeoUISettings asset and its id databases, or repair a broken one.",
                "Tools/Neo UI/Setup/Create or Repair Settings");
            yield return Menu("setup-starter", "Create or Repair Starter Kit", Setup,
                "Create the themed widget prefab library + Dark/Light palette + type scale.",
                "Tools/Neo UI/Setup/Create or Repair Starter Kit");
            yield return Menu("setup-fonts", "Create or Repair Fonts", Setup,
                "Regenerate the Inter + Lucide icon TMP SDF font assets and wire the icon font.",
                "Tools/Neo UI/Setup/Create or Repair Fonts");
            yield return Menu("setup-animations", "Create or Repair Animation Library", Setup,
                "Seed the curated default animation presets (fades, four-way slides, scale-pop, button " +
                "press, loop pulse) — auto-discovered, referenced by name from specs.",
                "Tools/Neo UI/Setup/Create or Repair Animation Library");
            yield return Menu("setup-menu-lib", "Create or Repair Menu Widget Library", Setup,
                "Create or repair the menu widget prefab library.",
                "Tools/Neo UI/Setup/Create or Repair Menu Widget Library");
            yield return Menu("setup-theme-bundle", "Apply Theme Bundle…", Setup,
                "Apply a curated theme bundle (CleanSlate / NeonArcade / SoftFantasy) — full token/" +
                "type/shape/motion system.",
                "Tools/Neo UI/Setup/Apply Theme Bundle…");
            yield return Menu("setup-effects", "Create or Repair Effect Assets", Setup,
                "Bake the procedural noise/ramp textures + the dissolve material and ShapeEffectDefinition used by Tier-2 shape effects.",
                "Tools/Neo UI/Setup/Create or Repair Effect Assets");

            // --- Advanced: spec / round-trip / agent power tools (Tools/Neo UI/Advanced/*) -------
            yield return Menu("adv-generate", "Generate From Spec…", Advanced,
                "Raw generate from a spec JSON (unsafe primitive — prefer Sync). First generation / " +
                "clean rebuilds.",
                "Tools/Neo UI/Advanced/Generate From Spec…");
            yield return Menu("adv-export", "Export Spec…", Advanced,
                "Export the live project back out to a spec JSON.",
                "Tools/Neo UI/Advanced/Export Spec…");
            yield return Menu("adv-validate", "Validate", Advanced,
                "Run hard validation + soft design / off-spec lint on the generated UI.",
                "Tools/Neo UI/Advanced/Validate");
            yield return Menu("adv-sync", "Sync With Spec…", Advanced,
                "Safe-regenerate: merge an incoming spec, surfacing conflicts / off-spec edits.",
                "Tools/Neo UI/Advanced/Sync With Spec…");
            yield return Menu("adv-capture", "Capture My Edits", Advanced,
                "Fold the current project into the baseline (no regenerate).",
                "Tools/Neo UI/Advanced/Capture My Edits");
            yield return Menu("adv-drift", "Check For Drift", Advanced,
                "Diff the live project against the baseline — green round-trips, red would be lost.",
                "Tools/Neo UI/Advanced/Check For Drift");
            yield return Menu("adv-binding", "Generate Binding Stub", Advanced,
                "Derive the binding contract from the spec and emit a C# partial-class wiring stub.",
                "Tools/Neo UI/Advanced/Generate Binding Stub");
            yield return Menu("adv-migrate", "Migrate Spec To Layout Model", Advanced,
                "Rewrite a legacy anchor/position spec to the Figma-style layout constraint model.",
                "Tools/Neo UI/Advanced/Migrate Spec To Layout Model");
            yield return Menu("adv-spec-ref", "Generate Spec Reference", Advanced,
                "Write the spec reference doc + JSON schema.",
                "Tools/Neo UI/Advanced/Generate Spec Reference");
            yield return Menu("adv-screenshot", "Screenshot Selected Prefab", Advanced,
                "Render the selected view/popup prefab to a PNG.",
                "Tools/Neo UI/Advanced/Screenshot Selected Prefab");
            yield return Menu("adv-agent-bridge", "Agent Bridge", Advanced,
                "Toggle the file-based agent request/result bridge.",
                "Tools/Neo UI/Advanced/Agent Bridge");
        }

        /// <summary> A tool that fires an editor menu item by its exact path. </summary>
        private static HubTool Menu(string id, string label, string category, string tooltip, string menuPath) =>
            new HubTool
            {
                id = id, label = label, category = category, tooltip = tooltip,
                invoke = () =>
                {
                    if (!EditorApplication.ExecuteMenuItem(menuPath))
                        UnityEngine.Debug.LogWarning($"[Neo.UI] Hub couldn't run menu item '{menuPath}'.");
                },
            };
    }
}
