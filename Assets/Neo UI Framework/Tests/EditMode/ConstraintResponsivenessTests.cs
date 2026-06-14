using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The "moved it in portrait, it disappears in landscape" proof. Builds a constrained element
    /// under a parent RectTransform, then resizes the parent across aspect ratios and asserts the
    /// element tracks its declared constraint (glued to the right edge, stretched, scaled, …) instead
    /// of staying at a fixed pixel position that walks off-screen. EditMode + ForceRebuildLayout, no
    /// play loop needed.
    /// </summary>
    public class ConstraintResponsivenessTests
    {
        private GameObject _root;
        private RectTransform _parent;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Canvas", typeof(Canvas), typeof(RectTransform));
            _parent = (RectTransform)_root.transform;
            SizeParent(1080f, 1920f); // portrait
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private void SizeParent(float w, float h)
        {
            _parent.anchorMin = Vector2.zero;
            _parent.anchorMax = Vector2.zero;
            _parent.pivot = new Vector2(0.5f, 0.5f);
            _parent.sizeDelta = new Vector2(w, h);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_parent);
        }

        private RectTransform MakeChild(LayoutSpec layout)
        {
            var go = new GameObject("Child", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_parent, worldPositionStays: false);
            rect.sizeDelta = new Vector2(200f, 80f);
            ConstraintLayout.Apply(rect, layout, parentLayout: null);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_parent);
            return rect;
        }

        [Test]
        public void RightConstraint_StaysGluedToRightEdge_WhenParentWidens()
        {
            var layout = new LayoutSpec { h = "right", v = "top", offset = new LayoutOffset(), size = new LayoutSize { w = 200f, h = 80f } };
            layout.offset.Set("right", 20f);
            layout.offset.Set("top", 30f);
            RectTransform child = MakeChild(layout);

            // child's right edge sits at parent.width + offsetMax.x (anchors at 1); the gap to the
            // right edge is -offsetMax.x — it must stay 20px at any parent width.
            SizeParent(1080f, 1920f);
            Assert.AreEqual(-20f, child.offsetMax.x, 1e-3f, "portrait: 20px from right edge");

            SizeParent(1920f, 1080f); // landscape
            Assert.AreEqual(-20f, child.offsetMax.x, 1e-3f, "landscape: STILL 20px from right edge (did not walk away)");
            Assert.AreEqual(200f, child.rect.width, 1e-2f, "width preserved across resize");
        }

        [Test]
        public void StretchConstraint_GrowsWithParent_KeepingInsets()
        {
            var layout = new LayoutSpec { h = "leftRight", v = "top", offset = new LayoutOffset(), size = new LayoutSize { h = 60f } };
            layout.offset.Set("left", 40f);
            layout.offset.Set("right", 40f);
            RectTransform child = MakeChild(layout);

            SizeParent(1080f, 1920f);
            float narrow = child.rect.width;
            Assert.AreEqual(1080f - 80f, narrow, 1e-2f, "stretched width = parent - insets (portrait)");

            SizeParent(1920f, 1080f);
            float wide = child.rect.width;
            Assert.AreEqual(1920f - 80f, wide, 1e-2f, "stretched width grows with the parent (landscape)");
            Assert.Greater(wide, narrow, "the element genuinely tracks the wider parent");
        }

        [Test]
        public void ScaleConstraint_OccupiesProportion_AtAnyParentSize()
        {
            var layout = new LayoutSpec { h = "scale", v = "scale", offset = new LayoutOffset() };
            layout.offset.Set("left", 0.25f);   // start fraction
            layout.offset.Set("right", 0.75f);  // end fraction → occupies the middle 50%
            layout.offset.Set("bottom", 0.0f);
            layout.offset.Set("top", 1.0f);
            RectTransform child = MakeChild(layout);

            SizeParent(1000f, 2000f);
            Assert.AreEqual(500f, child.rect.width, 1e-2f, "50% of 1000");

            SizeParent(2000f, 1000f);
            Assert.AreEqual(1000f, child.rect.width, 1e-2f, "50% of 2000 — scales with the parent");
        }

        [Test]
        public void CenterConstraint_StaysCentered_PlusSignedOffset()
        {
            var layout = new LayoutSpec { h = "center", v = "center", offset = new LayoutOffset(), size = new LayoutSize { w = 200f, h = 80f } };
            layout.offset.Set("h", 0f);
            layout.offset.Set("v", 0f);
            RectTransform child = MakeChild(layout);

            SizeParent(1080f, 1920f);
            Assert.AreEqual(0f, child.anchoredPosition.x, 1e-3f, "centered horizontally");
            SizeParent(1920f, 1080f);
            Assert.AreEqual(0f, child.anchoredPosition.x, 1e-3f, "still centered after resize");
            Assert.AreEqual(200f, child.rect.width, 1e-2f);
        }
    }
}
