using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// "Connect to…" — direct-manipulation flow wiring: writes a button→view navigation edge into a
    /// <see cref="FlowGraph"/> exactly the way an agent's spec `sync` would, but driven by picking two
    /// live scene objects (see <see cref="NeoSceneOverlay"/>'s pick mode + <see cref="ConnectToPopup"/>)
    /// instead of hand-editing JSON. Mirrors the generator's own bookkeeping — id-database registration
    /// (<see cref="UISpecGenerator"/>'s <c>RegisterId</c>) and runtime-resolvable transition registration
    /// (<see cref="ViewTransitionRegistry.EnsureRuntimeResolvable"/>) — so a native-authored connection
    /// round-trips and behaves identically to a generated one.
    /// </summary>
    public static class NeoFlowWiring
    {
        /// <summary> Outcome of a connect attempt — always returned, never thrown; check <see cref="error"/>. </summary>
        public sealed class WiringResult
        {
            public FlowGraph graph;
            public UINode fromNode;
            public UINode toNode;
            public FlowEdge edge;
            public bool createdFromNode;
            public bool createdToNode;

            /// <summary> True when an identical edge (same button trigger + destination) already existed. </summary>
            public bool alreadyExisted;

            /// <summary> Null/empty on success. Non-empty means nothing was wired (or, for the ambiguity
            /// case, edges were left exactly as they were before the call). </summary>
            public string error;

            /// <summary> Candidate node names when the source view is shown by more than one node. </summary>
            public List<string> fromCandidates;

            /// <summary> Candidate node names when the target view is shown by more than one node. </summary>
            public List<string> toCandidates;

            public bool ok => string.IsNullOrEmpty(error);
        }

        // ------------------------------------------------------------------ lookups

        /// <summary>
        /// The active scene's <see cref="FlowController"/> (first found), or null if none exists yet.
        /// Deliberately scans the scene graph rather than <see cref="FlowController.allControllers"/>:
        /// that registry only fills in via <c>OnEnable</c>, which plain MonoBehaviours never run outside
        /// Play mode — and "Connect to…" is an edit-time authoring action.
        /// </summary>
        public static FlowController FindSceneController()
        {
            Scene active = SceneManager.GetActiveScene();
            if (!active.IsValid()) return null;
            foreach (GameObject root in active.GetRootGameObjects())
            {
                FlowController found = root.GetComponentInChildren<FlowController>(true);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary> Every <see cref="UINode"/> in <paramref name="graph"/> whose showViews lists this view. </summary>
        public static List<UINode> NodesShowingView(FlowGraph graph, string category, string viewName)
        {
            var result = new List<UINode>();
            if (graph == null) return result;
            foreach (FlowNode node in graph.nodes)
            {
                if (!(node is UINode uiNode)) continue;
                foreach (UINode.ViewRef view in uiNode.showViews)
                {
                    if (string.Equals(view.category, category, StringComparison.Ordinal) &&
                        string.Equals(view.viewName, viewName, StringComparison.Ordinal))
                    {
                        result.Add(uiNode);
                        break;
                    }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ connect (graph supplied)

        /// <summary> Connects <paramref name="button"/>'s click to navigate to <paramref name="target"/>. </summary>
        public static WiringResult ConnectButtonToView(FlowGraph graph, UIButton button, UIView target,
            string transitionFullName, bool allowsBack = true) =>
            ConnectButtonToView(graph, button, target, transitionFullName, allowsBack, null, null);

        /// <summary>
        /// Same as the five-argument overload, but lets a caller (the Connect-to popup) pass the node
        /// the user picked when <see cref="NodesShowingView"/> found more than one candidate for the
        /// source and/or target view. Pass null for whichever side isn't ambiguous — it resolves
        /// normally (reuse the single match, else create one).
        /// </summary>
        public static WiringResult ConnectButtonToView(FlowGraph graph, UIButton button, UIView target,
            string transitionFullName, bool allowsBack, UINode explicitFromNode, UINode explicitToNode)
        {
            var result = new WiringResult { graph = graph };
            if (graph == null) { result.error = "no flow graph to wire into"; return result; }
            if (button == null) { result.error = "no button selected"; return result; }
            if (target == null) { result.error = "no target view to connect to"; return result; }

            UIView sourceView = button.GetComponentInParent<UIView>(true);
            if (sourceView == null) { result.error = "button is not inside a UIView"; return result; }

            Undo.RecordObject(graph, "Connect Button To View");

            // ---- resolve/create the FROM node (the source view's node)
            UINode fromNode = explicitFromNode;
            if (fromNode == null)
            {
                List<UINode> candidates = NodesShowingView(graph, sourceView.id.Category, sourceView.id.Name);
                if (candidates.Count > 1)
                {
                    result.fromCandidates = candidates.Select(n => n.name).ToList();
                    result.error = $"multiple nodes show '{sourceView.id.Category}/{sourceView.id.Name}' " +
                                   $"({string.Join(", ", result.fromCandidates)}) — pick one to disambiguate";
                    return result;
                }
                if (candidates.Count == 1)
                {
                    fromNode = candidates[0];
                }
                else
                {
                    UINode nearTarget = NodesShowingView(graph, target.id.Category, target.id.Name).FirstOrDefault();
                    Vector2 pos = nearTarget != null
                        ? nearTarget.position - new Vector2(260f, 0f)
                        : NextGridPosition(graph);
                    fromNode = graph.AddNode<UINode>(sourceView.id.Name, pos);
                    fromNode.showViews.Add(new UINode.ViewRef(sourceView.id.Category, sourceView.id.Name));
                    result.createdFromNode = true;
                }
            }
            result.fromNode = fromNode;

            // ---- resolve/create the TO node (the target view's node)
            UINode toNode = explicitToNode;
            if (toNode == null)
            {
                List<UINode> candidates = NodesShowingView(graph, target.id.Category, target.id.Name);
                if (candidates.Count > 1)
                {
                    result.toCandidates = candidates.Select(n => n.name).ToList();
                    result.error = $"multiple nodes show '{target.id.Category}/{target.id.Name}' " +
                                   $"({string.Join(", ", result.toCandidates)}) — pick one to disambiguate";
                    return result;
                }
                if (candidates.Count == 1)
                {
                    toNode = candidates[0];
                }
                else
                {
                    Vector2 pos = fromNode.position + new Vector2(260f, 0f);
                    toNode = graph.AddNode<UINode>(target.id.Name, pos);
                    toNode.showViews.Add(new UINode.ViewRef(target.id.Category, target.id.Name));
                    result.createdToNode = true;
                }
            }
            result.toNode = toNode;

            // ---- an identical edge (same button trigger + destination) already there? hand it back.
            FlowEdge existing = fromNode.outputs.FirstOrDefault(e => IsSameConnection(e, toNode.name, button));
            if (existing != null)
            {
                result.edge = existing;
                result.alreadyExisted = true;
                EditorUtility.SetDirty(graph);
                return result;
            }

            var edge = new FlowEdge
            {
                portName = button.id.Name,
                toNode = toNode.name,
                allowsBack = allowsBack,
                transition = transitionFullName ?? "",
                trigger = new FlowTrigger
                {
                    type = FlowTrigger.TriggerType.ButtonClick,
                    category = button.id.Category,
                    name = button.id.Name
                }
            };
            fromNode.outputs.Add(edge);
            result.edge = edge;

            // ---- register ids exactly like the generator does (UISpecGenerator.RegisterId)
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null)
            {
                RegisterId(settings.buttonIds, button.id.Category, button.id.Name);
                RegisterId(settings.viewIds, sourceView.id.Category, sourceView.id.Name);
                RegisterId(settings.viewIds, target.id.Category, target.id.Name);
            }

            // ---- never silently drop a transition the player build can't resolve
            if (!string.IsNullOrEmpty(transitionFullName))
            {
                ViewTransitionAsset resolved = ViewTransitionRegistry.EnsureRuntimeResolvable(settings, transitionFullName);
                if (resolved == null)
                    Debug.LogWarning($"[Neo.UI] Connect To: transition '{transitionFullName}' did not resolve to any " +
                                      "registered ViewTransitionAsset — the edge will fall back to the project default at runtime.");
            }

            EditorUtility.SetDirty(graph);
            return result;
        }

        // ------------------------------------------------------------------ connect (resolves/creates the graph)

        /// <summary>
        /// Resolves the graph to wire into via the active scene's <see cref="FlowController"/>; when
        /// none exists yet, creates BOTH a new <see cref="FlowGraph"/> asset and the controller that
        /// runs it, seeded with a Start node wired to the (freshly created) source node — so the very
        /// first "Connect to…" in a scene produces a playable flow, not just an edge floating in an
        /// asset nothing runs.
        /// </summary>
        public static WiringResult ConnectButtonToView(UIButton button, UIView target, string transitionFullName,
            bool allowsBack = true)
        {
            if (button == null) return new WiringResult { error = "no button selected" };
            UIView sourceView = button.GetComponentInParent<UIView>(true);
            if (sourceView == null) return new WiringResult { error = "button is not inside a UIView" };

            FlowController controller = FindSceneController();
            FlowGraph graph = controller != null ? controller.flow : null;
            bool creatingController = false;

            if (graph == null)
            {
                graph = CreateGraphAsset(sourceView);
                if (controller == null)
                {
                    GameObject host = ResolveControllerHost(sourceView);
                    controller = host.GetComponent<FlowController>();
                    if (controller == null) controller = Undo.AddComponent<FlowController>(host);
                    creatingController = true;
                }
                if (controller.flow == null) controller.flow = graph;
                EditorUtility.SetDirty(controller);
            }

            WiringResult result = ConnectButtonToView(graph, button, target, transitionFullName, allowsBack, null, null);

            if (creatingController && result.ok)
            {
                StartNode start = graph.nodes.OfType<StartNode>().FirstOrDefault();
                if (start == null)
                    start = graph.AddNode<StartNode>("Start", result.fromNode.position - new Vector2(220f, 0f));
                if (!start.outputs.Any(e => string.Equals(e.toNode, result.fromNode.name, StringComparison.Ordinal)))
                    start.outputs.Add(new FlowEdge { portName = "Next", toNode = result.fromNode.name, allowsBack = false });
                if (string.IsNullOrEmpty(graph.startNode)) graph.startNode = start.name;
                EditorUtility.SetDirty(graph);
                AssetDatabase.SaveAssets();

                Debug.Log($"[Neo.UI] Connect To: created flow graph '{graph.name}' at " +
                          $"'{AssetDatabase.GetAssetPath(graph)}' and added a Flow Controller to '{controller.gameObject.name}'.");
            }

            return result;
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsSameConnection(FlowEdge edge, string toNodeName, UIButton button) =>
            edge != null && edge.trigger != null && edge.trigger.type == FlowTrigger.TriggerType.ButtonClick &&
            string.Equals(edge.toNode, toNodeName, StringComparison.Ordinal) &&
            string.Equals(edge.trigger.category, button.id.Category, StringComparison.Ordinal) &&
            string.Equals(edge.trigger.name, button.id.Name, StringComparison.Ordinal);

        private static Vector2 NextGridPosition(FlowGraph graph)
        {
            int count = graph.nodes.Count(n => n != null);
            int col = count % 4, row = count / 4;
            return new Vector2(80f + col * 260f, 80f + row * 180f);
        }

        /// <summary>
        /// The Flow asset root a fresh "Connect to…" graph lands in: the source view's showcase's
        /// <c>Generated/Flow</c> when it was generated/captured inside one (<see cref="GeneratedMarker.showcaseId"/>),
        /// else the shared <see cref="UISpecGenerator.FlowFolder"/> bucket — the same choice every other
        /// generated asset makes.
        /// </summary>
        private static FlowGraph CreateGraphAsset(UIView sourceView)
        {
            string folder = ResolveFlowFolder(sourceView, out string graphName);
            EnsureFolder(folder);
            string path = $"{folder}/{graphName}.asset";

            FlowGraph existing = AssetDatabase.LoadAssetAtPath<FlowGraph>(path);
            if (existing != null) return existing;

            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            graph.graphName = graphName;
            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();
            return graph;
        }

        private static string ResolveFlowFolder(UIView sourceView, out string graphName)
        {
            var marker = sourceView.GetComponentInParent<GeneratedMarker>(true);
            if (marker != null && !string.IsNullOrEmpty(marker.showcaseId)
                && ShowcaseRegistry.TryGet(marker.showcaseId, out Showcase showcase))
            {
                graphName = showcase.id;
                return $"{showcase.GeneratedRoot}/Flow";
            }
            graphName = "Flow";
            return UISpecGenerator.FlowFolder;
        }

        /// <summary> Where a freshly created <see cref="FlowController"/> attaches: the source view's
        /// nearest ancestor Canvas, falling back to the view's transform root. </summary>
        private static GameObject ResolveControllerHost(UIView sourceView)
        {
            Canvas canvas = sourceView.GetComponentInParent<Canvas>(true);
            return canvas != null ? canvas.gameObject : sourceView.transform.root.gameObject;
        }

        // Mirrors UISpecGenerator.RegisterId exactly — the generator's own id-database write mechanism.
        private static void RegisterId(IdDatabase database, string category, string name)
        {
            if (database == null || string.IsNullOrEmpty(category) || string.IsNullOrEmpty(name)) return;
            database.Add(category, name);
            EditorUtility.SetDirty(database);
        }

        // Recursively create a project-relative asset folder (AssetDatabase.CreateFolder needs each level).
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
