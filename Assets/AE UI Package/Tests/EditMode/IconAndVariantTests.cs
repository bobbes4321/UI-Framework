using System.Linq;
using AlterEyes.UI;
using AlterEyes.UI.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// Beautification P3 (Lucide icons: IconMap, CreateIcon, button/tab icon slots) and
    /// P2 (button variants/sizes + the WidgetStyleTag round-trip) — including the polymorphic
    /// spec "size" key (string variant vs [w,h] array) and the fixed-point guarantee.
    /// </summary>
    public class IconAndVariantTests
    {
        private const string DecoSpecJson = @"{
          ""views"": [ { ""id"": ""Deco/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""button"": { ""id"": ""Deco/Play"", ""label"": ""Play"", ""icon"": ""play"", ""size"": ""lg"" } },
              { ""button"": { ""id"": ""Deco/Quit"", ""label"": ""Quit"", ""variant"": ""danger"", ""size"": ""sm"" } },
              { ""button"": { ""id"": ""Deco/Skip"", ""label"": ""Skip"", ""variant"": ""ghost"" } },
              { ""button"": { ""id"": ""Deco/Settings"", ""icon"": ""settings"" } },
              { ""icon"": { ""name"": ""trophy"", ""size"": 48, ""color"": ""Warning"" } },
              { ""tabbar"": { ""id"": ""DecoTabs/TabBar"", ""children"": [
                { ""tab"": { ""id"": ""DecoTabs/Home"", ""label"": ""Home"", ""icon"": ""home"" } },
                { ""tab"": { ""id"": ""DecoTabs/Shop"", ""label"": ""Shop"", ""icon"": ""shopping-bag"" } }
              ] } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateDecoView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(DecoSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Deco_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        private static GameObject Button(GameObject prefab, string name) =>
            prefab.GetComponentsInChildren<UIButton>(true)
                .First(b => b.id.Matches("Deco", name)).gameObject;

        [Test]
        public void IconMap_ResolvesNamesAliasesAndReverse()
        {
            Assert.GreaterOrEqual(IconMap.Count, 100, "curated subset should stay roughly 100+ icons");

            Assert.IsTrue(IconMap.TryGetGlyph("play", out char play));
            Assert.IsTrue(IconMap.TryGetName(play, out string playName));
            Assert.AreEqual("play", playName, "glyph → name must reverse the map");

            Assert.IsTrue(IconMap.TryGetGlyph("home", out char house), "aliases must resolve");
            Assert.IsTrue(IconMap.TryGetGlyph("house", out char canonical));
            Assert.AreEqual(canonical, house, "'home' aliases the canonical 'house'");

            Assert.IsFalse(IconMap.TryGetGlyph("definitely-not-an-icon", out _));
            Assert.AreEqual(IconMap.Count, IconMap.AllGlyphs().Length);
        }

        [Test]
        public void Buttons_GetVariantsSizesAndIcons()
        {
            GameObject prefab = GenerateDecoView();

            GameObject play = Button(prefab, "Play");
            WidgetStyleTag playTag = play.GetComponent<WidgetStyleTag>();
            Assert.AreEqual(UIWidgetFactory.VariantPrimary, playTag.variant);
            Assert.AreEqual(UIWidgetFactory.SizeLarge, playTag.size);
            Assert.AreEqual(72f, play.GetComponent<LayoutElement>().preferredHeight, "lg = 72px");
            TMP_Text playIcon = play.transform.Find(UIWidgetFactory.IconName)?.GetComponent<TMP_Text>();
            Assert.IsNotNull(playIcon, "icon slot must create an Icon child");
            Assert.IsTrue(IconMap.TryGetGlyph("play", out char playGlyph));
            Assert.AreEqual(playGlyph.ToString(), playIcon.text);
            Assert.AreEqual(UIWidgetFactory.TextStyleButtonLabelLarge,
                play.transform.Find(UIWidgetFactory.LabelName).GetComponent<ThemeTextStyleTarget>()?.style,
                "lg buttons use the large label style");

            GameObject quit = Button(prefab, "Quit");
            Assert.AreEqual(UIWidgetFactory.VariantDanger, quit.GetComponent<WidgetStyleTag>().variant);
            Assert.AreEqual(40f, quit.GetComponent<LayoutElement>().preferredHeight, "sm = 40px");
            SelectableColorSet quitColors = quit.GetComponent<UISelectableColorAnimator>().colors;
            Assert.AreEqual(UIWidgetFactory.TokenDanger, quitColors.normal.token);
            Assert.AreEqual(UIWidgetFactory.TokenDangerHover, quitColors.highlighted.token);

            GameObject ghost = Button(prefab, "Skip");
            Assert.IsNull(ghost.GetComponent<ThemeShapeStyleTarget>(), "ghost buttons have no bordered surface style");
            SelectableColorSet ghostColors = ghost.GetComponent<UISelectableColorAnimator>().colors;
            Assert.IsFalse(ghostColors.normal.useToken);
            Assert.AreEqual(0f, ghostColors.normal.color.a, "ghost resting fill is transparent");
            Assert.AreEqual(UIWidgetFactory.TokenPrimary,
                ghost.transform.Find(UIWidgetFactory.LabelName).GetComponent<ThemeColorTarget>()?.token,
                "ghost labels use the Primary token");

            GameObject iconOnly = Button(prefab, "Settings");
            Assert.IsNull(iconOnly.transform.Find(UIWidgetFactory.LabelName), "icon-only buttons skip the label");
            Assert.IsNotNull(iconOnly.transform.Find(UIWidgetFactory.IconName));
            Assert.AreEqual(56f, iconOnly.GetComponent<LayoutElement>().preferredWidth, "icon-only buttons are square");
        }

        [Test]
        public void StandaloneIcon_And_TabIcons_Generate()
        {
            GameObject prefab = GenerateDecoView();
            AEUISettings settings = AEUISettings.instance;
            Assert.IsNotNull(settings.iconFont, "generation must register the icon font on the settings asset");

            TMP_Text trophy = prefab.GetComponentsInChildren<TMP_Text>(true)
                .First(t => t.font == settings.iconFont && t.transform.parent.GetComponent<UIButton>() == null
                            && t.transform.parent.GetComponent<UITab>() == null);
            Assert.IsTrue(IconMap.TryGetGlyph("trophy", out char trophyGlyph));
            Assert.AreEqual(trophyGlyph.ToString(), trophy.text);
            Assert.AreEqual(48f, trophy.fontSize, "scalar spec size drives the glyph size");
            Assert.AreEqual("Warning", trophy.GetComponent<ThemeColorTarget>()?.token);

            UITab home = prefab.GetComponentsInChildren<UITab>(true).First(t => t.id.Matches("DecoTabs", "Home"));
            TMP_Text homeIcon = home.transform.Find(UIWidgetFactory.IconName)?.GetComponent<TMP_Text>();
            Assert.IsNotNull(homeIcon, "tabs with icons get an Icon child");
            Assert.IsTrue(IconMap.TryGetGlyph("house", out char houseGlyph));
            Assert.AreEqual(houseGlyph.ToString(), homeIcon.text, "'home' alias resolves to the house glyph");
        }

        [Test]
        public void Export_VariantsSizesAndIcons_RoundTrip()
        {
            GenerateDecoView();
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Deco/Screen");
            Assert.IsNotNull(view);
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");

            ElementSpec play = stack.children.First(e => e.id == "Deco/Play");
            Assert.AreEqual("play", play.icon);
            Assert.AreEqual("lg", play.sizeVariant, "string size variant must round-trip");
            Assert.IsNull(play.size, "the size variant owns the size key — no [w,h] next to it");
            Assert.IsNull(play.variant, "default variant stays implicit");

            ElementSpec quit = stack.children.First(e => e.id == "Deco/Quit");
            Assert.AreEqual("danger", quit.variant);
            Assert.AreEqual("sm", quit.sizeVariant);

            ElementSpec ghost = stack.children.First(e => e.id == "Deco/Skip");
            Assert.AreEqual("ghost", ghost.variant);

            ElementSpec icon = stack.children.First(e => e.kind == "icon");
            Assert.AreEqual("trophy", icon.icon);
            Assert.AreEqual("Warning", icon.labelColor);
            Assert.AreEqual(new[] { 48f, 48f }, icon.size, "scalar parse size exports as [w,h]");

            ElementSpec tabbar = stack.children.First(e => e.kind == "tabbar");
            Assert.AreEqual("house", tabbar.children[0].icon, "aliases export as canonical names");
            Assert.AreEqual("shopping-bag", tabbar.children[1].icon);
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_WithIconsAndVariants()
        {
            GenerateDecoView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "icons/variants/sizes must round-trip byte-identically through export → generate → export");
        }

        [Test]
        public void ColorUtils_DerivesStateColors()
        {
            var baseColor = new Color(0.3f, 0.6f, 0.9f);
            Color hover = ColorUtils.DeriveHover(baseColor);
            Color pressed = ColorUtils.DerivePressed(baseColor);

            ColorUtils.RgbToHsl(baseColor, out _, out _, out float baseL);
            ColorUtils.RgbToHsl(hover, out _, out _, out float hoverL);
            ColorUtils.RgbToHsl(pressed, out _, out _, out float pressedL);

            Assert.Greater(hoverL, baseL, "hover lifts lightness");
            Assert.Less(pressedL, baseL, "pressed drops lightness");

            Theme theme = AEUISettings.instance.theme;
            StarterKitBootstrap.EnsureFactoryTokens(theme);
            Assert.IsTrue(theme.HasToken(UIWidgetFactory.TokenDanger));
            Assert.IsTrue(theme.HasToken(UIWidgetFactory.TokenDangerHover));
            Assert.IsTrue(theme.HasToken(UIWidgetFactory.TokenSuccessPressed));
        }
    }
}
