using Neo.EditorUI;
using Neo.UI.Editor.Authoring;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// EditorUI-kit inspectors for the authoring ScriptableObjects that previously fell back to Unity's
    /// raw default — so creating/editing a showcase or animation database reads like the rest of the
    /// package (accent header + grouped foldout sections via <see cref="NeoGUI"/>).
    /// </summary>
    [CustomEditor(typeof(ShowcaseDefinition))]
    public class ShowcaseDefinitionEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Showcase Definition";
        protected override string HeaderSubtitle =>
            string.IsNullOrEmpty(((ShowcaseDefinition)target).id) ? "drop into the Hub gallery"
                : ((ShowcaseDefinition)target).id;
        protected override Color Accent => NeoColors.Theming;

        protected override void DrawBody()
        {
            if (NeoGUI.BeginFoldoutSection("NeoUI.Showcase.Identity", "Identity", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("title"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("category"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.Showcase.Content", "Content", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("specJson"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("specPathOverride"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("flowName"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.Showcase.Media", "Media"))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("thumbnail"));
            NeoGUI.EndFoldoutSection();
        }
    }

    [CustomEditor(typeof(AnimationPresetDatabase))]
    public class AnimationPresetDatabaseEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Animation Preset Database";
        protected override string HeaderSubtitle =>
            $"{((AnimationPresetDatabase)target).Presets.Count} explicit preset(s)";
        protected override Color Accent => NeoColors.Animation;

        protected override void DrawBody()
        {
            EditorGUILayout.HelpBox(
                "Animation presets auto-discover: drop a UIAnimationPreset asset anywhere and reference it " +
                "by name from a spec — no need to list it here. This list is an explicit override/curation " +
                "(an entry here wins over a discovered asset of the same name).", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("presets"), true);
        }
    }

    /// <summary>
    /// Custom inspector for a <see cref="UIAnimationPreset"/> — the design-system-central asset that
    /// previously fell back to Unity's raw default. The body is the shared <see cref="AnimationPresetGUI"/>
    /// drawer (so window + inspector stay one implementation), followed by a live Preview that plays the
    /// preset on the currently-selected UI object through the same <see cref="AnimationPreview"/>
    /// snapshot/restore machinery the animator picker's hover-preview uses.
    ///
    /// <para>
    /// Restore safety mirrors <c>AnimationPresetBrowserPopup</c>: the object's rest state is snapshotted
    /// before the first play and put back the instant the user hits Stop, changes selection, or closes the
    /// inspector (<see cref="OnDisable"/>). A throwaway scratch <see cref="UIAnimation"/> drives the
    /// preview so the preset asset itself is never dirtied.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(UIAnimationPreset))]
    public class UIAnimationPresetEditor : NeoUIEditor
    {
        // A throwaway animation the preset is copied into, so previewing never touches the asset.
        private readonly UIAnimation _preview = new UIAnimation();
        // The object we snapshotted; non-null == a preview is live and owes a restore.
        private RectTransform _previewTarget;

        protected override string HeaderTitle => "Animation Preset";
        protected override string HeaderSubtitle => ((UIAnimationPreset)target).fullName;
        protected override Color Accent => NeoColors.Animation;

        private void OnDisable() => RestorePreview();

        protected override void DrawBody()
        {
            AnimationPresetGUI.Draw(serializedObject);

            GUILayout.Space(NeoGUI.Spacing);
            DrawPreviewControls((UIAnimationPreset)target);
        }

        private void DrawPreviewControls(UIAnimationPreset preset)
        {
            // Live preview is single-target only (one concrete object to snapshot/restore).
            RectTransform selected = targets.Length == 1 && Selection.activeGameObject != null
                ? Selection.activeGameObject.transform as RectTransform
                : null;
            bool previewing = _previewTarget != null;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (previewing)
                {
                    if (GUILayout.Button(new GUIContent("▶ Replay", "Restart the preview from the object's rest state."),
                            EditorStyles.miniButtonLeft))
                        StartPreview(preset, _previewTarget);
                    if (GUILayout.Button(new GUIContent("■ Stop", "Restore the object to its original state."),
                            EditorStyles.miniButtonRight))
                        RestorePreview();
                }
                else
                {
                    using (new EditorGUI.DisabledScope(selected == null))
                    {
                        var content = new GUIContent(
                            selected == null ? "▶ Preview (select a UI object)" : "▶ Preview on selection",
                            selected == null
                                ? "Select a UI object in the scene to preview"
                                : $"Play this preset on '{selected.name}'.");
                        if (GUILayout.Button(content, EditorStyles.miniButton))
                            StartPreview(preset, selected);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ preview lifecycle

        private void StartPreview(UIAnimationPreset preset, RectTransform target)
        {
            if (preset == null || target == null) return;

            RestorePreview();                          // put any prior preview back to rest first
            AnimationPreview.BeginPreview(target);     // snapshot pos/rot/scale/alpha for a clean revert
            preset.CopyTo(_preview);

            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null && _preview.fade.enabled) group = target.gameObject.AddComponent<CanvasGroup>();
            _preview.SetTarget(target, group);
            _preview.CaptureStartValues();             // target is at rest here — captures color/start endpoints
            _preview.onFinish = null;
            _preview.Play();

            _previewTarget = target;
        }

        // Symmetric with AnimationPresetBrowserPopup.StopPreview: settle the scratch channels (incl. color,
        // which the transform snapshot doesn't cover) then hand the RectTransform back to its snapshot.
        private void RestorePreview()
        {
            if (_previewTarget == null) return;
            _preview.Stop(silent: true);
            _preview.RestoreStartValues();
            AnimationPreview.EndPreview(_previewTarget);
            _previewTarget = null;
        }
    }

    /// <summary>
    /// Custom inspector for a <see cref="ThemeBundleDefinition"/> — the no-C# theme-personality asset.
    /// Sectioned like <see cref="ThemeEditor"/> (Identity / Variants / Shape / Motion) with the Theming
    /// accent, plus an <b>Apply Bundle</b> button that routes through the exact
    /// <see cref="ThemeBundles.Apply"/> path the Tools → Neo UI → Setup → Apply Theme Bundle menu uses
    /// (recolors the theme across every variant AND reseeds/recolors the widget-preset library). The
    /// apply is gated behind a confirm dialog that names those consequences.
    /// </summary>
    [CustomEditor(typeof(ThemeBundleDefinition))]
    public class ThemeBundleDefinitionEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Theme Bundle";
        protected override string HeaderSubtitle
        {
            get
            {
                var def = (ThemeBundleDefinition)target;
                string display = string.IsNullOrWhiteSpace(def.bundleName) ? def.name : def.bundleName;
                return $"{display} · {def.variants?.Count ?? 0} variant(s)";
            }
        }
        protected override Color Accent => NeoColors.Theming;

        protected override void DrawBody()
        {
            if (NeoGUI.BeginFoldoutSection("NeoUI.ThemeBundle.Identity", "Identity", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bundleName"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.ThemeBundle.Variants", "Variants & tokens", defaultOpen: true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("variants"), true);
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.ThemeBundle.Shape", "Shape personality"))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("cardRadius"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("panelRadius"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("controlRadius"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("cardGradientToToken"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shadowSoftness"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.ThemeBundle.Motion", "Motion personality"))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("motionDuration"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("motionEase"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("headlineSpacing"));
            }
            NeoGUI.EndFoldoutSection();

            GUILayout.Space(NeoGUI.Spacing);
            DrawApplyButton((ThemeBundleDefinition)target);
        }

        private static void DrawApplyButton(ThemeBundleDefinition def)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = NeoColors.Theming;
                bool apply = GUILayout.Button(
                    new GUIContent("Apply Bundle",
                        "Recolor the project theme (all variants) and reseed/recolor the widget-preset library from this bundle."),
                    GUILayout.Height(24f), GUILayout.MinWidth(140f));
                GUI.backgroundColor = prev;
                if (!apply) return;

                string display = string.IsNullOrWhiteSpace(def.bundleName) ? def.name : def.bundleName;
                NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
                ThemeBundles.Bundle bundle = def.ToBundle();

                // Name the REAL consequences via the structural diff (B6) rather than a generic warning.
                ThemeBundles.BundleDiff diff = ThemeBundles.PreviewDiff(bundle, settings.theme, settings);
                if (diff.IsEmpty)
                {
                    EditorUtility.DisplayDialog("Apply Theme Bundle",
                        $"'{display}' is already applied — no tokens or styles would change.", "OK");
                    return;
                }
                bool confirmed = EditorUtility.DisplayDialog(
                    "Apply Theme Bundle",
                    $"Apply '{display}' to the project?\n\n" + diff.Summarize() +
                    "\n\nExisting token edits in the Design System window will be overwritten. " +
                    "Same action as Tools → Neo UI → Setup → Apply Theme Bundle.",
                    "Apply", "Cancel");
                if (!confirmed) return;

                // Route through the identical code path as the menu (ThemeBundles.ApplyFromMenu).
                var report = new GenerateReport();
                ThemeBundles.Apply(bundle, settings, report);
                Debug.Log($"[Neo.UI] {report}");
            }
        }
    }

    /// <summary>
    /// Custom inspector for a <see cref="NeoLayoutTemplateDefinition"/> — the no-C# layout-scaffold seam
    /// (discovered by <see cref="NeoLayoutTemplates"/>, appearing in <c>GameObject → Neo UI → Insert
    /// Template…</c>). Sectioned in house style (Identity / Content) with the Containers accent, plus a
    /// validation HelpBox that resolves the definition's JSON and reports whether it parses as a
    /// <see cref="UISpec"/> — the same parse <see cref="NeoLayoutTemplates.Insert"/> performs, surfaced up
    /// front so a malformed template is caught at authoring time rather than silently no-op'ing on insert.
    /// </summary>
    [CustomEditor(typeof(NeoLayoutTemplateDefinition))]
    public class NeoLayoutTemplateDefinitionEditor : NeoUIEditor
    {
        protected override string HeaderTitle => "Layout Template";
        protected override string HeaderSubtitle
        {
            get
            {
                var def = (NeoLayoutTemplateDefinition)target;
                return string.IsNullOrWhiteSpace(def.id) ? "Insert Template… scaffold" : def.id;
            }
        }
        protected override Color Accent => NeoColors.Containers;

        protected override void DrawBody()
        {
            if (NeoGUI.BeginFoldoutSection("NeoUI.LayoutTemplate.Identity", "Identity", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.LayoutTemplate.Content", "Content", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("specJson"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("specPathOverride"));
                DrawValidation((NeoLayoutTemplateDefinition)target);
            }
            NeoGUI.EndFoldoutSection();
        }

        // Resolve the spec JSON quietly (no warning log — that's for actual inserts) and report parse state.
        private static void DrawValidation(NeoLayoutTemplateDefinition def)
        {
            string json = def.specJson != null ? def.specJson.text
                : !string.IsNullOrEmpty(def.specPathOverride)
                    ? ResolveOverride(def.specPathOverride)
                    : null;

            if (string.IsNullOrWhiteSpace(json))
            {
                EditorGUILayout.HelpBox(
                    "No spec JSON assigned yet — assign a .json TextAsset above (or a Spec Path Override). " +
                    "The template inserts its views/popups into the current selection.", MessageType.Info);
                return;
            }

            try
            {
                UISpec spec = UISpec.FromJson(json);
                int views = spec?.views?.Count ?? 0;
                int popups = spec?.popups?.Count ?? 0;
                EditorGUILayout.HelpBox($"Valid spec — {views} view(s), {popups} popup(s).", MessageType.None);
            }
            catch (System.Exception e)
            {
                EditorGUILayout.HelpBox($"This spec does not parse as a UISpec: {e.Message}", MessageType.Error);
            }
        }

        private static string ResolveOverride(string path)
        {
            TextAsset ta = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (ta != null) return ta.text;
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        }
    }
}
