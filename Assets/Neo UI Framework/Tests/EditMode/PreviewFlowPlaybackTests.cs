using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Drives the Composer's live-flow-playback controller headlessly (Pillar G §G.2.3), mirroring
    /// <see cref="GeneratedFlowPlaythroughTests"/> at the unit level: start the flow in-memory, simulate a
    /// click on a flow-triggering button, and assert the expected node/view becomes active. No graphics
    /// device needed — the controller is graphics-free by design (the render pane does the drawing).
    ///
    /// EditMode on purpose: a button click → flow advance is synchronous static signal dispatch
    /// (<see cref="UIButton.Click"/> → <c>Signals.Send</c> → the active node's <c>FlowTrigger</c> listener →
    /// <c>Advance</c>), which needs no play-mode ticking. View visibility is asserted at the flow-graph
    /// level (the active node's <c>showViews</c>) — exactly what <see cref="PreviewFlowPlayback"/> exposes.
    /// </summary>
    public class PreviewFlowPlaybackTests
    {
        private const string SpecJson = @"{
          ""views"": [
            { ""id"": ""Menu/Main"", ""elements"": [
              { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
                { ""button"": { ""id"": ""Menu/Play"", ""label"": ""Play"" } }
              ] } }
            ] },
            { ""id"": ""Game/HUD"", ""elements"": [
              { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
                { ""button"": { ""id"": ""HUD/Pause"", ""label"": ""Pause"" } }
              ] } }
            ] }
          ],
          ""flow"": {
            ""name"": ""UI"", ""start"": ""MainMenu"",
            ""nodes"": [
              { ""name"": ""MainMenu"", ""view"": ""Menu/Main"",
                ""next"": [ { ""to"": ""Playing"", ""on"": { ""button"": ""Menu/Play"" } } ] },
              { ""name"": ""Playing"", ""view"": ""Game/HUD"",
                ""next"": [ { ""to"": ""MainMenu"", ""on"": { ""button"": ""HUD/Pause"" } } ] }
            ]
          }
        }";

        private UISpec _spec;
        private PreviewFlowPlayback _playback;

        [SetUp]
        public void Begin()
        {
            _spec = UISpec.FromJson(SpecJson);
            _playback = new PreviewFlowPlayback();
            Assert.IsTrue(_playback.Begin(_spec, out string error), error);
        }

        [TearDown]
        public void Teardown()
        {
            _playback?.Stop();
            _playback = null;
            _spec = null;
        }

        [Test]
        public void Begin_StartsOnTheFirstNode_ShowingItsView()
        {
            Assert.IsTrue(_playback.IsPlaying);
            Assert.AreEqual("MainMenu", _playback.CurrentNodeName, "the start edge advances to the first UI node");
            Assert.IsTrue(_playback.IsViewActive("Menu", "Main"));
            CollectionAssert.AreEqual(new[] { "Menu/Main" }, _playback.ActiveViewIds);
        }

        [Test]
        public void ClickingAFlowButton_AdvancesToTheRightNodeAndView()
        {
            Assert.AreEqual("MainMenu", _playback.CurrentNodeName);

            Assert.IsTrue(_playback.ClickById("Menu", "Play"), "the flow-triggering button must exist and fire");
            Assert.AreEqual("Playing", _playback.CurrentNodeName, "the click should advance the graph");
            Assert.IsTrue(_playback.IsViewActive("Game", "HUD"));
            Assert.IsFalse(_playback.IsViewActive("Menu", "Main"), "the previous node's view is no longer shown");

            // and the return edge brings us back
            Assert.IsTrue(_playback.ClickById("HUD", "Pause"));
            Assert.AreEqual("MainMenu", _playback.CurrentNodeName);
            Assert.IsTrue(_playback.IsViewActive("Menu", "Main"));
        }

        [Test]
        public void ClickElement_FiresTheInteractionOnThatSpecElement()
        {
            // the render pane maps a clicked box back to the document's own ElementSpec, then hands it here —
            // this pins that contract: clicking the Play button's element advances the flow.
            ElementSpec playButton = _spec.views[0].elements[0].children[0];
            Assert.AreEqual("button", playButton.kind);

            Assert.IsTrue(_playback.ClickElement(playButton), "clicking the button element fires its interaction");
            Assert.AreEqual("Playing", _playback.CurrentNodeName);
            Assert.IsTrue(_playback.IsViewActive("Game", "HUD"));
        }

        [Test]
        public void ClickingAMissingButton_WarnsAndDoesNotAdvance()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("no clickable element"));
            Assert.IsFalse(_playback.ClickById("Nope", "Missing"));
            Assert.AreEqual("MainMenu", _playback.CurrentNodeName, "an unmatched click never moves the flow");
        }

        [Test]
        public void Stop_TearsDownAndStopsReporting()
        {
            _playback.Stop();
            Assert.IsFalse(_playback.IsPlaying);
            Assert.IsNull(_playback.CurrentNodeName);
            CollectionAssert.IsEmpty(_playback.ActiveViewIds);
        }

        [Test]
        public void Begin_WithNoFlow_ReturnsFalseWithoutCrashing()
        {
            var noFlow = new PreviewFlowPlayback();
            UISpec spec = UISpec.FromJson(@"{ ""views"": [ { ""id"": ""A/B"", ""elements"": [] } ] }");
            Assert.IsFalse(noFlow.Begin(spec, out string error), "a spec with no flow can't be played");
            Assert.IsFalse(string.IsNullOrEmpty(error), "the refusal must say why (no silent failure)");
            Assert.IsFalse(noFlow.IsPlaying);
            noFlow.Stop();
        }
    }
}
