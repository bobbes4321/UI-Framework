using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The animation-preset browser an animator slot's Preset row opens — the motion sibling of
    /// <c>PresetPickerPopup</c>. Discovered <see cref="UIAnimationPreset"/>s are grouped by category:
    /// the slot role's suggested categories (<see cref="NeoAnimatorRoles"/>) sit expanded on top and
    /// every other category is collapsed-but-reachable below — relevance is a sort, never a hard
    /// filter, and the section list comes from whatever categories discovered assets declare (the
    /// open seam). The first row is "None", which clears the slot. Hovering a preset for a beat
    /// live-previews it on the actual selected widget through the <see cref="AnimationPreview"/>
    /// snapshot/restore machinery — the real object, real size, real colors — and everything is
    /// restored untouched when the popup closes; only a click applies (undo-recorded by the caller).
    /// <para>
    /// The grouping/sort/expand/search logic lives in the shared <see cref="AnimationPresetBrowserModel"/>
    /// (also driven by the Design System Motion tab's library browser) — this popup owns only its
    /// rendering, preview and styles. Pure IMGUI on the EditorUI palette, cached GUIStyles. Preview runs
    /// only for single selection (the caller passes a null <c>previewTarget</c> on multi-edit; apply still
    /// hits every target). The editor-update subscription lives strictly between OnOpen and OnClose — a
    /// transient popup needs a dwell timer, but nothing survives the window.
    /// </para>
    /// </summary>
    internal sealed class AnimationPresetBrowserPopup : PopupWindowContent
    {
        private const float RowHeight = 20f;
        private const float HeaderHeight = 20f;
        private const float SearchHeight = 24f;
        private const float RoleHeight = 16f;
        private const float Width = 300f;
        private const float MaxHeight = 480f;
        private const double DwellSeconds = 0.02;

        private readonly string _roleLabel;
        private readonly string _current;                     // applied preset full name (null = none)
        private readonly Action<UIAnimationPreset> _onSelect; // null argument = clear the slot
        private readonly RectTransform _previewTarget;        // null = no live preview (multi-edit)
        private readonly AnimationPresetBrowserModel _model;  // shared grouping/sort/expand/search

        private string _filter = "";
        private Vector2 _scroll;
        private GUIStyle _search, _rowLabel, _headerLabel, _roleTitle;

        // hover → dwell → preview state
        private string _hoverName;
        private double _hoverStart;
        private string _previewName;
        private readonly UIAnimation _scratch = new UIAnimation();

        public AnimationPresetBrowserPopup(string role, string current,
            Action<UIAnimationPreset> onSelect, RectTransform previewTarget)
        {
            _current = string.IsNullOrEmpty(current) ? null : current;
            _onSelect = onSelect;
            _previewTarget = previewTarget;

            NeoAnimatorRole info = null;
            if (!string.IsNullOrEmpty(role)) NeoAnimatorRoles.TryGet(role, out info);
            _roleLabel = info != null ? info.DisplayName : null;
            string[] suggested = info != null ? info.SuggestedCategories : Array.Empty<string>();

            _model = new AnimationPresetBrowserModel(AnimationPresetRegistry.All, suggested, _current);
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
            IReadOnlyList<AnimationPresetBrowserModel.Row> rows = _model.BuildRows(_filter);
            float height = 8f + (_roleLabel != null ? RoleHeight : 0f) + SearchHeight + RowHeight; // chrome + None row
            foreach (AnimationPresetBrowserModel.Row row in rows) height += row.header != null ? HeaderHeight : RowHeight;
            return new Vector2(Width, Mathf.Min(height + 8f, MaxHeight));
        }

        // ------------------------------------------------------------------ draw

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();
            Event e = Event.current;
            if (e.type == EventType.MouseMove) editorWindow.Repaint();

            float y = rect.y + 4f;
            if (_roleLabel != null)
            {
                GUI.Label(new Rect(rect.x + 6f, y, rect.width - 12f, RoleHeight), _roleLabel + " presets", _roleTitle);
                y += RoleHeight;
            }

            var searchRect = new Rect(rect.x + 6f, y, rect.width - 12f, SearchHeight - 6f);
            EditorGUI.BeginChangeCheck();
            string next = EditorGUI.TextField(searchRect, _filter, _search);
            if (EditorGUI.EndChangeCheck()) _filter = next ?? "";
            y += SearchHeight;

            IReadOnlyList<AnimationPresetBrowserModel.Row> rows = _model.BuildRows(_filter);
            var listRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            float contentHeight = RowHeight; // None row
            foreach (AnimationPresetBrowserModel.Row row in rows) contentHeight += row.header != null ? HeaderHeight : RowHeight;
            bool scrolling = contentHeight > listRect.height;
            var viewRect = new Rect(0, 0, listRect.width - (scrolling ? 16f : 0f), contentHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, viewRect);

            string hovered = null;
            float rowY = 0f;
            DrawNoneRow(new Rect(0, rowY, viewRect.width, RowHeight));
            rowY += RowHeight;

            foreach (AnimationPresetBrowserModel.Row row in rows)
            {
                if (row.header != null)
                {
                    DrawHeaderRow(new Rect(0, rowY, viewRect.width, HeaderHeight), row.header);
                    rowY += HeaderHeight;
                }
                else
                {
                    var rowRect = new Rect(0, rowY, viewRect.width, RowHeight);
                    DrawPresetRow(rowRect, row.preset);
                    if (rowRect.Contains(e.mousePosition)) hovered = row.preset.fullName;
                    rowY += RowHeight;
                }
            }
            GUI.EndScrollView();

            // Track hover on paint/move only — Layout events carry a stale mouse position.
            if ((e.type == EventType.Repaint || e.type == EventType.MouseMove) && hovered != _hoverName)
            {
                _hoverName = hovered;
                _hoverStart = EditorApplication.timeSinceStartup;
            }
        }

        private void DrawNoneRow(Rect rect)
        {
            bool selected = _current == null;
            DrawRowChrome(rect, selected);
            if (Event.current.type == EventType.Repaint)
                GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 12f, rect.height),
                    new GUIContent("∅  None", "Clear this slot — disables every channel."), _rowLabel);
            HandleRowClick(rect, null, isNone: true);
        }

        private void DrawHeaderRow(Rect rect, AnimationPresetBrowserModel.Group group)
        {
            Event e = Event.current;
            bool searching = !string.IsNullOrEmpty(_filter);
            bool open = searching || _model.IsExpanded(group.category);
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, NeoColors.SectionBackground);
                string suffix = group.suggested ? "  · suggested" : "";
                GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height),
                    $"{(open ? "▾" : "▸")}  {group.category}  ({group.presets.Count}){suffix}", _headerLabel);
            }
            if (!searching && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _model.ToggleExpanded(group.category);
                e.Use();
                editorWindow.Repaint();
            }
        }

        private void DrawPresetRow(Rect rect, UIAnimationPreset preset)
        {
            bool selected = string.Equals(_current, preset.fullName, StringComparison.Ordinal);
            DrawRowChrome(rect, selected, previewing: string.Equals(_previewName, preset.fullName, StringComparison.Ordinal));
            if (Event.current.type == EventType.Repaint)
                GUI.Label(new Rect(rect.x + 16f, rect.y, rect.width - 20f, rect.height),
                    new GUIContent(preset.presetName, preset.fullName), _rowLabel);
            HandleRowClick(rect, preset, isNone: false);
        }

        private void DrawRowChrome(Rect rect, bool selected, bool previewing = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            bool hover = rect.Contains(Event.current.mousePosition);
            if (selected) EditorGUI.DrawRect(rect, NeoColors.Animation.WithAlpha(0.22f));
            else if (hover) EditorGUI.DrawRect(rect, NeoColors.RowHover);
            if (selected || previewing)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), NeoColors.Animation);
        }

        private void HandleRowClick(Rect rect, UIAnimationPreset preset, bool isNone)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;
            _onSelect?.Invoke(preset);
            e.Use();
            editorWindow.Close(); // OnClose restores the preview snapshot; the applied slot data stays
        }

        // ------------------------------------------------------------------ hover preview

        private void Tick()
        {
            if (_previewTarget == null) return;
            if (_hoverName == null)
            {
                if (_previewName != null) StopPreview();
                return;
            }
            if (!string.Equals(_hoverName, _previewName, StringComparison.Ordinal)
                && EditorApplication.timeSinceStartup - _hoverStart >= DwellSeconds)
                StartPreview(_hoverName);
        }

        private void StartPreview(string fullName)
        {
            UIAnimationPreset preset = AnimationPresetRegistry.GetByFullName(fullName);
            if (preset == null || _previewTarget == null) return;

            SettleScratch(); // return the previous preview's channels to rest first
            AnimationPreview.BeginPreview(_previewTarget); // no-op when already snapshotted — keeps the ORIGINAL rest state
            preset.CopyTo(_scratch);
            CanvasGroup group = _previewTarget.GetComponent<CanvasGroup>();
            if (group == null && _scratch.fade.enabled) group = _previewTarget.gameObject.AddComponent<CanvasGroup>();
            _scratch.SetTarget(_previewTarget, group);
            _scratch.CaptureStartValues(); // target is at rest here — refreshes color/start endpoints
            _scratch.onFinish = null;
            _scratch.Play();
            _previewName = fullName;
            editorWindow.Repaint();
        }

        /// <summary> Stops the scratch animation and puts its driven channels (incl. color) back to rest. </summary>
        private void SettleScratch()
        {
            _scratch.Stop(silent: true);
            _scratch.RestoreStartValues();
        }

        private void StopPreview()
        {
            SettleScratch();
            if (_previewTarget != null) AnimationPreview.EndPreview(_previewTarget);
            _previewName = null;
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
            _roleTitle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleLeft };
        }
    }
}
