using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Bridges the Composer's in-memory <see cref="FlowSpec"/> to the existing
    /// <see cref="FlowGraphWindow"/>, which edits a <see cref="FlowGraph"/> asset. The Composer keeps
    /// flow editing in that window (per the plan — "Flow lives in its own window") and only mirrors
    /// the result back into the spec.
    ///
    /// <para><see cref="ToGraph"/> builds a TRANSIENT graph (never written to disk — <see
    /// cref="HideFlags.DontSave"/>) using the same node mapping the generator uses, so the flow the
    /// human sees matches what Save will materialize. <see cref="ToFlowSpec"/> reads an edited graph
    /// back via the canonical exporter, so a flow edit round-trips into the spec losslessly.</para>
    /// </summary>
    public static class ComposerFlowBridge
    {
        /// <summary> Materializes a throwaway FlowGraph from the spec for the flow window to edit.
        /// Mirrors <c>UISpecGenerator.GenerateFlow</c>'s Start + UINode + edge construction. </summary>
        public static FlowGraph ToGraph(FlowSpec flowSpec)
        {
            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            graph.hideFlags = HideFlags.DontSave;
            graph.name = flowSpec != null && !string.IsNullOrEmpty(flowSpec.name) ? flowSpec.name : "UI";
            graph.graphName = graph.name;

            var start = graph.AddNode<StartNode>("Start", Vector2.zero);
            if (flowSpec != null && !string.IsNullOrEmpty(flowSpec.start))
                start.outputs.Add(new FlowEdge { portName = "Start", toNode = flowSpec.start, allowsBack = false });

            if (flowSpec != null)
            {
                for (int i = 0; i < flowSpec.nodes.Count; i++)
                {
                    FlowNodeSpec nodeSpec = flowSpec.nodes[i];
                    var node = graph.AddNode<UINode>(nodeSpec.name, new Vector2(280f * (i + 1), 120f * (i % 3)));
                    node.hideShownViewsOnExit = true;
                    foreach (string shown in nodeSpec.views)
                    {
                        CategoryNameId.Parse(shown, out string category, out string name);
                        node.showViews.Add(new UINode.ViewRef(category, name));
                    }
                    foreach (string hidden in nodeSpec.hide)
                    {
                        CategoryNameId.Parse(hidden, out string category, out string name);
                        node.hideViews.Add(new UINode.ViewRef(category, name));
                    }
                    foreach (FlowEdgeSpec edgeSpec in nodeSpec.next)
                        node.outputs.Add(new FlowEdge
                        {
                            portName = edgeSpec.trigger != null ? edgeSpec.trigger.ToString() : "Next",
                            toNode = edgeSpec.to,
                            allowsBack = edgeSpec.allowsBack,
                            trigger = edgeSpec.trigger
                        });
                }
            }

            graph.startNode = "Start";
            return graph;
        }

        /// <summary> Reads an edited graph back into a fresh <see cref="FlowSpec"/> via the canonical
        /// exporter (the same path <c>export</c> uses), so flow edits round-trip into the document. </summary>
        public static FlowSpec ToFlowSpec(FlowGraph graph) =>
            graph != null ? UISpecExporter.ExportFlow(graph) : null;
    }
}
