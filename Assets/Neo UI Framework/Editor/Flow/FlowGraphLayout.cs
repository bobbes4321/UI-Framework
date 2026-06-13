using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Layered left-to-right auto-arrange for a <see cref="FlowGraph"/> (Sugiyama-lite). The whole
    /// point is to kill crossing edges: nodes are bucketed into columns by their depth from the
    /// start node, then each column is ordered to minimise crossings, then packed vertically.
    ///
    /// It is robust to the cycles UI flows are full of (every "Back" edge is a cycle): cycle-closing
    /// edges are detected with a DFS and ignored for layering, so the algorithm always terminates.
    /// Global portals get a band of their own above the main flow, and sticky notes are left exactly
    /// where the user put them.
    ///
    /// Mutates <see cref="FlowNode.position"/> only — the caller owns Undo/dirty/refresh.
    /// </summary>
    public static class FlowGraphLayout
    {
        private const float ColumnGap = 340f;   // x distance between layers
        private const float RowGap = 36f;        // y gap between stacked nodes in a column
        private const float PortalBandGap = 170f; // y gap above the main flow for the portal band
        private const int OrderingSweeps = 4;    // barycenter passes to settle crossings

        public static void Arrange(FlowGraph graph)
        {
            if (graph == null) return;

            List<FlowNode> all = graph.nodes.Where(n => n != null).ToList();
            List<FlowNode> notes = all.Where(FlowNodeStyle.IsNote).ToList();          // untouched
            List<FlowNode> portals = all.Where(n => n.isGlobal && !FlowNodeStyle.IsNote(n)).ToList();
            List<FlowNode> main = all.Where(n => !n.isGlobal && !FlowNodeStyle.IsNote(n)).ToList();

            if (main.Count == 0)
            {
                ArrangePortals(portals, 0f);
                return;
            }

            var index = new Dictionary<FlowNode, int>();
            for (int i = 0; i < main.Count; i++) index[main[i]] = i;

            FlowNode start = graph.ResolveStartNode();
            if (start == null || !index.ContainsKey(start)) start = main[0];

            int[] layer = AssignLayers(graph, main, index, start);
            List<List<int>> layers = GroupByLayer(layer, main.Count);
            OrderLayers(graph, main, index, layers);

            float minY = PlaceMain(main, layers);
            ArrangePortals(portals, minY - PortalBandGap);
        }

        // ------------------------------------------------------------------ layering

        /// <summary> Longest-path layering on the DAG that remains after dropping cycle-closing edges. </summary>
        private static int[] AssignLayers(FlowGraph graph, List<FlowNode> main,
            Dictionary<FlowNode, int> index, FlowNode start)
        {
            int n = main.Count;
            var succ = new List<int>[n];
            for (int i = 0; i < n; i++) succ[i] = new List<int>();
            foreach (FlowNode node in main)
            {
                int u = index[node];
                foreach (FlowEdge edge in node.outputs)
                {
                    if (string.IsNullOrEmpty(edge.toNode)) continue;
                    FlowNode target = graph.GetNode(edge.toNode);
                    if (target == null || !index.TryGetValue(target, out int v) || v == u) continue;
                    // pin the start node to the leftmost column: never count edges leading into it
                    if (target == start) continue;
                    if (!succ[u].Contains(v)) succ[u].Add(v);
                }
            }

            MarkBackEdges(succ, index[start], n);

            // longest-path layer via Kahn topological order over forward (non-cycle) edges only
            var indeg = new int[n];
            for (int u = 0; u < n; u++)
                foreach (int v in succ[u])
                    if (IsForward(u, v)) indeg[v]++;

            var layer = new int[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++) if (indeg[i] == 0) queue.Enqueue(i);

            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                foreach (int v in succ[u])
                {
                    if (!IsForward(u, v)) continue;
                    if (layer[v] < layer[u] + 1) layer[v] = layer[u] + 1;
                    if (--indeg[v] == 0) queue.Enqueue(v);
                }
            }
            return layer;
        }

        /// <summary>
        /// DFS from the start (then any unvisited node) classifying edges: an edge to a node still on
        /// the recursion stack closes a cycle and is recorded so layering can drop it. This is what
        /// makes the algorithm safe on the cyclic Back edges every UI flow contains.
        /// </summary>
        private static void MarkBackEdges(List<int>[] succ, int startIndex, int n)
        {
            var visited = new bool[n];
            var onStack = new bool[n];
            _backEdges = new HashSet<long>();

            DfsClassify(startIndex, succ, visited, onStack);
            for (int i = 0; i < n; i++) if (!visited[i]) DfsClassify(i, succ, visited, onStack);
        }

        // cycle-closing edges keyed by (u<<32)|v — populated by DfsClassify, read by IsForward
        private static HashSet<long> _backEdges;

        private static void DfsClassify(int u, List<int>[] succ, bool[] visited, bool[] onStack)
        {
            visited[u] = true;
            onStack[u] = true;
            foreach (int v in succ[u])
            {
                if (onStack[v]) _backEdges.Add(((long)u << 32) | (uint)v); // cycle-closing edge
                else if (!visited[v]) DfsClassify(v, succ, visited, onStack);
            }
            onStack[u] = false;
        }

        private static bool IsForward(int u, int v) =>
            !_backEdges.Contains(((long)u << 32) | (uint)v);

        // ------------------------------------------------------------------ ordering (crossing reduction)

        private static List<List<int>> GroupByLayer(int[] layer, int n)
        {
            int maxLayer = 0;
            for (int i = 0; i < n; i++) if (layer[i] > maxLayer) maxLayer = layer[i];
            var layers = new List<List<int>>();
            for (int l = 0; l <= maxLayer; l++) layers.Add(new List<int>());
            for (int i = 0; i < n; i++) layers[layer[i]].Add(i);
            return layers;
        }

        /// <summary> Barycenter sweeps: order each layer by the average position of its neighbours in the adjacent layer. </summary>
        private static void OrderLayers(FlowGraph graph, List<FlowNode> main,
            Dictionary<FlowNode, int> index, List<List<int>> layers)
        {
            // undirected adjacency by node index, for neighbour-position averaging
            var neighbors = new List<int>[main.Count];
            for (int i = 0; i < main.Count; i++) neighbors[i] = new List<int>();
            foreach (FlowNode node in main)
            {
                int u = index[node];
                foreach (FlowEdge edge in node.outputs)
                {
                    if (string.IsNullOrEmpty(edge.toNode)) continue;
                    FlowNode target = graph.GetNode(edge.toNode);
                    if (target == null || !index.TryGetValue(target, out int v) || v == u) continue;
                    neighbors[u].Add(v);
                    neighbors[v].Add(u);
                }
            }

            var pos = new int[main.Count]; // current order index within a layer
            void RecomputePositions()
            {
                foreach (List<int> lay in layers)
                    for (int i = 0; i < lay.Count; i++) pos[lay[i]] = i;
            }
            RecomputePositions();

            for (int sweep = 0; sweep < OrderingSweeps; sweep++)
            {
                bool downward = (sweep & 1) == 0;
                IEnumerable<List<int>> order = downward ? layers : Enumerable.Reverse(layers);
                foreach (List<int> lay in order)
                {
                    lay.Sort((a, b) => Barycenter(a, neighbors, pos).CompareTo(Barycenter(b, neighbors, pos)));
                    RecomputePositions();
                }
            }
        }

        private static float Barycenter(int node, List<int>[] neighbors, int[] pos)
        {
            List<int> ns = neighbors[node];
            if (ns.Count == 0) return pos[node]; // no neighbours: hold position
            float sum = 0f;
            foreach (int v in ns) sum += pos[v];
            return sum / ns.Count;
        }

        // ------------------------------------------------------------------ placement

        /// <summary> Packs each layer into a column and centres the columns on a common midline. Returns the topmost y. </summary>
        private static float PlaceMain(List<FlowNode> main, List<List<int>> layers)
        {
            // measure column heights so columns can be vertically centred against the tallest one
            var columnHeights = new float[layers.Count];
            for (int l = 0; l < layers.Count; l++)
            {
                float h = 0f;
                foreach (int i in layers[l]) h += NodeHeight(main[i]) + RowGap;
                columnHeights[l] = Mathf.Max(0f, h - RowGap);
            }
            float tallest = columnHeights.Length == 0 ? 0f : columnHeights.Max();

            float minY = 0f;
            for (int l = 0; l < layers.Count; l++)
            {
                float x = l * ColumnGap;
                float y = (tallest - columnHeights[l]) * 0.5f; // centre this column
                if (l == 0) minY = y;
                foreach (int i in layers[l])
                {
                    main[i].position = new Vector2(x, y);
                    if (y < minY) minY = y;
                    y += NodeHeight(main[i]) + RowGap;
                }
            }
            return minY;
        }

        private static void ArrangePortals(List<FlowNode> portals, float y)
        {
            for (int i = 0; i < portals.Count; i++)
                portals[i].position = new Vector2(i * ColumnGap, y);
        }

        private static float NodeHeight(FlowNode node)
        {
            if (FlowNodeStyle.IsReroute(node)) return 36f;
            int ports = Mathf.Max(1, node.outputs.Count);
            return 58f + ports * 22f; // header + input row + one row per output port + padding
        }
    }
}
