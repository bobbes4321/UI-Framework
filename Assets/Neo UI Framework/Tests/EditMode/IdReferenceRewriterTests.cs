using System.Collections.Generic;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pure-logic tests for the rename "close the loop" rewriter: the shared <see cref="IdRefSlots"/>
    /// visitor that both the usage scanner and the rewriter drive off, and
    /// <see cref="IdReferenceRewriter.Rewrite"/>'s in-memory rewrite (name rename, category rename,
    /// id-type isolation, no-op). The project file I/O (<see cref="IdReferenceRewriter.Apply"/>) is not
    /// unit-tested here — the rewrite itself is the load-bearing logic and is exercised in memory.
    /// </summary>
    public class IdReferenceRewriterTests
    {
        // a spec touching most reference forms: element ids (button/toggle), onClick.showView (ViewId),
        // domain signal (StreamId), tab.controls (PanelId), a flow ButtonClick trigger, and a same-string
        // ViewId so id-type isolation can be checked against "Action/Play".
        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""Action/Play"", ""elements"": [
            { ""button"": { ""id"": ""Action/Play"", ""label"": ""Play"",
                            ""onClick"": { ""showView"": ""Action/Play"" } } },
            { ""toggle"": { ""id"": ""Audio/Music"", ""label"": ""Music"",
                            ""signal"": { ""category"": ""Audio"", ""name"": ""Muted"" } } },
            { ""tab"":    { ""id"": ""Nav/Home"", ""label"": ""Home"", ""controls"": ""Panels/Home"" } }
          ] } ],
          ""flow"": { ""name"": ""UI"", ""start"": ""Main"", ""nodes"": [
            { ""name"": ""Main"", ""view"": ""Action/Play"",
              ""next"": [ { ""on"": { ""button"": ""Action/Play"" }, ""to"": ""Main"" } ] }
          ] }
        }";

        private static UISpec Parse() => UISpec.FromJson(SpecJson);

        // count, per id-type, how many slots currently read a given Category/Name
        private static int CountSlots(UISpec spec, System.Type idType, string category, string name)
        {
            int count = 0;
            IdRefSlots.Visit(spec, slot =>
            {
                if (slot.IdType != idType) return;
                slot.Get(out string c, out string n);
                if (c == category && n == name) count++;
            });
            return count;
        }

        [Test]
        public void Rewrite_Name_RewritesMatchingReferencesOnly()
        {
            UISpec spec = Parse();
            var rename = IdReferenceRewriter.Rename.ForName(typeof(ButtonId), "Action", "Play", "Start");

            int rewritten = IdReferenceRewriter.Rewrite(spec, rename);

            // two ButtonId references match: the button element id + the flow ButtonClick trigger
            Assert.AreEqual(2, rewritten, "exactly the two ButtonId 'Action/Play' references move");
            Assert.AreEqual(0, CountSlots(spec, typeof(ButtonId), "Action", "Play"));
            Assert.AreEqual(2, CountSlots(spec, typeof(ButtonId), "Action", "Start"));
        }

        [Test]
        public void Rewrite_Name_IsIdTypeIsolated()
        {
            UISpec spec = Parse();
            // rename the ButtonId — the ViewId "Action/Play" (view id + onClick.showView + flow node view)
            // must be untouched even though it's the same string.
            var rename = IdReferenceRewriter.Rename.ForName(typeof(ButtonId), "Action", "Play", "Start");

            IdReferenceRewriter.Rewrite(spec, rename);

            // the ViewId "Action/Play" slots: view's own id, onClick.showView, the flow node view → 3
            Assert.AreEqual(3, CountSlots(spec, typeof(ViewId), "Action", "Play"),
                "a same-string reference of a DIFFERENT id-type must not be touched");
            Assert.AreEqual(0, CountSlots(spec, typeof(ViewId), "Action", "Start"));
        }

        [Test]
        public void Rewrite_Category_MovesEveryNameUnderItForThatIdType()
        {
            UISpec spec = Parse();
            // move ButtonId category "Action" → "Game"; ViewId "Action/*" must stay put.
            var rename = IdReferenceRewriter.Rename.ForCategory(typeof(ButtonId), "Action", "Game");

            int rewritten = IdReferenceRewriter.Rewrite(spec, rename);

            Assert.AreEqual(2, rewritten, "both ButtonId Action/* references move category");
            Assert.AreEqual(2, CountSlots(spec, typeof(ButtonId), "Game", "Play"));
            Assert.AreEqual(0, CountSlots(spec, typeof(ButtonId), "Action", "Play"));
            // id-type isolation under a category rename too
            Assert.AreEqual(3, CountSlots(spec, typeof(ViewId), "Action", "Play"),
                "ViewId Action/* untouched by a ButtonId category rename");
        }

        [Test]
        public void Rewrite_StreamSignal_RewritesPairForm()
        {
            UISpec spec = Parse();
            var rename = IdReferenceRewriter.Rename.ForName(typeof(StreamId), "Audio", "Muted", "Silenced");

            int rewritten = IdReferenceRewriter.Rewrite(spec, rename);

            Assert.AreEqual(1, rewritten, "the toggle's domain signal stream id");
            Assert.AreEqual(1, CountSlots(spec, typeof(StreamId), "Audio", "Silenced"));
        }

        [Test]
        public void Rewrite_PanelControls_Rewrites()
        {
            UISpec spec = Parse();
            var rename = IdReferenceRewriter.Rename.ForCategory(typeof(PanelId), "Panels", "Tabs");

            int rewritten = IdReferenceRewriter.Rewrite(spec, rename);

            Assert.AreEqual(1, rewritten, "the tab.controls PanelId reference");
            Assert.AreEqual(1, CountSlots(spec, typeof(PanelId), "Tabs", "Home"));
        }

        [Test]
        public void Rewrite_NoMatch_IsNoOp()
        {
            UISpec spec = Parse();
            var rename = IdReferenceRewriter.Rename.ForName(typeof(ButtonId), "Action", "Nonexistent", "X");

            int rewritten = IdReferenceRewriter.Rewrite(spec, rename);

            Assert.AreEqual(0, rewritten, "nothing matches → nothing rewritten");
            // round-trip is unchanged, so Apply's content-equality guard would skip the file write
            Assert.AreEqual(Parse().ToJson(), spec.ToJson(), "an unmatched rename must not mutate the spec");
        }

        [Test]
        public void Rewrite_NoOpRename_SameName_RewritesNothing()
        {
            UISpec spec = Parse();
            var rename = IdReferenceRewriter.Rename.ForName(typeof(ButtonId), "Action", "Play", "Play");

            int rewritten = IdReferenceRewriter.Rewrite(spec, rename);

            Assert.AreEqual(0, rewritten, "renaming to the same name is not effective");
        }

        [Test]
        public void IdRefSlots_Visit_MatchesCollectOutput()
        {
            // the refactor must not change what the usage scanner gathers: Collect now drives off the
            // same visitor, so the references it bucketizes must include every expected id.
            var usage = new IdUsageScanner.Usage();
            IdUsageScanner.Collect(Parse(), usage);

            HashSet<IdUsageScanner.Ref> buttons = usage.For(typeof(ButtonId));
            CollectionAssert.Contains(buttons, new IdUsageScanner.Ref("Action", "Play"));
            CollectionAssert.Contains(usage.For(typeof(ViewId)), new IdUsageScanner.Ref("Action", "Play"));
            CollectionAssert.Contains(usage.For(typeof(ToggleId)), new IdUsageScanner.Ref("Audio", "Music"));
            CollectionAssert.Contains(usage.For(typeof(StreamId)), new IdUsageScanner.Ref("Audio", "Muted"));
            CollectionAssert.Contains(usage.For(typeof(PanelId)), new IdUsageScanner.Ref("Panels", "Home"),
                "tab.controls is the PanelId reference");
            CollectionAssert.Contains(usage.For(typeof(ToggleId)), new IdUsageScanner.Ref("Nav", "Home"),
                "a tab's own id is a ToggleId, not a PanelId");
        }
    }
}
