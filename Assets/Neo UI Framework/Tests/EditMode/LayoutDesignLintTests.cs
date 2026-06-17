using System.IO;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The two layout-guardrail design lints (soft <c>designWarnings</c>, never in <c>ValidateAll</c>)
    /// that catch the layout/aspect bug class that shipped bad showcases:
    /// <list type="number">
    /// <item>an authored fixed extent silently stomped by a force-expanding parent layout group (the
    /// element carries a <see cref="UnityEngine.UI.LayoutElement"/> preferred size but no
    /// <see cref="UnityEngine.UI.ContentSizeFitter"/> opt-out, so the group stretches it); and</item>
    /// <item>an <c>image</c> whose rect aspect is extreme, so its full-rect sprite fill distorts or
    /// crops badly.</item>
    /// </list>
    /// </summary>
    public class LayoutDesignLintTests
    {
        private const string ArtFolder = "Assets/NeoUILayoutLintArt";
        private const string SpritePath = ArtFolder + "/lint-art.png";

        [OneTimeSetUp]
        public void CreateSpriteAsset()
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder))
                AssetDatabase.CreateFolder("Assets", "NeoUILayoutLintArt");
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Color32[] pixels = Enumerable.Repeat((Color32)Color.cyan, 64).ToArray();
            texture.SetPixels32(pixels);
            File.WriteAllBytes(SpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(SpritePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single; // a Single Sprite sub-asset must exist for image src
            importer.SaveAndReimport();
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.DeleteAsset(ArtFolder);
            AssetDatabase.SaveAssets();
        }

        private static System.Collections.Generic.List<string> GenerateAndLint(string json)
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.collisions, report.ToString());
            return AgentValidation.ValidateDesign();
        }

        /// <summary>
        /// A legacy <c>size</c> on a shape inside a vstack sets a LayoutElement preferredWidth with NO
        /// ContentSizeFitter — the vstack force-expands width and stomps it. That must be flagged. The
        /// same authored width via <c>layout.sizing:"fixed"</c> (which adds a ContentSizeFitter) is the
        /// opt-out and must NOT be flagged. A plain full-width button/panel (no authored width) neither.
        /// </summary>
        [Test]
        public void ForceExpandStomp_FlagsUnprotectedFixedWidth_NotTheFitterOptOut()
        {
            string json = @"{
              ""views"": [ { ""id"": ""LintLayout/Stomp"", ""elements"": [
                { ""vstack"": { ""id"": ""LintLayout/Col"", ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 16, ""children"": [
                  { ""shape"": { ""id"": ""LintLayout/Stomped"", ""shape"": ""RoundedRect"", ""size"": [240, 80] } },
                  { ""shape"": { ""id"": ""LintLayout/Protected"", ""shape"": ""RoundedRect"",
                      ""layout"": { ""sizing"": { ""w"": ""fixed"" }, ""size"": { ""w"": 240, ""h"": 80 } } } },
                  { ""button"": { ""id"": ""LintLayout/FullWidth"", ""label"": ""Play"", ""onClick"": { ""signal"": ""X/Y"" } } }
                ] } }
              ] } ]
            }";

            var warnings = GenerateAndLint(json);
            bool Stomped(string name) =>
                warnings.Any(w => w.Contains(name) && w.Contains("force-expands width"));

            Assert.IsTrue(Stomped("LintLayout_Stomped"),
                "an authored width with no ContentSizeFitter inside a force-expanding vstack must be flagged:\n"
                + string.Join("\n", warnings));
            Assert.IsFalse(Stomped("LintLayout_Protected"),
                "a sizing:\"fixed\" child (carries a ContentSizeFitter opt-out) must NOT be flagged");
            Assert.IsFalse(warnings.Any(w => w.Contains("LintLayout_FullWidth") && w.Contains("force-expands")),
                "a normal full-width button has no authored width and must NOT be flagged");
        }

        /// <summary>
        /// An image with a ~5:1 rect distorts/crops its sprite — flagged. A square-ish (1:1-ish) image
        /// must NOT be flagged.
        /// </summary>
        [Test]
        public void ImageAspect_FlagsExtremeRect_NotBalancedRect()
        {
            string json = @"{
              ""views"": [ { ""id"": ""LintLayout/Aspect"", ""elements"": [
                { ""image"": { ""id"": ""LintLayout/Wide"", ""src"": """ + SpritePath + @""", ""anchor"": ""Center"", ""size"": [500, 100] } },
                { ""image"": { ""id"": ""LintLayout/Square"", ""src"": """ + SpritePath + @""", ""anchor"": ""Center"", ""size"": [200, 200] } }
              ] } ]
            }";

            var warnings = GenerateAndLint(json);
            bool ExtremeAspect(string name) =>
                warnings.Any(w => w.Contains(name) && w.Contains("distort or crop"));

            Assert.IsTrue(ExtremeAspect("LintLayout_Wide"),
                "a 5:1 image rect must be flagged:\n" + string.Join("\n", warnings));
            Assert.IsFalse(ExtremeAspect("LintLayout_Square"),
                "a 1:1 image rect must NOT be flagged");
        }
    }
}
