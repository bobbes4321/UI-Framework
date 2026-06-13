using System.Collections.Generic;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    public class SpecTests
    {
        [Test]
        public void MiniJson_ParsesPrimitives()
        {
            Assert.AreEqual(true, MiniJson.Parse("true"));
            Assert.AreEqual(false, MiniJson.Parse("false"));
            Assert.IsNull(MiniJson.Parse("null"));
            Assert.AreEqual(42d, MiniJson.Parse("42"));
            Assert.AreEqual(-3.5d, MiniJson.Parse("-3.5"));
            Assert.AreEqual("hi", MiniJson.Parse("\"hi\""));
            Assert.AreEqual("a\"b\n", MiniJson.Parse("\"a\\\"b\\n\""));
        }

        [Test]
        public void MiniJson_ParsesNestedStructures()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse("{ \"a\": [1, {\"b\": \"c\"}], \"d\": {} }");
            var array = (List<object>)root["a"];
            Assert.AreEqual(1d, array[0]);
            Assert.AreEqual("c", ((Dictionary<string, object>)array[1])["b"]);
            Assert.AreEqual(0, ((Dictionary<string, object>)root["d"]).Count);
        }

        [Test]
        public void MiniJson_RoundTrips()
        {
            const string json = "{\"name\":\"x\",\"value\":1.5,\"flag\":true,\"items\":[\"a\",\"b\"],\"nothing\":null}";
            object parsed = MiniJson.Parse(json);
            string serialized = MiniJson.Serialize(parsed, pretty: false);
            object reparsed = MiniJson.Parse(serialized);
            Assert.AreEqual(MiniJson.Serialize(parsed, false), MiniJson.Serialize(reparsed, false));
        }

        [Test]
        public void MiniJson_ThrowsOnMalformedInput()
        {
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("{ \"a\": }"));
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("[1, 2"));
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("{\"a\":1} trailing"));
        }

        // The worked example from the feature spec (§13), translated to the JSON schema.
        public const string WorkedExampleJson = @"{
  ""theme"": {
    ""tokens"": { ""Primary"": ""#3A86FF"", ""Background"": ""#14213D"", ""TextDefault"": ""#FFFFFF"" }
  },
  ""presets"": [
    { ""name"": ""SlideInLeft"",  ""type"": ""Show"", ""move"": { ""from"": ""Left"" }, ""fade"": { ""from"": 0 }, ""duration"": 0.3, ""ease"": ""OutCubic"" },
    { ""name"": ""SlideOutLeft"", ""type"": ""Hide"", ""move"": { ""to"": ""Left"" },   ""fade"": { ""to"": 0 },   ""duration"": 0.2, ""ease"": ""InCubic"" }
  ],
  ""views"": [
    { ""id"": ""Menu/Main"",
      ""showAnimation"": ""SlideInLeft"",
      ""hideAnimation"": ""SlideOutLeft"",
      ""background"": ""Background"",
      ""elements"": [
        { ""button"": { ""id"": ""Action/Play"", ""label"": ""Play"", ""labelColor"": ""TextDefault"", ""background"": ""Primary"",
                        ""onClick"": { ""signal"": { ""category"": ""Gameplay"", ""name"": ""StartPainting"" } } } },
        { ""button"": { ""id"": ""Action/Settings"", ""label"": ""Settings"", ""labelColor"": ""TextDefault"", ""background"": ""Primary"" } }
      ] },
    { ""id"": ""Menu/Settings"",
      ""showAnimation"": ""SlideInLeft"",
      ""hideAnimation"": ""SlideOutLeft"" }
  ],
  ""flow"": {
    ""name"": ""TestUI"",
    ""start"": ""MainMenu"",
    ""nodes"": [
      { ""name"": ""MainMenu"", ""view"": ""Menu/Main"",
        ""next"": [ { ""on"": { ""button"": ""Action/Settings"" }, ""to"": ""Settings"" } ] },
      { ""name"": ""Settings"", ""view"": ""Menu/Settings"",
        ""next"": [ { ""on"": { ""back"": true }, ""to"": ""MainMenu"" } ] }
    ]
  }
}";

        [Test]
        public void UISpec_ParsesWorkedExample()
        {
            UISpec spec = UISpec.FromJson(WorkedExampleJson);

            Assert.AreEqual(3, spec.theme.tokens.Count);
            Assert.AreEqual("#3A86FF", spec.theme.tokens["Primary"]);

            Assert.AreEqual(2, spec.presets.Count);
            PresetSpec show = spec.presets[0];
            Assert.AreEqual("SlideInLeft", show.name);
            Assert.AreEqual("Show", show.type);
            Assert.AreEqual("Left", show.move.from);
            Assert.AreEqual("0", show.fade.from);
            Assert.That(show.duration, Is.EqualTo(0.3f).Within(1e-4f));

            Assert.AreEqual(2, spec.views.Count);
            ViewSpec main = spec.views[0];
            Assert.AreEqual("Menu", main.category);
            Assert.AreEqual("Main", main.viewName);
            Assert.AreEqual("SlideInLeft", main.showAnimation);
            Assert.AreEqual(2, main.elements.Count);
            Assert.AreEqual("button", main.elements[0].kind);
            Assert.AreEqual("Action/Play", main.elements[0].id);
            Assert.AreEqual("Gameplay", main.elements[0].onClickSignal.category);
            Assert.AreEqual("StartPainting", main.elements[0].onClickSignal.name);

            Assert.IsNotNull(spec.flow);
            Assert.AreEqual("MainMenu", spec.flow.start);
            Assert.AreEqual(2, spec.flow.nodes.Count);
            FlowEdgeSpec edge = spec.flow.nodes[0].next[0];
            Assert.AreEqual("Settings", edge.to);
            Assert.AreEqual(FlowTrigger.TriggerType.ButtonClick, edge.trigger.type);
            Assert.AreEqual("Action", edge.trigger.category);
            Assert.AreEqual("Settings", edge.trigger.name);
            Assert.AreEqual(FlowTrigger.TriggerType.Back, spec.flow.nodes[1].next[0].trigger.type);
        }

        [Test]
        public void UISpec_RoundTripsThroughJson()
        {
            UISpec original = UISpec.FromJson(WorkedExampleJson);
            string exported = original.ToJson();
            UISpec reparsed = UISpec.FromJson(exported);

            Assert.AreEqual(original.theme.tokens.Count, reparsed.theme.tokens.Count);
            Assert.AreEqual(original.presets.Count, reparsed.presets.Count);
            Assert.AreEqual(original.views.Count, reparsed.views.Count);
            Assert.AreEqual(original.views[0].elements.Count, reparsed.views[0].elements.Count);
            Assert.AreEqual(original.flow.nodes.Count, reparsed.flow.nodes.Count);
            Assert.AreEqual(original.flow.start, reparsed.flow.start);
            Assert.AreEqual(
                original.flow.nodes[0].next[0].trigger.type,
                reparsed.flow.nodes[0].next[0].trigger.type);
            Assert.AreEqual("Action/Play", reparsed.views[0].elements[0].id);
        }

        [Test]
        public void FlowTrigger_AllTriggerKindsRoundTrip()
        {
            const string json = @"{
  ""flow"": { ""name"": ""T"", ""start"": ""A"", ""nodes"": [
    { ""name"": ""A"", ""next"": [
      { ""on"": { ""button"": ""C/B"" }, ""to"": ""B"" },
      { ""on"": { ""signal"": ""Cat/Sig"" }, ""to"": ""B"" },
      { ""on"": { ""toggleOn"": ""C/T"" }, ""to"": ""B"" },
      { ""on"": { ""toggleOff"": ""C/T"" }, ""to"": ""B"" },
      { ""on"": { ""viewShown"": ""C/V"" }, ""to"": ""B"" },
      { ""on"": { ""viewHidden"": ""C/V"" }, ""to"": ""B"" },
      { ""on"": { ""back"": true }, ""to"": ""B"" },
      { ""on"": { ""timer"": 2.5 }, ""to"": ""B"" }
    ] },
    { ""name"": ""B"" } ] }
}";
            UISpec spec = UISpec.FromJson(json);
            List<FlowEdgeSpec> edges = spec.flow.nodes[0].next;
            Assert.AreEqual(FlowTrigger.TriggerType.ButtonClick, edges[0].trigger.type);
            Assert.AreEqual(FlowTrigger.TriggerType.Signal, edges[1].trigger.type);
            Assert.AreEqual("Cat", edges[1].trigger.category);
            Assert.AreEqual(FlowTrigger.TriggerType.ToggleOn, edges[2].trigger.type);
            Assert.AreEqual(FlowTrigger.TriggerType.ToggleOff, edges[3].trigger.type);
            Assert.AreEqual(FlowTrigger.TriggerType.ViewShown, edges[4].trigger.type);
            Assert.AreEqual(FlowTrigger.TriggerType.ViewHidden, edges[5].trigger.type);
            Assert.AreEqual(FlowTrigger.TriggerType.Back, edges[6].trigger.type);
            Assert.AreEqual(FlowTrigger.TriggerType.Timer, edges[7].trigger.type);
            Assert.That(edges[7].trigger.timerDuration, Is.EqualTo(2.5f).Within(1e-4f));

            // round-trip through export
            UISpec reparsed = UISpec.FromJson(spec.ToJson());
            for (int i = 0; i < edges.Count; i++)
                Assert.AreEqual(edges[i].trigger.type, reparsed.flow.nodes[0].next[i].trigger.type, $"edge {i}");
        }
    }
}
