using AlterEyes.UI;
using NUnit.Framework;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    public class IdAndThemeTests
    {
        [Test]
        public void CategoryNameId_HasValueEquality()
        {
            var a = new ViewId("Menu", "Main");
            var b = new ViewId("Menu", "Main");
            var c = new ViewId("Menu", "Settings");

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, c);
            Assert.IsTrue(a.Matches("Menu", "Main"));
            Assert.AreEqual("Menu/Main", a.ToString());
        }

        [Test]
        public void CategoryNameId_DefaultsAndTrimming()
        {
            var id = new ButtonId("  Action ", " Play ");
            Assert.AreEqual("Action", id.Category);
            Assert.AreEqual("Play", id.Name);

            var empty = new ButtonId();
            Assert.IsTrue(empty.isDefault);
            Assert.AreEqual(CategoryNameId.DefaultCategory, empty.Category);
        }

        [Test]
        public void CategoryNameId_ParsesSlashStrings()
        {
            CategoryNameId.Parse("Menu/Main", out string category, out string name);
            Assert.AreEqual("Menu", category);
            Assert.AreEqual("Main", name);

            CategoryNameId.Parse("JustAName", out category, out name);
            Assert.AreEqual(CategoryNameId.DefaultCategory, category);
            Assert.AreEqual("JustAName", name);
        }

        [Test]
        public void IdDatabase_AddRemoveContains()
        {
            var database = ScriptableObject.CreateInstance<ViewIdDatabase>();
            try
            {
                Assert.IsTrue(database.Add("Menu", "Main"));
                Assert.IsFalse(database.Add("Menu", "Main"), "duplicate add should return false");
                Assert.IsTrue(database.Contains("Menu", "Main"));
                Assert.IsTrue(database.Add("Menu", "Settings"));
                CollectionAssert.AreEqual(new[] { "Main", "Settings" }, database.GetNames("Menu"));

                Assert.IsTrue(database.Remove("Menu", "Main"));
                Assert.IsFalse(database.Contains("Menu", "Main"));
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Theme_TokensAndVariants()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetToken("Primary", Color.red);
                Assert.IsTrue(theme.TryGetColor("Primary", out Color color));
                Assert.AreEqual(Color.red, color);
                Assert.IsTrue(theme.HasToken("Primary"));
                Assert.IsFalse(theme.HasToken("Missing"));

                theme.AddVariant("Dark");
                theme.SetToken("Primary", Color.blue, "Dark");

                theme.ActiveVariantName = "Dark";
                Assert.IsTrue(theme.TryGetColor("Primary", out color));
                Assert.AreEqual(Color.blue, color);

                theme.ActiveVariantName = "Default";
                theme.TryGetColor("Primary", out color);
                Assert.AreEqual(Color.red, color);

                Assert.IsTrue(theme.RemoveToken("Primary"));
                Assert.IsFalse(theme.HasToken("Primary"));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void Theme_ChangeNotification_Fires()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                ThemeService.activeTheme = theme;
                Theme changedTheme = null;
                ThemeService.OnThemeChanged += t => changedTheme = t;
                theme.SetToken("Accent", Color.yellow);
                Assert.AreSame(theme, changedTheme);
            }
            finally
            {
                ThemeService.activeTheme = null;
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void ThemeColorRef_ResolvesTokenOrFallback()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetToken("Primary", Color.green);
                var tokenRef = new ThemeColorRef("Primary");
                Assert.AreEqual(Color.green, tokenRef.Resolve(theme));

                var missingRef = new ThemeColorRef("Missing") { color = Color.magenta };
                Assert.AreEqual(Color.magenta, missingRef.Resolve(theme), "missing token should fall back to the inline color");

                var plainRef = new ThemeColorRef(Color.cyan);
                Assert.AreEqual(Color.cyan, plainRef.Resolve(theme));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void ColorUtils_HexRoundTrip()
        {
            Assert.IsTrue(ColorUtils.TryParseHex("#3A86FF", out Color color));
            Assert.AreEqual("#3A86FF", ColorUtils.ToHex(color));
            Assert.IsTrue(ColorUtils.TryParseHex("3A86FF", out _), "leading # should be optional");
            Assert.IsFalse(ColorUtils.TryParseHex("not-a-color", out _));
        }

        [Test]
        public void ColorUtils_HslRoundTrip()
        {
            var original = new Color(0.23f, 0.53f, 1f);
            ColorUtils.RgbToHsl(original, out float h, out float s, out float l);
            Color roundTrip = ColorUtils.HslToRgb(h, s, l);
            Assert.That(roundTrip.r, Is.EqualTo(original.r).Within(0.01f));
            Assert.That(roundTrip.g, Is.EqualTo(original.g).Within(0.01f));
            Assert.That(roundTrip.b, Is.EqualTo(original.b).Within(0.01f));
        }

        [Test]
        public void ColorUtils_LightenDarken()
        {
            var gray = new Color(0.5f, 0.5f, 0.5f);
            Color lighter = ColorUtils.Lighten(gray, 0.2f);
            Color darker = ColorUtils.Darken(gray, 0.2f);
            Assert.Greater(lighter.r, gray.r);
            Assert.Less(darker.r, gray.r);
        }

        [Test]
        public void SelectableColorSet_ReturnsPerStateColors()
        {
            var set = new SelectableColorSet
            {
                normal = new ThemeColorRef(Color.white),
                pressed = new ThemeColorRef(Color.gray)
            };
            Assert.AreEqual(Color.white, set.GetColor(UISelectionState.Normal));
            Assert.AreEqual(Color.gray, set.GetColor(UISelectionState.Pressed));
        }
    }
}
