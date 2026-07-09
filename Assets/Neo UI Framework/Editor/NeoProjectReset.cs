using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One resettable piece of the project — the uninstall-side twin of a create-or-repair bootstrap.
    /// A descriptor owns the asset paths its bootstrap creates (<see cref="cleanPaths"/>, evaluated
    /// lazily at plan time so registries like <see cref="ShowcaseRegistry"/> are only consulted when a
    /// reset is actually being planned) plus an optional <see cref="unwire"/> that prunes the dangling
    /// references deletion leaves on a surviving <see cref="NeoUISettings"/> asset. Registered through
    /// <see cref="NeoResetComponents"/> — a consuming project that ships its own bootstrap adds a
    /// descriptor for it and its content shows up in the Reset wizard for free.
    /// </summary>
    public sealed class ResetComponentDescriptor
    {
        /// <summary> Stable registry id, e.g. <c>"starter-kit"</c>. </summary>
        public string id;

        /// <summary> Row label in the Reset wizard, e.g. <c>"Starter Kit (widget prefab library)"</c>. </summary>
        public string label;

        /// <summary> Tooltip explaining what gets deleted and how to re-create it. </summary>
        public string tooltip;

        /// <summary>
        /// True to leave this component UNCHECKED by default in the Reset wizard — for curated libraries
        /// nobody plausibly wants empty (animations, transitions, fonts) and user-authored content
        /// (showcase scenes, custom theme bundles, binding stubs). The user can still tick it explicitly.
        /// </summary>
        public bool keepByDefault;

        /// <summary>
        /// The asset paths (files or folders, <c>Assets/...</c> forward-slash form) this component's
        /// bootstrap owns. Evaluated at plan time; paths that don't currently exist are skipped, and every
        /// path must pass <see cref="NeoProjectReset"/>'s safety guard or it is refused with a warning.
        /// </summary>
        public Func<IEnumerable<string>> cleanPaths;

        /// <summary>
        /// Optional post-delete cleanup run when the settings asset SURVIVES the reset: null out / prune
        /// the settings references that now dangle (e.g. <c>iconFont</c> after the fonts folder is gone)
        /// so the asset doesn't accumulate missing-GUID noise. Never runs when settings were deleted too.
        /// </summary>
        public Action<NeoUISettings> unwire;
    }

    /// <summary>
    /// Pattern R registry of <see cref="ResetComponentDescriptor"/>s — the catalog behind
    /// <c>Tools → Neo UI → Setup → Reset To Clean Slate…</c> (<see cref="NeoResetWizard"/>). Built-ins
    /// (<see cref="NeoResetComponentDefaults"/>) mirror every create-or-repair bootstrap; a consuming
    /// project folds its own resettable content in with a single <see cref="Register"/> call, no fork.
    /// </summary>
    public static class NeoResetComponents
    {
        private static readonly NeoKeyedRegistry<ResetComponentDescriptor> _registry =
            new NeoKeyedRegistry<ResetComponentDescriptor>(
                d => d.id,
                builtins: NeoResetComponentDefaults.Builtins,
                validate: d => d.cleanPaths != null && !string.IsNullOrEmpty(d.label),
                registryName: "NeoResetComponents");

        /// <summary> All registered components (built-ins + project additions), in registration order. </summary>
        public static IReadOnlyList<ResetComponentDescriptor> All => _registry.All;

        /// <summary> Case-sensitive (ordinal) lookup by id. False (and null) when nothing matches. </summary>
        public static bool TryGet(string id, out ResetComponentDescriptor component) =>
            _registry.TryGet(id, out component);

        /// <summary>
        /// Registers a component. Replaces in place when one with the same id (ordinal) already exists —
        /// so a project can override a built-in — otherwise appends. Null / id-less / path-less
        /// descriptors are warned-and-ignored, never thrown.
        /// </summary>
        public static void Register(ResetComponentDescriptor component) => _registry.Register(component);

        /// <summary> Test-only: removes a registered component by id (ordinal). True if one was removed. </summary>
        internal static bool Remove(string id) => _registry.Remove(id);

        /// <summary> Test-only: restores the registry to exactly the code-seeded built-ins. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }

    /// <summary>
    /// The code-seeded built-in reset components — one per create-or-repair bootstrap, kept in sync with
    /// the <c>Tools/Neo UI/Setup/*</c> menu tree, plus the generated-content roots (shared generated UI,
    /// showcase generated content, custom theme bundles, binding stubs). Paths reference the owning
    /// bootstrap's constants so they can never drift from what actually gets created.
    /// </summary>
    internal static class NeoResetComponentDefaults
    {
        public static IEnumerable<ResetComponentDescriptor> Builtins()
        {
            // --- the Setup wizard's include list, in its order --------------------------------------
            yield return new ResetComponentDescriptor
            {
                id = "settings", label = "Core settings + databases + theme",
                tooltip = "The NeoUISettings asset, the id databases and the default Theme. " +
                          "Re-create via Setup → Create or Repair Settings (or the Setup wizard).",
                cleanPaths = () => new[]
                {
                    NeoUISettingsBootstrap.SettingsAssetPath,
                    NeoUISettingsBootstrap.ResourcesFolder + "/DefaultTheme.asset",
                    NeoUISettingsBootstrap.DatabasesFolder,
                },
            };
            yield return new ResetComponentDescriptor
            {
                id = "starter-kit", label = "Starter Kit (widget prefab library + menu widgets)",
                tooltip = "The themed widget prefab library under Assets/Neo UI Framework/Starter, " +
                          "including the menu widget library. Re-create via Setup → Create or Repair " +
                          "Starter Kit / Menu Widget Library.",
                cleanPaths = () => new[] { StarterKitBootstrap.StarterFolder },
                unwire = settings =>
                {
                    // only clear when the reference actually died with the folder — a project-authored
                    // library living elsewhere stays wired
                    if (settings.menuWidgets == null) settings.menuWidgets = null;
                },
            };
            yield return new ResetComponentDescriptor
            {
                id = "fonts", label = "Fonts (Inter + Lucide icons)", keepByDefault = true,
                tooltip = "The committed TMP SDF font assets under Assets/Neo UI Framework/Fonts. Kept " +
                          "by default — regeneration (Setup → Create or Repair Fonts) is slow and no " +
                          "project plausibly wants an empty font set.",
                cleanPaths = () => new[] { FontAssetBootstrap.FontsFolder },
                unwire = settings =>
                {
                    if (settings.iconFont == null) settings.iconFont = null;
                },
            };
            yield return new ResetComponentDescriptor
            {
                id = "widget-presets", label = "Widget preset library",
                tooltip = "The seeded NeoWidgetPreset assets (Primary Button, Section Header, …) under " +
                          "Assets/Neo UI Framework/Presets. Re-create via Setup → Create or Repair " +
                          "Widget Presets.",
                cleanPaths = () => new[] { NeoWidgetPresets.PresetsRoot },
            };
            yield return new ResetComponentDescriptor
            {
                id = "animations", label = "Animation preset library", keepByDefault = true,
                tooltip = "The curated ~46-preset motion library under Assets/Neo UI Framework/" +
                          "Animations. Kept by default — a project with zero animation presets isn't a " +
                          "plausible starting point. Re-create via Setup → Create or Repair Animation " +
                          "Library.",
                cleanPaths = () => new[] { AnimationLibraryBootstrap.LibraryRoot },
                unwire = settings =>
                {
                    // prune role-default rows whose preset asset died with the library
                    settings.animatorDefaults?.RemoveAll(entry => entry == null || entry.preset == null);
                },
            };
            yield return new ResetComponentDescriptor
            {
                id = "transitions", label = "View transition library", keepByDefault = true,
                tooltip = "The curated ViewTransitionAsset library under Assets/Neo UI Framework/" +
                          "Transitions. Kept by default for the same reason as animations. Re-create via " +
                          "Setup → Create or Repair Transition Library.",
                cleanPaths = () => new[] { TransitionLibraryBootstrap.LibraryRoot },
                unwire = settings =>
                {
                    settings.viewTransitions?.RemoveAll(transition => transition == null);
                },
            };
            yield return new ResetComponentDescriptor
            {
                id = "effects", label = "Effect assets (Tier-2 materials)",
                tooltip = "The baked noise/ramp textures + dissolve/holo/glitch materials under " +
                          "Assets/Neo UI Framework/Resources/Effects. Re-create via Setup → Create or " +
                          "Repair Effect Assets.",
                cleanPaths = () => new[] { NoiseAssetBootstrap.EffectsFolder },
            };

            // --- generated / authored content outside the bootstraps --------------------------------
            yield return new ResetComponentDescriptor
            {
                id = "generated-ui", label = "Generated UI (shared root + baseline)",
                tooltip = "Everything under " + UISpecGenerator.DefaultGeneratedRoot + " — spec-generated " +
                          "views, popups, flow graphs and the stored baseline. Regenerate from a spec via " +
                          "the agent bridge or Advanced → Generate From Spec….",
                cleanPaths = () => new[] { UISpecGenerator.DefaultGeneratedRoot },
            };
            yield return new ResetComponentDescriptor
            {
                id = "showcases", label = "Showcase generated content + scenes", keepByDefault = true,
                tooltip = "Every registered showcase's Generated/ root and committed .unity scene (specs " +
                          "are never touched). Kept by default — these are the package's living demo " +
                          "catalog; the Hub's Open re-generates any showcase on demand.",
                cleanPaths = ShowcaseCleanPaths,
            };
            yield return new ResetComponentDescriptor
            {
                id = "custom-themes", label = "Custom theme bundles", keepByDefault = true,
                tooltip = "ThemeBundleDefinition assets saved by the Setup wizard / Design System under " +
                          NeoSetupWizard.CustomThemesRoot + ". Kept by default — user-authored content.",
                cleanPaths = () => new[] { NeoSetupWizard.CustomThemesRoot },
            };
            yield return new ResetComponentDescriptor
            {
                id = "binding-stubs", label = "Generated binding stubs", keepByDefault = true,
                tooltip = "The C# wiring stubs under " + BindingStubGenerator.DefaultDirectory + ". Kept " +
                          "by default — the developer's hand-written sibling partials live beside them.",
                cleanPaths = () => new[] { BindingStubGenerator.DefaultDirectory },
            };
        }

        private static IEnumerable<string> ShowcaseCleanPaths()
        {
            foreach (Showcase showcase in ShowcaseRegistry.All)
            {
                yield return showcase.GeneratedRoot;
                yield return showcase.ScenePath;
            }
        }
    }

    /// <summary> One planned component: the descriptor plus the subset of its paths that exist right now. </summary>
    public sealed class ResetPlanEntry
    {
        public ResetComponentDescriptor component;
        public List<string> paths = new List<string>();
    }

    /// <summary> The existence-filtered, safety-checked set of deletions a reset would perform. </summary>
    public sealed class ResetPlan
    {
        public readonly List<ResetPlanEntry> entries = new List<ResetPlanEntry>();

        public int TotalPathCount
        {
            get
            {
                int count = 0;
                foreach (ResetPlanEntry entry in entries) count += entry.paths.Count;
                return count;
            }
        }
    }

    /// <summary> What a reset actually did — surfaced verbatim by the wizard (no silent failures). </summary>
    public sealed class ResetReport
    {
        public int deletedPaths;
        public readonly List<string> failedPaths = new List<string>();
        public readonly List<string> componentLabels = new List<string>();

        public string Summary =>
            componentLabels.Count == 0
                ? "Nothing to delete."
                : $"Deleted {deletedPaths} asset path(s) — {string.Join(", ", componentLabels)}." +
                  (failedPaths.Count > 0
                      ? $" {failedPaths.Count} path(s) could not be deleted (see Console)."
                      : string.Empty);
    }

    /// <summary>
    /// The clean-slate engine behind <see cref="NeoResetWizard"/>: <see cref="BuildPlan"/> resolves a
    /// component selection into the concrete, existence-filtered asset paths that would be deleted
    /// (every path gated by <see cref="IsSafeToDelete"/> so a mis-registered descriptor can never take
    /// out package code, the NeoShape shader or the showcase specs), and <see cref="Execute"/> performs
    /// the deletion, prunes dangling settings references via each component's <c>unwire</c>, and logs
    /// loudly what failed. Split plan/execute so the plan is previewable in the wizard and testable
    /// without ever deleting real assets.
    /// </summary>
    public static class NeoProjectReset
    {
        /// <summary>
        /// Paths a reset may NEVER delete — nor delete an ancestor of. Package code, the shared shader
        /// and the showcase specs are the package itself, not bootstrap output.
        /// </summary>
        private static readonly string[] ProtectedRoots =
        {
            NeoUISettingsBootstrap.PackageRoot,
            NeoUISettingsBootstrap.PackageRoot + "/Runtime",
            NeoUISettingsBootstrap.PackageRoot + "/Editor",
            NeoUISettingsBootstrap.PackageRoot + "/Tests",
            NeoUISettingsBootstrap.ResourcesFolder,
            NeoUISettingsBootstrap.ResourcesFolder + "/NeoShape.shader",
            ShowcaseRegistry.ShowcasesRoot,
            ShowcaseRegistry.ShowcasesRoot + "/Specs",
        };

        /// <summary>
        /// True when <paramref name="path"/> is a deletable project path: under <c>Assets/</c>, no parent
        /// traversal, not a protected root, and not an ANCESTOR of one (deleting an ancestor deletes the
        /// protected content with it). Descendants of a protected root (e.g. Resources/Effects under the
        /// protected Resources folder) are fine — protection is about the node and what contains it.
        /// </summary>
        internal static bool IsSafeToDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/').TrimEnd('/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            if (path.Contains("..")) return false;
            foreach (string root in ProtectedRoots)
            {
                if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return false;
                if (root.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// Resolves <paramref name="componentIds"/> into the concrete deletions a reset would perform:
        /// unknown ids warn and are skipped, paths that don't currently exist are dropped, and any path
        /// failing <see cref="IsSafeToDelete"/> is refused with a warning (never silently) — so the plan
        /// the wizard previews is exactly what <see cref="Execute"/> will do.
        /// </summary>
        public static ResetPlan BuildPlan(IEnumerable<string> componentIds)
        {
            var plan = new ResetPlan();
            if (componentIds == null) return plan;

            foreach (string id in componentIds)
            {
                if (!NeoResetComponents.TryGet(id, out ResetComponentDescriptor component))
                {
                    Debug.LogWarning($"[Neo.UI] Reset: unknown component '{id}'.");
                    continue;
                }

                var entry = new ResetPlanEntry { component = component };
                foreach (string rawPath in component.cleanPaths())
                {
                    if (string.IsNullOrEmpty(rawPath)) continue;
                    string path = rawPath.Replace('\\', '/').TrimEnd('/');
                    if (!IsSafeToDelete(path))
                    {
                        Debug.LogWarning(
                            $"[Neo.UI] Reset: refusing unsafe path '{path}' from component '{component.id}'.");
                        continue;
                    }
                    if (!ExistsInProject(path)) continue;
                    entry.paths.Add(path);
                }

                if (entry.paths.Count > 0) plan.entries.Add(entry);
            }

            return plan;
        }

        /// <summary>
        /// Performs the planned deletions in one batch, then — when the settings asset survived — runs
        /// each deleted component's <c>unwire</c> to prune now-dangling references. When settings were
        /// deleted, the cached <see cref="NeoUISettings.instance"/> is cleared instead so nothing keeps
        /// handing out a destroyed object. Every failed path is logged (no silent failures).
        /// </summary>
        public static ResetReport Execute(ResetPlan plan)
        {
            var report = new ResetReport();
            if (plan == null || plan.TotalPathCount == 0) return report;

            var paths = new List<string>();
            foreach (ResetPlanEntry entry in plan.entries)
            {
                paths.AddRange(entry.paths);
                report.componentLabels.Add(entry.component.label);
            }

            AssetDatabase.DeleteAssets(paths.ToArray(), report.failedPaths);
            report.deletedPaths = paths.Count - report.failedPaths.Count;
            foreach (string failed in report.failedPaths)
                Debug.LogWarning($"[Neo.UI] Reset: couldn't delete '{failed}'.");

            var settings =
                AssetDatabase.LoadAssetAtPath<NeoUISettings>(NeoUISettingsBootstrap.SettingsAssetPath);
            if (settings == null)
            {
                NeoUISettings.instance = null;
            }
            else
            {
                foreach (ResetPlanEntry entry in plan.entries)
                    entry.component.unwire?.Invoke(settings);
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Neo.UI] Reset To Clean Slate — {report.Summary}");
            return report;
        }

        private static bool ExistsInProject(string path) =>
            AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null;
    }
}
