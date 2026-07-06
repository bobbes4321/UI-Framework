using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary> Entry point: activated when the graph starts, immediately advances. </summary>
    [Serializable]
    public class StartNode : FlowNode
    {
        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            FlowEdge next = GetFirstConnectedOutput();
            if (next != null) controller.Advance(next, pushHistory: false);
        }
    }

    /// <summary>
    /// A resolved view transition for one navigation cut — planted on both the outgoing and
    /// incoming <see cref="UINode"/> by <c>FlowController.SetActiveNode</c> (see
    /// <see cref="UINode.pendingTransition"/>), exactly like <see cref="UINode.carryOverViews"/>.
    /// <see cref="outgoingViews"/> is filled in by the outgoing node's OnExit (the views it actually
    /// showed) so the incoming node's OnEnter can look for <see cref="NeoSharedElement"/> pairs
    /// without either node needing a reference to the other.
    /// </summary>
    internal class ViewTransitionPlan
    {
        public ViewTransitionAsset asset;
        public List<UINode.ViewRef> outgoingViews;
    }

    /// <summary>
    /// The core node: shows/hides views on enter (and optionally on exit), then advances when one
    /// of its output-edge triggers fires (button click / signal / toggle / view event / back / timer).
    /// </summary>
    [Serializable]
    public class UINode : FlowNode
    {
        [Serializable]
        public class ViewRef
        {
            public string category;
            public string viewName;

            public ViewRef() { }

            public ViewRef(string viewCategory, string name)
            {
                category = viewCategory;
                viewName = name;
            }
        }

        [Tooltip("Views shown when this node activates")]
        public List<ViewRef> showViews = new List<ViewRef>();

        [Tooltip("Views hidden when this node activates")]
        public List<ViewRef> hideViews = new List<ViewRef>();

        [Tooltip("Hide the shown views again when this node deactivates")]
        public bool hideShownViewsOnExit;

        /// <summary>
        /// Views the NEXT node also shows — set by the controller right before OnExit so a view
        /// shared across the transition stays put instead of hiding and immediately re-showing
        /// (which replays both animations as a visible flash). Cleared after each exit.
        /// </summary>
        [NonSerialized] internal List<ViewRef> carryOverViews;

        /// <summary>
        /// The resolved view transition for the CURRENT navigation cut — the transition analog of
        /// <see cref="carryOverViews"/>, planted on both the outgoing and incoming node right before
        /// OnExit/OnEnter run. Consumed and cleared by this node's own OnExit/OnEnter; never carried
        /// past one cut.
        /// </summary>
        [NonSerialized] internal ViewTransitionPlan pendingTransition;

        [NonSerialized] private readonly List<FlowTriggerListener> _listeners = new List<FlowTriggerListener>();
        [NonSerialized] private readonly List<FlowEdge> _timerEdges = new List<FlowEdge>();
        [NonSerialized] private float _timeInNode;
        [NonSerialized] private bool _active;

        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            _active = true;
            _timeInNode = 0f;

            ViewTransitionPlan plan = pendingTransition;
            pendingTransition = null;

            foreach (ViewRef view in hideViews) UIView.Hide(view.category, view.viewName);
            foreach (ViewRef view in showViews)
            {
                // a resolved transition owns the incoming choreography (offset/cascade/hero) for
                // every shown view, including ones also carried over from the previous node — Show()
                // is a no-op on an already-visible view either way, so this can't replay a carryover.
                if (plan != null)
                    ViewTransitionRunner.ScheduleShow(view.category, view.viewName, plan.asset, plan.outgoingViews);
                else
                    UIView.Show(view.category, view.viewName);
            }

            _timerEdges.Clear();
            foreach (FlowEdge edge in outputs)
            {
                if (edge.trigger == null) continue;
                if (string.IsNullOrEmpty(edge.toNode))
                {
                    // no-silent-failures: a trigger with no target is dead wiring — the button/signal
                    // will visibly do nothing, so say so instead of skipping quietly
                    if (edge.trigger.type != FlowTrigger.TriggerType.None)
                        Debug.LogWarning($"FlowGraph: node '{name}' has a '{edge.trigger}' edge with no target node — its trigger will never advance the flow. Set the edge's toNode in the flow graph window.");
                    continue;
                }
                if (edge.trigger.type == FlowTrigger.TriggerType.Timer)
                {
                    _timerEdges.Add(edge);
                }
                else if (edge.trigger.usesSignalStream)
                {
                    FlowEdge captured = edge;
                    var listener = new FlowTriggerListener(edge.trigger, () =>
                    {
                        if (_active) controller.Advance(captured);
                    });
                    listener.Connect();
                    _listeners.Add(listener);
                }
            }
        }

        public override void OnExit(FlowController controller)
        {
            _active = false;
            foreach (FlowTriggerListener listener in _listeners) listener.Disconnect();
            _listeners.Clear();
            _timerEdges.Clear();

            ViewTransitionPlan plan = pendingTransition;
            pendingTransition = null;
            // hand this node's shown views to the incoming node's plan (shared-element flights match
            // against them) before they get hidden below
            if (plan != null) plan.outgoingViews = showViews;

            if (hideShownViewsOnExit)
                foreach (ViewRef view in showViews)
                    if (!IsCarriedOver(view))
                    {
                        if (plan != null && ViewTransitionAsset.Overrides(plan.asset.outgoing))
                            UIView.Hide(view.category, view.viewName, plan.asset.outgoing);
                        else
                            UIView.Hide(view.category, view.viewName);
                    }
            carryOverViews = null;
        }

        private bool IsCarriedOver(ViewRef view)
        {
            if (carryOverViews == null) return false;
            foreach (ViewRef kept in carryOverViews)
                if (kept.category == view.category && kept.viewName == view.viewName)
                    return true;
            return false;
        }

        public override void Tick(FlowController controller, float deltaTime)
        {
            if (!_active || _timerEdges.Count == 0) return;
            _timeInNode += deltaTime;
            foreach (FlowEdge edge in _timerEdges)
            {
                if (_timeInNode < edge.trigger.timerDuration) continue;
                controller.Advance(edge);
                return;
            }
        }
    }

    /// <summary> Sends a signal (optionally with a string payload) and advances. </summary>
    [Serializable]
    public class SignalNode : FlowNode
    {
        public string streamCategory;
        public string streamName;
        [Tooltip("Optional string payload sent with the signal")]
        public string payload;

        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            if (string.IsNullOrEmpty(payload)) Signals.Send(streamCategory, streamName, sender: controller);
            else Signals.Send(streamCategory, streamName, payload, controller);

            FlowEdge next = GetFirstConnectedOutput();
            if (next != null) controller.Advance(next, pushHistory: false);
        }
    }

    /// <summary> Enables/disables the back-button system (optionally clearing history) and advances. </summary>
    [Serializable]
    public class BackButtonNode : FlowNode
    {
        public enum Action
        {
            Enable = 0,
            Disable = 1,
            EnableByForce = 2
        }

        public Action action = Action.Enable;
        public bool clearHistory;

        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            switch (action)
            {
                case Action.Enable: BackButton.Enable(); break;
                case Action.Disable: BackButton.Disable(); break;
                case Action.EnableByForce: BackButton.EnableByForce(); break;
            }
            if (clearHistory) controller.ClearHistory();

            FlowEdge next = GetFirstConnectedOutput();
            if (next != null) controller.Advance(next, pushHistory: false);
        }
    }

    /// <summary>
    /// Global node: always listening while the graph runs. When its trigger fires, the flow jumps
    /// here and advances through its output — cross-flow shortcuts from anywhere.
    /// </summary>
    [Serializable]
    public class PortalNode : FlowNode
    {
        public FlowTrigger trigger = new FlowTrigger();
        public bool clearHistoryOnJump;

        public override bool isGlobal => true;

        [NonSerialized] private FlowTriggerListener _listener;

        public override void OnGraphStarted(FlowController controller)
        {
            _listener = new FlowTriggerListener(trigger, () =>
            {
                if (controller.graphState != FlowGraphState.Playing) return;
                if (controller.activeNode == this) return;
                if (clearHistoryOnJump) controller.ClearHistory();
                controller.SetActiveNode(this);
            });
            _listener.Connect();
        }

        public override void OnGraphStopped(FlowController controller)
        {
            _listener?.Disconnect();
            _listener = null;
        }

        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            FlowEdge next = GetFirstConnectedOutput();
            if (next != null) controller.Advance(next, pushHistory: false);
        }
    }

    /// <summary> Picks a weighted-random connected output and advances. </summary>
    [Serializable]
    public class RandomNode : FlowNode
    {
        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            int total = 0;
            foreach (FlowEdge edge in outputs)
            {
                if (string.IsNullOrEmpty(edge.toNode) || edge.weight <= 0) continue;
                total += edge.weight;
            }
            if (total <= 0) return;

            int roll = UnityEngine.Random.Range(0, total);
            int cumulative = 0;
            foreach (FlowEdge edge in outputs)
            {
                if (string.IsNullOrEmpty(edge.toNode) || edge.weight <= 0) continue;
                cumulative += edge.weight;
                if (roll < cumulative)
                {
                    controller.Advance(edge);
                    return;
                }
            }
        }
    }

    /// <summary> Sets Time.timeScale instantly or eased over time, optionally waiting before advancing. </summary>
    [Serializable]
    public class TimeScaleNode : FlowNode
    {
        [Min(0f)] public float targetTimeScale = 1f;
        public bool animate;
        [Min(0f)] public float animationDuration = 0.5f;
        public Ease animationEase = Ease.OutQuad;
        public bool waitForFinish;

        [NonSerialized] private FloatTween _tween;

        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            FlowEdge next = GetFirstConnectedOutput();

            if (!animate || animationDuration <= 0f)
            {
                Time.timeScale = targetTimeScale;
                if (next != null) controller.Advance(next, pushHistory: false);
                return;
            }

            _tween = _tween ?? TweenPool.Get<FloatTween>();
            _tween.settings = new TweenSettings { duration = animationDuration, ease = animationEase };
            _tween.SetTarget(() => Time.timeScale, v => Time.timeScale = Mathf.Max(0f, v));
            _tween.PlayToValue(targetTimeScale);

            if (next == null) return;
            if (waitForFinish)
                _tween.onFinish = () => controller.Advance(next, pushHistory: false);
            else
                controller.Advance(next, pushHistory: false);
        }

        public override void OnExit(FlowController controller)
        {
            if (_tween != null && !waitForFinish) return; // let it run out
            if (_tween != null && !_tween.isActive)
            {
                TweenPool.Release(_tween);
                _tween = null;
            }
        }
    }

    /// <summary> Quits the application (exits play mode in the editor). </summary>
    [Serializable]
    public class ApplicationQuitNode : FlowNode
    {
        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary> Pass-through routing node used to declutter graphs. </summary>
    [Serializable]
    public class PivotNode : FlowNode
    {
        public enum Orientation
        {
            HorizontalLeft = 0,
            HorizontalRight = 1,
            VerticalUp = 2,
            VerticalDown = 3
        }

        public Orientation orientation = Orientation.HorizontalRight;

        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            FlowEdge next = GetFirstConnectedOutput();
            if (next != null) controller.Advance(next, pushHistory: false);
        }
    }

    /// <summary> Editor-only documentation node; never executed. </summary>
    [Serializable]
    public class StickyNoteNode : FlowNode
    {
        [TextArea] public string text;
        public Vector2 size = new Vector2(200f, 120f);

        public override bool isExecutable => false;
    }

    /// <summary> Logs a message and passes through. </summary>
    [Serializable]
    public class DebugNode : FlowNode
    {
        public string message = "Debug node reached";
        public LogType logType = LogType.Log;

        public override void OnEnter(FlowController controller, FlowEdge viaEdge)
        {
            string text = $"[FlowGraph:{controller.name}] {message}";
            switch (logType)
            {
                case LogType.Warning: Debug.LogWarning(text, controller); break;
                case LogType.Error: Debug.LogError(text, controller); break;
                default: Debug.Log(text, controller); break;
            }

            FlowEdge next = GetFirstConnectedOutput();
            if (next != null) controller.Advance(next, pushHistory: false);
        }
    }
}
