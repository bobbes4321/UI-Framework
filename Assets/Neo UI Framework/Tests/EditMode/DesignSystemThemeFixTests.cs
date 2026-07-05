using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Theme-level behavior behind the Design System window's Phase-0 bug fixes: per-variant state
    /// derivation (B2), the variant-scoped color getter it relies on, shape-style edits firing the
    /// theme-changed event (B4), and the documented theme-wide token-removal scope (B3).
    /// </summary>
    public class DesignSystemThemeFixTests
    {
        // B2: deriving hover/pressed must read each variant's OWN base and write back into only that
        // variant — Dark's derived states must not clobber Light's (and vice versa).
        [Test]
        public void DerivePair_DerivesPerVariant_KeepingVariantsDistinct()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetToken("Primary", Color.red);           // Default variant
                theme.AddVariant("Dark");
                theme.SetToken("Primary", Color.blue, "Dark");  // Dark's own base differs

                NeoDesignSystemWindow.DerivePair(theme, "Primary", "PrimaryHover", "PrimaryPressed");

                Assert.IsTrue(theme.TryGetColor("PrimaryHover", out Color defHover, "Default"));
                Assert.IsTrue(theme.TryGetColor("PrimaryHover", out Color darkHover, "Dark"));
                Assert.AreEqual(ColorUtils.DeriveHover(Color.red), defHover,
                    "Default hover should derive from Default's own base (red)");
                Assert.AreEqual(ColorUtils.DeriveHover(Color.blue), darkHover,
                    "Dark hover should derive from Dark's own base (blue)");
                Assert.AreNotEqual(defHover, darkHover, "variants must keep distinct derived states");

                Assert.IsTrue(theme.TryGetColor("PrimaryPressed", out Color defPressed, "Default"));
                Assert.IsTrue(theme.TryGetColor("PrimaryPressed", out Color darkPressed, "Dark"));
                Assert.AreEqual(ColorUtils.DerivePressed(Color.red), defPressed);
                Assert.AreEqual(ColorUtils.DerivePressed(Color.blue), darkPressed);
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        // The variant-scoped getter the derive fix depends on: reads a SPECIFIC variant, not the active one.
        [Test]
        public void TryGetColor_VariantScoped_ReadsSpecifiedVariant()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetToken("Primary", Color.red);
                theme.AddVariant("Dark");
                theme.SetToken("Primary", Color.blue, "Dark");

                Assert.IsTrue(theme.TryGetColor("Primary", out Color light, "Default"));
                Assert.IsTrue(theme.TryGetColor("Primary", out Color dark, "Dark"));
                Assert.AreEqual(Color.red, light);
                Assert.AreEqual(Color.blue, dark);

                Assert.IsFalse(theme.TryGetColor("Primary", out _, "NoSuchVariant"),
                    "unknown variant name should return false");
                Assert.IsFalse(theme.TryGetColor("Missing", out _, "Default"),
                    "unknown token should return false");
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        // B4: routing a shape-style edit through SetShapeStyle must notify ThemeService so live
        // ThemeShapeStyleTargets refresh.
        [Test]
        public void SetShapeStyle_RaisesThemeChanged()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            System.Action<Theme> handler = null;
            try
            {
                ThemeService.activeTheme = theme;
                int fired = 0;
                handler = _ => fired++;
                ThemeService.OnThemeChanged += handler;

                theme.SetShapeStyle(new ShapeStyle { name = "Card", radius = 8f });
                Assert.GreaterOrEqual(fired, 1, "adding a shape style should raise the theme-changed event");

                theme.SetShapeStyle(new ShapeStyle { name = "Card", radius = 16f }); // upsert existing
                Assert.GreaterOrEqual(fired, 2, "editing a shape style should raise the theme-changed event");
            }
            finally
            {
                if (handler != null) ThemeService.OnThemeChanged -= handler;
                ThemeService.activeTheme = null;
                Object.DestroyImmediate(theme);
            }
        }

        // B3: tokens are theme-wide by DESIGN — RemoveToken removes from every variant. The window's
        // confirm dialog states this scope; this documents/guards the underlying behavior.
        [Test]
        public void RemoveToken_RemovesAcrossAllVariants()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetToken("Primary", Color.red);
                theme.AddVariant("Dark");
                theme.SetToken("Primary", Color.blue, "Dark");

                Assert.IsTrue(theme.TryGetColor("Primary", out _, "Default"));
                Assert.IsTrue(theme.TryGetColor("Primary", out _, "Dark"));

                Assert.IsTrue(theme.RemoveToken("Primary"));

                Assert.IsFalse(theme.TryGetColor("Primary", out _, "Default"),
                    "token removal is theme-wide — gone from Default");
                Assert.IsFalse(theme.TryGetColor("Primary", out _, "Dark"),
                    "token removal is theme-wide — gone from Dark");
                Assert.IsFalse(theme.HasToken("Primary"));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }
    }
}
