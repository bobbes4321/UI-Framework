using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The view-transition browser a flow edge's Transition row opens — the navigation sibling of
    /// <see cref="AnimationPresetBrowserPopup"/>. Discovered <see cref="ViewTransitionAsset"/>s are
    /// grouped by category, all expanded (the library is small — a handful of curated entries plus
    /// whatever a project drops in). The first row is "(project default)", which clears the edge's
    /// override so it falls through to <c>NeoUISettings.defaultViewTransition</c> at runtime.
    /// <para>
    /// Hovering a transition for a beat live-previews it on the actual from/to view instances passed in
    /// by the caller (already resolved to live scene <see cref="RectTransform"/>s — this popup does no
    /// scene lookups of its own) via the <see cref="AnimationPreview"/> snapshot/restore machinery: the
    /// outgoing side plays immediately, the incoming side is scheduled after the transition's
    /// <see cref="ViewTransitionAsset.incomingOffset"/> using the same editor-update clock that drives
    /// the dwell timer. Everything is restored untouched when the popup closes; only a click applies.
    /// Either root list may be null/empty (edge with no resolvable views) — the list still works, it
    /// just previews nothing.
    /// </para>
    /// </summary>
    internal sealed class ViewTransitionBrowserPopup : PopupWindowContent
    {
        private const float RowHeight = 20f;
        private const float HeaderHeight = 20f;
        private const float SearchHeight = 24f;
        private const float Width = 280f;
        private const float MaxHeight = 420f;
        private const double DwellSeconds = 0.25;

        private readonly string _current;                 // applied transition full name (null = project default)
        private readonly Action<string> _onSelect;         // "" = clear to project default
        private readonly List<RectTransform> _outgoingRoots;
        private readonly List<RectTransform> _incomingRoots;
        private readonly List<UIAnimation> _outScratches;
        private readonly List<UIAnimation> _inScratches;

        private string _filter = "";
        private Vector2 _scroll;
        private GUIStyle _search, _rowLabel, _headerLabel;

        // hover → dwell → preview state
        private string _hoverName;
        private double _hoverStart;
        private string _previewName;
        private ViewTransitionAsset _previewTransition;
        private double _previewStart;
        private bool _incomingPending;

        private readonly struct Row
        {
            public readonly string header;
            public readonly ViewTransitionAsset transition;
            public Row(string header, ViewTransitionAsset transition) { this.header = header; this.transition = transition; }
        }

        public ViewTransitionBrowserPopup(string current, Action<string> onSelect,
            List<RectTransform> outgoingRoots, List<RectTransform> incomingRoots)
        {
            _current = string.IsNullOrEmpty(current) ? null : current;
            _onSelect = onSelect;
            _outgoingRoots = outgoingRoots;
            _incomingRoots = incomingRoots;
            _outScratches = BuildScratches(_outgoingRoots);
            _inScratches = BuildScratches(_incomingRoots);
        }

        private static List<UIAnimation> BuildScratches(List<RectTransform> roots)
        {
            var list = new List<UIAnimation>();
            if (roots == null) return list;
            for (int i = 0; i < roots.Count; i++) list.Add(new UIAnimation());
            return list;
        }

        // ------------------------------------------------------------------ lifecycle

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;
            EditorApplication.update += Tick;
        }

        public override void OnClose()
        {
            EditorApplication.update -= Tick;
            StopPreview();
        }

        // ------------------------------------------------------------------ layout

        public override Vector2 GetWindowSize()
        {
            List<Row> rows = BuildRows();
            float height = 8f + SearchHeight + RowHeight; // chrome + "(project default)" row
            foreach (Row row in rows) height += row.header != null ? HeaderHeight : RowHeight;
            return new Vector2(Width, Mathf.Min(height + 8f, MaxHeight));
        }

        private List<Row> BuildRows()
        {
            var rows = new List<Row>();
            var groups = new SortedDictionary<string, List<ViewTransitionAsset>>(StringComparer.Ordinal);
            foreach (ViewTransitionAsset t in ViewTransitionRegistry.All)
            {
                if (t == null || string.IsNullOrEmpty(t.transitionName)) continue;
                if (!string.IsNullOrEmpty(_filter) && t.fullName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!groups.TryGetValue(t.category, out List<ViewTransitionAsset> list))
                    groups[t.category] = list = new List<ViewTransitionAsset>();
                list.Add(t);
            }
            foreach (KeyValuePair<string, List<ViewTransitionAsset>> group in groups)
            {
                group.Value.Sort((a, b) => string.CompareOrdinal(a.transitionName, b.transitionName));
                rows.Add(new Row(group.Key, null));
                foreach (ViewTransitionAsset t in group.Value) rows.Add(new Row(null, t));
            }
            return rows;
        }

        // ------------------------------------------------------------------ draw

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();
            Event e = Event.current;
            if (e.type == EventType.MouseMove) editorWindow.Repaint();

            float y = rect.y + 4f;
            var searchRect = new Rect(rect.x + 6f, y, rect.width - 12f, SearchHeight - 6f);
            EditorGUI.BeginChangeCheck();
            string next = EditorGUI.TextField(searchRect, _filter, _search);
            if (EditorGUI.EndChangeCheck()) _filter = next ?? "";
            y += SearchHeight;

            List<Row> rows = BuildRows();
            var listRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            float contentHeight = RowHeight;
            foreach (Row row in rows) contentHeight += row.header != null ? HeaderHeight : RowHeight;
            bool scrolling = contentHeight > listRect.height;
            var viewRect = new Rect(0, 0, listRect.width - (scrolling ? 16f : 0f), contentHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, viewRect);

            string hovered = null;
            float rowY = 0f;
            DrawDefaultRow(new Rect(0, rowY, viewRect.width, RowHeight));
            rowY += RowHeight;

            foreach (Row row in rows)
            {
                if (row.header != null)
                {
                    DrawHeaderRow(new Rect(0, rowY, viewRect.width, HeaderHeight), row.header);
                    rowY += HeaderHeight;
                }
                else
                {
                    var rowRect = new Rect(0, rowY, viewRect.width, RowHeight);
                    DrawTransitionRow(rowRect, row.transition);
                    if (rowRect.Contains(e.mousePosition)) hovered = row.transition.fullName;
                    rowY += RowHeight;
                }
            }
            GUI.EndScrollView();

            if ((e.type == EventType.Repaint || e.type == EventType.MouseMove) && hovered != _hoverName)
            {
                _hoverName = hovered;
                _hoverStart = EditorApplication.timeSinceStartup;
            }
        }

        private void DrawDefaultRow(Rect rect)
        {
            bool selected = _current == null;
            DrawRowChrome(rect, selected, previewing: false);
            if (Event.current.type == EventType.Repaint)
                GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 12f, rect.height),
                    new GUIContent("∅  (project default)", "Falls through to NeoUISettings.defaultViewTransition."),
                    _rowLabel);
            HandleRowClick(rect, null);
        }

        private void DrawHeaderRow(Rect rect, string category)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, NeoColors.SectionBackground);
            GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height), category, _headerLabel);
        }

        private void DrawTransitionRow(Rect rect, ViewTransitionAsset transition)
        {
            bool selected = string.Equals(_current, transition.fullName, StringComparison.Ordinal);
            DrawRowChrome(rect, selected, previewing: string.Equals(_previewName, transition.fullName, StringComparison.Ordinal));
            if (Event.current.type == EventType.Repaint)
                GUI.Label(new Rect(rect.x + 16f, rect.y, rect.width - 20f, rect.height),
                    new GUIContent(transition.transitionName, transition.fullName), _rowLabel);
            HandleRowClick(rect, transition);
        }

        private void DrawRowChrome(Rect rect, bool selected, bool previewing)
        {
            if (Event.current.type != EventType.Repaint) return;
            bool hover = rect.Contains(Event.current.mousePosition);
            if (selected) EditorGUI.DrawRect(rect, NeoColors.Flow.WithAlpha(0.22f));
            else if (hover) EditorGUI.DrawRect(rect, NeoColors.RowHover);
            if (selected || previewing)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), NeoColors.Flow);
        }

        private void HandleRowClick(Rect rect, ViewTransitionAsset transition)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;
            _onSelect?.Invoke(transition != null ? transition.fullName : "");
            e.Use();
            editorWindow.Close(); // OnClose restores the preview snapshot; the applied selection stays
        }

        // ------------------------------------------------------------------ hover preview

        private void Tick()
        {
            bool anyRoots = (_outgoingRoots != null && _outgoingRoots.Count > 0) ||
                            (_incomingRoots != null && _incomingRoots.Count > 0);
            if (!anyRoots) return;

            if (_hoverName == null)
            {
                if (_previewName != null) StopPreview();
                return;
            }
            if (!string.Equals(_hoverName, _previewName, StringComparison.Ordinal)
                && EditorApplication.timeSinceStartup - _hoverStart >= DwellSeconds)
                StartPreview(_hoverName);

            if (_incomingPending && _previewTransition != null
                && EditorApplication.timeSinceStartup - _previewStart >= _previewTransition.incomingOffset)
            {
                PlayChannel(_previewTransition.incoming, _incomingRoots, _inScratches);
                _incomingPending = false;
            }
        }

        private void StartPreview(string fullName)
        {
            if (!ViewTransitionRegistry.TryGet(fullName, out ViewTransitionAsset transition)) return;

            SettleScratches(); // return the previous preview's channels to rest first
            BeginPreviewRoots(); // no-op where already snapshotted — keeps the ORIGINAL rest state

            bool overridesOutgoing = ViewTransitionAsset.Overrides(transition.outgoing);
            bool overridesIncoming = ViewTransitionAsset.Overrides(transition.incoming);

            if (overridesOutgoing) PlayChannel(transition.outgoing, _outgoingRoots, _outScratches);

            _previewTransition = transition;
            _previewStart = EditorApplication.timeSinceStartup;
            _incomingPending = overridesIncoming;
            if (overridesIncoming && transition.incomingOffset <= 0f)
            {
                PlayChannel(transition.incoming, _incomingRoots, _inScratches);
                _incomingPending = false;
            }

            _previewName = fullName;
            editorWindow.Repaint();
        }

        private static void PlayChannel(UIAnimation source, List<RectTransform> roots, List<UIAnimation> scratches)
        {
            if (roots == null) return;
            for (int i = 0; i < roots.Count; i++)
            {
                RectTransform root = roots[i];
                if (root == null) continue;
                UIAnimation scratch = scratches[i];
                UIAnimationChannels.Copy(source, scratch);
                CanvasGroup group = root.GetComponent<CanvasGroup>();
                if (group == null && scratch.fade.enabled) group = root.gameObject.AddComponent<CanvasGroup>();
                scratch.SetTarget(root, group);
                scratch.CaptureStartValues(); // target is at rest here — refreshes color/start endpoints
                scratch.onFinish = null;
                scratch.Play();
            }
        }

        private void BeginPreviewRoots()
        {
            if (_outgoingRoots != null) foreach (RectTransform r in _outgoingRoots) if (r != null) AnimationPreview.BeginPreview(r);
            if (_incomingRoots != null) foreach (RectTransform r in _incomingRoots) if (r != null) AnimationPreview.BeginPreview(r);
        }

        private void SettleScratches()
        {
            foreach (UIAnimation s in _outScratches) { s.Stop(silent: true); s.RestoreStartValues(); }
            foreach (UIAnimation s in _inScratches) { s.Stop(silent: true); s.RestoreStartValues(); }
        }

        private void StopPreview()
        {
            SettleScratches();
            if (_outgoingRoots != null) foreach (RectTransform r in _outgoingRoots) if (r != null) AnimationPreview.EndPreview(r);
            if (_incomingRoots != null) foreach (RectTransform r in _incomingRoots) if (r != null) AnimationPreview.EndPreview(r);
            _previewName = null;
            _previewTransition = null;
            _incomingPending = false;
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ styles

        private void EnsureStyles()
        {
            _search ??= new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
            _rowLabel ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 11 };
            _headerLabel ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = NeoColors.TextSubtle }
            };
        }
    }
}
