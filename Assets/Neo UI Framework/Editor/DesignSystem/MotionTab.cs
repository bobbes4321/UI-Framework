using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Motion" tab — a dual-pane master–detail authoring surface (it opts out of
    /// the window's own scroll view via <see cref="DesignSystemTabDescriptor.ownsLayout"/> and fills the
    /// full remaining height through <see cref="DesignSystemGUI.BeginSplitPane"/>):
    /// <list type="bullet">
    /// <item><b>LEFT (master, fixed 260px, own scroll):</b> a collapsible <b>Default motion per animator
    /// role</b> form (the <see cref="NeoUISettings.animatorDefaults"/> the Setup wizard seeds and animator
    /// <c>Reset()</c> / the widget factory consume, so editing here flows into every newly-added animator
    /// and freshly-generated button/view), then the <b>preset library browser</b> — the full discovered
    /// <see cref="UIAnimationPreset"/> set as a grouped, searchable list (the shared
    /// <see cref="AnimationPresetBrowserModel"/>, same grouping as <see cref="AnimationPresetBrowserPopup"/>)
    /// with right-aligned M/R/S/F/C channel badges and <b>hover-dwell live preview</b> (parity with the
    /// popup), then the <b>New preset</b> row + a Create-or-Repair-library button.</item>
    /// <item><b>RIGHT (detail, flexible, own scroll):</b> the selected preset's editor — a header
    /// (Editing / Ping / Duplicate / Delete), the shared five-channel <see cref="AnimationPresetGUI"/>
    /// form (same as the <see cref="UIAnimationPresetEditor"/> inspector), a rename-to-convention notice
    /// when the filename drifts from <c>{Category}_{Name}.asset</c>, the preview surface (below), an
    /// After-Effects-lite scrub + lanes strip (the lanes reuse
    /// <see cref="AnimationPreview.DrawChannelLanes(UIAnimation,float,bool)"/>), and the preview
    /// controls. A friendly empty state shows when nothing is selected.</item>
    /// </list>
    /// <b>Preview target:</b> a valid scene-selection RectTransform WINS (previewing in real context beats
    /// a synthetic card — a one-line hint says where the preview plays); with nothing selected, previews
    /// (button, hover-dwell AND scrub) fall back to the built-in offscreen <see cref="MotionPreviewStage"/>
    /// — a dummy card rendered live into an aspect-correct viewport in the right pane — so the tab is
    /// fully self-sufficient for browsing motion. <see cref="ResolvePreviewTarget"/> is the ONE seam that
    /// decides; the stage is created lazily and torn down with the State.
    /// Stateful: the tab's <see cref="State"/> caches the browser model, the two split-pane scroll
    /// positions, the selected preset's <see cref="SerializedObject"/> (recreated only on selection
    /// change), a cached rename-convention check, the lazily-created stage, and a single preview lifecycle
    /// (used by the button, hover-dwell AND scrub) that restores the previewed object on Stop / selection
    /// change / tab switch / window close.
    /// </summary>
    internal static class MotionTab
    {
        // How long the Motion tab may go undrawn (tab switched away / window hidden) before an in-flight
        // preview is treated as orphaned and the object restored. The Tick repaints while the tab IS active,
        // so lastDraw stays fresh; when Draw stops running, this fires.
        private const double PreviewIdleRestoreSeconds = 0.4;

        // Browser hover-dwell threshold before a hovered row live-previews. AnimationPresetBrowserPopup's
        // own DwellSeconds (0.02) is private/inaccessible AND tuned for a transient popup you commit from
        // immediately; a persistent window you mouse ACROSS wants a calmer beat, so per the task's fallback
        // guidance (~0.35–0.5s) this uses 0.4s.
        private const double HoverDwellSeconds = 0.4;

        // --- resizable master column (DesignSystemGUI split-pane) ---
        // Default 260px (the width the master form was tuned for); the drag clamps it to [Min, view−Right]
        // so the right (detail) pane always keeps at least RightMinWidth. Persisted per-tab via SessionState
        // (keyed like NeoDesignSystemWindow.TabKey) so a drag survives close/reopen within the session.
        private const float DefaultLeftWidth = 260f;
        private const float LeftMinWidth = 200f;
        private const float RightMinWidth = 320f;
        private const string LeftWidthKey = "NeoUI.DesignSystem.Motion.LeftWidth";

        /// <summary> Per-window UI state for the Motion tab (disposed by the window; see <see cref="Dispose"/>). </summary>
        internal sealed class State : IDisposable
        {
            // What currently owns the single preview lifecycle — so hover-leave tears down ONLY a
            // hover-started preview, never a manual (button/replay/scrub) one.
            private enum PreviewOwner { None, Manual, Hover }

            // --- library browser ---
            public AnimationPresetBrowserModel model;
            public int builtForCount = -1;      // discovered-count the model was built for (rebuild on change)
            public string filter = "";

            // --- split-pane scroll positions (caller-owned, per DesignSystemGUI.BeginSplitLeft/Right) ---
            public Vector2 leftScroll;
            public Vector2 rightScroll;

            // --- draggable master-column width (caller-owned + SessionState-persisted; see LeftWidthKey) ---
            public float leftWidth;
            private float _persistedWidth;

            /// <summary> Seeds <see cref="leftWidth"/> from SessionState (or <paramref name="def"/> first
            /// time) — call once from the state factory. </summary>
            public void LoadWidth(string key, float def) => leftWidth = _persistedWidth = SessionState.GetFloat(key, def);

            /// <summary> Writes <see cref="leftWidth"/> back to SessionState only when a drag actually
            /// changed it (never a per-OnGUI write) — call after the split pane closes. </summary>
            public void PersistWidth(string key)
            {
                if (leftWidth == _persistedWidth) return;
                SessionState.SetFloat(key, leftWidth);
                _persistedWidth = leftWidth;
            }

            // --- selection + its cached SerializedObject (recreated only when the selection changes) ---
            public UIAnimationPreset selected;
            public SerializedObject selectedSO;

            // --- new-preset row ---
            public string newCategory = "";
            public string newName = "";

            // --- built-in offscreen preview stage (fallback preview target) ---
            // Created lazily by ResolvePreviewTarget / the right-pane preview surface — a user who always
            // previews on a scene selection never pays for it. Owned by this State: disposed with it
            // (window close), and self-healing across domain reloads / RT loss (see MotionPreviewStage).
            public MotionPreviewStage stage;

            // --- rename-to-convention check (cached per selection; item 7) ---
            private UIAnimationPreset _renameCheckedFor;
            private bool _renameNeeded;
            private string _renameToStem;               // the "{Category}_{Name}" the file SHOULD have
            public bool RenameNeeded => _renameNeeded;
            public string RenameToStem => _renameToStem;

            // --- preview lifecycle (button / hover-dwell / scrub all share this ONE path) ---
            // A throwaway animation the preset is copied into, so previewing/scrubbing never dirties the asset.
            private readonly UIAnimation _preview = new UIAnimation();
            private RectTransform _previewTarget;    // non-null == a preview is live and owes a restore
            private UIAnimationPreset _previewPreset; // which preset _preview currently holds (hover de-dup)
            private PreviewOwner _owner = PreviewOwner.None;
            private bool _scrubbing;                  // preview is a static scrub pose (no playback tick)
            private float _scrubProgress;             // last scrubbed progress (0..1) for slider/playhead redraw
            private EditorWindow _window;             // for repaint-driven idle detection while previewing
            private double _lastDraw;
            private bool _subscribed;

            // --- browser hover-dwell tracking ---
            private string _hoverName;                // preset fullName the mouse is over (null = none)
            private double _hoverStart;               // when the current hover began (dwell threshold)

            public bool Previewing => _previewTarget != null;
            public RectTransform PreviewTarget => _previewTarget;
            public bool Scrubbing => _scrubbing;
            public float ScrubProgress => _scrubProgress;

            /// <summary> True while the LIVE preview is the currently-selected preset (a hover preview of a
            /// DIFFERENT row must not flip the right-pane controls into their Replay/Stop state). </summary>
            public bool PreviewingSelected => _previewTarget != null && _previewPreset == selected;

            /// <summary> Called at the top of every Draw: keeps the window handle current, marks the tab
            /// alive, and enables mouse-move events (needed to detect browser hover for the dwell timer). </summary>
            public void MarkDrawn(EditorWindow window)
            {
                _window = window;
                _lastDraw = EditorApplication.timeSinceStartup;
                if (_window != null) _window.wantsMouseMove = true;
            }

            /// <summary> Repaints the hosting window (so a click/toggle shows immediately). </summary>
            public void RepaintHost()
            {
                if (_window != null) _window.Repaint();
            }

            public void EnsureSerialized()
            {
                if (selected == null) { selectedSO = null; return; }
                if (selectedSO == null || selectedSO.targetObject != selected)
                    selectedSO = new SerializedObject(selected);
            }

            public void Select(UIAnimationPreset preset)
            {
                if (selected == preset) return;
                RestorePreview();          // switching selection ends any live preview cleanly
                selected = preset;
                selectedSO = null;         // rebuilt lazily by EnsureSerialized
                RefreshRenameCheck(force: true);
            }

            // ------------------------------------------------------------------ preview lifecycle

            /// <summary> Plays <paramref name="preset"/> on <paramref name="target"/> as a MANUAL preview
            /// (button / replay). </summary>
            public void StartPreview(UIAnimationPreset preset, RectTransform target) =>
                StartPreview(preset, target, PreviewOwner.Manual);

            private void StartPreview(UIAnimationPreset preset, RectTransform target, PreviewOwner owner)
            {
                if (preset == null || target == null) return;
                RestorePreview();                       // put any prior preview back to rest first
                AnimationPreview.BeginPreview(target);  // snapshot pos/rot/scale/alpha for a clean revert
                preset.CopyTo(_preview);
                CanvasGroup group = target.GetComponent<CanvasGroup>();
                if (group == null && _preview.fade.enabled) group = target.gameObject.AddComponent<CanvasGroup>();
                _preview.SetTarget(target, group);
                _preview.CaptureStartValues();          // target is at rest here — captures color/start endpoints
                _preview.onFinish = null;
                _preview.Play();
                _previewTarget = target;
                _previewPreset = preset;
                _owner = owner;
                _scrubbing = false;
                EnsureTicking();
            }

            /// <summary>
            /// Statically poses <paramref name="preset"/> on <paramref name="target"/> at
            /// <paramref name="progress"/> (0..1) WITHOUT playing — the After-Effects-lite scrub. Routes
            /// through the SAME scratch/snapshot machinery as the play preview (never the asset, so a scrub
            /// can't dirty it), so Stop restores playback AND scrub sessions identically. A scrub is a
            /// MANUAL preview — hover-leave won't tear it down.
            /// </summary>
            public void ScrubTo(UIAnimationPreset preset, RectTransform target, float progress)
            {
                if (preset == null || target == null) return;
                progress = Mathf.Clamp01(progress);
                // (Re)prepare the scratch when this is a fresh scrub session or the target/preset changed.
                if (!_scrubbing || _previewTarget != target || _previewPreset != preset)
                {
                    RestorePreview();                       // end any play/hover preview or stale scrub first
                    AnimationPreview.BeginPreview(target);
                    preset.CopyTo(_preview);
                    CanvasGroup group = target.GetComponent<CanvasGroup>();
                    if (group == null && _preview.fade.enabled) group = target.gameObject.AddComponent<CanvasGroup>();
                    _preview.SetTarget(target, group);
                    _preview.CaptureStartValues();          // capture rest BEFORE we pose to progress
                    _preview.onFinish = null;
                    _previewTarget = target;
                    _previewPreset = preset;
                    _owner = PreviewOwner.Manual;
                    _scrubbing = true;
                    EnsureTicking();                        // so the idle-orphan restore covers scrubs too
                }
                _scrubProgress = progress;
                _preview.SetProgressAt(progress);           // a pose holds without a per-frame tick
                SceneView.RepaintAll();
                RepaintHost();
            }

            // Symmetric with AnimationPresetBrowserPopup.StopPreview: settle the scratch channels (incl.
            // color, which the transform snapshot doesn't cover) then hand the RectTransform back. Ticking
            // is left to self-manage — a pending hover may still need the heartbeat; StopTickingIfIdle
            // (end of Tick) / Dispose drop the subscription once nothing needs it.
            public void RestorePreview()
            {
                if (_previewTarget == null)
                { _owner = PreviewOwner.None; _previewPreset = null; _scrubbing = false; return; }
                _preview.Stop(silent: true);
                _preview.RestoreStartValues();
                AnimationPreview.EndPreview(_previewTarget);
                _previewTarget = null;
                _previewPreset = null;
                _owner = PreviewOwner.None;
                _scrubbing = false;
            }

            // ------------------------------------------------------------------ hover-dwell + heartbeat

            /// <summary> Records which preset row (if any) the mouse is over this frame. Call it after the
            /// browser row loop, only on a Repaint/MouseMove event (Layout carries a stale mouse position,
            /// same as <see cref="AnimationPresetBrowserPopup"/>). Entering a row arms the dwell heartbeat. </summary>
            public void SetHover(string fullName)
            {
                if (fullName == _hoverName) return;
                _hoverName = fullName;
                _hoverStart = EditorApplication.timeSinceStartup;
                if (_hoverName != null) EnsureTicking();
            }

            private void EnsureTicking() { if (!_subscribed) { EditorApplication.update += Tick; _subscribed = true; } }
            private void StopTicking()  { if (_subscribed)  { EditorApplication.update -= Tick; _subscribed = false; } }

            // Keep the heartbeat only while there's something to animate/detect: a live preview OR a
            // pending hover to dwell on. Otherwise drop it (no idle editor-tick, per CLAUDE.md).
            private void StopTickingIfIdle()
            {
                if (_previewTarget == null && _hoverName == null) StopTicking();
            }

            private void Tick()
            {
                // (a) Orphan cleanup: Motion.Draw refreshes _lastDraw every OnGUI; if it stops running
                // (tab switched away / window hidden) an in-flight preview is orphaned — put it back.
                if (_previewTarget != null && EditorApplication.timeSinceStartup - _lastDraw > PreviewIdleRestoreSeconds)
                { RestorePreview(); StopTickingIfIdle(); return; }

                // (b) Hover-dwell (parity with AnimationPresetBrowserPopup): mouse off every row drops a
                // hover-owned preview; dwelling on a new row past the threshold previews it through the ONE
                // preview path (superseding any prior preview). Resolution goes through the same seam as
                // the button/scrub — scene selection first, else the built-in stage — so dwell works with
                // nothing selected too; target is null only when the stage can't build (headless).
                if (_hoverName == null)
                {
                    if (_owner == PreviewOwner.Hover) RestorePreview();
                }
                else if (EditorApplication.timeSinceStartup - _hoverStart >= HoverDwellSeconds
                         && (_previewPreset == null || _previewPreset.fullName != _hoverName))
                {
                    UIAnimationPreset preset = AnimationPresetRegistry.GetByFullName(_hoverName);
                    RectTransform target = MotionTab.ResolvePreviewTarget(this);
                    if (preset != null && target != null) StartPreview(preset, target, PreviewOwner.Hover);
                }

                // Repaint ONLY while something is actually moving toward a change: a live preview
                // (UIAnimation.isActive — the same accessor Stop()/UIAnimator.isPlaying consult; it's false
                // once a one-shot preview's tweens finish AND for a scrub pose, since SetProgressAt
                // applies-and-stops rather than leaving a tween running) or a hover dwell that hasn't yet
                // resolved into its own preview (keeps the dwell-in-progress row ticking toward the
                // threshold between real MouseMove events, so arming a hover doesn't depend on us already
                // repainting). A held end pose / static scrub / idle browser burns zero repaints from here
                // until Stop / Replay / a new scrub / a new hover.
                bool hoverPending = _hoverName != null && (_previewPreset == null || _previewPreset.fullName != _hoverName);
                if (_window != null && ((_previewTarget != null && _preview.isActive) || hoverPending))
                    _window.Repaint();
                StopTickingIfIdle();
            }

            // ------------------------------------------------------------------ rename-to-convention check

            /// <summary> Recomputes whether the selected asset's filename matches the
            /// <c>{Category}_{Name}</c> convention. Cheap (one GetAssetPath + string compare), cached per
            /// selection; pass <paramref name="force"/> after the form reports an edit. </summary>
            public void RefreshRenameCheck(bool force)
            {
                if (!force && _renameCheckedFor == selected) return;
                _renameCheckedFor = selected;
                _renameNeeded = false;
                _renameToStem = null;
                if (selected == null) return;
                string path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path)) return;
                string current = System.IO.Path.GetFileNameWithoutExtension(path);
                string convention = $"{selected.category}_{selected.presetName}";
                if (!string.Equals(current, convention, StringComparison.Ordinal))
                { _renameNeeded = true; _renameToStem = convention; }
            }

            // RestorePreview BEFORE the stage teardown: a live preview may be posing the stage card, and
            // restore must run while its RectTransform still exists (the stage then destroys it cleanly).
            public void Dispose()
            {
                RestorePreview();
                StopTicking();
                stage?.Dispose();
                stage = null;
            }
        }

        internal static object CreateState()
        {
            var s = new State();
            s.LoadWidth(LeftWidthKey, DefaultLeftWidth);
            return s;
        }

        // ---------------------------------------------------------------- preview-target seam
        //
        // THE ONE place that answers "which RectTransform do Motion-tab previews (button / hover-dwell /
        // scrub) play on". A valid scene-selection RectTransform WINS — previewing in real context beats
        // a synthetic card; otherwise the built-in offscreen stage's dummy card is the target (created
        // lazily here, cached on State, presented by DrawPreviewSurface). Returns null only when the
        // stage can't build (should not happen with a live editor).
        internal static RectTransform ResolvePreviewTarget(State s)
        {
            RectTransform selection = SceneSelectionTarget();
            if (selection != null) return selection;
            s.stage ??= new MotionPreviewStage();
            return s.stage.Target;
        }

        // The scene-selection half of the seam, exposed separately so the right pane can tell WHICH
        // target a preview would play on (selection ⇒ hint line; stage ⇒ live viewport) without
        // side-effecting the lazy stage creation.
        private static RectTransform SceneSelectionTarget() =>
            Selection.activeGameObject != null
                ? Selection.activeGameObject.transform as RectTransform
                : null;

        internal static void Draw(DesignSystemTabContext ctx)
        {
            var s = ctx.State<State>();
            s.MarkDrawn(ctx.window);
            NeoUISettings settings = ctx.settings;

            IReadOnlyList<UIAnimationPreset> all = AnimationPresetRegistry.All;

            // Zero-preset onboarding stays full-width (there's nothing to edit yet, so no split): the role
            // defaults plus a bootstrap help box that seeds the curated ~46-preset library.
            if (all.Count == 0)
            {
                DrawRoleDefaults(settings);
                NeoGUI.Splitter();
                EditorGUILayout.LabelField("Preset library", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("No animation presets found yet. Seed the curated ~46-preset " +
                    "library with the button below, or drop your own UIAnimationPreset asset (right-click " +
                    "in Project → Create → Neo UI → Animation Preset).", MessageType.Info);
                if (GUILayout.Button(new GUIContent("Create or Repair Animation Library",
                        "Seed/repair the curated preset library the role dropdowns and this browser use")))
                {
                    int n = AnimationLibraryBootstrap.CreateOrRepair();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[Neo.UI] Animation library: {n} preset(s) created.");
                    s.model = null; // force a rebuild against the freshly-seeded set
                }
                return;
            }

            EnsureModel(s, all);

            // Dual-pane master–detail: browse/author on the LEFT, edit the selection on the RIGHT. The
            // separator between them is a drag handle (DesignSystemGUI.EndSplitLeft); the dragged width
            // rides s.leftWidth and is persisted below.
            using (DesignSystemGUI.BeginSplitPane(ctx.window))
            {
                DesignSystemGUI.BeginSplitLeft(ref s.leftScroll, ref s.leftWidth, LeftMinWidth, RightMinWidth);
                DrawLeftPane(s, settings, all);
                DesignSystemGUI.EndSplitLeft(ref s.leftWidth, LeftMinWidth, RightMinWidth);

                DesignSystemGUI.BeginSplitRight(ref s.rightScroll);
                DrawRightPane(s, settings);
                DesignSystemGUI.EndSplitRight();
            }
            s.PersistWidth(LeftWidthKey);
        }

        // ---------------------------------------------------------------- LEFT pane (master)

        private static void DrawLeftPane(State s, NeoUISettings settings, IReadOnlyList<UIAnimationPreset> all)
        {
            DrawRoleDefaults(settings);      // collapsible so it never crowds the browser
            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Preset library", EditorStyles.boldLabel);
            DrawSearch(s);
            DrawBrowser(s);
            NeoGUI.Splitter();
            DrawNewPreset(s, all);
        }

        // Role defaults, now a compact collapsible form (narrow master column).
        private static void DrawRoleDefaults(NeoUISettings settings)
        {
            if (NeoGUI.BeginFoldoutSection("NeoUI.DesignSystem.Motion.Roles",
                    "Default motion per animator role", defaultOpen: true))
            {
                EditorGUILayout.LabelField("Copied into new animator components and generated buttons/views. " +
                    "Pick a preset per role (hover a row to preview it live), or clear (✕) to keep the " +
                    "built-in feel.", EditorStyles.wordWrappedMiniLabel);
                foreach (NeoAnimatorRole role in NeoAnimatorRoles.All)
                    DrawMotionRole(settings, role);
            }
            NeoGUI.EndFoldoutSection();
        }

        // The shared browser row (grouped presets + search + hover-dwell live preview) — parity with the
        // animator inspectors' Preset row and the Setup wizard. Only the scene selection is passed as the
        // target; with none, the popup previews on its OWN stage in an inline viewport (never this tab's
        // stage — a popup can open over the right pane, hiding it). Label narrower than the old
        // full-width tab: the master column is only 260px (tooltip carries the role name + description).
        private static void DrawMotionRole(NeoUISettings settings, NeoAnimatorRole role)
        {
            settings.TryGetDefaultAnimation(role.Id, out UIAnimationPreset current);
            AnimationPresetBrowserPopup.DrawRoleRow(role.Id, current != null ? current.fullName : null,
                104f, SceneSelectionTarget,
                chosen =>
                {
                    Undo.RecordObject(settings, chosen != null ? "Set motion default" : "Clear motion default");
                    settings.SetDefaultAnimation(role.Id, chosen);
                    EditorUtility.SetDirty(settings);
                });
        }

        // Rebuild the browser model only when the discovered set changes (create/delete) — never per OnGUI.
        private static void EnsureModel(State s, IReadOnlyList<UIAnimationPreset> all)
        {
            if (s.model != null && s.builtForCount == all.Count) return;
            AnimationPresetBrowserModel previous = s.model;
            // No animator role in the tab context → no suggested categories (default: everything expanded).
            // Force the selected preset's own category open so it stays visible after a rebuild.
            s.model = new AnimationPresetBrowserModel(all, null, s.selected != null ? s.selected.fullName : null);
            s.model.CopyExpansionFrom(previous); // preserve the user's fold state across create/delete
            s.builtForCount = all.Count;
        }

        private static void DrawSearch(State s)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(52f));
                s.filter = EditorGUILayout.TextField(s.filter ?? "");
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(s.filter)))
                    if (GUILayout.Button(new GUIContent("✕", "Clear the search"), GUILayout.Width(22f)))
                    { s.filter = ""; GUI.FocusControl(null); }
            }
        }

        private static void DrawBrowser(State s)
        {
            IReadOnlyList<AnimationPresetBrowserModel.Row> rows = s.model.BuildRows(s.filter);
            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("No presets match the search.", EditorStyles.miniLabel);
                s.SetHover(null); // no rows → nothing hovered (tears down a hover preview cleanly)
                return;
            }

            Event e = Event.current;
            string hovered = null;
            foreach (AnimationPresetBrowserModel.Row row in rows)
            {
                if (row.header != null) { DrawHeaderRow(s, row.header); continue; }
                Rect rect = DrawPresetRow(s, row.preset);
                if (rect.Contains(e.mousePosition)) hovered = row.preset.fullName;
            }

            // Track hover only on paint/move — Layout carries a stale mouse position (parity with the popup).
            if (e.type == EventType.Repaint || e.type == EventType.MouseMove) s.SetHover(hovered);
        }

        private static void DrawHeaderRow(State s, AnimationPresetBrowserModel.Group group)
        {
            Event e = Event.current;
            bool searching = !string.IsNullOrEmpty(s.filter);
            bool open = searching || s.model.IsExpanded(group.category);
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, NeoColors.SectionBackground);
                GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height),
                    $"{(open ? "▾" : "▸")}  {group.category}  ({group.presets.Count})", EditorStyles.miniBoldLabel);
            }
            if (!searching && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                s.model.ToggleExpanded(group.category);
                e.Use();
                s.RepaintHost();
            }
        }

        // Returns the row rect so the caller can attribute hover (dwell preview) to it.
        private static Rect DrawPresetRow(State s, UIAnimationPreset preset)
        {
            Event e = Event.current;
            bool selected = preset == s.selected;
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            if (e.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(rect, NeoColors.Animation.WithAlpha(0.22f));
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), NeoColors.Animation);
                }
                float badgesWidth = s_badgeContents.Length * BadgeWidth;
                // Clip the label short of the badge strip so a long name can't paint over it.
                GUI.Label(new Rect(rect.x + 16f, rect.y, Mathf.Max(0f, rect.width - 20f - badgesWidth), rect.height),
                    new GUIContent(preset.presetName, preset.fullName), EditorStyles.label);
                DrawChannelBadges(new Rect(rect.xMax - badgesWidth - 2f, rect.y, badgesWidth, rect.height), preset.animation);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                s.Select(preset);
                e.Use();
                s.RepaintHost();
            }
            return rect;
        }

        // ---------------------------------------------------------------- channel badges (item 2)
        //
        // Right-aligned "M R S F C" letters — the same channel letters AnimationPreviewEditor's lanes use
        // (Move/Rotate/Scale/Fade/Color). Lit when that channel is enabled on the preset, dim otherwise.
        // Repaint-only, zero per-frame allocations: the GUIContents and style are cached statically and the
        // per-badge colour rides GUI.contentColor (no style mutation, no click handling stolen).

        private const float BadgeWidth = 11f;

        private static readonly GUIContent[] s_badgeContents =
        {
            new GUIContent("M", "Move channel"),
            new GUIContent("R", "Rotate channel"),
            new GUIContent("S", "Scale channel"),
            new GUIContent("F", "Fade channel"),
            new GUIContent("C", "Color channel"),
        };

        private static GUIStyle s_badgeStyle;
        private static GUIStyle BadgeStyle => s_badgeStyle ?? (s_badgeStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        });

        private static void DrawChannelBadges(Rect area, UIAnimation anim)
        {
            if (anim == null) return;
            DrawBadge(area, 0, anim.move.enabled);
            DrawBadge(area, 1, anim.rotate.enabled);
            DrawBadge(area, 2, anim.scale.enabled);
            DrawBadge(area, 3, anim.fade.enabled);
            DrawBadge(area, 4, anim.color.enabled);
        }

        private static void DrawBadge(Rect area, int index, bool enabled)
        {
            Color prev = GUI.contentColor;
            GUI.contentColor = enabled ? NeoColors.Animation : NeoColors.TextDim.WithAlpha(0.5f);
            GUI.Label(new Rect(area.x + index * BadgeWidth, area.y, BadgeWidth, area.height),
                s_badgeContents[index], BadgeStyle);
            GUI.contentColor = prev;
        }

        // ---------------------------------------------------------------- new preset

        private static void DrawNewPreset(State s, IReadOnlyList<UIAnimationPreset> all)
        {
            EditorGUILayout.LabelField("New preset", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Category", GUILayout.Width(60f));
                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                // Categories from whatever the discovered assets declare (never hardcoded) + free-text "+ Add".
                NeoDropdown.ValuePopup(rect, s.newCategory,
                    () => all.Where(p => p != null)
                             .Select(p => string.IsNullOrEmpty(p.category) ? "Custom" : p.category)
                             .Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList(),
                    chosen => s.newCategory = chosen,
                    emptyLabel: "(pick or add)",
                    onAddNew: value => s.newCategory = value);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Name", GUILayout.Width(60f));
                s.newName = EditorGUILayout.TextField(s.newName ?? "");
                using (new EditorGUI.DisabledScope(
                           string.IsNullOrWhiteSpace(s.newName) || string.IsNullOrWhiteSpace(s.newCategory)))
                    if (GUILayout.Button("Create", GUILayout.Width(70f)))
                        CreatePreset(s);
            }

            if (GUILayout.Button(new GUIContent("Create or Repair Animation Library",
                    "Seed any missing curated built-in presets (never clobbers your edits)")))
            {
                int n = AnimationLibraryBootstrap.CreateOrRepair();
                AssetDatabase.SaveAssets();
                Debug.Log($"[Neo.UI] Animation library: {n} preset(s) created.");
                s.model = null; // rebuild to include any newly-seeded presets
            }
        }

        private static void CreatePreset(State s)
        {
            string category = s.newCategory.Trim();
            string name = s.newName.Trim();
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(name)) return;

            DesignSystemGUI.EnsureFolder(AnimationLibraryBootstrap.LibraryRoot);
            // Same "{Category}_{Name}.asset" convention AnimationLibraryBootstrap seeds under.
            string path = $"{AnimationLibraryBootstrap.LibraryRoot}/{category}_{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<UIAnimationPreset>(path) != null)
            {
                Debug.LogWarning($"[Neo.UI] An animation preset already exists at '{path}'.");
                return;
            }

            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = category;
            preset.presetName = name;
            preset.animation = new UIAnimation();
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery(); // matches the bootstrap's own creation path

            s.model = null;    // rebuild the browser to include the new asset
            s.newName = "";
            s.Select(preset);
            Debug.Log($"[Neo.UI] Created animation preset '{preset.fullName}' at '{path}'.");
        }

        // ---------------------------------------------------------------- RIGHT pane (detail)

        private static void DrawRightPane(State s, NeoUISettings settings)
        {
            if (s.selected == null) { DrawEmptyState(s); return; }
            DrawSelectedEditor(s, settings);
        }

        private static GUIStyle s_emptyStateStyle;

        private static void DrawEmptyState(State s)
        {
            s_emptyStateStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                "Select a preset on the left to edit it —\nor create a new one below the browser.",
                s_emptyStateStyle);
            // Hover-dwell previews work with NO preset selected too — when one is live on the stage
            // (nothing scene-selected), show the viewport here so the motion isn't playing invisibly
            // offscreen. Reads the raw field (never creates the stage just to draw an empty state).
            if (s.stage != null && s.Previewing && s.PreviewTarget == s.stage.Target)
                DrawStageViewport(s);
            GUILayout.FlexibleSpace();
        }

        private static void DrawSelectedEditor(State s, NeoUISettings settings)
        {
            s.EnsureSerialized();
            if (s.selectedSO == null || s.selectedSO.targetObject == null)
            {
                // The asset was deleted out from under us (elsewhere) — drop the selection.
                s.Select(null);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Editing: {s.selected.fullName}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Ping", "Select this asset in the Project window"),
                        EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
                    EditorGUIUtility.PingObject(s.selected);
                if (GUILayout.Button(new GUIContent("Duplicate", "Copy this preset to a new asset in the same folder"),
                        EditorStyles.miniButtonMid, GUILayout.Width(72f)))
                { DuplicateSelected(s); return; }
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.82f, 0.35f, 0.35f);
                bool delete = GUILayout.Button(new GUIContent("Delete", "Delete this preset asset"),
                    EditorStyles.miniButtonRight, GUILayout.Width(58f));
                GUI.backgroundColor = prev;
                if (delete) { DeleteSelected(s, settings); return; }
            }

            // The one edit transaction: caller owns Update/ApplyModifiedProperties; ApplyModifiedProperties
            // records Undo and dirties the asset (SaveAssets from the window footer persists) — the same
            // semantics as the UIAnimationPresetEditor inspector consumer.
            s.selectedSO.Update();
            AnimationPresetGUI.Draw(s.selectedSO, "NeoUI.DesignSystem.Motion");
            bool changed = s.selectedSO.ApplyModifiedProperties();

            // Category/name edits desync the asset filename from the {Category}_{Name} convention — offer a
            // one-click rename. Cheap cached check; re-run only when the form actually changed something.
            s.RefreshRenameCheck(force: changed);
            if (s.RenameNeeded) DrawRenameNotice(s);

            GUILayout.Space(NeoGUI.Spacing);
            DrawPreviewSurface(s);
            DrawScrubStrip(s);
            DrawPreviewControls(s);
        }

        // ---------------------------------------------------------------- preview surface (stage viewport)

        /// <summary>
        /// The right pane's preview surface, sitting directly above the scrub strip + controls: when a
        /// scene selection is the preview target, a one-line hint (previews play in the scene — same
        /// affordance style as the rest of the strip); otherwise the built-in stage's live viewport (the
        /// stage IS or WOULD BE the target, so what the buttons/scrub drive is always visible).
        /// </summary>
        private static void DrawPreviewSurface(State s)
        {
            RectTransform selection = SceneSelectionTarget();
            if (selection != null)
            {
                // Scene selection wins (see ResolvePreviewTarget) — no stage viewport, just say where.
                EditorGUILayout.LabelField(
                    new GUIContent($"Previews play on the scene selection ‘{selection.name}’.",
                        "Clear the selection to preview on the built-in stage instead."),
                    EditorStyles.miniLabel);
                return;
            }
            s.stage ??= new MotionPreviewStage(); // the viewport is the stage's on-ramp: create it here
            DrawStageViewport(s);
        }

        // The stage's shared viewport (MotionPreviewStage.DrawViewport — chrome + render policy live
        // there, also used by the preset browser popup's inline fallback). Live only while this tab's
        // OWN preview machinery poses the card (button/hover/scrub); the preview Tick already repaints
        // the window while a preview is live, so no extra update subscription.
        private static void DrawStageViewport(State s)
        {
            MotionPreviewStage stage = s.stage;
            if (stage == null) return;
            stage.DrawViewport(s.Previewing && s.PreviewTarget == stage.Target);
        }

        // ---------------------------------------------------------------- rename-to-convention (item 7)

        private static void DrawRenameNotice(State s)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent($"⚠ Filename ≠ ‘{s.RenameToStem}.asset’ convention.",
                        "The library names assets {Category}_{Name}.asset; editing the category/name above " +
                        "drifted the filename. Rename keeps it in sync (never changes the preset's Category/Name)."),
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(new GUIContent("Rename file", $"Rename this asset to ‘{s.RenameToStem}.asset’."),
                        EditorStyles.miniButton, GUILayout.Width(84f)))
                    RenameToConvention(s);
            }
        }

        private static void RenameToConvention(State s)
        {
            UIAnimationPreset preset = s.selected;
            if (preset == null) return;
            string path = AssetDatabase.GetAssetPath(preset);
            if (string.IsNullOrEmpty(path)) return;

            string dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string target = $"{dir}/{s.RenameToStem}.asset";
            // Never clobber: if something already lives at the convention path, warn and no-op.
            if (AssetDatabase.LoadAssetAtPath<UIAnimationPreset>(target) != null)
            {
                Debug.LogWarning($"[Neo.UI] Can't rename to '{s.RenameToStem}.asset' — an asset already exists there.");
                return;
            }

            string err = AssetDatabase.RenameAsset(path, s.RenameToStem);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogWarning($"[Neo.UI] Rename failed: {err}");
                return;
            }
            AssetDatabase.SaveAssets();
            s.RefreshRenameCheck(force: true); // clears the notice (fullName is unchanged, so no model rebuild)
            Debug.Log($"[Neo.UI] Renamed animation preset asset to '{s.RenameToStem}.asset'.");
        }

        // ---------------------------------------------------------------- scrub + lanes strip (item 4)

        private static void DrawScrubStrip(State s)
        {
            // Never null with a live editor: the stage is the fallback target (ResolvePreviewTarget).
            // Null only if the stage couldn't build (headless) — keep the guard so the slider is inert.
            RectTransform target = ResolvePreviewTarget(s);
            UIAnimation anim = s.selected != null ? s.selected.animation : null;
            bool disabled = target == null || anim == null || !anim.hasEnabledChannels;
            float total = anim != null ? anim.totalDuration : 0f;
            float shown = s.Scrubbing ? s.ScrubProgress : 0f;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent("Scrub",
                    target == null ? "Scrubbing needs a preview target (no graphics device for the stage)."
                    : disabled ? "Enable at least one channel to scrub."
                    : "Drag to pose the preset at any point in time — stays posed until Stop or a new scrub. " +
                      "Poses the scene selection when one is selected, else the built-in stage."),
                    GUILayout.Width(38f));

                using (new EditorGUI.DisabledScope(disabled))
                {
                    EditorGUI.BeginChangeCheck();
                    float next = GUILayout.HorizontalSlider(shown, 0f, 1f);
                    if (EditorGUI.EndChangeCheck() && !disabled) s.ScrubTo(s.selected, target, next);
                }

                GUILayout.Label($"{shown * total:0.00}s / {total:0.00}s", EditorStyles.miniLabel, GUILayout.Width(90f));
            }

            // Read-only channel lanes — the identical M/R/S/F/C strip the animator inspectors draw, via the
            // shared AnimationPreview.DrawChannelLanes overload factored out for hosts that own their preview
            // lifecycle. The playhead follows our scrub session.
            AnimationPreview.DrawChannelLanes(anim, shown, s.Scrubbing);
        }

        // ---------------------------------------------------------------- preview controls

        private static void DrawPreviewControls(State s)
        {
            RectTransform target = ResolvePreviewTarget(s);

            // The resolved target moved off the object we're previewing (selection changed, or was
            // cleared/made so the stage takes over) → restore before the preview drifts out of sight.
            // Only for a preview of the SELECTED preset (a hover preview of another row is transient and
            // Tick already reconciles it).
            if (s.PreviewingSelected && target != s.PreviewTarget) s.RestorePreview();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (s.PreviewingSelected)
                {
                    if (GUILayout.Button(new GUIContent("▶ Replay", "Restart the preview from the object's rest state."),
                            EditorStyles.miniButtonLeft))
                        s.StartPreview(s.selected, s.PreviewTarget);
                    if (GUILayout.Button(new GUIContent("■ Stop", "Restore the object to its original state (ends playback and scrub)."),
                            EditorStyles.miniButtonRight))
                        s.RestorePreview();
                }
                else
                {
                    // Always available: the stage is the fallback target, so a valid target only ever
                    // misses when the stage can't build (headless — keep the guard, never a dead click).
                    using (new EditorGUI.DisabledScope(target == null))
                    {
                        RectTransform selection = SceneSelectionTarget();
                        var content = new GUIContent(
                            selection != null ? "▶ Preview on selection" : "▶ Preview",
                            selection != null
                                ? $"Play this preset on '{selection.name}'."
                                : "Play this preset on the built-in preview stage — select a UI object " +
                                  "in the scene to preview in real context instead.");
                        if (GUILayout.Button(content, EditorStyles.miniButton))
                            s.StartPreview(s.selected, target);
                    }
                }
            }
        }

        private static void DuplicateSelected(State s)
        {
            UIAnimationPreset src = s.selected;
            if (src == null) return;

            s.RestorePreview(); // don't leave a preview pointing at the selection we're about to swap

            string srcPath = AssetDatabase.GetAssetPath(src);
            string folder = string.IsNullOrEmpty(srcPath)
                ? AnimationLibraryBootstrap.LibraryRoot
                : System.IO.Path.GetDirectoryName(srcPath).Replace('\\', '/');
            DesignSystemGUI.EnsureFolder(folder);

            // Keep the {Category}_{Name}.asset convention; GenerateUniqueAssetPath appends " 1"/" 2"… to the
            // stem on collision (so "… Copy", then "… Copy 1", …). Derive the new presetName back from the
            // final stem so name and filename stay in sync (item 7's convention holds for the duplicate too).
            string basePath = $"{folder}/{src.category}_{src.presetName} Copy.asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(basePath);
            string stem = System.IO.Path.GetFileNameWithoutExtension(path);
            string prefix = src.category + "_";
            string newName = stem.StartsWith(prefix, StringComparison.Ordinal)
                ? stem.Substring(prefix.Length)
                : src.presetName + " Copy";

            var dup = ScriptableObject.CreateInstance<UIAnimationPreset>();
            dup.category = src.category;
            dup.presetName = newName;
            dup.animation = new UIAnimation();
            src.CopyTo(dup.animation);         // deep-copies all five channels (the sanctioned copy path)
            dup.animation.sourcePreset = null; // a duplicated PRESET is its own source, not seeded from another

            AssetDatabase.CreateAsset(dup, path);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();

            s.model = null;   // rebuild the browser to include the duplicate
            s.Select(dup);    // select it (RestorePreview + rename-check refresh handled inside Select)
            Debug.Log($"[Neo.UI] Duplicated animation preset to '{dup.fullName}' at '{path}'.");
        }

        private static void DeleteSelected(State s, NeoUISettings settings)
        {
            UIAnimationPreset preset = s.selected;
            if (preset == null) return;
            string path = AssetDatabase.GetAssetPath(preset);
            if (!EditorUtility.DisplayDialog("Delete Animation Preset",
                    $"Delete '{preset.fullName}'?\n\n{path}\n\nThis cannot be undone.", "Delete", "Cancel"))
                return;

            s.RestorePreview();

            // No-silent-failure: if any motion-role default points at this preset, clear it (undo-recorded)
            // and say which roles were cleared — the default would otherwise dangle at a deleted asset.
            var clearedRoles = new List<string>();
            if (settings != null && settings.animatorDefaults != null)
            {
                foreach (NeoUISettings.AnimatorDefault entry in settings.animatorDefaults)
                    if (entry != null && entry.preset == preset && !string.IsNullOrEmpty(entry.role))
                        clearedRoles.Add(entry.role);
                if (clearedRoles.Count > 0)
                {
                    Undo.RecordObject(settings, "Clear motion defaults for deleted preset");
                    foreach (string role in clearedRoles) settings.SetDefaultAnimation(role, null);
                    EditorUtility.SetDirty(settings);
                }
            }

            string fullName = preset.fullName;
            s.Select(null);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
            AnimationPresetRegistry.InvalidateDiscovery();
            s.model = null; // rebuild the browser without the deleted asset

            if (clearedRoles.Count > 0)
                Debug.Log($"[Neo.UI] Deleted animation preset '{fullName}'. Cleared it from motion default " +
                          $"role(s): {string.Join(", ", clearedRoles)}.");
            else
                Debug.Log($"[Neo.UI] Deleted animation preset '{fullName}'.");
        }
    }
}
