using System;
using System.Collections.Generic;
using System.Text;
using Neo.EditorUI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Inspector for <see cref="NeoIcon"/>: a live glyph preview + a searchable glyph-grid picker
    /// over the packaged Lucide set (plus any project <see cref="IconMapOverlay"/> entries), so the
    /// icon library is browsable instead of a tofu square in a TMP text field. The raw name field
    /// stays editable underneath (agent-first); hand-edits re-bake through NeoIcon.OnValidate.
    /// </summary>
    [CustomEditor(typeof(NeoIcon)), CanEditMultipleObjects]
    public class NeoIconEditor : NeoUIEditor
    {
        private const float PreviewSize = 40f;

        protected override string HeaderTitle => "NeoIcon";
        protected override string HeaderSubtitle => ((NeoIcon)target).icon;
        protected override Color Accent => NeoColors.Rendering;

        protected override void DrawBody()
        {
            SerializedProperty iconProperty = serializedObject.FindProperty("icon");
            bool mixed = iconProperty.hasMultipleDifferentValues;
            string current = mixed ? null : iconProperty.stringValue;

            Rect row = EditorGUILayout.GetControlRect(false, PreviewSize + 4f);
            var previewRect = new Rect(row.x, row.y + 2f, PreviewSize, PreviewSize);
            EditorGUI.DrawRect(previewRect, NeoColors.SectionBackground);
            bool known = !mixed && !string.IsNullOrEmpty(current) && IconMap.TryResolveIcon(current, out _);
            if (Event.current.type == EventType.Repaint)
            {
                if (known) NeoIconPickerPopup.DrawIconPreview(previewRect, current, 28);
                else EditorGUI.LabelField(previewRect, mixed ? "—" : "∅", NeoIconPickerPopup.FallbackStyle());
            }

            var nameRect = new Rect(previewRect.xMax + 8f, row.y + 4f, row.width - PreviewSize - 120f, 18f);
            EditorGUI.LabelField(nameRect, mixed ? "(mixed)" : string.IsNullOrEmpty(current) ? "(no icon)" : current,
                EditorStyles.boldLabel);

            var buttonRect = new Rect(row.xMax - 104f, row.y + (row.height - 22f) * 0.5f, 104f, 22f);
            if (GUI.Button(buttonRect, "Choose Icon…", EditorStyles.miniButton))
                PopupWindow.Show(buttonRect, new NeoIconPickerPopup(current, ApplyIcon));

            EditorGUILayout.PropertyField(iconProperty);
            if (!mixed && !string.IsNullOrEmpty(current) && !known)
                EditorGUILayout.HelpBox($"Unknown icon '{current}' — not in the Lucide set or the " +
                                        "project's IconMapOverlay.", MessageType.Warning);
        }

        /// <summary> Applies a picked name (null = clear) to every selected NeoIcon: stamps the
        /// canonical name and bakes the glyph + icon font into the sibling TMP, one undo step. </summary>
        private void ApplyIcon(string pickedName)
        {
            foreach (UnityEngine.Object o in targets)
            {
                var neoIcon = (NeoIcon)o;
                var text = neoIcon.GetComponent<TMP_Text>();
                Undo.RecordObjects(text != null
                    ? new UnityEngine.Object[] { neoIcon, text }
                    : new UnityEngine.Object[] { neoIcon }, "Set Icon");
                if (string.IsNullOrEmpty(pickedName))
                {
                    neoIcon.icon = null;
                    if (text != null) text.text = string.Empty;
                }
                else if (IconMap.TryResolveIcon(pickedName, out ResolvedIcon resolved))
                {
                    neoIcon.icon = resolved.name;
                    if (text != null)
                    {
                        if (resolved.isSprite)
                        {
                            if (text.spriteAsset != resolved.spriteAsset) text.spriteAsset = resolved.spriteAsset;
                            text.richText = true;
                        }
                        else
                        {
                            TMP_FontAsset iconFont = FontAssetBootstrap.EnsureIconFont(NeoUISettings.instance);
                            if (iconFont != null)
                            {
                                if (text.font != iconFont) text.font = iconFont;
                                if (!iconFont.HasCharacter(resolved.glyph))
                                    iconFont.TryAddCharacters(resolved.glyph.ToString());
                            }
                        }
                        text.text = resolved.BakedText;
                    }
                }
                EditorUtility.SetDirty(neoIcon);
                if (text != null) EditorUtility.SetDirty(text);
            }
            serializedObject.Update();
            Repaint();
        }
    }

    /// <summary>
    /// Searchable glyph-grid popup over <see cref="IconMap.Names"/> — the icon sibling of
    /// <c>PresetPickerPopup</c>. Tiles render the REAL glyphs through the committed Lucide.ttf
    /// (an IMGUI dynamic font — overlay glyphs the ttf lacks fall back to their name); search also
    /// matches the forgiving aliases ("home" finds "house"). Public so other editors (button/tab
    /// icon rows) can reuse it. Selecting invokes <c>onSelect</c> (null = clear) and closes.
    /// </summary>
    public sealed class NeoIconPickerPopup : PopupWindowContent
    {
        private const float Tile = 40f;
        private const float Gap = 4f;
        private const int Columns = 8;
        private const float SearchH = 24f;
        private const float FooterH = 18f;

        private readonly string _current;
        private readonly Action<string> _onSelect; // null arg = clear
        private readonly List<string> _names = new List<string>();
        private readonly Dictionary<string, string> _searchBlobs = new Dictionary<string, string>();

        private string _filter = "";
        private string _hovered;
        private Vector2 _scroll;
        private bool _focused;

        private static Font s_lucideTtf;
        private static GUIStyle s_glyph, s_fallback, s_search, s_footer;

        public NeoIconPickerPopup(string current, Action<string> onSelect)
        {
            _current = current;
            _onSelect = onSelect;
            foreach (string name in IconMap.Names) _names.Add(name);

            // fold aliases into search so agent vocabulary ("home", "delete") finds the canonical tile
            var aliasBlobs = new Dictionary<string, StringBuilder>();
            foreach (KeyValuePair<string, string> alias in IconLibrary.BuiltInAliases)
            {
                if (!aliasBlobs.TryGetValue(alias.Value, out StringBuilder blob))
                    aliasBlobs[alias.Value] = blob = new StringBuilder();
                blob.Append(' ').Append(alias.Key);
            }
            foreach (string name in _names)
                _searchBlobs[name] = aliasBlobs.TryGetValue(name, out StringBuilder blob)
                    ? name + blob : name;
        }

        public override void OnOpen() => editorWindow.wantsMouseMove = true;

        public override Vector2 GetWindowSize()
        {
            float width = Columns * (Tile + Gap) + Gap + 16f;
            return new Vector2(width, 420f);
        }

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();
            var searchRect = new Rect(rect.x + Gap, rect.y + 4f, rect.width - Gap * 2f, SearchH - 6f);
            GUI.SetNextControlName("neo-icon-search");
            _filter = GUI.TextField(searchRect, _filter ?? "", s_search);
            if (!_focused && Event.current.type == EventType.Layout)
            {
                GUI.FocusControl("neo-icon-search");
                _focused = true;
            }

            string needle = string.IsNullOrEmpty(_filter) ? null : _filter.ToLowerInvariant();
            var visible = new List<string>(_names.Count);
            foreach (string name in _names)
                if (needle == null || _searchBlobs[name].Contains(needle)) visible.Add(name);

            int total = visible.Count + 1; // + the clear tile
            int rows = Mathf.Max(1, (total + Columns - 1) / Columns);
            var listRect = new Rect(rect.x, rect.y + SearchH, rect.width, rect.height - SearchH - FooterH);
            var viewRect = new Rect(0, 0, listRect.width - 16f, rows * (Tile + Gap) + Gap);
            _hovered = null;
            _scroll = GUI.BeginScrollView(listRect, _scroll, viewRect);
            for (int i = 0; i < total; i++)
            {
                int col = i % Columns, row = i / Columns;
                var tile = new Rect(Gap + col * (Tile + Gap), Gap + row * (Tile + Gap), Tile, Tile);
                if (i == 0) DrawClearTile(tile);
                else DrawTile(tile, visible[i - 1]);
            }
            GUI.EndScrollView();

            var footerRect = new Rect(rect.x + Gap, rect.yMax - FooterH, rect.width - Gap * 2f, FooterH);
            GUI.Label(footerRect, _hovered ?? (needle == null
                ? $"{visible.Count} icons"
                : $"{visible.Count} match \"{_filter}\""), s_footer);

            if (Event.current.type == EventType.MouseMove) editorWindow.Repaint();
        }

        private void DrawClearTile(Rect tile)
        {
            DrawChrome(tile, string.IsNullOrEmpty(_current));
            if (Event.current.type == EventType.Repaint)
                s_fallback.Draw(tile, new GUIContent("∅", "No icon"), false, false, false, false);
            if (tile.Contains(Event.current.mousePosition)) _hovered = "(none)";
            HandleClick(tile, null);
        }

        private void DrawTile(Rect tile, string name)
        {
            DrawChrome(tile, string.Equals(_current, name, StringComparison.Ordinal));
            DrawIconPreview(tile, name, 20);
            if (tile.Contains(Event.current.mousePosition)) _hovered = name;
            HandleClick(tile, name);
        }

        /// <summary>
        /// Draws any icon's preview into <paramref name="rect"/>: font glyphs render through the
        /// committed Lucide.ttf (an IMGUI dynamic font), sprite icons draw their sprite-sheet rect,
        /// anything unrenderable falls back to its name. Repaint-only. Shared by the NeoIcon
        /// inspector and the Design System Icons tab.
        /// </summary>
        public static void DrawIconPreview(Rect rect, string iconName, int glyphFontSize)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            if (!IconMap.TryResolveIcon(iconName, out ResolvedIcon resolved))
            {
                s_fallback.Draw(rect, new GUIContent(iconName), false, false, false, false);
                return;
            }
            if (resolved.isSprite)
            {
                if (DrawSpritePreview(rect, resolved)) return;
                s_fallback.Draw(rect, new GUIContent(resolved.name), false, false, false, false);
                return;
            }
            Font ttf = LucideTtf();
            if (ttf != null && ttf.HasCharacter(resolved.glyph))
            {
                s_glyph.fontSize = glyphFontSize;
                s_glyph.Draw(rect, new GUIContent(resolved.glyph.ToString()), false, false, false, false);
            }
            else
            {
                // overlay glyphs may not exist in the committed Lucide.ttf — show the name, not tofu
                s_fallback.Draw(rect, new GUIContent(resolved.name), false, false, false, false);
            }
        }

        private static bool DrawSpritePreview(Rect rect, in ResolvedIcon resolved)
        {
            TMP_SpriteAsset asset = resolved.spriteAsset;
            Texture sheet = asset != null ? asset.spriteSheet : null;
            if (asset == null || sheet == null) return false;
            int characterIndex = asset.GetSpriteIndexFromName(resolved.spriteName);
            if (characterIndex < 0 || characterIndex >= asset.spriteCharacterTable.Count) return false;
            uint glyphIndex = asset.spriteCharacterTable[characterIndex].glyphIndex;
            TMP_SpriteGlyph glyph = asset.spriteGlyphTable.Find(g => g.index == glyphIndex);
            if (glyph == null) return false;
            UnityEngine.TextCore.GlyphRect glyphRect = glyph.glyphRect;
            if (glyphRect.width <= 0 || glyphRect.height <= 0) return false;
            var uv = new Rect((float)glyphRect.x / sheet.width, (float)glyphRect.y / sheet.height,
                (float)glyphRect.width / sheet.width, (float)glyphRect.height / sheet.height);
            float scale = Mathf.Min(rect.width / glyphRect.width, rect.height / glyphRect.height);
            float w = glyphRect.width * scale, h = glyphRect.height * scale;
            var dst = new Rect(rect.x + (rect.width - w) * 0.5f, rect.y + (rect.height - h) * 0.5f, w, h);
            GUI.DrawTextureWithTexCoords(dst, sheet, uv, true);
            return true;
        }

        private void DrawChrome(Rect tile, bool selected)
        {
            if (Event.current.type != EventType.Repaint) return;
            bool hover = tile.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(tile, selected ? NeoColors.Rendering.WithAlpha(0.28f)
                : hover ? NeoColors.RowHover : NeoColors.SectionBackground);
        }

        private void HandleClick(Rect tile, string name)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && tile.Contains(e.mousePosition))
            {
                _onSelect?.Invoke(name);
                editorWindow.Close();
                e.Use();
            }
        }

        /// <summary> Shared Lucide-ttf glyph style (dynamic font renders real icons in IMGUI). </summary>
        public static GUIStyle GlyphStyle(int fontSize)
        {
            EnsureStyles();
            s_glyph.fontSize = fontSize;
            return s_glyph;
        }

        public static GUIStyle FallbackStyle()
        {
            EnsureStyles();
            return s_fallback;
        }

        private static Font LucideTtf()
        {
            if (s_lucideTtf == null)
                s_lucideTtf = AssetDatabase.LoadAssetAtPath<Font>(FontAssetBootstrap.FontsFolder + "/Lucide.ttf");
            return s_lucideTtf;
        }

        private static void EnsureStyles()
        {
            if (s_glyph != null) return;
            s_glyph = new GUIStyle(GUI.skin.label)
            {
                font = LucideTtf(), fontSize = 20, alignment = TextAnchor.MiddleCenter
            };
            s_fallback = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 8, wordWrap = true, clipping = TextClipping.Clip
            };
            s_search = new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
            s_footer = new GUIStyle(EditorStyles.miniLabel);
        }
    }
}
