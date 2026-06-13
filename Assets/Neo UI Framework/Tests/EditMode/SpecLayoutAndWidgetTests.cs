using System.IO;
using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The agent-pillar v2 surface: layout containers, functional widget templates, geometry
    /// overrides, popups, the starter kit and the screenshot harness — plus the exporter
    /// fixed-point guarantee (export → generate → export is byte-identical).
    /// </summary>
    public class SpecLayoutAndWidgetTests
    {
        private const string LayoutSpecJson = @"{
          ""views"": [ { ""id"": ""Spec/Layout"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 12, ""children"": [
              { ""button"": { ""id"": ""Spec/Go"", ""label"": ""Go"" } },
              { ""switch"": { ""id"": ""Spec/Sound"" } },
              { ""slider"": { ""id"": ""Spec/Volume"", ""min"": 0, ""max"": 10, ""value"": 5 } },
              { ""progress"": { ""min"": 0, ""max"": 100 } },
              { ""grid"": { ""columns"": 3, ""cellSize"": [100, 100], ""spacing"": 6, ""children"": [
                { ""shape"": { ""shape"": ""Circle"", ""background"": ""Primary"" } },
                { ""shape"": { ""shape"": ""Pill"" } }
              ] } },
              { ""tabbar"": { ""id"": ""SpecTabs/TabBar"", ""children"": [
                { ""tab"": { ""id"": ""SpecTabs/Home"", ""label"": ""Home"" } },
                { ""tab"": { ""id"": ""SpecTabs/Options"", ""label"": ""Options"" } }
              ] } },
              { ""list"": { ""children"": [ { ""text"": { ""label"": ""Row"", ""color"": ""TextDefault"" } } ] } }
            ] } },
            { ""image"": { ""anchor"": ""TopRight"", ""size"": [200, 60], ""position"": [-20, -20] } }
          ] } ],
          ""popups"": [ { ""name"": ""SpecConfirm"", ""title"": ""Sure?"", ""message"": ""Really quit."" } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.popupDatabase != null && settings.popupDatabase.Remove("SpecConfirm"))
                EditorUtility.SetDirty(settings.popupDatabase);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateLayoutView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(LayoutSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Spec_Layout.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        [Test]
        public void LayoutContainers_GenerateLayoutGroups()
        {
            GameObject prefab = GenerateLayoutView();

            VerticalLayoutGroup stack = prefab.GetComponentsInChildren<VerticalLayoutGroup>(true)
                .FirstOrDefault(g => g.GetComponent<ScrollRect>() == null && g.transform.parent == prefab.transform);
            Assert.IsNotNull(stack, "vstack should become a VerticalLayoutGroup");
            Assert.AreEqual(16, stack.padding.left);
            Assert.AreEqual(12f, stack.spacing);

            var grid = prefab.GetComponentInChildren<GridLayoutGroup>(true);
            Assert.IsNotNull(grid);
            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
            Assert.AreEqual(3, grid.constraintCount);
            Assert.AreEqual(new Vector2(100f, 100f), grid.cellSize);
            Assert.AreEqual(2, grid.GetComponentsInChildren<NeoShape>(true).Length, "two shapes in the grid");

            var scroll = prefab.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNotNull(scroll, "list should become a ScrollRect");
            Assert.IsNotNull(scroll.content, "list content must be wired");
            Assert.IsNotNull(scroll.viewport, "list viewport must be wired");
        }

        [Test]
        public void Widgets_GenerateFunctionalHierarchies()
        {
            GameObject prefab = GenerateLayoutView();

            UISlider slider = prefab.GetComponentsInChildren<UISlider>(true).FirstOrDefault();
            Assert.IsNotNull(slider);
            Assert.AreEqual(0f, slider.minValue);
            Assert.AreEqual(10f, slider.maxValue);
            Assert.AreEqual(5f, slider.value);
            Assert.IsNotNull(slider.fillRect, "slider fill must be wired");
            Assert.IsNotNull(slider.handleRect, "slider handle must be wired");

            UIToggle switchToggle = prefab.GetComponentsInChildren<UIToggle>(true)
                .FirstOrDefault(t => t.id.Matches("Spec", "Sound"));
            Assert.IsNotNull(switchToggle);
            Assert.IsNotNull(switchToggle.transform.Find(UIWidgetFactory.KnobName), "switch needs a knob");

            var progressor = prefab.GetComponentInChildren<Progressor>(true);
            Assert.IsNotNull(progressor);
            Assert.AreEqual(100f, progressor.toValue);
            Assert.AreEqual(1, progressor.progressTargets.Count, "progress fill target must be registered");

            var group = prefab.GetComponentInChildren<UIToggleGroup>(true);
            Assert.IsNotNull(group, "tabbar needs a toggle group");
            Assert.AreEqual(2, group.GetComponentsInChildren<UITab>(true).Length);

            UIButton button = prefab.GetComponentsInChildren<UIButton>(true)
                .FirstOrDefault(b => b.id.Matches("Spec", "Go"));
            Assert.IsNotNull(button);
            Assert.IsNotNull(button.GetComponent<NeoShape>(), "buttons are NeoShape-based");
            Assert.IsNotNull(button.GetComponent<UISelectableColorAnimator>(), "buttons animate state colors");
        }

        [Test]
        public void AnchorSizePosition_Overrides_Apply()
        {
            GameObject prefab = GenerateLayoutView();
            RectTransform image = prefab.GetComponentsInChildren<Image>(true)
                .Select(i => (RectTransform)i.transform)
                .FirstOrDefault(r => r.name.StartsWith("image"));
            Assert.IsNotNull(image, "overridden image element missing");
            Assert.AreEqual(Vector2.one, image.anchorMin);
            Assert.AreEqual(Vector2.one, image.anchorMax);
            Assert.AreEqual(new Vector2(200f, 60f), image.sizeDelta);
            Assert.AreEqual(new Vector2(-20f, -20f), image.anchoredPosition);
        }

        [Test]
        public void Popups_Generate_AndRegister()
        {
            GenerateLayoutView();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Popups/SpecConfirm.prefab");
            Assert.IsNotNull(prefab, "popup prefab missing");
            UIPopup popup = prefab.GetComponent<UIPopup>();
            Assert.IsNotNull(popup);
            Assert.AreEqual("SpecConfirm", popup.popupName);
            Assert.AreEqual(2, popup.labels.Count);
            Assert.AreEqual("Sure?", popup.labels[0].text);
            Assert.IsNotNull(prefab.GetComponentInChildren<HideContainerOnClick>(true), "popup needs a close button");
            Assert.AreEqual(prefab, NeoUISettings.instance.popupDatabase.GetPrefab("SpecConfirm"),
                "popup must be registered in the database");
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint()
        {
            GenerateLayoutView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "export → generate → export must be stable, or agents can't safely round-trip hand-tweaked UI");
        }

        [Test]
        public void ExportedLayout_KeepsStructureAndOverrides()
        {
            GenerateLayoutView();
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Spec/Layout");
            Assert.IsNotNull(view);

            ElementSpec stack = view.elements.FirstOrDefault(e => e.kind == "vstack");
            Assert.IsNotNull(stack, "vstack must export as vstack");
            Assert.AreEqual(16f, stack.padding);
            Assert.AreEqual(12f, stack.spacing);
            CollectionAssert.AreEqual(
                new[] { "button", "switch", "slider", "progress", "grid", "tabbar", "list" },
                stack.children.Select(c => c.kind).ToArray(),
                "container children must round-trip in order");

            ElementSpec slider = stack.children.First(c => c.kind == "slider");
            Assert.AreEqual(10f, slider.max);
            Assert.AreEqual(5f, slider.value);

            ElementSpec image = view.elements.FirstOrDefault(e => e.kind == "image");
            Assert.IsNotNull(image);
            Assert.AreEqual("TopRight", image.anchor);
            Assert.AreEqual(new[] { 200f, 60f }, image.size);
            Assert.AreEqual(new[] { -20f, -20f }, image.position);

            Assert.IsTrue(exported.popups.Any(p => p.name == "SpecConfirm" && p.title == "Sure?"));
        }

        [Test]
        public void StarterKit_CreatesThemedPrefabLibrary()
        {
            // intentionally not cleaned up: the starter kit IS package content and is idempotent
            GenerateReport report = StarterKitBootstrap.CreateOrRepair();
            Assert.IsEmpty(report.collisions, report.ToString());
            Assert.IsEmpty(report.issues, report.ToString());

            Theme theme = NeoUISettings.instance.theme;
            Assert.IsNotNull(theme.GetVariant(StarterKitBootstrap.DarkVariant));
            Assert.IsNotNull(theme.GetVariant(StarterKitBootstrap.LightVariant));
            Assert.IsTrue(theme.HasToken(UIWidgetFactory.TokenSurfaceElevated));
            Assert.IsTrue(theme.TryGetShapeStyle(UIWidgetFactory.StyleControl, out ShapeStyle control));
            Assert.AreEqual(1f, control.borderWidth);

            foreach (string name in new[]
                { "Button", "Toggle", "Switch", "Slider", "ProgressBar", "Card", "TabBar", "ListView", "Tooltip", "Popup", "Showcase" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{StarterKitBootstrap.StarterFolder}/{name}.prefab");
                Assert.IsNotNull(prefab, $"starter prefab '{name}' missing");
                Assert.IsNotNull(prefab.GetComponent<GeneratedMarker>(), $"'{name}' must carry the generated marker");
            }

            var card = AssetDatabase.LoadAssetAtPath<GameObject>($"{StarterKitBootstrap.StarterFolder}/Card.prefab");
            Assert.IsTrue(card.GetComponentsInChildren<NeoShape>(true).Length >= 2, "card has shadow + surface shapes");
            Assert.AreEqual(0, card.GetComponentsInChildren<Image>(true)
                    .Count(i => i.sprite != null), "starter kit ships zero sprites");
        }

        [Test]
        public void Screenshotter_WritesPng()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics run) — the screenshotter needs a GPU");

            GameObject root = UIWidgetFactory.CreateButton(null, "Shot", "Test", "Screenshot Me");
            string folder = $"{UISpecGenerator.GeneratedRoot}/Views";
            if (!AssetDatabase.IsValidFolder(UISpecGenerator.GeneratedRoot))
                AssetDatabase.CreateFolder("Assets", "Neo UI Generated");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(UISpecGenerator.GeneratedRoot, "Views");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/ScreenshotTest.prefab");
            Object.DestroyImmediate(root);

            string path = UIScreenshotter.Capture(prefab, $"Temp/neo-screenshots/test-button.png", 480, 320);

            Assert.IsTrue(File.Exists(path), "screenshot PNG must be written");
            Assert.Greater(new FileInfo(path).Length, 500, "PNG should contain an actual rendered image");
        }
    }
}
