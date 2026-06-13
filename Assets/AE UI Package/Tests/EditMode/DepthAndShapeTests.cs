using System.Linq;
using AlterEyes.UI;
using AlterEyes.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// Beautification P4: Ring/Arc SDF shapes, the theme-riding spec gradient (AEGradient),
    /// ShapeStyle gradient/elevation extensions, the standardized elevation recipe and the
    /// radial progress widget — plus their deterministic export.
    /// </summary>
    public class DepthAndShapeTests
    {
        private const string DepthSpecJson = @"{
          ""views"": [ { ""id"": ""Depth/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""shape"": ""Ring"", ""thickness"": 12, ""background"": ""Primary"", ""size"": [96, 96] } },
              { ""shape"": { ""shape"": ""Arc"", ""thickness"": 10, ""arcStart"": 45, ""arcSweep"": 180, ""background"": ""Success"" } },
              { ""shape"": { ""shape"": ""RoundedRect"", ""radius"": 20,
                ""gradient"": { ""from"": ""PrimaryHover"", ""to"": ""Primary"", ""angle"": 90 } } },
              { ""progress"": { ""style"": ""radial"", ""min"": 0, ""max"": 100, ""value"": 25 } },
              { ""progress"": { ""min"": 0, ""max"": 100, ""value"": 50 } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateDepthView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(DepthSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Depth_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        [Test]
        public void RingAndArc_Generate_WithParams()
        {
            GameObject prefab = GenerateDepthView();

            AEShape ring = prefab.GetComponentsInChildren<AEShape>(true)
                .First(s => s.shape == ShapeType.Ring && s.GetComponent<Progressor>() == null);
            Assert.AreEqual(12f, ring.ringThickness);
            Assert.AreEqual("Primary", ring.GetComponent<ThemeColorTarget>()?.token);

            AEShape arc = prefab.GetComponentsInChildren<AEShape>(true)
                .First(s => s.shape == ShapeType.Arc && s.GetComponentInParent<Progressor>(true) == null);
            Assert.AreEqual(10f, arc.ringThickness);
            Assert.AreEqual(45f, arc.arcStart);
            Assert.AreEqual(180f, arc.arcSweep);
        }

        [Test]
        public void Gradient_RidesAEGradient_WithTokenRefs()
        {
            GameObject prefab = GenerateDepthView();

            AEGradient gradient = prefab.GetComponentsInChildren<AEGradient>(true).FirstOrDefault();
            Assert.IsNotNull(gradient, "spec gradient must add an AEGradient");
            Assert.IsTrue(gradient.colorA.useToken);
            Assert.AreEqual("PrimaryHover", gradient.colorA.token);
            Assert.IsTrue(gradient.colorB.useToken);
            Assert.AreEqual("Primary", gradient.colorB.token);
            Assert.AreEqual(90f, gradient.angle);
            Assert.IsNull(gradient.GetComponent<ThemeColorTarget>(),
                "the gradient owns the color — no fill token next to it");
        }

        [Test]
        public void RadialProgress_BuildsArcDial_WYSIWYG()
        {
            GameObject prefab = GenerateDepthView();

            Progressor radial = prefab.GetComponentsInChildren<Progressor>(true)
                .First(p => p.progressTargets.Any(t => t is ShapeProgressTarget));
            Assert.AreEqual(Progressor.StartBehaviour.SetCustomValue, radial.onStartBehaviour);
            Assert.AreEqual(25f, radial.startValue);

            Assert.AreEqual(ShapeType.Ring, radial.GetComponent<AEShape>().shape, "the track is a ring");

            var target = (ShapeProgressTarget)radial.progressTargets.First(t => t is ShapeProgressTarget);
            Assert.IsNotNull(target.shape);
            Assert.AreEqual(ShapeType.Arc, target.shape.shape);
            Assert.AreEqual(90f, target.shape.arcSweep, 0.01f, "25% must bake a 90° sweep (WYSIWYG)");

            Progressor linear = prefab.GetComponentsInChildren<Progressor>(true)
                .First(p => !p.progressTargets.Any(t => t is ShapeProgressTarget));
            Assert.IsNotNull(linear.GetComponentInChildren<RectFillProgressTarget>(true),
                "plain progress stays the anchor-driven pill bar");
        }

        [Test]
        public void Export_DepthFeatures_RoundTrip()
        {
            GenerateDepthView();
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Depth/Screen");
            Assert.IsNotNull(view);
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");

            ElementSpec ring = stack.children.First(e => e.kind == "shape" && e.shape == "Ring");
            Assert.AreEqual(12f, ring.thickness);
            Assert.IsNull(ring.arcSweep, "rings have no sweep");

            ElementSpec arc = stack.children.First(e => e.kind == "shape" && e.shape == "Arc");
            Assert.AreEqual(10f, arc.thickness);
            Assert.AreEqual(45f, arc.arcStart);
            Assert.AreEqual(180f, arc.arcSweep);

            ElementSpec gradient = stack.children.First(e => e.kind == "shape" && e.gradient != null);
            Assert.AreEqual("PrimaryHover", gradient.gradient.from);
            Assert.AreEqual("Primary", gradient.gradient.to);
            Assert.AreEqual(90f, gradient.gradient.angle);
            Assert.IsNull(gradient.background, "the gradient owns the color");

            ElementSpec radial = stack.children.First(e => e.kind == "progress" && e.style == "radial");
            Assert.AreEqual(25f, radial.value);
            Assert.IsTrue(stack.children.Any(e => e.kind == "progress" && e.style == null),
                "the linear bar must not grow a style");
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_WithDepthFeatures()
        {
            GenerateDepthView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "rings/arcs/gradients/radial progress must round-trip byte-identically");
        }

        [Test]
        public void Elevation_Recipe_DrivesCardAndHelper()
        {
            GameObject card = UIWidgetFactory.CreateCard(null, new Vector2(360f, 240f));
            try
            {
                Transform shadow = card.transform.Find(UIWidgetFactory.ShadowName);
                Assert.IsNotNull(shadow, "cards carry the level-2 shadow");
                Assert.AreEqual(0, shadow.GetSiblingIndex(), "the shadow renders behind everything");

                ElevationRecipe.Level level2 = ElevationRecipe.Get(2);
                var shadowRect = (RectTransform)shadow.transform;
                Assert.AreEqual(level2.OffsetMin, shadowRect.offsetMin);
                Assert.AreEqual(level2.OffsetMax, shadowRect.offsetMax);
                Assert.AreEqual(level2.softness, shadow.GetComponent<AEShape>().edgeSoftness);

                // re-applying a different level reconfigures in place (idempotent)
                UIWidgetFactory.WithElevation(card, 3);
                Assert.AreEqual(1, card.GetComponentsInChildren<Transform>(true)
                    .Count(t => t.name == UIWidgetFactory.ShadowName), "no duplicate shadows");
                Assert.AreEqual(ElevationRecipe.Get(3).softness,
                    card.transform.Find(UIWidgetFactory.ShadowName).GetComponent<AEShape>().edgeSoftness);

                UIWidgetFactory.WithElevation(card, 0);
                Assert.IsNull(card.transform.Find(UIWidgetFactory.ShadowName), "level 0 removes the shadow");
            }
            finally
            {
                Object.DestroyImmediate(card);
            }
        }

        [Test]
        public void ShapeStyle_Gradient_AppliesToShape()
        {
            Theme theme = ScriptableObject.CreateInstance<Theme>();
            var go = new GameObject("Styled", typeof(RectTransform));
            try
            {
                theme.SetToken("GlowTop", Color.cyan);
                theme.SetShapeStyle(new ShapeStyle
                {
                    name = "Glow",
                    fillMode = ShapeFillMode.LinearGradient,
                    fillColorB = new ThemeColorRef("GlowTop"),
                    gradientAngle = 45f,
                    elevation = 1
                });

                var shape = go.AddComponent<AEShape>();
                Assert.IsTrue(theme.TryGetShapeStyle("Glow", out ShapeStyle style));
                style.ApplyTo(shape, theme);

                Assert.AreEqual(ShapeFillMode.LinearGradient, shape.fill);
                Assert.AreEqual(Color.cyan, shape.colorB, "gradient color B resolves through the theme");
                Assert.AreEqual(45f, shape.gradientAngleDegrees);
                Assert.AreEqual(1, style.elevation);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(theme);
            }
        }
    }
}
