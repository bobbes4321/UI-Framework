using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Colors" tab: browse/edit theme token colors per variant, add tokens and
    /// variants, and re-derive hover/pressed states. Split out of the old monolithic
    /// <see cref="NeoDesignSystemWindow"/> (Phase 2.9); restructured for legibility so the variant set is
    /// visible at a glance and creation rows are discoverable instead of buried below a splitter: a
    /// one-line subtitle, a "Variants" section listing every variant as a row (browse-toggle + an
    /// "Active" tag + a per-row "Set Active" button, plus a "Duplicate" button on whichever row is
    /// BROWSED — copies every token color of the browsed variant into a newly-named variant, e.g.
    /// "Dark" → "Dark 2", and browses the copy) with the "New variant" row directly under it, then a
    /// "Tokens — {variant}" section with a search field (<see cref="DesignSystemCatalog.SearchField"/>,
    /// filtering rows by token name — the header count always stays the TOTAL, a "N of M shown" line
    /// appears only while filtered) followed by its "New token" row pinned at the TOP of the token list
    /// (visible regardless of the filter), and the re-derive action last. The draw semantics are
    /// otherwise unchanged, including the Phase-0 fixes: browse-vs-active variant state (B1 — clicking a
    /// variant row only changes what this window is BROWSING, never the live
    /// <see cref="Theme.ActiveVariantName"/>; only the explicit "Set Active" button does that — and
    /// duplicating a variant browses the new copy without touching the live active variant either),
    /// theme-wide token-removal confirm (B3) and per-variant state derivation (via
    /// <see cref="NeoDesignSystemWindow.DeriveStates"/>, which stays on the window so its tests keep their
    /// call contract).
    /// </summary>
    internal static class ColorsTab
    {
        /// <summary> Per-window UI state for the Colors tab. </summary>
        internal sealed class State
        {
            // The variant the window is currently BROWSING (local view state only — it never flips the
            // live active variant; that's the explicit "Set Active" button — B1). Falls back to the
            // active variant when unset/stale.
            public string browsedVariant;
            public string newToken = "";
            public string newVariant = "";
            public string search = "";
        }

        private static readonly GUIContent DuplicateVariantContent = new GUIContent("Duplicate",
            "Copy this variant — every token's color — as a starting point (e.g. Dark → Light).");
        private static readonly GUIContent SetActiveContent = new GUIContent("Set Active",
            "Make this the live variant the UI renders with.");

        internal static object CreateState() => new State();

        internal static void Draw(DesignSystemTabContext ctx)
        {
            Theme theme = ctx.theme;
            var s = ctx.State<State>();

            Color prevColor = GUI.contentColor;
            GUI.contentColor = NeoColors.TextSubtle;
            EditorGUILayout.LabelField(
                "Theme color tokens — referenced by widgets, presets, shape styles and specs. Each " +
                "variant (e.g. Dark/Light) supplies its own value per token.", EditorStyles.wordWrappedMiniLabel);
            GUI.contentColor = prevColor;

            string active = string.Empty;
            EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
            if (theme.Variants.Count == 0)
            {
                EditorGUILayout.HelpBox("No color variants yet. Run Setup → Create or Repair Starter Kit " +
                    "to seed Dark/Light, or add one below.", MessageType.Info);
            }
            else
            {
                active = theme.ActiveVariantName;
                if (string.IsNullOrEmpty(active) || theme.GetVariant(active) == null)
                    active = theme.Variants[0].name;

                // Browse state is LOCAL — selecting a row never flips the live active variant (B1).
                if (string.IsNullOrEmpty(s.browsedVariant) || theme.GetVariant(s.browsedVariant) == null)
                    s.browsedVariant = active;

                foreach (Theme.ThemeVariant tv in theme.Variants)
                    DrawVariantRow(theme, s, tv.name, active);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                s.newVariant = EditorGUILayout.TextField(
                    new GUIContent("New variant", "Adds a variant (e.g. \"Dark\"/\"Light\") with default colors."),
                    s.newVariant);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(s.newVariant))
                {
                    Undo.RecordObject(theme, "Add variant");
                    theme.AddVariant(s.newVariant.Trim());
                    EditorUtility.SetDirty(theme);
                    s.newVariant = "";
                }
            }

            if (theme.Variants.Count > 0)
            {
                NeoGUI.Splitter();
                Theme.ThemeVariant variant = theme.GetVariant(s.browsedVariant);
                if (variant != null)
                {
                    EditorGUILayout.LabelField($"Tokens — {variant.name} ({variant.colors.Count})",
                        EditorStyles.boldLabel);

                    DesignSystemCatalog.SearchField(ref s.search);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        s.newToken = EditorGUILayout.TextField(
                            new GUIContent("New token",
                                "Token name referenced by widgets/presets/shape styles/specs, e.g. \"Primary\"."),
                            s.newToken);
                        if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(s.newToken))
                        {
                            Undo.RecordObject(theme, "Add token");
                            theme.SetToken(s.newToken.Trim(), Color.gray);
                            EditorUtility.SetDirty(theme);
                            s.newToken = "";
                        }
                    }

                    List<Theme.TokenColor> shown = string.IsNullOrEmpty(s.search)
                        ? variant.colors.ToList()
                        : variant.colors.Where(tc => !string.IsNullOrEmpty(tc.token) &&
                            tc.token.IndexOf(s.search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                    if (shown.Count != variant.colors.Count)
                    {
                        Color prevSubtle = GUI.contentColor;
                        GUI.contentColor = NeoColors.TextSubtle;
                        EditorGUILayout.LabelField($"{shown.Count} of {variant.colors.Count} tokens shown",
                            EditorStyles.miniLabel);
                        GUI.contentColor = prevSubtle;
                    }

                    foreach (Theme.TokenColor tc in shown)
                        DrawTokenRow(theme, variant, tc);
                }
            }

            NeoGUI.Splitter();
            if (GUILayout.Button(new GUIContent("Re-derive hover / pressed states",
                "Recompute Primary/Success/Danger hover+pressed from their base color")))
                NeoDesignSystemWindow.DeriveStates(theme);
        }

        // One row per variant: a browse toggle (its own pressed-look IS the "which one am I browsing"
        // affordance), an "Active" tag on the live variant, a "Duplicate" button on whichever row is
        // BROWSED (active or not — always in the same slot so it's discoverable), and — only on the
        // browsed, non-active row — the explicit "Set Active" button (B1: the only thing allowed to
        // flip the live variant).
        private static void DrawVariantRow(Theme theme, State s, string variantName, string active)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool isActive = variantName == active;
                bool isBrowsed = variantName == s.browsedVariant;

                bool pressed = GUILayout.Toggle(isBrowsed,
                    new GUIContent(variantName, "Browse this variant's tokens below (does not change the live variant)."),
                    EditorStyles.miniButton, GUILayout.ExpandWidth(true));
                if (pressed && !isBrowsed)
                    s.browsedVariant = variantName;

                if (isActive)
                    NeoGUI.Badge("Active", NeoColors.Add);

                if (isBrowsed)
                {
                    if (GUILayout.Button(DuplicateVariantContent, EditorStyles.miniButton, GUILayout.Width(70f)))
                        DuplicateVariant(theme, s, variantName);

                    if (!isActive && GUILayout.Button(SetActiveContent, EditorStyles.miniButton, GUILayout.Width(84f)))
                    {
                        Undo.RecordObject(theme, "Set active variant");
                        theme.ActiveVariantName = variantName;
                        EditorUtility.SetDirty(theme);
                    }
                }
            }
        }

        // Duplicates the source variant under a uniquely-suffixed name ("Dark" -> "Dark 2", "Dark 3", …)
        // and copies EVERY token color of the source into it. Theme.AddVariant seeds a new variant from
        // the theme's FIRST variant (not necessarily the source being duplicated), so we still need this
        // explicit copy to make the semantic "duplicate what I'm browsing" hold even when the source
        // isn't variants[0]. Browses the new copy afterward — the live active variant is untouched.
        private static void DuplicateVariant(Theme theme, State s, string sourceVariantName)
        {
            Theme.ThemeVariant source = theme.GetVariant(sourceVariantName);
            if (source == null) return;
            string newName = NextDuplicateVariantName(theme, sourceVariantName);

            Undo.RecordObject(theme, "Duplicate variant");
            theme.AddVariant(newName);
            foreach (Theme.TokenColor tc in source.colors)
                theme.SetToken(tc.token, tc.color, newName);
            EditorUtility.SetDirty(theme);

            s.browsedVariant = newName;
        }

        private static string NextDuplicateVariantName(Theme theme, string sourceVariantName)
        {
            for (int i = 2; ; i++)
            {
                string candidate = $"{sourceVariantName} {i}";
                if (theme.GetVariant(candidate) == null) return candidate;
            }
        }

        private static void DrawTokenRow(Theme theme, Theme.ThemeVariant variant, Theme.TokenColor tc)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                Color c = EditorGUILayout.ColorField(
                    new GUIContent(tc.token, $"Color for '{tc.token}' in the '{variant.name}' variant."), tc.color);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(theme, "Edit token");
                    theme.SetToken(tc.token, c, variant.name);
                    EditorUtility.SetDirty(theme);
                }
                // Provenance: a subtle tag when this token's color matches a known bundle's
                // value for the browsed variant (cached — never recomputed per OnGUI).
                string origin = BundleProvenance.For(theme, variant.name, tc.token);
                if (origin != null)
                {
                    Color prevCol = GUI.contentColor;
                    GUI.contentColor = NeoColors.TextSubtle;
                    GUILayout.Label(new GUIContent(origin,
                            $"Matches the '{origin}' bundle's value for the '{variant.name}' variant."),
                        EditorStyles.miniLabel, GUILayout.Width(84f));
                    GUI.contentColor = prevCol;
                }
                if (GUILayout.Button(new GUIContent("✕", "Remove this token from every variant."),
                    GUILayout.Width(22f)))
                {
                    // Tokens are theme-wide by design (B3) — say so before removing everywhere.
                    if (EditorUtility.DisplayDialog("Remove token",
                        $"Remove token '{tc.token}' from all {theme.Variants.Count} variant(s)?\n\n" +
                        "Tokens are theme-wide — this removes it from every variant, not just " +
                        $"'{variant.name}'.", "Remove", "Cancel"))
                    {
                        Undo.RecordObject(theme, "Remove token");
                        theme.RemoveToken(tc.token);
                        EditorUtility.SetDirty(theme);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Cheap, correct provenance lookup for the Colors tab (Phase 2.8): "does this token's current color
    /// match a registered bundle's value for the browsed variant?". Computed ONCE per variant and cached;
    /// the whole cache is dropped whenever the theme changes (<see cref="ThemeService.OnThemeChanged"/> —
    /// which every token/bundle edit fires) or the browsed theme instance switches, so no recomputation
    /// and no <c>AssetDatabase</c> scan happens on a plain repaint. First registered bundle wins on ties.
    /// </summary>
    internal static class BundleProvenance
    {
        private const float Eps = 1f / 512f;

        private static bool _subscribed;
        private static Theme _theme;
        private static readonly Dictionary<string, Dictionary<string, string>> _byVariant =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary> The name of a bundle whose <paramref name="variant"/> palette matches the theme's
        /// current color for <paramref name="token"/>, or null. </summary>
        internal static string For(Theme theme, string variant, string token)
        {
            EnsureSubscribed();
            if (!ReferenceEquals(theme, _theme)) { _theme = theme; _byVariant.Clear(); }
            if (theme == null || string.IsNullOrEmpty(variant) || string.IsNullOrEmpty(token)) return null;
            if (!_byVariant.TryGetValue(variant, out Dictionary<string, string> map))
                _byVariant[variant] = map = Build(theme, variant);
            return map.TryGetValue(token, out string name) ? name : null;
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            ThemeService.OnThemeChanged += _ => _byVariant.Clear();
            _subscribed = true;
        }

        private static Dictionary<string, string> Build(Theme theme, string variant)
        {
            var map = new Dictionary<string, string>();
            Theme.ThemeVariant tv = theme.GetVariant(variant);
            if (tv == null) return map;
            foreach (ThemeBundles.Bundle bundle in ThemeBundleRegistry.All)
            {
                if (bundle.palettes == null) continue;
                (string variant, Dictionary<string, Color> tokens) palette =
                    bundle.palettes.FirstOrDefault(p => p.variant == variant);
                if (palette.tokens == null) continue;
                foreach (Theme.TokenColor tc in tv.colors)
                {
                    if (string.IsNullOrEmpty(tc.token) || map.ContainsKey(tc.token)) continue;
                    if (palette.tokens.TryGetValue(tc.token, out Color col) && Approx(col, tc.color))
                        map[tc.token] = bundle.name;
                }
            }
            return map;
        }

        private static bool Approx(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) <= Eps && Mathf.Abs(a.g - b.g) <= Eps &&
            Mathf.Abs(a.b - b.b) <= Eps && Mathf.Abs(a.a - b.a) <= Eps;
    }
}
