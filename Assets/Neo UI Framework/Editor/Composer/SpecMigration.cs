using System.Collections.Generic;
using Neo.UI.Editor;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Opt-in, explicit migration of a spec's legacy <c>anchor</c>/<c>position</c>/<c>size</c>/
    /// <c>flex</c> placement into the equivalent Figma-style <c>layout</c> model. NEVER runs
    /// automatically (CLAUDE.md: the legacy path stays fully supported and byte-identical; the
    /// committed demo + starter kit are GUID-referenced and are not migrated by default). It is
    /// proven pixel-identical by <c>SpecMigrationTests</c> and is idempotent: an already-migrated
    /// element (it already has a <c>layout</c>, or no legacy geometry) is left untouched.
    ///
    /// Conversion is done by replaying the generator's legacy placement onto a throwaway
    /// RectTransform, then reverse-detecting it through the same <see cref="ConstraintLayout"/>
    /// machinery used at export — so the migrated layout, when re-applied, reproduces the identical
    /// anchors/offsets.
    /// </summary>
    public static class SpecMigration
    {
        [MenuItem("Tools/Neo UI/Migrate Spec To Layout Model", priority = 81)]
        public static void MigrateSpecFileDialog()
        {
            string path = EditorUtility.OpenFilePanel("Select UI spec to migrate (JSON)", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            string json = System.IO.File.ReadAllText(path);
            UISpec migrated = MigrateLegacyToLayout(UISpec.FromJson(json));
            string outPath = EditorUtility.SaveFilePanel("Save migrated spec", System.IO.Path.GetDirectoryName(path),
                System.IO.Path.GetFileNameWithoutExtension(path) + ".layout.json", "json");
            if (string.IsNullOrEmpty(outPath)) return;
            System.IO.File.WriteAllText(outPath, migrated.ToJson());
            EditorUtility.DisplayDialog("Migrate Spec",
                $"Migrated legacy placement to the layout model:\n{outPath}\n\nGeneration is pixel-identical; review before committing.", "OK");
        }

        /// <summary>
        /// Returns a NEW spec with every element's legacy placement rewritten as a <c>layout</c>.
        /// Pure (no asset I/O); the input spec is not mutated.
        /// </summary>
        public static UISpec MigrateLegacyToLayout(UISpec spec)
        {
            if (spec == null) return null;
            UISpec clone = UISpec.FromJson(spec.ToJson()); // deep copy via the canonical round-trip
            foreach (ViewSpec view in clone.views)
                MigrateElements(view.elements, inLayout: false);
            foreach (PopupSpec popup in clone.popups)
                MigrateElements(popup.elements, inLayout: false);
            return clone;
        }

        private static void MigrateElements(List<ElementSpec> elements, bool inLayout)
        {
            if (elements == null) return;
            foreach (ElementSpec element in elements)
            {
                MigrateOne(element, inLayout);
                bool childrenInLayout = IsLayoutGroup(element.kind);
                MigrateElements(element.children, childrenInLayout);
                if (element.item != null) MigrateOne(element.item, childrenInLayout);
            }
        }

        /// <summary> Containers whose direct children are placed by a layout group. </summary>
        private static bool IsLayoutGroup(string kind) =>
            kind == "vstack" || kind == "hstack" || kind == "grid";

        private static void MigrateOne(ElementSpec element, bool inLayout)
        {
            // idempotent: already migrated, or nothing legacy to migrate
            if (element.layout != null && !element.layout.IsEmpty) return;
            bool hasFreeGeometry = !string.IsNullOrEmpty(element.anchor) || element.position != null
                || (element.size != null && element.size.Length >= 2);
            bool hasLayoutGeometry = (element.size != null && element.size.Length >= 2) || element.flex.HasValue;
            if (inLayout ? !hasLayoutGeometry : !hasFreeGeometry) return;

            element.layout = inLayout ? BuildLayoutInLayout(element) : BuildLayoutFree(element);

            // clear the legacy fields the layout now owns (size variant is a string and is untouched)
            element.anchor = null;
            element.position = null;
            element.flex = null;
            if (element.size != null) element.size = null;
        }

        /// <summary> Free element: replay legacy anchor/size/position onto a temp rect, reverse-detect. </summary>
        private static LayoutSpec BuildLayoutFree(ElementSpec element)
        {
            string preset = string.IsNullOrEmpty(element.anchor) ? "TopLeft" : element.anchor;
            if (!ConstraintLayout.PresetConstraints.TryGetValue(preset, out (string h, string v) pair))
                pair = (LayoutConstraints.Left, LayoutConstraints.Top);

            var go = new GameObject("__migrate", typeof(RectTransform));
            try
            {
                var rect = (RectTransform)go.transform;
                // replay the generator's legacy free path (ApplyCommonOverrides !inLayout branch)
                if (element.size != null && element.size.Length >= 2)
                    rect.sizeDelta = new Vector2(element.size[0], element.size[1]);
                UIWidgetFactory.TryApplyAnchor(rect, preset);
                if (element.size != null && element.size.Length >= 2)
                {
                    Vector2 cur = rect.sizeDelta;
                    rect.sizeDelta = new Vector2(
                        element.size[0] > 0f ? element.size[0] : cur.x,
                        element.size[1] > 0f ? element.size[1] : cur.y);
                }
                if (element.position != null && element.position.Length >= 2)
                    rect.anchoredPosition = new Vector2(element.position[0], element.position[1]);

                return DetectAxes(rect, pair.h, pair.v);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// In-layout child: size → fixed sizing on the sized axes; flex → fill on the parent's main
        /// axis. (The placement constraint is owned by the layout group, so only sizing migrates.)
        /// </summary>
        private static LayoutSpec BuildLayoutInLayout(ElementSpec element)
        {
            var layout = new LayoutSpec();
            if (element.size != null && element.size.Length >= 2)
            {
                var size = new LayoutSize();
                var sizing = new LayoutSizing();
                if (element.size[0] > 0f) { size.w = element.size[0]; sizing.w = LayoutSizingModes.Fixed; }
                if (element.size[1] > 0f) { size.h = element.size[1]; sizing.h = LayoutSizingModes.Fixed; }
                if (!size.IsEmpty) layout.size = size;
                if (!sizing.IsEmpty) layout.sizing = sizing;
            }
            if (element.flex.HasValue && element.flex.Value > 0f)
            {
                // legacy flex collapsed to one number; the generator re-derived the axis from the
                // parent stack. We can't see the parent here, so mark fill on both axes' sizing — the
                // generator's per-child fill applies harmlessly on the non-main axis. Conservative:
                // only set the axis that has no fixed size.
                layout.sizing = layout.sizing ?? new LayoutSizing();
                if (string.IsNullOrEmpty(layout.sizing.w)) layout.sizing.w = LayoutSizingModes.Fill;
                if (string.IsNullOrEmpty(layout.sizing.h)) layout.sizing.h = LayoutSizingModes.Fill;
            }
            return layout.IsEmpty ? null : layout;
        }

        private static LayoutSpec DetectAxes(RectTransform rect, string hId, string vId)
        {
            var spec = new LayoutSpec { h = hId, v = vId, offset = new LayoutOffset(), size = new LayoutSize() };

            DetectInto(rect, hId, LayoutAxis.Horizontal, spec);
            DetectInto(rect, vId, LayoutAxis.Vertical, spec);

            if (spec.offset.IsEmpty) spec.offset = null;
            if (spec.size.IsEmpty) spec.size = null;
            return spec;
        }

        private static void DetectInto(RectTransform rect, string id, LayoutAxis axis, LayoutSpec spec)
        {
            ILayoutConstraint c = LayoutConstraints.Get(id, axis);
            if (c == null) return;
            if (!c.TryDetect(rect, out LayoutOffsetValue value, out float? size)) return;
            WriteOffset(spec.offset, c, axis, value);
            if (!c.Stretches && size.HasValue && size.Value > 0f)
            {
                if (axis == LayoutAxis.Horizontal) spec.size.w = size.Value;
                else spec.size.h = size.Value;
            }
        }

        private static void WriteOffset(LayoutOffset offset, ILayoutConstraint constraint, LayoutAxis axis,
            LayoutOffsetValue value)
        {
            switch (constraint.Id)
            {
                case LayoutConstraints.Left: offset.Set("left", value.primary); break;
                case LayoutConstraints.Right: offset.Set("right", value.primary); break;
                case LayoutConstraints.Top: offset.Set("top", value.primary); break;
                case LayoutConstraints.Bottom: offset.Set("bottom", value.primary); break;
                case LayoutConstraints.Center:
                    offset.Set(axis == LayoutAxis.Horizontal ? "h" : "v", value.primary); break;
                case LayoutConstraints.LeftRight:
                    offset.Set("left", value.primary); offset.Set("right", value.secondary); break;
                case LayoutConstraints.TopBottom:
                    offset.Set("top", value.primary); offset.Set("bottom", value.secondary); break;
                case LayoutConstraints.Scale:
                    if (axis == LayoutAxis.Horizontal) { offset.Set("left", value.primary); offset.Set("right", value.secondary); }
                    else { offset.Set("bottom", value.primary); offset.Set("top", value.secondary); }
                    break;
            }
        }
    }
}
