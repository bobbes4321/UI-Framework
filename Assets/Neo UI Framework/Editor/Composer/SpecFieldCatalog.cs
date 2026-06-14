using System.Collections.Generic;

namespace Neo.UI.Editor.Composer
{
    /// <summary> How the inspector should draw a field — picks the kit control and the option set. </summary>
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

        // ---- Pillar F composite layout editors (read/write element.layout / padding4, not a single
        //      boxed value — the inspector draws them specially; get/set are unused sentinels). ----
        Constraint,     // Figma constraint widget over element.layout.h/v/offset/size
        SizingMode,     // per-child Fixed/Hug/Fill over element.layout.sizing (axis encoded in the key)
        AutoLayout,     // vstack/hstack/grid panel: direction/gap/per-side padding/align
    }

    /// <summary>
    /// One editable field on an <see cref="ElementSpec"/>: how to label it, how to draw it, and how
    /// to read/write it. Accessors box the value (editor-only — allocation here is irrelevant) so the
    /// inspector can stay a single data-driven loop.
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

    /// <summary>
    /// The single source of truth for which spec fields the inspector exposes per element kind, and
    /// the kind list the "+ Add child" picker offers. Extend HERE when <see cref="ElementSpec"/> grows
    /// — <c>SpecFieldCatalogTests</c> guards that every <see cref="ElementSpec.Kinds"/> entry resolves
    /// to at least one field and that the core serialized fields stay covered.
    /// </summary>
    public static class SpecFieldCatalog
    {
        // kind groupings (kept here so the field table reads cleanly)
        private static readonly string[] Containers =
            { "vstack", "hstack", "grid", "scroll", "panel", "overlay", "safearea" };
        private static readonly string[] Stacks = { "vstack", "hstack", "grid" };
        private static readonly string[] Labelled =
            { "button", "toggle", "switch", "tab", "text", "counter", "stepper", "input", "dropdown" };
        private static readonly string[] Ranged = { "slider", "progress", "stepper" };

        private static readonly List<SpecField> All = new List<SpecField>();

        // Pillar F composite layout editors. Kept OUT of <see cref="All"/> (and thus <see cref="AllKeys"/>,
        // which pins the single-value field set) because they don't carry a boxed value — the inspector
        // reads/writes element.layout / padding4 directly. They DO surface through <see cref="For"/> so
        // the inspector and tests see them per kind. Stacks-only fields are filtered by their kinds set.
        private static readonly List<SpecField> LayoutFields = new List<SpecField>();

        /// <summary> Stable field keys for the composite layout editors (test ids). </summary>
        public const string ConstraintKey = "constraint";
        public const string SizingWKey = "sizingW";
        public const string SizingHKey = "sizingH";
        public const string AutoLayoutKey = "autolayout";

        static SpecFieldCatalog()
        {
            // ---- layout (every kind) ----
            Add("anchor", "Anchor", FieldKind.Anchor, e => e.anchor, (e, v) => e.anchor = (string)v, null);
            Add("size", "Size [w,h]", FieldKind.Vector2, e => e.size, (e, v) => e.size = (float[])v, null);
            Add("position", "Position [x,y]", FieldKind.Vector2, e => e.position, (e, v) => e.position = (float[])v, null);
            Add("rotation", "Rotation°", FieldKind.Float, e => e.rotation, (e, v) => e.rotation = (float?)v, null);
            Add("flex", "Flex", FieldKind.Float, e => e.flex, (e, v) => e.flex = (float?)v, null);

            // ---- surface / styling ----
            Add("background", "Background", FieldKind.ColorToken, e => e.background, (e, v) => e.background = (string)v, null);
            Add("style", "Shape Style", FieldKind.ShapeStyle, e => e.style, (e, v) => e.style = (string)v, null);
            Add("radius", "Radius", FieldKind.Float, e => e.radius, (e, v) => e.radius = (float?)v, null);

            // ---- text / labels ----
            Add("label", "Label", FieldKind.Text, e => e.label, (e, v) => e.label = (string)v, Labelled);
            Add("labelColor", "Text Color", FieldKind.ColorToken, e => e.labelColor, (e, v) => e.labelColor = (string)v,
                new[] { "button", "toggle", "switch", "tab", "text", "icon", "counter", "stepper" });
            Add("textStyle", "Text Style", FieldKind.TextStyle, e => e.textStyle, (e, v) => e.textStyle = (string)v,
                new[] { "text", "button", "toggle", "switch", "tab", "counter", "stepper" });
            Add("fontSize", "Font Size", FieldKind.Float, e => e.fontSize, (e, v) => e.fontSize = (float?)v,
                new[] { "text", "counter" });
            Add("align", "Align", FieldKind.Align, e => e.align, (e, v) => e.align = (string)v,
                new[] { "text", "vstack", "hstack", "grid" });
            Add("outlineColor", "Outline Color", FieldKind.ColorToken, e => e.outlineColor, (e, v) => e.outlineColor = (string)v,
                new[] { "text" });
            Add("outlineWidth", "Outline Width", FieldKind.Float, e => e.outlineWidth, (e, v) => e.outlineWidth = (float?)v,
                new[] { "text" });

            // ---- containers ----
            Add("padding", "Padding", FieldKind.Float, e => e.padding, (e, v) => e.padding = (float?)v, Containers);
            Add("spacing", "Spacing", FieldKind.Float, e => e.spacing, (e, v) => e.spacing = (float?)v, Containers);
            Add("cascade", "Cascade Entrance", FieldKind.Bool, e => e.cascade, (e, v) => e.cascade = (bool)v, Stacks);
            Add("columns", "Columns", FieldKind.Int, e => e.columns, (e, v) => e.columns = (int?)v, new[] { "grid" });
            Add("cellSize", "Cell Size [w,h]", FieldKind.Vector2, e => e.cellSize, (e, v) => e.cellSize = (float[])v, new[] { "grid" });

            // ---- button ----
            Add("variant", "Variant", FieldKind.ButtonVariant, e => e.variant, (e, v) => e.variant = (string)v, new[] { "button" });
            Add("sizeVariant", "Size Variant", FieldKind.ButtonSize, e => e.sizeVariant, (e, v) => e.sizeVariant = (string)v, new[] { "button" });
            Add("icon", "Icon", FieldKind.IconName, e => e.icon, (e, v) => e.icon = (string)v,
                new[] { "button", "tab", "icon" });
            Add("badge", "Badge Count", FieldKind.Float, e => e.badge, (e, v) => e.badge = (float?)v, new[] { "button", "tab" });

            // ---- tab ----
            Add("controls", "Controls Panel", FieldKind.PanelRef, e => e.controls, (e, v) => e.controls = (string)v, new[] { "tab" });
            Add("group", "Group", FieldKind.Text, e => e.group, (e, v) => e.group = (string)v, new[] { "tab" });

            // ---- ranged (slider/progress/stepper) + tab "value" baking ----
            Add("min", "Min", FieldKind.Float, e => e.min, (e, v) => e.min = (float?)v, Ranged);
            Add("max", "Max", FieldKind.Float, e => e.max, (e, v) => e.max = (float?)v, Ranged);
            Add("value", "Value", FieldKind.Float, e => e.value, (e, v) => e.value = (float?)v,
                new[] { "slider", "progress", "stepper", "tab", "counter" });
            Add("step", "Step", FieldKind.Float, e => e.step, (e, v) => e.step = (float?)v, new[] { "stepper" });

            // ---- shape ----
            Add("shape", "Shape", FieldKind.ShapeName, e => e.shape, (e, v) => e.shape = (string)v, new[] { "shape" });
            Add("thickness", "Thickness", FieldKind.Float, e => e.thickness, (e, v) => e.thickness = (float?)v, new[] { "shape" });
            Add("arcStart", "Arc Start°", FieldKind.Float, e => e.arcStart, (e, v) => e.arcStart = (float?)v, new[] { "shape" });
            Add("arcSweep", "Arc Sweep°", FieldKind.Float, e => e.arcSweep, (e, v) => e.arcSweep = (float?)v, new[] { "shape" });

            // ---- image ----
            Add("src", "Sprite Path", FieldKind.Text, e => e.src, (e, v) => e.src = (string)v, new[] { "image" });
            Add("fit", "Fit", FieldKind.Text, e => e.fit, (e, v) => e.fit = (string)v, new[] { "image" });

            // ---- data ----
            Add("options", "Options", FieldKind.StringList, e => e.options, (e, v) => e.options = (List<string>)v, new[] { "dropdown" });
            Add("bind", "Data Source", FieldKind.DataRef, e => e.bind, (e, v) => e.bind = (string)v, new[] { "list", "grid", "scroll" });

            // ---- id (interactive elements addressed by Category/Name) ----
            Add("id", "Id", FieldKind.IdRef, e => e.id, (e, v) => e.id = (string)v,
                new[] { "button", "toggle", "switch", "tab", "slider", "progress", "stepper", "input", "dropdown", "counter", "list", "settings", "cheats" });

            // ---- catalog reference (settings/cheats elements) ----
            Add("catalog", "Catalog", FieldKind.Text, e => e.catalog, (e, v) => e.catalog = (string)v, new[] { "settings", "cheats" });

            // ---- Pillar F composite layout editors (drawn specially; see LayoutFields note) ----
            // Constraint + per-child sizing apply to every kind (sizing is further gated to layout-group
            // children by the inspector, since "is my parent a stack/grid" isn't a per-kind property).
            AddLayout(ConstraintKey, "Constraint", FieldKind.Constraint, null);
            AddLayout(SizingWKey, "Sizing W", FieldKind.SizingMode, null);
            AddLayout(SizingHKey, "Sizing H", FieldKind.SizingMode, null);
            // The auto-layout panel is for layout-group containers only.
            AddLayout(AutoLayoutKey, "Auto Layout", FieldKind.AutoLayout, Stacks);
        }

        // composite editors carry no boxed value — the inspector reads/writes element.layout/padding4
        private static void AddLayout(string key, string label, FieldKind kind, string[] kinds)
        {
            LayoutFields.Add(new SpecField(key, label, kind, _ => null, (_, __) => { }, kinds));
        }

        private static void Add(string key, string label, FieldKind kind,
            System.Func<ElementSpec, object> get, System.Action<ElementSpec, object> set, string[] kinds)
        {
            All.Add(new SpecField(key, label, kind, get, set, kinds));
        }

        // Extensibility seam: project-registered fields not tied to a built-in kind table entry.
        // (A project kind's own fields ride INeoElementKind.Fields; this is the explicit global hook the
        // plan calls for, for fields a project wants to splice onto kinds generically.)
        private static readonly List<SpecField> Registered = new List<SpecField>();

        /// <summary> Registers an extra inspector field (e.g. for a project-defined element kind). </summary>
        public static void RegisterField(SpecField field)
        {
            if (field != null) Registered.Add(field);
        }

        /// <summary> Test/seam hook: clears project-registered fields (built-ins are unaffected). </summary>
        public static void ClearRegisteredForTests() => Registered.Clear();

        /// <summary> The fields the inspector should draw for an element of <paramref name="elementKind"/>,
        /// in declaration order. Unions the built-in table, any globally-registered fields, and (for a
        /// project-registered kind) that kind's own <see cref="INeoElementKind.Fields"/>. </summary>
        public static List<SpecField> For(string elementKind)
        {
            var result = new List<SpecField>();
            foreach (SpecField field in LayoutFields)
                if (field.AppliesTo(elementKind))
                    result.Add(field);
            foreach (SpecField field in All)
                if (field.AppliesTo(elementKind))
                    result.Add(field);
            foreach (SpecField field in Registered)
                if (field.AppliesTo(elementKind))
                    result.Add(field);
            if (NeoElementKinds.TryGet(elementKind, out INeoElementKind ext) && ext.Fields != null)
                foreach (SpecField field in ext.Fields)
                    if (field != null) result.Add(field);
            return result;
        }

        /// <summary> Every field key the catalog knows (for coverage tests). </summary>
        public static IReadOnlyList<string> AllKeys()
        {
            var keys = new List<string>(All.Count);
            foreach (SpecField field in All) keys.Add(field.key);
            return keys;
        }

        /// <summary> The element kinds the "+ Add" pickers offer — the built-in spec kinds unioned with
        /// any project-registered kinds, so the picker and parser never drift. </summary>
        public static IReadOnlyList<string> ElementKinds => ElementSpec.KnownKinds;
    }
}
