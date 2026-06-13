using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlterEyes.UI
{
    public enum FlowGraphState
    {
        Idle = 0,
        Playing = 1,
        Paused = 2,
        Stopped = 3
    }

    /// <summary>
    /// Runs a <see cref="FlowGraph"/>. Global controllers are findable cross-scene by name/static
    /// lookup; the asset graph is cloned at start so runtime traversal never mutates the asset.
    /// Supports SetActiveNodeByName (shortcuts/cheats), a history stack with GoBack(), and exposes
    /// the active node for the graph window's runtime debugging.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Flow Controller")]
    public class FlowController : MonoBehaviour, ITickable
    {
        public enum FlowType
        {
            Global = 0,
            Local = 1
        }

        public enum ControllerBehaviour
        {
            DoNothing = 0,
            StartFlow = 1,
            StopFlow = 2,
            PauseFlow = 3,
            ResumeFlow = 4
        }

        [Tooltip("Graph asset to run (cloned at start)")]
        public FlowGraph flow;

        public FlowType flowType = FlowType.Global;
        public ControllerBehaviour onEnableBehaviour = ControllerBehaviour.StartFlow;
        public ControllerBehaviour onDisableBehaviour = ControllerBehaviour.StopFlow;
        public bool dontDestroyOnSceneChange;

        [Tooltip("Navigate back through the history when the back-button signal fires")]
        public bool goBackOnBackButton = true;

        private static readonly HashSet<FlowController> Registry = new HashSet<FlowController>();

        private readonly Stack<string> _history = new Stack<string>();
        private readonly SignalReceiver _backReceiver = new SignalReceiver(BackButton.StreamCategory, BackButton.StreamName);
        private FlowGraph _runtimeGraph;
        private bool _goingBack;

        public FlowGraphState graphState { get; private set; } = FlowGraphState.Idle;

        public FlowNode activeNode { get; private set; }
        public FlowNode previousNode { get; private set; }

        /// <summary> The cloned graph being executed (null until started). </summary>
        public FlowGraph runtimeGraph => _runtimeGraph;

        public IReadOnlyCollection<string> history => _history;

        /// <summary> Raised when the active node changes — the graph window's live highlight hook. </summary>
        public event Action<FlowNode> OnActiveNodeChanged;

        public event Action OnFlowStarted;
        public event Action OnFlowStopped;
        public event Action OnFlowPaused;
        public event Action OnFlowResumed;

        public static IEnumerable<FlowController> allControllers => Registry;

        public static FlowController GetGlobalController() =>
            Registry.FirstOrDefault(c => c.flowType == FlowType.Global);

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (dontDestroyOnSceneChange) DontDestroyOnLoad(gameObject);
        }

        private bool _startRan;

        private void OnEnable()
        {
            Registry.Add(this);
            // first enable defers to Start(): views register in their own OnEnable and enable
            // order across a loading scene is arbitrary — starting the flow now could Show()
            // into a registry the views haven't joined yet (black screen, silently)
            if (_startRan) RunBehaviour(onEnableBehaviour);
        }

        private void Start()
        {
            _startRan = true;
            RunBehaviour(onEnableBehaviour);
        }

        private void OnDisable()
        {
            Registry.Remove(this);
            RunBehaviour(onDisableBehaviour);
            UITick.Unregister(this);
        }

        private void RunBehaviour(ControllerBehaviour behaviour)
        {
            switch (behaviour)
            {
                case ControllerBehaviour.StartFlow: StartFlow(); break;
                case ControllerBehaviour.StopFlow: StopFlow(); break;
                case ControllerBehaviour.PauseFlow: PauseFlow(); break;
                case ControllerBehaviour.ResumeFlow: ResumeFlow(); break;
            }
        }

        // ------------------------------------------------------------------ control API

        public void StartFlow()
        {
            if (flow == null)
            {
                Debug.LogWarning("[AlterEyes.UI] FlowController has no flow graph assigned — nothing to start.", this);
                return;
            }
            if (graphState == FlowGraphState.Playing) return;

            StopFlow();

            _runtimeGraph = Instantiate(flow);
            _runtimeGraph.name = $"{flow.name} (runtime)";
            graphState = FlowGraphState.Playing;
            _history.Clear();
            UITick.Register(this);

            foreach (FlowNode node in _runtimeGraph.nodes.Where(n => n != null && n.isGlobal))
                node.OnGraphStarted(this);

            if (goBackOnBackButton)
            {
                _backReceiver.SetOnSignalCallback(_ => GoBack());
                _backReceiver.Connect();
            }

            OnFlowStarted?.Invoke();

            FlowNode start = _runtimeGraph.ResolveStartNode();
            if (start != null) SetActiveNode(start);
            else Debug.LogWarning($"[AlterEyes.UI] FlowGraph '{flow.name}' has no start node.", this);
        }

        public void StopFlow()
        {
            if (graphState == FlowGraphState.Idle && _runtimeGraph == null) return;

            if (activeNode != null)
            {
                activeNode.OnExit(this);
                activeNode = null;
                OnActiveNodeChanged?.Invoke(null);
            }

            if (_runtimeGraph != null)
            {
                foreach (FlowNode node in _runtimeGraph.nodes.Where(n => n != null && n.isGlobal))
                    node.OnGraphStopped(this);
                if (Application.isPlaying) Destroy(_runtimeGraph);
                else DestroyImmediate(_runtimeGraph);
                _runtimeGraph = null;
            }

            _backReceiver.Disconnect();
            _history.Clear();
            UITick.Unregister(this);

            if (graphState != FlowGraphState.Idle)
            {
                graphState = FlowGraphState.Stopped;
                OnFlowStopped?.Invoke();
            }
        }

        public void PauseFlow()
        {
            if (graphState != FlowGraphState.Playing) return;
            graphState = FlowGraphState.Paused;
            OnFlowPaused?.Invoke();
        }

        public void ResumeFlow()
        {
            if (graphState != FlowGraphState.Paused) return;
            graphState = FlowGraphState.Playing;
            OnFlowResumed?.Invoke();
        }

        // ------------------------------------------------------------------ navigation

        /// <summary> Jumps the flow to the named node (shortcut/cheat entry point). </summary>
        public bool SetActiveNodeByName(string nodeName)
        {
            if (_runtimeGraph == null) return false;
            FlowNode node = _runtimeGraph.GetNode(nodeName);
            if (node == null || !node.isExecutable)
            {
                Debug.LogWarning($"[AlterEyes.UI] FlowController has no executable node named '{nodeName}'.", this);
                return false;
            }
            SetActiveNode(node);
            return true;
        }

        /// <summary> Advances across an edge (the standard traversal path). </summary>
        public void Advance(FlowEdge edge, bool pushHistory = true)
        {
            if (graphState != FlowGraphState.Playing || _runtimeGraph == null || edge == null) return;
            FlowNode target = _runtimeGraph.GetNode(edge.toNode);
            if (target == null || !target.isExecutable)
            {
                Debug.LogWarning($"[AlterEyes.UI] Flow edge points to missing node '{edge.toNode}'.", this);
                return;
            }
            SetActiveNode(target, pushHistory && edge.allowsBack);
        }

        /// <summary> Activates a node directly. </summary>
        public void SetActiveNode(FlowNode node, bool pushHistory = true) => SetActiveNode(node, null, pushHistory);

        private void SetActiveNode(FlowNode node, FlowEdge viaEdge, bool pushHistory = true)
        {
            if (node == null || graphState == FlowGraphState.Idle || graphState == FlowGraphState.Stopped) return;

            if (activeNode != null)
            {
                // a view both nodes show must survive the transition untouched — without this the
                // exit hides it and the enter re-shows it, replaying both animations as a flash
                if (activeNode is UINode previousUi && node is UINode nextUi)
                    previousUi.carryOverViews = nextUi.showViews;
                activeNode.OnExit(this);
                if (pushHistory && !_goingBack && !string.IsNullOrEmpty(activeNode.name))
                    _history.Push(activeNode.name);
            }

            previousNode = activeNode;
            activeNode = node;
            OnActiveNodeChanged?.Invoke(node);
            node.OnEnter(this, viaEdge);
        }

        /// <summary> Returns to the previously active node (history stack). </summary>
        public bool GoBack()
        {
            if (_runtimeGraph == null || _history.Count == 0) return false;
            string nodeName = _history.Pop();
            FlowNode node = _runtimeGraph.GetNode(nodeName);
            if (node == null) return false;

            _goingBack = true;
            SetActiveNode(node, pushHistory: false);
            _goingBack = false;
            return true;
        }

        public void ClearHistory() => _history.Clear();

        // ------------------------------------------------------------------ ticking

        public void Tick(float deltaTime)
        {
            if (graphState != FlowGraphState.Playing) return;
            activeNode?.Tick(this, deltaTime);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
