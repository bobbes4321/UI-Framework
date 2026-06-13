using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// File-based request bridge for agents while the editor is open (opt-in via
    /// "Tools → Neo UI → Agent Bridge"). Drop JSON at <c>Temp/neo-request.json</c>; the
    /// result lands at <c>Temp/neo-result.json</c>. One request at a time, polled cheaply
    /// (one File.Exists per ~100 editor updates).
    ///
    /// Actions:
    /// - <c>{"action":"screenshot","prefab":"Assets/...","out":"shots/a.png","width":1080,"height":1920}</c>
    ///   (also the default when "action" is omitted — back-compat with the old screenshot watcher)
    /// - <c>{"action":"generate","spec":"path/to/spec.json"}</c> — runs the spec generator,
    ///   returns the created/updated/collision/issue lists
    /// - <c>{"action":"export","out":"path/out.json"}</c> — exports the project spec; "out" optional,
    ///   the spec JSON is always inlined in the result
    /// - <c>{"action":"validate"}</c> — runs AgentValidation, returns issues + designWarnings +
    ///   offSpecWarnings (the last names editor edits that won't survive a round trip)
    /// - <c>{"action":"diff","baseline":"path.json"}</c> — exports the project and diffs it against
    ///   the given baseline (or the stored <c>.neo-baseline.json</c>); returns changes +
    ///   offSpecWarnings. Degrades to "no baseline" when none exists.
    /// - <c>{"action":"merge","incoming":"new.json","out":"merged.json","conflictPolicy":"preferTheirs"}</c>
    ///   — three-way merge of stored baseline + live project + incoming spec; writes the merged spec
    ///   and returns applied / conflicts / dropped (off-spec edits that cannot be folded in)
    /// - <c>{"action":"sync","incoming":"new.json","force":false,"conflictPolicy":"preferTheirs"}</c>
    ///   — the safe-regenerate protocol and the STANDING way an agent changes generated UI: export →
    ///   drift+lint vs the baseline → (refuse on off-spec edits unless "force") → three-way merge →
    ///   generate from the merged spec → rewrite the baseline. Preserves human prefab edits a stale
    ///   incoming spec would otherwise wipe. Omit "incoming" to just capture the human's edits into the
    ///   baseline (export + fold drift, no regenerate). Returns ok / refused / regenerated / applied /
    ///   conflicts / offSpecWarnings / dropped / merged.
    /// - <c>{"action":"buildScene"}</c> — builds the playable scene from generated assets
    ///   (refuses while the open scene has unsaved changes)
    /// - <c>{"action":"specReference","out":"path.md"}</c> — writes the code-generated spec
    ///   authoring reference ("out" optional, defaults to Assets/docs/spec-reference.md)
    /// - <c>{"action":"importSprites","folder":"Assets/..."}</c> — imports every texture under the
    ///   folder as a Single sprite (spec image "src" needs Sprite sub-assets, not raw textures)
    ///
    /// With the editor CLOSED, run the same requests headlessly:
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod Neo.UI.Editor.AgentBridge.RunBatch
    /// -neoRequest requests.json -neoResult results.json</c> — the request file holds one request
    /// object or an array (processed in order; omit -nographics when screenshots/previews are in it).
    /// </summary>
    public static class AgentBridge
    {
        public const string RequestPath = "Temp/neo-request.json";
        public const string ResultPath = "Temp/neo-result.json";
        private const string MenuPath = "Tools/Neo UI/Agent Bridge";
        private const string PrefsKey = "Neo.UI.AgentBridge";

        private static int s_pollCountdown;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            if (EditorPrefs.GetBool(PrefsKey, false)) EditorApplication.update += Poll;
        }

        [MenuItem(MenuPath, priority = 4)]
        private static void Toggle()
        {
            bool enable = !EditorPrefs.GetBool(PrefsKey, false);
            EditorPrefs.SetBool(PrefsKey, enable);
            EditorApplication.update -= Poll;
            if (enable) EditorApplication.update += Poll;
            Menu.SetChecked(MenuPath, enable);
            Debug.Log($"[Neo.UI] Agent bridge {(enable ? "enabled" : "disabled")} (watching {RequestPath})");
        }

        [MenuItem(MenuPath, validate = true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PrefsKey, false));
            return true;
        }

        private static void Poll()
        {
            if (--s_pollCountdown > 0) return;
            s_pollCountdown = 100;
            if (!File.Exists(RequestPath)) return;

            string json = File.ReadAllText(RequestPath);
            File.Delete(RequestPath);
            File.WriteAllText(ResultPath, HandleRequest(json));
        }

        /// <summary> Handles one request JSON and returns the result JSON (also used by tests). </summary>
        public static string HandleRequest(string json)
        {
            var result = new Dictionary<string, object>();
            try
            {
                var request = JsonReader.AsObject(MiniJson.Parse(json), "agent request");
                string action = JsonReader.GetString(request, "action", "screenshot");

                // generating/screenshotting in play mode corrupts bakes: AddComponent fires
                // Awake/OnEnable on the factory's temp objects mid-construction (and pollutes
                // the running game's registries)
                // diff/merge build reference widget subtrees in memory (like preview), so the same
                // play-mode hazard applies (AddComponent fires Awake/OnEnable mid-construction)
                bool mutatesAssets = action == "generate" || action == "buildScene"
                                     || action == "screenshot" || action == "preview"
                                     || action == "diff" || action == "merge" || action == "sync";
                if (mutatesAssets && UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    result["ok"] = false;
                    result["error"] = $"'{action}' refused while the editor is in Play mode — exit Play mode and retry";
                    return MiniJson.Serialize(result);
                }

                switch (action)
                {
                    case "screenshot": HandleScreenshot(request, result); break;
                    case "generate": HandleGenerate(request, result); break;
                    case "export": HandleExport(request, result); break;
                    case "validate": HandleValidate(result); break;
                    case "diff": HandleDiff(request, result); break;
                    case "merge": HandleMerge(request, result); break;
                    case "sync": HandleSync(request, result); break;
                    case "buildScene": HandleBuildScene(request, result); break;
                    case "specReference": HandleSpecReference(request, result); break;
                    case "preview": HandlePreview(request, result); break;
                    case "importSprites": HandleImportSprites(request, result); break;
                    default:
                        result["ok"] = false;
                        result["error"] = $"Unknown action '{action}' (screenshot | generate | export | validate | diff | merge | sync | buildScene | specReference | preview | importSprites)";
                        break;
                }
            }
            catch (Exception e)
            {
                result["ok"] = false;
                result["error"] = e.Message;
            }
            return MiniJson.Serialize(result);
        }

        private static void HandleScreenshot(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string prefabPath = JsonReader.GetString(request, "prefab");
            string outPath = JsonReader.GetString(request, "out");
            int width = (int)JsonReader.GetFloat(request, "width", 1080);
            int height = (int)JsonReader.GetFloat(request, "height", 1920);
            string written = UIScreenshotter.CapturePrefab(prefabPath, outPath, width, height);
            result["ok"] = true;
            result["path"] = Path.GetFullPath(written);
        }

        private static void HandleGenerate(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string specPath = JsonReader.GetString(request, "spec");
            if (string.IsNullOrEmpty(specPath))
                throw new ArgumentException("generate needs \"spec\": path to a spec JSON file");
            GenerateReport report = UISpecGenerator.GenerateFromSpecFile(specPath);
            result["ok"] = !report.hasProblems;
            result["created"] = new List<object>(report.created);
            result["updated"] = new List<object>(report.updated);
            result["collisions"] = new List<object>(report.collisions);
            result["issues"] = new List<object>(report.issues);
            // a plain generate against a drifted tree overwrites human prefab edits — surface that
            // loudly (points at 'sync', which preserves them). Soft: never flips "ok".
            result["warnings"] = new List<object>(report.warnings);
            if (report.warnings.Count > 0) result["warning"] = string.Join(" ", report.warnings);
        }

        private static void HandleExport(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string specJson = UISpecExporter.ExportProject().ToJson();
            string outPath = JsonReader.GetString(request, "out");
            if (!string.IsNullOrEmpty(outPath))
            {
                string directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(outPath, specJson);
                result["path"] = Path.GetFullPath(outPath);
            }
            result["ok"] = true;
            result["spec"] = specJson;
        }

        private static void HandlePreview(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string specPath = JsonReader.GetString(request, "spec");
            if (string.IsNullOrEmpty(specPath))
                throw new ArgumentException("preview needs \"spec\": path to a spec JSON file");
            string outDir = JsonReader.GetString(request, "out");
            if (string.IsNullOrEmpty(outDir)) outDir = $"{UIScreenshotter.DefaultOutputFolder}/preview";

            UISpec spec = UISpec.FromJson(File.ReadAllText(specPath));
            // renders in-memory across the resolution matrix; commits NO prefabs/assets
            List<string> paths = UISpecPreview.Render(spec, outDir);
            var absolute = new List<object>(paths.Count);
            foreach (string p in paths) absolute.Add(Path.GetFullPath(p));
            result["ok"] = true;
            result["paths"] = absolute;
        }

        private static void HandleSpecReference(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            // "out" optional — default to the docs path so the committed reference stays current
            string outPath = JsonReader.GetString(request, "out");
            string written = SpecReference.Write(string.IsNullOrEmpty(outPath) ? SpecReference.DefaultPath : outPath);
            string schema = SpecReference.WriteSchema(SpecReference.DefaultSchemaPath);
            result["ok"] = true;
            result["path"] = Path.GetFullPath(written);
            result["schema"] = Path.GetFullPath(schema);
        }

        private static void HandleValidate(Dictionary<string, object> result)
        {
            List<string> issues = AgentValidation.ValidateAll();
            result["ok"] = issues.Count == 0;
            result["issues"] = new List<object>(issues);
            // soft design lint (contrast / raw font sizes / off-scale spacing) — never fails "ok"
            result["designWarnings"] = new List<object>(AgentValidation.ValidateDesign());
            // round-trip safety lint: editor edits that won't survive a regenerate (advisory, never
            // fails "ok"). Needs a baseline and an in-memory rebuild, so skip it in Play mode.
            var offSpec = new List<object>();
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                foreach (OffSpecFinding finding in OffSpecLint.ScanProject(NeoBaseline.Load()))
                    offSpec.Add(finding.ToJsonObject());
            result["offSpecWarnings"] = offSpec;
        }

        private static void HandleDiff(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string baselinePath = JsonReader.GetString(request, "baseline");
            UISpec baseline = !string.IsNullOrEmpty(baselinePath)
                ? UISpec.FromJson(File.ReadAllText(baselinePath))
                : NeoBaseline.Load();
            if (baseline == null)
            {
                result["ok"] = true;
                result["changes"] = new List<object>();
                result["offSpecWarnings"] = new List<object>();
                result["note"] = "no baseline — establish one (Tools/Neo UI/Check For Drift, or export) before diffing";
                return;
            }

            UISpec current = UISpecExporter.ExportProject();
            var changes = new List<object>();
            foreach (SpecChange change in SpecDiff.Compare(baseline, current)) changes.Add(change.ToJsonObject());
            var offSpec = new List<object>();
            foreach (OffSpecFinding finding in OffSpecLint.ScanProject(baseline)) offSpec.Add(finding.ToJsonObject());

            result["ok"] = true;
            result["changes"] = changes;
            result["offSpecWarnings"] = offSpec;
        }

        private static void HandleMerge(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string incomingPath = JsonReader.GetString(request, "incoming");
            if (string.IsNullOrEmpty(incomingPath) || !File.Exists(incomingPath))
                throw new ArgumentException($"merge needs \"incoming\": an existing spec JSON file (got '{incomingPath}')");

            UISpec theirs = UISpec.FromJson(File.ReadAllText(incomingPath));
            UISpec ours = UISpecExporter.ExportProject();
            UISpec baseline = NeoBaseline.Load();
            bool hadBaseline = baseline != null;
            if (baseline == null) baseline = new UISpec(); // 2-way union when no common ancestor exists

            ConflictPolicy policy = ParsePolicy(JsonReader.GetString(request, "conflictPolicy"));
            MergeResult merge = SpecMerge.Merge(baseline, ours, theirs, policy);
            // off-spec edits can never be folded into a spec — report them so the caller can refuse
            merge.dropped.AddRange(OffSpecLint.ScanProject(hadBaseline ? baseline : ours));

            string mergedJson = merge.merged.ToJson();
            string outPath = JsonReader.GetString(request, "out");
            if (!string.IsNullOrEmpty(outPath))
            {
                string directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(outPath, mergedJson);
                result["path"] = Path.GetFullPath(outPath);
            }

            var applied = new List<object>();
            foreach (SpecChange change in merge.applied) applied.Add(change.ToJsonObject());
            var conflicts = new List<object>();
            foreach (SpecChange change in merge.conflicts) conflicts.Add(change.ToJsonObject());
            var dropped = new List<object>();
            foreach (OffSpecFinding finding in merge.dropped) dropped.Add(finding.ToJsonObject());

            result["ok"] = !merge.failed;
            result["merged"] = mergedJson;
            result["applied"] = applied;
            result["conflicts"] = conflicts;
            result["dropped"] = dropped;
            if (!hadBaseline)
                result["note"] = "no baseline — performed a 2-way union (establish a baseline for true three-way merges)";
        }

        private static ConflictPolicy ParsePolicy(string value)
        {
            switch (value)
            {
                case "preferOurs": return ConflictPolicy.PreferOurs;
                case "fail": return ConflictPolicy.Fail;
                default: return ConflictPolicy.PreferTheirs;
            }
        }

        /// <summary>
        /// The safe-regenerate protocol (Plan 4). With "incoming": export → drift+lint vs baseline →
        /// (refuse on off-spec edits unless "force") → three-way merge → generate → rewrite baseline.
        /// Without "incoming": capture the human's edits into the baseline (no regenerate). This is the
        /// standing way agents change generated UI — it preserves human prefab edits a stale incoming
        /// spec would otherwise wipe, and never discards a non-round-trippable edit silently.
        /// </summary>
        private static void HandleSync(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string incomingPath = JsonReader.GetString(request, "incoming");
            UISpec incoming = null;
            if (!string.IsNullOrEmpty(incomingPath))
            {
                if (!File.Exists(incomingPath))
                    throw new ArgumentException($"sync \"incoming\" points at a missing file: '{incomingPath}' " +
                                                "(omit \"incoming\" to capture the human's edits into the baseline)");
                incoming = UISpec.FromJson(File.ReadAllText(incomingPath));
            }

            bool force = JsonReader.GetBool(request, "force");
            ConflictPolicy policy = ParsePolicy(JsonReader.GetString(request, "conflictPolicy"));

            SyncResult sync = SpecBaseline.Sync(incoming, policy, force);

            result["ok"] = sync.ok;
            result["refused"] = sync.refused;
            result["regenerated"] = sync.regenerated;
            result["baselineUpdated"] = sync.baselineUpdated;
            result["humanChanges"] = ChangeList(sync.humanChanges);
            result["applied"] = ChangeList(sync.applied);
            result["conflicts"] = ChangeList(sync.conflicts);
            result["offSpecWarnings"] = FindingList(sync.offSpecWarnings);
            result["dropped"] = FindingList(sync.dropped);
            if (sync.merged != null) result["merged"] = sync.merged.ToJson();
            if (sync.generateReport != null)
            {
                result["created"] = new List<object>(sync.generateReport.created);
                result["updated"] = new List<object>(sync.generateReport.updated);
                result["collisions"] = new List<object>(sync.generateReport.collisions);
                result["issues"] = new List<object>(sync.generateReport.issues);
            }
            if (!string.IsNullOrEmpty(sync.note)) result["note"] = sync.note;

            // optional: also write the merged/captured spec out for inspection (like merge's "out")
            string outPath = JsonReader.GetString(request, "out");
            if (!string.IsNullOrEmpty(outPath) && sync.merged != null)
            {
                string directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(outPath, sync.merged.ToJson());
                result["path"] = Path.GetFullPath(outPath);
            }
        }

        private static List<object> ChangeList(List<SpecChange> changes)
        {
            var list = new List<object>(changes.Count);
            foreach (SpecChange change in changes) list.Add(change.ToJsonObject());
            return list;
        }

        private static List<object> FindingList(List<OffSpecFinding> findings)
        {
            var list = new List<object>(findings.Count);
            foreach (OffSpecFinding finding in findings) list.Add(finding.ToJsonObject());
            return list;
        }

        private static void HandleImportSprites(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            string folder = JsonReader.GetString(request, "folder");
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException($"importSprites needs \"folder\": an existing Assets folder (got '{folder}')");

            var imported = new List<object>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                if (importer.textureType == TextureImporterType.Sprite
                    && importer.spriteImportMode == SpriteImportMode.Single) continue;
                importer.textureType = TextureImporterType.Sprite;
                // textureType alone leaves spriteImportMode None — no Sprite sub-asset generates
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
                imported.Add(path);
            }
            result["ok"] = true;
            result["imported"] = imported;
        }

        /// <summary>
        /// Headless bridge: processes a request file (one request object or an array, in order)
        /// without the editor open. Exits 0 when every request reports ok, 1 otherwise.
        /// <c>-executeMethod Neo.UI.Editor.AgentBridge.RunBatch -neoRequest req.json
        /// -neoResult res.json</c>
        /// </summary>
        public static void RunBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string requestPath = ArgValue(args, "-neoRequest");
            string resultPath = ArgValue(args, "-neoResult") ?? "neo-batch-result.json";
            int exitCode = 0;
            var results = new List<object>();
            try
            {
                if (string.IsNullOrEmpty(requestPath) || !File.Exists(requestPath))
                    throw new ArgumentException($"-neoRequest must point at an existing request JSON file (got '{requestPath}')");

                object parsed = MiniJson.Parse(File.ReadAllText(requestPath));
                var requests = parsed is List<object> list ? list : new List<object> { parsed };
                foreach (object request in requests)
                {
                    var single = JsonReader.AsObject(MiniJson.Parse(HandleRequest(MiniJson.Serialize(request))), "batch result");
                    results.Add(single);
                    if (!(single.TryGetValue("ok", out object ok) && ok is bool isOk && isOk)) exitCode = 1;
                }
            }
            catch (Exception e)
            {
                results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = e.Message });
                exitCode = 1;
            }
            File.WriteAllText(resultPath, MiniJson.Serialize(results));
            EditorApplication.Exit(exitCode);
        }

        private static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static void HandleBuildScene(Dictionary<string, object> request, Dictionary<string, object> result)
        {
            // building replaces the open scene — never discard a human's unsaved work silently
            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException(
                    "The open scene has unsaved changes — save it in the editor first, then retry");
            // optional "flow": which generated app to build when the generated folder holds several
            // flows. Omit it when only one exists; the builder throws (listing them) if it's ambiguous.
            string flowName = JsonReader.GetString(request, "flow");
            string path = GeneratedSceneBuilder.Build(flowName);
            result["ok"] = true;
            result["path"] = path;
        }
    }
}
