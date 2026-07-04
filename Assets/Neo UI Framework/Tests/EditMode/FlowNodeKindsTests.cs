using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Wave 7 Task 7.2: <see cref="FlowNodeKinds"/> is the registry that replaced
    /// <c>FlowGraphWindow</c>'s hand-listed <c>AddCreateEntry&lt;T&gt;()</c> calls and the
    /// <c>node is UINode || node is PortalNode || …</c> default-output type-check chain (audit E2).
    /// The GraphView-driven creation menu itself needs a live window and is out of scope for an
    /// EditMode test — these tests exercise the registry API directly: the built-ins are present and
    /// behave like the old switch, and a project-registered fake kind is creatable and seeds its own
    /// outputs.
    /// </summary>
    public class FlowNodeKindsTests
    {
        private const string FakeKind = "TestPingPongNode";

        [TearDown]
        public void Reset() => FlowNodeKinds.ResetForTests();

        [Test]
        public void All_ContainsExactlyTheElevenBuiltins()
        {
            var expected = new[]
            {
                "StartNode", "UINode", "SignalNode", "BackButtonNode", "PortalNode", "RandomNode",
                "TimeScaleNode", "ApplicationQuitNode", "PivotNode", "StickyNoteNode", "DebugNode",
            };
            CollectionAssert.AreEqual(expected, FlowNodeKinds.All.Select(d => d.id).ToArray());
        }

        [Test]
        public void Builtins_CreateDelegate_ProducesTheExpectedNodeType()
        {
            Assert.IsTrue(FlowNodeKinds.TryGet("StartNode", out FlowNodeDescriptor start));
            Assert.IsInstanceOf<StartNode>(start.create());

            Assert.IsTrue(FlowNodeKinds.TryGet("PivotNode", out FlowNodeDescriptor pivot));
            Assert.IsInstanceOf<PivotNode>(pivot.create());
            Assert.AreEqual("Reroute", pivot.menuLabel, "reroute keeps its historical menu label");
        }

        [TestCase("UINode")]
        [TestCase("PortalNode")]
        [TestCase("RandomNode")]
        public void Builtins_BareEdgeGroup_SeedsOneUnnamedOutput(string id)
        {
            Assert.IsTrue(FlowNodeKinds.TryGet(id, out FlowNodeDescriptor descriptor));
            FlowNode node = descriptor.create();
            descriptor.seedDefaultOutputs(node);
            Assert.AreEqual(1, node.outputs.Count);
            Assert.AreEqual("Next", node.outputs[0].portName, "FlowEdge's own field initializer defaults portName to Next");
        }

        [TestCase("StartNode")]
        [TestCase("SignalNode")]
        [TestCase("BackButtonNode")]
        [TestCase("TimeScaleNode")]
        [TestCase("PivotNode")]
        [TestCase("DebugNode")]
        public void Builtins_NamedNextEdgeGroup_SeedsOneNextOutput(string id)
        {
            Assert.IsTrue(FlowNodeKinds.TryGet(id, out FlowNodeDescriptor descriptor));
            FlowNode node = descriptor.create();
            descriptor.seedDefaultOutputs(node);
            Assert.AreEqual(1, node.outputs.Count);
            Assert.AreEqual("Next", node.outputs[0].portName);
        }

        [TestCase("ApplicationQuitNode")]
        [TestCase("StickyNoteNode")]
        public void Builtins_NoOutputGroup_SeedsNoOutputs(string id)
        {
            Assert.IsTrue(FlowNodeKinds.TryGet(id, out FlowNodeDescriptor descriptor));
            FlowNode node = descriptor.create();
            descriptor.seedDefaultOutputs(node);
            Assert.AreEqual(0, node.outputs.Count);
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(FlowNodeKinds.TryGet("not-a-real-node-kind", out _));
        }

        [Test]
        public void Register_NovelKind_IsCreatable_AndSeedsItsOwnDefaultOutputs()
        {
            int before = FlowNodeKinds.All.Count;

            FlowNodeKinds.Register(new FlowNodeDescriptor(
                FakeKind, "PingPong",
                () => new DebugNode(), // stand-in FlowNode subtype — the fake kind proves the seam, not a new node type
                node => node.outputs.Add(new FlowEdge { portName = "Ping" })));

            Assert.AreEqual(before + 1, FlowNodeKinds.All.Count, "a novel id appends");
            Assert.IsTrue(FlowNodeKinds.TryGet(FakeKind, out FlowNodeDescriptor descriptor));
            Assert.AreEqual("PingPong", descriptor.menuLabel);

            FlowNode node = descriptor.create();
            Assert.IsInstanceOf<DebugNode>(node);
            Assert.AreEqual(0, node.outputs.Count, "create() alone must not seed outputs");

            descriptor.seedDefaultOutputs(node);
            Assert.AreEqual(1, node.outputs.Count);
            Assert.AreEqual("Ping", node.outputs[0].portName, "the registered seeding delegate must run");
        }

        [Test]
        public void Register_SameId_ReplacesInPlace_NeverDuplicates()
        {
            int before = FlowNodeKinds.All.Count;

            FlowNodeKinds.Register(new FlowNodeDescriptor(FakeKind, "First", () => new DebugNode(), null));
            Assert.AreEqual(before + 1, FlowNodeKinds.All.Count);

            FlowNodeKinds.Register(new FlowNodeDescriptor(FakeKind, "Second", () => new DebugNode(), null));
            Assert.AreEqual(before + 1, FlowNodeKinds.All.Count, "same id replaces, never duplicates");
            Assert.IsTrue(FlowNodeKinds.TryGet(FakeKind, out FlowNodeDescriptor got));
            Assert.AreEqual("Second", got.menuLabel);
        }

        [Test]
        public void Register_NullCreateDelegate_WarnsAndIgnores_NeverThrows()
        {
            int before = FlowNodeKinds.All.Count;
            LogAssert.Expect(LogType.Warning, new Regex("FlowNodeKinds: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => FlowNodeKinds.Register(new FlowNodeDescriptor(FakeKind, "Bad", null, null)));

            Assert.AreEqual(before, FlowNodeKinds.All.Count, "an invalid registration must not add a row");
        }

        [Test]
        public void ResetForTests_ClearsRegistrations_AndRestoresExactlyTheElevenBuiltins()
        {
            FlowNodeKinds.Register(new FlowNodeDescriptor(FakeKind, "Debug", () => new DebugNode(), null));
            Assert.IsTrue(FlowNodeKinds.TryGet(FakeKind, out _));

            FlowNodeKinds.ResetForTests();

            Assert.IsFalse(FlowNodeKinds.TryGet(FakeKind, out _), "reset drops project registrations");
            Assert.AreEqual(11, FlowNodeKinds.All.Count, "reset re-seeds exactly the 11 built-ins");
        }
    }
}
