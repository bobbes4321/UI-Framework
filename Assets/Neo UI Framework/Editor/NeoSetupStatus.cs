using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Cheap "is this setup step already installed" probes, shared by the Hub's setup strip
    /// (<see cref="NeoUIHubWindow"/>) and the New Project Setup wizard (<see cref="NeoSetupWizard"/>) so
    /// both windows report identical install state from ONE implementation
    /// (design-system-cohesion-plan Phase 1.2). Every probe is a handful of asset-database lookups /
    /// in-memory registry counts — no folder scans, no per-OnGUI recompute; callers should snapshot
    /// once (OnEnable/OnFocus/an explicit Refresh button) and cache the result, per the CLAUDE.md
    /// editor-perf rules.
    /// </summary>
    internal static class NeoSetupStatus
    {
        /// <summary> A point-in-time read of what's already set up in the project. </summary>
        public readonly struct Snapshot
        {
            /// <summary> The settings asset, or null if it hasn't been created yet. </summary>
            public readonly NeoUISettings settings;
            public readonly bool hasSettings;
            public readonly bool hasStarterKit;
            public readonly bool hasFonts;
            public readonly bool hasPresets;
            public readonly bool hasAnimations;
            public readonly bool hasEffects;

            public Snapshot(NeoUISettings settings, bool hasStarterKit, bool hasFonts, bool hasPresets,
                bool hasAnimations, bool hasEffects)
            {
                this.settings = settings;
                hasSettings = settings != null;
                this.hasStarterKit = hasStarterKit;
                this.hasFonts = hasFonts;
                this.hasPresets = hasPresets;
                this.hasAnimations = hasAnimations;
                this.hasEffects = hasEffects;
            }
        }

        /// <summary>
        /// Recomputes the full snapshot. Cheap, but still meant to be called on open/focus/explicit
        /// refresh only (never per-OnGUI) — cache the result in the caller.
        /// </summary>
        public static Snapshot Compute()
        {
            var settings =
                AssetDatabase.LoadAssetAtPath<NeoUISettings>(NeoUISettingsBootstrap.SettingsAssetPath);
            // starter kit: a factory-referenced token present on the theme = the kit was expanded
            bool hasStarterKit = settings != null && settings.theme != null
                && settings.theme.HasToken(UIWidgetFactory.TokenPrimary);
            // fonts: the icon font wired onto settings is the canonical "fonts generated" signal
            bool hasFonts = settings != null && settings.iconFont != null;
            // presets / animations: discovered library non-empty (seeded assets OR project-authored ones)
            bool hasPresets = NeoWidgetPresets.All.Count > 0;
            bool hasAnimations = AnimationPresetRegistry.All.Count > 0;
            // effects: the dissolve ShapeEffectDefinition is the canonical "effect assets generated" signal
            bool hasEffects =
                AssetDatabase.LoadAssetAtPath<ShapeEffectDefinition>(NoiseAssetBootstrap.DefinitionPath) != null;
            return new Snapshot(settings, hasStarterKit, hasFonts, hasPresets, hasAnimations, hasEffects);
        }
    }

    /// <summary>
    /// The intent-name ↔ theme-token map the New Project Setup wizard's custom-color fields are built
    /// on — the SAME pairing <see cref="ThemeBundles.BuildPalette"/> writes, kept in one pure, testable
    /// place so loading a theme's current colors back into the wizard (design-system-cohesion-plan
    /// Phase 1.2) is a guaranteed inverse of applying it, not a hand-duplicated guess. Pure/static and
    /// read-only — never touches an asset.
    /// </summary>
    internal static class NeoSetupPalette
    {
        // intent key (as used by ReadFrom's result) -> theme token name.
        // "warning"/"error" intentionally use ThemeBundles.BuildPalette's literal string keys — those two
        // tokens have no UIWidgetFactory constant.
        public static readonly (string intent, string token)[] IntentTokens =
        {
            ("background", UIWidgetFactory.TokenBackground),
            ("surface", UIWidgetFactory.TokenSurface),
            ("surfaceElevated", UIWidgetFactory.TokenSurfaceElevated),
            ("outline", UIWidgetFactory.TokenOutline),
            ("primary", UIWidgetFactory.TokenPrimary),
            ("textOnPrimary", UIWidgetFactory.TokenTextOnPrimary),
            ("textStrong", UIWidgetFactory.TokenTextStrong),
            ("textDefault", UIWidgetFactory.TokenTextDefault),
            ("textMuted", UIWidgetFactory.TokenTextMuted),
            ("success", UIWidgetFactory.TokenSuccess),
            ("warning", "Warning"),
            ("error", "Error"),
            ("shadow", UIWidgetFactory.TokenShadow),
        };

        /// <summary>
        /// Reads every intent this theme's active variant has a color for. An intent whose token is
        /// missing from the theme is simply absent from the result (caller keeps its own default) —
        /// this never guesses or fills gaps.
        /// </summary>
        public static Dictionary<string, Color> ReadFrom(Theme theme)
        {
            var result = new Dictionary<string, Color>();
            if (theme == null) return result;
            foreach ((string intent, string token) in IntentTokens)
                if (theme.TryGetColor(token, out Color color))
                    result[intent] = color;
            return result;
        }
    }
}
