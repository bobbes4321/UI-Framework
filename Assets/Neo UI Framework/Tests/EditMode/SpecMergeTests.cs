using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Three-way spec merge (Plan 1, D): the "no lost work" guarantee. Verifies the canonical
    /// three-way matrix, the conflict-policy variants and that both sides' additions survive.
    /// Pure model — no Unity assets.
    /// </summary>
    public class SpecMergeTests
    {
        private static string ViewWithButtonLabel(string label) => $@"{{
          ""views"": [ {{ ""id"": ""Spec/Main"", ""elements"": [
            {{ ""button"": {{ ""id"": ""Spec/A"", ""label"": ""{label}"" }} }}
          ] }} ]
        }}";

        private static UISpec Parse(string json) => UISpec.FromJson(json);

        [Test]
        public void OursOnly_Change_IsPreserved()
        {
            MergeResult r = SpecMerge.Merge(
                Parse(ViewWithButtonLabel("A")),  // base
                Parse(ViewWithButtonLabel("B")),  // ours (human edit)
                Parse(ViewWithButtonLabel("A"))); // theirs (stale)

            Assert.AreEqual("B", ButtonLabel(r.merged), "human edit must survive a stale incoming spec");
            Assert.IsEmpty(r.conflicts);
            Assert.IsTrue(r.applied.Any(c => c.path.EndsWith("/label")), "the folded human edit is recorded as applied");
        }

        [Test]
        public void TheirsOnly_Change_IsApplied()
        {
            MergeResult r = SpecMerge.Merge(
                Parse(ViewWithButtonLabel("A")),
                Parse(ViewWithButtonLabel("A")),
                Parse(ViewWithButtonLabel("C")));

            Assert.AreEqual("C", ButtonLabel(r.merged));
            Assert.IsEmpty(r.conflicts);
            Assert.IsEmpty(r.applied, "ours did not change, so nothing was folded in");
        }

        [Test]
        public void SameChange_BothSides_NoConflict()
        {
            MergeResult r = SpecMerge.Merge(
                Parse(ViewWithButtonLabel("A")),
                Parse(ViewWithButtonLabel("B")),
                Parse(ViewWithButtonLabel("B")));

            Assert.AreEqual("B", ButtonLabel(r.merged));
            Assert.IsEmpty(r.conflicts, "identical edits on both sides are not a conflict");
        }

        [Test]
        public void Conflict_DefaultsToTheirs_ButIsRecorded()
        {
            MergeResult r = SpecMerge.Merge(
                Parse(ViewWithButtonLabel("A")),
                Parse(ViewWithButtonLabel("B")),
                Parse(ViewWithButtonLabel("C")));

            Assert.AreEqual("C", ButtonLabel(r.merged), "PreferTheirs: agent intent wins on collision");
            Assert.AreEqual(1, r.conflicts.Count, "the collision must be surfaced, not swallowed");
            Assert.IsFalse(r.failed);
            StringAssert.EndsWith("/label", r.conflicts[0].path);
        }

        [Test]
        public void Conflict_PreferOurs_KeepsHumanValue()
        {
            MergeResult r = SpecMerge.Merge(
                Parse(ViewWithButtonLabel("A")),
                Parse(ViewWithButtonLabel("B")),
                Parse(ViewWithButtonLabel("C")),
                ConflictPolicy.PreferOurs);

            Assert.AreEqual("B", ButtonLabel(r.merged));
            Assert.AreEqual(1, r.conflicts.Count);
        }

        [Test]
        public void Conflict_FailPolicy_FlagsFailure()
        {
            MergeResult r = SpecMerge.Merge(
                Parse(ViewWithButtonLabel("A")),
                Parse(ViewWithButtonLabel("B")),
                Parse(ViewWithButtonLabel("C")),
                ConflictPolicy.Fail);

            Assert.IsTrue(r.failed, "Fail policy must flag the merge as failed when a conflict occurs");
            Assert.AreEqual(1, r.conflicts.Count);
        }

        [Test]
        public void Additions_FromBothSides_AllSurvive()
        {
            UISpec baseSpec = Parse(ViewWithButtonLabel("A"));

            UISpec ours = Parse(ViewWithButtonLabel("A"));
            ours.views[0].elements.Add(ElementOf("{ \"text\": { \"label\": \"OursOnly\" } }"));

            UISpec theirs = Parse(ViewWithButtonLabel("A"));
            theirs.views[0].elements.Add(ElementOf("{ \"toggle\": { \"id\": \"Spec/T\", \"label\": \"TheirsOnly\" } }"));

            MergeResult r = SpecMerge.Merge(baseSpec, ours, theirs);
            List<ElementSpec> merged = r.merged.views.Single(v => v.id == "Spec/Main").elements;

            Assert.IsTrue(merged.Any(e => e.kind == "text" && e.label == "OursOnly"), "human-added element survives");
            Assert.IsTrue(merged.Any(e => e.kind == "toggle" && e.id == "Spec/T"), "agent-added element survives");
            Assert.IsTrue(merged.Any(e => e.kind == "button" && e.id == "Spec/A"), "the common element is kept once");
        }

        [Test]
        public void OursOnly_View_Survives_StaleIncoming()
        {
            UISpec baseSpec = Parse(ViewWithButtonLabel("A"));
            UISpec ours = Parse(ViewWithButtonLabel("A"));
            ours.views.Add(ViewOf("Spec/Added"));
            UISpec theirs = Parse(ViewWithButtonLabel("A")); // unaware of the new view

            MergeResult r = SpecMerge.Merge(baseSpec, ours, theirs);
            Assert.IsTrue(r.merged.views.Any(v => v.id == "Spec/Added"), "a human-added view must not be dropped");
        }

        // ----------------------------------------------------------------- Pillar B: breakpoints

        private const string BreakpointBaseJson = @"{
          ""breakpoints"": [ { ""name"": ""wide"", ""when"": { ""minAspect"": 1.6 } } ],
          ""views"": [ { ""id"": ""Bp/Main"", ""elements"": [
            { ""panel"": { ""id"": ""Bp/Card"", ""layout"": { ""h"": ""left"", ""v"": ""top"" },
                           ""overrides"": { ""wide"": { ""h"": ""center"" } } } }
          ] } ]
        }";

        [Test]
        public void Breakpoint_OurRename_Survives_StaleIncoming()
        {
            // base + theirs share "wide"; ours renamed it to "huge". Keyed by name, the rename folds in
            // cleanly (no add+remove collision) so the human's rename survives the stale incoming spec.
            UISpec baseSpec = Parse(BreakpointBaseJson);
            UISpec ours = Parse(BreakpointBaseJson.Replace(@"""name"": ""wide""", @"""name"": ""huge"""));
            UISpec theirs = Parse(BreakpointBaseJson);

            MergeResult r = SpecMerge.Merge(baseSpec, ours, theirs);
            Assert.IsTrue(r.merged.breakpoints.Any(b => b.name == "huge"),
                "the human's breakpoint rename must survive a stale incoming spec");
            Assert.IsFalse(r.failed);
        }

        [Test]
        public void Breakpoint_TheirCondition_Change_IsApplied()
        {
            UISpec baseSpec = Parse(BreakpointBaseJson);
            UISpec ours = Parse(BreakpointBaseJson);
            UISpec theirs = Parse(BreakpointBaseJson.Replace(@"""minAspect"": 1.6", @"""minAspect"": 2.0"));

            MergeResult r = SpecMerge.Merge(baseSpec, ours, theirs);
            BreakpointSpec wide = r.merged.breakpoints.Single(b => b.name == "wide");
            Assert.AreEqual(2.0f, wide.when.minAspect, "an incoming condition tweak applies when ours didn't touch it");
            Assert.IsEmpty(r.conflicts);
        }

        private static string ButtonLabel(UISpec spec) =>
            FindElement(spec.views.Single(v => v.id == "Spec/Main").elements,
                e => e.kind == "button" && e.id == "Spec/A")?.label;

        private static ElementSpec FindElement(IEnumerable<ElementSpec> elements, System.Func<ElementSpec, bool> match)
        {
            foreach (ElementSpec e in elements)
            {
                if (match(e)) return e;
                ElementSpec child = FindElement(e.children, match);
                if (child != null) return child;
            }
            return null;
        }

        private static ElementSpec ElementOf(string json) =>
            ElementSpec.Parse(JsonReader.AsObject(MiniJson.Parse(json), "element"));

        private static ViewSpec ViewOf(string id) =>
            ViewSpec.Parse(JsonReader.AsObject(MiniJson.Parse($"{{ \"id\": \"{id}\" }}"), "view"));
    }
}
