using System.Collections;
using Neo.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    public class GraphicsPlayModeTests : PlayModeTestBase
    {
        private const AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3 |
            AdditionalCanvasShaderChannels.Tangent;

        private Theme _theme;

        public override void TearDown()
        {
            base.TearDown();
            if (_theme != null) Object.Destroy(_theme);
        }

        [UnityTest]
        public IEnumerator NeoShape_OnCanvas_EnablesVertexChannels()
        {
            GameObject go = CreateUIObject("Shape");
            go.AddComponent<NeoShape>();
            yield return null;

            Assert.AreEqual(RequiredChannels, canvas.additionalShaderChannels & RequiredChannels,
                "NeoShape must opt its canvas into UV1-3 + tangent, or the shader gets zeroed channels");
        }

        [UnityTest]
        public IEnumerator NeoShape_RendersWithSharedShapeShader()
        {
            GameObject go = CreateUIObject("Shape");
            var shape = go.AddComponent<NeoShape>();
            yield return null;

            Assert.NotNull(shape.materialForRendering, "shape must have a render material");
            Assert.AreEqual(NeoShape.ShaderName, shape.materialForRendering.shader.name,
                "the NeoShape shader asset must be importable and found by name");
            Assert.AreSame(NeoShape.sharedShapeMaterial, shape.material,
                "all shapes share one material so they batch");
        }

        [UnityTest]
        public IEnumerator ThemeShapeStyleTarget_AppliesStyle_AndFollowsThemeEdits()
        {
            _theme = ScriptableObject.CreateInstance<Theme>();
            _theme.SetToken("Surface", Color.green);
            _theme.SetShapeStyle(new ShapeStyle
            {
                name = "Card",
                radius = 17f,
                borderWidth = 2f,
                fillColor = new ThemeColorRef("Surface")
            });

            GameObject go = CreateUIObject("Styled");
            var shape = go.AddComponent<NeoShape>();
            var styleTarget = go.AddComponent<ThemeShapeStyleTarget>();
            styleTarget.themeOverride = _theme;
            styleTarget.style = "Card";
            yield return null; // Start() re-applies after the fields land

            Assert.AreEqual(17f, shape.cornerRadius);
            Assert.AreEqual(2f, shape.border);
            Assert.AreEqual(Color.green, shape.color, "fill color must resolve through the theme token");

            // live edit: change the style on the theme -> bound shape restyles
            _theme.GetShapeStyle("Card").radius = 30f;
            _theme.RaiseChanged();
            Assert.AreEqual(30f, shape.cornerRadius);
        }
    }
}
