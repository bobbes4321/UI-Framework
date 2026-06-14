using System;
using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One id-reference "slot" in a <see cref="UISpec"/>: a single mutable location that holds a
    /// Category/Name reference to an id of a known <see cref="IdType"/>. It exposes a typed
    /// <see cref="Get"/> (read the current category+name) and <see cref="Set"/> (write a new
    /// category+name back into the model), abstracting over the two storage forms the spec uses —
    /// a slashed "Category/Name" string (e.g. <c>element.id</c>, <c>onClickShowView</c>, a flow
    /// node's view entry) and separate category/name fields (e.g. a flow trigger, a signal ref, a
    /// menu item). Consumers never see which form backs a given slot.
    ///
    /// This is the seam that single-sources "what references an id": <see cref="IdRefSlots.Visit"/>
    /// is the ONE traversal of every such location. <see cref="IdUsageScanner.Collect"/> reads
    /// through it; <see cref="IdReferenceRewriter"/> mutates through it. Add a new id-bearing field
    /// once, here, and both the usage scan and the rename-rewrite pick it up automatically.
    /// </summary>
    public readonly struct IdRefSlot
    {
        /// <summary> The id <see cref="System.Type"/> this slot addresses (ViewId/ButtonId/…). Never null. </summary>
        public readonly Type IdType;

        private readonly Action<Action<string, string>> _get; // hands category+name to a callback
        private readonly Action<string, string> _set;          // writes category+name back

        internal IdRefSlot(Type idType, Action<Action<string, string>> get, Action<string, string> set)
        {
            IdType = idType;
            _get = get;
            _set = set;
        }

        /// <summary> Reads the slot's current Category/Name into the out params. </summary>
        public void Get(out string category, out string name)
        {
            string c = null, n = null;
            _get((cat, nm) => { c = cat; n = nm; });
            category = c;
            name = n;
        }

        /// <summary> Writes a new Category/Name into the slot (in the model's native storage form). </summary>
        public void Set(string category, string name) => _set(category, name);
    }

    /// <summary>
    /// The single enumeration of every id-reference slot in a <see cref="UISpec"/>. Knows the full
    /// catalog of id-bearing fields once — view ids, element ids by kind→idType (the same mapping as
    /// <see cref="IdUsageScanner.IdTypeForKind"/>), panel ids / tab.controls, domain + onClick signals,
    /// onClick show/hide views, list/grid row templates, flow node views/hide, flow triggers, and
    /// menu-catalog item ids — so reading and rewriting can't drift over what an id reference is.
    /// Pure: it walks the in-memory model and calls back per slot; no asset I/O.
    /// </summary>
    public static class IdRefSlots
    {
        /// <summary> Visits every id-reference slot in <paramref name="spec"/>, invoking <paramref name="onSlot"/> per slot. </summary>
        public static void Visit(UISpec spec, Action<IdRefSlot> onSlot)
        {
            if (spec == null || onSlot == null) return;

            if (spec.views != null)
                foreach (ViewSpec view in spec.views)
                {
                    if (view == null) continue;
                    VisitView(view, onSlot);
                }

            if (spec.popups != null)
                foreach (PopupSpec popup in spec.popups)
                    if (popup?.elements != null)
                        foreach (ElementSpec element in popup.elements)
                            VisitElement(element, onSlot);

            if (spec.flow?.nodes != null)
                foreach (FlowNodeSpec node in spec.flow.nodes)
                    VisitFlowNode(node, onSlot);

            VisitMenuCatalogs(spec.settings, onSlot);
            VisitMenuCatalogs(spec.cheats, onSlot);
        }

        private static void VisitView(ViewSpec view, Action<IdRefSlot> onSlot)
        {
            // a view's own id is a declared ViewId reference (split category/viewName storage)
            onSlot(Pair(typeof(ViewId),
                () => view.category, () => view.viewName,
                c => view.category = c, n => view.viewName = n));

            if (view.elements != null)
                foreach (ElementSpec element in view.elements)
                    VisitElement(element, onSlot);
        }

        private static void VisitElement(ElementSpec element, Action<IdRefSlot> onSlot)
        {
            if (element == null) return;

            // the widget's own id → the database its kind addresses (slashed "Category/Name" string)
            Type idType = IdUsageScanner.IdTypeForKind(element.kind);
            if (idType != null && !string.IsNullOrEmpty(element.id))
                onSlot(Slashed(idType, () => element.id, s => element.id = s));

            // a panel's own id and a tab's "controls" both reference the PanelId database
            if (element.kind == "panel" && !string.IsNullOrEmpty(element.id))
                onSlot(Slashed(typeof(PanelId), () => element.id, s => element.id = s));
            if (!string.IsNullOrEmpty(element.controls))
                onSlot(Slashed(typeof(PanelId), () => element.controls, s => element.controls = s));

            // domain stream signals (toggle/slider/dropdown "signal", button onClick.signal)
            if (element.signal != null)
                onSlot(Pair(typeof(StreamId),
                    () => element.signal.category, () => element.signal.name,
                    c => element.signal.category = c, n => element.signal.name = n));
            if (element.onClickSignal != null)
                onSlot(Pair(typeof(StreamId),
                    () => element.onClickSignal.category, () => element.onClickSignal.name,
                    c => element.onClickSignal.category = c, n => element.onClickSignal.name = n));

            // onClick view commands reference the ViewId database
            if (!string.IsNullOrEmpty(element.onClickShowView))
                onSlot(Slashed(typeof(ViewId), () => element.onClickShowView, s => element.onClickShowView = s));
            if (!string.IsNullOrEmpty(element.onClickHideView))
                onSlot(Slashed(typeof(ViewId), () => element.onClickHideView, s => element.onClickHideView = s));

            if (element.item != null) VisitElement(element.item, onSlot);
            if (element.children != null)
                foreach (ElementSpec child in element.children) VisitElement(child, onSlot);
        }

        private static void VisitFlowNode(FlowNodeSpec node, Action<IdRefSlot> onSlot)
        {
            if (node == null) return;
            // node.views / node.hide are List<string> of slashed view ids — bind each by index so a
            // write lands on the right entry.
            VisitStringList(node.views, typeof(ViewId), onSlot);
            VisitStringList(node.hide, typeof(ViewId), onSlot);

            if (node.next != null)
                foreach (FlowEdgeSpec edge in node.next)
                    VisitTrigger(edge?.trigger, onSlot);
        }

        private static void VisitStringList(List<string> list, Type idType, Action<IdRefSlot> onSlot)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                int index = i; // capture
                if (string.IsNullOrEmpty(list[index])) continue;
                onSlot(Slashed(idType, () => list[index], s => list[index] = s));
            }
        }

        private static void VisitTrigger(FlowTrigger trigger, Action<IdRefSlot> onSlot)
        {
            if (trigger == null) return;
            Type idType;
            switch (trigger.type)
            {
                case FlowTrigger.TriggerType.ButtonClick: idType = typeof(ButtonId); break;
                case FlowTrigger.TriggerType.ToggleOn:
                case FlowTrigger.TriggerType.ToggleOff: idType = typeof(ToggleId); break;
                case FlowTrigger.TriggerType.ViewShown:
                case FlowTrigger.TriggerType.ViewHidden: idType = typeof(ViewId); break;
                case FlowTrigger.TriggerType.Signal: idType = typeof(StreamId); break;
                case FlowTrigger.TriggerType.Custom:
                    // a project trigger kind that carries a preferred id database (the same seam the
                    // inspector dropdown uses) contributes its reference; others have none.
                    idType = NeoTriggerKinds.TryGet(trigger.customKind, out INeoTriggerKind kind)
                             && kind is ITriggerKindIdDatabase withDb
                        ? withDb.PreferredIdType
                        : null;
                    break;
                default: idType = null; break;
            }
            if (idType == null) return;
            onSlot(Pair(idType,
                () => trigger.category, () => trigger.name,
                c => trigger.category = c, n => trigger.name = n));
        }

        private static void VisitMenuCatalogs(List<MenuCatalogSpec> catalogs, Action<IdRefSlot> onSlot)
        {
            if (catalogs == null) return;
            foreach (MenuCatalogSpec catalog in catalogs)
            {
                if (catalog?.items == null) continue;
                foreach (MenuItemSpec item in catalog.items)
                {
                    if (item == null) continue;
                    Type idType = IdUsageScanner.IdTypeForKind(item.kind);
                    if (idType == null) continue;
                    onSlot(Pair(idType,
                        () => item.category, () => item.name,
                        c => item.category = c, n => item.name = n));
                }
            }
        }

        // ---- slot builders for the two storage forms -------------------------------------------

        /// <summary> A slot backed by a single slashed "Category/Name" string field. </summary>
        private static IdRefSlot Slashed(Type idType, Func<string> get, Action<string> set) =>
            new IdRefSlot(idType,
                emit => { CategoryNameId.Parse(get(), out string c, out string n); emit(c, n); },
                (c, n) => set($"{c}/{n}"));

        /// <summary> A slot backed by separate category and name fields. </summary>
        private static IdRefSlot Pair(Type idType,
            Func<string> getCategory, Func<string> getName,
            Action<string> setCategory, Action<string> setName) =>
            new IdRefSlot(idType,
                emit => emit(getCategory(), getName()),
                (c, n) => { setCategory(c); setName(n); });
    }
}
