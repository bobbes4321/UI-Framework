using System;
using System.Collections.Generic;
using System.Globalization;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Right pane: a data-driven re-projection of the selected node's spec fields. Element fields come
    /// from <see cref="SpecFieldCatalog"/> (the one place that knows kind → fields), drawn with the
    /// kit's controls (token swatches, searchable dropdowns, on-scale spacing). Views, popups,
    /// catalogs, menu items, the theme and the flow leaf get their own bespoke sections. Every write
    /// goes through <see cref="SpecDocument.ApplyEdit"/>; text/number fields are DELAYED so a single
    /// undo step covers a full edit rather than one per keystroke.
    /// </summary>
    public class SpecInspector
    {
        private readonly SpecDocument _document;
        private readonly Action<string> _selectPath;   // lets the inspector re-select after a path change
        private readonly Action _openFlow;
        private Vector2 _scroll;

        public SpecInspector(SpecDocument document, Action<string> selectPath, Action openFlow)
        {
            _document = document;
            _selectPath = selectPath;
            _openFlow = openFlow;
        }

        private UISpec Spec => _document.Spec;

        public void OnGUI(Rect rect, SpecNode node)
        {
            GUILayout.BeginArea(rect);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (node == null)
                EditorGUILayout.HelpBox("Select a node in the tree to edit it.", MessageType.Info);
            else
                Draw(node);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Draw(SpecNode node)
        {
            switch (node.kind)
            {
                case SpecNodeKind.Theme: ThemePaletteEditor.Draw(_document); break;
                case SpecNodeKind.View: DrawView(node.view); break;
                case SpecNodeKind.Popup: DrawPopup(node.popup); break;
                case SpecNodeKind.Element: DrawElement(node); break;
                case SpecNodeKind.Catalog: MenuCatalogEditor.DrawCatalog(_document, node.catalog); break;
                case SpecNodeKind.MenuItem: MenuCatalogEditor.DrawItem(_document, node.catalog, node.menuItem); break;
                case SpecNodeKind.Flow: DrawFlow(); break;
                default:
                    EditorGUILayout.HelpBox("Select a view, element, popup, catalog or theme.", MessageType.Info);
                    break;
            }
        }

        // ------------------------------------------------------------------ view

        private void DrawView(ViewSpec view)
        {
            NeoGUI.ComponentHeader("View", view.id, NeoColors.Containers);

            // The view's own addressable id, drawn with the same Category/Name picker as element ids
            // (backed by the viewIds database) so categories stay consistent and new ones are one click away.
            DrawCategoryNamePair("Id (Category / Name)",
                "The view's addressable id. Pick an existing category for consistency, or type a new value and choose “+ Add”.",
                view.category, view.viewName, IdDatabaseOptions.For(typeof(ViewId)),
                v => { string c = Sanitize(v, "View"); Apply(() => view.category = c, "Edit View", SpecPath.View($"{c}/{view.viewName}")); },
                v => { string n = Sanitize(v, "Main"); Apply(() => view.viewName = n, "Edit View", SpecPath.View($"{view.category}/{n}")); });

            DrawPopupRow("Background", view.background, () => ComposerOptions.Tokens(Spec),
                v => Apply(() => view.background = Empty(v), "Edit Background"), AddTokenAction());
            DrawPopupRow("Show Animation", view.showAnimation, PresetNames,
                v => Apply(() => view.showAnimation = Empty(v), "Edit Show Animation"));
            DrawPopupRow("Hide Animation", view.hideAnimation, PresetNames,
                v => Apply(() => view.hideAnimation = Empty(v), "Edit Hide Animation"));

            EditorGUILayout.Space(4f);
            DrawAddChildRow(view.elements, SpecPath.View(view.id));
        }

        // ------------------------------------------------------------------ popup

        private void DrawPopup(PopupSpec popup)
        {
            NeoGUI.ComponentHeader("Popup", popup.name, NeoColors.Containers);
            DrawDelayedText("Name", popup.name, v => Apply(() => popup.name = Empty(v) ?? popup.name, "Edit Popup",
                SpecPath.Popup(Empty(v) ?? popup.name)));
            DrawDelayedText("Title", popup.title, v => Apply(() => popup.title = Empty(v), "Edit Title"));
            DrawDelayedText("Message", popup.message, v => Apply(() => popup.message = Empty(v), "Edit Message"));
            DrawBool("Close Button", popup.close, v => Apply(() => popup.close = v, "Edit Close"));
            DrawSizeArray("Card Size", popup.size, v => Apply(() => popup.size = v, "Edit Size"));

            EditorGUILayout.Space(4f);
            DrawAddChildRow(popup.elements, SpecPath.Popup(popup.name));
        }

        // ------------------------------------------------------------------ element

        private void DrawElement(SpecNode node)
        {
            ElementSpec element = node.element;
            NeoGUI.ComponentHeader(element.kind, DescribeOwner(node), AccentFor(element.kind));

            // structural controls
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("▲", GUILayout.Width(26f))) MoveElement(node, -1);
                if (GUILayout.Button("▼", GUILayout.Width(26f))) MoveElement(node, 1);
                if (GUILayout.Button("Duplicate")) Apply(() => node.siblings.Insert(node.index + 1, ComposerFactory.Clone(element)), "Duplicate");
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = NeoColors.Remove;
                if (GUILayout.Button("Delete")) { Apply(() => node.siblings.Remove(element), "Delete"); _selectPath?.Invoke(null); return; }
                GUI.backgroundColor = prev;
            }

            if (_document.IsEditingOverride)
                EditorGUILayout.HelpBox(
                    $"Editing the “{_document.ActiveBreakpoint}” breakpoint — layout edits below are saved as " +
                    "an override delta over the base. Other fields still edit the base.", MessageType.None);

            NeoGUI.Splitter();

            bool buttonWithVariant = element.kind == "button" && !string.IsNullOrEmpty(element.sizeVariant);
            ParentInfo parent = FindParent(node);

            // group the kind's fields into Figma-style sections; composite layout editors are drawn
            // bespoke (constraint widget / sizing dropdowns / auto-layout panel)
            var layout = new List<SpecField>();
            var appearance = new List<SpecField>();
            var behavior = new List<SpecField>();
            var data = new List<SpecField>();
            foreach (SpecField field in SpecFieldCatalog.For(element.kind))
            {
                if (field.key == "size" && buttonWithVariant) continue; // polymorphic "size" — variant owns the key
                switch (SectionOf(field.key))
                {
                    case Section.Layout: layout.Add(field); break;
                    case Section.Appearance: appearance.Add(field); break;
                    case Section.Behavior: behavior.Add(field); break;
                    case Section.Data: data.Add(field); break;
                }
            }

            DrawSection("neo.composer.sec.layout", "Layout", NeoColors.Containers, true, () =>
            {
                foreach (SpecField field in layout) DrawLayoutField(element, field, parent);
            });
            if (appearance.Count > 0)
                DrawSection("neo.composer.sec.appearance", "Appearance", NeoColors.Rendering, true, () =>
                {
                    foreach (SpecField field in appearance) DrawField(element, field);
                });
            if (behavior.Count > 0 || HasOnClick(element.kind))
                DrawSection("neo.composer.sec.behavior", "Behavior", NeoColors.Interactive, true, () =>
                {
                    foreach (SpecField field in behavior) DrawField(element, field);
                    if (HasOnClick(element.kind)) DrawOnClick(element);
                });
            if (data.Count > 0)
                DrawSection("neo.composer.sec.data", "Data", NeoColors.Data, false, () =>
                {
                    foreach (SpecField field in data) DrawField(element, field);
                });

            EditorGUILayout.Space(4f);
            DrawAddChildRow(element.children, node.path);
        }

        // ------------------------------------------------------------------ sections

        private enum Section { Layout, Appearance, Behavior, Data }

        // map a field key to a section (the catalog is unordered by section, so this is the projection)
        private static Section SectionOf(string key)
        {
            switch (key)
            {
                case SpecFieldCatalog.ConstraintKey:
                case SpecFieldCatalog.SizingWKey:
                case SpecFieldCatalog.SizingHKey:
                case SpecFieldCatalog.AutoLayoutKey:
                case "anchor": case "size": case "position": case "rotation": case "flex":
                case "padding": case "spacing": case "cascade": case "columns": case "cellSize": case "align":
                    return Section.Layout;
                case "background": case "style": case "radius":
                case "labelColor": case "textStyle": case "fontSize": case "outlineColor": case "outlineWidth":
                case "variant": case "sizeVariant": case "icon": case "badge":
                case "shape": case "thickness": case "arcStart": case "arcSweep": case "src": case "fit":
                    return Section.Appearance;
                case "options": case "bind": case "catalog":
                    return Section.Data;
                default:
                    // label/id/controls/group/min/max/value/step + project-registered fields
                    return Section.Behavior;
            }
        }

        private void DrawSection(string key, string title, Color accent, bool defaultOpen, Action body)
        {
            if (!NeoGUI.BeginFoldoutSection(key, title, null, defaultOpen)) { NeoGUI.EndFoldoutSection(); return; }
            Rect strip = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(strip.x, strip.y, 2f, strip.height), accent);
            body();
            NeoGUI.EndFoldoutSection();
        }

        // dispatch: composite layout editors are drawn bespoke (and override-scoped); everything else
        // falls through to the generic field draw
        private void DrawLayoutField(ElementSpec e, SpecField f, ParentInfo parent)
        {
            switch (f.kind)
            {
                case FieldKind.Constraint: DrawConstraint(e); break;
                case FieldKind.SizingMode:
                    if (parent.IsLayoutGroup) DrawSizing(e, f.key == SpecFieldCatalog.SizingWKey, f.label);
                    break;
                case FieldKind.AutoLayout: DrawAutoLayout(e); break;
                default: DrawField(e, f); break;
            }
        }

        // ------------------------------------------------------------------ constraint widget

        // The Figma constraint control. Reads/writes the effective layout for the active edit scope:
        // base when no breakpoint is active, else the element's overrides[breakpoint] delta. The widget
        // POD's primary/secondary mirror UIWidgetFactory.ResolveOffset's per-constraint key convention.
        private void DrawConstraint(ElementSpec e)
        {
            LayoutSpec layout = EffectiveLayout(e);
            string h = !string.IsNullOrEmpty(layout?.h) ? layout.h : LayoutConstraints.Left;
            string v = !string.IsNullOrEmpty(layout?.v) ? layout.v : LayoutConstraints.Top;
            var model = new ConstraintModel { h = h, v = v };
            ReadOffsets(layout?.offset, h, LayoutAxis.Horizontal, out model.hPrimary, out model.hSecondary);
            ReadOffsets(layout?.offset, v, LayoutAxis.Vertical, out model.vPrimary, out model.vSecondary);

            DrawOverrideHeader("Constraint", LayoutDiffersFromBase(e), () => ClearLayoutOverride(e));

            Rect rect = EditorGUILayout.GetControlRect(false, NeoConstraintWidget.Height);
            NeoConstraintWidget.Draw(rect, model, next => Apply(() => WriteConstraint(e, next), "Edit Constraint"));
        }

        // translate a constraint's primary/secondary back into the named offset keys (the inverse of
        // UIWidgetFactory.ResolveOffset), then write the whole layout into the active scope.
        private void WriteConstraint(ElementSpec e, ConstraintModel m)
        {
            LayoutSpec target = ScopedLayout(e, create: true);
            target.h = m.h;
            target.v = m.v;
            var off = target.offset ?? new LayoutOffset();
            off.values.Clear();
            WriteOffsets(off, m.h, LayoutAxis.Horizontal, m.hPrimary, m.hSecondary);
            WriteOffsets(off, m.v, LayoutAxis.Vertical, m.vPrimary, m.vSecondary);
            target.offset = off.IsEmpty ? null : off;
            NormalizeScopedLayout(e);
        }

        internal static void ReadOffsets(LayoutOffset off, string id, LayoutAxis axis, out float primary, out float secondary)
        {
            primary = 0f; secondary = 0f;
            if (off == null) { if (id == LayoutConstraints.Scale) secondary = 1f; return; }
            switch (id)
            {
                case LayoutConstraints.Left: primary = off.GetOr("left", 0f); break;
                case LayoutConstraints.Right: primary = off.GetOr("right", 0f); break;
                case LayoutConstraints.Top: primary = off.GetOr("top", 0f); break;
                case LayoutConstraints.Bottom: primary = off.GetOr("bottom", 0f); break;
                case LayoutConstraints.Center: primary = off.GetOr(axis == LayoutAxis.Horizontal ? "h" : "v", 0f); break;
                case LayoutConstraints.LeftRight: primary = off.GetOr("left", 0f); secondary = off.GetOr("right", 0f); break;
                case LayoutConstraints.TopBottom: primary = off.GetOr("top", 0f); secondary = off.GetOr("bottom", 0f); break;
                case LayoutConstraints.Scale:
                    primary = off.GetOr(axis == LayoutAxis.Horizontal ? "left" : "bottom", 0f);
                    secondary = off.GetOr(axis == LayoutAxis.Horizontal ? "right" : "top", 1f);
                    break;
            }
        }

        internal static void WriteOffsets(LayoutOffset off, string id, LayoutAxis axis, float primary, float secondary)
        {
            switch (id)
            {
                case LayoutConstraints.Left: off.Set("left", primary); break;
                case LayoutConstraints.Right: off.Set("right", primary); break;
                case LayoutConstraints.Top: off.Set("top", primary); break;
                case LayoutConstraints.Bottom: off.Set("bottom", primary); break;
                case LayoutConstraints.Center: off.Set(axis == LayoutAxis.Horizontal ? "h" : "v", primary); break;
                case LayoutConstraints.LeftRight: off.Set("left", primary); off.Set("right", secondary); break;
                case LayoutConstraints.TopBottom: off.Set("top", primary); off.Set("bottom", secondary); break;
                case LayoutConstraints.Scale:
                    off.Set(axis == LayoutAxis.Horizontal ? "left" : "bottom", primary);
                    off.Set(axis == LayoutAxis.Horizontal ? "right" : "top", secondary);
                    break;
            }
        }

        // ------------------------------------------------------------------ sizing dropdown

        // per-child Fixed/Hug/Fill, sourced from the LayoutSizingModes registry (so a project's mode
        // appears). Edits element.layout.sizing.{w|h} in the active scope.
        private void DrawSizing(ElementSpec e, bool horizontal, string label)
        {
            LayoutSpec layout = EffectiveLayout(e);
            string current = layout?.sizing == null ? null : (horizontal ? layout.sizing.w : layout.sizing.h);
            DrawPopupRow(label, current, SizingModeOptions, value => Apply(() =>
            {
                LayoutSpec target = ScopedLayout(e, create: true);
                if (target.sizing == null) target.sizing = new LayoutSizing();
                if (horizontal) target.sizing.w = Empty(value); else target.sizing.h = Empty(value);
                if (target.sizing.IsEmpty) target.sizing = null;
                NormalizeScopedLayout(e);
            }, "Edit " + label));
        }

        private static List<string> SizingModeOptions()
        {
            var list = new List<string>();
            foreach (ILayoutSizingMode mode in LayoutSizingModes.All)
                if (mode != null && !string.IsNullOrEmpty(mode.Id)) list.Add(mode.Id);
            return list;
        }

        // ------------------------------------------------------------------ auto-layout panel

        // direction (the kind / columns), gap (spacing), per-side padding (padding4 over uniform
        // padding), child alignment (align). Base-only — overrides carry only layout deltas (Pillar B).
        private void DrawAutoLayout(ElementSpec e)
        {
            EditorGUILayout.LabelField(e.kind == "grid" ? "Grid" : (e.kind == "hstack" ? "Row (hstack)" : "Column (vstack)"),
                EditorStyles.miniBoldLabel);

            // direction: vstack ↔ hstack swap (grid is its own kind — offer columns instead)
            if (e.kind == "vstack" || e.kind == "hstack")
            {
                DrawEnumRow("Direction", e.kind, new[] { "vstack", "hstack" },
                    v => { if (!string.IsNullOrEmpty(v) && v != e.kind) Apply(() => e.kind = v, "Edit Direction"); });
            }
            else if (e.kind == "grid")
            {
                DrawNullableInt("Columns", e.columns, v => Apply(() => e.columns = v, "Edit Columns"));
            }

            DrawSnapFloat("Gap (spacing)", e.spacing, v => Apply(() => e.spacing = v, "Edit Spacing"));
            DrawPadding4(e);
            DrawEnumRow("Child Align", e.align, ComposerOptions.Aligns, v => Apply(() => e.align = Empty(v), "Edit Align"));
        }

        // a 4-field per-side padding editor over padding4 [left, top, right, bottom]. When padding4 is
        // absent it shows the uniform "padding" splatted across the four sides; the first edit
        // materializes padding4 (which wins over uniform per the model). A "uniform" toggle collapses
        // back to the single value.
        private static readonly GUIContent[] LTRB =
            { new GUIContent("L"), new GUIContent("T"), new GUIContent("R"), new GUIContent("B") };

        private void DrawPadding4(ElementSpec e)
        {
            bool per = e.padding4 != null && e.padding4.Length >= 4;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Padding");
                EditorGUI.BeginChangeCheck();
                bool nowPer = GUILayout.Toggle(per, "per-side", EditorStyles.miniButton, GUILayout.Width(64f));
                if (EditorGUI.EndChangeCheck() && nowPer != per)
                {
                    if (nowPer)
                    {
                        float u = e.padding ?? 0f;
                        Apply(() => { e.padding4 = new[] { u, u, u, u }; }, "Per-side Padding");
                    }
                    else
                    {
                        float l = e.padding4 != null && e.padding4.Length >= 1 ? e.padding4[0] : 0f;
                        Apply(() => { e.padding4 = null; e.padding = l != 0f ? (float?)l : e.padding; }, "Uniform Padding");
                    }
                    return;
                }
            }

            if (!per)
            {
                DrawSnapFloat("  Uniform", e.padding, v => Apply(() => e.padding = v, "Edit Padding"));
                return;
            }

            var values = new[] { e.padding4[0], e.padding4[1], e.padding4[2], e.padding4[3] };
            Rect rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent("  Sides"));
            EditorGUI.BeginChangeCheck();
            EditorGUI.MultiFloatField(rect, LTRB, values);
            if (EditorGUI.EndChangeCheck())
                Apply(() => e.padding4 = new[] { values[0], values[1], values[2], values[3] }, "Edit Padding");
        }

        // ------------------------------------------------------------------ override scoping helpers

        // the layout the editor should READ for the active scope: the override delta MERGED over base so
        // the widget shows the resolved value, falling back to base when there is no override.
        private LayoutSpec EffectiveLayout(ElementSpec e)
        {
            if (!_document.IsEditingOverride) return e.layout;
            LayoutSpec delta = OverrideDelta(e, _document.ActiveBreakpoint);
            LayoutSpec baseLayout = e.layout ?? new LayoutSpec();
            return baseLayout.MergedWith(delta);
        }

        // the layout object writes should target: base, or the overrides[bp] delta (created on demand).
        private LayoutSpec ScopedLayout(ElementSpec e, bool create)
        {
            if (!_document.IsEditingOverride)
            {
                if (e.layout == null && create) e.layout = new LayoutSpec();
                return e.layout;
            }
            string bp = _document.ActiveBreakpoint;
            LayoutSpec delta = OverrideDelta(e, bp);
            if (delta == null && create)
            {
                if (e.overrides == null) e.overrides = new Dictionary<string, LayoutSpec>();
                delta = new LayoutSpec();
                e.overrides[bp] = delta;
            }
            return delta;
        }

        private static LayoutSpec OverrideDelta(ElementSpec e, string bp) =>
            e.overrides != null && e.overrides.TryGetValue(bp, out LayoutSpec d) ? d : null;

        // after a scoped write, drop an emptied override key so a no-op edit doesn't leave a dangling
        // breakpoint entry (and base stays null when emptied).
        private void NormalizeScopedLayout(ElementSpec e)
        {
            if (!_document.IsEditingOverride)
            {
                if (e.layout != null && e.layout.IsEmpty) e.layout = null;
                return;
            }
            string bp = _document.ActiveBreakpoint;
            if (e.overrides != null && e.overrides.TryGetValue(bp, out LayoutSpec d) && (d == null || d.IsEmpty))
            {
                e.overrides.Remove(bp);
                if (e.overrides.Count == 0) e.overrides = null;
            }
        }

        // does this element carry a layout override for the active breakpoint? (the badge condition)
        private bool LayoutDiffersFromBase(ElementSpec e)
        {
            if (!_document.IsEditingOverride) return false;
            LayoutSpec d = OverrideDelta(e, _document.ActiveBreakpoint);
            return d != null && !d.IsEmpty;
        }

        private void ClearLayoutOverride(ElementSpec e)
        {
            if (!_document.IsEditingOverride) return;
            string bp = _document.ActiveBreakpoint;
            Apply(() =>
            {
                if (e.overrides != null && e.overrides.Remove(bp) && e.overrides.Count == 0) e.overrides = null;
            }, "Reset To Base");
        }

        // a sub-header for an override-scoped editor: shows an "overridden" badge + reset affordance when
        // the active breakpoint carries a delta. No-op chrome on base scope.
        private void DrawOverrideHeader(string title, bool overridden, Action reset)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (overridden)
                {
                    NeoGUI.Badge("overridden", NeoColors.Animation);
                    if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(48f))) reset?.Invoke();
                }
            }
        }

        // ------------------------------------------------------------------ parent lookup

        private struct ParentInfo
        {
            public ElementSpec element;   // null when the element is top-level in a view/popup
            public bool IsLayoutGroup;    // parent is a vstack/hstack (Unity layout group); grid uses GridLayoutGroup
        }

        // find the element whose children list IS this node's sibling list — gives the parent kind so the
        // sizing dropdowns only show inside a layout group. Bounded walk of the owning view/popup, done
        // on draw (cheap; no per-frame registry scan).
        private static ParentInfo FindParent(SpecNode node)
        {
            var info = new ParentInfo();
            if (node.siblings == null) return info;
            List<ElementSpec> roots = node.view != null ? node.view.elements : node.popup?.elements;
            if (roots == null) return info;
            if (ReferenceEquals(roots, node.siblings)) return info; // top-level: no element parent
            ElementSpec found = FindOwner(roots, node.siblings);
            info.element = found;
            info.IsLayoutGroup = found != null && (found.kind == "vstack" || found.kind == "hstack");
            return info;
        }

        private static ElementSpec FindOwner(List<ElementSpec> elements, List<ElementSpec> target)
        {
            foreach (ElementSpec el in elements)
            {
                if (el.children == null || el.children.Count == 0) continue;
                if (ReferenceEquals(el.children, target)) return el;
                ElementSpec deeper = FindOwner(el.children, target);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private static bool HasOnClick(string kind) => kind == "button" || kind == "toggle" || kind == "tab";

        private void DrawOnClick(ElementSpec e)
        {
            if (!NeoGUI.BeginFoldoutSection($"neo.composer.onclick", "On Click", null, true)) { NeoGUI.EndFoldoutSection(); return; }

            DrawRefRow("Show View", e.onClickShowView, () => ComposerOptions.ViewIds(Spec),
                v => Apply(() => e.onClickShowView = v, "Edit On Click"));
            DrawRefRow("Hide View", e.onClickHideView, () => ComposerOptions.ViewIds(Spec),
                v => Apply(() => e.onClickHideView = v, "Edit On Click"));
            DrawRefRow("Open Popup", e.onClickPopup, () => ComposerOptions.PopupNames(Spec),
                v => Apply(() => e.onClickPopup = v, "Edit On Click"));
            DrawBool("Close Container", e.onClickClose, v => Apply(() => e.onClickClose = v, "Edit On Click"));

            string signal = e.onClickSignal != null ? $"{e.onClickSignal.category}/{e.onClickSignal.name}" : "";
            DrawDelayedText("Signal (Cat/Name)", signal, v =>
            {
                Apply(() =>
                {
                    if (string.IsNullOrWhiteSpace(v)) { e.onClickSignal = null; return; }
                    CategoryNameId.Parse(v, out string category, out string name);
                    e.onClickSignal = new SignalRefSpec { category = category, name = name };
                }, "Edit On Click Signal");
            });
            NeoGUI.EndFoldoutSection();
        }

        private void DrawField(ElementSpec e, SpecField f)
        {
            switch (f.kind)
            {
                case FieldKind.Text:
                case FieldKind.MultilineText:
                    DrawDelayedText(f.label, (string)f.get(e), v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.Float:
                    if (f.key == "padding" || f.key == "spacing")
                        DrawSnapFloat(f.label, (float?)f.get(e), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    else
                        DrawNullableFloat(f.label, (float?)f.get(e), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
                case FieldKind.Int:
                    DrawNullableInt(f.label, (int?)f.get(e), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
                case FieldKind.Bool:
                    DrawBool(f.label, (bool)f.get(e), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
                case FieldKind.Vector2:
                    DrawSizeArray(f.label, (float[])f.get(e), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
                case FieldKind.ColorToken:
                    DrawTokenRow(f.label, (string)f.get(e), v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.ShapeStyle:
                    DrawPopupRow(f.label, (string)f.get(e), ComposerOptions.ShapeStyles, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.TextStyle:
                    DrawPopupRow(f.label, (string)f.get(e), ComposerOptions.TextStyles, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.Anchor:
                    DrawPopupRow(f.label, (string)f.get(e), ComposerOptions.Anchors, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.ButtonVariant:
                    DrawEnumRow(f.label, (string)f.get(e), ComposerOptions.ButtonVariants, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.ButtonSize:
                    DrawEnumRow(f.label, (string)f.get(e), ComposerOptions.ButtonSizes, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.Align:
                    DrawEnumRow(f.label, (string)f.get(e), ComposerOptions.Aligns, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.ShapeName:
                    DrawEnumRow(f.label, (string)f.get(e), ComposerOptions.ShapeNames, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.IconName:
                    DrawPopupRow(f.label, (string)f.get(e), ComposerOptions.Icons, v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.ViewRef:
                    DrawRefRow(f.label, (string)f.get(e), () => ComposerOptions.ViewIds(Spec), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
                case FieldKind.PopupRef:
                    DrawRefRow(f.label, (string)f.get(e), () => ComposerOptions.PopupNames(Spec), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
                case FieldKind.PanelRef:
                    DrawRefRow(f.label, (string)f.get(e), () => CurrentViewPanels(), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
                case FieldKind.DataRef:
                    DrawDelayedText(f.label, (string)f.get(e), v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.IdRef:
                    DrawIdRef(e, f);
                    break;
                case FieldKind.StringList:
                    DrawStringList(f.label, (List<string>)f.get(e), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
            }
        }

        // element.id as a Category/Name pair — two searchable dropdowns (the package's standard id UI,
        // mirroring CategoryNameIdDrawer) backed by the kind's ID database, each with an inline "+ Add" row
        // so a category/name that doesn't exist yet is created on the spot (no modal). Kinds with no
        // dedicated database still get the pair: the "+ Add" row stays enabled so a value is always typeable,
        // it just isn't persisted into a reusable database.
        private void DrawIdRef(ElementSpec e, SpecField f)
        {
            IdDatabase db = IdDatabaseOptions.ForElementKind(e.kind);
            CategoryNameId.Parse((string)f.get(e), out string category, out string name);
            DrawCategoryNamePair(f.label,
                "Addressable id (Category / Name). Pick from the database, or type a new value and choose “+ Add”.",
                category, name, db,
                picked => Apply(() => f.set(e, ComposeId(picked, name)), "Edit " + f.label),
                picked => Apply(() => f.set(e, ComposeId(category, picked)), "Edit " + f.label));
        }

        // The package's standard Category/Name id UI: two searchable dropdowns (mirroring CategoryNameIdDrawer)
        // backed by `db` for autocomplete + inline "+ Add" persistence. Works with db == null too — the
        // "+ Add" row stays enabled so a value is always typeable, it just isn't persisted to a database.
        // Single source for the element-id field AND the view id (category/name) so they read identically.
        private void DrawCategoryNamePair(string label, string tooltip, string category, string name,
            IdDatabase db, Action<string> onCategory, Action<string> onName)
        {
            Rect row = EditorGUILayout.GetControlRect();
            row = EditorGUI.PrefixLabel(row, new GUIContent(label, tooltip));
            NeoGUI.SplitHorizontal(row, out Rect categoryRect, out Rect nameRect);

            NeoDropdown.ValuePopup(categoryRect, category,
                () => IdDatabaseOptions.Categories(db),
                onCategory,
                CategoryNameId.DefaultCategory,
                db != null ? (Action<string>)(nv => IdDatabaseOptions.AddCategory(db, nv)) : (_ => { }));

            NeoDropdown.ValuePopup(nameRect, name,
                () => IdDatabaseOptions.Names(db, category),
                onName,
                CategoryNameId.DefaultName,
                db != null ? (Action<string>)(nv => IdDatabaseOptions.AddName(db, category, nv)) : (_ => { }));
        }

        // Recombines a Category/Name pair into the element.id string: preserves the bare-name form when the
        // category is the implicit default (so "Play" doesn't churn into "None/Play"), and clears the id
        // entirely when both sides are empty/default.
        private static string ComposeId(string category, string name)
        {
            bool catDefault = string.IsNullOrWhiteSpace(category) || category == CategoryNameId.DefaultCategory;
            bool nameDefault = string.IsNullOrWhiteSpace(name) || name == CategoryNameId.DefaultName;
            if (catDefault && nameDefault) return null;
            if (catDefault) return name.Trim();
            return category.Trim() + "/" + (nameDefault ? CategoryNameId.DefaultName : name.Trim());
        }

        // panel ids of the view the selected element lives in (best effort — the inspector knows the
        // owning view through the tree node, but DrawField has only the element; fall back to all views)
        private List<string> CurrentViewPanels()
        {
            var list = new List<string>();
            foreach (ViewSpec view in Spec.views) list.AddRange(ComposerOptions.PanelIds(view));
            return list;
        }

        // ------------------------------------------------------------------ flow

        private void DrawFlow()
        {
            NeoGUI.ComponentHeader("Flow", Spec.flow != null ? Spec.flow.name : "(none)", NeoColors.Flow);
            EditorGUILayout.HelpBox(
                "Flow editing happens in the Flow Graph window, bound to this document. Edits you make " +
                "there are mirrored back into the spec and saved with it.", MessageType.Info);
            if (Spec.flow == null && GUILayout.Button("Create Flow"))
                Apply(() => Spec.flow = new FlowSpec { name = "UI" }, "Create Flow");
            if (NeoGUI.AccentButton("Open in Flow Graph Window", NeoColors.Flow))
                _openFlow?.Invoke();
        }

        // ------------------------------------------------------------------ shared field controls

        private void DrawAddChildRow(List<ElementSpec> list, string parentPath)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("Add Child"));
            NeoDropdown.ValuePopup(rect, "", () => new List<string>(ElementSpec.KnownKinds),
                kind => Apply(() => list.Add(ComposerFactory.NewElement(kind)), "Add " + kind), "+ element");
        }

        private void DrawDelayedText(string label, string current, Action<string> onCommit)
        {
            EditorGUI.BeginChangeCheck();
            string v = EditorGUILayout.DelayedTextField(label, current ?? "");
            if (EditorGUI.EndChangeCheck()) onCommit(v);
        }

        private void DrawBool(string label, bool current, Action<bool> onChange)
        {
            EditorGUI.BeginChangeCheck();
            bool v = EditorGUILayout.Toggle(label, current);
            if (EditorGUI.EndChangeCheck()) onChange(v);
        }

        // a row is: [ label column ][ 14px enable toggle ][ label-less field… ]. Drawing the label
        // through PrefixLabel (never letting the field draw its own) keeps it on ONE line and stops
        // the toggle/label/field from overlapping.
        private const float ToggleWidth = 14f;
        private const float ToggleGap = 18f;
        private static readonly GUIContent[] XY = { new GUIContent("X"), new GUIContent("Y") };

        private static void SplitToggleRow(string label, out Rect toggleRect, out Rect fieldRect)
        {
            Rect content = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(label));
            toggleRect = new Rect(content.x, content.y, ToggleWidth, content.height);
            fieldRect = new Rect(content.x + ToggleGap, content.y, content.width - ToggleGap, content.height);
        }

        private void DrawNullableFloat(string label, float? current, Action<float?> onChange)
        {
            SplitToggleRow(label, out Rect toggleRect, out Rect fieldRect);
            EditorGUI.BeginChangeCheck();
            bool has = EditorGUI.Toggle(toggleRect, current.HasValue);
            using (new EditorGUI.DisabledScope(!has))
            {
                float value = EditorGUI.DelayedFloatField(fieldRect, current ?? 0f);
                if (EditorGUI.EndChangeCheck()) onChange(has ? (float?)value : null);
            }
        }

        private void DrawSnapFloat(string label, float? current, Action<float?> onChange)
        {
            SplitToggleRow(label, out Rect toggleRect, out Rect fieldRect);
            const float snapWidth = 34f;
            var snapRect = new Rect(fieldRect.xMax - snapWidth, fieldRect.y, snapWidth, fieldRect.height);
            fieldRect.width -= snapWidth + 2f;

            EditorGUI.BeginChangeCheck();
            bool has = EditorGUI.Toggle(toggleRect, current.HasValue);
            using (new EditorGUI.DisabledScope(!has))
            {
                float value = EditorGUI.DelayedFloatField(fieldRect, current ?? 0f);
                if (EditorGUI.EndChangeCheck()) onChange(has ? (float?)value : null);
            }
            // on-scale snap dropdown (4/8/12/16/24/32/48/64) — selecting a value also enables the field.
            // empty label → just the popup's arrow (a compact "pick a scale value" affordance)
            NeoDropdown.ValuePopup(snapRect, "", ScaleStrings, s =>
            {
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    onChange(parsed);
            }, "");
        }

        private static List<string> ScaleStrings()
        {
            var list = new List<string>();
            foreach (float v in ComposerOptions.SpacingScale) list.Add(v.ToString(CultureInfo.InvariantCulture));
            return list;
        }

        private void DrawNullableInt(string label, int? current, Action<int?> onChange)
        {
            SplitToggleRow(label, out Rect toggleRect, out Rect fieldRect);
            EditorGUI.BeginChangeCheck();
            bool has = EditorGUI.Toggle(toggleRect, current.HasValue);
            using (new EditorGUI.DisabledScope(!has))
            {
                int value = EditorGUI.DelayedIntField(fieldRect, current ?? 0);
                if (EditorGUI.EndChangeCheck()) onChange(has ? (int?)value : null);
            }
        }

        private void DrawSizeArray(string label, float[] current, Action<float[]> onChange)
        {
            SplitToggleRow(label, out Rect toggleRect, out Rect fieldRect);
            bool present = current != null && current.Length >= 2;
            EditorGUI.BeginChangeCheck();
            bool has = EditorGUI.Toggle(toggleRect, present);
            using (new EditorGUI.DisabledScope(!has))
            {
                var values = new[] { present ? current[0] : 0f, present ? current[1] : 0f };
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUI.MultiFloatField(fieldRect, XY, values);
                EditorGUIUtility.labelWidth = previousLabelWidth;
                if (EditorGUI.EndChangeCheck()) onChange(has ? new[] { values[0], values[1] } : null);
            }
        }

        private void DrawPopupRow(string label, string current, Func<List<string>> options, Action<string> onSelect, Action<string> onAdd = null)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            NeoDropdown.ValuePopup(rect, current, options, onSelect, "None", onAdd);
        }

        private void DrawEnumRow(string label, string current, string[] options, Action<string> onSelect)
        {
            DrawPopupRow(label, current, () => new List<string>(options), onSelect);
        }

        // The "(None)" sentinel surfaced at the top of a clearable reference dropdown. A real id could
        // never collide with it (ids don't carry parentheses in this position), so selecting it unambiguously
        // means "clear".
        private const string NoneRef = "(None)";

        // A reference dropdown that can always be reset: prepends a "(None)" entry that writes null, so a
        // Show/Hide-View, popup or panel reference set by mistake can be cleared back to nothing. Use for
        // every "points at another named thing" field; plain string fields keep DrawPopupRow.
        private void DrawRefRow(string label, string current, Func<List<string>> options, Action<string> onSet)
        {
            DrawPopupRow(label, current, () =>
                {
                    var list = new List<string> { NoneRef };
                    List<string> provided = options?.Invoke();
                    if (provided != null) list.AddRange(provided);
                    return list;
                },
                v => onSet(v == NoneRef ? null : Empty(v)));
        }

        private void DrawTokenRow(string label, string current, Action<string> onSelect)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            var swatchRect = new Rect(rect.x, rect.y + 1f, 16f, rect.height - 2f);
            if (Event.current.type == EventType.Repaint && TryResolveToken(current, out Color color))
                EditorGUI.DrawRect(swatchRect, color);
            var popupRect = new Rect(rect.x + 20f, rect.y, rect.width - 20f, rect.height);
            NeoDropdown.ValuePopup(popupRect, current, () => ComposerOptions.Tokens(Spec), onSelect, "None", AddTokenAction());
        }

        private Action<string> AddTokenAction() => token =>
        {
            Apply(() =>
            {
                if (Spec.theme == null) Spec.theme = new ThemeSpec();
                if (!Spec.theme.tokens.ContainsKey(token)) Spec.theme.tokens[token] = "#FFFFFF";
            }, "Add Token");
        };

        private bool TryResolveToken(string token, out Color color)
        {
            color = Color.clear;
            if (string.IsNullOrEmpty(token)) return false;
            if (Spec.theme != null && Spec.theme.tokens.TryGetValue(token, out string hex)
                && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out color))
                return true;
            NeoUISettings settings = NeoUISettings.instance;
            return settings != null && settings.theme != null && settings.theme.TryGetColor(token, out color);
        }

        private void DrawStringList(string label, List<string> current, Action<List<string>> onChange)
        {
            EditorGUILayout.LabelField(label);
            var list = current != null ? new List<string>(current) : new List<string>();
            int removeAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string v = EditorGUILayout.DelayedTextField(list[i]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        list[i] = v;
                        onChange(list);
                        return;
                    }
                    if (GUILayout.Button("−", GUILayout.Width(24f))) removeAt = i;
                }
            }
            if (removeAt >= 0)
            {
                list.RemoveAt(removeAt);
                onChange(list.Count > 0 ? list : null);
                return;
            }
            if (GUILayout.Button("+ Add Option", GUILayout.Width(110f)))
            {
                list.Add("Option");
                onChange(list);
            }
        }

        private List<string> PresetNames()
        {
            var list = new List<string>();
            foreach (PresetSpec preset in Spec.presets) list.Add(preset.name);
            return list;
        }

        // ------------------------------------------------------------------ helpers

        private void MoveElement(SpecNode node, int delta)
        {
            int target = node.index + delta;
            if (target < 0 || target >= node.siblings.Count) return;
            Apply(() =>
            {
                ElementSpec moved = node.siblings[node.index];
                node.siblings.RemoveAt(node.index);
                node.siblings.Insert(target, moved);
            }, "Move Element", ListPathOf(node.path) + $"[{target}]");  // follow the moved element to its new slot
        }

        // Strips the trailing "[index]" off an element path to recover the list it lives in
        // (e.g. ".../elements[2]" → ".../elements"), so a moved element can be re-addressed at its new index.
        private static string ListPathOf(string elementPath)
        {
            int bracket = elementPath.LastIndexOf('[');
            return bracket < 0 ? elementPath : elementPath.Substring(0, bracket);
        }

        private void Apply(Action mutate, string label, string reselectPath = null)
        {
            _document.ApplyEdit(mutate, label);
            if (reselectPath != null) _selectPath?.Invoke(reselectPath);
        }

        private static string Empty(string v) => string.IsNullOrWhiteSpace(v) ? null : v;
        private static string Sanitize(string v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();

        private static string DescribeOwner(SpecNode node) =>
            node.view != null ? node.view.id : node.popup != null ? node.popup.name : "";

        private static Color AccentFor(string kind)
        {
            // project-registered kinds carry their own accent; built-ins fall to the category default
            if (NeoElementKinds.TryGet(kind, out INeoElementKind ext)) return ext.Accent;
            switch (kind)
            {
                case "button": case "toggle": case "switch": case "tab": case "slider":
                case "stepper": case "input": case "dropdown": case "tabbar":
                    return NeoColors.Interactive;
                case "shape": case "image": case "icon": case "progress": case "counter":
                    return NeoColors.Rendering;
                default:
                    return NeoColors.Containers;
            }
        }
    }
}
