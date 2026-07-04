using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The <c>importSprites</c> bridge action had zero coverage at any level (audit §2.5). Drives it
    /// through the real JSON entry point (invocation pattern: <see cref="SyncProtocolTests"/> lines
    /// 170-180) against a texture imported with defaults — plain <c>textureType</c> alone leaves
    /// <c>spriteImportMode</c> at <c>None</c>, so no Sprite sub-asset exists yet — and asserts it comes
    /// out Single + a loadable Sprite sub-asset, which is what a spec image <c>src</c> needs.
    /// </summary>
    public class ImportSpritesActionTests
    {
        private const string ArtFolder = "Assets/NeoUITestScratch/ImportSpritesArt";

        private string _texturePath;

        [SetUp]
        public void CreateTexture()
        {
            if (!AssetDatabase.IsValidFolder(NeoTestScratchRoot.ScratchRoot))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratch");
            if (!AssetDatabase.IsValidFolder(ArtFolder))
                AssetDatabase.CreateFolder(NeoTestScratchRoot.ScratchRoot, "ImportSpritesArt");

            _texturePath = $"{ArtFolder}/scratch-sprite-{Guid.NewGuid():N}.png";
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] pixels = Enumerable.Repeat((Color32)Color.cyan, 16).ToArray();
            texture.SetPixels32(pixels);
            File.WriteAllBytes(_texturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(_texturePath);
        }

        [TearDown]
        public void DeleteTexture()
        {
            if (!string.IsNullOrEmpty(_texturePath)) AssetDatabase.DeleteAsset(_texturePath);
        }

        [OneTimeTearDown]
        public void Cleanup() => AssetDatabase.DeleteAsset(ArtFolder);

        [Test]
        public void ImportSprites_TurnsPlainTexturesIntoSingleSprites()
        {
            var importerBefore = (TextureImporter)AssetImporter.GetAtPath(_texturePath);
            Assert.AreNotEqual(TextureImporterType.Sprite, importerBefore.textureType,
                "precondition: the texture must start out as a plain (non-sprite) texture");

            string request = "{\"action\":\"importSprites\",\"folder\":\"" + ArtFolder + "\"}";
            var result = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(request)), "result");

            Assert.AreEqual(true, result["ok"], result.TryGetValue("error", out object err) ? (string)err : null);
            var imported = (List<object>)result["imported"];
            Assert.IsTrue(imported.Contains(_texturePath), "the touched texture must be reported as imported");

            var importerAfter = (TextureImporter)AssetImporter.GetAtPath(_texturePath);
            Assert.AreEqual(TextureImporterType.Sprite, importerAfter.textureType);
            Assert.AreEqual(SpriteImportMode.Single, importerAfter.spriteImportMode);

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(_texturePath);
            Assert.IsNotNull(sprite, "a Sprite sub-asset must be loadable after import");
        }

        [Test]
        public void ImportSprites_AlreadySingleSprite_IsSkipped_NotReimported()
        {
            string request = "{\"action\":\"importSprites\",\"folder\":\"" + ArtFolder + "\"}";
            var first = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(request)), "result");
            Assert.AreEqual(true, first["ok"]);
            CollectionAssert.Contains((List<object>)first["imported"], _texturePath);

            var second = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(request)), "result");
            Assert.AreEqual(true, second["ok"]);
            CollectionAssert.DoesNotContain((List<object>)second["imported"], _texturePath,
                "an already-Single sprite must be skipped on a repeat run");
        }

        [Test]
        public void ImportSprites_MissingFolder_ReportsError_NotSilentFailure()
        {
            string request = "{\"action\":\"importSprites\",\"folder\":\"Assets/DoesNotExist_NeoUI\"}";
            var result = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(request)), "result");

            Assert.AreEqual(false, result["ok"]);
            StringAssert.Contains("folder", ((string)result["error"]).ToLowerInvariant(),
                "the failure must explain what was wrong, not fail silently");
        }
    }
}
