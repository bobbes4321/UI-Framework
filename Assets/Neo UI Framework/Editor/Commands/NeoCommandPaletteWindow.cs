using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The Ctrl-K command palette: a search-as-you-type list of every visible <see cref="NeoCommands"/>
    /// entry, grouped by category, with keyboard navigation (Up/Down/Enter/Escape) and mouse click.
    /// A command that needs an argument (e.g. "Connect selected button to view…") swaps the window into
    /// a second "argument" page listing the options via <see cref="PushPage"/> — Escape on that page
    /// returns to the root command list rather than closing the window.
    /// <para>
    /// Opened via <c>Tools → Neo UI → Command Palette</c> (Ctrl/Cmd-K). Row lists are rebuilt only when
    /// the search text, the page, or the editor selection changes — never per repaint.
    /// </para>
    /// </summary>
    public sealed class NeoCommandPaletteWindow : EditorWindow
    {
        /// <summary> One selectable row in either palette page — a root command or an argument option
        /// (e.g. a candidate view to connect/navigate to). </summary>
        public readonly struct PaletteRow
        {
            public readonly string label;
            public readonly string sublabel;
            public readonly string category;
            public readonly Action onSelect;
            public readonly string[] keywords;

            public PaletteRow(string label, Action onSelect, string category = null, string sublabel = null,
                string[] keywords = null)
            {
                this.label = label;
                this.onSelect = onSelect;
                this.category = category;
                this.sublabel = sublabel;
                this.keywords = keywords;
            }
        }

        private enum Page { Root, Argument }

        private const float WindowWidth = 560f;
        private const float WindowHeight = 420f;
        private const float SearchHeight = 28f;
        private const float HeaderBarHeight = 22f;
        private const float RowHeight = 22f;
        private const float SectionHeaderHeight = 18f;
        private const string SearchControlName = "NeoCommandPaletteSearch";

        private static NeoCommandPaletteWindow _instance;

        private Page _page = Page.Root;
        private string _pageTitle;
        private List<PaletteRow> _rootRowsCache;
        private IReadOnlyList<PaletteRow> _argumentRows;

        private string _search = "";
        private bool _dirty = true;
        private bool _focusPending = true;
        private Vector2 _scroll;
        private int _highlight;

        // Rebuilt only when _dirty (search/page changed) — never allocated per-OnGUI.
        private readonly List<PaletteRow> _filteredRows = new List<PaletteRow>();
        private readonly List<float> _rowY = new List<float>();
        private readonly List<(float y, string text)> _headers = new List<(float, string)>();
        private float _contentHeight;

        [MenuItem("Tools/Neo UI/Command Palette %k", priority = 1)]
        public static void Open()
        {
            var window = CreateInstance<NeoCommandPaletteWindow>();
            window.titleContent = new GUIContent("Neo Command Palette");
            window.InitRoot();

            Rect parent = focusedWindow != null ? focusedWindow.position : new Rect(100f, 100f, 1280f, 800f);
            var size = new Vector2(WindowWidth, WindowHeight);
            window.position = new Rect(
                parent.x + (parent.width - size.x) * 0.5f,
                parent.y + (parent.height - size.y) * 0.5f,
                size.x, size.y);
            window.minSize = size;
            window.maxSize = size;
            window.ShowUtility();
            window.Focus();
        }

        /// <summary>
        /// Swaps the open palette into a follow-up argument page listing <paramref name="rows"/> (e.g.
        /// candidate views to connect/navigate to) — the mechanism a root command uses to collect one
        /// more pick before it runs. Escape returns to the root command list. No-op if no palette is open.
        /// </summary>
        public static void PushPage(string title, IReadOnlyList<PaletteRow> rows)
        {
            if (_instance == null) return;
            _instance._page = Page.Argument;
            _instance._pageTitle = title;
            _instance._argumentRows = rows ?? Array.Empty<PaletteRow>();
            _instance._search = "";
            _instance._highlight = 0;
            _instance._scroll = Vector2.zero;
            _instance._dirty = true;
            _instance._focusPending = true;
            _instance.Repaint();
        }

        private void OnEnable()
        {
            _instance = this;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            if (_instance == this) _instance = null;
        }

        private void OnSelectionChanged()
        {
            if (_page != Page.Root) return; // an in-progress argument pick shouldn't be yanked away
            _rootRowsCache = BuildRootRows();
            _dirty = true;
            Repaint();
        }

        private void InitRoot()
        {
            _page = Page.Root;
            _pageTitle = null;
            _rootRowsCache = BuildRootRows();
            _search = "";
            _highlight = 0;
            _scroll = Vector2.zero;
            _dirty = true;
            _focusPending = true;
        }

        private static List<PaletteRow> BuildRootRows()
        {
            IReadOnlyList<NeoCommandDescriptor> commands = NeoCommands.All;
            var list = new List<PaletteRow>(commands.Count);
            foreach (NeoCommandDescriptor d in commands)
            {
                if (d.visible != null && !d.visible()) continue;
                NeoCommandDescriptor captured = d;
                list.Add(new PaletteRow(d.label, () => captured.run?.Invoke(), category: d.category, keywords: d.searchKeywords));
            }
            return list;
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            if (_dirty) RebuildFilteredRows();
            HandleKeyboard(); // before the text field, so Up/Down/Enter/Escape never fall into it

            float y = 0f;
            if (_page == Page.Argument)
            {
                DrawArgumentHeader(new Rect(0f, y, position.width, HeaderBarHeight));
                y += HeaderBarHeight;
            }

            DrawSearch(new Rect(8f, y + 4f, position.width - 16f, 20f));
            y += SearchHeight;

            DrawList(new Rect(0f, y, position.width, position.height - y));
        }

        private void DrawArgumentHeader(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, NeoColors.HeaderBackground);
            var titleRect = new Rect(rect.x + 8f, rect.y, rect.width - 100f, rect.height);
            GUI.Label(titleRect, _pageTitle, NeoStyles.HeaderTitle);
            var hintRect = new Rect(rect.xMax - 92f, rect.y, 88f, rect.height);
            GUI.Label(hintRect, "Esc: back", NeoStyles.HeaderSubtitle);
        }

        private void DrawSearch(Rect rect)
        {
            GUI.SetNextControlName(SearchControlName);
            EditorGUI.BeginChangeCheck();
            _search = EditorGUI.TextField(rect, _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                _highlight = 0;
                _dirty = true;
            }
            if (_focusPending)
            {
                EditorGUI.FocusTextInControl(SearchControlName);
                _focusPending = false;
            }
        }

        private void DrawList(Rect rect)
        {
            var viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(_contentHeight, rect.height));
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);

            foreach ((float y, string text) header in _headers)
                GUI.Label(new Rect(8f, header.y + 2f, viewRect.width - 16f, SectionHeaderHeight - 2f),
                    header.text, NeoStyles.SectionTitle);

            for (int i = 0; i < _filteredRows.Count; i++)
                DrawRow(new Rect(0f, _rowY[i], viewRect.width, RowHeight), i, _filteredRows[i]);

            GUI.EndScrollView();

            if (_filteredRows.Count == 0)
                GUI.Label(rect, "no matching commands", NeoStyles.PopupSearchHint);
        }

        private void DrawRow(Rect rowRect, int index, PaletteRow row)
        {
            Event current = Event.current;
            bool hovered = rowRect.Contains(current.mousePosition);
            if (hovered && (current.type == EventType.MouseMove || current.type == EventType.MouseDrag))
                _highlight = index;

            if (current.type == EventType.Repaint)
            {
                if (index == _highlight)
                    EditorGUI.DrawRect(rowRect, NeoColors.RowSelected);
                else if (hovered)
                    EditorGUI.DrawRect(rowRect, NeoColors.RowHover);

                bool hasSublabel = !string.IsNullOrEmpty(row.sublabel);
                var labelRect = hasSublabel
                    ? new Rect(rowRect.x + 10f, rowRect.y, rowRect.width * 0.55f, rowRect.height)
                    : new Rect(rowRect.x + 10f, rowRect.y, rowRect.width - 20f, rowRect.height);
                NeoStyles.PopupItem.Draw(labelRect, row.label, isHover: hovered, isActive: false, on: false, hasKeyboardFocus: false);

                if (hasSublabel)
                {
                    var subRect = new Rect(labelRect.xMax, rowRect.y, rowRect.width - labelRect.width - 12f, rowRect.height);
                    GUI.Label(subRect, row.sublabel, NeoStyles.MiniDim);
                }
            }

            if (current.type == EventType.MouseDown && hovered)
            {
                CommitRow(row);
                current.Use();
            }
        }

        private void HandleKeyboard()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown) return;

            int rows = _filteredRows.Count;
            switch (current.keyCode)
            {
                case KeyCode.DownArrow:
                    if (rows > 0) { _highlight = (_highlight + 1) % rows; EnsureVisible(); }
                    current.Use();
                    Repaint();
                    break;
                case KeyCode.UpArrow:
                    if (rows > 0) { _highlight = (_highlight - 1 + rows) % rows; EnsureVisible(); }
                    current.Use();
                    Repaint();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (rows > 0) CommitRow(_filteredRows[Mathf.Clamp(_highlight, 0, rows - 1)]);
                    current.Use();
                    break;
                case KeyCode.Escape:
                    if (_page == Page.Argument) InitRoot();
                    else Close();
                    current.Use();
                    break;
            }
        }

        private void EnsureVisible()
        {
            if (_highlight < 0 || _highlight >= _rowY.Count) return;
            float y = _rowY[_highlight];
            float listHeight = position.height - SearchHeight - (_page == Page.Argument ? HeaderBarHeight : 0f);
            if (y < _scroll.y) _scroll.y = y;
            float bottom = _scroll.y + listHeight - RowHeight;
            if (y > bottom) _scroll.y = y - listHeight + RowHeight * 2f;
        }

        private void CommitRow(PaletteRow row)
        {
            if (_page == Page.Argument)
            {
                row.onSelect?.Invoke();
                Close();
                return;
            }

            Page before = _page;
            row.onSelect?.Invoke();
            if (_page == before) Close(); // command ran to completion (no follow-up page pushed)
        }

        // ------------------------------------------------------------------ filtering

        private void RebuildFilteredRows()
        {
            _filteredRows.Clear();
            _rowY.Clear();
            _headers.Clear();

            bool groupByCategory = _page == Page.Root;
            IReadOnlyList<PaletteRow> source = _page == Page.Root
                ? (IReadOnlyList<PaletteRow>)_rootRowsCache ?? Array.Empty<PaletteRow>()
                : _argumentRows ?? Array.Empty<PaletteRow>();

            string search = _search.Trim();
            string lastCategory = null;
            bool anyCategorySeen = false;
            float y = 0f;

            foreach (PaletteRow row in source)
            {
                if (!Matches(row, search)) continue;

                if (groupByCategory && (!anyCategorySeen || !string.Equals(row.category, lastCategory, StringComparison.Ordinal)))
                {
                    _headers.Add((y, string.IsNullOrEmpty(row.category) ? "Other" : row.category));
                    y += SectionHeaderHeight;
                    lastCategory = row.category;
                    anyCategorySeen = true;
                }

                _rowY.Add(y);
                _filteredRows.Add(row);
                y += RowHeight;
            }

            _contentHeight = y;
            _highlight = _filteredRows.Count == 0 ? 0 : Mathf.Clamp(_highlight, 0, _filteredRows.Count - 1);
            _dirty = false;
        }

        private static bool Matches(PaletteRow row, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            if (Contains(row.label, search)) return true;
            if (Contains(row.sublabel, search)) return true;
            if (Contains(row.category, search)) return true;
            if (row.keywords != null)
                foreach (string keyword in row.keywords)
                    if (Contains(keyword, search)) return true;
            return false;
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
