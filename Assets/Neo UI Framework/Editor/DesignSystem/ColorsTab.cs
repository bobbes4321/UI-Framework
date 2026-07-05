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
    /// <see cref="NeoDesignSystemWindow"/> (Phase 2.9) — the draw code is unchanged, including the
    /// Phase-0 fixes: browse-vs-active variant state (B1), theme-wide token-removal confirm (B3) and
    /// per-variant state derivation (via <see cref="NeoDesignSystemWindow.DeriveStates"/>, which stays
    /// on the window so its tests keep their call contract).
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
        }

        internal static object CreateState() => new State();

        internal static void Draw(DesignSystemTabContext ctx)
        {
            Theme theme = ctx.theme;
            var s = ctx.State<State>();

            if (theme.Variants.Count == 0)
            {
                EditorGUILayout.HelpBox("No color variants yet. Run Setup → Create or Repair Starter Kit " +
                    "to seed Dark/Light, or add one below.", MessageType.Info);
            }
            else
            {
                string active = theme.ActiveVariantName;
                if (string.IsNullOrEmpty(active) || theme.GetVariant(active) == null)
                    active = theme.Variants[0].name;

                // Browse state is LOCAL — the dropdown never flips the live active variant (B1).
                if (string.IsNullOrEmpty(s.browsedVariant) || theme.GetVariant(s.browsedVariant) == null)
                    s.browsedVariant = active;

                Rect rect = EditorGUILayout.GetControlRect();
                rect = EditorGUI.PrefixLabel(rect, new GUIContent("Variant"));
                NeoDropdown.ValuePopup(rect, s.browsedVariant, () => theme.Variants.Select(v => v.name).ToList(),
                    chosen => s.browsedVariant = chosen);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(s.browsedVariant == active
                        ? $"Active: {active}"
                        : $"Active: {active}   (browsing {s.browsedVariant})", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(s.browsedVariant == active))
                        if (GUILayout.Button("Set Active", GUILayout.Width(90f)))
                        {
                            Undo.RecordObject(theme, "Set active variant");
                            theme.ActiveVariantName = s.browsedVariant;
                            EditorUtility.SetDirty(theme);
                        }
                }

                Theme.ThemeVariant variant = theme.GetVariant(s.browsedVariant);
                if (variant != null)
                {
                    foreach (Theme.TokenColor tc in variant.colors.ToList())
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUI.BeginChangeCheck();
                            Color c = EditorGUILayout.ColorField(tc.token, tc.color);
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
                            if (GUILayout.Button("✕", GUILayout.Width(22f)))
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
            }

            NeoGUI.Splitter();
            using (new EditorGUILayout.HorizontalScope())
            {
                s.newToken = EditorGUILayout.TextField("New token", s.newToken);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(s.newToken))
                {
                    Undo.RecordObject(theme, "Add token");
                    theme.SetToken(s.newToken.Trim(), Color.gray);
                    EditorUtility.SetDirty(theme);
                    s.newToken = "";
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                s.newVariant = EditorGUILayout.TextField("New variant", s.newVariant);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(s.newVariant))
                {
                    Undo.RecordObject(theme, "Add variant");
                    theme.AddVariant(s.newVariant.Trim());
                    EditorUtility.SetDirty(theme);
                    s.newVariant = "";
                }
            }
            if (GUILayout.Button(new GUIContent("Re-derive hover / pressed states",
                "Recompute Primary/Success/Danger hover+pressed from their base color")))
                NeoDesignSystemWindow.DeriveStates(theme);
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
