using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The design-system editor: one window to author the project's LOOK over the live
    /// <see cref="NeoUISettings"/> + <see cref="Theme"/> — Colors (theme tokens/variants), Buttons
    /// (per-state variant colors + sizes), Shapes (radius / outline / softness), Presets (named
    /// component styles like "Primary Button") and Motion (default animation preset per animator role).
    /// It edits the exact structures the widget factory already consults (`buttonVariants`, theme tokens,
    /// shape styles, <see cref="NeoWidgetPreset"/>, `animatorDefaults`), so changes flow straight into
    /// generated and native-built UI.
    /// <para>
    /// The tab set itself is a registry (<see cref="NeoDesignSystemTabs"/>) — the built-in five each live
    /// in their own file under <c>Editor/DesignSystem/</c>, and a consuming project adds its own tab via
    /// <see cref="NeoDesignSystemTabs.Register"/> without forking the package (the "extensible by design"
    /// hard constraint). This window is now just the shell: header, settings/theme gate, the
    /// registry-driven tab strip, and per-tab UI state it owns and disposes.
    /// </para>
    /// </summary>
    public sealed class NeoDesignSystemWindow : EditorWindow
    {
        private const string TabKey = "NeoUI.DesignSystem.Tab";

        private Vector2 _scroll;

        // Registry-driven tab set, cached per window (NOT rebuilt per OnGUI — projects register at load,
        // so the set is stable for the window's lifetime; a domain reload nulls these and rebuilds).
        private List<DesignSystemTabDescriptor> _orderedTabs;
        private string[] _tabTitles;

        // Each tab's own persistent UI state (browsed index, new-name fields, cached preview textures),
        // keyed by tab id — the state objects the tabs' createState factories produced.
        private Dictionary<string, object> _tabStates;

        // Reused context handed to the active tab each draw (kept a field so we don't allocate per OnGUI).
        private DesignSystemTabContext _ctx;

        [MenuItem("Tools/Neo UI/Design System", priority = 12)]
        public static void Open()
        {
            var w = GetWindow<NeoDesignSystemWindow>(false, "Neo UI — Design System");
            w.minSize = new Vector2(460f, 520f);
        }

        /// <summary>
        /// Jumps the window's persisted tab selection (<see cref="TabKey"/>, the same
        /// <see cref="SessionState"/> key <see cref="NeoGUI.Tabs"/> reads) to the tab with the given id —
        /// the "Jump" buttons the Overview tab's cards use. Resolves the index against
        /// <see cref="NeoDesignSystemTabs.Ordered"/> directly rather than a cached per-window list, so it
        /// works even before the window has drawn once. Silently no-ops on an unknown id (e.g. a sibling
        /// tab — such as "bundles" — that hasn't registered yet), and repaints an already-open window so
        /// the jump is visible immediately.
        /// </summary>
        internal static void OpenTab(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var ordered = NeoDesignSystemTabs.Ordered;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].id != id) continue;
                SessionState.SetInt(TabKey, i);
                if (HasOpenInstances<NeoDesignSystemWindow>()) GetWindow<NeoDesignSystemWindow>().Repaint();
                return;
            }
        }

        private void OnDisable()
        {
            // Route disposal through the state objects — the Buttons tab's state destroys its cached
            // preview Texture2D here (the old OnDisable behavior), any other IDisposable state too.
            if (_tabStates != null)
                foreach (object st in _tabStates.Values)
                    (st as IDisposable)?.Dispose();
            _tabStates = null;
            _orderedTabs = null;
            _tabTitles = null;
        }

        private void OnGUI()
        {
            NeoGUI.ComponentHeader("Design System", "Author your colors, buttons, shapes and presets",
                NeoColors.Theming);

            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            Theme theme = settings != null ? settings.theme : null;
            if (settings == null || theme == null)
            {
                EditorGUILayout.HelpBox("No settings/theme yet. Run New Project Setup first.", MessageType.Info);
                if (GUILayout.Button("Open New Project Setup")) NeoSetupWizard.Open();
                return;
            }

            EnsureTabs();
            if (_orderedTabs.Count == 0) { EditorGUILayout.LabelField("No design-system tabs registered."); return; }

            // The persisted tab index maps into the registry's ORDERED list — stable even when a project
            // registers extra tabs, because ordering is by each descriptor's `order` (ties keep
            // registration order), not by however the registry happened to enumerate.
            int tab = Mathf.Clamp(NeoGUI.Tabs(TabKey, _tabTitles), 0, _orderedTabs.Count - 1);
            NeoGUI.Splitter();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DesignSystemTabDescriptor desc = _orderedTabs[tab];
            _ctx.settings = settings;
            _ctx.theme = theme;
            _ctx.window = this;
            _ctx.state = _tabStates.TryGetValue(desc.id, out object state) ? state : null;
            desc.draw?.Invoke(_ctx);
            EditorGUILayout.EndScrollView();

            NeoGUI.Splitter();
            if (GUILayout.Button("Save Assets")) AssetDatabase.SaveAssets();
        }

        // Builds the ordered tab list, titles and per-tab state once per window (lazily).
        private void EnsureTabs()
        {
            if (_orderedTabs != null) return;
            _orderedTabs = new List<DesignSystemTabDescriptor>(NeoDesignSystemTabs.Ordered);
            _tabTitles = new string[_orderedTabs.Count];
            _tabStates = new Dictionary<string, object>(_orderedTabs.Count);
            for (int i = 0; i < _orderedTabs.Count; i++)
            {
                DesignSystemTabDescriptor d = _orderedTabs[i];
                _tabTitles[i] = d.title;
                _tabStates[d.id] = d.createState?.Invoke();
            }
            _ctx = new DesignSystemTabContext();
        }

        // ============================================================ theme state derivation
        // Kept on the window (not moved into ColorsTab) so DesignSystemThemeFixTests keeps its call
        // contract — the Colors tab's "Re-derive states" button calls DeriveStates.

        internal static void DeriveStates(Theme theme)
        {
            Undo.RecordObject(theme, "Derive states");
            DerivePair(theme, UIWidgetFactory.TokenPrimary, UIWidgetFactory.TokenPrimaryHover, UIWidgetFactory.TokenPrimaryPressed);
            DerivePair(theme, UIWidgetFactory.TokenSuccess, UIWidgetFactory.TokenSuccessHover, UIWidgetFactory.TokenSuccessPressed);
            DerivePair(theme, UIWidgetFactory.TokenDanger, UIWidgetFactory.TokenDangerHover, UIWidgetFactory.TokenDangerPressed);
            EditorUtility.SetDirty(theme);
        }

        // Derive hover/pressed PER VARIANT from each variant's OWN base color, writing back into only
        // that variant (B2) — so Dark-derived states never overwrite Light's (and vice versa).
        internal static void DerivePair(Theme theme, string baseToken, string hover, string pressed)
        {
            if (!theme.HasToken(baseToken)) return;
            foreach (Theme.ThemeVariant variant in theme.Variants)
            {
                if (!theme.TryGetColor(baseToken, out Color b, variant.name)) continue;
                theme.SetToken(hover, ColorUtils.DeriveHover(b), variant.name);
                theme.SetToken(pressed, ColorUtils.DerivePressed(b), variant.name);
            }
        }
    }
}
