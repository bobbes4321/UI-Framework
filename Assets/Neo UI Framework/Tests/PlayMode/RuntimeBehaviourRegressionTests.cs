using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Regression net for the "renders fine, does nothing at runtime" failure class found in the
    /// first playable generated scene: the flow/view enable-order race, steppers with frozen
    /// labels, and progressors wiping their authored start state.
    /// </summary>
    public class RuntimeBehaviourRegressionTests
    {
        private readonly System.Collections.Generic.List<Object> _cleanup =
            new System.Collections.Generic.List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object obj in _cleanup)
                if (obj != null) Object.Destroy(obj);
            _cleanup.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _cleanup.Add(go);
            return go;
        }

        /// <summary>
        /// THE race: a FlowController enabled before any view registers must still show the start
        /// view (it defers its first auto-start to Start, after every OnEnable has run).
        /// </summary>
        [UnityTest]
        public IEnumerator FlowController_EnabledBeforeViews_StillShowsStartView()
        {
            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            _cleanup.Add(graph);
            StartNode start = graph.AddNode<StartNode>("Start");
            UINode menu = graph.AddNode<UINode>("Menu");
            menu.showViews.Add(new UINode.ViewRef("Race", "Main"));
            start.outputs.Add(new FlowEdge { toNode = "Menu" });
            graph.startNode = "Start";

            // controller FIRST — its OnEnable runs while zero views are registered
            var controllerGo = Track(new GameObject("Controller", typeof(FlowController)));
            controllerGo.GetComponent<FlowController>().flow = graph;

            var viewGo = Track(new GameObject("View", typeof(RectTransform), typeof(CanvasGroup)));
            var view = viewGo.AddComponent<UIView>();
            view.id = new ViewId("Race", "Main");
            view.onStartBehaviour = ContainerStartBehaviour.InstantHide;

            yield return null; // Start() phase: views hide themselves, controller starts the flow
            yield return null;

            Assert.IsTrue(
                view.visibilityState == VisibilityState.Visible ||
                view.visibilityState == VisibilityState.IsShowing,
                $"start view must show even when the controller enabled first (state: {view.visibilityState})");
        }

        /// <summary> Stepper value labels must follow the value — a frozen label reads as broken buttons. </summary>
        [UnityTest]
        public IEnumerator StepperValueLabel_FollowsSteps()
        {
            var stepperGo = Track(new GameObject("Stepper", typeof(RectTransform)));
            var stepper = stepperGo.AddComponent<UIStepper>();
            stepper.minValue = 0f;
            stepper.maxValue = 10f;
            stepper.stepSize = 2f;
            stepper.wholeNumbers = true;

            var labelGo = new GameObject("Value", typeof(RectTransform));
            labelGo.transform.SetParent(stepperGo.transform, worldPositionStays: false);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            labelGo.AddComponent<UIStepperValueLabel>(); // auto-finds stepper in parents + label here

            yield return null;
            Assert.AreEqual("0", label.text, "label must show the initial value");

            stepper.StepUp();
            Assert.AreEqual("2", label.text, "label must follow StepUp");
            stepper.StepDown();
            Assert.AreEqual("0", label.text, "label must follow StepDown");
            stepper.StepDown();
            Assert.AreEqual("0", label.text, "label must respect the min clamp");
        }

        /// <summary>
        /// A view laid out at an editor-only offset (scene builder spreads views side-by-side, then
        /// relies on customStartPosition to snap them back at runtime) must show at customStartPosition,
        /// NOT at the offset. The hazard: the animator captures its StartValue endpoints in its own
        /// Awake, which can run before the container's position snap (component Awake order is
        /// undefined), baking the offset into the slide-in target and sending the shown view off-screen
        /// (clicks then land on the wrong/off-screen view — the original "wrong menu, can't go back"
        /// bug). The container must re-establish the rest pose and have animators recapture before the
        /// first transition.
        /// </summary>
        [UnityTest]
        public IEnumerator CustomStartPosition_CorrectsStaleAnimatorCapture_ShowsAtRestPose()
        {
            var go = Track(new GameObject("View", typeof(RectTransform), typeof(CanvasGroup)));
            var rect = (RectTransform)go.transform;
            var view = go.AddComponent<UIView>();
            view.id = new ViewId("Race", "Custom");
            view.onStartBehaviour = ContainerStartBehaviour.Disabled; // no auto transition — we drive it
            view.useCustomStartPosition = true;
            view.customStartPosition = Vector3.zero;

            var animator = go.AddComponent<UIContainerUIAnimator>();
            animator.showAnimation.move.enabled = true;
            animator.showAnimation.move.fromDirection = UIMoveDirection.Left;
            animator.showAnimation.move.toDirection = UIMoveDirection.CustomPosition;
            animator.showAnimation.move.toReference = ReferenceValue.StartValue; // slide-in ends at the rest pose

            yield return null; // OnEnable registers the animator with the view

            // Reproduce the Awake race deterministically: the animator captured the editor layout
            // offset as its rest pose, then the container's snap returned the rect to (0,0,0).
            var offset = new Vector3(1200f, 0f, 0f);
            rect.anchoredPosition3D = offset;
            animator.RecaptureStartValues();
            Assert.AreEqual(offset, animator.showAnimation.startPosition,
                "precondition: the stale capture holds the editor layout offset");
            rect.anchoredPosition3D = Vector3.zero;

            // First transition re-establishes the rest pose and recaptures the animators...
            view.InstantHide();
            Assert.AreEqual(Vector3.zero, animator.showAnimation.startPosition,
                "the first transition must correct the stale StartValue to customStartPosition");

            // ...so showing lands the view at customStartPosition, not the off-screen offset.
            view.InstantShow();
            Assert.AreEqual(Vector3.zero, rect.anchoredPosition3D,
                "shown view must land at customStartPosition, not the editor layout offset");
        }

        /// <summary> SetCustomValue keeps the authored start state instead of wiping the bar to empty. </summary>
        [UnityTest]
        public IEnumerator Progressor_SetCustomValue_StartsAtAuthoredValue()
        {
            var go = Track(new GameObject("Progressor", typeof(RectTransform)));
            var progressor = go.AddComponent<Progressor>();
            progressor.fromValue = 0f;
            progressor.toValue = 100f;
            progressor.onStartBehaviour = Progressor.StartBehaviour.SetCustomValue;
            progressor.startValue = 72f;

            yield return null; // Start()

            Assert.AreEqual(72f, progressor.currentValue, 0.001f,
                "progressor must start at the authored value, not reset to fromValue");
            Assert.AreEqual(0.72f, progressor.progress, 0.001f);
        }
    }
}
