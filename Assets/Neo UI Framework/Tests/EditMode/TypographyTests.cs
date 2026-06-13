using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The typography pillar (beautification P1): TextStyle on the theme, the
    /// ThemeTextStyleTarget binder, the curated starter type scale (Inter) and the spec's
    /// "textStyle" field — including the export rule that a style owns the size (no raw
    /// fontSize next to a textStyle) and the fixed-point guarantee.
    /// </summary>
    public class TypographyTests
    {
        private const string TypographySpecJson = @"{
          ""views"": [ { ""id"": ""Typo/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 24, ""spacing"": 12, ""children"": [
              { ""text"": { ""label"": ""Settings"", ""textStyle"": ""Title"" } },
              { ""text"": { ""label"": ""Raw sized"", ""fontSize"": 27, ""color"": ""TextMuted"" } },
              { ""button"": { ""id"": ""Typo/Apply"", ""label"": ""Apply"" } },
              { ""toggle"": { ""id"": ""Typo/Music"", ""label"": ""Music"", ""textStyle"": ""Caption"" } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateTypographyView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(TypographySpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Typo_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        [Test]
        public void Theme_TextStyleApi_MirrorsShapeStyles()
        {
            Theme theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                Assert.IsFalse(theme.TryGetTextStyle("Body", out _));

                theme.SetTextStyle(new TextStyle { name = "Body", size = 24f });
                theme.SetTextStyle(new TextStyle { name = "Title", size = 44f });
                CollectionAssert.AreEquivalent(new[] { "Body", "Title" }, theme.GetTextStyleNames().ToArray());

                theme.SetTextStyle(new TextStyle { name = "Body", size = 26f });
                Assert.IsTrue(theme.TryGetTextStyle("Body", out TextStyle body));
                Assert.AreEqual(26f, body.size, "SetTextStyle must replace by name, not duplicate");
                Assert.AreEqual(2, theme.TextStyles.Count);

                Assert.IsTrue(theme.RemoveTextStyle("Title"));
                Assert.IsFalse(theme.TryGetTextStyle("Title", out _));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TextStyleTarget_AppliesAndFollowsThemeChanges()
        {
            Theme theme = ScriptableObject.CreateInstance<Theme>();
            var go = new GameObject("StyledText", typeof(RectTransform));
            try
            {
                theme.SetToken("TextDefault", Color.red);
                theme.SetTextStyle(new TextStyle
                {
                    name = "Body", size = 30f, characterSpacing = 2f,
                    color = new ThemeColorRef("TextDefault")
                });

                var text = go.AddComponent<TextMeshProUGUI>();
                var target = go.AddComponent<ThemeTextStyleTarget>();
                target.themeOverride = theme;
                target.style = "Body";
                target.ApplyStyle();

                Assert.AreEqual(30f, text.fontSize);
                Assert.AreEqual(2f, text.characterSpacing);
                Assert.AreEqual(Color.red, text.color, "applyColor default pulls the style's token color");

                theme.GetTextStyle("Body").size = 36f;
                theme.RaiseChanged();
                Assert.AreEqual(36f, text.fontSize, "bound texts must follow live theme edits");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void StarterKit_CreatesTypeScale_AndFontAssets()
        {
            GenerateReport report = StarterKitBootstrap.CreateOrRepair();
            Assert.IsEmpty(report.collisions, report.ToString());
            Assert.IsEmpty(report.issues, report.ToString());

            Theme theme = NeoUISettings.instance.theme;
            foreach ((string style, float size) in new[]
                     {
                         (UIWidgetFactory.TextStyleDisplay, 72f), (UIWidgetFactory.TextStyleTitle, 44f),
                         (UIWidgetFactory.TextStyleHeading, 30f), (UIWidgetFactory.TextStyleBody, 24f),
                         (UIWidgetFactory.TextStyleCaption, 18f), (UIWidgetFactory.TextStyleButtonLabel, 24f)
                     })
            {
                Assert.IsTrue(theme.TryGetTextStyle(style, out TextStyle textStyle), $"starter style '{style}' missing");
                Assert.AreEqual(size, textStyle.size, $"'{style}' size off the curated scale");
                Assert.IsNotNull(textStyle.font, $"'{style}' must reference a committed Inter font asset");
            }

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetBootstrap.InterRegularAssetPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetBootstrap.InterSemiBoldAssetPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetBootstrap.InterBoldAssetPath));
        }

        [Test]
        public void Generate_TextStyle_BindsAndBakes()
        {
            GameObject prefab = GenerateTypographyView();
            Theme theme = NeoUISettings.instance.theme;
            Assert.IsTrue(theme.TryGetTextStyle(UIWidgetFactory.TextStyleTitle, out TextStyle title));

            TMP_Text styled = prefab.GetComponentsInChildren<TMP_Text>(true)
                .First(t => t.text == "Settings");
            var binder = styled.GetComponent<ThemeTextStyleTarget>();
            Assert.IsNotNull(binder, "styled text must carry the ThemeTextStyleTarget binder");
            Assert.AreEqual(UIWidgetFactory.TextStyleTitle, binder.style);
            Assert.AreEqual(title.size, styled.fontSize, "WYSIWYG: the style must be baked into the prefab");
            Assert.AreEqual(title.font, styled.font, "WYSIWYG: the Inter font must be baked into the prefab");

            TMP_Text raw = prefab.GetComponentsInChildren<TMP_Text>(true)
                .First(t => t.text == "Raw sized");
            Assert.IsNull(raw.GetComponent<ThemeTextStyleTarget>(), "styleless text stays raw");
            Assert.AreEqual(27f, raw.fontSize);

            TMP_Text buttonLabel = prefab.GetComponentsInChildren<UIButton>(true)
                .First(b => b.id.Matches("Typo", "Apply"))
                .GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual(UIWidgetFactory.TextStyleButtonLabel,
                buttonLabel.GetComponent<ThemeTextStyleTarget>()?.style,
                "factory buttons default to the ButtonLabel style");

            TMP_Text toggleLabel = prefab.GetComponentsInChildren<UIToggle>(true)
                .First(t => t.id.Matches("Typo", "Music"))
                .GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual(UIWidgetFactory.TextStyleCaption,
                toggleLabel.GetComponent<ThemeTextStyleTarget>()?.style,
                "spec textStyle must override the toggle label default");
        }

        [Test]
        public void Export_TextStyle_OwnsTheSize()
        {
            GenerateTypographyView();
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Typo/Screen");
            Assert.IsNotNull(view);
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");

            ElementSpec styledText = stack.children.First(e => e.kind == "text" && e.label == "Settings");
            Assert.AreEqual("Title", styledText.textStyle);
            Assert.IsNull(styledText.fontSize, "a textStyle owns the size — raw fontSize must not be exported");

            ElementSpec rawText = stack.children.First(e => e.kind == "text" && e.label == "Raw sized");
            Assert.IsNull(rawText.textStyle);
            Assert.AreEqual(27f, rawText.fontSize, "styleless text keeps its raw fontSize");

            ElementSpec button = stack.children.First(e => e.kind == "button");
            Assert.AreEqual(UIWidgetFactory.TextStyleButtonLabel, button.textStyle);

            ElementSpec toggle = stack.children.First(e => e.kind == "toggle");
            Assert.AreEqual("Caption", toggle.textStyle);
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_WithTextStyles()
        {
            GenerateTypographyView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "textStyle must round-trip byte-identically through export → generate → export");
        }
    }
}
