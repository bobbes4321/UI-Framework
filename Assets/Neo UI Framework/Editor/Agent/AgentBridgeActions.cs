using System;
using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One <see cref="AgentBridge"/> action: an id, whether it mutates project assets (gates it in Play
    /// mode — see <see cref="AgentBridge.HandleRequest"/>), and the handler that reads the request and
    /// fills the result.
    /// </summary>
    public sealed class BridgeAction
    {
        public string id;
        public bool mutatesAssets;
        public Action<Dictionary<string, object>, Dictionary<string, object>> handler;
    }

    /// <summary>
    /// The extension seam for <c>Editor/Agent/AgentBridge.cs</c> actions (audit E1). Before this, adding
    /// an agent action meant editing a sealed switch AND a hand-list of "which actions mutate assets" AND
    /// the unknown-action error string — three parallel lists a consuming project could not extend
    /// without forking the package's flagship agent-first surface. Now it's one
    /// <see cref="NeoKeyedRegistry{T}"/> over <see cref="BridgeAction"/>: the built-ins are seeded once,
    /// lazily, and a project adds its own action with a single <see cref="Register"/> call — no fork.
    /// </summary>
    public static class AgentBridgeActions
    {
        private static readonly NeoKeyedRegistry<BridgeAction> Registry = new NeoKeyedRegistry<BridgeAction>(
            key: a => a.id,
            builtins: Builtins,
            registryName: "AgentBridgeActions");

        /// <summary> All registered actions, built-ins first. </summary>
        public static IReadOnlyList<BridgeAction> All => Registry.All;

        /// <summary> Looks up an action by id (the request's <c>"action"</c> value). </summary>
        public static bool TryGet(string id, out BridgeAction action) => Registry.TryGet(id, out action);

        /// <summary>
        /// Registers a bridge action (or replaces one of the same id — a project can override a built-in
        /// this way too). <paramref name="mutatesAssets"/> alone decides whether the action is refused in
        /// Play mode: generating/screenshotting/exporting-and-diffing mid-play corrupts bakes, because
        /// <c>AddComponent</c> fires <c>Awake</c>/<c>OnEnable</c> on the factory's temp objects while
        /// they're still under construction.
        /// </summary>
        public static void Register(string id, bool mutatesAssets,
            Action<Dictionary<string, object>, Dictionary<string, object>> handler)
        {
            Registry.Register(new BridgeAction { id = id, mutatesAssets = mutatesAssets, handler = handler });
        }

        /// <summary> Test-only: restores the registry to just its built-ins. </summary>
        internal static void ResetForTests() => Registry.ResetForTests();

        private static IEnumerable<BridgeAction> Builtins()
        {
            yield return new BridgeAction { id = "screenshot", mutatesAssets = true, handler = AgentBridge.HandleScreenshot };
            yield return new BridgeAction { id = "generate", mutatesAssets = true, handler = AgentBridge.HandleGenerate };
            yield return new BridgeAction { id = "export", mutatesAssets = false, handler = AgentBridge.HandleExport };
            yield return new BridgeAction { id = "validate", mutatesAssets = false, handler = (request, result) => AgentBridge.HandleValidate(result) };
            yield return new BridgeAction { id = "diff", mutatesAssets = true, handler = AgentBridge.HandleDiff };
            yield return new BridgeAction { id = "merge", mutatesAssets = true, handler = AgentBridge.HandleMerge };
            yield return new BridgeAction { id = "sync", mutatesAssets = true, handler = AgentBridge.HandleSync };
            yield return new BridgeAction { id = "buildScene", mutatesAssets = true, handler = AgentBridge.HandleBuildScene };
            yield return new BridgeAction { id = "regenerateShowcase", mutatesAssets = true, handler = AgentBridge.HandleRegenerateShowcase };
            yield return new BridgeAction { id = "specReference", mutatesAssets = false, handler = AgentBridge.HandleSpecReference };
            yield return new BridgeAction { id = "preview", mutatesAssets = true, handler = AgentBridge.HandlePreview };
            yield return new BridgeAction { id = "importSprites", mutatesAssets = false, handler = AgentBridge.HandleImportSprites };
            yield return new BridgeAction { id = "bindings", mutatesAssets = false, handler = AgentBridge.HandleBindings };
        }
    }
}
