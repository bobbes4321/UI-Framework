using System.IO;
using Neo.EditorUI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// The Neo UI Composer: a spec-authoring editor window whose live document is a <see cref="UISpec"/>
    /// in memory (<see cref="SpecDocument"/>). Every GUI edit mutates that spec; the center pane is a
    /// preview, never the source of truth — so everything done here round-trips losslessly. Save is the
    /// only moment assets are written (spec JSON + a generator run).
    ///
    /// <para>Three panes (like the flow window): tree (left) · live preview (center) · inspector
    /// (right). All chrome goes through the EditorUI kit.</para>
    /// </summary>
    public class NeoComposerWindow : EditorWindow
    {
        private SpecDocument _document;
        private SpecTreeView _tree;
        private SpecInspector _inspector;
        private SpecPreviewPane _preview;
        private BreakpointBar _breakpointBar;
        private PalettePane _palette;

        private IMGUIContainer _treeGui;
        private IMGUIContainer _previewGui;
        private IMGUIContainer _inspectorGui;
        private IMGUIContainer _breakpointGui;
        private IMGUIContainer _paletteGui;
        private Label _statusLabel;

        private FlowGraph _flowGraph; // transient graph the flow window edits on this document's behalf

        [MenuItem("Tools/Neo UI/Composer", priority = 11)]
        public static void Open() => GetWindow<NeoComposerWindow>("Neo Composer");

        private void OnEnable()
        {
            if (_document == null) _document = new SpecDocument();
            _tree = new SpecTreeView(_document);
            _preview = new SpecPreviewPane(_document, () => _previewGui?.MarkDirtyRepaint(), ReselectPath);
            _inspector = new SpecInspector(_document, ReselectPath, OpenFlow);
            _breakpointBar = new BreakpointBar(_document);
            _palette = new PalettePane(AddKindToCurrentView);

            _document.Changed += OnDocumentChanged;
            _document.ActiveBreakpointChanged += OnActiveBreakpointChanged;
            _tree.SelectionChanged += OnSelectionChanged;
            EditorApplication.update += OnUpdate;

            BuildUI();
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            _document.Changed -= OnDocumentChanged;
            _document.ActiveBreakpointChanged -= OnActiveBreakpointChanged;
            if (_tree != null) _tree.SelectionChanged -= OnSelectionChanged;
            EditorApplication.update -= OnUpdate;
            _preview?.Dispose();
            if (_flowGraph != null) { DestroyImmediate(_flowGraph); _flowGraph = null; }
        }

        private void OnUpdate() => _preview?.Update();

        // ------------------------------------------------------------------ layout

        private void BuildUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(BuildToolbar());

            // breakpoint authoring bar (Pillar B "B-ui"): scope selector + manager
            _breakpointGui = new IMGUIContainer(DrawBreakpointBar);
            rootVisualElement.Add(_breakpointGui);

            var outer = new TwoPaneSplitView(0, 250f, TwoPaneSplitViewOrientation.Horizontal);
            outer.style.flexGrow = 1f;
            rootVisualElement.Add(outer);

            // Left pane: the spec tree on top, the widget palette as a collapsible strip below — a vertical
            // split so the existing three-pane (tree · preview · inspector) horizontal layout is untouched.
            var leftSplit = new TwoPaneSplitView(1, 200f, TwoPaneSplitViewOrientation.Vertical);
            leftSplit.style.flexGrow = 1f;
            leftSplit.style.minWidth = 180f;

            _treeGui = new IMGUIContainer(DrawTree);
            _treeGui.style.flexGrow = 1f;
            leftSplit.Add(WrapWithHeader("Spec", _treeGui, BuildTreeToolbar()));

            _paletteGui = new IMGUIContainer(DrawPalette);
            _paletteGui.style.flexGrow = 1f;
            _paletteGui.style.minHeight = 80f;
            leftSplit.Add(WrapWithHeader("Widgets", _paletteGui, null));

            outer.Add(leftSplit);
            _palette.OnOpen();

            var inner = new TwoPaneSplitView(1, 320f, TwoPaneSplitViewOrientation.Horizontal);
            inner.style.flexGrow = 1f;
            outer.Add(inner);

            _previewGui = new IMGUIContainer(DrawPreview);
            _previewGui.style.flexGrow = 1f;
            inner.Add(_previewGui);

            _inspectorGui = new IMGUIContainer(DrawInspector);
            _inspectorGui.style.minWidth = 260f;
            inner.Add(_inspectorGui);

            _statusLabel = new Label();
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            _statusLabel.style.paddingLeft = 6f;
            _statusLabel.style.paddingRight = 6f;
            _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            rootVisualElement.Add(_statusLabel);
            UpdateStatus();
        }

        private Toolbar BuildToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(NewDocument) { text = "New" });
            toolbar.Add(new ToolbarButton(OpenFile) { text = "Open…" });
            toolbar.Add(new ToolbarButton(OpenCurrentProject) { text = "Open Project" });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(Save) { text = "Save" });
            toolbar.Add(new ToolbarButton(SaveAs) { text = "Save As…" });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(() => { _document.Undo(); }) { text = "Undo" });
            toolbar.Add(new ToolbarButton(() => { _document.Redo(); }) { text = "Redo" });
            var spacer = new ToolbarSpacer { style = { flexGrow = 1f } };
            toolbar.Add(spacer);
            return toolbar;
        }

        private VisualElement BuildTreeToolbar()
        {
            var bar = new Toolbar();
            bar.Add(new ToolbarButton(() => _tree.AddViewFromToolbar()) { text = "+ View" });
            bar.Add(new ToolbarButton(() => _tree.AddPopupFromToolbar()) { text = "+ Popup" });
            // one neutral "+ Menu ▾" picker over every registered catalog kind (settings, cheats, …)
            // — no product opinion baked into the chrome, and a project's registered kind shows up here.
            ToolbarButton menuButton = null;
            menuButton = new ToolbarButton(() =>
                NeoSearchablePopup.Show(menuButton.worldBound, null, SpecTreeView.CatalogKindLabels(),
                    label =>
                    {
                        string id = SpecTreeView.CatalogKindIdForLabel(label);
                        if (id != null) _tree.AddCatalogFromToolbar(id);
                    }))
            { text = "+ Menu ▾" };
            bar.Add(menuButton);

            // "New from template ▾": curated scaffolds (main menu, settings, HUD, pause, popup) + project
            // templates, inserted into the current document collision-safe (Pillar E).
            ToolbarButton templateButton = null;
            templateButton = new ToolbarButton(() => ShowTemplatePicker(templateButton.worldBound))
            { text = "+ Template ▾" };
            bar.Add(templateButton);
            return bar;
        }

        private static VisualElement WrapWithHeader(string title, VisualElement body, VisualElement toolbar)
        {
            var root = new VisualElement { style = { flexGrow = 1f } };
            if (toolbar != null) root.Add(toolbar);
            body.style.flexGrow = 1f;
            root.Add(body);
            return root;
        }

        // ------------------------------------------------------------------ pane draws

        private void DrawTree() => _tree.OnGUI(LocalRect(_treeGui));

        private void DrawPalette() => _palette.OnGUI(LocalRect(_paletteGui));

        private void DrawPreview()
        {
            SyncPreviewTarget();
            _preview.OnGUI(LocalRect(_previewGui));
        }

        private void DrawInspector() => _inspector.OnGUI(LocalRect(_inspectorGui), _tree.Selected);

        private void DrawBreakpointBar() => _breakpointBar.OnGUI();

        private static Rect LocalRect(IMGUIContainer container) =>
            new Rect(0f, 0f, container.contentRect.width, container.contentRect.height);

        private void SyncPreviewTarget()
        {
            SpecNode node = _tree.Selected;
            ViewSpec view = node?.view;
            if (view == null && node != null && node.kind == SpecNodeKind.View) view = node.view;
            if (view == null && _document.Spec.views.Count > 0) view = _document.Spec.views[0];
            _preview.SetTarget(view, node?.element);
        }

        // ------------------------------------------------------------------ events

        private void OnDocumentChanged()
        {
            _tree.MarkDirty();
            _preview.RequestRebuild();
            _treeGui?.MarkDirtyRepaint();
            _previewGui?.MarkDirtyRepaint();
            _inspectorGui?.MarkDirtyRepaint();
            UpdateStatus();
        }

        private void OnSelectionChanged()
        {
            _treeGui?.MarkDirtyRepaint();
            _inspectorGui?.MarkDirtyRepaint();
            _previewGui?.MarkDirtyRepaint();
        }

        // active edit breakpoint changed: re-scope the inspector's layout edits + reflect in the bar.
        // The preview wires to SpecDocument.ActiveBreakpoint in a later wave (it drives IActiveBreakpoint).
        private void OnActiveBreakpointChanged()
        {
            _breakpointGui?.MarkDirtyRepaint();
            _inspectorGui?.MarkDirtyRepaint();
            _previewGui?.MarkDirtyRepaint();
        }

        private void ReselectPath(string path)
        {
            _tree.Select(path);
            OnSelectionChanged();
        }

        // Palette click-to-add: place the kind relative to the current selection, mirroring drag-to-tree.
        //   • a selected container element  → nest it as a CHILD (VStack selected → button goes inside);
        //   • a selected leaf element       → insert it as a following SIBLING in that leaf's own list
        //                                      (so it stays in the right view/popup/parent, not view root);
        //   • a selected view/popup         → append to that surface's elements;
        //   • nothing selected              → append to the first view (create one first, else warn).
        // Every branch routes through ApplyEdit and reselects the freshly added node.
        private void AddKindToCurrentView(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return;
            SpecNode node = _tree.Selected;

            if (node != null && node.kind == SpecNodeKind.Element && node.element != null)
            {
                if (ComposerCanvas.IsContainerKind(node.element.kind))
                {
                    int childIndex = node.element.children.Count;
                    _document.ApplyEdit(() => node.element.children.Add(ComposerFactory.NewElement(kind)), $"Add {kind}");
                    ReselectPath(node.path + $"/children[{childIndex}]");
                    return;
                }
                if (node.siblings != null)
                {
                    int at = node.index + 1;
                    _document.ApplyEdit(() => node.siblings.Insert(at, ComposerFactory.NewElement(kind)), $"Add {kind}");
                    ReselectPath(ListPathOf(node.path) + $"[{at}]");
                    return;
                }
            }

            if (node != null && node.kind == SpecNodeKind.Popup && node.popup != null)
            {
                PopupSpec popup = node.popup;
                _document.ApplyEdit(() => popup.elements.Add(ComposerFactory.NewElement(kind)), $"Add {kind}");
                ReselectPath(node.path + $"/elements[{popup.elements.Count - 1}]");
                return;
            }

            ViewSpec view = node?.view ?? (_document.Spec.views.Count > 0 ? _document.Spec.views[0] : null);
            if (view == null)
            {
                Debug.LogWarning("[Composer] No view to add a widget to — create a view first.");
                return;
            }
            _document.ApplyEdit(() => view.elements.Add(ComposerFactory.NewElement(kind)), $"Add {kind}");
            ReselectPath(SpecPath.View(view.id) + $"/elements[{view.elements.Count - 1}]");
        }

        // Strips the trailing "[index]" off an element path to recover the list it lives in
        // (e.g. ".../elements[2]" → ".../elements") so a new sibling can be addressed in that same list.
        private static string ListPathOf(string elementPath)
        {
            int bracket = elementPath.LastIndexOf('[');
            return bracket < 0 ? elementPath : elementPath.Substring(0, bracket);
        }

        // ------------------------------------------------------------------ templates

        // "New from template ▾": stamp a curated scaffold into the document (collision-safe, via ApplyEdit).
        private void ShowTemplatePicker(Rect anchor)
        {
            var labels = new System.Collections.Generic.List<string>();
            foreach (TemplateEntry t in ComposerTemplates.All) labels.Add(t.label);
            if (labels.Count == 0) return;
            NeoSearchablePopup.Show(anchor, null, labels, InsertTemplateByLabel);
        }

        private void InsertTemplateByLabel(string label)
        {
            foreach (TemplateEntry t in ComposerTemplates.All)
            {
                if (t.label != label) continue;
                string select = ComposerTemplates.Insert(_document, t, out var warnings);
                if (warnings != null && warnings.Count > 0)
                    ShowNotification(new GUIContent($"Inserted “{t.label}” — {warnings.Count} name clash(es) renamed"));
                else
                    ShowNotification(new GUIContent($"Inserted template “{t.label}”"));
                if (select != null) ReselectPath(select);
                return;
            }
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;
            string file = string.IsNullOrEmpty(_document.FilePath) ? "(unsaved — no file)" : _document.FilePath;
            string mode = _document.SavesThroughSync ? "   ·   Project — Save merges safely into the live UI" : "";
            _statusLabel.text = (_document.Dirty ? "● " : "") + file + mode;
        }

        // ------------------------------------------------------------------ commands

        private void NewDocument()
        {
            if (!ConfirmDiscard()) return;
            _document.Load(SpecDocument.NewEmptySpec(), null);
        }

        private void OpenFile()
        {
            if (!ConfirmDiscard()) return;
            string path = EditorUtility.OpenFilePanel("Open UI Spec", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            try { _document.LoadFromFile(path); }
            catch (System.Exception e) { EditorUtility.DisplayDialog("Open Failed", e.Message, "OK"); }
        }

        private void OpenCurrentProject()
        {
            if (!ConfirmDiscard()) return;
            _document.LoadCurrentProject();
        }

        private void Save()
        {
            // a project doc records itself in the baseline (rewritten by the safe sync) — no spec file
            // is required, so don't make the designer pick one just to apply an edit
            if (_document.SavesThroughSync) { DoSave(); return; }
            if (string.IsNullOrEmpty(_document.FilePath)) { SaveAs(); return; }
            DoSave();
        }

        private void SaveAs()
        {
            string path = EditorUtility.SaveFilePanel("Save UI Spec", Application.dataPath, "ui-spec", "json");
            if (string.IsNullOrEmpty(path)) return;
            _document.SetFilePath(path);
            DoSave();
        }

        private void DoSave()
        {
            if (_document.SavesThroughSync) DoSyncSave();
            else DoGenerateSave();
            UpdateStatus();
        }

        // standalone (New/File) document — the doc is the whole intended spec, generate from it
        private void DoGenerateSave()
        {
            GenerateReport report = _document.GenerateSave(out string error);
            if (error != null) { EditorUtility.DisplayDialog("Save Failed", error, "OK"); return; }
            if (report != null && report.hasProblems)
                EditorUtility.DisplayDialog("Saved with issues",
                    "Generated assets, but the generator reported:\n\n" + report, "OK");
            else
                ShowNotification(new GUIContent("Saved + regenerated"));
        }

        // project document — fold the edits back through the safe merge so prefab edits elsewhere are
        // preserved and screens this doc doesn't mention are never deleted
        private void DoSyncSave()
        {
            SyncResult result = _document.SyncSave(false, out string error);
            if (error != null) { EditorUtility.DisplayDialog("Save Failed", error, "OK"); return; }

            if (result != null && result.refused)
            {
                bool force = EditorUtility.DisplayDialog("Some edits can't round-trip",
                    result.note + "\n\n" + Bullet(result.offSpecWarnings, f => f.ToString(), 8) +
                    "\n\nForce regenerate and lose them, or cancel and fix them first?",
                    "Force Regenerate", "Cancel");
                if (!force) return;
                result = _document.SyncSave(true, out error);
                if (error != null) { EditorUtility.DisplayDialog("Save Failed", error, "OK"); return; }
            }
            SummarizeSync(result);
        }

        private void SummarizeSync(SyncResult result)
        {
            if (result == null) return;
            if (result.conflicts != null && result.conflicts.Count > 0)
                EditorUtility.DisplayDialog("Saved — review conflicts",
                    $"{result.conflicts.Count} node(s) were changed both in the project and in your edits. " +
                    "Your edits won by default:\n\n" + Bullet(result.conflicts, c => c.path, 10), "OK");
            else if (result.generateReport != null && result.generateReport.hasProblems)
                EditorUtility.DisplayDialog("Saved with issues",
                    "Regenerated, but the generator reported:\n\n" + result.generateReport, "OK");
            else
            {
                int merged = result.applied?.Count ?? 0;
                ShowNotification(new GUIContent(merged > 0
                    ? $"Saved — merged {merged} project edit(s) with yours"
                    : "Saved + regenerated"));
            }
        }

        private static string Bullet<T>(System.Collections.Generic.List<T> items, System.Func<T, string> text, int max)
        {
            if (items == null || items.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < items.Count && i < max; i++) sb.Append("• ").Append(text(items[i])).Append('\n');
            if (items.Count > max) sb.Append($"…and {items.Count - max} more");
            return sb.ToString();
        }

        private bool ConfirmDiscard()
        {
            if (!_document.Dirty) return true;
            return EditorUtility.DisplayDialog("Discard changes?",
                "The current document has unsaved changes. Discard them?", "Discard", "Cancel");
        }

        // ------------------------------------------------------------------ flow

        private void OpenFlow()
        {
            if (_document.Spec.flow == null)
                _document.ApplyEdit(() => _document.Spec.flow = new FlowSpec { name = "UI" }, "Create Flow");

            if (_flowGraph != null) DestroyImmediate(_flowGraph);
            _flowGraph = ComposerFlowBridge.ToGraph(_document.Spec.flow);
            FlowGraphWindow.OpenForSpec(_flowGraph,
                () => _document.ReplaceFlow(ComposerFlowBridge.ToFlowSpec(_flowGraph)));
        }
    }
}
