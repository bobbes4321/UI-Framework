using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar B cascade semantics (pure model — no Unity assets): the effective layout for a breakpoint
    /// is the base <see cref="LayoutSpec"/> merged field-by-field with the override delta. Verifies
    /// per-key offset merge, per-axis size/sizing merge, scalar h/v replace, and that an empty delta is
    /// a no-op. Also exercises <see cref="BreakpointConditions"/> first-match selection.
    /// </summary>
    public class BreakpointCascadeTests
    {
        private static LayoutSpec Base() => new LayoutSpec
        {
            h = "leftRight",
            v = "topBottom",
            offset = Offset(("left", 24f), ("right", 24f), ("top", 48f), ("bottom", 48f)),
            size = new LayoutSize { w = 600f, h = 400f },
            sizing = new LayoutSizing { w = "fill", h = "fixed" }
        };

        private static LayoutOffset Offset(params (string key, float value)[] entries)
        {
            var o = new LayoutOffset();
            foreach ((string key, float value) in entries) o.Set(key, value);
            return o;
        }

        [Test]
        public void EmptyDelta_IsNoOp_ButDistinctInstance()
        {
            LayoutSpec b = Base();
            LayoutSpec merged = b.MergedWith(new LayoutSpec());
            Assert.AreEqual("leftRight", merged.h);
            Assert.AreEqual("topBottom", merged.v);
            Assert.AreEqual(600f, merged.size.w);
            Assert.AreEqual(400f, merged.size.h);
            Assert.AreEqual(24f, merged.offset.GetOr("left", -1f));
            Assert.AreNotSame(b.offset, merged.offset, "merge must not alias the base's sub-objects");
        }

        [Test]
        public void ScalarConstraint_Replaces_WhenSet()
        {
            LayoutSpec merged = Base().MergedWith(new LayoutSpec { h = "center" });
            Assert.AreEqual("center", merged.h, "set delta.h replaces base.h");
            Assert.AreEqual("topBottom", merged.v, "unset delta.v inherits base.v");
        }

        [Test]
        public void Offset_MergesPerKey()
        {
            // delta overrides only left/right; top/bottom inherit from base
            LayoutSpec merged = Base().MergedWith(new LayoutSpec { offset = Offset(("left", 8f), ("right", 8f)) });
            Assert.AreEqual(8f, merged.offset.GetOr("left", -1f), "left overridden");
            Assert.AreEqual(8f, merged.offset.GetOr("right", -1f), "right overridden");
            Assert.AreEqual(48f, merged.offset.GetOr("top", -1f), "top inherited");
            Assert.AreEqual(48f, merged.offset.GetOr("bottom", -1f), "bottom inherited");
        }

        [Test]
        public void Size_MergesPerAxis()
        {
            // delta overrides only w; h inherits
            LayoutSpec merged = Base().MergedWith(new LayoutSpec { size = new LayoutSize { w = 900f } });
            Assert.AreEqual(900f, merged.size.w, "w overridden");
            Assert.AreEqual(400f, merged.size.h, "h inherited from base");
        }

        [Test]
        public void Sizing_MergesPerAxis()
        {
            LayoutSpec merged = Base().MergedWith(new LayoutSpec { sizing = new LayoutSizing { h = "hug" } });
            Assert.AreEqual("fill", merged.sizing.w, "w sizing inherited");
            Assert.AreEqual("hug", merged.sizing.h, "h sizing overridden");
        }

        [Test]
        public void Merge_OverNullBase_TreatsDeltaAsWhole()
        {
            var delta = new LayoutSpec { h = "center", size = new LayoutSize { w = 320f } };
            LayoutSpec merged = new LayoutSpec().MergedWith(delta);
            Assert.AreEqual("center", merged.h);
            Assert.AreEqual(320f, merged.size.w);
            Assert.IsNull(merged.v, "nothing to inherit on the empty base");
        }

        [Test]
        public void Conditions_FirstMatchWins_BuiltIns()
        {
            // landscape-wide first, then a broad portrait; a 1920x1080 env is landscape & wide
            var wide = new BreakpointCondition { orientation = "landscape", minAspect = 1.6f };
            var portrait = new BreakpointCondition { orientation = "portrait" };

            BreakpointEnv land = BreakpointEnv.FromSize(1920f, 1080f);
            Assert.IsTrue(BreakpointConditions.Evaluate(wide, land), "1920x1080 is landscape & aspect 1.78 >= 1.6");
            Assert.IsFalse(BreakpointConditions.Evaluate(portrait, land), "1920x1080 is not portrait");

            BreakpointEnv tall = BreakpointEnv.FromSize(1080f, 1920f);
            Assert.IsFalse(BreakpointConditions.Evaluate(wide, tall), "tall is not landscape");
            Assert.IsTrue(BreakpointConditions.Evaluate(portrait, tall), "tall is portrait");
        }

        [Test]
        public void WidthConditions_AreBounds()
        {
            var narrow = new BreakpointCondition { maxWidth = 600f };
            Assert.IsTrue(BreakpointConditions.Evaluate(narrow, BreakpointEnv.FromSize(480f, 800f)));
            Assert.IsFalse(BreakpointConditions.Evaluate(narrow, BreakpointEnv.FromSize(900f, 800f)));
        }

        [Test]
        public void EmptyCondition_MatchesNothing()
        {
            Assert.IsFalse(BreakpointConditions.Evaluate(new BreakpointCondition(),
                BreakpointEnv.FromSize(800f, 600f)),
                "a condition with no active kind must not win unconditionally and shadow later breakpoints");
        }
    }
}
