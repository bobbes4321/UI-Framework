using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Off-spec lint (Plan 1, C): editor edits below a composite widget root that the exporter can't
    /// see, so a regenerate would silently lose them. Uses controlled in-memory trees (so the result
    /// doesn't depend on the exact factory internals) plus a real-widget false-positive guard.
    /// </summary>
    public class OffSpecLintTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        /// <summary> View → composite widget ("Btn", a UIButton) → factory-internal child ("Deco"). </summary>
        private GameObject BuildTree(Color decoColor, bool tokenBound)
        {
            var root = new GameObject("View", typeof(RectTransform));
            _spawned.Add(root);
            var widget = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(UIButton));
            widget.transform.SetParent(root.transform, false);
            var deco = new GameObject("Deco", typeof(RectTransform), typeof(Image));
            deco.transform.SetParent(widget.transform, false);
            deco.GetComponent<Image>().color = decoColor;
            if (tokenBound) deco.AddComponent<ThemeColorTarget>();
            return root;
        }

        [Test]
        public void NoDrift_ProducesNoFindings()
        {
            var findings = new List<OffSpecFinding>();
            OffSpecLint.CompareTree(BuildTree(Color.white, false), BuildTree(Color.white, false),
                "views/Test/V", findings);
            Assert.IsEmpty(findings, "identical trees must report no off-spec edits");
        }

        [Test]
        public void RawColorOnInternalChild_IsFlaggedWithFix()
        {
            var findings = new List<OffSpecFinding>();
            OffSpecLint.CompareTree(BuildTree(Color.red, false), BuildTree(Color.white, false),
                "views/Test/V", findings);

            OffSpecFinding finding = findings.SingleOrDefault(f => f.message.Contains("Color"));
            Assert.IsNotNull(finding, "a raw color on a factory-internal child must be flagged");
            StringAssert.Contains("Deco", finding.path);
            Assert.IsFalse(string.IsNullOrWhiteSpace(finding.fix), "the finding must carry an actionable fix");
        }

        [Test]
        public void TokenBoundColor_ProducesNoFinding()
        {
            var findings = new List<OffSpecFinding>();
            // colour differs, but a ThemeColorTarget drives it — that round-trips, so no finding
            OffSpecLint.CompareTree(BuildTree(Color.red, true), BuildTree(Color.white, true),
                "views/Test/V", findings);
            Assert.IsEmpty(findings, "a token-bound color is exportable and must not be flagged");
        }

        [Test]
        public void AddedInternalChild_IsFlagged()
        {
            GameObject live = BuildTree(Color.white, false);
            GameObject reference = BuildTree(Color.white, false);
            var extra = new GameObject("Extra", typeof(RectTransform));
            extra.transform.SetParent(live.transform.Find("Btn"), false);

            var findings = new List<OffSpecFinding>();
            OffSpecLint.CompareTree(live, reference, "views/Test/V", findings);

            Assert.IsTrue(findings.Any(f => f.message.Contains("added") && f.path.Contains("Extra")),
                "a child added inside a widget's internal subtree must be flagged");
        }

        [Test]
        public void RealGeneratedView_AgainstItself_HasNoFalsePositives()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Lint/V"", ""elements"": [
              { ""button"": { ""id"": ""Lint/Go"", ""label"": ""Go"", ""background"": ""Primary"" } },
              { ""switch"": { ""id"": ""Lint/Mute"" } },
              { ""slider"": { ""id"": ""Lint/Vol"", ""min"": 0, ""max"": 1, ""value"": 0.5 } }
            ] } ] }";
            UISpec spec = UISpec.FromJson(json);

            List<GameObject> referenceRoots = UISpecPreview.BuildViews(spec);
            List<GameObject> liveRoots = UISpecPreview.BuildViews(spec);
            _spawned.AddRange(referenceRoots);
            _spawned.AddRange(liveRoots);
            Assert.IsNotEmpty(liveRoots, "preview must build the view in memory");

            var findings = new List<OffSpecFinding>();
            OffSpecLint.CompareTree(liveRoots[0], referenceRoots[0], "views/Lint/V", findings);
            Assert.IsEmpty(findings,
                "a freshly-built view must not lint against an identical build: " +
                string.Join("; ", findings.Select(f => f.message)));
        }
    }
}
