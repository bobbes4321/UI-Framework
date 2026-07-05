using System.Collections.Generic;
using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 2.6 (design-system-cohesion-plan.md) — root-cause fix for "button variants only
    /// partially editable": primary/secondary/ghost/danger used to live ONLY inside
    /// <c>UIWidgetFactory.VariantColors</c>'s fallback switch, so a fresh project's
    /// <see cref="NeoUISettings.buttonVariants"/> list was empty and the Design System window's
    /// Buttons tab showed "No custom variants". <see cref="StarterKitBootstrap.EnsureButtonVariants"/>
    /// seeds the four legacy variants plus a new <c>success</c> variant into that list as first-class,
    /// editable, token-bound data. Exercises the bootstrap method directly against an in-memory
    /// settings instance — no AssetDatabase writes needed, mirrors <c>WidgetAttributeRegistryTests</c>'
    /// direct-construction style for <see cref="ButtonVariantAsset"/>.
    /// </summary>
    public class StarterKitButtonVariantSeedingTests
    {
        private NeoUISettings _settings;

        [SetUp]
        public void SetUp() => _settings = ScriptableObject.CreateInstance<NeoUISettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        [Test]
        public void FreshSettings_SeedsAllFiveBuiltInVariants_TokenBound()
        {
            Assert.IsTrue(_settings.buttonVariants == null || _settings.buttonVariants.Count == 0,
                "precondition: a fresh settings instance has no variants yet");

            StarterKitBootstrap.EnsureButtonVariants(_settings, new GenerateReport());

            CollectionAssert.AreEquivalent(
                new[] { "primary", "secondary", "ghost", "danger", "success" },
                _settings.buttonVariants.Select(v => v.name),
                "a fresh project must see exactly the five canonical, editable variants");

            foreach (ButtonVariantAsset v in _settings.buttonVariants)
            {
                Assert.IsFalse(string.IsNullOrEmpty(v.contentToken), $"'{v.name}' needs a content token");
                // every variant token-binds its rest color EXCEPT ghost, whose factory-switch rest
                // state is a raw fully-transparent white with no matching surface token to bind to
                // (documented in StarterKitBootstrap.BuiltInButtonVariants) — that is the one
                // intentional exception, not a seeding gap.
                if (v.name != "ghost")
                    Assert.IsTrue(v.colors.normal.useToken, $"'{v.name}' normal color should be token-bound");
            }
        }

        [Test]
        public void SuccessVariant_ResolvesThroughTryGetVariantColors()
        {
            StarterKitBootstrap.EnsureButtonVariants(_settings, new GenerateReport());

            Assert.IsTrue(_settings.TryGetVariantColors("success", out SelectableColorSet colors, out string contentToken),
                "success must resolve from seeded data, not the built-in switch (which has no success case)");
            Assert.AreEqual(UIWidgetFactory.TokenTextOnPrimary, contentToken);
            Assert.AreEqual(UIWidgetFactory.TokenSuccess, colors.normal.token);
            Assert.AreEqual(UIWidgetFactory.TokenSuccessHover, colors.highlighted.token);
            Assert.AreEqual(UIWidgetFactory.TokenSuccessPressed, colors.pressed.token);
            Assert.AreEqual(UIWidgetFactory.TokenSuccessHover, colors.selected.token);
            Assert.AreEqual(UIWidgetFactory.TokenOutline, colors.disabled.token);

            // case-insensitive, matching TryGetVariantColors' documented contract
            Assert.IsTrue(_settings.TryGetVariantColors("SUCCESS", out _, out _));
        }

        [Test]
        public void Repair_LeavesCustomOrRenamedVariant_Untouched_OnlyAddsMissingBuiltIns()
        {
            _settings.buttonVariants = new List<ButtonVariantAsset>
            {
                new ButtonVariantAsset
                {
                    // a project-authored custom variant, unrelated to the built-in five
                    name = "Important",
                    contentToken = "CustomToken",
                    colors = new SelectableColorSet { normal = new ThemeColorRef(Color.magenta) }
                },
                new ButtonVariantAsset
                {
                    // an existing built-in the project already hand-edited (different case, on
                    // purpose — TryGetVariantColors/the seed both match case-insensitively)
                    name = "PRIMARY",
                    contentToken = "HandEditedToken",
                    colors = new SelectableColorSet { normal = new ThemeColorRef(new Color(1f, 0f, 1f)) }
                }
            };

            StarterKitBootstrap.EnsureButtonVariants(_settings, new GenerateReport());

            ButtonVariantAsset important = _settings.buttonVariants.First(v => v.name == "Important");
            Assert.AreEqual("CustomToken", important.contentToken, "a custom variant must survive repair untouched");

            List<ButtonVariantAsset> primaryMatches = _settings.buttonVariants
                .Where(v => string.Equals(v.name, "primary", System.StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.AreEqual(1, primaryMatches.Count,
                "repair must not add a second 'primary' row alongside the hand-edited 'PRIMARY'");
            Assert.AreEqual("HandEditedToken", primaryMatches[0].contentToken,
                "an existing entry (even case-renamed) must never be overwritten by the seed");

            // the three still-missing built-ins (secondary/ghost/danger) plus success got added
            CollectionAssert.AreEquivalent(
                new[] { "Important", "PRIMARY", "secondary", "ghost", "danger", "success" },
                _settings.buttonVariants.Select(v => v.name));
        }

        [Test]
        public void Repair_IsIdempotent()
        {
            var report1 = new GenerateReport();
            StarterKitBootstrap.EnsureButtonVariants(_settings, report1);
            Assert.AreEqual(5, report1.created.Count, "first run creates all five");

            var report2 = new GenerateReport();
            StarterKitBootstrap.EnsureButtonVariants(_settings, report2);
            Assert.AreEqual(0, report2.created.Count, "a second run creates nothing new");
            Assert.AreEqual(5, _settings.buttonVariants.Count, "no duplicates");
        }
    }
}
