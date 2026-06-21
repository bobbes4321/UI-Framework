using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The fifth UIAnimation channel (color/tint): it stays off by default, counts as an enabled
    /// channel + contributes to duration when on, deep-copies through a preset, and round-trips
    /// through the spec preset's color encoding (start / current / #hex / theme-token name).
    /// </summary>
    public class AnimationColorChannelTests
    {
        [Test]
        public void FreshAnimation_DoesNotAnimateColor()
        {
            var anim = new UIAnimation();
            Assert.IsFalse(anim.color.enabled, "the color channel must default OFF (back-compat)");
            Assert.IsFalse(anim.hasEnabledChannels);
        }

        [Test]
        public void ColorChannel_CountsTowardEnabledChannels_AndDuration()
        {
            var anim = new UIAnimation();
            anim.color.enabled = true;
            anim.color.settings.duration = 0.4f;

            Assert.IsTrue(anim.hasEnabledChannels);
            Assert.AreEqual(0.4f, anim.totalDuration, 1e-4f,
                "totalDuration must account for the color channel");
        }

        [Test]
        public void CopyTo_DeepCopiesColorChannel()
        {
            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            try
            {
                preset.animation.color.enabled = true;
                preset.animation.color.from.reference = ColorReference.StartColor;
                preset.animation.color.to.reference = ColorReference.ThemeToken;
                preset.animation.color.to.themeToken = "Primary";
                preset.animation.color.settings.duration = 0.25f;

                var target = new UIAnimation();
                preset.CopyTo(target);

                Assert.IsTrue(target.color.enabled);
                Assert.AreEqual(ColorReference.StartColor, target.color.from.reference);
                Assert.AreEqual(ColorReference.ThemeToken, target.color.to.reference);
                Assert.AreEqual("Primary", target.color.to.themeToken);
                Assert.AreEqual(0.25f, target.color.settings.duration, 1e-4f);

                // Deep copy: the endpoint objects are distinct instances, not shared references.
                preset.animation.color.to.themeToken = "Danger";
                Assert.AreEqual("Primary", target.color.to.themeToken,
                    "CopyTo must copy endpoint fields, not share the endpoint instance");
            }
            finally { Object.DestroyImmediate(preset); }
        }

        [Test]
        public void PresetSpec_CustomColor_RoundTripsThroughJson()
        {
            var anim = new UIAnimation();
            anim.color.enabled = true;
            anim.color.from.reference = ColorReference.CurrentColor;
            anim.color.to.reference = ColorReference.CustomColor;
            anim.color.to.customColor = (Color)new Color32(0x33, 0x82, 0xF6, 0xFF);
            anim.color.settings.duration = 0.2f;
            anim.color.settings.ease = Ease.OutQuad;

            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            try
            {
                preset.presetName = "TintTest";
                preset.category = "Hover";
                preset.animation = anim;

                PresetSpec spec = UISpecExporter.ExportPreset(preset);
                Assert.IsNotNull(spec.color, "an enabled color channel must export a color spec");
                Assert.AreEqual("current", spec.color.from);
                Assert.AreEqual("#3382F6", spec.color.to);

                // Round-trip through the JSON dictionary form back into a fresh animation.
                PresetSpec reparsed = PresetSpec.Parse(spec.ToJsonObject());
                var rebuilt = new UIAnimation();
                UISpecGenerator.ApplyPresetToAnimation(reparsed, rebuilt, new GenerateReport());

                Assert.IsTrue(rebuilt.color.enabled);
                Assert.AreEqual(ColorReference.CurrentColor, rebuilt.color.from.reference);
                Assert.AreEqual(ColorReference.CustomColor, rebuilt.color.to.reference);
                Assert.AreEqual("#3382F6", ColorUtils.ToHex(rebuilt.color.to.customColor));
                Assert.AreEqual(0.2f, rebuilt.color.settings.duration, 1e-4f);
                Assert.AreEqual(Ease.OutQuad, rebuilt.color.settings.ease);
            }
            finally { Object.DestroyImmediate(preset); }
        }

        [Test]
        public void PresetSpec_ThemeTokenEndpoint_RoundTripsAsBareName()
        {
            var anim = new UIAnimation();
            anim.color.enabled = true;
            anim.color.to.reference = ColorReference.ThemeToken;
            anim.color.to.themeToken = "Accent";

            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            try
            {
                preset.presetName = "TokenTint";
                preset.animation = anim;

                PresetSpec spec = UISpecExporter.ExportPreset(preset);
                Assert.AreEqual("Accent", spec.color.to, "a theme-token endpoint exports its bare name");

                PresetSpec reparsed = PresetSpec.Parse(spec.ToJsonObject());
                var rebuilt = new UIAnimation();
                UISpecGenerator.ApplyPresetToAnimation(reparsed, rebuilt, new GenerateReport());

                Assert.AreEqual(ColorReference.ThemeToken, rebuilt.color.to.reference);
                Assert.AreEqual("Accent", rebuilt.color.to.themeToken);
            }
            finally { Object.DestroyImmediate(preset); }
        }
    }
}
