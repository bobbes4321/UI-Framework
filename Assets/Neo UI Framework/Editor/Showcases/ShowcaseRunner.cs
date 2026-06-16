using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The one-click orchestration behind the Hub: turns a <see cref="Showcase"/> (metadata + a spec
    /// path) into an open, playable scene, hiding the generate → build → open ritual entirely.
    /// <para>
    /// Two entry points, with opposite safety stances:
    /// <list type="bullet">
    /// <item><see cref="Open"/> is <b>idempotent and non-destructive</b> — it generates ONLY when the
    /// showcase has nothing on disk, and otherwise just opens the committed scene (the fast path). It
    /// never clobbers human prefab edits.</item>
    /// <item><see cref="Regenerate"/> routes through <see cref="SpecBaseline.Sync"/> — the
    /// safe-regenerate protocol that folds human drift back in and <i>refuses</i> rather than silently
    /// losing off-spec edits. Regenerate = sync, never a raw destructive generate.</item>
    /// </list>
    /// Every generate/build runs inside <c>using NeoWorkspace.Scoped(showcase)</c> so the assets land
    /// in (and the baseline follows) the showcase's own isolated <c>Generated/</c> folder — distinct
    /// showcases can never collide.
    /// </para>
    /// </summary>
    public static class ShowcaseRunner
    {
        /// <summary>
        /// Opens a showcase's scene, generating + building it first only if nothing exists yet.
        /// <list type="number">
        /// <item>guard the active scene (offer to save unsaved work);</item>
        /// <item>ensure package setup (settings/starter kit/fonts) is present — idempotent and cheap;</item>
        /// <item><b>fast path:</b> when the scene file AND the showcase's generated <c>Views</c> folder
        ///   already exist, just open the scene (never regenerates — human edits are safe);</item>
        /// <item>otherwise, inside the showcase scope: generate from the spec if <c>Views</c> is missing,
        ///   build the scene at the showcase's path, run the optional <see cref="Showcase.postBuild"/>
        ///   hook, then open it.</item>
        /// </list>
        /// Aborts (with an error dialog) rather than opening a broken scene if generation has problems.
        /// </summary>
        public static void Open(Showcase showcase)
        {
            if (showcase == null) { Debug.LogWarning("[Neo.UI] ShowcaseRunner.Open: null showcase"); return; }
            if (string.IsNullOrEmpty(showcase.specPath))
            {
                Debug.LogWarning($"[Neo.UI] Showcase '{showcase.id}' has no spec path — cannot open.");
                return;
            }

            // never discard a human's unsaved scene work silently
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureSetup();

            // Fast path: assets + scene already on disk → open, never regenerate.
            if (File.Exists(showcase.ScenePath) && AssetDatabase.IsValidFolder($"{showcase.GeneratedRoot}/Views"))
            {
                EditorSceneManager.OpenScene(showcase.ScenePath);
                Debug.Log($"[Neo.UI] Opened showcase '{showcase.id}' (existing scene — no regenerate).");
                return;
            }

            using (NeoWorkspace.Scoped(showcase))
            {
                // generate only when nothing is there yet — keeps Open non-destructive
                if (!AssetDatabase.IsValidFolder($"{showcase.GeneratedRoot}/Views"))
                {
                    UISpec spec;
                    try
                    {
                        spec = UISpec.FromJson(File.ReadAllText(showcase.specPath));
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Neo.UI] Showcase '{showcase.id}': could not read spec '{showcase.specPath}': {e.Message}");
                        EditorUtility.DisplayDialog("Open Showcase",
                            $"Could not read the spec for '{showcase.title}':\n{e.Message}", "OK");
                        return;
                    }

                    GenerateReport report = UISpecGenerator.Generate(spec);
                    if (report.hasProblems)
                    {
                        Debug.LogError($"[Neo.UI] Showcase '{showcase.id}' generation failed:\n{report}");
                        EditorUtility.DisplayDialog("Open Showcase",
                            $"Generation of '{showcase.title}' reported problems — the scene was not opened.\n\n{report}",
                            "OK");
                        return; // don't open a broken scene
                    }
                }

                try
                {
                    GeneratedSceneBuilder.Build(showcase.flowName, showcase.ScenePath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Neo.UI] Showcase '{showcase.id}' scene build failed: {e.Message}");
                    EditorUtility.DisplayDialog("Open Showcase",
                        $"Building the scene for '{showcase.title}' failed:\n{e.Message}", "OK");
                    return;
                }
            } // root restored before we open, so the opened scene's assets resolve normally

            Scene opened = EditorSceneManager.OpenScene(showcase.ScenePath);
            if (showcase.postBuild != null)
            {
                try { showcase.postBuild(opened); EditorSceneManager.SaveScene(opened); }
                catch (Exception e)
                {
                    Debug.LogError($"[Neo.UI] Showcase '{showcase.id}' postBuild hook threw: {e}");
                }
            }
            Debug.Log($"[Neo.UI] Built and opened showcase '{showcase.id}' at {showcase.ScenePath} — press Play.");
        }

        /// <summary>
        /// The safe regenerate: scope to the showcase and run <see cref="SpecBaseline.Sync"/> on its
        /// spec. Returns the <see cref="SyncResult"/> so the caller (Hub) can surface a refusal,
        /// conflicts, or off-spec warnings instead of silently overwriting human edits.
        /// Returns null (with a warning) when the showcase has no readable spec.
        /// </summary>
        public static SyncResult Regenerate(Showcase showcase)
        {
            if (showcase == null) { Debug.LogWarning("[Neo.UI] ShowcaseRunner.Regenerate: null showcase"); return null; }
            if (string.IsNullOrEmpty(showcase.specPath) || !File.Exists(showcase.specPath))
            {
                Debug.LogWarning($"[Neo.UI] Showcase '{showcase.id}': no spec at '{showcase.specPath}' — cannot regenerate.");
                return null;
            }

            UISpec incoming;
            try
            {
                incoming = UISpec.FromJson(File.ReadAllText(showcase.specPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Neo.UI] Showcase '{showcase.id}': could not read spec '{showcase.specPath}': {e.Message}");
                return null;
            }

            using (NeoWorkspace.Scoped(showcase))
                return SpecBaseline.Sync(incoming);
        }

        /// <summary>
        /// Idempotently ensures the package's one-time setup is present: the settings asset (+ its
        /// databases/theme), the starter kit, and the TMP font assets. Each is created only when
        /// missing, so calling this on every Open is cheap.
        /// </summary>
        public static void EnsureSetup()
        {
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();

            // starter kit: a missing factory-referenced token means the kit was never built
            if (settings != null && settings.theme != null
                && !settings.theme.HasToken(UIWidgetFactory.TokenPrimary))
                StarterKitBootstrap.CreateOrRepair();

            // fonts: the icon font is the canonical signal the font assets were generated + wired
            if (settings != null && settings.iconFont == null)
                FontAssetBootstrap.EnsureIconFont(settings);
        }
    }
}
