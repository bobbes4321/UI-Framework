using System.Collections.Generic;
using System.IO;
using Neo.UI.Editor.Composer; // SpecDocument — the Composer document Insert() edits (dies with the window in Wave 3)
using UnityEngine;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// One curated scaffold the Composer can stamp into the current document — a small, valid
    /// <see cref="UISpec"/> fragment (one or more views/popups, plus any breakpoints it relies on)
    /// authored with the Figma-style <c>layout</c> model. <see cref="loadJson"/> is a lazy loader so a
    /// built-in template reads its JSON from <c>Templates~</c> only when actually inserted (the tilde
    /// folder is not imported as Unity assets — it's raw text on disk).
    /// </summary>
    public readonly struct TemplateEntry
    {
        /// <summary> Stable id (also the picker key). </summary>
        public readonly string id;
        /// <summary> Human label shown in the "New from template" picker. </summary>
        public readonly string label;
        /// <summary> One-line description (tooltip). </summary>
        public readonly string description;
        /// <summary> Returns the template's raw spec JSON. Lazy so built-ins touch disk only on use. </summary>
        public readonly System.Func<string> loadJson;

        public TemplateEntry(string id, string label, System.Func<string> loadJson, string description = null)
        {
            this.id = id;
            this.label = string.IsNullOrEmpty(label) ? id : label;
            this.description = description;
            this.loadJson = loadJson;
        }
    }

    /// <summary>
    /// The registry of insertable templates (Pattern R): the package built-ins shipped under
    /// <c>Editor/Authoring/Templates~/*.json</c> plus anything a consuming project registers via
    /// <see cref="Register"/>. The "New from template" picker reads <see cref="All"/>; insertion is
    /// performed by <see cref="Insert"/>, always through <see cref="SpecDocument.ApplyEdit"/>, with
    /// name-collision handling (warn + suffix, never silent overwrite).
    /// </summary>
    public static class NeoLayoutTemplates
    {
        private static readonly List<TemplateEntry> _registered = new List<TemplateEntry>();
        private static bool _builtinsSeeded;

        // built-in template files (under Templates~, loaded by File.ReadAllText)
        private static readonly (string id, string label, string file, string desc)[] Builtins =
        {
            ("main-menu",      "Main Menu",       "main-menu.json",       "Centered title + stacked menu buttons."),
            ("settings-screen","Settings Screen", "settings-screen.json", "Scrollable settings rows with a header bar."),
            ("hud",            "HUD",             "hud.json",             "Corner-anchored gameplay overlay (health, score, minimap slot)."),
            ("pause-menu",     "Pause Menu",      "pause-menu.json",      "Dimmed pause overlay with resume / settings / quit."),
            ("popup",          "Popup",           "popup.json",           "A rich confirm popup card with two actions."),
        };

        private static void EnsureBuiltins()
        {
            if (_builtinsSeeded) return;
            _builtinsSeeded = true;
            foreach (var b in Builtins)
            {
                string file = b.file;
                _registered.Add(new TemplateEntry(b.id, b.label, () => LoadBuiltin(file), b.desc));
            }
        }

        /// <summary> Every registered template (built-ins first, then project additions). </summary>
        public static IReadOnlyList<TemplateEntry> All
        {
            get { EnsureBuiltins(); return _registered; }
        }

        /// <summary> Finds a template by id. </summary>
        public static bool TryGet(string id, out TemplateEntry entry)
        {
            EnsureBuiltins();
            for (int i = 0; i < _registered.Count; i++)
                if (_registered[i].id == id) { entry = _registered[i]; return true; }
            entry = default;
            return false;
        }

        /// <summary> Registers (or replaces, by id) a template. The extension seam: a project adds its own
        /// scaffolds with one call. </summary>
        public static void Register(TemplateEntry entry)
        {
            EnsureBuiltins();
            if (string.IsNullOrEmpty(entry.id) || entry.loadJson == null)
            {
                Debug.LogWarning("NeoLayoutTemplates.Register ignored an entry with a null/empty id or loader.");
                return;
            }
            for (int i = 0; i < _registered.Count; i++)
                if (_registered[i].id == entry.id) { _registered[i] = entry; return; }
            _registered.Add(entry);
        }

        /// <summary>
        /// Inserts the template's views, popups and breakpoints into <paramref name="document"/> (one
        /// undo step). Names that collide with the document's existing views/popups are renamed (a numeric
        /// suffix) and reported through <paramref name="report"/> — a template never silently overwrites a
        /// designer's screen. Returns the id of the first inserted view (or popup) for selection, or null.
        /// </summary>
        public static string Insert(SpecDocument document, TemplateEntry entry, out List<string> report)
        {
            report = new List<string>();
            if (document == null) return null;

            string json;
            try { json = entry.loadJson?.Invoke(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Composer] Template '{entry.id}' failed to load: {e.Message}");
                report.Add($"Template '{entry.id}' failed to load: {e.Message}");
                return null;
            }
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"[Composer] Template '{entry.id}' produced no JSON; nothing inserted.");
                report.Add($"Template '{entry.id}' produced no JSON.");
                return null;
            }

            UISpec fragment;
            try { fragment = UISpec.FromJson(json); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Composer] Template '{entry.id}' is not a valid UISpec: {e.Message}");
                report.Add($"Template '{entry.id}' is not a valid UISpec: {e.Message}");
                return null;
            }

            var localReport = report;
            string firstSelectPath = null;

            document.ApplyEdit(() =>
            {
                UISpec spec = document.Spec;

                // breakpoints: add any the template names that the doc doesn't already declare
                foreach (BreakpointSpec bp in fragment.breakpoints)
                {
                    if (bp == null || string.IsNullOrEmpty(bp.name)) continue;
                    if (spec.breakpoints.Exists(b => b != null && b.name == bp.name)) continue;
                    spec.breakpoints.Add(bp);
                }

                foreach (ViewSpec view in fragment.views)
                {
                    if (view == null) continue;
                    string originalId = view.id;
                    if (spec.views.Exists(v => v != null && v.id == view.id))
                    {
                        string baseName = view.viewName;
                        int n = 2;
                        while (spec.views.Exists(v => v != null && v.id == $"{view.category}/{baseName}{n}")) n++;
                        view.viewName = baseName + n;
                        localReport.Add($"View '{originalId}' already exists — inserted as '{view.id}'.");
                    }
                    spec.views.Add(view);
                    if (firstSelectPath == null) firstSelectPath = SpecPath.View(view.id);
                }

                foreach (PopupSpec popup in fragment.popups)
                {
                    if (popup == null) continue;
                    string originalName = popup.name;
                    if (spec.popups.Exists(p => p != null && p.name == popup.name))
                    {
                        string baseName = popup.name;
                        int n = 2;
                        while (spec.popups.Exists(p => p != null && p.name == $"{baseName}{n}")) n++;
                        popup.name = baseName + n;
                        localReport.Add($"Popup '{originalName}' already exists — inserted as '{popup.name}'.");
                    }
                    spec.popups.Add(popup);
                    if (firstSelectPath == null) firstSelectPath = SpecPath.Popup(popup.name);
                }
            }, $"Insert Template: {entry.label}");

            foreach (string warning in report) Debug.LogWarning($"[Composer] {warning}");
            return firstSelectPath;
        }

        // ------------------------------------------------------------------ built-in JSON loading

        private static string LoadBuiltin(string file)
        {
            string path = Path.Combine(TemplatesDir, file);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Composer template not found at '{path}'.");
            return File.ReadAllText(path);
        }

        // The Templates~ folder sits next to this script. The tilde keeps Unity from importing the JSON as
        // assets, so it can't be addressed by GUID — we resolve the folder at runtime by locating THIS
        // script asset (a normal .cs asset, so AssetDatabase sees it), and fall back to the standard
        // package path. The compile-time CallerFilePath is a last resort (it points at the build machine).
        private static string _templatesDir;
        private static string TemplatesDir
        {
            get
            {
                if (_templatesDir != null) return _templatesDir;

                // 1) locate this script asset and walk to its sibling Templates~ folder
                string[] guids = UnityEditor.AssetDatabase.FindAssets("NeoLayoutTemplates t:MonoScript");
                foreach (string guid in guids)
                {
                    string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (!assetPath.EndsWith("/NeoLayoutTemplates.cs", System.StringComparison.Ordinal)) continue;
                    string dir = Path.GetDirectoryName(ProjectPathToAbsolute(assetPath));
                    string candidate = Path.Combine(dir, "Templates~");
                    if (Directory.Exists(candidate)) { _templatesDir = candidate; return _templatesDir; }
                }

                // 2) standard package path
                string standard = Path.Combine(Application.dataPath, "Neo UI Framework", "Editor", "Authoring", "Templates~");
                if (Directory.Exists(standard)) { _templatesDir = standard; return _templatesDir; }

                // 3) compile-time fallback
                string scriptDir = ScriptDirectory();
                _templatesDir = scriptDir != null ? Path.Combine(scriptDir, "Templates~") : standard;
                return _templatesDir;
            }
        }

        private static string ProjectPathToAbsolute(string assetPath)
        {
            // "Assets/..." → absolute, anchored at the project root (dataPath is "<root>/Assets")
            string root = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ScriptDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = null)
            => string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
    }
}
