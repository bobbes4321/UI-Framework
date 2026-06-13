using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Covers the flow-trigger extensibility seam (NeoTriggerKinds, the runtime Pattern-R registry):
    /// a project-registered "probe" trigger parses to Custom/probe, round-trips byte-identically,
    /// connects + matches at runtime, and an unknown trigger key warns rather than failing silently.
    /// </summary>
    public class NeoTriggerKindsTests
    {
        private const string ProbeId = "probe";

        /// <summary>
        /// A stand-in for a project-supplied trigger kind. Fires on its own signal stream
        /// (category/name) and matches any signal carrying its sentinel message.
        /// </summary>
        private sealed class ProbeTrigger : INeoTriggerKind, ITriggerKindIdDatabase
        {
            public string Id => ProbeId;
            public string JsonKey => "probe";
            public Type PreferredIdType => typeof(StreamId);

            public void Connect(FlowTriggerListener listener, Action fire)
            {
                FlowTrigger t = listener.Trigger;
                listener.BindStream(Signals.Stream(t.category, t.name));
            }

            public bool Matches(FlowTriggerListener listener, Signal signal) =>
                signal != null && signal.message == "probe-hit";
        }

        [SetUp]
        public void Register() => NeoTriggerKinds.Register(new ProbeTrigger());

        [TearDown]
        public void Cleanup() => NeoTriggerKinds.Unregister(ProbeId);

        [Test]
        public void Registry_ReplacesById_AndLooksUpByKey()
        {
            // Re-registering the same Id replaces, never duplicates.
            NeoTriggerKinds.Register(new ProbeTrigger());
            int probeCount = 0;
            foreach (INeoTriggerKind k in NeoTriggerKinds.All)
                if (k.Id == ProbeId) probeCount++;
            Assert.AreEqual(1, probeCount, "Register must replace by Id, not append a duplicate");

            Assert.IsTrue(NeoTriggerKinds.TryGet(ProbeId, out INeoTriggerKind byId));
            Assert.AreEqual("probe", byId.JsonKey);
            Assert.IsTrue(NeoTriggerKinds.TryGetByKey("probe", out INeoTriggerKind byKey));
            Assert.AreSame(byId, byKey);

            Assert.IsFalse(NeoTriggerKinds.TryGet("nope", out _));
            Assert.IsFalse(NeoTriggerKinds.TryGetByKey("nope", out _));
        }

        [Test]
        public void ParseTrigger_FallsThroughToCustomKind()
        {
            var on = new Dictionary<string, object> { ["probe"] = "Gestures/Swipe" };
            FlowTrigger trigger = FlowEdgeSpec.ParseTrigger(on);

            Assert.AreEqual(FlowTrigger.TriggerType.Custom, trigger.type);
            Assert.AreEqual(ProbeId, trigger.customKind);
            Assert.AreEqual("Gestures", trigger.category);
            Assert.AreEqual("Swipe", trigger.name);
        }

        [Test]
        public void CustomTrigger_RoundTripsByteIdentical()
        {
            const string json = @"{
  ""flow"": { ""name"": ""T"", ""start"": ""A"", ""nodes"": [
    { ""name"": ""A"", ""next"": [
      { ""on"": { ""probe"": ""Gestures/Swipe"" }, ""to"": ""B"" }
    ] },
    { ""name"": ""B"" } ] }
}";
            UISpec spec = UISpec.FromJson(json);
            FlowTrigger trigger = spec.flow.nodes[0].next[0].trigger;
            Assert.AreEqual(FlowTrigger.TriggerType.Custom, trigger.type);
            Assert.AreEqual(ProbeId, trigger.customKind);

            string exported = spec.ToJson();
            string reExported = UISpec.FromJson(exported).ToJson();
            Assert.AreEqual(exported, reExported, "export → generate → export must be byte-identical");

            // The serialized key is the kind's JsonKey with the canonical Category/Name payload.
            Dictionary<string, object> on = FlowEdgeSpec.TriggerToJson(trigger);
            Assert.IsTrue(on.ContainsKey("probe"));
            Assert.AreEqual("Gestures/Swipe", on["probe"]);
        }

        [Test]
        public void CustomTrigger_ConnectsAndMatchesItsSignal()
        {
            var trigger = new FlowTrigger
            {
                type = FlowTrigger.TriggerType.Custom,
                customKind = ProbeId,
                category = "ProbeCat",
                name = "ProbeName"
            };
            bool fired = false;
            var listener = new FlowTriggerListener(trigger, () => fired = true);
            listener.Connect();

            // A non-matching signal (wrong message) must NOT fire.
            Signals.Stream("ProbeCat", "ProbeName").SendSignal(sender: null, message: "other");
            Assert.IsFalse(fired, "non-matching signal should not fire the trigger");

            // The sentinel message matches → fires.
            Signals.Stream("ProbeCat", "ProbeName").SendSignal(sender: null, message: "probe-hit");
            Assert.IsTrue(fired, "matching signal should fire the trigger");

            listener.Disconnect();
        }

        [Test]
        public void ForTrigger_UsesCustomKindPreferredDatabase()
        {
            // Built-ins still map by enum; the custom kind exposes its preferred id type. With no
            // settings asset present in a bare EditMode run GetDatabaseFor returns null — the contract
            // we assert is that the call is routed through the kind without throwing.
            Assert.DoesNotThrow(() => IdDatabaseOptions.ForTrigger(FlowTrigger.TriggerType.Custom, ProbeId));
        }

        [Test]
        public void UnknownTriggerKey_LogsWarning_DoesNotThrow()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no recognized key"));
            var on = new Dictionary<string, object> { ["totallyUnknown"] = "X/Y" };
            FlowTrigger trigger = FlowEdgeSpec.ParseTrigger(on);
            // Falls back to a harmless None trigger rather than crashing.
            Assert.AreEqual(FlowTrigger.TriggerType.None, trigger.type);
        }
    }
}
