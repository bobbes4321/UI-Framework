using System.Collections.Generic;
using System.Reflection;
using AlterEyes.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// AEShape mesh generation: the SDF shader gets every parameter through vertex channels, so
    /// these tests pin the packing contract (uv0 = pos/half-size, uv1 = radii or stroke,
    /// uv2 = mode/border/softness/angle, uv3 = border color, tangent = gradient color B).
    /// </summary>
    public class AEShapeTests
    {
        private GameObject _go;
        private AEShape _shape;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("shape", typeof(RectTransform));
            _go.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 100f);
            _shape = _go.AddComponent<AEShape>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        private List<UIVertex> Populate()
        {
            MethodInfo populate = typeof(AEShape).GetMethod("OnPopulateMesh",
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(VertexHelper) }, null);
            Assert.NotNull(populate, "AEShape.OnPopulateMesh(VertexHelper) not found");
            var stream = new List<UIVertex>();
            using (var vh = new VertexHelper())
            {
                populate.Invoke(_shape, new object[] { vh });
                vh.GetUIVertexStream(stream);
            }
            return stream;
        }

        [Test]
        public void RoundedRect_PacksQuadWithChannels()
        {
            _shape.cornerRadius = 12f;
            _shape.border = 3f;
            _shape.outlineColor = Color.red;
            _shape.colorB = Color.blue;

            List<UIVertex> verts = Populate(); // 2 triangles
            Assert.AreEqual(6, verts.Count);

            foreach (UIVertex v in verts)
            {
                Assert.AreEqual(100f, v.uv0.z, 1e-3f, "uv0.z must carry half width");
                Assert.AreEqual(50f, v.uv0.w, 1e-3f, "uv0.w must carry half height");
                Assert.AreEqual(v.position.x, v.uv0.x, 1e-3f, "uv0.xy must be the local position");
                Assert.AreEqual(v.position.y, v.uv0.y, 1e-3f);
                Assert.AreEqual(new Vector4(12f, 12f, 12f, 12f), v.uv1, "uniform radius in all four slots");
                Assert.AreEqual(0f, v.uv2.x, 1e-3f, "rounded rect + solid fill packs mode 0");
                Assert.AreEqual(3f, v.uv2.y, 1e-3f, "border width");
                Assert.AreEqual((Vector4)Color.red, v.uv3, "border color rides uv3");
                Assert.AreEqual((Vector4)Color.blue, v.tangent, "gradient color B rides the tangent");
            }
        }

        [Test]
        public void PerCornerRadii_ReorderedForShader()
        {
            _shape.useUniformRadius = false;
            _shape.cornerRadii = new Vector4(1f, 2f, 3f, 4f); // TL, TR, BR, BL

            List<UIVertex> verts = Populate();
            // shader order: x=TR y=BR z=TL w=BL
            Assert.AreEqual(new Vector4(2f, 3f, 1f, 4f), verts[0].uv1);
        }

        [Test]
        public void Softness_ExpandsMeshBeyondRect()
        {
            _shape.edgeSoftness = 20f;
            List<UIVertex> verts = Populate();

            float maxX = float.MinValue;
            foreach (UIVertex v in verts) maxX = Mathf.Max(maxX, v.position.x);
            Assert.Greater(maxX, 100f + 20f - 1f, "mesh must expand by softness so the blur is not clipped");
            Assert.AreEqual(20f, verts[0].uv2.z, 1e-3f, "softness rides uv2.z");
        }

        [Test]
        public void LinearGradient_PacksFillModeAndAngle()
        {
            _shape.fill = ShapeFillMode.LinearGradient;
            _shape.gradientAngleDegrees = 90f;

            List<UIVertex> verts = Populate();
            Assert.AreEqual(16f, verts[0].uv2.x, 1e-3f, "fill mode packs into bits above the shape mode");
            Assert.AreEqual(90f * Mathf.Deg2Rad, verts[0].uv2.w, 1e-4f);
        }

        [Test]
        public void Glyph_AutoStroke_ScalesWithRect()
        {
            _shape.shape = ShapeType.Checkmark;
            _shape.border = 0f; // auto

            List<UIVertex> verts = Populate();
            // rect 200x100 -> minHalf 50 -> auto stroke = 50 * 0.32 = 16
            Assert.AreEqual(16f, verts[0].uv1.x, 1e-3f, "auto stroke rides uv1.x");
            Assert.AreEqual(0f, verts[0].uv2.y, 1e-3f, "glyphs have no fill/border split");
        }

        [Test]
        public void ResolveCornerRadii_Percent_ConvertsToPixels()
        {
            _shape.cornerRadiusUnit = ShapeRadiusUnit.Percent;
            _shape.cornerRadius = 50f;
            Vector4 corners = _shape.ResolveCornerRadii(new Vector2(100f, 50f), 50f);
            Assert.AreEqual(25f, corners.x, 1e-3f, "50% of half the smaller dimension (50) is 25 px");
        }

        [Test]
        public void ResolveCornerRadii_Overlap_ScalesAllCornersDown()
        {
            _shape.cornerRadius = 100f;
            Vector4 corners = _shape.ResolveCornerRadii(new Vector2(50f, 50f), 50f);
            // adjacent corners would cover 200 px of a 100 px edge -> CSS rule halves them all
            Assert.AreEqual(50f, corners.x, 1e-3f);
            Assert.AreEqual(50f, corners.z, 1e-3f);
        }

        [Test]
        public void ResolveCornerRadii_Pill_UsesHalfMinDimension()
        {
            _shape.shape = ShapeType.Pill;
            Vector4 corners = _shape.ResolveCornerRadii(new Vector2(100f, 50f), 50f);
            Assert.AreEqual(new Vector4(50f, 50f, 50f, 50f), corners);
        }
    }

    public class ShapeStyleTests
    {
        [Test]
        public void Theme_ShapeStyles_UpsertLookupRemove()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetShapeStyle(new ShapeStyle { name = "Card", radius = 16f });
                theme.SetShapeStyle(new ShapeStyle { name = "Chip", radius = 99f });
                theme.SetShapeStyle(new ShapeStyle { name = "Card", radius = 24f }); // upsert replaces

                CollectionAssert.AreEquivalent(new[] { "Card", "Chip" }, theme.GetShapeStyleNames());
                Assert.IsTrue(theme.TryGetShapeStyle("Card", out ShapeStyle card));
                Assert.AreEqual(24f, card.radius);

                Assert.IsTrue(theme.RemoveShapeStyle("Chip"));
                Assert.IsFalse(theme.TryGetShapeStyle("Chip", out _));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void ShapeStyle_ApplyTo_CopiesSurfaceFields()
        {
            var go = new GameObject("shape", typeof(RectTransform));
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetToken("Outline", Color.cyan);
                var shape = go.AddComponent<AEShape>();
                var style = new ShapeStyle
                {
                    name = "Card",
                    radius = 17f,
                    borderWidth = 3f,
                    borderColor = new ThemeColorRef("Outline"),
                    softness = 5f
                };

                style.ApplyTo(shape, theme);

                Assert.AreEqual(17f, shape.cornerRadius);
                Assert.AreEqual(3f, shape.border);
                Assert.AreEqual(Color.cyan, shape.outlineColor, "border color must resolve through the theme");
                Assert.AreEqual(5f, shape.edgeSoftness);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(theme);
            }
        }
    }

    public class AEGradientTests
    {
        [Test]
        public void ModifyMesh_MultipliesVertexColorsAlongAngle()
        {
            var go = new GameObject("gradient", typeof(RectTransform));
            try
            {
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 100f);
                go.AddComponent<AEShape>();
                var gradient = go.AddComponent<AEGradient>();
                gradient.colorA = new ThemeColorRef(Color.black);
                gradient.colorB = new ThemeColorRef(Color.white);
                gradient.angle = 90f; // bottom -> top

                using (var vh = new VertexHelper())
                {
                    UIVertex v = UIVertex.simpleVert;
                    v.color = Color.white;
                    v.position = new Vector3(-50f, -50f); vh.AddVert(v);
                    v.position = new Vector3(50f, -50f); vh.AddVert(v);
                    v.position = new Vector3(50f, 50f); vh.AddVert(v);
                    v.position = new Vector3(-50f, 50f); vh.AddVert(v);
                    vh.AddTriangle(0, 1, 2);
                    vh.AddTriangle(2, 3, 0);

                    gradient.ModifyMesh(vh);

                    UIVertex bottom = default, top = default;
                    vh.PopulateUIVertex(ref bottom, 0);
                    vh.PopulateUIVertex(ref top, 2);
                    Assert.AreEqual(0, ((Color32)bottom.color).r, "bottom edge multiplied by colorA (black)");
                    Assert.AreEqual(255, ((Color32)top.color).r, "top edge multiplied by colorB (white)");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
