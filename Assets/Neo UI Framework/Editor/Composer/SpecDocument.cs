using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// The Composer's live document: a <see cref="UISpec"/> held in memory. This is the source of
    /// truth — the prefab is only a preview. Every GUI edit flows through <see cref="ApplyEdit"/>,
    /// which snapshots for undo, runs the mutation, marks dirty and notifies listeners (the panes
    /// rebuild; the preview rebuild is debounced by the window). Because the document is always a
    /// spec and we never reverse-engineer the prefab, everything done here round-trips losslessly.
    ///
    /// <para>Undo/redo are deterministic JSON snapshots (<see cref="UISpec.ToJson"/>) — cheap and
    /// exact, and they survive the spec's managed-reference shape changing under edits.</para>
    /// </summary>
    public class SpecDocument
    {
        private const int MaxHistory = 128;

        /// <summary> Where this document came from — it decides how Save materializes.
        /// <see cref="Project"/> documents (loaded via "Open Project") are a derivative of the live
        /// committed UI, so Save merges them back through the safe <see cref="SpecBaseline.Sync"/>
        /// protocol (never clobbering prefab edits, never deleting screens the doc happens not to
        /// mention). <see cref="New"/>/<see cref="File"/> documents are authored standalone, so the
        /// doc IS the intended whole and Save generates from it directly. </summary>
        public enum DocumentOrigin { New, File, Project }

        public UISpec Spec { get; private set; }

        /// <summary> Backing JSON file (under Assets) this document was loaded from / saves to.
        /// Null for a brand-new doc or one loaded from the live project until first "Save As". </summary>
        public string FilePath { get; private set; }

        public DocumentOrigin Origin { get; private set; } = DocumentOrigin.New;

        public bool Dirty { get; private set; }

        /// <summary> Raised after any structural change (edit / undo / redo / load) so the panes can
        /// rebuild. The preview pane debounces its own heavy rebuild off this. </summary>
        public event Action Changed;

        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();

        private string _activeBreakpoint = "";

        public SpecDocument()
        {
            Spec = NewEmptySpec();
        }

        // ------------------------------------------------------------------ active edit breakpoint

        /// <summary>
        /// Raised when the active edit breakpoint changes (the breakpoint bar sets it; the inspector and
        /// preview repaint off it). Separate from <see cref="Changed"/> because it is not a document
        /// mutation — no undo snapshot, no dirty flag.
        /// </summary>
        public event Action ActiveBreakpointChanged;

        /// <summary>
        /// The breakpoint name layout edits are currently scoped to (Pillar B). Empty/null = the base
        /// layout (edits write the element's base, as before); a non-empty name routes layout edits into
        /// <c>ElementSpec.overrides[name]</c> and tells the preview which condition to show. The active
        /// scope is editor session state, NOT part of the spec — so it never serializes and never
        /// round-trips.
        /// </summary>
        public string ActiveBreakpoint => _activeBreakpoint ?? "";

        /// <summary> True when a non-base breakpoint is the edit scope. </summary>
        public bool IsEditingOverride => !string.IsNullOrEmpty(_activeBreakpoint);

        /// <summary>
        /// Sets the active edit breakpoint. Empty/null selects base. A non-empty name that no
        /// <see cref="UISpec.breakpoints"/> entry declares is rejected with a warning (no silent
        /// failure) and the scope falls back to base.
        /// </summary>
        public void SetActiveBreakpoint(string breakpoint)
        {
            string next = breakpoint ?? "";
            if (!string.IsNullOrEmpty(next) && !BreakpointExists(next))
            {
                Debug.LogWarning($"SpecDocument.SetActiveBreakpoint: no breakpoint named '{next}' in the spec; staying on base.");
                next = "";
            }
            if (string.Equals(next, _activeBreakpoint ?? "", StringComparison.Ordinal)) return;
            _activeBreakpoint = next;
            ActiveBreakpointChanged?.Invoke();
        }

        private bool BreakpointExists(string name)
        {
            if (Spec?.breakpoints == null) return false;
            foreach (BreakpointSpec bp in Spec.breakpoints)
                if (bp != null && string.Equals(bp.name, name, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary> A minimal valid document: one empty view so the tree and preview have something
        /// to show. </summary>
        public static UISpec NewEmptySpec()
        {
            var spec = new UISpec();
            spec.views.Add(new ViewSpec { category = "Menu", viewName = "Main" });
            return spec;
        }

        // ------------------------------------------------------------------ mutation choke point

        /// <summary>
        /// The single entry point for mutating the document. Snapshots the pre-edit state for undo,
        /// applies <paramref name="mutate"/>, clears the redo stack, marks dirty and raises
        /// <see cref="Changed"/>. Never mutate <see cref="Spec"/> outside this method.
        /// </summary>
        public void ApplyEdit(Action mutate, string label)
        {
            if (mutate == null) return;
            PushHistory(_undo);
            _redo.Clear();
            mutate();
            Dirty = true;
            Changed?.Invoke();
        }

        private void PushHistory(List<string> stack)
        {
            stack.Add(Spec.ToJson());
            if (stack.Count > MaxHistory) stack.RemoveAt(0);
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Undo()
        {
            if (_undo.Count == 0) return;
            PushHistory(_redo);
            RestoreFrom(_undo);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            PushHistory(_undo);
            RestoreFrom(_redo);
        }

        private void RestoreFrom(List<string> stack)
        {
            string json = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            Spec = UISpec.FromJson(json);
            Dirty = true;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ load

        /// <summary> Replaces the document, clearing history. Used by all the open paths. </summary>
        public void Load(UISpec spec, string filePath, DocumentOrigin origin = DocumentOrigin.New)
        {
            Spec = spec ?? NewEmptySpec();
            FilePath = filePath;
            Origin = origin;
            _undo.Clear();
            _redo.Clear();
            Dirty = false;
            // the incoming spec may not declare the previously-selected breakpoint — reset to base scope
            // (raises ActiveBreakpointChanged only when it actually was non-base)
            SetActiveBreakpoint("");
            Changed?.Invoke();
        }

        public void LoadFromFile(string path)
        {
            Load(UISpec.FromJson(File.ReadAllText(path)), path, DocumentOrigin.File);
        }

        /// <summary> "Open Current Project": seed the document from whatever assets already exist, so a
        /// human can start authoring from the live state. The file path is left null — the project is
        /// the source, not a particular spec file — so Save prompts for a location. Marked
        /// <see cref="DocumentOrigin.Project"/> so Save folds the edits back through the safe sync. </summary>
        public void LoadCurrentProject()
        {
            Load(UISpecExporter.ExportProject(), null, DocumentOrigin.Project);
        }

        // ------------------------------------------------------------------ save

        /// <summary> True when Save should take the safe, project-aware merge path. </summary>
        public bool SavesThroughSync => Origin == DocumentOrigin.Project;

        /// <summary>
        /// Optional isolated generated-asset root this document saves into (a showcase's
        /// <c>Assets/Showcases/{id}/Generated</c>). When set, <see cref="GenerateSave"/> and
        /// <see cref="SyncSave"/> run inside <see cref="NeoWorkspace.Scoped(string,string)"/> so the
        /// generate/sync — and the <c>.neo-baseline.json</c> that follows
        /// <see cref="UISpecGenerator.GeneratedRoot"/> — land in the showcase folder instead of the
        /// default root. Null ⇒ default root (plain Composer use). Independent of <see cref="Load"/>,
        /// so it survives the in-document reload <see cref="SyncSave"/> does on success; the window
        /// clears it when the user opens a different (non-showcase) document.
        /// </summary>
        public string WorkspaceRoot { get; private set; }

        /// <summary> Points this document's saves at an isolated showcase root (null ⇒ default root). </summary>
        public void SetWorkspaceRoot(string root) => WorkspaceRoot = root;

        /// <summary> Scopes generate/sync to <see cref="WorkspaceRoot"/> when set; a no-op otherwise
        /// (boxes the NeoWorkspace struct to IDisposable — fine for a one-off save). </summary>
        private IDisposable BeginWorkspace() =>
            string.IsNullOrEmpty(WorkspaceRoot) ? null : NeoWorkspace.Scoped(WorkspaceRoot);

        /// <summary>
        /// Standalone save (New/File documents): the doc IS the whole intended spec, so serialize it to
        /// <see cref="FilePath"/> and generate the committed assets from it directly. Returns the
        /// generator report, or null with <paramref name="error"/> set when the write failed.
        /// </summary>
        public GenerateReport GenerateSave(out string error)
        {
            error = null;
            if (!WriteSpecFile(Spec, out error)) return null;
            GenerateReport report;
            using (BeginWorkspace())
                report = UISpecGenerator.Generate(Spec);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Dirty = false;
            return report;
        }

        /// <summary>
        /// Safe save for a document opened FROM the project (the scenario the Composer is built for —
        /// adding to existing UI). Routes through <see cref="SpecBaseline.Sync"/>: it exports the live
        /// project, three-way merges (base = baseline, ours = live project + any prefab drift,
        /// theirs = THIS document) and regenerates from the merge — so a designer who added one setting
        /// can't wipe a teammate's prefab tweak, and screens the doc doesn't mention are never deleted.
        ///
        /// <para>Refuses (writes nothing, <see cref="SyncResult.refused"/>) when off-spec editor edits
        /// would be silently lost, unless <paramref name="force"/>. On success the MERGED spec (the new
        /// truth) is written to <see cref="FilePath"/> and folded back into the live document.</para>
        /// </summary>
        public SyncResult SyncSave(bool force, out string error)
        {
            error = null;
            SyncResult result;
            try
            {
                using (BeginWorkspace())
                    result = SpecBaseline.Sync(Spec, ConflictPolicy.PreferTheirs, force);
            }
            catch (Exception e) { error = e.Message; return null; }

            if (result.refused) return result; // nothing written — the window offers Force

            // Sync already rewrote the canonical baseline + the assets. A separate spec file is optional
            // for a project doc — only write it when the designer chose a path (Save As). Then reflect
            // the MERGE back into the document so the Composer shows exactly what was regenerated.
            if (!string.IsNullOrEmpty(FilePath) && !WriteSpecFile(result.merged ?? Spec, out error)) return result;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (result.merged != null) Load(result.merged, FilePath, DocumentOrigin.Project);
            Dirty = false;
            return result;
        }

        private bool WriteSpecFile(UISpec spec, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(FilePath)) { error = "No file path — use Save As first."; return false; }
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(FilePath, spec.ToJson());
                AssetDatabase.ImportAsset(ToAssetPath(FilePath));
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        public void SetFilePath(string path) => FilePath = path;

        /// <summary> Mirrors an edited flow back into the document. Flow editing happens in the flow
        /// window, which keeps its OWN undo stack, so this does not push onto the Composer's history
        /// (that would double-count) — it just updates the spec and marks dirty. </summary>
        public void ReplaceFlow(FlowSpec flow)
        {
            Spec.flow = flow;
            Dirty = true;
            Changed?.Invoke();
        }

        /// <summary> Converts an absolute or project-relative path to a "Assets/..."-rooted path so
        /// <see cref="AssetDatabase"/> recognizes it (no-op when already relative). </summary>
        private static string ToAssetPath(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string root = Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/');
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length).TrimStart('/')
                : path;
        }
    }
}
