using System.Collections;
using Neo.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    public class PopupAndFlowTests : PlayModeTestBase
    {
        private GameObject CreatePopupTemplate(string name)
        {
            var template = new GameObject(name, typeof(RectTransform));
            Track(template);
            var popup = template.AddComponent<UIPopup>();
            popup.popupName = name;
            popup.hideOnBackButton = false;
            template.SetActive(false); // template, not a live popup
            return template;
        }

        [UnityTest]
        public IEnumerator PopupQueue_ShowsOneAtATime_Fifo()
        {
            GameObject template = CreatePopupTemplate("Popup_Test");
            yield return null;

            UIPopup first = UIPopup.Get(template);
            UIPopup second = UIPopup.Get(template);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);

            first.Show();
            second.Show();
            yield return null;

            Assert.AreEqual(VisibilityState.Visible, first.visibilityState, "first popup should be visible");
            Assert.AreEqual(VisibilityState.Hidden, second.visibilityState, "second popup should wait in the queue");
            Assert.AreEqual(2, UIPopup.GetQueueLength());
            Assert.AreSame(first, UIPopup.GetCurrentPopup());

            first.Hide();
            yield return WaitUntil(() => second.visibilityState == VisibilityState.Visible, 5f, "second popup to show");
            Assert.AreSame(second, UIPopup.GetCurrentPopup());

            second.Hide();
            yield return WaitUntil(() => UIPopup.GetQueueLength() == 0, 5f, "queue to drain");

            yield return null; // deferred destroys
            Assert.IsTrue(first == null, "popups created via Get must destroy after being hidden");
            Assert.IsTrue(second == null);
        }

        [UnityTest]
        public IEnumerator Popup_FluentSetTexts_WritesLabels()
        {
            GameObject template = CreatePopupTemplate("Popup_Texts");
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(template.transform, false);
            labelGo.AddComponent<TMPro.TextMeshProUGUI>();
            yield return null;

            UIPopup popup = UIPopup.Get(template).SetTexts("Hello");
            Assert.AreEqual("Hello", popup.GetComponentInChildren<TMPro.TMP_Text>(true).text);

            popup.SetText(0, "Changed");
            Assert.AreEqual("Changed", popup.GetComponentInChildren<TMPro.TMP_Text>(true).text);

            Object.Destroy(popup.gameObject);
        }

        [UnityTest]
        public IEnumerator Popup_HideOnBackButton_Hides()
        {
            GameObject template = CreatePopupTemplate("Popup_Back");
            template.GetComponent<UIPopup>().hideOnBackButton = true;
            yield return null;

            UIPopup popup = UIPopup.Get(template);
            popup.Show();
            yield return null;
            Assert.AreEqual(VisibilityState.Visible, popup.visibilityState);

            BackButton.EnableByForce();
            BackButton.ForceFire();
            yield return WaitUntil(() => popup == null || popup.visibilityState == VisibilityState.Hidden, 5f, "popup to hide on back");
        }

        // ------------------------------------------------------------------ flow

        private FlowGraph BuildTestGraph()
        {
            var graph = ScriptableObject.CreateInstance<FlowGraph>();

            var start = graph.AddNode<StartNode>("Start");
            start.outputs.Add(new FlowEdge { toNode = "MainMenu", allowsBack = false });

            var mainMenu = graph.AddNode<UINode>("MainMenu");
            mainMenu.showViews.Add(new UINode.ViewRef("Menu", "Main"));
            mainMenu.hideShownViewsOnExit = true;
            mainMenu.outputs.Add(new FlowEdge
            {
                toNode = "Settings",
                trigger = new FlowTrigger { type = FlowTrigger.TriggerType.ButtonClick, category = "Action", name = "Settings" }
            });

            var settings = graph.AddNode<UINode>("Settings");
            settings.showViews.Add(new UINode.ViewRef("Menu", "Settings"));
            settings.hideShownViewsOnExit = true;
            settings.outputs.Add(new FlowEdge
            {
                toNode = "MainMenu",
                trigger = new FlowTrigger { type = FlowTrigger.TriggerType.Signal, category = "Test", name = "GoHome" }
            });

            var portal = graph.AddNode<PortalNode>("JumpToSettings");
            portal.trigger = new FlowTrigger { type = FlowTrigger.TriggerType.Signal, category = "Test", name = "Portal" };
            portal.outputs.Add(new FlowEdge { toNode = "Settings" });

            graph.startNode = "Start";
            return graph;
        }

        /// <summary> Creates a started controller (component added while inactive so flow is set before OnEnable). </summary>
        private FlowController CreateController(FlowGraph graph)
        {
            GameObject controllerGo = Track(new GameObject("FlowController"));
            controllerGo.SetActive(false);
            var controller = controllerGo.AddComponent<FlowController>();
            controller.flow = graph;
            controller.onEnableBehaviour = FlowController.ControllerBehaviour.StartFlow;
            controllerGo.SetActive(true);
            return controller;
        }

        private UIView CreateView(string category, string name)
        {
            GameObject go = CreateUIObject($"View_{category}_{name}");
            var view = go.AddComponent<UIView>();
            view.id = new ViewId(category, name);
            view.onStartBehaviour = ContainerStartBehaviour.InstantHide;
            return view;
        }

        [UnityTest]
        public IEnumerator Flow_NavigatesOnButtonSignal_AndGoesBack()
        {
            UIView mainView = CreateView("Menu", "Main");
            UIView settingsView = CreateView("Menu", "Settings");

            GameObject controllerGo = Track(new GameObject("FlowController"));
            controllerGo.SetActive(false);
            var controller = controllerGo.AddComponent<FlowController>();
            controller.flow = BuildTestGraph();
            controller.onEnableBehaviour = FlowController.ControllerBehaviour.StartFlow;
            yield return null; // views run Start (instant hide)

            controllerGo.SetActive(true);
            yield return null; // first start is deferred to Start() (the enable-order race fix)
            Assert.AreEqual(FlowGraphState.Playing, controller.graphState);
            Assert.AreEqual("MainMenu", controller.activeNode.name);
            yield return WaitUntil(() => mainView.visibilityState == VisibilityState.Visible, 5f, "main view to show");

            // a button click signal advances the flow
            GameObject buttonGo = CreateUIObject("SettingsButton");
            var button = buttonGo.AddComponent<UIButton>();
            button.id = new ButtonId("Action", "Settings");
            yield return null;
            button.Click();

            Assert.AreEqual("Settings", controller.activeNode.name);
            yield return WaitUntil(() => settingsView.visibilityState == VisibilityState.Visible, 5f, "settings view to show");
            Assert.AreEqual(VisibilityState.Hidden, mainView.visibilityState, "hideShownViewsOnExit should hide the previous view");

            // back navigation via the back-button signal
            BackButton.EnableByForce();
            BackButton.ForceFire(this);
            Assert.AreEqual("MainMenu", controller.activeNode.name, "back should return to the previous node");
            yield return WaitUntil(() => mainView.visibilityState == VisibilityState.Visible, 5f, "main view to show again");

            Object.Destroy(controller.flow);
        }

        [UnityTest]
        public IEnumerator Flow_PortalJumpsFromAnywhere()
        {
            CreateView("Menu", "Main");
            CreateView("Menu", "Settings");

            FlowController controller = CreateController(BuildTestGraph());
            yield return null;

            Assert.AreEqual("MainMenu", controller.activeNode.name);

            Signal.Send("Test", "Portal");
            Assert.AreEqual("Settings", controller.activeNode.name, "portal should jump the flow to Settings");

            Object.Destroy(controller.flow);
        }

        [UnityTest]
        public IEnumerator Flow_SetActiveNodeByName_Jumps()
        {
            CreateView("Menu", "Main");
            CreateView("Menu", "Settings");

            FlowController controller = CreateController(BuildTestGraph());
            yield return null;

            Assert.IsTrue(controller.SetActiveNodeByName("Settings"));
            Assert.AreEqual("Settings", controller.activeNode.name);
            Assert.IsFalse(controller.SetActiveNodeByName("DoesNotExist"));

            Object.Destroy(controller.flow);
        }

        [UnityTest]
        public IEnumerator Flow_TimerTriggerAdvances()
        {
            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            var start = graph.AddNode<StartNode>("Start");
            start.outputs.Add(new FlowEdge { toNode = "A", allowsBack = false });
            var nodeA = graph.AddNode<UINode>("A");
            nodeA.outputs.Add(new FlowEdge
            {
                toNode = "B",
                trigger = new FlowTrigger { type = FlowTrigger.TriggerType.Timer, timerDuration = 0.2f }
            });
            graph.AddNode<UINode>("B");
            graph.startNode = "Start";

            FlowController controller = CreateController(graph);
            yield return null;

            Assert.AreEqual("A", controller.activeNode.name);
            yield return WaitUntil(() => controller.activeNode.name == "B", 5f, "timer trigger to advance the flow");

            Object.Destroy(graph);
        }

        [UnityTest]
        public IEnumerator Progressor_AnimatesValueAndUpdatesTargets()
        {
            GameObject go = CreateUIObject("Progressor");
            var progressor = go.AddComponent<Progressor>();
            progressor.fromValue = 0f;
            progressor.toValue = 100f;
            progressor.settings.duration = 0.2f;
            progressor.settings.ease = Ease.Linear;
            progressor.onStartBehaviour = Progressor.StartBehaviour.SetFromValue;

            GameObject imageGo = CreateUIObject("Fill");
            var image = imageGo.AddComponent<UnityEngine.UI.Image>();
            image.type = UnityEngine.UI.Image.Type.Filled;
            var target = imageGo.AddComponent<ImageProgressTarget>();
            target.image = image;
            target.targetMode = ProgressTarget.Mode.Progress;
            progressor.progressTargets.Add(target);
            yield return null;

            float lastValue = -1f;
            progressor.OnValueChanged.AddListener(v => lastValue = v);

            progressor.Play();
            yield return WaitUntil(() => !progressor.isActive, 5f, "progressor to finish");

            Assert.That(progressor.currentValue, Is.EqualTo(100f).Within(0.1f));
            Assert.That(progressor.progress, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(image.fillAmount, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(lastValue, Is.EqualTo(100f).Within(0.1f));

            progressor.SetProgressAt(0.5f);
            Assert.That(progressor.currentValue, Is.EqualTo(50f).Within(0.1f));
            Assert.That(image.fillAmount, Is.EqualTo(0.5f).Within(1e-3f));
        }
    }
}
