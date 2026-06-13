using Neo.EditorUI;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Per-node-type visual identity for the flow graph: an accent color (drawn from the shared
    /// <see cref="NeoColors"/> family palette so flow nodes read as the same design language as the
    /// rest of the tooling) and a short category label. No raster icons — a colored category dot
    /// carries the type at a glance, which keeps the look crisp and avoids fragile built-in
    /// icon-name lookups.
    /// </summary>
    public static class FlowNodeStyle
    {
        /// <summary> Accent color for a node, keyed by its concrete type. </summary>
        public static Color Accent(FlowNode node)
        {
            switch (node)
            {
                case StartNode _:            return NeoColors.Add;          // green — entry point
                case UINode _:               return NeoColors.Containers;   // cyan — shows/hides views
                case SignalNode _:           return NeoColors.Signals;      // teal — sends a signal
                case PortalNode _:           return NeoColors.Flow;         // purple — global jump
                case RandomNode _:           return NeoColors.Data;         // yellow — branch
                case TimeScaleNode _:        return NeoColors.Animation;    // orange — time
                case BackButtonNode _:       return NeoColors.Interactive;  // blue — back system
                case PivotNode _:            return NeoColors.TextDim;      // neutral — reroute knot
                case StickyNoteNode _:       return NeoColors.Warning;      // amber — note
                case ApplicationQuitNode _:  return NeoColors.Remove;       // red — quit
                case DebugNode _:            return NeoColors.TextSubtle;   // grey — log
                default:                     return NeoColors.Flow;
            }
        }

        /// <summary> True for the compact pass-through routing knot (rendered minimal, no title). </summary>
        public static bool IsReroute(FlowNode node) => node is PivotNode;

        /// <summary> True for the editor-only documentation node (never executed). </summary>
        public static bool IsNote(FlowNode node) => node is StickyNoteNode;
    }
}
