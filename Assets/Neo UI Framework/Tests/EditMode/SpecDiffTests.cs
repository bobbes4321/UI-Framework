using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Structural spec diff (Plan 1, A): round-trip identity, per-section single-field changes,
    /// element add/remove/modify and flow edge changes. Pure model — no Unity assets.
    /// </summary>
    public class SpecDiffTests
    {
        private const string BaseJson = @"{
          ""theme"": { ""tokens"": { ""Primary"": ""#FFFFFF"", ""Background"": ""#000000"" } },
          ""views"": [ { ""id"": ""Spec/Main"", ""elements"": [
            { ""button"": { ""id"": ""Spec/A"", ""label"": ""Play"", ""background"": ""Primary"" } },
            { ""text"": { ""label"": ""Hello"" } }
          ] } ],
          ""flow"": { ""name"": ""UI"", ""start"": ""MainMenu"", ""nodes"": [
            { ""name"": ""MainMenu"", ""view"": ""Spec/Main"",
              ""next"": [ { ""on"": { ""button"": ""Spec/A"" }, ""to"": ""Settings"" } ] },
            { ""name"": ""Settings"", ""view"": ""Spec/Main"" }
          ] }
        }";

        private static UISpec Parse(string json) => UISpec.FromJson(json);

        [Test]
        public void Compare_Identity_IsEmpty()
        {
            var changes = SpecDiff.Compare(Parse(BaseJson), Parse(BaseJson));
            Assert.IsEmpty(changes, "Compare(spec, spec) must report no changes");
        }

        [Test]
        public void ThemeToken_SingleFieldChange()
        {
            UISpec candidate = Parse(BaseJson.Replace("#FFFFFF", "#FF0000"));
            var changes = SpecDiff.Compare(Parse(BaseJson), candidate);

            Assert.AreEqual(1, changes.Count, "exactly one token changed");
            SpecChange change = changes[0];
            Assert.AreEqual(SpecChangeKind.Modified, change.kind);
            Assert.AreEqual("theme/tokens/Primary", change.path);
            Assert.AreEqual(SpecPath.ThemeSection, change.section);
            Assert.AreEqual("#FFFFFF", change.before);
            Assert.AreEqual("#FF0000", change.after);
            Assert.IsTrue(change.roundTrips, "a token recolor is fully representable in the spec");
        }

        [Test]
        public void ElementField_Modified()
        {
            UISpec candidate = Parse(BaseJson.Replace(@"""label"": ""Play""", @"""label"": ""Start"""));
            var changes = SpecDiff.Compare(Parse(BaseJson), candidate);

            SpecChange labelChange = changes.SingleOrDefault(c => c.path.EndsWith("/label") && c.before == "Play");
            Assert.IsNotNull(labelChange, "the button label change must be reported");
            Assert.AreEqual(SpecChangeKind.Modified, labelChange.kind);
            Assert.AreEqual("Start", labelChange.after);
            Assert.AreEqual(SpecPath.ViewSection, labelChange.section);
            StringAssert.StartsWith("views/Spec/Main/elements[0]", labelChange.path);
        }

        [Test]
        public void Element_Added_IsStructural()
        {
            UISpec baseline = Parse(BaseJson);
            UISpec candidate = Parse(BaseJson);
            candidate.views[0].elements.Add(ElementOf("{ \"toggle\": { \"id\": \"Spec/New\", \"label\": \"Extra\" } }"));

            var changes = SpecDiff.Compare(baseline, candidate);
            SpecChange added = changes.SingleOrDefault(c => c.kind == SpecChangeKind.Added);
            Assert.IsNotNull(added, "the added element must surface as one structural Added");
            Assert.AreEqual("(node)", added.after);
            Assert.AreEqual(SpecPath.ViewSection, added.section);
            StringAssert.Contains("elements[2]", added.path);
        }

        [Test]
        public void Element_Removed_IsStructural()
        {
            UISpec baseline = Parse(BaseJson);
            UISpec candidate = Parse(BaseJson);
            candidate.views[0].elements.RemoveAt(1); // drop the text element

            var changes = SpecDiff.Compare(baseline, candidate);
            SpecChange removed = changes.SingleOrDefault(c => c.kind == SpecChangeKind.Removed);
            Assert.IsNotNull(removed, "the removed element must surface as one structural Removed");
            Assert.AreEqual("(node)", removed.before);
            StringAssert.Contains("elements[1]", removed.path);
        }

        [Test]
        public void FlowEdge_Change_IsReported()
        {
            UISpec candidate = Parse(BaseJson.Replace(@"""to"": ""Settings""", @"""to"": ""Options"""));
            var changes = SpecDiff.Compare(Parse(BaseJson), candidate);

            Assert.IsTrue(changes.Any(c => c.section == SpecPath.FlowSection),
                "a re-pointed flow edge must show up in the flow section");
            Assert.IsTrue(changes.Any(c => c.kind == SpecChangeKind.Added)
                          && changes.Any(c => c.kind == SpecChangeKind.Removed),
                "re-pointing an edge reads as remove(old)+add(new) under edge identity");
        }

        // ----------------------------------------------------------------- Pillar B: breakpoints

        private const string BreakpointJson = @"{
          ""breakpoints"": [
            { ""name"": ""wide"", ""when"": { ""minAspect"": 1.6 } }
          ],
          ""views"": [ { ""id"": ""Bp/Main"", ""elements"": [
            { ""panel"": { ""id"": ""Bp/Card"", ""layout"": { ""h"": ""left"", ""v"": ""top"" },
                           ""overrides"": { ""wide"": { ""h"": ""center"" } } } }
          ] } ]
        }";

        [Test]
        public void Breakpoint_ConditionChange_DiffsAsModify_UnderStableName()
        {
            // Breakpoints key by name (SpecPath addition), like popups/presets/nodes. Changing a
            // breakpoint's CONDITION while keeping its name reads as a clean field modify addressed
            // under the breakpoint's stable name path — NOT a phantom add+remove. That is the value
            // of name-keying.
            UISpec candidate = Parse(BreakpointJson.Replace(@"""minAspect"": 1.6", @"""minAspect"": 3"));
            var changes = SpecDiff.Compare(Parse(BreakpointJson), candidate);

            // minAspect is a float? widened to double for JSON, so 1.6f serializes as "1.60000002…";
            // assert on the path + that the value changed, not on an exact float string.
            Assert.IsTrue(changes.Any(c => c.kind == SpecChangeKind.Modified
                                           && c.path.Contains("wide") && c.path.Contains("minAspect")
                                           && c.before.StartsWith("1.6") && c.after.StartsWith("3")),
                "a condition tweak surfaces as a modify under the breakpoint's stable name path");
            Assert.IsFalse(changes.Any(c => c.kind == SpecChangeKind.Added || c.kind == SpecChangeKind.Removed),
                "tweaking a same-named breakpoint must NOT read as add/remove");
        }

        [Test]
        public void Breakpoint_Rename_DiffsAsAddRemove_LikeNamedEntities()
        {
            // A name IS the identity for breakpoints (overrides reference it by name), so a rename is
            // legitimately remove(old)+add(new) — identical to the FlowEdge re-point convention above.
            UISpec candidate = Parse(BreakpointJson.Replace(@"""name"": ""wide""", @"""name"": ""huge"""));
            var changes = SpecDiff.Compare(Parse(BreakpointJson), candidate);

            Assert.IsTrue(changes.Any(c => c.kind == SpecChangeKind.Removed && c.path.Contains("breakpoints") && c.path.Contains("wide")),
                "the old breakpoint name reads as a remove");
            Assert.IsTrue(changes.Any(c => c.kind == SpecChangeKind.Added && c.path.Contains("breakpoints") && c.path.Contains("huge")),
                "the new breakpoint name reads as an add");
        }

        [Test]
        public void Override_Change_IsReported_UnderBreakpointKey()
        {
            // overrides is a dict keyed by breakpoint name → DiffDict addresses it directly
            UISpec candidate = Parse(BreakpointJson.Replace(@"""h"": ""center""", @"""h"": ""right"""));
            var changes = SpecDiff.Compare(Parse(BreakpointJson), candidate);

            SpecChange change = changes.SingleOrDefault(c => c.path.Contains("overrides/wide") && c.path.EndsWith("/h"));
            Assert.IsNotNull(change, "the override delta change must be addressed under overrides/<name>");
            Assert.AreEqual("center", change.before);
            Assert.AreEqual("right", change.after);
        }

        private static ElementSpec ElementOf(string json) =>
            ElementSpec.Parse(JsonReader.AsObject(MiniJson.Parse(json), "element"));
    }
}
