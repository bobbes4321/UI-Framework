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

            DrawDelayedText("Category", view.category, v => Apply(() => view.category = Sanitize(v, "View"), "Edit View",
                SpecPath.View($"{Sanitize(v, "View")}/{view.viewName}")));
            DrawDelayedText("Name", view.viewName, v => Apply(() => view.viewName = Sanitize(v, "Main"), "Edit View",
                SpecPath.View($"{view.category}/{Sanitize(v, "Main")}")));

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
            NeoGUI.Splitter();

            bool buttonWithVariant = element.kind == "button" && !string.IsNullOrEmpty(element.sizeVariant);
            foreach (SpecField field in SpecFieldCatalog.For(element.kind))
            {
                // polymorphic "size": a button with a size-variant owns the "size" key — don't also
                // draw the [w,h] array (they'd fight over the same JSON key)
                if (field.key == "size" && buttonWithVariant) continue;
                DrawField(element, field);
            }

            if (HasOnClick(element.kind)) DrawOnClick(element);

            EditorGUILayout.Space(4f);
            DrawAddChildRow(element.children, node.path);
        }

        private static bool HasOnClick(string kind) => kind == "button" || kind == "toggle" || kind == "tab";

        private void DrawOnClick(ElementSpec e)
        {
            if (!NeoGUI.BeginFoldoutSection($"neo.composer.onclick", "On Click", null, true)) { NeoGUI.EndFoldoutSection(); return; }

            DrawPopupRow("Show View", e.onClickShowView, () => ComposerOptions.ViewIds(Spec),
                v => Apply(() => e.onClickShowView = Empty(v), "Edit On Click"));
            DrawPopupRow("Hide View", e.onClickHideView, () => ComposerOptions.ViewIds(Spec),
                v => Apply(() => e.onClickHideView = Empty(v), "Edit On Click"));
            DrawPopupRow("Open Popup", e.onClickPopup, () => ComposerOptions.PopupNames(Spec),
                v => Apply(() => e.onClickPopup = Empty(v), "Edit On Click"));
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
                    DrawPopupRow(f.label, (string)f.get(e), () => ComposerOptions.ViewIds(Spec), v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.PopupRef:
                    DrawPopupRow(f.label, (string)f.get(e), () => ComposerOptions.PopupNames(Spec), v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.PanelRef:
                    DrawPopupRow(f.label, (string)f.get(e), () => CurrentViewPanels(), v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.DataRef:
                    DrawDelayedText(f.label, (string)f.get(e), v => Apply(() => f.set(e, Empty(v)), "Edit " + f.label));
                    break;
                case FieldKind.StringList:
                    DrawStringList(f.label, (List<string>)f.get(e), v => Apply(() => f.set(e, v), "Edit " + f.label));
                    break;
            }
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
            NeoDropdown.ValuePopup(rect, "", () => new List<string>(ElementSpec.Kinds),
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
            }, "Move Element");
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
