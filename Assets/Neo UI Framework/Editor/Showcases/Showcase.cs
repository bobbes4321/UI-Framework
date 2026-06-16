using System;
using UnityEngine.SceneManagement;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A single, self-contained demo of one package aspect (buttons, toggles, a settings menu, …).
    /// A showcase is just metadata plus a spec path: it points at the JSON spec to generate from and
    /// the flow to build a scene around, and everything else is <em>derived from its <see cref="id"/></em>.
    /// <para>
    /// Crucially, <see cref="GeneratedRoot"/> and <see cref="ScenePath"/> are computed (never stored)
    /// from the id under <see cref="ShowcaseRegistry.ShowcasesRoot"/>, so two distinct showcases can
    /// never share a generated-asset root or scene by construction — the "two specs collide in one
    /// bucket and the scene builder throws on multiple flows" headache becomes structurally
    /// impossible. Each showcase therefore owns its own <c>Generated/</c> folder (views/flow/popups/
    /// presets + its own <c>.neo-baseline.json</c>) and its own committed <c>{id}.unity</c> scene.
    /// </para>
    /// </summary>
    public class Showcase
    {
        /// <summary> Stable, file-safe identifier (also the folder + scene name). The single source of derivation. </summary>
        public string id;

        /// <summary> Human-facing title shown in the Hub gallery. </summary>
        public string title;

        /// <summary> One-line description of what this showcase demonstrates. </summary>
        public string description;

        /// <summary> Grouping bucket for the Hub gallery (e.g. "Interactive", "Layout", "Theming"). </summary>
        public string category;

        /// <summary> Path to the JSON spec this showcase generates from (e.g. <c>Assets/Showcases/Specs/buttons.json</c>). </summary>
        public string specPath;

        /// <summary> Name of the flow graph to build the scene around (passed to the scene builder). </summary>
        public string flowName;

        /// <summary> Optional committed thumbnail asset path for the gallery (built-ins prefer a baked PNG over a live render). </summary>
        public string thumbnail;

        /// <summary>
        /// Optional post-build hook — runs against the freshly built scene (e.g. the game-ui showcase
        /// attaching its director/bindings). The extensibility seam for behaviour a spec alone can't
        /// express; null for the common spec-only showcase.
        /// </summary>
        public Action<Scene> postBuild;

        /// <summary>
        /// The generated-asset root for this showcase — <c>{ShowcasesRoot}/{id}/Generated</c>. Derived
        /// from <see cref="id"/>, never stored, so it can never collide with another showcase's root.
        /// </summary>
        public string GeneratedRoot => $"{ShowcaseRegistry.ShowcasesRoot}/{id}/Generated";

        /// <summary>
        /// The committed scene path for this showcase — <c>{ShowcasesRoot}/{id}/{id}.unity</c>. Derived
        /// from <see cref="id"/>, never stored.
        /// </summary>
        public string ScenePath => $"{ShowcaseRegistry.ShowcasesRoot}/{id}/{id}.unity";
    }
}
