using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A UI navigation graph: nodes + string-addressed edges, run by a <see cref="FlowController"/>.
    /// Nodes serialize with [SerializeReference] into this single force-text asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Flow Graph", fileName = "FlowGraph")]
    public class FlowGraph : ScriptableObject
    {
        public string graphName;
        [TextArea] public string graphDescription;

        [Tooltip("Node activated when the graph starts (falls back to the first executable node)")]
        public string startNode;

        [SerializeReference] public List<FlowNode> nodes = new List<FlowNode>();

        [Header("Editor View State")]
        public Vector2 editorPan;
        public float editorZoom = 1f;

        public FlowNode GetNode(string nodeName) =>
            nodes.FirstOrDefault(n => n != null && string.Equals(n.name, nodeName, StringComparison.Ordinal));

        public T AddNode<T>(string nodeName, Vector2 editorPosition = default) where T : FlowNode, new()
        {
            var node = new T { name = MakeUniqueNodeName(nodeName), position = editorPosition };
            nodes.Add(node);
            return node;
        }

        public bool RemoveNode(string nodeName)
        {
            FlowNode node = GetNode(nodeName);
            if (node == null) return false;
            nodes.Remove(node);
            foreach (FlowNode remaining in nodes.Where(n => n != null))
                remaining.outputs.RemoveAll(e => e.toNode == nodeName);
            return true;
        }

        public string MakeUniqueNodeName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Node";
            string candidate = baseName;
            int suffix = 1;
            while (GetNode(candidate) != null) candidate = $"{baseName} {++suffix}";
            return candidate;
        }

        public FlowNode ResolveStartNode()
        {
            FlowNode explicitStart = GetNode(startNode);
            if (explicitStart != null && explicitStart.isExecutable) return explicitStart;
            FlowNode startNodeType = nodes.FirstOrDefault(n => n is StartNode);
            if (startNodeType != null) return startNodeType;
            return nodes.FirstOrDefault(n => n != null && n.isExecutable && !n.isGlobal);
        }

        /// <summary> Lints the graph: duplicate node names, dangling edges, missing start node. </summary>
        public List<string> Validate()
        {
            var issues = new List<string>();
            var seen = new HashSet<string>();
            foreach (FlowNode node in nodes)
            {
                if (node == null)
                {
                    issues.Add("Graph contains a null node (broken SerializeReference?)");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(node.name)) issues.Add($"A {node.GetType().Name} has no name");
                else if (!seen.Add(node.name)) issues.Add($"Duplicate node name '{node.name}'");

                foreach (FlowEdge edge in node.outputs)
                {
                    if (string.IsNullOrEmpty(edge.toNode)) continue;
                    if (GetNode(edge.toNode) == null)
                        issues.Add($"Node '{node.name}' has an edge to missing node '{edge.toNode}'");
                }
            }
            if (ResolveStartNode() == null && nodes.Count > 0) issues.Add("Graph has no resolvable start node");
            return issues;
        }
    }
}
