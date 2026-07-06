using System;
using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One entry in the flow-node creation vocabulary — the id shown in the "Create Node/…" context
    /// menu, a factory for a bare instance of the node type, and the policy that seeds its initial
    /// output edge(s) once it's added to the graph. Wave 7 Task 7.2 (audit E2): replaces
    /// <c>FlowGraphWindow</c>'s hand-listed <c>AddCreateEntry&lt;T&gt;()</c> calls and the
    /// <c>node is UINode || node is PortalNode || …</c> type-check chain that used to hardcode
    /// default-output seeding.
    /// </summary>
    public readonly struct FlowNodeDescriptor
    {
        /// <summary> Stable registry key (the node type's name, e.g. "StartNode"). Not shown to the
        /// user — see <see cref="menuLabel"/> for that. </summary>
        public readonly string id;

        /// <summary> Text shown after "Create Node/" in the graph's right-click menu — also the base
        /// name a freshly created node of this kind gets (<see cref="FlowGraph.MakeUniqueNodeName"/>
        /// disambiguates duplicates), matching the pre-refactor behavior where the create-menu label
        /// doubled as the node's default name. </summary>
        public readonly string menuLabel;

        /// <summary> Constructs a bare instance of this node type (not yet named/positioned/added to
        /// a graph — the caller does that, mirroring <see cref="FlowGraph.AddNode{T}"/>'s shape). </summary>
        public readonly Func<FlowNode> create;

        /// <summary> Seeds the node's initial <see cref="FlowNode.outputs"/> right after it's created
        /// and added to the graph — the per-kind policy the old type-check chain hardcoded (a bare
        /// pass-through edge, a named "Next" edge, or none for non-executable nodes). </summary>
        public readonly Action<FlowNode> seedDefaultOutputs;

        /// <summary> Extra synonyms the graph's search-as-you-type node creator can match this kind
        /// against (e.g. "wait"/"delay" for a timer node) — additive, defaults to none. Reserved for
        /// a future custom-filtered searcher: the stock <c>UnityEditor.Experimental.GraphView.SearchWindow</c>
        /// only text-matches an entry's visible label, so this isn't consulted by
        /// <see cref="FlowNodeSearchWindowProvider"/> yet. </summary>
        public readonly string[] searchKeywords;

        public FlowNodeDescriptor(string id, string menuLabel, Func<FlowNode> create, Action<FlowNode> seedDefaultOutputs,
            string[] searchKeywords = null)
        {
            this.id = id;
            this.menuLabel = menuLabel;
            this.create = create;
            this.seedDefaultOutputs = seedDefaultOutputs ?? (_ => { });
            this.searchKeywords = searchKeywords ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// The single source of truth for the flow graph's node-creation vocabulary — see
    /// <see cref="FlowNodeDescriptor"/> for what an entry owns. Pattern R
    /// (<see cref="NeoKeyedRegistry{T}"/>), same shape as <see cref="NeoCatalogKinds"/> and
    /// <see cref="NeoMenuItemKinds"/>. Seeded with the 11 built-in node types found in
    /// <c>FlowGraphWindow</c>'s old hand-written creation menu; a consuming project registers its own
    /// <see cref="FlowNode"/> subtype once (e.g. from an <c>[InitializeOnLoad]</c> static ctor) to make
    /// it appear in the graph's "Create Node/…" menu without forking the package.
    /// </summary>
    public static class FlowNodeKinds
    {
        private static readonly NeoKeyedRegistry<FlowNodeDescriptor> _registry =
            new NeoKeyedRegistry<FlowNodeDescriptor>(
                d => d.id,
                builtins: Builtins,
                validate: d => d.create != null,
                registryName: "FlowNodeKinds");

        // The 11 built-ins, in the order the old hand-written create menu listed them.
        private static IEnumerable<FlowNodeDescriptor> Builtins()
        {
            yield return new FlowNodeDescriptor("StartNode", "Start", () => new StartNode(), SeedNamedNextEdge);
            yield return new FlowNodeDescriptor("UINode", "UI", () => new UINode(), SeedBareEdge);
            yield return new FlowNodeDescriptor("SignalNode", "Signal", () => new SignalNode(), SeedNamedNextEdge);
            yield return new FlowNodeDescriptor("BackButtonNode", "BackButton", () => new BackButtonNode(), SeedNamedNextEdge);
            yield return new FlowNodeDescriptor("PortalNode", "Portal", () => new PortalNode(), SeedBareEdge);
            yield return new FlowNodeDescriptor("RandomNode", "Random", () => new RandomNode(), SeedBareEdge);
            yield return new FlowNodeDescriptor("TimeScaleNode", "TimeScale", () => new TimeScaleNode(), SeedNamedNextEdge);
            yield return new FlowNodeDescriptor("ApplicationQuitNode", "ApplicationQuit", () => new ApplicationQuitNode(), SeedNone);
            // Reroute keeps its historical "Reroute" label rather than the type-derived "Pivot".
            yield return new FlowNodeDescriptor("PivotNode", "Reroute", () => new PivotNode(), SeedNamedNextEdge);
            yield return new FlowNodeDescriptor("StickyNoteNode", "StickyNote", () => new StickyNoteNode(), SeedNone);
            yield return new FlowNodeDescriptor("DebugNode", "Debug", () => new DebugNode(), SeedNamedNextEdge);
        }

        // Default-output policies — kept as named, reusable delegates rather than inline lambdas so
        // the built-ins table above reads as a plain per-kind assignment.
        private static void SeedBareEdge(FlowNode node) => node.outputs.Add(new FlowEdge());
        private static void SeedNamedNextEdge(FlowNode node) => node.outputs.Add(new FlowEdge { portName = "Next" });
        private static void SeedNone(FlowNode node) { }

        /// <summary> Every registered node kind, in registration order (built-ins first). Backs the
        /// graph's "Create Node/…" context menu. </summary>
        public static IReadOnlyList<FlowNodeDescriptor> All => _registry.All;

        /// <summary> Resolves a node kind by id. False (default) on miss. </summary>
        public static bool TryGet(string id, out FlowNodeDescriptor descriptor) => _registry.TryGet(id, out descriptor);

        /// <summary>
        /// Registers (or replaces, by id) a node kind. The extension seam: a consuming project calls
        /// this once to make its own <see cref="FlowNode"/> subtype creatable from the graph's context
        /// menu without forking the package.
        /// </summary>
        public static void Register(FlowNodeDescriptor descriptor) => _registry.Register(descriptor);

        /// <summary> Test-only: clears project registrations and re-seeds the built-ins on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }
}
