using Neo.UI.Editor.Composer; // ComposerFactory / ComposerPalette: shared spec-authoring utilities,
                              // promoted to the Neo.UI.Editor root namespace when the Composer retires.
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// The native-Unity authoring entry point: create one Neo widget directly into the open scene the way
    /// Unity's own <c>GameObject → UI → …</c> commands do. Every create routes through
    /// <see cref="UISpecGenerator.BuildElementLive"/> — the SAME path generation uses — so a widget made
    /// here is byte-identical to one generated from a spec, which is what lets it round-trip back to JSON
    /// (Phase 2 capture). This is callable static API: the <c>GameObject/Neo UI/…</c> menu (NeoCreateMenu)
    /// and the scene-view overlay's "Add Widget" both call it, so the behaviour lives in exactly one place.
    /// </summary>
    public static class NeoSceneAuthoring
    {
        /// <summary>
        /// Creates a widget of <paramref name="kind"/> under the best parent derived from the current
        /// <see cref="Selection"/> (an existing Neo container/view if one is selected, else a Canvas —
        /// found or bootstrapped). Returns the created root, or null if the kind couldn't be built.
        /// </summary>
        public static GameObject CreateWidget(string kind) =>
            CreateWidget(kind, Selection.activeGameObject);

        /// <summary>
        /// Creates a widget of <paramref name="kind"/> under <paramref name="parentSelection"/> when it is a
        /// suitable UI parent, otherwise under the resolved/created Canvas. Used by the overlay's "Add Widget"
        /// to drop into a specific element.
        /// </summary>
        public static GameObject CreateWidget(string kind, GameObject parentSelection)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            ElementSpec element = ComposerFactory.NewElement(kind);
            return Place(element, kind, parentSelection, $"Create Neo {Humanize(kind)}");
        }

        /// <summary>
        /// Creates an empty stretched <see cref="UIView"/> root — the container hand-built UI lives inside
        /// and the unit Phase 2 capture turns into a spec view. Stamps <see cref="GeneratedMarker.showcaseId"/>
        /// from the active scene's showcase when there is one, so capture can route it home with no prompt.
        /// </summary>
        public static GameObject CreateView()
        {
            NeoUISettings settings = PrepareSettings();
            ResolveParent(Selection.activeGameObject, requireCanvas: true, out RectTransform parent, out _);

            var view = new ViewSpec { category = "Main", viewName = UniqueViewName(parent) };
            var report = new GenerateReport();
            GameObject go = UISpecGenerator.BuildViewGameObject(view, settings, report);
            FinishCreate(go, parent, "Create Neo View", report);
            return go;
        }

        /// <summary>
        /// Re-styles <paramref name="widget"/> to a reusable <see cref="NeoWidgetPreset"/> by rebuilding it
        /// through the generator under that preset. Keeps the widget's identity + content (kind/id/label/icon)
        /// but drops its captured styling so the PRESET drives the look (otherwise the element's own
        /// variant/size/shape would override the preset and nothing would change). Placement and sibling
        /// order are preserved; the swap is one undo step.
        /// </summary>
        public static GameObject ApplyPreset(GameObject widget, string presetName)
        {
            if (widget == null || string.IsNullOrEmpty(presetName)) return null;
            if (!(widget.transform.parent is RectTransform parent))
            {
                Debug.LogWarning("Neo UI: can't apply a preset to a root object — select a widget inside a view.");
                return null;
            }
            bool inLayout = parent.GetComponent<LayoutGroup>() != null;
            ElementSpec current = UISpecExporter.ExportElement(widget, inLayout);
            if (current == null)
            {
                Debug.LogWarning($"Neo UI: '{widget.name}' isn't a recognized Neo widget — can't apply a preset.");
                return null;
            }

            var spec = new ElementSpec
            {
                kind = current.kind, id = current.id, label = current.label, icon = current.icon,
                preset = presetName,
            };
            NeoUISettings settings = PrepareSettings();
            var report = new GenerateReport();
            GameObject built = UISpecGenerator.BuildElementLive(spec, parent, settings, report);
            if (built == null)
            {
                Debug.LogWarning($"Neo UI: applying preset '{presetName}' failed — {string.Join("; ", report.issues)}");
                return null;
            }

            int index = widget.transform.GetSiblingIndex();
            var src = (RectTransform)widget.transform;
            var dst = (RectTransform)built.transform;
            if (!inLayout)
            {
                dst.anchorMin = src.anchorMin; dst.anchorMax = src.anchorMax; dst.pivot = src.pivot;
                dst.anchoredPosition = src.anchoredPosition; dst.sizeDelta = src.sizeDelta;
            }
            dst.SetSiblingIndex(index);
            built.name = widget.name;

            Undo.RegisterCreatedObjectUndo(built, "Apply Neo Preset");
            Undo.DestroyObjectImmediate(widget);
            Selection.activeGameObject = built;
            EditorGUIUtility.PingObject(built);
            if (report.issues.Count > 0)
                Debug.LogWarning($"Neo UI: apply preset '{presetName}' — {string.Join("; ", report.issues)}");
            return built;
        }

        // ---------------------------------------------------------------- placement

        private static GameObject Place(ElementSpec element, string kind, GameObject parentSelection, string undoLabel)
        {
            NeoUISettings settings = PrepareSettings();
            ResolveParent(parentSelection, requireCanvas: true, out RectTransform parent, out _);

            var report = new GenerateReport();
            GameObject go = UISpecGenerator.BuildElementLive(element, parent, settings, report);
            if (go == null)
            {
                Debug.LogWarning($"Neo UI: could not create a '{kind}' — {string.Join("; ", report.issues)}");
                return null;
            }
            FinishCreate(go, parent, undoLabel, report);
            return go;
        }

        // BuildElementLive parents through the factory; BuildViewGameObject returns a detached root (in
        // the generate flow it becomes a prefab), so reparent it here. Parenting BEFORE the create-undo
        // means a single Undo removes the whole subtree, parenting and all.
        private static void FinishCreate(GameObject go, RectTransform parent, string undoLabel, GenerateReport report)
        {
            if (go == null) return;
            if (parent != null && go.transform.parent != parent)
                go.transform.SetParent(parent, worldPositionStays: false);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, undoLabel);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (report != null && report.issues.Count > 0)
                Debug.LogWarning($"Neo UI: '{undoLabel}' reported — {string.Join("; ", report.issues)}");
        }

        private static NeoUISettings PrepareSettings()
        {
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            // The factory styles widgets through theme tokens/text styles; fill any gaps so a freshly
            // created widget renders themed rather than blank (same prep generation does — UISpecGenerator:188).
            if (settings != null && settings.theme != null)
            {
                StarterKitBootstrap.EnsureFactoryTokens(settings.theme);
                StarterKitBootstrap.EnsureTextStyles(settings.theme);
            }
            return settings;
        }

        // ---------------------------------------------------------------- parent / canvas bootstrap

        /// <summary>
        /// Picks the parent a new widget should drop into, mirroring Unity's built-in UI create: prefer the
        /// selected RectTransform when it already lives under a Canvas; otherwise find-or-create a Canvas
        /// (and an EventSystem wired for the New Input System). <paramref name="createdCanvas"/> reports
        /// whether a Canvas was bootstrapped this call.
        /// </summary>
        private static void ResolveParent(GameObject selection, bool requireCanvas,
            out RectTransform parent, out bool createdCanvas)
        {
            createdCanvas = false;
            if (selection != null)
            {
                var rt = selection.GetComponent<RectTransform>();
                if (rt != null && selection.GetComponentInParent<Canvas>() != null)
                {
                    parent = rt;
                    return;
                }
            }
            parent = (RectTransform)FindOrCreateCanvas(out createdCanvas).transform;
        }

        private static Canvas FindOrCreateCanvas(out bool created)
        {
            created = false;
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Exclude);
            if (canvas != null) return canvas;

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            created = true;

            // New Input System only (hard constraint): never StandaloneInputModule.
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Exclude) == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }
            return canvas;
        }

        // ---------------------------------------------------------------- naming

        private static string UniqueViewName(RectTransform parent)
        {
            const string baseName = "View";
            if (parent == null) return baseName;
            int n = 0;
            string candidate = baseName;
            while (ChildNamed(parent, $"Main_{candidate}")) candidate = baseName + (++n);
            return candidate;
        }

        private static bool ChildNamed(RectTransform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return true;
            return false;
        }

        // "vstack" → "Vertical Stack" when the palette knows it, else a title-cased fallback.
        internal static string Humanize(string kind)
        {
            foreach (PaletteEntry e in ComposerPalette.All)
                if (e.kind == kind) return e.label;
            if (string.IsNullOrEmpty(kind)) return kind;
            return char.ToUpperInvariant(kind[0]) + kind.Substring(1);
        }
    }
}
