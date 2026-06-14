using System.Collections.Generic;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pure coverage for the multi-select align/distribute toolbar. Operates on device-space rects (the
    /// space the canvas snapshots), asserts the repositioned rects, and — for the end-to-end story —
    /// runs the result through <see cref="ConstraintWriteback"/> and asserts the resulting
    /// <see cref="LayoutSpec"/> offsets (what the spec actually stores, a single undoable edit).
    /// </summary>
    public class AlignDistributeTests
    {
        private static readonly Rect Parent = new Rect(0f, 0f, 1000f, 1000f);
        private const float Eps = 0.01f;

        // three boxes at varying x/y (device space, y up)
        private static Dictionary<string, Rect> ThreeBoxes() => new Dictionary<string, Rect>
        {
            ["a"] = new Rect(10f, 800f, 100f, 50f),
            ["b"] = new Rect(200f, 400f, 120f, 60f),
            ["c"] = new Rect(500f, 100f, 80f, 40f),
        };

        // ------------------------------------------------------------------ align

        [Test]
        public void AlignLeft_SharesMinX()
        {
            var result = AlignDistribute.Apply(AlignDistribute.Op.Left, ThreeBoxes());
            Assert.AreEqual(10f, result["a"].xMin, Eps);
            Assert.AreEqual(10f, result["b"].xMin, Eps);
            Assert.AreEqual(10f, result["c"].xMin, Eps);
            // widths preserved; y untouched
            Assert.AreEqual(120f, result["b"].width, Eps);
            Assert.AreEqual(400f, result["b"].yMin, Eps);
        }

        [Test]
        public void AlignRight_SharesMaxX()
        {
            var result = AlignDistribute.Apply(AlignDistribute.Op.Right, ThreeBoxes());
            float maxX = 580f; // c: 500+80
            Assert.AreEqual(maxX, result["a"].xMax, Eps);
            Assert.AreEqual(maxX, result["b"].xMax, Eps);
            Assert.AreEqual(maxX, result["c"].xMax, Eps);
        }

        [Test]
        public void AlignCenterX_SharesCenter()
        {
            var result = AlignDistribute.Apply(AlignDistribute.Op.CenterX, ThreeBoxes());
            float min = 10f, max = 580f, center = (min + max) * 0.5f;
            Assert.AreEqual(center, result["a"].center.x, Eps);
            Assert.AreEqual(center, result["b"].center.x, Eps);
            Assert.AreEqual(center, result["c"].center.x, Eps);
        }

        [Test]
        public void AlignTop_SharesMaxY()
        {
            var result = AlignDistribute.Apply(AlignDistribute.Op.Top, ThreeBoxes());
            float maxY = 850f; // a: 800+50
            Assert.AreEqual(maxY, result["a"].yMax, Eps);
            Assert.AreEqual(maxY, result["b"].yMax, Eps);
            Assert.AreEqual(maxY, result["c"].yMax, Eps);
        }

        [Test]
        public void AlignBottom_SharesMinY()
        {
            var result = AlignDistribute.Apply(AlignDistribute.Op.Bottom, ThreeBoxes());
            float minY = 100f; // c
            Assert.AreEqual(minY, result["a"].yMin, Eps);
            Assert.AreEqual(minY, result["b"].yMin, Eps);
            Assert.AreEqual(minY, result["c"].yMin, Eps);
        }

        [Test]
        public void AlignCenterY_SharesCenter()
        {
            var result = AlignDistribute.Apply(AlignDistribute.Op.CenterY, ThreeBoxes());
            float min = 100f, max = 850f, center = (min + max) * 0.5f;
            Assert.AreEqual(center, result["a"].center.y, Eps);
            Assert.AreEqual(center, result["b"].center.y, Eps);
            Assert.AreEqual(center, result["c"].center.y, Eps);
        }

        // ------------------------------------------------------------------ distribute

        [Test]
        public void DistributeHorizontal_EqualizesGaps()
        {
            // three side-by-side boxes (overlapping y), uneven gaps → equal gaps after
            var boxes = new Dictionary<string, Rect>
            {
                ["a"] = new Rect(0f, 0f, 100f, 50f),
                ["b"] = new Rect(150f, 0f, 100f, 50f),
                ["c"] = new Rect(700f, 0f, 100f, 50f),
            };
            var result = AlignDistribute.Apply(AlignDistribute.Op.DistributeH, boxes);

            // extremes stay put; span = 0..800, widths sum 300, two gaps of (800-300)/2 = 250
            Assert.AreEqual(0f, result["a"].xMin, Eps);
            Assert.AreEqual(700f, result["c"].xMin, Eps);
            float gapAB = result["b"].xMin - result["a"].xMax;
            float gapBC = result["c"].xMin - result["b"].xMax;
            Assert.AreEqual(gapAB, gapBC, Eps, "gaps must be equal");
            Assert.AreEqual(250f, gapAB, Eps);
        }

        [Test]
        public void DistributeVertical_EqualizesGaps()
        {
            var boxes = new Dictionary<string, Rect>
            {
                ["a"] = new Rect(0f, 0f, 50f, 100f),
                ["b"] = new Rect(0f, 150f, 50f, 100f),
                ["c"] = new Rect(0f, 700f, 50f, 100f),
            };
            var result = AlignDistribute.Apply(AlignDistribute.Op.DistributeV, boxes);
            Assert.AreEqual(0f, result["a"].yMin, Eps);
            Assert.AreEqual(700f, result["c"].yMin, Eps);
            float gapAB = result["b"].yMin - result["a"].yMax;
            float gapBC = result["c"].yMin - result["b"].yMax;
            Assert.AreEqual(gapAB, gapBC, Eps);
            Assert.AreEqual(250f, gapAB, Eps);
        }

        [Test]
        public void Distribute_WithTwoElements_IsNoOp()
        {
            var boxes = new Dictionary<string, Rect>
            {
                ["a"] = new Rect(0f, 0f, 100f, 50f),
                ["b"] = new Rect(400f, 0f, 100f, 50f),
            };
            var result = AlignDistribute.Apply(AlignDistribute.Op.DistributeH, boxes);
            Assert.AreEqual(boxes["a"], result["a"]);
            Assert.AreEqual(boxes["b"], result["b"]);
        }

        // ------------------------------------------------------------------ end-to-end: result → layout

        [Test]
        public void AlignLeft_WritesLeftOffsetIntoLayout()
        {
            // two left/top-constrained elements; aligning left should set both their layout.offset.left
            var a = new ElementSpec { kind = "button", layout = new LayoutSpec { h = LayoutConstraints.Left, v = LayoutConstraints.Top } };
            var b = new ElementSpec { kind = "button", layout = new LayoutSpec { h = LayoutConstraints.Left, v = LayoutConstraints.Top } };
            var rects = new Dictionary<ElementSpec, Rect>
            {
                [a] = new Rect(40f, 800f, 100f, 50f),
                [b] = new Rect(220f, 600f, 120f, 60f),
            };

            Dictionary<ElementSpec, Rect> result = AlignDistribute.Apply(AlignDistribute.Op.Left, rects);
            foreach (var kv in result)
                ConstraintWriteback.Write(kv.Key, kv.Value, Parent);

            // both share the smaller left inset (40)
            Assert.AreEqual(40f, a.layout.offset.GetOr("left", -1f), Eps);
            Assert.AreEqual(40f, b.layout.offset.GetOr("left", -1f), Eps);
            // their constraint ids are untouched
            Assert.AreEqual(LayoutConstraints.Left, a.layout.h);
            Assert.AreEqual(LayoutConstraints.Left, b.layout.h);
        }

        [Test]
        public void AlignRight_WritesRightOffset_ForRightConstrained()
        {
            var a = new ElementSpec { kind = "button", layout = new LayoutSpec { h = LayoutConstraints.Right, v = LayoutConstraints.Top } };
            var b = new ElementSpec { kind = "button", layout = new LayoutSpec { h = LayoutConstraints.Right, v = LayoutConstraints.Top } };
            var rects = new Dictionary<ElementSpec, Rect>
            {
                [a] = new Rect(700f, 800f, 100f, 50f), // xMax 800
                [b] = new Rect(850f, 600f, 120f, 60f), // xMax 970
            };

            Dictionary<ElementSpec, Rect> result = AlignDistribute.Apply(AlignDistribute.Op.Right, rects);
            foreach (var kv in result)
                ConstraintWriteback.Write(kv.Key, kv.Value, Parent);

            // both align to maxX = 970 → right inset = 1000-970 = 30
            Assert.AreEqual(30f, a.layout.offset.GetOr("right", -1f), Eps);
            Assert.AreEqual(30f, b.layout.offset.GetOr("right", -1f), Eps);
        }

        [Test]
        public void Apply_SingleElement_IsNoOp()
        {
            var boxes = new Dictionary<string, Rect> { ["a"] = new Rect(5f, 5f, 10f, 10f) };
            var result = AlignDistribute.Apply(AlignDistribute.Op.CenterX, boxes);
            Assert.AreEqual(boxes["a"], result["a"]);
        }
    }
}
