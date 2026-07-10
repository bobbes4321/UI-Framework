using System;
using System.Collections.Generic;
using Neo.EditorUI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System "Icons" tab: browse/search every resolvable icon (project overlay entries,
    /// the curated Lucide subset, and — via search — the full ~1960-name Lucide table), inspect a
    /// selection (preview, codepoint/sprite provenance, aliases, copy-name), and extend the set
    /// without touching package code: "Add From Texture" runs the PNG → named-icon pipeline
    /// (<see cref="NeoIconSpriteAssets.AddProjectIcon"/>), "Add Glyph Entry" upserts an
    /// <see cref="IconMapOverlay"/> glyph row with a pick-from-font codepoint helper.
    /// </summary>
    public static class IconsTab
    {
        private const string WidthKey = "NeoUI.DesignSystem.Icons.LeftWidth";
        private const float LeftMinWidth = 280f;
        private const float RightMinWidth = 300f;
        private const int SearchCap = 250;

        private sealed class State
        {
            public string search = "";
            public string selectedName;
            public Vector2 leftScroll, rightScroll;
            public float leftWidth;
            private float _persistedWidth;

            public Texture2D newTexture;
            public string newTextureName = "";
            public bool newTint;
            public string newGlyphName = "";
            public string newGlyphCodepoint = "";

            public void LoadWidth() => leftWidth = _persistedWidth = SessionState.GetFloat(WidthKey, 320f);

            public void PersistWidth()
            {
                if (Mathf.Approximately(leftWidth, _persistedWidth)) return;
                SessionState.SetFloat(WidthKey, leftWidth);
                _persistedWidth = leftWidth;
            }
        }

        public static object CreateState()
        {
            var state = new State();
            state.LoadWidth();
            return state;
        }

        public static void Draw(DesignSystemTabContext ctx)
        {
            var s = ctx.State<State>();
            if (s == null) return;
            using (DesignSystemGUI.BeginSplitPane(ctx.window))
            {
                DesignSystemGUI.BeginSplitLeft(ref s.leftScroll, ref s.leftWidth, LeftMinWidth, RightMinWidth);
                DrawBrowsePane(s, ctx.settings);
                DesignSystemGUI.EndSplitLeft(ref s.leftWidth, LeftMinWidth, RightMinWidth);
                DesignSystemGUI.BeginSplitRight(ref s.rightScroll);
                DrawDetailPane(s, ctx.settings);
                DesignSystemGUI.EndSplitRight();
            }
            s.PersistWidth();
        }

        // ------------------------------------------------------------------ browse (left)

        private static void DrawBrowsePane(State s, NeoUISettings settings)
        {
            DesignSystemCatalog.SearchField(ref s.search);
            bool searching = !string.IsNullOrEmpty(s.search);
            string needle = searching ? s.search.ToLowerInvariant() : null;

            // featured (overlay + curated) always browsable; the full table joins in via search
            IEnumerable<string> source = searching ? IconMap.Names : IconMap.FeaturedNames;
            var visible = new List<string>(256);
            int hidden = 0;
            foreach (string name in source)
            {
                if (needle != null && !name.Contains(needle)) continue;
                if (visible.Count >= SearchCap) { hidden++; continue; }
                visible.Add(name);
            }
            // an alias query ("home") surfaces its canonical even though the name doesn't match
            if (needle != null && IconMap.TryResolveIcon(s.search, out ResolvedIcon aliased)
                && !visible.Contains(aliased.name))
                visible.Insert(0, aliased.name);

            foreach (string name in visible)
            {
                string captured = name;
                if (DesignSystemCatalog.Row(name, name == s.selectedName,
                        drawAccessory: r => NeoIconPickerPopup.DrawIconPreview(r, captured, 14),
                        trailingBadge: Badge(settings, name)))
                    s.selectedName = name;
            }
            if (hidden > 0)
                EditorGUILayout.LabelField($"…and {hidden} more — refine the search", EditorStyles.centeredGreyMiniLabel);
            if (!searching)
                EditorGUILayout.LabelField("Search to reach the full Lucide set (~1960 icons)",
                    EditorStyles.centeredGreyMiniLabel);

            NeoGUI.Splitter();
            DrawAddFromTexture(s, settings);
            NeoGUI.Splitter();
            DrawAddGlyphEntry(s, settings);
        }

        private static void DrawAddFromTexture(State s, NeoUISettings settings)
        {
            EditorGUILayout.LabelField("Add From Texture", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            s.newTexture = (Texture2D)EditorGUILayout.ObjectField("Texture", s.newTexture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck() && s.newTexture != null && string.IsNullOrEmpty(s.newTextureName))
                s.newTextureName = Slug(s.newTexture.name);
            s.newTextureName = EditorGUILayout.TextField("Icon Name", s.newTextureName);
            s.newTint = EditorGUILayout.Toggle(new GUIContent("Tint With Label Color",
                "On for white/alpha art that should follow the theme; off renders color art as-is"), s.newTint);
            using (new EditorGUI.DisabledScope(s.newTexture == null || string.IsNullOrEmpty(Slug(s.newTextureName))))
            {
                if (GUILayout.Button("Add Icon"))
                {
                    IconMapOverlay.SpriteEntry entry = NeoIconSpriteAssets.AddProjectIcon(
                        s.newTexture, Slug(s.newTextureName), s.newTint);
                    if (entry != null)
                    {
                        s.selectedName = entry.name;
                        s.newTexture = null;
                        s.newTextureName = "";
                    }
                }
            }
        }

        private static void DrawAddGlyphEntry(State s, NeoUISettings settings)
        {
            EditorGUILayout.LabelField("Add Glyph Entry", EditorStyles.boldLabel);
            s.newGlyphName = EditorGUILayout.TextField("Icon Name", s.newGlyphName);
            using (new EditorGUILayout.HorizontalScope())
            {
                s.newGlyphCodepoint = EditorGUILayout.TextField(
                    new GUIContent("Codepoint", "Hex, e.g. F101 — a char in the icon font (or a fallback)"),
                    s.newGlyphCodepoint);
                TMP_FontAsset iconFont = settings != null ? settings.iconFont : null;
                using (new EditorGUI.DisabledScope(iconFont == null))
                {
                    if (GUILayout.Button("Pick…", EditorStyles.miniButton, GUILayout.Width(48f)))
                    {
                        Rect activator = GUILayoutUtility.GetLastRect();
                        PopupWindow.Show(activator, new FontGlyphPickerPopup(iconFont,
                            unicode => s.newGlyphCodepoint = ((int)unicode).ToString("X4")));
                    }
                }
            }
            bool valid = !string.IsNullOrEmpty(Slug(s.newGlyphName)) && TryParseHex(s.newGlyphCodepoint, out _);
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Add Glyph"))
                {
                    IconMapOverlay overlay = NeoIconSpriteAssets.EnsureOverlay(settings);
                    if (overlay != null)
                    {
                        string name = Slug(s.newGlyphName);
                        Undo.RecordObject(overlay, "Add Icon Glyph");
                        IconMapOverlay.Entry entry = overlay.glyphs.Find(
                            e => e != null && string.Equals(e.name, name, StringComparison.Ordinal));
                        if (entry == null)
                        {
                            entry = new IconMapOverlay.Entry { name = name };
                            overlay.glyphs.Add(entry);
                        }
                        entry.codepoint = s.newGlyphCodepoint.Trim();
                        EditorUtility.SetDirty(overlay);
                        AssetDatabase.SaveAssets();
                        s.selectedName = name;
                        s.newGlyphName = "";
                        s.newGlyphCodepoint = "";
                    }
                }
            }
        }

        // ------------------------------------------------------------------ detail (right)

        private static void DrawDetailPane(State s, NeoUISettings settings)
        {
            if (string.IsNullOrEmpty(s.selectedName) ||
                !IconMap.TryResolveIcon(s.selectedName, out ResolvedIcon resolved))
            {
                DesignSystemCatalog.EmptyState("Select an icon on the left to inspect it —\n" +
                                               "or add your own from a texture or glyph below the list.");
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect preview = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(72f));
                EditorGUI.DrawRect(preview, NeoColors.SectionBackground);
                NeoIconPickerPopup.DrawIconPreview(preview, resolved.name, 48);
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(resolved.name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(Provenance(settings, resolved), EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Copy Name", EditorStyles.miniButton, GUILayout.Width(90f)))
                            EditorGUIUtility.systemCopyBuffer = resolved.name;
                        if (resolved.isSprite && resolved.spriteAsset != null &&
                            GUILayout.Button("Ping Asset", EditorStyles.miniButton, GUILayout.Width(90f)))
                            EditorGUIUtility.PingObject(resolved.spriteAsset);
                    }
                }
            }
            GUILayout.Space(NeoGUI.Spacing);
            EditorGUILayout.LabelField($"Spec usage: \"icon\": \"{resolved.name}\" (icon elements, button/tab icon slots)",
                EditorStyles.miniLabel);

            if (!resolved.isSprite)
            {
                EditorGUILayout.LabelField($"Codepoint: U+{(int)resolved.glyph:X4}", EditorStyles.miniLabel);
                string aliases = AliasesFor(settings, resolved.name);
                if (!string.IsNullOrEmpty(aliases))
                    EditorGUILayout.LabelField($"Aliases: {aliases}", EditorStyles.miniLabel);
            }

            IconMapOverlay overlay = settings != null ? settings.iconOverlay : null;
            if (overlay == null) return;

            // overlay-owned entries get in-place editing + delete
            IconMapOverlay.SpriteEntry spriteEntry = overlay.sprites?.Find(
                e => e != null && string.Equals(e.name, resolved.name, StringComparison.Ordinal));
            if (spriteEntry != null)
            {
                NeoGUI.Splitter();
                EditorGUILayout.LabelField("Overlay Sprite Entry", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var newAsset = (TMP_SpriteAsset)EditorGUILayout.ObjectField("Sprite Asset",
                    spriteEntry.spriteAsset, typeof(TMP_SpriteAsset), false);
                string newSpriteName = EditorGUILayout.TextField("Sprite Name", spriteEntry.spriteName);
                bool newTint = EditorGUILayout.Toggle("Tint With Label Color", spriteEntry.tint);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(overlay, "Edit Icon Sprite Entry");
                    spriteEntry.spriteAsset = newAsset;
                    spriteEntry.spriteName = newSpriteName;
                    spriteEntry.tint = newTint;
                    EditorUtility.SetDirty(overlay);
                }
                DrawDeleteEntry(s, overlay, () => overlay.sprites.Remove(spriteEntry));
                return;
            }

            IconMapOverlay.Entry glyphEntry = overlay.glyphs?.Find(
                e => e != null && string.Equals(e.name, resolved.name, StringComparison.Ordinal));
            if (glyphEntry != null)
            {
                NeoGUI.Splitter();
                EditorGUILayout.LabelField("Overlay Glyph Entry", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                string newCodepoint = EditorGUILayout.TextField("Codepoint (hex)", glyphEntry.codepoint);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(overlay, "Edit Icon Glyph Entry");
                    glyphEntry.codepoint = newCodepoint;
                    EditorUtility.SetDirty(overlay);
                }
                DrawDeleteEntry(s, overlay, () => overlay.glyphs.Remove(glyphEntry));
            }
        }

        private static void DrawDeleteEntry(State s, IconMapOverlay overlay, Action remove)
        {
            GUILayout.Space(NeoGUI.Spacing);
            if (!GUILayout.Button("Delete Entry", GUILayout.Width(110f))) return;
            if (!EditorUtility.DisplayDialog("Delete icon entry",
                    $"Remove '{s.selectedName}' from the icon overlay? Widgets using it keep their " +
                    "baked visual but the name stops resolving.", "Delete", "Cancel")) return;
            Undo.RecordObject(overlay, "Delete Icon Entry");
            remove();
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssets();
            s.selectedName = null;
        }

        // ------------------------------------------------------------------ helpers

        private static string Badge(NeoUISettings settings, string name)
        {
            IconMapOverlay overlay = settings != null ? settings.iconOverlay : null;
            if (overlay != null && overlay.TryGetSprite(name, out _)) return "sprite";
            if (overlay != null && overlay.TryGetGlyph(name, out _)) return "overlay";
            return IconLibrary.IsCurated(name) ? null : "full set";
        }

        private static string Provenance(NeoUISettings settings, in ResolvedIcon resolved)
        {
            if (resolved.isSprite) return "Project sprite icon (IconMapOverlay)";
            IconMapOverlay overlay = settings != null ? settings.iconOverlay : null;
            if (overlay != null && overlay.TryGetGlyph(resolved.name, out _)) return "Project glyph (IconMapOverlay)";
            return IconLibrary.IsCurated(resolved.name)
                ? "Lucide 1.17.0 — curated subset (pre-baked)"
                : "Lucide 1.17.0 — full set (glyph bakes into the atlas on first use)";
        }

        private static string AliasesFor(NeoUISettings settings, string canonical)
        {
            var found = new List<string>();
            foreach (KeyValuePair<string, string> alias in IconLibrary.BuiltInAliases)
                if (string.Equals(alias.Value, canonical, StringComparison.Ordinal)) found.Add(alias.Key);
            IconMapOverlay overlay = settings != null ? settings.iconOverlay : null;
            if (overlay?.aliases != null)
                foreach (IconMapOverlay.Alias alias in overlay.aliases)
                    if (alias != null && string.Equals(alias.target, canonical, StringComparison.Ordinal))
                        found.Add(alias.alias);
            return found.Count == 0 ? null : string.Join(", ", found);
        }

        private static string Slug(string raw) =>
            string.IsNullOrEmpty(raw) ? "" : raw.Trim().ToLowerInvariant().Replace(' ', '-');

        private static bool TryParseHex(string raw, out int code)
        {
            code = 0;
            if (string.IsNullOrEmpty(raw)) return false;
            string hex = raw.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ? raw.Substring(2) : raw;
            return int.TryParse(hex.Trim(), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out code) && code > 0 && code <= char.MaxValue;
        }
    }

    /// <summary>
    /// Grid popup over a TMP font asset's baked character table — click a glyph to take its
    /// codepoint (the Icons tab's "Pick…" helper, so overlay glyph entries never mean hand-typing
    /// hex). Renders through the font's source ttf when available, hex labels otherwise.
    /// </summary>
    public sealed class FontGlyphPickerPopup : PopupWindowContent
    {
        private const float Tile = 34f;
        private const float Gap = 4f;
        private const int Columns = 9;

        private readonly List<uint> _unicodes = new List<uint>();
        private readonly Action<uint> _onPick;
        private readonly Font _renderFont;
        private Vector2 _scroll;
        private GUIStyle _glyph, _hex;

        public FontGlyphPickerPopup(TMP_FontAsset font, Action<uint> onPick)
        {
            _onPick = onPick;
            if (font != null)
            {
                foreach (TMP_Character character in font.characterTable)
                    if (character != null) _unicodes.Add(character.unicode);
                _renderFont = font.sourceFontFile;
            }
            _unicodes.Sort();
        }

        public override Vector2 GetWindowSize() =>
            new Vector2(Columns * (Tile + Gap) + Gap + 16f, 360f);

        public override void OnGUI(Rect rect)
        {
            _glyph ??= new GUIStyle(GUI.skin.label)
                { font = _renderFont, fontSize = 18, alignment = TextAnchor.MiddleCenter };
            _hex ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 8 };

            int rows = Mathf.Max(1, (_unicodes.Count + Columns - 1) / Columns);
            var viewRect = new Rect(0, 0, rect.width - 16f, rows * (Tile + Gap) + Gap);
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);
            for (int i = 0; i < _unicodes.Count; i++)
            {
                var tile = new Rect(Gap + i % Columns * (Tile + Gap), Gap + i / Columns * (Tile + Gap), Tile, Tile);
                if (Event.current.type == EventType.Repaint)
                {
                    bool hover = tile.Contains(Event.current.mousePosition);
                    EditorGUI.DrawRect(tile, hover ? NeoColors.RowHover : NeoColors.SectionBackground);
                    string hex = ((int)_unicodes[i]).ToString("X4");
                    if (_renderFont != null && _unicodes[i] <= char.MaxValue &&
                        _renderFont.HasCharacter((char)_unicodes[i]))
                        _glyph.Draw(tile, new GUIContent(((char)_unicodes[i]).ToString(), "U+" + hex),
                            false, false, false, false);
                    else
                        _hex.Draw(tile, new GUIContent(hex), false, false, false, false);
                }
                if (Event.current.type == EventType.MouseDown && tile.Contains(Event.current.mousePosition))
                {
                    _onPick?.Invoke(_unicodes[i]);
                    editorWindow.Close();
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
        }
    }
}
