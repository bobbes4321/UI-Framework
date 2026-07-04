using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// Native <c>GameObject → Neo UI → …</c> creation menu — the familiar Unity way to add UI, mirroring
    /// the built-in <c>GameObject → UI</c> commands. Explicit items cover the common widgets (discoverable,
    /// hotkey-able, grouped by submenu); "More Widgets…" lists the full <see cref="NeoWidgetPalette"/> set
    /// (the long tail PLUS any project-registered <see cref="NeoElementKinds"/> kind) via a popup, since
    /// Unity can't synthesize <c>[MenuItem]</c>s at runtime; "Insert Template…" lists every
    /// <see cref="NeoLayoutTemplates"/> scaffold via the same popup pattern. Every entry calls
    /// <see cref="NeoSceneAuthoring"/>, so creation behaviour lives in one place and parents into the
    /// right-clicked object when there is one.
    /// </summary>
    internal static class NeoCreateMenu
    {
        private const string Root = "GameObject/Neo UI/";

        private static GameObject Parent(MenuCommand cmd) =>
            (cmd?.context as GameObject) ?? Selection.activeGameObject;

        // --- View root (priority sits it above the widget groups) ---
        [MenuItem(Root + "View", false, 10)]
        private static void CreateView(MenuCommand _) => NeoSceneAuthoring.CreateView();

        // --- Input ---
        [MenuItem(Root + "Input/Button", false, 20)]
        private static void Button(MenuCommand c) => NeoSceneAuthoring.CreateWidget("button", Parent(c));
        [MenuItem(Root + "Input/Toggle", false, 21)]
        private static void Toggle(MenuCommand c) => NeoSceneAuthoring.CreateWidget("toggle", Parent(c));
        [MenuItem(Root + "Input/Switch", false, 22)]
        private static void Switch(MenuCommand c) => NeoSceneAuthoring.CreateWidget("switch", Parent(c));
        [MenuItem(Root + "Input/Slider", false, 23)]
        private static void Slider(MenuCommand c) => NeoSceneAuthoring.CreateWidget("slider", Parent(c));
        [MenuItem(Root + "Input/Stepper", false, 24)]
        private static void Stepper(MenuCommand c) => NeoSceneAuthoring.CreateWidget("stepper", Parent(c));
        [MenuItem(Root + "Input/Input Field", false, 25)]
        private static void Input(MenuCommand c) => NeoSceneAuthoring.CreateWidget("input", Parent(c));
        [MenuItem(Root + "Input/Dropdown", false, 26)]
        private static void Dropdown(MenuCommand c) => NeoSceneAuthoring.CreateWidget("dropdown", Parent(c));

        // --- Layout ---
        [MenuItem(Root + "Layout/Vertical Stack", false, 40)]
        private static void VStack(MenuCommand c) => NeoSceneAuthoring.CreateWidget("vstack", Parent(c));
        [MenuItem(Root + "Layout/Horizontal Stack", false, 41)]
        private static void HStack(MenuCommand c) => NeoSceneAuthoring.CreateWidget("hstack", Parent(c));
        [MenuItem(Root + "Layout/Grid", false, 42)]
        private static void Grid(MenuCommand c) => NeoSceneAuthoring.CreateWidget("grid", Parent(c));
        [MenuItem(Root + "Layout/Panel", false, 43)]
        private static void Panel(MenuCommand c) => NeoSceneAuthoring.CreateWidget("panel", Parent(c));
        [MenuItem(Root + "Layout/Scroll", false, 44)]
        private static void Scroll(MenuCommand c) => NeoSceneAuthoring.CreateWidget("scroll", Parent(c));

        // --- Display ---
        [MenuItem(Root + "Display/Text", false, 60)]
        private static void Text(MenuCommand c) => NeoSceneAuthoring.CreateWidget("text", Parent(c));
        [MenuItem(Root + "Display/Image", false, 61)]
        private static void Image(MenuCommand c) => NeoSceneAuthoring.CreateWidget("image", Parent(c));
        [MenuItem(Root + "Display/Icon", false, 62)]
        private static void Icon(MenuCommand c) => NeoSceneAuthoring.CreateWidget("icon", Parent(c));
        [MenuItem(Root + "Display/Shape", false, 63)]
        private static void Shape(MenuCommand c) => NeoSceneAuthoring.CreateWidget("shape", Parent(c));
        [MenuItem(Root + "Display/Progress", false, 64)]
        private static void Progress(MenuCommand c) => NeoSceneAuthoring.CreateWidget("progress", Parent(c));

        // --- Everything else + project-registered custom kinds ---
        [MenuItem(Root + "More Widgets…", false, 80)]
        private static void More(MenuCommand cmd)
        {
            GameObject parent = Parent(cmd);
            var menu = new GenericMenu();
            foreach (PaletteEntry e in NeoWidgetPalette.All)
            {
                string kind = e.kind;
                string preset = e.preset; // null for a bare kind; a Components tile carries its preset name
                menu.AddItem(new GUIContent($"{e.category}/{e.label}"), false,
                    () => NeoSceneAuthoring.CreateWidget(kind, preset, parent));
            }
            menu.ShowAsContext();
        }

        // --- Curated layout scaffolds (a small valid element tree, not a bare kind) ---
        [MenuItem(Root + "Insert Template…", false, 100)]
        private static void InsertTemplate(MenuCommand cmd)
        {
            GameObject parent = Parent(cmd);
            var menu = new GenericMenu();
            foreach (TemplateEntry t in NeoLayoutTemplates.All)
            {
                TemplateEntry entry = t;
                menu.AddItem(new GUIContent(entry.label), false,
                    () => NeoSceneAuthoring.InsertTemplate(entry, parent));
            }
            menu.ShowAsContext();
        }
    }
}
