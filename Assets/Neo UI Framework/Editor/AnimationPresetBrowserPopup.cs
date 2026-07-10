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
    /// hits every target) — unless <see cref="stageFallback"/> is on, in which case a null target makes
    /// the popup preview on its own offscreen <see cref="MotionPreviewStage"/>, rendered in an inline
    /// viewport at the top of the popup (always visible, whichever way the popup opens). The
    /// editor-update subscription lives strictly between OnOpen and OnClose — a transient popup needs a
    /// dwell timer, but nothing survives the window (the fallback stage is disposed with it).
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
        private readonly RectTransform _explicitTarget;       // ctor target (scene selection / inspected widget)
        private readonly AnimationPresetBrowserModel _model;  // shared grouping/sort/expand/search

        /// <summary>
        /// When true and no explicit target was given, the popup owns a lazy offscreen
        /// <see cref="MotionPreviewStage"/> and renders its viewport INSIDE the popup (pinned above the
        /// search field) — previews are always visible regardless of which way the popup opens or what
        /// it covers in the host window. Role-default pickers (Setup wizard, Design System Motion tab)
        /// enable this; the animator inspectors keep it off (multi-edit stays preview-less by design).
        /// </summary>
        internal bool stageFallback;
        private MotionPreviewStage _stage;   // popup-owned fallback stage (lazy; disposed in OnClose)
        private RectTransform _target;       // the target the CURRENT preview is live on (owes a restore)

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
            _explicitTarget = previewTarget;

            NeoAnimatorRole info = null;
            if (!string.IsNullOrEmpty(role)) NeoAnimatorRoles.TryGet(role, out info);
            _roleLabel = info != null ? info.DisplayName : null;
            string[] suggested = info != null ? info.SuggestedCategories : Array.Empty<string>();

            _model = new AnimationPresetBrowserModel(AnimationPresetRegistry.All, suggested, _current);
        }

        /// <summary>
        /// Shared "animator role → default preset" picker row — the Design System Motion tab's role
        /// defaults and the Setup wizard's Motion section both draw through here so every role picker
        /// gets the same browser (grouped presets, search, None row, hover-dwell live preview) instead
        /// of a plain dropdown. Previews play on the scene selection when
        /// <paramref name="previewTarget"/> resolves one (real context wins); otherwise the popup
        /// spins up its OWN offscreen stage and shows the preview in an inline viewport at its top —
        /// so the preview is always visible no matter which way the popup opens.
        /// <paramref name="apply"/> receives the chosen preset, or null from the None row / ✕ button.
        /// </summary>
        internal static void DrawRoleRow(string roleId, string current, float labelWidth,
            Func<RectTransform> previewTarget, Action<UIAnimationPreset> apply,
            string emptyLabel = "(built-in)")
        {
            NeoAnimatorRoles.TryGet(roleId, out NeoAnimatorRole info);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(info != null
                        ? new GUIContent(info.DisplayName, info.Description)
                        : new GUIContent(roleId),
                    GUILayout.Width(labelWidth));

                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                bool empty = string.IsNullOrEmpty(current);
                var label = new GUIContent(empty ? emptyLabel : current,
                    "Pick a preset for this role — hovering a row previews it live.");
                if (GUI.Button(rect, label, EditorStyles.popup))
                    PopupWindow.Show(rect, new AnimationPresetBrowserPopup(roleId, current, apply,
                        previewTarget?.Invoke()) { stageFallback = true });

                using (new EditorGUI.DisabledScope(empty))
                    if (GUILayout.Button(new GUIContent("✕", "Clear — keep the built-in feel."),
                            GUILayout.Width(22f)))
                        apply(null);
            }
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
            _stage?.Dispose();   // after StopPreview — restoring the card needs it alive
            _stage = null;
        }

        // ------------------------------------------------------------------ layout

        // The inline stage viewport (fallback-preview mode): pinned above the search field, full popup
        // width, at the stage's aspect. Reserved from the moment the popup opens (never resizes on the
        // first hover) — the stage renders its static card until a preview plays.
        private bool ShowsStageViewport => stageFallback && _explicitTarget == null;
        private const float ViewportWidth = Width - 12f;
        private const float ViewportHeight = ViewportWidth / MotionPreviewStage.AspectRatio;

        public override Vector2 GetWindowSize()
        {
            IReadOnlyList<AnimationPresetBrowserModel.Row> rows = _model.BuildRows(_filter);
            float height = 8f + (_roleLabel != null ? RoleHeight : 0f) + SearchHeight + RowHeight; // chrome + None row
            foreach (AnimationPresetBrowserModel.Row row in rows) height += row.header != null ? HeaderHeight : RowHeight;
            float extra = 0f;
            if (ShowsStageViewport) extra = ViewportHeight + 4f;
            else if (stageFallback) extra = RoleHeight;   // "previews play on the selection" hint line
            return new Vector2(Width, Mathf.Min(height + 8f, MaxHeight) + extra);
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

            if (ShowsStageViewport)
            {
                _stage ??= new MotionPreviewStage();
                _stage.DrawViewport(new Rect(rect.x + 6f, y, rect.width - 12f, ViewportHeight),
                    live: _previewName != null);
                y += ViewportHeight + 4f;
            }
            else if (stageFallback)   // explicit target won — say where hover-previews play
            {
                GUI.Label(new Rect(rect.x + 6f, y, rect.width - 12f, RoleHeight),
                    new GUIContent($"Previews play on the scene selection ‘{_explicitTarget.name}’.",
                        "Clear the selection to preview on the built-in stage instead."), _roleTitle);
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

        // Selection/inspected widget wins (real context beats a synthetic card); the popup-owned stage
        // is the fallback when stageFallback is on. Null only on multi-edit (no fallback) or headless.
        private RectTransform ResolvePreviewTarget()
        {
            if (_explicitTarget != null) return _explicitTarget;
            if (!stageFallback) return null;
            _stage ??= new MotionPreviewStage();
            return _stage.Target;
        }

        private void Tick()
        {
            if (_explicitTarget == null && !stageFallback) return; // multi-edit: previews disabled
            if (_hoverName == null)
            {
                if (_previewName != null) StopPreview();
            }
            else if (!string.Equals(_hoverName, _previewName, StringComparison.Ordinal)
                     && EditorApplication.timeSinceStartup - _hoverStart >= DwellSeconds)
                StartPreview(_hoverName);

            // The inline viewport re-renders only during this window's Repaint — keep it live.
            if (_previewName != null && ShowsStageViewport) editorWindow.Repaint();
        }

        private void StartPreview(string fullName)
        {
            UIAnimationPreset preset = AnimationPresetRegistry.GetByFullName(fullName);
            RectTransform target = ResolvePreviewTarget();
            if (preset == null || target == null) return;

            SettleScratch(); // return the previous preview's channels to rest first
            AnimationPreview.BeginPreview(target); // no-op when already snapshotted — keeps the ORIGINAL rest state
            preset.CopyTo(_scratch);
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null && _scratch.fade.enabled) group = target.gameObject.AddComponent<CanvasGroup>();
            _scratch.SetTarget(target, group);
            _scratch.CaptureStartValues(); // target is at rest here — refreshes color/start endpoints
            _scratch.onFinish = null;
            _scratch.Play();
            _previewName = fullName;
            _target = target;
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
            if (_target != null) AnimationPreview.EndPreview(_target);
            _target = null;
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
