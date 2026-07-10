using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The Doozy-parity back system: any UIButton named "Back" (NeoUISettings.backButtonName) fires
    /// the back signal with no wiring, and FlowController treats history-back as the FALLBACK —
    /// explicit graph wiring (a Back-trigger edge, or a ButtonClick edge matching the pressed
    /// back-named button) consumes the press so nothing ever navigates twice.
    /// </summary>
    public class BackNavigationTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            Signals.ClearAll();
            BackButton.EnableByForce();
            BackButton.ResetCooldown();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            BackButton.EnableByForce();
            Signals.ClearAll();
        }

        private UIButton MakeButton(string category, string name)
        {
            var go = new GameObject($"Button {category}/{name}", typeof(RectTransform));
            _spawned.Add(go);
            UIButton button = go.AddComponent<UIButton>();
            button.id = new ButtonId(category, name);
            return button;
        }

        // ------------------------------------------------------------------ button bridge

        [Test]
        public void BackNamedButton_Click_FiresBackSignal()
        {
            BackButton.EnsureButtonBridge();
            int fired = 0;
            Signals.On(BackButton.StreamCategory, BackButton.StreamName, () => fired++);

            MakeButton("Menu", "Play").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual(0, fired, "an ordinary button must not fire back");

            MakeButton("Menu", "Back").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual(1, fired, "a button named 'Back' fires back with no wiring");

            BackButton.ResetCooldown();
            MakeButton("AnyCategory", "back").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual(2, fired, "the name match is case-insensitive and category-agnostic");
        }

        [Test]
        public void BackNamedButton_CarriesItsSourceOnTheBackSignal()
        {
            BackButton.EnsureButtonBridge();
            ButtonSignalData? source = null;
            Signals.On(BackButton.StreamCategory, BackButton.StreamName,
                (Signal s) => source = s.TryGetValue(out ButtonSignalData data) ? data : (ButtonSignalData?)null);

            MakeButton("Menu", "Back").ExecuteTrigger(BehaviourTrigger.Click);

            Assert.IsTrue(source.HasValue, "button-originated back fires carry their ButtonSignalData");
            Assert.AreEqual("Menu", source.Value.category);
            Assert.AreEqual("Back", source.Value.buttonName);
        }

        [Test]
        public void BackBridge_RespectsDisableLevels()
        {
            BackButton.EnsureButtonBridge();
            int fired = 0;
            Signals.On(BackButton.StreamCategory, BackButton.StreamName, () => fired++);

            BackButton.Disable();
            MakeButton("Menu", "Back").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual(0, fired);
            BackButton.EnableByForce();
        }

        [Test]
        public void RegisterButtonName_ExtendsTheConvention()
        {
            BackButton.EnsureButtonBridge();
            int fired = 0;
            Signals.On(BackButton.StreamCategory, BackButton.StreamName, () => fired++);

            UIButton exit = MakeButton("Menu", "Return");
            exit.ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual(0, fired);

            BackButton.RegisterButtonName("Return");
            exit.ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual(1, fired);

            BackButton.UnregisterButtonName("Return");
            BackButton.ResetCooldown();
            exit.ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void IsBackButtonName_UsesConfiguredName()
        {
            Assert.IsTrue(BackButton.IsBackButtonName("Back"));
            Assert.IsTrue(BackButton.IsBackButtonName("back"));
            Assert.IsFalse(BackButton.IsBackButtonName("Play"));
            Assert.IsFalse(BackButton.IsBackButtonName(""));
            Assert.IsFalse(BackButton.IsBackButtonName(null));
        }

        // ------------------------------------------------------------------ flow precedence

        private FlowController StartFlow(FlowGraph graph)
        {
            var go = new GameObject("FlowController");
            _spawned.Add(go);
            var controller = go.AddComponent<FlowController>();
            controller.onEnableBehaviour = FlowController.ControllerBehaviour.DoNothing;
            controller.onDisableBehaviour = FlowController.ControllerBehaviour.StopFlow;
            controller.flow = graph;
            controller.StartFlow();
            return controller;
        }

        private static FlowGraph MakeGraph(params FlowNode[] nodes)
        {
            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            graph.nodes.AddRange(nodes);
            return graph;
        }

        private static FlowEdge ButtonEdge(string toNode, string category, string name) => new FlowEdge
        {
            portName = name,
            toNode = toNode,
            trigger = new FlowTrigger { type = FlowTrigger.TriggerType.ButtonClick, category = category, name = name }
        };

        private static FlowEdge BackEdge(string toNode) => new FlowEdge
        {
            portName = "Back",
            toNode = toNode,
            trigger = new FlowTrigger { type = FlowTrigger.TriggerType.Back }
        };

        [Test]
        public void BackNamedButton_NavigatesBackThroughHistory()
        {
            var start = new StartNode { name = "Start", outputs = { new FlowEdge { toNode = "A" } } };
            var a = new UINode { name = "A", outputs = { ButtonEdge("B", "Menu", "Play") } };
            var b = new UINode { name = "B" };
            FlowController controller = StartFlow(MakeGraph(start, a, b));
            Assert.AreEqual("A", controller.activeNode.name);

            MakeButton("Menu", "Play").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual("B", controller.activeNode.name);

            // no wiring on B at all — the back-named button walks the history
            MakeButton("Menu", "Back").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual("A", controller.activeNode.name);
            Assert.AreEqual(0, controller.history.Count);
        }

        [Test]
        public void ExplicitBackEdge_WinsOverHistoryBack()
        {
            var start = new StartNode { name = "Start", outputs = { new FlowEdge { toNode = "A" } } };
            var a = new UINode { name = "A", outputs = { ButtonEdge("B", "Menu", "Play") } };
            var b = new UINode { name = "B", outputs = { BackEdge("C") } };
            var c = new UINode { name = "C" };
            FlowController controller = StartFlow(MakeGraph(start, a, b, c));

            MakeButton("Menu", "Play").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual("B", controller.activeNode.name);

            BackButton.ResetCooldown();
            BackButton.Fire();
            Assert.AreEqual("C", controller.activeNode.name,
                "the explicit Back edge must consume the press instead of GoBack() double-navigating");
        }

        [Test]
        public void BackNamedButton_WithMatchingButtonClickEdge_NavigatesOnceViaTheEdge()
        {
            var start = new StartNode { name = "Start", outputs = { new FlowEdge { toNode = "A" } } };
            var a = new UINode { name = "A", outputs = { ButtonEdge("B", "Menu", "Play") } };
            var b = new UINode { name = "B", outputs = { ButtonEdge("C", "Menu", "Back") } };
            var c = new UINode { name = "C" };
            FlowController controller = StartFlow(MakeGraph(start, a, b, c));

            MakeButton("Menu", "Play").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual("B", controller.activeNode.name);

            BackButton.ResetCooldown();
            MakeButton("Menu", "Back").ExecuteTrigger(BehaviourTrigger.Click);
            Assert.AreEqual("C", controller.activeNode.name,
                "the explicit ButtonClick edge wired to the back-named button wins over history-back");
        }

        [Test]
        public void AlreadyAdvancedRecord_SuppressesOnlyTheSamePress()
        {
            var start = new StartNode { name = "Start", outputs = { new FlowEdge { toNode = "A" } } };
            var a = new UINode { name = "A", outputs = { ButtonEdge("B", "Menu", "Play") } };
            var b = new UINode { name = "B", outputs = { ButtonEdge("C", "Menu", "Back") } };
            var c = new UINode { name = "C" };
            FlowController controller = StartFlow(MakeGraph(start, a, b, c));
            MakeButton("Menu", "Play").ExecuteTrigger(BehaviourTrigger.Click);

            // simulate the edge's listener running FIRST within the button dispatch: advance across
            // the explicit edge, then deliver the bridged back signal for the very same press
            FlowEdge backButtonEdge = b.outputs[0];
            controller.Advance(backButtonEdge);
            Assert.AreEqual("C", controller.activeNode.name);
            Signals.Send(BackButton.StreamCategory, BackButton.StreamName,
                new ButtonSignalData { category = "Menu", buttonName = "Back", trigger = BehaviourTrigger.Click });
            Assert.AreEqual("C", controller.activeNode.name,
                "the same press must not ALSO walk the history after its edge advanced");

            // a fresh press is a new event — the consumed record must not suppress it
            Signals.Send(BackButton.StreamCategory, BackButton.StreamName,
                new ButtonSignalData { category = "Menu", buttonName = "Back", trigger = BehaviourTrigger.Click });
            Assert.AreEqual("B", controller.activeNode.name, "the next press falls back to history-back");
        }
    }
}
