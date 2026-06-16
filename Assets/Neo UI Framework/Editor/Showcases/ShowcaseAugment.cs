using UnityEngine;
using UnityEngine.SceneManagement;
using Neo.UI.Demo;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Reusable post-build augmentation for showcases whose behaviour a spec alone can't express.
    /// Extracted from the retired <c>ShowcaseSceneBuilder</c> so it can be wired as the game-ui
    /// showcase's <see cref="Showcase.postBuild"/> hook (Phase C). Spec-only showcases never need it.
    /// </summary>
    public static class ShowcaseAugment
    {
        /// <summary>
        /// Injects the live HUD simulation (<see cref="ShowcaseDirector"/>) and the project-side
        /// <c>Game.UI.GameUIBindings</c> (the binding-guide worked example — domain signals, the typed
        /// Shop/Deals list, the coin economy) into a freshly built scene.
        /// <para>
        /// The bindings live in the developer's own Assembly-CSharp, which this package editor assembly
        /// can't reference by type, so they're resolved + attached reflectively — skipping cleanly (with
        /// a hint) when the stub hasn't been generated yet (Tools ▸ Neo UI ▸ Advanced ▸ Generate Binding
        /// Stub). Objects are created in the given scene so the caller can save it.
        /// </para>
        /// </summary>
        public static void AttachGameUIDirector(Scene scene)
        {
            var directorGo = new GameObject("Showcase Director", typeof(ShowcaseDirector)); // HUD output simulation
            SceneManager.MoveGameObjectToScene(directorGo, scene);

            System.Type bindingsType = System.Type.GetType("Game.UI.GameUIBindings, Assembly-CSharp");
            if (bindingsType != null)
            {
                var bindingsGo = new GameObject("Game UI Bindings", bindingsType);
                SceneManager.MoveGameObjectToScene(bindingsGo, scene);
            }
            else
            {
                Debug.Log("[Neo.UI] Showcase: Game.UI.GameUIBindings not found — generate the binding " +
                          "stub (Tools ▸ Neo UI ▸ Advanced ▸ Generate Binding Stub) to wire the shop economy.");
            }
        }
    }
}
