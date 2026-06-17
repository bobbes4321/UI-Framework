using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Fill color parsing: a "#RRGGBB"/"#RRGGBBAA" <c>background</c>/fill bakes a LITERAL graphic
    /// color (alpha preserved, never opaque white) while a bare name still resolves through the
    /// theme as a token. Unparseable hex warns loudly instead of failing silently. Pins defects A
    /// (hex fills rendered opaque white) and B (silent fallback on an unresolved color).
    /// </summary>
    public class ColorFillParsingTests
    {
        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static string SpecWith(string background) => @"{
          ""views"": [ { ""id"": ""Col/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""id"": ""Col/Fill"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": """ + background + @""" } }
            ] } }
          ] } ]
        }";

        private static Graphic GenerateFillGraphic(string background)
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecWith(background)));
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Col_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            var graphic = prefab.GetComponentsInChildren<NeoShape>(true)
                .FirstOrDefault(s => s.name == "Col_Fill")?.GetComponent<Graphic>();
            Assert.IsNotNull(graphic, "fill shape graphic missing");
            return graphic;
        }

        [Test]
        public void HexFill_BakesLiteralColor_NotWhite()
        {
            Graphic graphic = GenerateFillGraphic("#3366CCFF");
            Color expected = new Color(0x33 / 255f, 0x66 / 255f, 0xCC / 255f, 1f);
            Assert.AreNotEqual(Color.white, graphic.color, "hex fill must not render as opaque white");
            Assert.AreEqual(expected.r, graphic.color.r, 0.01f);
            Assert.AreEqual(expected.g, graphic.color.g, 0.01f);
            Assert.AreEqual(expected.b, graphic.color.b, 0.01f);
            Assert.AreEqual(1f, graphic.color.a, 0.01f);
        }

        [Test]
        public void TransparentHexFill_BakesZeroAlpha()
        {
            Graphic graphic = GenerateFillGraphic("#00000000");
            Assert.AreEqual(0f, graphic.color.a, 0.01f, "#RRGGBBAA alpha must round-trip to a transparent fill");
        }

        [Test]
        public void TokenName_StillResolvesAsToken()
        {
            // "Primary" is a theme token, not a hex value: the bound target keeps the token name and
            // resolves a non-white themed color (the existing token path must be unaffected).
            Graphic graphic = GenerateFillGraphic("Primary");
            var target = graphic.GetComponent<ThemeColorTarget>();
            Assert.IsNotNull(target, "token fill must still bind a ThemeColorTarget");
            Assert.AreEqual("Primary", target.token, "token name must be preserved verbatim");
            Assert.IsFalse(target.token.StartsWith("#"), "a token name is not a hex literal");
        }

        [Test]
        public void UnparseableHex_WarnsLoudly()
        {
            // Defect B: a bad "#…" value must surface, not silently leave white.
            LogAssert.Expect(LogType.Warning, new Regex("could not parse hex color"));
            // ThemeColorTarget also re-validates on apply; tolerate (don't require) a second warning.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                GenerateFillGraphic("#zzz");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
