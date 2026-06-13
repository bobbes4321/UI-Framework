using AlterEyes.EditorUI;
using UnityEngine;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Per-node-type visual identity for the flow graph: an accent color (drawn from the shared
    /// <see cref="AEColors"/> family palette so flow nodes read as the same design language as the
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
                case StartNode _:            return AEColors.Add;          // green — entry point
                case UINode _:               return AEColors.Containers;   // cyan — shows/hides views
                case SignalNode _:           return AEColors.Signals;      // teal — sends a signal
                case PortalNode _:           return AEColors.Flow;         // purple — global jump
                case RandomNode _:           return AEColors.Data;         // yellow — branch
                case TimeScaleNode _:        return AEColors.Animation;    // orange — time
                case BackButtonNode _:       return AEColors.Interactive;  // blue — back system
                case PivotNode _:            return AEColors.TextDim;      // neutral — reroute knot
                case StickyNoteNode _:       return AEColors.Warning;      // amber — note
                case ApplicationQuitNode _:  return AEColors.Remove;       // red — quit
                case DebugNode _:            return AEColors.TextSubtle;   // grey — log
                default:                     return AEColors.Flow;
            }
        }

        /// <summary> True for the compact pass-through routing knot (rendered minimal, no title). </summary>
        public static bool IsReroute(FlowNode node) => node is PivotNode;

        /// <summary> True for the editor-only documentation node (never executed). </summary>
        public static bool IsNote(FlowNode node) => node is StickyNoteNode;
    }
}
