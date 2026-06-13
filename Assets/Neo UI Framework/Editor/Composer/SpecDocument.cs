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

        public UISpec Spec { get; private set; }

        /// <summary> Backing JSON file (under Assets) this document was loaded from / saves to.
        /// Null for a brand-new doc or one loaded from the live project until first "Save As". </summary>
        public string FilePath { get; private set; }

        public bool Dirty { get; private set; }

        /// <summary> Raised after any structural change (edit / undo / redo / load) so the panes can
        /// rebuild. The preview pane debounces its own heavy rebuild off this. </summary>
        public event Action Changed;

        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();

        public SpecDocument()
        {
            Spec = NewEmptySpec();
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
        public void Load(UISpec spec, string filePath)
        {
            Spec = spec ?? NewEmptySpec();
            FilePath = filePath;
            _undo.Clear();
            _redo.Clear();
            Dirty = false;
            Changed?.Invoke();
        }

        public void LoadFromFile(string path)
        {
            Load(UISpec.FromJson(File.ReadAllText(path)), path);
        }

        /// <summary> "Open Current Project": seed the document from whatever assets already exist, so a
        /// human can start authoring from the live state. The file path is left null — the project is
        /// the source, not a particular spec file — so Save prompts for a location. </summary>
        public void LoadCurrentProject()
        {
            Load(UISpecExporter.ExportProject(), null);
        }

        // ------------------------------------------------------------------ save

        /// <summary>
        /// The ONLY moment assets are written: serialize the document to <see cref="FilePath"/> then
        /// run the generator over it, materializing the committed prefabs/assets. Returns the
        /// generator report (collisions/issues surfaced to the user), or null with
        /// <paramref name="error"/> set when the write itself failed.
        /// </summary>
        public GenerateReport Save(out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(FilePath))
            {
                error = "No file path — use Save As first.";
                return null;
            }
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(FilePath, Spec.ToJson());
                AssetDatabase.ImportAsset(ToAssetPath(FilePath));
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }

            GenerateReport report = UISpecGenerator.Generate(Spec);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Dirty = false;
            return report;
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
