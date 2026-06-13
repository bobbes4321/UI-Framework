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

        private IMGUIContainer _treeGui;
        private IMGUIContainer _previewGui;
        private IMGUIContainer _inspectorGui;
        private Label _statusLabel;

        private FlowGraph _flowGraph; // transient graph the flow window edits on this document's behalf

        [MenuItem("Tools/Neo UI/Composer", priority = 11)]
        public static void Open() => GetWindow<NeoComposerWindow>("Neo Composer");

        private void OnEnable()
        {
            if (_document == null) _document = new SpecDocument();
            _tree = new SpecTreeView(_document);
            _preview = new SpecPreviewPane(_document, () => _previewGui?.MarkDirtyRepaint());
            _inspector = new SpecInspector(_document, ReselectPath, OpenFlow);

            _document.Changed += OnDocumentChanged;
            _tree.SelectionChanged += OnSelectionChanged;
            EditorApplication.update += OnUpdate;

            BuildUI();
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            _document.Changed -= OnDocumentChanged;
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

            var outer = new TwoPaneSplitView(0, 250f, TwoPaneSplitViewOrientation.Horizontal);
            outer.style.flexGrow = 1f;
            rootVisualElement.Add(outer);

            _treeGui = new IMGUIContainer(DrawTree);
            _treeGui.style.minWidth = 180f;
            outer.Add(WrapWithHeader("Spec", _treeGui, BuildTreeToolbar()));

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
            bar.Add(new ToolbarButton(() => _tree.AddSettingsFromToolbar()) { text = "+ Settings" });
            bar.Add(new ToolbarButton(() => _tree.AddCheatsFromToolbar()) { text = "+ Cheats" });
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

        private void DrawPreview()
        {
            SyncPreviewTarget();
            _preview.OnGUI(LocalRect(_previewGui));
        }

        private void DrawInspector() => _inspector.OnGUI(LocalRect(_inspectorGui), _tree.Selected);

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

        private void ReselectPath(string path)
        {
            _tree.Select(path);
            OnSelectionChanged();
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;
            string file = string.IsNullOrEmpty(_document.FilePath) ? "(unsaved — no file)" : _document.FilePath;
            _statusLabel.text = (_document.Dirty ? "● " : "") + file;
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
            GenerateReport report = _document.Save(out string error);
            if (error != null) { EditorUtility.DisplayDialog("Save Failed", error, "OK"); return; }
            UpdateStatus();
            if (report != null && report.hasProblems)
                EditorUtility.DisplayDialog("Saved with issues",
                    "Generated assets, but the generator reported:\n\n" + report, "OK");
            else
                ShowNotification(new GUIContent("Saved + regenerated"));
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
