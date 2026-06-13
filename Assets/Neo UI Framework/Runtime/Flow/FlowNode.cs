using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// An outgoing connection from a node: the target node (by name — flow graphs are fully
    /// string-addressed), an optional trigger that fires it, a back-allowed flag and a weight
    /// (used by RandomNode).
    /// </summary>
    [Serializable]
    public class FlowEdge
    {
        [Tooltip("Label shown on the node's output port")]
        public string portName = "Next";

        [Tooltip("Name of the node this edge leads to")]
        public string toNode;

        [Tooltip("Whether GoBack() may return across this edge")]
        public bool allowsBack = true;

        [Tooltip("Condition advancing across this edge (UINode / PortalNode)")]
        public FlowTrigger trigger = new FlowTrigger();

        [Tooltip("Relative weight for RandomNode output selection")]
        [Min(0)] public int weight = 100;
    }

    /// <summary>
    /// Base flow node. Nodes are plain serializable classes stored with [SerializeReference] in the
    /// graph asset — flat, force-text, agent-readable; no nested sub-assets.
    /// </summary>
    [Serializable]
    public abstract class FlowNode
    {
        [Tooltip("Unique node name within the graph — the string everything addresses it by")]
        public string name;

        [Tooltip("Editor graph position")]
        public Vector2 position;

        public List<FlowEdge> outputs = new List<FlowEdge>();

        /// <summary> Global nodes (portals) listen while the graph runs, not only while active. </summary>
        public virtual bool isGlobal => false;

        /// <summary> Whether this node can be entered at runtime (sticky notes can't). </summary>
        public virtual bool isExecutable => true;

        public virtual void OnEnter(FlowController controller, FlowEdge viaEdge) { }

        public virtual void OnExit(FlowController controller) { }

        public virtual void Tick(FlowController controller, float deltaTime) { }

        /// <summary> Called when the graph starts/stops for global nodes. </summary>
        public virtual void OnGraphStarted(FlowController controller) { }
        public virtual void OnGraphStopped(FlowController controller) { }

        public FlowEdge GetFirstConnectedOutput() =>
            outputs.Find(e => !string.IsNullOrEmpty(e.toNode));
    }
}
