using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar B runtime driver (the behavior proof): <see cref="UIResponsiveRoot"/> selects the FIRST
    /// matching breakpoint on a simulated resize/orientation change and applies that breakpoint's
    /// pre-resolved vectors to its targets — ONLY on change, and reverting to the baked base when no
    /// breakpoint matches. WYSIWYG: the baked base is what shows when nothing matches.
    /// </summary>
    public class ResponsiveDriverTests
    {
        private readonly System.Collections.Generic.List<Object> _cleanup =
            new System.Collections.Generic.List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object obj in _cleanup)
                if (obj != null) Object.Destroy(obj);
            _cleanup.Clear();
        }

        // Distinct vector marker per state so an assertion pins down exactly which set was applied.
        private static readonly Vector2 BaseSize = new Vector2(100f, 100f);
        private static readonly Vector2 LandscapeSize = new Vector2(900f, 100f);
        private static readonly Vector2 PortraitSize = new Vector2(50f, 100f);

        /// <summary> Builds a root (fixed-size so rect.width/height are deterministic) with one child
        /// target, a landscape + portrait breakpoint table and pre-resolved sizeDelta entries. </summary>
        private (UIResponsiveRoot root, RectTransform target, RectTransform rootRect) BuildDriver()
        {
            var rootGo = new GameObject("ResponsiveRoot", typeof(RectTransform));
            _cleanup.Add(rootGo);
            var rootRect = (RectTransform)rootGo.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.zero; // fixed: rect size == sizeDelta
            rootRect.pivot = new Vector2(0.5f, 0.5f);

            var childGo = new GameObject("Card", typeof(RectTransform));
            childGo.transform.SetParent(rootGo.transform, false);
            var target = (RectTransform)childGo.transform;
            target.sizeDelta = BaseSize;

            var root = rootGo.AddComponent<UIResponsiveRoot>();
            root.conditions.Add(new UIResponsiveRoot.ResponsiveCondition
            {
                name = "landscape", orientation = "landscape", minAspect = 1.6f
            });
            root.conditions.Add(new UIResponsiveRoot.ResponsiveCondition
            {
                name = "portrait", orientation = "portrait"
            });

            root.bases.Add(Base(target, BaseSize));
            root.entries.Add(Entry("landscape", target, LandscapeSize));
            root.entries.Add(Entry("portrait", target, PortraitSize));

            return (root, target, rootRect);
        }

        private static UIResponsiveRoot.ResponsiveBase Base(RectTransform t, Vector2 size) =>
            new UIResponsiveRoot.ResponsiveBase
            {
                target = t, anchorMin = t.anchorMin, anchorMax = t.anchorMax,
                offsetMin = t.offsetMin, offsetMax = t.offsetMax, sizeDelta = size, pivot = t.pivot
            };

        private static UIResponsiveRoot.ResponsiveEntry Entry(string bp, RectTransform t, Vector2 size) =>
            new UIResponsiveRoot.ResponsiveEntry
            {
                breakpoint = bp, target = t, anchorMin = t.anchorMin, anchorMax = t.anchorMax,
                offsetMin = t.offsetMin, offsetMax = t.offsetMax, sizeDelta = size, pivot = t.pivot
            };

        [UnityTest]
        public IEnumerator Resize_ToLandscape_AppliesLandscapeOverride()
        {
            (UIResponsiveRoot root, RectTransform target, RectTransform rootRect) = BuildDriver();

            // start portrait-ish so Start() doesn't pre-pick landscape
            rootRect.sizeDelta = new Vector2(400f, 800f);
            yield return null; // let Start run

            Assert.AreEqual("portrait", root.ActiveBreakpoint, "tall start selects portrait");
            Assert.AreEqual(PortraitSize, target.sizeDelta);

            // resize to a wide landscape viewport
            rootRect.sizeDelta = new Vector2(1920f, 1080f);
            yield return null;

            Assert.AreEqual("landscape", root.ActiveBreakpoint, "wide viewport selects landscape (first match)");
            Assert.AreEqual(LandscapeSize, target.sizeDelta, "landscape override applied");
        }

        [UnityTest]
        public IEnumerator NoMatch_RevertsToBakedBase()
        {
            // a table that only matches very wide; a square viewport matches nothing → base
            var rootGo = new GameObject("ResponsiveRoot", typeof(RectTransform));
            _cleanup.Add(rootGo);
            var rootRect = (RectTransform)rootGo.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.zero;

            var childGo = new GameObject("Card", typeof(RectTransform));
            childGo.transform.SetParent(rootGo.transform, false);
            var target = (RectTransform)childGo.transform;
            target.sizeDelta = BaseSize;

            var root = rootGo.AddComponent<UIResponsiveRoot>();
            root.conditions.Add(new UIResponsiveRoot.ResponsiveCondition { name = "ultrawide", minAspect = 3f });
            root.bases.Add(Base(target, BaseSize));
            root.entries.Add(Entry("ultrawide", target, LandscapeSize));

            rootRect.sizeDelta = new Vector2(3200f, 800f); // aspect 4 → ultrawide
            yield return null;
            Assert.AreEqual("ultrawide", root.ActiveBreakpoint);
            Assert.AreEqual(LandscapeSize, target.sizeDelta);

            rootRect.sizeDelta = new Vector2(800f, 800f); // aspect 1 → no match → base
            yield return null;
            Assert.AreEqual(UIResponsiveRoot.BaseBreakpoint, root.ActiveBreakpoint, "no match falls back to base");
            Assert.AreEqual(BaseSize, target.sizeDelta, "base vectors restored");
        }

        [UnityTest]
        public IEnumerator OnlyAppliesOnChange()
        {
            (UIResponsiveRoot root, RectTransform target, RectTransform rootRect) = BuildDriver();

            rootRect.sizeDelta = new Vector2(1920f, 1080f);
            yield return null;
            Assert.AreEqual("landscape", root.ActiveBreakpoint);
            Assert.AreEqual(LandscapeSize, target.sizeDelta);

            // a foreign system pokes the target while STILL in the landscape breakpoint
            target.sizeDelta = new Vector2(123f, 45f);

            // resize within landscape (aspect stays >= 1.6, still landscape) → breakpoint unchanged →
            // the driver must NOT re-apply, so our poke survives (proof it only acts on change)
            rootRect.sizeDelta = new Vector2(1600f, 900f);
            yield return null;
            Assert.AreEqual("landscape", root.ActiveBreakpoint, "still landscape");
            Assert.AreEqual(new Vector2(123f, 45f), target.sizeDelta,
                "no breakpoint change ⇒ no re-apply (only-on-change contract)");
        }

        [UnityTest]
        public IEnumerator EmptyCondition_NeverMatches_FallsBackToBase()
        {
            // An all-default (unset) condition must never match — otherwise it would win
            // unconditionally and shadow every later breakpoint (A7).
            var rootGo = new GameObject("ResponsiveRoot", typeof(RectTransform));
            _cleanup.Add(rootGo);
            var rootRect = (RectTransform)rootGo.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.zero;

            var childGo = new GameObject("Card", typeof(RectTransform));
            childGo.transform.SetParent(rootGo.transform, false);
            var target = (RectTransform)childGo.transform;
            target.sizeDelta = BaseSize;

            var root = rootGo.AddComponent<UIResponsiveRoot>();
            var empty = new UIResponsiveRoot.ResponsiveCondition { name = "empty" };
            Assert.IsTrue(empty.IsEmpty);
            Assert.IsFalse(empty.Matches(1f, 1f), "an all-unset condition matches nothing");
            Assert.IsFalse(empty.Matches(3200f, 100f), "not even an extreme viewport");
            root.conditions.Add(empty);
            root.bases.Add(Base(target, BaseSize));
            root.entries.Add(Entry("empty", target, LandscapeSize));

            rootRect.sizeDelta = new Vector2(3200f, 100f); // extreme viewport that would match "anything"
            yield return null;

            Assert.AreEqual(UIResponsiveRoot.BaseBreakpoint, root.ActiveBreakpoint,
                "an empty condition must never be selected");
            Assert.AreEqual(BaseSize, target.sizeDelta, "base vectors stand, not the empty condition's entry");
        }

        [UnityTest]
        public IEnumerator ForceActiveBreakpoint_PreviewHook_OverridesViewport()
        {
            (UIResponsiveRoot root, RectTransform target, RectTransform rootRect) = BuildDriver();

            rootRect.sizeDelta = new Vector2(400f, 800f); // portrait viewport
            yield return null;
            Assert.AreEqual("portrait", root.ActiveBreakpoint);

            // Pillar C preview forces landscape regardless of the live viewport
            ((IActiveBreakpoint)root).SetActiveBreakpoint("landscape");
            Assert.AreEqual("landscape", root.ActiveBreakpoint);
            Assert.AreEqual(LandscapeSize, target.sizeDelta, "forced breakpoint applied immediately");

            // releasing the force re-follows the (portrait) viewport
            root.SetActiveBreakpoint(null);
            Assert.AreEqual("portrait", root.ActiveBreakpoint);
            Assert.AreEqual(PortraitSize, target.sizeDelta);
        }
    }
}
