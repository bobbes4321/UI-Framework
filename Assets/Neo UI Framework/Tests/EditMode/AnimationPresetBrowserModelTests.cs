using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The grouping/sort/expand/search machinery extracted from <c>AnimationPresetBrowserPopup</c> into
    /// the shared <see cref="AnimationPresetBrowserModel"/> (Phase 2.4) — so the popup and the Design
    /// System Motion tab browser share one implementation. Pure data, so it's directly unit-testable
    /// (the IMGUI hosts are not).
    /// </summary>
    public class AnimationPresetBrowserModelTests
    {
        private readonly List<UIAnimationPreset> _presets = new List<UIAnimationPreset>();

        private UIAnimationPreset Make(string category, string name)
        {
            var p = ScriptableObject.CreateInstance<UIAnimationPreset>();
            p.category = category;
            p.presetName = name;
            _presets.Add(p);
            return p;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UIAnimationPreset p in _presets) Object.DestroyImmediate(p);
            _presets.Clear();
        }

        [Test]
        public void Groups_ComeFromAssetCategories_NotHardcoded()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            Make("Wacky", "Custom1"); // a project-declared category the package never lists

            var model = new AnimationPresetBrowserModel(_presets, null, null);

            CollectionAssert.AreEquivalent(
                new[] { "Show", "Loop", "Wacky" },
                model.Groups.Select(g => g.category).ToArray(),
                "categories are read from whatever the assets declare");
        }

        [Test]
        public void BlankCategory_FallsBackToCustom()
        {
            Make("", "Nameless");
            var model = new AnimationPresetBrowserModel(_presets, null, null);
            Assert.AreEqual("Custom", model.Groups.Single().category);
        }

        [Test]
        public void SuggestedCategories_SortFirst_AndStartExpanded()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            Make("Hover", "ScaleUp");

            var model = new AnimationPresetBrowserModel(_presets, new[] { "Hover" }, null);

            Assert.AreEqual("Hover", model.Groups[0].category, "suggested category sorts to the top");
            Assert.IsTrue(model.Groups[0].suggested);
            Assert.IsTrue(model.IsExpanded("Hover"), "a suggested category starts expanded");
        }

        [Test]
        public void NoRole_ExpandsEverything()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            var model = new AnimationPresetBrowserModel(_presets, null, null);
            Assert.IsTrue(model.IsExpanded("Show"));
            Assert.IsTrue(model.IsExpanded("Loop"));
        }

        [Test]
        public void CurrentPreset_Category_IsForcedOpen()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            // Only "Show" is suggested → "Loop" would collapse, but current lives in Loop, so it opens.
            var model = new AnimationPresetBrowserModel(_presets, new[] { "Show" }, "Loop/Pulse");
            Assert.IsTrue(model.IsExpanded("Loop"), "the applied preset's category is force-expanded");
        }

        [Test]
        public void BuildRows_Collapsed_HidesPresets_ExpandedShowsThem()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            var model = new AnimationPresetBrowserModel(_presets, new[] { "Show" }, null);

            // Loop is not suggested → collapsed by default: header only, no preset row.
            var collapsed = model.BuildRows(null);
            Assert.IsFalse(collapsed.Any(r => r.preset != null && r.preset.category == "Loop"),
                "a collapsed group shows no preset rows");

            model.ToggleExpanded("Loop");
            var expanded = model.BuildRows(null);
            Assert.IsTrue(expanded.Any(r => r.preset != null && r.preset.presetName == "Pulse"),
                "expanding the group reveals its preset rows");
        }

        [Test]
        public void BuildRows_Search_MatchesNameAcrossCollapsedGroups_IgnoringExpansion()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            Make("Loop", "Breathe");

            var model = new AnimationPresetBrowserModel(_presets, null, null);
            var rows = model.BuildRows("puls");

            var matched = rows.Where(r => r.preset != null).Select(r => r.preset.presetName).ToArray();
            CollectionAssert.AreEqual(new[] { "Pulse" }, matched, "search filters to name matches");
            Assert.IsTrue(rows.Any(r => r.header != null && r.header.category == "Loop"),
                "a matched preset still shows under its header");
        }

        [Test]
        public void BuildRows_Search_CategoryHit_ShowsAllOfThatCategory()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            Make("Loop", "Breathe");

            var model = new AnimationPresetBrowserModel(_presets, null, null);
            var rows = model.BuildRows("loop");

            var matched = rows.Where(r => r.preset != null).Select(r => r.preset.presetName).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Pulse", "Breathe" }, matched,
                "a category-name hit surfaces every preset in that category");
        }

        [Test]
        public void CopyExpansionFrom_PreservesFolds_AcrossRebuild()
        {
            Make("Show", "FadeIn");
            Make("Loop", "Pulse");
            var first = new AnimationPresetBrowserModel(_presets, new[] { "Show" }, null);
            first.ToggleExpanded("Loop"); // user opens Loop

            // Simulate a rebuild after create/delete.
            var rebuilt = new AnimationPresetBrowserModel(_presets, new[] { "Show" }, null);
            rebuilt.CopyExpansionFrom(first);
            Assert.IsTrue(rebuilt.IsExpanded("Loop"), "user fold state carries across a rebuild");
        }
    }
}
