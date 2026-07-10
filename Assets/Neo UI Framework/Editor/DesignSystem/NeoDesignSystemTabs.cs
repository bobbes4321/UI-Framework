using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Everything one <see cref="NeoDesignSystemWindow"/> tab needs to draw itself: the live settings +
    /// theme it edits, a handle back to the hosting window (for <see cref="EditorWindow.Repaint"/> and
    /// the like), and the tab's OWN persistent UI state — the object its
    /// <see cref="DesignSystemTabDescriptor.createState"/> factory produced, cast back to the tab's
    /// state type. Kept a growable CLASS (not a long parameter list) so a later change can hand tabs
    /// more context without breaking the draw contract every registered tab is compiled against.
    /// </summary>
    public sealed class DesignSystemTabContext
    {
        /// <summary> The live settings asset the window is authoring (never null while a tab draws). </summary>
        public NeoUISettings settings;

        /// <summary> The live theme (<c>settings.theme</c>) — never null while a tab draws. </summary>
        public Theme theme;

        /// <summary> The hosting window, for <see cref="EditorWindow.Repaint"/> and friends. </summary>
        public EditorWindow window;

        /// <summary> This tab's own persistent state object (from its descriptor's state factory), or
        /// null when the tab declared no state factory. Use <see cref="State{T}"/> to read it typed. </summary>
        public object state;

        /// <summary> This tab's state, cast to <typeparamref name="T"/> (null if unset / wrong type). </summary>
        public T State<T>() where T : class => state as T;
    }

    /// <summary>
    /// One entry in the Design System window's tab vocabulary — its stable id, the title shown on the
    /// tab strip, a sort order (built-ins reserve 0/10/20/30/40 so a project can slot a tab between
    /// them), an optional factory for the tab's per-window UI state, and the draw hook itself.
    /// </summary>
    public readonly struct DesignSystemTabDescriptor
    {
        /// <summary> Stable registry key (e.g. "colors"). Not shown to the user — see <see cref="title"/>. </summary>
        public readonly string id;

        /// <summary> Text shown on the window's tab strip. </summary>
        public readonly string title;

        /// <summary> Sort key deciding tab-strip order (ascending; ties keep registration order). The
        /// built-ins use 0/10/20/30/40, leaving gaps a project can register between. </summary>
        public readonly int order;

        /// <summary> Optional factory for the tab's per-window UI state (browsed index, new-name text
        /// fields, cached preview textures, …). Called once per window; the returned object is handed
        /// back to <see cref="draw"/> via <see cref="DesignSystemTabContext.state"/> and disposed with
        /// the window if it implements <see cref="IDisposable"/>. Null ⇒ the tab is stateless. </summary>
        public readonly Func<object> createState;

        /// <summary> Draws the tab's body inside the window's scroll view. </summary>
        public readonly Action<DesignSystemTabContext> draw;

        /// <summary> When true, the window does NOT wrap this tab's <see cref="draw"/> call in its own
        /// <see cref="EditorGUILayout.BeginScrollView"/> — the tab draws its own scroll container(s) and
        /// is handed the full remaining window height to fill instead (see
        /// <see cref="DesignSystemGUI.BeginSplitPane"/>, the master-detail helper built for this). Default
        /// false, meaning the window's outer scroll view wraps the tab as before. This is a public
        /// extensibility seam: a consuming project's registered tab can opt in too, via the constructor's
        /// <c>ownsLayout</c> parameter, for its own dual-pane/master-detail layouts. </summary>
        public readonly bool ownsLayout;

        public DesignSystemTabDescriptor(string id, string title, int order,
            Func<object> createState, Action<DesignSystemTabContext> draw, bool ownsLayout = false)
        {
            this.id = id;
            this.title = title;
            this.order = order;
            this.createState = createState;
            this.draw = draw;
            this.ownsLayout = ownsLayout;
        }
    }

    /// <summary>
    /// The single source of truth for the Design System window's tab set — see
    /// <see cref="DesignSystemTabDescriptor"/> for what an entry owns. Pattern R
    /// (<see cref="NeoKeyedRegistry{T}"/>), same shape as <see cref="FlowNodeKinds"/> /
    /// <see cref="HubToolRegistry"/>. Seeded with the eight built-in tabs (Overview/Colors/Typography/
    /// Buttons/Shapes/Presets/Motion/Bundles, each living in its own file under <c>Editor/DesignSystem/</c>); a consuming
    /// project calls <see cref="Register"/> once (typically from an <c>[InitializeOnLoad]</c> static
    /// ctor) to add its own design-system tab without forking the package — per the "extensible by
    /// design" hard constraint.
    /// </summary>
    public static class NeoDesignSystemTabs
    {
        private static readonly NeoKeyedRegistry<DesignSystemTabDescriptor> _registry =
            new NeoKeyedRegistry<DesignSystemTabDescriptor>(
                d => d.id,
                builtins: Builtins,
                validate: d => d.draw != null,
                registryName: "NeoDesignSystemTabs");

        // The built-ins, ordered: Overview first (the dashboard/default), then the authoring tabs in
        // the order the window historically drew them, Typography between Colors and Buttons, Bundles last.
        private static IEnumerable<DesignSystemTabDescriptor> Builtins()
        {
            yield return OverviewTab.Descriptor;
            yield return new DesignSystemTabDescriptor("colors", "Colors", 0, ColorsTab.CreateState, ColorsTab.Draw);
            yield return TypographyTab.Descriptor;
            yield return new DesignSystemTabDescriptor("buttons", "Buttons", 10, ButtonsTab.CreateState, ButtonsTab.Draw, ownsLayout: true);
            yield return new DesignSystemTabDescriptor("shapes", "Shapes", 20, ShapesTab.CreateState, ShapesTab.Draw, ownsLayout: true);
            yield return new DesignSystemTabDescriptor("icons", "Icons", 25, IconsTab.CreateState, IconsTab.Draw, ownsLayout: true);
            yield return new DesignSystemTabDescriptor("presets", "Presets", 30, PresetsTab.CreateState, PresetsTab.Draw, ownsLayout: true);
            yield return new DesignSystemTabDescriptor("motion", "Motion", 40, MotionTab.CreateState, MotionTab.Draw, ownsLayout: true);
            yield return BundlesTab.Descriptor;
        }

        /// <summary> Every registered tab, in registration order (built-ins first). Prefer
        /// <see cref="Ordered"/> for display — it applies each descriptor's <c>order</c>. </summary>
        public static IReadOnlyList<DesignSystemTabDescriptor> All => _registry.All;

        /// <summary> The tabs sorted for display — ascending <c>order</c>, ties broken by registration
        /// order (stable). Fresh list per call; the window caches it, so it isn't rebuilt per OnGUI. </summary>
        public static IReadOnlyList<DesignSystemTabDescriptor> Ordered =>
            _registry.All.OrderBy(d => d.order).ToList();

        /// <summary> Resolves a tab by id. False (default) on miss. </summary>
        public static bool TryGet(string id, out DesignSystemTabDescriptor descriptor) =>
            _registry.TryGet(id, out descriptor);

        /// <summary>
        /// Registers (or replaces, by id) a tab. The extension seam: a consuming project calls this once
        /// to add its own design-system tab to the window without forking the package. A draw-less entry
        /// is warned-and-ignored (never thrown).
        /// </summary>
        public static void Register(DesignSystemTabDescriptor descriptor) => _registry.Register(descriptor);

        /// <summary> Test-only: clears project registrations and re-seeds the built-ins on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();
    }
}
