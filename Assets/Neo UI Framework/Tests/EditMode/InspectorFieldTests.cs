using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar F (Composer inspector overhaul) coverage: the catalog surfaces the new composite layout
    /// editors per kind; the constraint offset mapping round-trips through the inspector's read/write
    /// (the inverse of the generator's <c>ResolveOffset</c>); the per-child sizing modes come from the
    /// registry seam; and the document's active-edit-breakpoint scopes layout edits into
    /// <c>overrides[bp]</c> rather than base.
    /// </summary>
    public class InspectorFieldTests
    {
        // ---------------------------------------------------------------- catalog surfaces new fields

        [Test]
        public void Catalog_EveryKind_HasConstraintAndSizingFields()
        {
            foreach (string kind in ElementSpec.Kinds)
            {
                var keys = SpecFieldCatalog.For(kind).Select(f => f.key).ToList();
                CollectionAssert.Contains(keys, SpecFieldCatalog.ConstraintKey, $"kind '{kind}' missing constraint");
                CollectionAssert.Contains(keys, SpecFieldCatalog.SizingWKey, $"kind '{kind}' missing sizingW");
                CollectionAssert.Contains(keys, SpecFieldCatalog.SizingHKey, $"kind '{kind}' missing sizingH");
            }
        }

        [Test]
        public void Catalog_AutoLayout_OnlyForStacks()
        {
            foreach (string stack in new[] { "vstack", "hstack", "grid" })
                CollectionAssert.Contains(SpecFieldCatalog.For(stack).Select(f => f.key).ToList(),
                    SpecFieldCatalog.AutoLayoutKey, $"{stack} should expose auto-layout");

            foreach (string leaf in new[] { "button", "text", "image" })
                CollectionAssert.DoesNotContain(SpecFieldCatalog.For(leaf).Select(f => f.key).ToList(),
                    SpecFieldCatalog.AutoLayoutKey, $"{leaf} should not expose auto-layout");
        }

        [Test]
        public void Catalog_LayoutEditors_AreNotInPinnedSingleValueKeys()
        {
            // composite editors must stay OUT of AllKeys (which pins the single-value field set)
            CollectionAssert.DoesNotContain(SpecFieldCatalog.AllKeys(), SpecFieldCatalog.ConstraintKey);
            CollectionAssert.DoesNotContain(SpecFieldCatalog.AllKeys(), SpecFieldCatalog.SizingWKey);
            CollectionAssert.DoesNotContain(SpecFieldCatalog.AllKeys(), SpecFieldCatalog.AutoLayoutKey);
        }

        [Test]
        public void Catalog_StillExtensible_RegisteredFieldAppears()
        {
            try
            {
                var field = new SpecField("projField", "Project Field", FieldKind.Text,
                    e => e.label, (e, v) => e.label = (string)v, new[] { "button" });
                SpecFieldCatalog.RegisterField(field);
                CollectionAssert.Contains(SpecFieldCatalog.For("button").Select(f => f.key).ToList(), "projField");
            }
            finally { SpecFieldCatalog.ClearRegisteredForTests(); }
        }

        // ---------------------------------------------------------------- constraint offset round-trip

        [Test]
        public void Constraint_EdgeOffsets_RoundTrip()
        {
            AssertOffsetRoundTrip(LayoutConstraints.Left, LayoutAxis.Horizontal, 24f, 0f, "left");
            AssertOffsetRoundTrip(LayoutConstraints.Right, LayoutAxis.Horizontal, 16f, 0f, "right");
            AssertOffsetRoundTrip(LayoutConstraints.Top, LayoutAxis.Vertical, 12f, 0f, "top");
            AssertOffsetRoundTrip(LayoutConstraints.Bottom, LayoutAxis.Vertical, 8f, 0f, "bottom");
        }

        [Test]
        public void Constraint_StretchOffsets_RoundTrip()
        {
            AssertOffsetRoundTrip(LayoutConstraints.LeftRight, LayoutAxis.Horizontal, 24f, 48f, "left", "right");
            AssertOffsetRoundTrip(LayoutConstraints.TopBottom, LayoutAxis.Vertical, 10f, 20f, "top", "bottom");
        }

        [Test]
        public void Constraint_ScaleAndCenter_RoundTrip()
        {
            AssertOffsetRoundTrip(LayoutConstraints.Scale, LayoutAxis.Horizontal, 0.1f, 0.9f, "left", "right");
            AssertOffsetRoundTrip(LayoutConstraints.Scale, LayoutAxis.Vertical, 0.2f, 0.8f, "bottom", "top");
            AssertOffsetRoundTrip(LayoutConstraints.Center, LayoutAxis.Horizontal, 30f, 0f, "h");
            AssertOffsetRoundTrip(LayoutConstraints.Center, LayoutAxis.Vertical, -15f, 0f, "v");
        }

        // writes the primary/secondary through WriteOffsets, asserts the named keys, then ReadOffsets
        // recovers the same primary/secondary (the inspector ↔ spec mapping is lossless).
        private static void AssertOffsetRoundTrip(string id, LayoutAxis axis, float primary, float secondary,
            params string[] expectedKeys)
        {
            var off = new LayoutOffset();
            SpecInspector.WriteOffsets(off, id, axis, primary, secondary);
            foreach (string key in expectedKeys)
                Assert.IsTrue(off.values.ContainsKey(key), $"{id}: expected offset key '{key}'");

            SpecInspector.ReadOffsets(off, id, axis, out float p, out float s);
            Assert.AreEqual(primary, p, 1e-4f, $"{id} primary");
            if (expectedKeys.Length > 1) Assert.AreEqual(secondary, s, 1e-4f, $"{id} secondary");
        }

        // ---------------------------------------------------------------- sizing modes from registry

        [Test]
        public void SizingModes_ComeFromRegistry_IncludingProjectMode()
        {
            try
            {
                LayoutSizingModes.Register(new ClampMode());
                var ids = LayoutSizingModes.All.Select(m => m.Id).ToList();
                CollectionAssert.Contains(ids, LayoutSizingModes.Fixed);
                CollectionAssert.Contains(ids, LayoutSizingModes.Hug);
                CollectionAssert.Contains(ids, LayoutSizingModes.Fill);
                CollectionAssert.Contains(ids, "clamp");
            }
            finally { LayoutSizingModes.ResetForTests(); }
        }

        private sealed class ClampMode : ILayoutSizingMode
        {
            public string Id => "clamp";
            public bool WantsForceExpand => false;
            public void Apply(UnityEngine.GameObject go, bool horizontal, float? size) { }
            public bool TryDetect(UnityEngine.GameObject go, bool horizontal) => false;
        }

        // ---------------------------------------------------------------- active edit breakpoint scope

        [Test]
        public void Document_ActiveBreakpoint_DefaultsToBase()
        {
            var doc = new SpecDocument();
            Assert.AreEqual("", doc.ActiveBreakpoint);
            Assert.IsFalse(doc.IsEditingOverride);
        }

        [Test]
        public void Document_SetActiveBreakpoint_RejectsUnknownName()
        {
            var doc = new SpecDocument();
            LogAssert_ExpectWarning();
            doc.SetActiveBreakpoint("ghost");
            Assert.AreEqual("", doc.ActiveBreakpoint, "unknown breakpoint should fall back to base");
            Assert.IsFalse(doc.IsEditingOverride);
        }

        [Test]
        public void Document_SetActiveBreakpoint_AcceptsDeclaredName()
        {
            var doc = new SpecDocument();
            doc.Spec.breakpoints.Add(new BreakpointSpec { name = "landscape" });
            int raised = 0;
            doc.ActiveBreakpointChanged += () => raised++;
            doc.SetActiveBreakpoint("landscape");
            Assert.AreEqual("landscape", doc.ActiveBreakpoint);
            Assert.IsTrue(doc.IsEditingOverride);
            Assert.AreEqual(1, raised);
            doc.SetActiveBreakpoint("landscape"); // idempotent
            Assert.AreEqual(1, raised);
        }

        // ---------------------------------------------------------------- override delta lands in dict

        [Test]
        public void OverrideDelta_MergesOverBase_NotMutatingBase()
        {
            // base: left/top, w 200; override: center h, w 900 (the landscape delta)
            var e = new ElementSpec
            {
                kind = "vstack",
                layout = new LayoutSpec
                {
                    h = LayoutConstraints.Left, v = LayoutConstraints.Top,
                    size = new LayoutSize { w = 200f }
                }
            };
            e.overrides = new Dictionary<string, LayoutSpec>
            {
                ["landscape"] = new LayoutSpec { h = LayoutConstraints.Center, size = new LayoutSize { w = 900f } }
            };

            LayoutSpec effective = e.layout.MergedWith(e.overrides["landscape"]);
            Assert.AreEqual(LayoutConstraints.Center, effective.h, "delta h wins");
            Assert.AreEqual(LayoutConstraints.Top, effective.v, "unset delta v inherits base");
            Assert.AreEqual(900f, effective.size.w, "delta size wins");

            // base is untouched by the merge
            Assert.AreEqual(LayoutConstraints.Left, e.layout.h);
            Assert.AreEqual(200f, e.layout.size.w);
        }

        [Test]
        public void OverrideDelta_RoundTripsThroughJson()
        {
            var spec = new UISpec();
            spec.breakpoints.Add(new BreakpointSpec { name = "narrow", when = new BreakpointCondition { maxWidth = 768f } });
            var view = new ViewSpec { category = "Menu", viewName = "Main" };
            view.elements.Add(new ElementSpec
            {
                kind = "vstack",
                layout = new LayoutSpec { h = LayoutConstraints.LeftRight, offset = MakeOffset(("left", 24f), ("right", 24f)) },
                overrides = new Dictionary<string, LayoutSpec>
                {
                    ["narrow"] = new LayoutSpec { offset = MakeOffset(("left", 8f), ("right", 8f)) }
                }
            });
            spec.views.Add(view);

            UISpec round = UISpec.FromJson(spec.ToJson());
            ElementSpec e = round.views[0].elements[0];
            Assert.IsNotNull(e.overrides, "overrides survived round-trip");
            Assert.IsTrue(e.overrides.ContainsKey("narrow"));
            Assert.AreEqual(8f, e.overrides["narrow"].offset.GetOr("left", -1f), 1e-4f);
        }

        private static LayoutOffset MakeOffset(params (string key, float value)[] entries)
        {
            var off = new LayoutOffset();
            foreach (var (key, value) in entries) off.Set(key, value);
            return off;
        }

        // small local helper so the test reads cleanly without pulling in the whole LogAssert surface
        private static void LogAssert_ExpectWarning() =>
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex(".*"));
    }
}
