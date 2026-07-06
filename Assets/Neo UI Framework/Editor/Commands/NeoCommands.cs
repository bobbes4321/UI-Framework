using System;
using System.Collections.Generic;
using Neo.UI.Editor.Authoring;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One entry in the Ctrl-K command palette's vocabulary — an id, a display label, the category it
    /// groups under, an optional visibility gate (context-sensitive commands like "Connect selected
    /// button to view…" only show up when the gate passes) and the action it runs. Mirrors
    /// <see cref="FlowNodeKinds"/>'s <see cref="FlowNodeDescriptor"/> shape.
    /// </summary>
    public readonly struct NeoCommandDescriptor
    {
        /// <summary> Stable registry key, e.g. "connect-button". Not shown to the user. </summary>
        public readonly string id;

        /// <summary> Text shown in the palette row. </summary>
        public readonly string label;

        /// <summary> Category the row groups under in the palette ("Wiring" / "Create" / "Navigate" /
        /// "Tools" / a project's own bucket — free-form, a new one just appears as its own group). </summary>
        public readonly string category;

        /// <summary> Optional context gate — the command is hidden from the palette when this returns
        /// false. Null means always visible. Re-evaluated whenever the palette opens or the editor
        /// selection changes, never per keystroke. </summary>
        public readonly Func<bool> visible;

        /// <summary> Executes the command. May run immediately (e.g. open a window) or push a follow-up
        /// argument page onto the open palette via <see cref="NeoCommandPaletteWindow.PushPage"/> (e.g.
        /// "Connect selected button to view…" lists views to pick from). </summary>
        public readonly Action run;

        /// <summary> Extra terms the palette's search matches against, alongside <see cref="label"/> and
        /// <see cref="category"/> (e.g. "wire"/"link" for the connect command). </summary>
        public readonly string[] searchKeywords;

        public NeoCommandDescriptor(string id, string label, string category, Action run,
            Func<bool> visible = null, string[] searchKeywords = null)
        {
            this.id = id;
            this.label = label;
            this.category = category;
            this.run = run;
            this.visible = visible;
            this.searchKeywords = searchKeywords ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// The single source of truth for the Ctrl-K command palette's (<see cref="NeoCommandPaletteWindow"/>)
    /// vocabulary — a <see cref="NeoKeyedRegistry{T}"/> (Pattern R, same shape as
    /// <see cref="FlowNodeKinds"/>/<see cref="NeoMenuItemKinds"/>). This is the extensibility seam: a
    /// consuming project registers its own editor command without forking the package, e.g. from an
    /// <c>[InitializeOnLoad]</c> static constructor:
    /// <code>
    /// NeoCommands.Register(new NeoCommandDescriptor(
    ///     "my-project.open-loot-table", "Open Loot Table Editor", "Tools",
    ///     run: LootTableWindow.Open));
    /// </code>
    /// </summary>
    public static class NeoCommands
    {
        private static readonly NeoKeyedRegistry<NeoCommandDescriptor> _registry =
            new NeoKeyedRegistry<NeoCommandDescriptor>(
                d => d.id,
                builtins: Builtins,
                validate: d => d.run != null,
                registryName: "NeoCommands");

        /// <summary> Every registered command, in registration order (built-ins first). Backs the
        /// Ctrl-K palette's root page. </summary>
        public static IReadOnlyList<NeoCommandDescriptor> All => _registry.All;

        /// <summary> Resolves a command by id. False (default) on miss. </summary>
        public static bool TryGet(string id, out NeoCommandDescriptor descriptor) => _registry.TryGet(id, out descriptor);

        /// <summary>
        /// Registers (or replaces, by id) a command. The extension seam: a consuming project calls this
        /// once to add its own row to the Ctrl-K palette without forking the window.
        /// </summary>
        public static void Register(NeoCommandDescriptor descriptor) => _registry.Register(descriptor);

        /// <summary> Test-only: clears project registrations and re-seeds the built-ins on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();

        // ------------------------------------------------------------------ built-ins

        private static IEnumerable<NeoCommandDescriptor> Builtins()
        {
            // ---- Wiring ------------------------------------------------------------------------
            yield return new NeoCommandDescriptor(
                "connect-button", "Connect selected button to view…", "Wiring",
                run: RunConnectButtonToView,
                visible: HasSelectedButton,
                searchKeywords: new[] { "wire", "flow", "navigate", "link", "connect to" });

            // ---- Create: one command per palette entry (widgets + discovered widget presets) --
            foreach (PaletteEntry entry in NeoWidgetPalette.All)
            {
                PaletteEntry captured = entry; // capture per-iteration for the closure below
                yield return new NeoCommandDescriptor(
                    $"create:{captured.kind}:{captured.label}", $"Create widget: {captured.label}", "Create",
                    run: () => NeoSceneAuthoring.CreateWidget(captured.kind, captured.preset, Selection.activeGameObject),
                    searchKeywords: new[] { captured.kind, captured.category, "add", "insert", "widget" });
            }

            // ---- Navigate ------------------------------------------------------------------------
            yield return new NeoCommandDescriptor(
                "goto-view", "Go to view…", "Navigate",
                run: RunGoToView,
                searchKeywords: new[] { "select", "find", "jump", "view", "focus" });

            // ---- Tools: open the package's own windows -------------------------------------------
            yield return new NeoCommandDescriptor("tool-flow-graph", "Open Flow Graph window", "Tools",
                run: () => RunMenuItem("Tools/Neo UI/Flow Graph Editor"),
                searchKeywords: new[] { "flow", "graph" });
            yield return new NeoCommandDescriptor("tool-design-system", "Open Design System", "Tools",
                run: () => RunMenuItem("Tools/Neo UI/Design System"),
                searchKeywords: new[] { "theme", "colors", "typography" });
            yield return new NeoCommandDescriptor("tool-hub", "Open Neo UI Hub", "Tools",
                run: () => RunMenuItem("Tools/Neo UI/Hub"),
                searchKeywords: new[] { "hub", "home" });
            yield return new NeoCommandDescriptor("tool-gallery", "Open Gallery", "Tools",
                run: () => RunMenuItem("Tools/Neo UI/Gallery"),
                searchKeywords: new[] { "gallery", "screenshots", "preview" });
        }

        private static void RunMenuItem(string menuPath)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath))
                Debug.LogWarning($"[Neo.UI] Command Palette couldn't run menu item '{menuPath}'.");
        }

        // ------------------------------------------------------------------ Wiring: connect button

        private static bool HasSelectedButton() =>
            Selection.activeGameObject != null && Selection.activeGameObject.GetComponentInParent<UIButton>(true) != null;

        private static void RunConnectButtonToView()
        {
            GameObject selected = Selection.activeGameObject;
            UIButton button = selected != null ? selected.GetComponentInParent<UIButton>(true) : null;
            if (button == null) return;

            UIView[] views = UnityEngine.Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (views.Length == 0)
            {
                Debug.LogWarning("[Neo.UI] Command Palette: no UIView found in the open scene(s) to connect to.");
                return;
            }

            var rows = new List<NeoCommandPaletteWindow.PaletteRow>(views.Length);
            foreach (UIView view in views)
            {
                UIView capturedView = view;
                rows.Add(new NeoCommandPaletteWindow.PaletteRow(
                    $"{capturedView.id.Category}/{capturedView.id.Name}",
                    () => RunConnect(button, capturedView),
                    sublabel: GameObjectPath(capturedView.gameObject)));
            }
            NeoCommandPaletteWindow.PushPage("Connect to view", rows);
        }

        private static void RunConnect(UIButton button, UIView target)
        {
            NeoFlowWiring.WiringResult result = NeoFlowWiring.ConnectButtonToView(button, target, "", true);
            if (!result.ok)
            {
                Debug.LogWarning($"[Neo.UI] Command Palette: connect failed — {result.error}. " +
                                  "Use the scene-view overlay's 'Connect to…' flow to disambiguate.");
                return;
            }
            Debug.Log($"[Neo.UI] Command Palette: connected '{button.id.Category}/{button.id.Name}' -> " +
                      $"'{target.id.Category}/{target.id.Name}'" + (result.alreadyExisted ? " (edge already existed)." : "."));
        }

        // ------------------------------------------------------------------ Navigate: go to view

        private static void RunGoToView()
        {
            UIView[] views = UnityEngine.Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (views.Length == 0)
            {
                Debug.LogWarning("[Neo.UI] Command Palette: no UIView found in the open scene(s).");
                return;
            }

            var rows = new List<NeoCommandPaletteWindow.PaletteRow>(views.Length);
            foreach (UIView view in views)
            {
                UIView captured = view;
                rows.Add(new NeoCommandPaletteWindow.PaletteRow(
                    $"{captured.id.Category}/{captured.id.Name}",
                    () =>
                    {
                        Selection.activeGameObject = captured.gameObject;
                        EditorGUIUtility.PingObject(captured.gameObject);
                        SceneView.lastActiveSceneView?.FrameSelected();
                    },
                    sublabel: GameObjectPath(captured.gameObject)));
            }
            NeoCommandPaletteWindow.PushPage("Go to view", rows);
        }

        private static string GameObjectPath(GameObject go)
        {
            if (go == null) return "";
            var sb = new System.Text.StringBuilder(go.name);
            Transform t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, "/").Insert(0, t.name);
                t = t.parent;
            }
            return sb.ToString();
        }
    }
}
