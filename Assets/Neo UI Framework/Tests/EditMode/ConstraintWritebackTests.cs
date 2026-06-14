using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pure (no Unity scene) coverage for <see cref="ConstraintWriteback"/> — the device-rect ⇄ layout
    /// offset math that makes a dragged element stay glued to its constraint across a viewport aspect
    /// change. For every constraint × axis it asserts: (1) Write produces the offsets the §A.2.3 table
    /// expects, and (2) Write→Resolve round-trips back to the same device rect; and that an edge-glued
    /// element re-resolves against a RESIZED parent to the geometry the constraint promises.
    /// </summary>
    public class ConstraintWritebackTests
    {
        // a 1080×1920 device parent (y up, bottom-origin) and a child rect inside it
        private static readonly Rect Parent = new Rect(0f, 0f, 1080f, 1920f);
        private const float Eps = 0.01f;

        private static ElementSpec WithConstraint(string h, string v) =>
            new ElementSpec { kind = "button", layout = new LayoutSpec { h = h, v = v } };

        private static void AssertRectApprox(Rect expected, Rect actual, string msg)
        {
            Assert.AreEqual(expected.xMin, actual.xMin, Eps, msg + " xMin");
            Assert.AreEqual(expected.yMin, actual.yMin, Eps, msg + " yMin");
            Assert.AreEqual(expected.width, actual.width, Eps, msg + " width");
            Assert.AreEqual(expected.height, actual.height, Eps, msg + " height");
        }

        // ------------------------------------------------------------------ per-constraint round-trips

        [Test]
        public void Left_StoresLeftInset_AndRoundTrips()
        {
            var el = WithConstraint(LayoutConstraints.Left, LayoutConstraints.Bottom);
            var rect = new Rect(40f, 100f, 200f, 80f);
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(40f, el.layout.offset.GetOr("left", -1f), Eps);
            Assert.AreEqual(100f, el.layout.offset.GetOr("bottom", -1f), Eps);
            Assert.AreEqual(200f, el.layout.size.w);
            Assert.AreEqual(80f, el.layout.size.h);

            Rect back = ConstraintWriteback.Resolve(el.layout, Parent, new Vector2(200f, 80f));
            AssertRectApprox(rect, back, "left/bottom");
        }

        [Test]
        public void Right_StoresRightInset_AndStaysGluedWhenParentWidens()
        {
            var el = WithConstraint(LayoutConstraints.Right, LayoutConstraints.Top);
            var rect = new Rect(840f, 1740f, 200f, 100f); // right inset 1080-1040=40, top inset 1920-1840=80
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(40f, el.layout.offset.GetOr("right", -1f), Eps);
            Assert.AreEqual(80f, el.layout.offset.GetOr("top", -1f), Eps);

            // round-trip in the same parent
            Rect back = ConstraintWriteback.Resolve(el.layout, Parent, new Vector2(200f, 100f));
            AssertRectApprox(rect, back, "right/top same parent");

            // the glue test: widen the parent to 1280 — the right edge must keep its 40px inset
            var wider = new Rect(0f, 0f, 1280f, 1920f);
            Rect resized = ConstraintWriteback.Resolve(el.layout, wider, new Vector2(200f, 100f));
            Assert.AreEqual(1280f - 40f, resized.xMax, Eps, "right edge stays 40px from the new right");
            Assert.AreEqual(200f, resized.width, Eps, "width preserved");
        }

        [Test]
        public void Center_StoresSignedDelta_AndRoundTrips()
        {
            var el = WithConstraint(LayoutConstraints.Center, LayoutConstraints.Center);
            // element center at (640, 1060); parent center (540, 960) → delta (+100, +100)
            var rect = new Rect(540f, 1010f, 200f, 100f);
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(100f, el.layout.offset.GetOr("h", -999f), Eps);
            Assert.AreEqual(100f, el.layout.offset.GetOr("v", -999f), Eps);

            Rect back = ConstraintWriteback.Resolve(el.layout, Parent, new Vector2(200f, 100f));
            AssertRectApprox(rect, back, "center");
        }

        [Test]
        public void LeftRight_StoresBothInsets_AndStretchesWithParent()
        {
            var el = WithConstraint(LayoutConstraints.LeftRight, LayoutConstraints.TopBottom);
            var rect = new Rect(24f, 48f, 1080f - 48f, 1920f - 96f); // left 24 / right 24, bottom 48 / top 48
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(24f, el.layout.offset.GetOr("left", -1f), Eps);
            Assert.AreEqual(24f, el.layout.offset.GetOr("right", -1f), Eps);
            Assert.AreEqual(48f, el.layout.offset.GetOr("top", -1f), Eps);
            Assert.AreEqual(48f, el.layout.offset.GetOr("bottom", -1f), Eps);

            Rect back = ConstraintWriteback.Resolve(el.layout, Parent, rect.size);
            AssertRectApprox(rect, back, "leftRight/topBottom");

            // widen the parent: the element stretches, insets preserved
            var wider = new Rect(0f, 0f, 1280f, 1920f);
            Rect resized = ConstraintWriteback.Resolve(el.layout, wider, rect.size);
            Assert.AreEqual(24f, resized.xMin, Eps);
            Assert.AreEqual(1280f - 24f, resized.xMax, Eps);
        }

        [Test]
        public void Scale_StoresFractions_AndScalesWithParent()
        {
            var el = WithConstraint(LayoutConstraints.Scale, LayoutConstraints.Scale);
            // element occupies x 0.25..0.75, y 0.10..0.40 of the parent
            var rect = new Rect(0.25f * 1080f, 0.10f * 1920f, 0.5f * 1080f, 0.3f * 1920f);
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(0.25f, el.layout.offset.GetOr("left", -1f), 0.001f);
            Assert.AreEqual(0.75f, el.layout.offset.GetOr("right", -1f), 0.001f);
            Assert.AreEqual(0.10f, el.layout.offset.GetOr("bottom", -1f), 0.001f);
            Assert.AreEqual(0.40f, el.layout.offset.GetOr("top", -1f), 0.001f);

            Rect back = ConstraintWriteback.Resolve(el.layout, Parent, rect.size);
            AssertRectApprox(rect, back, "scale same parent");

            // scale with a doubled-width parent: the element occupies the same fractions
            var wider = new Rect(0f, 0f, 2160f, 1920f);
            Rect resized = ConstraintWriteback.Resolve(el.layout, wider, rect.size);
            Assert.AreEqual(0.25f * 2160f, resized.xMin, Eps);
            Assert.AreEqual(0.5f * 2160f, resized.width, Eps);
        }

        [Test]
        public void Top_StoresTopInset_AndRoundTrips()
        {
            var el = WithConstraint(LayoutConstraints.Center, LayoutConstraints.Top);
            var rect = new Rect(440f, 1740f, 200f, 100f); // top inset = 1920-1840 = 80
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(80f, el.layout.offset.GetOr("top", -1f), Eps);
            Rect back = ConstraintWriteback.Resolve(el.layout, Parent, new Vector2(200f, 100f));
            AssertRectApprox(rect, back, "top");
        }

        [Test]
        public void Bottom_StoresBottomInset_AndRoundTrips()
        {
            var el = WithConstraint(LayoutConstraints.Center, LayoutConstraints.Bottom);
            var rect = new Rect(440f, 60f, 200f, 100f); // bottom inset = 60
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(60f, el.layout.offset.GetOr("bottom", -1f), Eps);
            Rect back = ConstraintWriteback.Resolve(el.layout, Parent, new Vector2(200f, 100f));
            AssertRectApprox(rect, back, "bottom");
        }

        [Test]
        public void DefaultConstraint_WhenLayoutEmpty_IsLeftTop()
        {
            var el = new ElementSpec { kind = "text", layout = new LayoutSpec() };
            var rect = new Rect(10f, 1800f, 100f, 40f); // top inset 1920-1840=80, left 10
            ConstraintWriteback.Write(el, rect, Parent);

            Assert.AreEqual(LayoutConstraints.Left, el.layout.h);
            Assert.AreEqual(LayoutConstraints.Top, el.layout.v);
            Assert.AreEqual(10f, el.layout.offset.GetOr("left", -1f), Eps);
            Assert.AreEqual(80f, el.layout.offset.GetOr("top", -1f), Eps);
        }

        [Test]
        public void Write_NullElement_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ConstraintWriteback.Write(null, Parent, Parent));
        }
    }
}
