using System.Collections.Generic;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Pure tree drag-to-reparent math, kept free of IMGUI so it unit-tests in isolation
    /// (<c>TreeDragTests</c>). The <see cref="SpecTreeView"/> resolves a hovered row + drop zone into a
    /// (source list, element, destination list, insert index) and calls <see cref="Move"/>; the cycle
    /// guard (<see cref="IsAncestorOrSelf"/>) stops a node being dropped into its own subtree.
    ///
    /// <para>Mirrors the canvas reparent semantics in <c>ComposerCanvas</c> (drop-onto-a-container vs.
    /// reorder-within-a-list) so the two authoring surfaces move elements the same way — the spec stays
    /// the single source of truth either way.</para>
    /// </summary>
    internal static class TreeDrag
    {
        /// <summary> Where in a target row the drop landed: above it (insert before as a sibling),
        /// on it (reparent INTO, when the target can host children), or below it (insert after). </summary>
        public enum Zone { Before, Into, After }

        /// <summary> The zone for a pointer at normalized vertical position <paramref name="t"/> (0 = row
        /// top, 1 = row bottom). The middle band is the "into" target; the top/bottom bands insert as a
        /// sibling — the standard hierarchy drag affordance. </summary>
        public static Zone ZoneFor(float t) => t < 0.3f ? Zone.Before : t > 0.7f ? Zone.After : Zone.Into;

        /// <summary> True when <paramref name="node"/> is <paramref name="target"/> or an ancestor of it —
        /// i.e. dropping <paramref name="target"/>'s location into <paramref name="node"/>'s subtree would
        /// create a cycle. </summary>
        public static bool IsAncestorOrSelf(ElementSpec node, ElementSpec target)
        {
            if (node == null) return false;
            if (ReferenceEquals(node, target)) return true;
            if (node.children == null) return false;
            foreach (ElementSpec child in node.children)
                if (IsAncestorOrSelf(child, target)) return true;
            return false;
        }

        /// <summary>
        /// Moves <paramref name="element"/> out of <paramref name="from"/> and into <paramref name="to"/>
        /// at <paramref name="insertIndex"/>, correcting the index when both are the same list (the removal
        /// shifts later slots down by one). Returns the final index it landed at, or -1 when the element
        /// wasn't in <paramref name="from"/>.
        /// </summary>
        public static int Move(List<ElementSpec> from, ElementSpec element, List<ElementSpec> to, int insertIndex)
        {
            if (from == null || to == null || element == null) return -1;
            int old = from.IndexOf(element);
            if (old < 0) return -1;
            from.RemoveAt(old);
            if (ReferenceEquals(from, to) && old < insertIndex) insertIndex--;
            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > to.Count) insertIndex = to.Count;
            to.Insert(insertIndex, element);
            return insertIndex;
        }
    }
}
