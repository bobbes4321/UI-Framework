using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary> How an inspector should draw a field — picks the kit control and the option set. </summary>
    public enum FieldKind
    {
        Text,
        MultilineText,
        Float,
        Int,
        Bool,
        Vector2,        // float[2] — size / position / cellSize
        ColorToken,     // theme token name (swatch + dropdown)
        ShapeStyle,     // theme shape style name
        TextStyle,      // theme text style name
        Anchor,         // anchor preset name
        ButtonVariant,  // primary / secondary / ghost / danger
        ButtonSize,     // sm / md / lg
        Align,          // left / center / right
        ShapeName,      // roundedRect / circle / pill / …
        IconName,       // Lucide icon (searchable)
        StringList,     // dropdown options
        ViewRef,        // "Category/Name" of a view
        PopupRef,       // popup name
        PanelRef,       // sibling panel id (tab.controls)
        DataRef,        // bound UIData id (list.bind)
        IdRef,          // element.id — a Category/Name pair backed by the kind's ID database
                        // (two searchable dropdowns with inline "+ Add", like CategoryNameIdDrawer)

        // ---- composite layout editors (read/write element.layout / padding4, not a single boxed
        //      value — a host inspector draws them specially; get/set are unused sentinels). ----
        Constraint,     // Figma constraint widget over element.layout.h/v/offset/size
        SizingMode,     // per-child Fixed/Hug/Fill over element.layout.sizing (axis encoded in the key)
        AutoLayout,     // vstack/hstack/grid panel: direction/gap/per-side padding/align
    }

    /// <summary>
    /// One editable field on an <see cref="ElementSpec"/>: how to label it, how to draw it, and how
    /// to read/write it. Accessors box the value (editor-only — allocation here is irrelevant) so a
    /// host inspector can stay a single data-driven loop. Used by <see cref="INeoElementKind.Fields"/>
    /// so a project-registered element kind can declare its own inspector fields.
    /// </summary>
    public sealed class SpecField
    {
        public readonly string key;     // the ElementSpec field / JSON key (stable id for tests)
        public readonly string label;
        public readonly FieldKind kind;
        public readonly System.Func<ElementSpec, object> get;
        public readonly System.Action<ElementSpec, object> set;
        private readonly HashSet<string> _kinds; // null = applies to every element kind

        public SpecField(string key, string label, FieldKind kind,
            System.Func<ElementSpec, object> get, System.Action<ElementSpec, object> set, string[] kinds)
        {
            this.key = key;
            this.label = label;
            this.kind = kind;
            this.get = get;
            this.set = set;
            _kinds = kinds == null ? null : new HashSet<string>(kinds);
        }

        public bool AppliesTo(string elementKind) => _kinds == null || _kinds.Contains(elementKind);
    }
}
