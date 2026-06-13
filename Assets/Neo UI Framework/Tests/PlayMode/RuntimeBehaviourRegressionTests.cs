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
