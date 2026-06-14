using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pure (no Unity scene) parse/emit coverage for the constraint+offset <see cref="LayoutSpec"/>
    /// data model and the constraint/sizing registries. Asserts the §A.2.1 JSON round-trips
    /// byte-identically and that the seams ship their built-ins.
    /// </summary>
    public class LayoutSpecParseTests
    {
        [Test]
        public void LayoutSpec_ParsesAndReEmits_ByteIdentical()
        {
            const string json = @"{
              ""h"": ""leftRight"",
              ""v"": ""center"",
              ""offset"": { ""left"": 24, ""right"": 24, ""v"": 0 },
              ""size"": { ""h"": 96 },
              ""sizing"": { ""w"": ""fill"", ""h"": ""fixed"" }
            }";
            var obj = JsonReader.AsObject(MiniJson.Parse(json), "layout");
            LayoutSpec spec = LayoutSpec.Parse(obj);
            Assert.IsNotNull(spec);

            string first = MiniJson.Serialize(spec.ToJsonObject());
            LayoutSpec reparsed = LayoutSpec.Parse(JsonReader.AsObject(MiniJson.Parse(first), "layout"));
            string second = MiniJson.Serialize(reparsed.ToJsonObject());

            Assert.AreEqual(first, second, "LayoutSpec must round-trip byte-identically");
            Assert.AreEqual("leftRight", spec.h);
            Assert.AreEqual("center", spec.v);
            Assert.AreEqual(24f, spec.offset.GetOr("left", -1f));
            Assert.AreEqual(0f, spec.offset.GetOr("v", -1f));
            Assert.AreEqual(96f, spec.size.h);
            Assert.IsNull(spec.size.w);
            Assert.AreEqual("fill", spec.sizing.w);
            Assert.AreEqual("fixed", spec.sizing.h);
        }

        [Test]
        public void LayoutSpec_KeyOrder_IsDeterministic()
        {
            // build with keys in a scrambled order; emit must always be h,v,offset,size,sizing and
            // offsets must be left,right,top,bottom,h,v
            var spec = new LayoutSpec
            {
                v = "bottom",
                h = "left",
                size = new LayoutSize { w = 320f },
                sizing = new LayoutSizing { w = "fixed" },
                offset = new LayoutOffset()
            };
            spec.offset.Set("bottom", 64f);
            spec.offset.Set("left", 40f);

            string json = MiniJson.Serialize(spec.ToJsonObject());
            int hi = json.IndexOf("\"h\"");
            int vi = json.IndexOf("\"v\"");
            int oi = json.IndexOf("\"offset\"");
            int si = json.IndexOf("\"size\"");
            int zi = json.IndexOf("\"sizing\"");
            Assert.Less(hi, vi, "h before v");
            Assert.Less(vi, oi, "v before offset");
            Assert.Less(oi, si, "offset before size");
            Assert.Less(si, zi, "size before sizing");

            int li = json.IndexOf("\"left\"");
            int bi = json.IndexOf("\"bottom\"");
            Assert.Less(li, bi, "offset emits left before bottom");
        }

        [Test]
        public void LayoutSpec_Empty_ParsesToNull_AndNeverEmits()
        {
            Assert.IsNull(LayoutSpec.Parse(null));
            var obj = JsonReader.AsObject(MiniJson.Parse("{}"), "layout");
            Assert.IsNull(LayoutSpec.Parse(obj), "an empty layout object parses to null so it never emits");

            var empty = new LayoutSpec();
            Assert.IsTrue(empty.IsEmpty);
        }

        [Test]
        public void ElementSpec_OmitsLayout_WhenAbsent_AndKeepsLegacyFields()
        {
            const string legacy = @"{ ""image"": { ""anchor"": ""TopRight"", ""size"": [200, 60], ""position"": [-20, -20] } }";
            ElementSpec element = ElementSpec.Parse(JsonReader.AsObject(MiniJson.Parse(legacy), "el"));
            Assert.IsNull(element.layout, "legacy element must not synthesize a layout");

            string emitted = MiniJson.Serialize(element.ToJsonObject());
            Assert.IsFalse(emitted.Contains("\"layout\""), "absent layout must never be emitted");
            Assert.IsTrue(emitted.Contains("\"anchor\""), "legacy fields must still emit");
            Assert.IsTrue(emitted.Contains("\"position\""));
        }

        [Test]
        public void ElementSpec_EmitsLayout_AfterAnchor_WhenPresent()
        {
            const string json = @"{ ""button"": { ""label"": ""Play"",
              ""layout"": { ""h"": ""center"", ""v"": ""bottom"", ""offset"": { ""bottom"": 64 }, ""size"": { ""w"": 320, ""h"": 96 } } } }";
            ElementSpec element = ElementSpec.Parse(JsonReader.AsObject(MiniJson.Parse(json), "el"));
            Assert.IsNotNull(element.layout);
            Assert.AreEqual("center", element.layout.h);

            string first = MiniJson.Serialize(element.ToJsonObject());
            ElementSpec re = ElementSpec.Parse(JsonReader.AsObject(MiniJson.Parse(first), "el"));
            string second = MiniJson.Serialize(re.ToJsonObject());
            Assert.AreEqual(first, second, "an element with a layout round-trips byte-identically");
        }

        [Test]
        public void Registries_ShipBuiltins_AndWarnOnMissing()
        {
            // constraints: 5 per axis
            Assert.AreEqual(10, LayoutConstraints.All.Count);
            Assert.IsNotNull(LayoutConstraints.Get(LayoutConstraints.Left, LayoutAxis.Horizontal));
            Assert.IsNotNull(LayoutConstraints.Get(LayoutConstraints.TopBottom, LayoutAxis.Vertical));
            Assert.IsTrue(LayoutConstraints.Get(LayoutConstraints.LeftRight, LayoutAxis.Horizontal).Stretches);
            Assert.IsFalse(LayoutConstraints.Get(LayoutConstraints.Left, LayoutAxis.Horizontal).Stretches);

            // a horizontal-only id must not resolve on the vertical axis
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("no constraint"));
            Assert.IsNull(LayoutConstraints.Get(LayoutConstraints.Left, LayoutAxis.Vertical));

            // sizing modes: 3
            Assert.AreEqual(3, LayoutSizingModes.All.Count);
            Assert.IsTrue(LayoutSizingModes.Get(LayoutSizingModes.Fill).WantsForceExpand);
            Assert.IsFalse(LayoutSizingModes.Get(LayoutSizingModes.Fixed).WantsForceExpand);
        }

        [Test]
        public void Registry_Register_ReplacesByIdAndAxis()
        {
            try
            {
                int before = LayoutConstraints.All.Count;
                LayoutConstraints.Register(new StubConstraint("left", LayoutAxis.Horizontal));
                Assert.AreEqual(before, LayoutConstraints.All.Count, "replace-by-id+axis, not append");
                LayoutConstraints.Register(new StubConstraint("projcustom", LayoutAxis.Horizontal));
                Assert.AreEqual(before + 1, LayoutConstraints.All.Count, "a novel id appends");
            }
            finally
            {
                LayoutConstraints.ResetForTests();
            }
        }

        private sealed class StubConstraint : ILayoutConstraint
        {
            public StubConstraint(string id, LayoutAxis axis) { Id = id; Axis = axis; }
            public string Id { get; }
            public LayoutAxis Axis { get; }
            public bool Stretches => false;
            public void Apply(UnityEngine.RectTransform rect, LayoutOffsetValue offset, float? size) { }
            public bool TryDetect(UnityEngine.RectTransform rect, out LayoutOffsetValue offset, out float? size)
            { offset = default; size = null; return false; }
        }
    }
}
