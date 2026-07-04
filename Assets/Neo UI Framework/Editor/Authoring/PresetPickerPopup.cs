using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// A visual, searchable grid of <see cref="NeoWidgetPreset"/> cards — the Figma-style "pick a
    /// component" popup a preset picker opens, replacing a flat text dropdown (UX goal #2 of the
    /// widget-presets plan). Used by the scene-view overlay's Apply-Preset action (and, while it lives,
    /// the Composer's inspector Preset row). Each card is a cached in-memory render of the preset applied
    /// to its target kind (<see cref="PresetThumbnailCache"/>), so you SEE "Primary Button" look primary
    /// before choosing it. The first card is always "(none)" (unlink). Kind-scoped so only presets that
    /// fit the selected element show.
    /// <para>
    /// Pure IMGUI on the EditorUI palette; cached GUIStyles (built once); thumbnails come from the shared
    /// cache (never rendered per OnGUI). Headless-safe — a null thumbnail falls back to the preset name.
    /// </para>
    /// </summary>
    public sealed class PresetPickerPopup : PopupWindowContent
    {
        private const float CardW = 140f;
        private const float CardThumb = 116f;
        private const float CardLabel = 18f;
        private const float CardH = CardThumb + CardLabel + 6f;
        private const float Gap = 6f;
        private const float SearchH = 22f;
        private const int Columns = 3;

        private readonly string _kind;
        private readonly string _current;          // currently linked preset name (null = none)
        private readonly Action<string> _onSelect; // null arg = unlink

        private readonly List<NeoWidgetPreset> _presets = new List<NeoWidgetPreset>();
        private string _filter = "";
        private Vector2 _scroll;
        private GUIStyle _search, _label, _fallback;

        public PresetPickerPopup(string kind, string current, Action<string> onSelect)
        {
            _kind = kind;
            _current = current;
            _onSelect = onSelect;
            foreach (NeoWidgetPreset p in NeoWidgetPresets.ForKind(kind))
                if (p != null && !string.IsNullOrEmpty(p.presetName)) _presets.Add(p);
            _presets.Sort((a, b) => string.CompareOrdinal(a.presetName, b.presetName));
        }

        public override Vector2 GetWindowSize()
        {
            int cells = _presets.Count + 1; // + the "(none)" card
            int rows = Mathf.Max(1, (cells + Columns - 1) / Columns);
            float width = Columns * CardW + (Columns + 1) * Gap;
            float height = SearchH + rows * (CardH + Gap) + Gap + 6f;
            return new Vector2(width, Mathf.Min(height, 520f));
        }

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();
            var searchRect = new Rect(rect.x + Gap, rect.y + 4f, rect.width - Gap * 2f, SearchH - 6f);
            EditorGUI.BeginChangeCheck();
            string next = EditorGUI.TextField(searchRect, _filter, _search);
            if (EditorGUI.EndChangeCheck()) _filter = next ?? "";

            string needle = string.IsNullOrEmpty(_filter) ? null : _filter.ToLowerInvariant();
            var listRect = new Rect(rect.x, rect.y + SearchH, rect.width, rect.height - SearchH);

            // build the visible cell list: "(none)" first (never filtered out), then matching presets
            var cells = new List<NeoWidgetPreset>(_presets.Count);
            foreach (NeoWidgetPreset p in _presets)
                if (needle == null || p.presetName.ToLowerInvariant().Contains(needle)) cells.Add(p);

            int total = cells.Count + 1;
            int rows = Mathf.Max(1, (total + Columns - 1) / Columns);
            var viewRect = new Rect(0, 0, listRect.width - 16f, rows * (CardH + Gap) + Gap);
            _scroll = GUI.BeginScrollView(listRect, _scroll, viewRect);

            for (int i = 0; i < total; i++)
            {
                int col = i % Columns, row = i / Columns;
                var cardRect = new Rect(Gap + col * (CardW + Gap), Gap + row * (CardH + Gap), CardW, CardH);
                if (i == 0) DrawNoneCard(cardRect);
                else DrawCard(cardRect, cells[i - 1]);
            }
            GUI.EndScrollView();
        }

        private void DrawNoneCard(Rect rect)
        {
            bool selected = string.IsNullOrEmpty(_current);
            DrawCardChrome(rect, NeoColors.TextSubtle, selected);
            var thumbRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, CardThumb - 4f);
            _fallback.Draw(thumbRect, new GUIContent("∅"), false, false, false, false);
            _label.Draw(new Rect(rect.x + 2f, rect.yMax - CardLabel, rect.width - 4f, CardLabel),
                new GUIContent("(none)"), false, false, false, false);
            HandleClick(rect, null);
        }

        private void DrawCard(Rect rect, NeoWidgetPreset preset)
        {
            bool selected = string.Equals(_current, preset.presetName, StringComparison.Ordinal);
            DrawCardChrome(rect, NeoColors.Theming, selected);

            var thumbRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, CardThumb - 4f);
            Texture2D thumb = PresetThumbnailCache.GetOrRender(preset, (int)CardThumb);
            if (thumb != null) GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit, alphaBlend: true);
            else _fallback.Draw(thumbRect, new GUIContent(preset.presetName), false, false, false, false);

            _label.Draw(new Rect(rect.x + 2f, rect.yMax - CardLabel, rect.width - 4f, CardLabel),
                new GUIContent(preset.presetName, preset.description), false, false, false, false);
            HandleClick(rect, preset.presetName);
        }

        private void DrawCardChrome(Rect rect, Color accent, bool selected)
        {
            Event e = Event.current;
            if (e.type != EventType.Repaint) return;
            bool hover = rect.Contains(e.mousePosition);
            EditorGUI.DrawRect(rect, selected ? accent.WithAlpha(0.22f)
                : hover ? NeoColors.RowHover : NeoColors.SectionBackground);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), accent.WithAlpha(selected ? 1f : 0.85f));
            if (selected)
            {
                // a thin selected outline
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), accent);
            }
        }

        private void HandleClick(Rect rect, string presetName)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _onSelect?.Invoke(presetName);
                editorWindow.Close();
                e.Use();
            }
        }

        private void EnsureStyles()
        {
            _search ??= new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
            _label ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 10, clipping = TextClipping.Clip
            };
            _fallback ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = true
            };
        }
    }
}
