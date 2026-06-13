using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Token-grid editor for the document's <see cref="ThemeSpec"/>: pick a starting bundle, add /
    /// rename / remove tokens and edit their default and per-variant colors. Colors are stored as
    /// <c>#RRGGBB</c> hex (what the generator expects). Renaming a token propagates into every
    /// variant override so nothing is orphaned. All edits route through
    /// <see cref="SpecDocument.ApplyEdit"/>.
    /// </summary>
    public static class ThemePaletteEditor
    {
        public static void Draw(SpecDocument document)
        {
            NeoGUI.ComponentHeader("Theme", "Tokens & Variants", NeoColors.Theming);

            ThemeSpec theme = document.Spec.theme;

            Rect bundleRect = EditorGUILayout.GetControlRect();
            bundleRect = EditorGUI.PrefixLabel(bundleRect, new GUIContent("Bundle"));
            NeoDropdown.ValuePopup(bundleRect, theme?.bundle, () => new List<string>(ThemeBundleRegistry.Names),
                v => document.ApplyEdit(() => Ensure(document).bundle = string.IsNullOrEmpty(v) ? null : v, "Edit Bundle"),
                "None");
            EditorGUILayout.HelpBox("A bundle seeds a full token set on Save; tokens below override it.", MessageType.None);

            if (theme == null)
            {
                if (GUILayout.Button("Add Tokens")) document.ApplyEdit(() => Ensure(document), "Add Theme");
                return;
            }

            NeoGUI.Splitter();
            EditorGUILayout.LabelField($"Tokens ({theme.tokens.Count})", EditorStyles.boldLabel);

            string renameFrom = null, renameTo = null, removeToken = null;
            foreach (KeyValuePair<string, string> entry in theme.tokens)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUILayout.DelayedTextField(entry.Key, GUILayout.Width(140f));
                    if (EditorGUI.EndChangeCheck() && newName != entry.Key && !string.IsNullOrWhiteSpace(newName))
                    {
                        renameFrom = entry.Key; renameTo = newName.Trim();
                    }

                    Color current = ParseHex(entry.Value);
                    EditorGUI.BeginChangeCheck();
                    Color picked = EditorGUILayout.ColorField(GUIContent.none, current, true, false, false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        string key = entry.Key, hex = ToHex(picked);
                        document.ApplyEdit(() => theme.tokens[key] = hex, "Edit Token Color");
                    }

                    if (GUILayout.Button("−", GUILayout.Width(24f))) removeToken = entry.Key;
                }
            }

            if (renameFrom != null) RenameToken(document, theme, renameFrom, renameTo);
            else if (removeToken != null)
            {
                string key = removeToken;
                document.ApplyEdit(() =>
                {
                    theme.tokens.Remove(key);
                    foreach (var variant in theme.variants.Values) variant.Remove(key);
                }, "Remove Token");
            }

            if (GUILayout.Button("+ Add Token", GUILayout.Width(110f)))
                document.ApplyEdit(() => theme.tokens[UniqueToken(theme)] = "#FFFFFF", "Add Token");

            DrawVariants(document, theme);
        }

        private static void DrawVariants(SpecDocument document, ThemeSpec theme)
        {
            NeoGUI.Splitter();
            EditorGUILayout.LabelField($"Variants ({theme.variants.Count})", EditorStyles.boldLabel);

            string removeVariant = null;
            foreach (KeyValuePair<string, Dictionary<string, string>> variant in theme.variants)
            {
                if (!NeoGUI.BeginFoldoutSection($"neo.composer.variant.{variant.Key}", variant.Key, $"{variant.Value.Count} overrides"))
                {
                    NeoGUI.EndFoldoutSection();
                    continue;
                }
                foreach (string token in new List<string>(theme.tokens.Keys))
                {
                    bool overridden = variant.Value.TryGetValue(token, out string hex);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        bool keep = EditorGUILayout.ToggleLeft(token, overridden, GUILayout.Width(140f));
                        Color current = overridden ? ParseHex(hex) : ParseHex(theme.tokens[token]);
                        Color picked = EditorGUILayout.ColorField(GUIContent.none, current, true, false, false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            string variantName = variant.Key, tokenName = token, value = ToHex(picked);
                            document.ApplyEdit(() =>
                            {
                                if (keep) theme.variants[variantName][tokenName] = value;
                                else theme.variants[variantName].Remove(tokenName);
                            }, "Edit Variant");
                            return;
                        }
                    }
                }
                if (GUILayout.Button("Remove Variant", GUILayout.Width(120f))) removeVariant = variant.Key;
                NeoGUI.EndFoldoutSection();
            }

            if (removeVariant != null)
            {
                string key = removeVariant;
                document.ApplyEdit(() => theme.variants.Remove(key), "Remove Variant");
                return;
            }

            if (GUILayout.Button("+ Add Variant", GUILayout.Width(110f)))
                document.ApplyEdit(() =>
                {
                    int n = theme.variants.Count + 1;
                    string name = $"Variant{n}";
                    while (theme.variants.ContainsKey(name)) name = $"Variant{++n}";
                    theme.variants[name] = new Dictionary<string, string>();
                }, "Add Variant");
        }

        private static void RenameToken(SpecDocument document, ThemeSpec theme, string from, string to)
        {
            document.ApplyEdit(() =>
            {
                if (!theme.tokens.TryGetValue(from, out string value) || theme.tokens.ContainsKey(to)) return;
                theme.tokens.Remove(from);
                theme.tokens[to] = value;
                foreach (var variant in theme.variants.Values)
                    if (variant.TryGetValue(from, out string v)) { variant.Remove(from); variant[to] = v; }
            }, "Rename Token");
        }

        private static ThemeSpec Ensure(SpecDocument document)
        {
            if (document.Spec.theme == null) document.Spec.theme = new ThemeSpec();
            return document.Spec.theme;
        }

        private static string UniqueToken(ThemeSpec theme)
        {
            int n = theme.tokens.Count + 1;
            string name = $"Token{n}";
            while (theme.tokens.ContainsKey(name)) name = $"Token{++n}";
            return name;
        }

        private static Color ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
        }

        private static string ToHex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);
    }
}
