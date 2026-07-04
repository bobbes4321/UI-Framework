using System;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The single definition of "how to walk a spec's element tree" — every element-tree walker in
    /// the package (id-reference scanning, binding-manifest derivation + token extraction, the
    /// legacy-to-layout migration) used to hand-roll its own <c>children</c>/<c>item</c> recursion,
    /// and about half of them disagreed on whether a bound-list <c>item</c> row template counts
    /// (audit D5 — the same drift class as D4's duplicated token scanner). Every caller now goes
    /// through here, so "does item count" is answered once, consistently, via
    /// <paramref name="includeItemTemplates"/>.
    ///
    /// Pure and allocation-light: no asset I/O, just a depth-first pre-order walk (element itself,
    /// then its <c>item</c> template's whole subtree when included, then its <c>children</c>).
    /// </summary>
    public static class SpecWalk
    {
        /// <summary> Visits every element in <paramref name="view"/>'s tree(s), depth-first. </summary>
        public static void Elements(ViewSpec view, bool includeItemTemplates, Action<ElementSpec> visit)
        {
            if (view?.elements == null || visit == null) return;
            foreach (ElementSpec element in view.elements)
                Elements(element, includeItemTemplates, visit);
        }

        /// <summary> Visits every element in <paramref name="popup"/>'s tree(s), depth-first. </summary>
        public static void Elements(PopupSpec popup, bool includeItemTemplates, Action<ElementSpec> visit)
        {
            if (popup?.elements == null || visit == null) return;
            foreach (ElementSpec element in popup.elements)
                Elements(element, includeItemTemplates, visit);
        }

        /// <summary>
        /// Visits <paramref name="root"/> itself, then (if <paramref name="includeItemTemplates"/>)
        /// its <c>item</c> row template's whole subtree, then its <c>children</c> — recursively.
        /// </summary>
        public static void Elements(ElementSpec root, bool includeItemTemplates, Action<ElementSpec> visit)
        {
            if (root == null || visit == null) return;
            visit(root);
            if (includeItemTemplates && root.item != null)
                Elements(root.item, includeItemTemplates, visit);
            if (root.children != null)
                foreach (ElementSpec child in root.children)
                    Elements(child, includeItemTemplates, visit);
        }

        /// <summary>
        /// Parent-aware variant of <see cref="Elements(ViewSpec,bool,Action{ElementSpec})"/> for
        /// callers whose per-element behavior depends on the immediate structural parent (e.g. "is my
        /// parent a layout group") rather than accumulated ancestor state. <c>parent</c> is null for a
        /// top-level view element. An <c>item</c> template's parent is the element that owns it, the
        /// same as for a genuine child.
        /// </summary>
        public static void Elements(ViewSpec view, bool includeItemTemplates, Action<ElementSpec, ElementSpec> visit)
        {
            if (view?.elements == null || visit == null) return;
            foreach (ElementSpec element in view.elements)
                ElementsWithParent(element, null, includeItemTemplates, visit);
        }

        /// <summary> Parent-aware variant, popup-rooted. See the <see cref="ViewSpec"/> overload. </summary>
        public static void Elements(PopupSpec popup, bool includeItemTemplates, Action<ElementSpec, ElementSpec> visit)
        {
            if (popup?.elements == null || visit == null) return;
            foreach (ElementSpec element in popup.elements)
                ElementsWithParent(element, null, includeItemTemplates, visit);
        }

        private static void ElementsWithParent(ElementSpec element, ElementSpec parent, bool includeItemTemplates,
            Action<ElementSpec, ElementSpec> visit)
        {
            if (element == null) return;
            visit(element, parent);
            if (includeItemTemplates && element.item != null)
                ElementsWithParent(element.item, element, includeItemTemplates, visit);
            if (element.children != null)
                foreach (ElementSpec child in element.children)
                    ElementsWithParent(child, element, includeItemTemplates, visit);
        }
    }
}
