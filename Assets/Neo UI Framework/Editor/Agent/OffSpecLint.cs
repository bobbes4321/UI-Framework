using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Editor
{
    /// <summary>
    /// An editor edit that will NOT survive a round trip — the exporter can't see it, so the next
    /// generate silently discards it. <see cref="fix"/> tells the human how to make the change
    /// survive (bind a token, fold it into the spec, …).
    /// </summary>
    public sealed class OffSpecFinding
    {
        public string path;
        public string message;
        public string fix;

        public override string ToString() => $"{message} — {fix}";

        public Dictionary<string, object> ToJsonObject() => new Dictionary<string, object>
        {
            ["path"] = path,
            ["message"] = message,
            ["fix"] = fix
        };
    }

    /// <summary>
    /// Detects editor edits the exporter cannot capture (so a regenerate would lose them silently).
    /// The exporter recurses into layout containers but treats composite widgets as opaque — their
    /// internal children are factory-owned (<see cref="UISpecExporter"/> ll. 256-268). So lint
    /// rebuilds each generated view's widget subtree IN MEMORY from the baseline spec and walks the
    /// live prefab against that reference in lockstep by child name, flagging divergences that live
    /// BELOW a composite widget root — raw colors/materials, internal geometry, added/removed
    /// internal children.
    ///
    /// <para>Advisory only — kept OUT of <see cref="AgentValidation.ValidateAll"/> (the same rule
    /// the design lint follows) and surfaced as <c>offSpecWarnings</c>.</para>
    /// </summary>
    public static class OffSpecLint
    {
        private const float GeometryEpsilon = 0.5f;

        /// <summary>
        /// Walks every generated view prefab against a reference built from its <paramref name="baseline"/>
        /// node. Views absent from the baseline are skipped (no baseline to compare — they're new,
        /// not drifted). Returns an empty list when there is no baseline.
        /// </summary>
        public static List<OffSpecFinding> ScanProject(UISpec baseline)
        {
            var findings = new List<OffSpecFinding>();
            if (baseline == null) return findings;

            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null) return findings;

            var baseViews = baseline.views.ToDictionary(v => v.id, v => v);

            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                UIView view = prefab.GetComponent<UIView>();
                if (view == null) continue;
                if (!baseViews.TryGetValue(view.id.ToString(), out ViewSpec baseView)) continue;

                GameObject reference = UISpecGenerator.BuildViewGameObject(baseView, settings, new GenerateReport());
                if (reference == null) continue;
                try
                {
                    CompareTree(prefab, reference, SpecPath.View(view.id.ToString()), findings);
                }
                finally
                {
                    Object.DestroyImmediate(reference);
                }
            }
            return findings;
        }

        /// <summary>
        /// Lockstep walk of a live root against a freshly-built reference root. Public so it is
        /// testable in-memory without committing prefabs. <paramref name="basePath"/> is the
        /// <see cref="SpecPath"/> of the roots (e.g. <c>views/Menu/Main</c>).
        /// </summary>
        public static void CompareTree(GameObject live, GameObject reference, string basePath,
            List<OffSpecFinding> findings)
        {
            if (live == null || reference == null) return;
            Walk(live.transform, reference.transform, basePath, insideWidget: false,
                isExportableLabel: false, findings);
        }

        private static void Walk(Transform live, Transform reference, string path, bool insideWidget,
            bool isExportableLabel, List<OffSpecFinding> findings)
        {
            // visual divergences only matter for factory-internal nodes (below a composite widget) —
            // everything at or above the widget root is the spec-exportable layer (SpecDiff's job)
            if (insideWidget) CompareVisuals(live, reference, path, isExportableLabel, findings);

            bool widgetRoot = IsCompositeWidget(live);
            bool childrenInside = insideWidget || widgetRoot;

            var refChildren = new Dictionary<string, Transform>();
            foreach (Transform child in reference)
                if (!refChildren.ContainsKey(child.name)) refChildren[child.name] = child;

            var liveSeen = new HashSet<string>();
            foreach (Transform child in live)
            {
                liveSeen.Add(child.name);
                string childPath = $"{path}/{child.name}";
                // the direct child the exporter reads back as the widget's `label` (UISpecExporter
                // FindChildText(go, LabelName)) — its text round-trips, so a text edit there is NOT
                // off-spec. Only the text is exempt; raw color/geometry on it still won't round-trip.
                bool childIsExportableLabel = widgetRoot && child.name == UIWidgetFactory.LabelName;
                if (refChildren.TryGetValue(child.name, out Transform refChild))
                {
                    Walk(child, refChild, childPath, childrenInside, childIsExportableLabel, findings);
                }
                else if (childrenInside)
                {
                    findings.Add(new OffSpecFinding
                    {
                        path = childPath,
                        message = $"Child '{child.name}' was added inside widget '{path}'",
                        fix = "Widget internals are factory-owned and are dropped on regenerate — " +
                              "rebuild this as a spec element on the widget's container instead."
                    });
                }
            }

            if (childrenInside)
                foreach (KeyValuePair<string, Transform> entry in refChildren)
                    if (!liveSeen.Contains(entry.Key))
                        findings.Add(new OffSpecFinding
                        {
                            path = $"{path}/{entry.Key}",
                            message = $"Factory child '{entry.Key}' was removed from widget '{path}'",
                            fix = "Removing widget internals doesn't round-trip — the factory recreates " +
                                  "them on regenerate. Hide it via the spec instead of deleting it."
                        });
        }

        private static void CompareVisuals(Transform live, Transform reference, string path,
            bool isExportableLabel, List<OffSpecFinding> findings)
        {
            var liveGraphic = live.GetComponent<Graphic>();
            var refGraphic = reference.GetComponent<Graphic>();
            if (liveGraphic != null && refGraphic != null)
            {
                // a color bound to a theme token round-trips (the exporter reads the token); a raw
                // color set straight on a factory child does not
                bool tokenBound = live.GetComponent<ThemeColorTarget>() != null
                                  || live.GetComponent<NeoGradient>() != null;
                if (!tokenBound && !ColorsEqual(liveGraphic.color, refGraphic.color))
                    findings.Add(new OffSpecFinding
                    {
                        path = path,
                        message = $"Color set directly on factory child '{path}'",
                        fix = "Bind it to a theme token (ThemeColorTarget) so it survives regeneration, " +
                              "or fold this edit into the spec on the owning widget."
                    });

                if (liveGraphic.material != refGraphic.material)
                    findings.Add(new OffSpecFinding
                    {
                        path = path,
                        message = $"Raw material assigned to factory child '{path}'",
                        fix = "Materials on widget internals aren't exported — use a theme shape style " +
                              "or gradient on the widget so the look round-trips."
                    });
            }

            var liveText = live.GetComponent<TMP_Text>();
            var refText = reference.GetComponent<TMP_Text>();
            if (!isExportableLabel && liveText != null && refText != null && liveText.text != refText.text)
                findings.Add(new OffSpecFinding
                {
                    path = path,
                    message = $"Text changed on factory child '{path}' ('{refText.text}' → '{liveText.text}')",
                    fix = "Internal widget text is factory-owned — set the widget's spec label instead " +
                          "so the change survives a regenerate."
                });

            var liveRect = live as RectTransform;
            var refRect = reference as RectTransform;
            if (liveRect != null && refRect != null
                && (!Approximately(liveRect.sizeDelta, refRect.sizeDelta)
                    || !Approximately(liveRect.anchoredPosition, refRect.anchoredPosition)))
                findings.Add(new OffSpecFinding
                {
                    path = path,
                    message = $"Internal geometry of factory child '{path}' changed",
                    fix = "Widget-internal layout is factory-owned and not exported — adjust the widget's " +
                          "spec size/anchor fields instead of nudging its internals."
                });
        }

        /// <summary> Composite widgets the exporter treats as opaque (it reads their values but
        /// never recurses into their internal children). </summary>
        private static bool IsCompositeWidget(Transform t) =>
            t.GetComponent<UIButton>() != null
            || t.GetComponent<UIToggle>() != null
            || t.GetComponent<UITab>() != null
            || t.GetComponent<UISlider>() != null
            || t.GetComponent<Progressor>() != null
            || t.GetComponent<UIStepper>() != null
            || t.GetComponent<TMP_InputField>() != null
            || t.GetComponent<UIDropdown>() != null;

        private static bool ColorsEqual(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f
            && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;

        private static bool Approximately(Vector2 a, Vector2 b) =>
            Mathf.Abs(a.x - b.x) < GeometryEpsilon && Mathf.Abs(a.y - b.y) < GeometryEpsilon;

        private static IEnumerable<GameObject> LoadGeneratedPrefabs()
        {
            string folder = $"{UISpecGenerator.GeneratedRoot}/Views";
            if (!AssetDatabase.IsValidFolder(folder)) yield break;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent<GeneratedMarker>() != null) yield return prefab;
            }
        }
    }

    /// <summary>
    /// The committed baseline spec — the last spec the project was generated from, used as the
    /// common ancestor for drift detection and three-way merge. Stored as a hidden dotfile under
    /// <see cref="UISpecGenerator.GeneratedRoot"/> so Unity's importer ignores it (no .meta, no
    /// GUID). Plan 4 owns the policy around when it is refreshed; Plan 1 only reads/writes it.
    /// </summary>
    public static class NeoBaseline
    {
        public static string Path => $"{UISpecGenerator.GeneratedRoot}/.neo-baseline.json";

        public static bool Exists => File.Exists(Path);

        public static UISpec Load() => Exists ? UISpec.FromJson(File.ReadAllText(Path)) : null;

        public static void Save(UISpec spec)
        {
            string dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, spec.ToJson());
        }

        /// <summary> Captures the current project as the new baseline and returns it. </summary>
        public static UISpec Establish()
        {
            UISpec spec = UISpecExporter.ExportProject();
            Save(spec);
            return spec;
        }
    }
}
