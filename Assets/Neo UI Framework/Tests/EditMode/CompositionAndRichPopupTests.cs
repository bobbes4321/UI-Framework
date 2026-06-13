using System.IO;
using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The card-composition surface built for art-heavy game UIs: "overlay" z-stack containers
    /// (free-anchored children inside layout cells), image "src" sprite fills riding NeoShape
    /// (rounded-corner art, one shared material) and rich popups (spec elements in the card,
    /// X close button, card size, UIPopup indexed slots) — plus deterministic export for all of it.
    /// </summary>
    public class CompositionAndRichPopupTests
    {
        private const string ArtFolder = "Assets/NeoUITestArt";
        private const string SpritePath = ArtFolder + "/test-card-art.png";

        private static string SpecJson => @"{
          ""views"": [ { ""id"": ""Mock/Card"", ""elements"": [
            { ""grid"": { ""columns"": 2, ""cellSize"": [220, 300], ""children"": [
              { ""overlay"": { ""background"": ""Surface"", ""radius"": 20, ""children"": [
                { ""image"": { ""src"": """ + SpritePath + @""", ""radius"": 20, ""anchor"": ""Stretch"" } },
                { ""text"": { ""label"": ""AQUATIC"", ""anchor"": ""Bottom"", ""position"": [0, 24], ""size"": [200, 40] } },
                { ""shape"": { ""shape"": ""Pill"", ""background"": ""Success"", ""anchor"": ""TopRight"",
                  ""position"": [-10, -10], ""size"": [64, 28] } }
              ] } },
              { ""scroll"": { ""background"": ""none"", ""size"": [300, 200], ""spacing"": 12, ""children"": [
                { ""text"": { ""label"": ""Row"" } }
              ] } }
            ] } }
          ] } ],
          ""popups"": [
            { ""name"": ""PackDetails"", ""title"": ""CUPID"", ""size"": [720, 480], ""close"": true, ""elements"": [
              { ""image"": { ""src"": """ + SpritePath + @""", ""radius"": 16, ""size"": [200, 280] } },
              { ""text"": { ""label"": ""This pack contains 4 models."" } },
              { ""button"": { ""id"": ""Popup/GetPack"", ""label"": ""GET PACK!"", ""onClick"": { ""close"": true } } }
            ] },
            { ""name"": ""Confirm"", ""title"": ""BENCH"", ""message"": ""Buy for 15?"" }
          ]
        }";

        [OneTimeSetUp]
        public void CreateSpriteAsset()
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder))
                AssetDatabase.CreateFolder("Assets", "NeoUITestArt");
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Color32[] pixels = Enumerable.Repeat((Color32)Color.magenta, 64).ToArray();
            texture.SetPixels32(pixels);
            File.WriteAllBytes(SpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(SpritePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            // textureType alone leaves spriteImportMode None — no Sprite sub-asset gets generated
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.DeleteAsset(ArtFolder);
            AssetDatabase.SaveAssets();
        }

        private static GenerateReport Generate()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            return report;
        }

        private static GameObject LoadPrefab(string relativePath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/{relativePath}");
            Assert.IsNotNull(prefab, $"generated prefab missing: {relativePath}");
            return prefab;
        }

        // ------------------------------------------------------------------ overlay

        [Test]
        public void Overlay_HostsFreeAnchoredChildren_WithDecor()
        {
            Generate();
            GameObject prefab = LoadPrefab("Views/Mock_Card.prefab");

            UIOverlay overlay = prefab.GetComponentInChildren<UIOverlay>(true);
            Assert.IsNotNull(overlay, "overlay element must add the UIOverlay marker");
            Assert.IsNull(overlay.GetComponent<LayoutGroup>(), "overlays never stack their children");

            var decor = overlay.GetComponent<NeoShape>();
            Assert.IsNotNull(decor, "background/radius on an overlay adds card decor");
            Assert.AreEqual(20f, decor.cornerRadius);
            Assert.AreEqual("Surface", overlay.GetComponent<ThemeColorTarget>()?.token);

            var badge = (RectTransform)overlay.GetComponentsInChildren<NeoShape>(true)
                .First(s => s.shape == ShapeType.Pill).transform;
            Assert.AreEqual(Vector2.one, badge.anchorMin, "TopRight anchor must survive the layout cell");
            Assert.AreEqual(new Vector2(-10f, -10f), badge.anchoredPosition);

            var art = (RectTransform)overlay.GetComponentsInChildren<NeoShape>(true)
                .First(s => s.sprite != null).transform;
            Assert.AreEqual(Vector2.zero, art.anchorMin, "Stretch child fills the overlay");
            Assert.AreEqual(Vector2.one, art.anchorMax);
        }

        // ------------------------------------------------------------------ image src

        [Test]
        public void ImageSrc_RidesAEShape_WithRoundedCorners()
        {
            Generate();
            GameObject prefab = LoadPrefab("Views/Mock_Card.prefab");

            NeoShape art = prefab.GetComponentsInChildren<NeoShape>(true).First(s => s.sprite != null);
            Assert.AreEqual(SpritePath, AssetDatabase.GetAssetPath(art.sprite));
            Assert.AreEqual(20f, art.cornerRadius, "radius rounds the art's corners");
            Assert.AreEqual(Color.white, art.color, "no tint: the sprite renders unmodified");
            Assert.AreEqual(art.sprite.texture, art.mainTexture,
                "the sprite texture binds per CanvasRenderer (shared material stays)");
        }

        [Test]
        public void ImageSrc_MissingSprite_ReportsIssue()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(@"{
              ""views"": [ { ""id"": ""Mock/Broken"", ""elements"": [
                { ""image"": { ""src"": ""Assets/DoesNotExist.png"" } } ] } ] }"));
            Assert.IsTrue(report.issues.Any(i => i.Contains("DoesNotExist")),
                "a missing sprite must be reported, never silently skipped");
            AssetDatabase.DeleteAsset($"{UISpecGenerator.GeneratedRoot}/Views/Mock_Broken.prefab");
        }

        [Test]
        public void TransparentScroll_StripsBacking_AndRoundTrips()
        {
            Generate();
            GameObject prefab = LoadPrefab("Views/Mock_Card.prefab");

            ScrollRect scroll = prefab.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNull(scroll.GetComponent<NeoShape>(), "background 'none' strips the card backing");
            Assert.IsNull(scroll.GetComponent<ThemeColorTarget>());
            Assert.AreEqual(12f, scroll.content.GetComponent<VerticalLayoutGroup>().spacing,
                "scroll spacing drives the content layout");

            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec grid = exported.views.First(v => v.id == "Mock/Card").elements.First(e => e.kind == "grid");
            ElementSpec list = grid.children.First(e => e.kind == "list");
            Assert.AreEqual("none", list.background, "the stripped backing exports as the 'none' sentinel");
            Assert.AreEqual(12f, list.spacing);
        }

        [Test]
        public void ExplicitSizes_AreRigid_MinEqualsPreferred()
        {
            Generate();
            GameObject prefab = LoadPrefab("Popups/PackDetails.prefab");
            var popup = prefab.GetComponent<UIPopup>();
            var layoutElement = popup.images[0].GetComponent<LayoutElement>();
            Assert.AreEqual(layoutElement.preferredHeight, layoutElement.minHeight,
                "authored sizes must not squeeze to nothing when a page overflows");
            Assert.AreEqual(layoutElement.preferredWidth, layoutElement.minWidth);
        }

        // ------------------------------------------------------------------ rich popups

        [Test]
        public void RichPopup_BuildsElements_CloseButton_AndSlots()
        {
            Generate();
            GameObject prefab = LoadPrefab("Popups/PackDetails.prefab");
            var popup = prefab.GetComponent<UIPopup>();

            Assert.AreEqual(new Vector2(720f, 480f), popup.content.sizeDelta, "spec size drives the card");

            Transform close = popup.content.Find(UIWidgetFactory.CloseName);
            Assert.IsNotNull(close, "close: true adds the X button on the card corner");
            Assert.IsNotNull(close.GetComponent<HideContainerOnClick>());

            Transform content = popup.content.Find(UIWidgetFactory.ContentName);
            Assert.IsNotNull(content.Find("Title"), "title still renders in rich popups");
            Assert.IsNull(content.Find("Buttons"), "rich popups skip the canonical OK row");

            Assert.AreEqual(1, popup.images.Count, "image elements register as indexed slots");
            Assert.IsInstanceOf<NeoShape>(popup.images[0]);
            Assert.AreEqual(1, popup.buttons.Count, "buttons register as indexed slots");
            Assert.AreEqual("Popup/GetPack", popup.buttons[0].id.ToString());
            Assert.IsNotNull(popup.buttons[0].GetComponent<HideContainerOnClick>(),
                "onClick.close wires the CTA to dismiss the popup");
        }

        [Test]
        public void LegacyPopup_KeepsCanonicalForm()
        {
            Generate();
            GameObject prefab = LoadPrefab("Popups/Confirm.prefab");
            var popup = prefab.GetComponent<UIPopup>();

            Assert.AreEqual(UIWidgetFactory.PopupDefaultCardSize, popup.content.sizeDelta);
            Assert.IsNull(popup.content.Find(UIWidgetFactory.CloseName));
            Transform content = popup.content.Find(UIWidgetFactory.ContentName);
            Assert.IsNotNull(content.Find("Buttons"), "plain popups keep the OK row");
        }

        // ------------------------------------------------------------------ export

        [Test]
        public void Export_RoundTrips_AllNewFields()
        {
            Generate();
            UISpec exported = UISpecExporter.ExportProject();

            ViewSpec view = exported.views.First(v => v.id == "Mock/Card");
            ElementSpec grid = view.elements.First(e => e.kind == "grid");
            ElementSpec overlay = grid.children.First(e => e.kind == "overlay");
            Assert.AreEqual("Surface", overlay.background);
            Assert.AreEqual(20f, overlay.radius);

            ElementSpec art = overlay.children.First(e => e.kind == "image");
            Assert.AreEqual(SpritePath, art.src);
            Assert.AreEqual(20f, art.radius);
            Assert.AreEqual("Stretch", art.anchor);

            ElementSpec badge = overlay.children.First(e => e.kind == "shape");
            Assert.AreEqual("TopRight", badge.anchor);
            Assert.AreEqual(new[] { -10f, -10f }, badge.position);

            PopupSpec rich = exported.popups.First(p => p.name == "PackDetails");
            Assert.AreEqual("CUPID", rich.title);
            Assert.IsTrue(rich.close);
            Assert.AreEqual(new[] { 720f, 480f }, rich.size);
            Assert.AreEqual(3, rich.elements.Count, "popup content exports as elements");
            Assert.AreEqual(SpritePath, rich.elements.First(e => e.kind == "image").src);
            Assert.IsTrue(rich.elements.First(e => e.kind == "button").onClickClose);

            PopupSpec legacy = exported.popups.First(p => p.name == "Confirm");
            Assert.AreEqual("BENCH", legacy.title);
            Assert.AreEqual("Buy for 15?", legacy.message);
            Assert.IsEmpty(legacy.elements, "plain popups keep the three-field form");
            Assert.IsFalse(legacy.close);
            Assert.IsNull(legacy.size);
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_WithCompositionFeatures()
        {
            Generate();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "overlays/image src/rich popups must round-trip byte-identically");
        }
    }
}
