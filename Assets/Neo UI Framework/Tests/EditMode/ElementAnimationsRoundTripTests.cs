using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Per-element "animations" spec field: named presets copy into a widget's hover/press/loop animators,
    /// a NeoAnimationSourceTag records the names, and the whole thing round-trips export→generate→export
    /// byte-identically. Uses a self-made temp preset so the test never depends on the shipped library.
    /// </summary>
    public class ElementAnimationsRoundTripTests
    {
        private const string PresetFolder = "Assets/NeoUITestScratchAnim";

        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""Anim/Screen"", ""elements"": [
            { ""button"": { ""id"": ""Anim/Hero"", ""label"": ""Go"",
              ""animations"": { ""hover"": ""RTScale"", ""loop"": ""RTScale"" } } }
          ] } ]
        }";

        [OneTimeSetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(PresetFolder))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratchAnim");

            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = "Hover";
            preset.presetName = "RTScale";
            preset.animation.scale.enabled = true;
            preset.animation.scale.toReference = ReferenceValue.CustomValue;
            preset.animation.scale.toCustomValue = new Vector3(1.2f, 1.2f, 1f);
            AssetDatabase.CreateAsset(preset, $"{PresetFolder}/Hover_RTScale.asset");
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.DeleteAsset(PresetFolder);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
        }

        private static UIButton GenerateAndFindHero()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Anim_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab.GetComponentsInChildren<UIButton>(true).First(b => b.id.Matches("Anim", "Hero"));
        }

        [Test]
        public void GenerateBakesPresets_AndStampsTag()
        {
            UIButton button = GenerateAndFindHero();

            var sel = button.GetComponent<UISelectableUIAnimator>();
            Assert.IsNotNull(sel, "hover animation reuses/adds the selectable animator");
            Assert.IsTrue(sel.highlightedAnimation.scale.enabled, "hover preset copied into highlighted slot");
            Assert.AreEqual(new Vector3(1.2f, 1.2f, 1f), sel.highlightedAnimation.scale.toCustomValue);

            var loop = button.GetComponent<UIAnimator>();
            Assert.IsNotNull(loop, "loop adds a play-on-start UIAnimator");
            Assert.AreEqual(AnimatorStartBehaviour.PlayForward, loop.onStartBehaviour);
            Assert.IsTrue(loop.animation.scale.enabled);

            var tag = button.GetComponent<NeoAnimationSourceTag>();
            Assert.IsNotNull(tag, "applied names are stamped for export");
            Assert.AreEqual("RTScale", tag.hover);
            Assert.AreEqual("RTScale", tag.loop);
        }

        [Test]
        public void Export_RecoversNames_AndRoundTripsByteIdentical()
        {
            GenerateAndFindHero();

            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec button = exported.views.First(v => v.id == "Anim/Screen")
                .elements.First(e => e.kind == "button");
            Assert.IsNotNull(button.animations, "the tag is recovered into the spec");
            Assert.AreEqual("RTScale", button.animations.hover);
            Assert.AreEqual("RTScale", button.animations.loop);

            string first = UISpecExporter.ExportProject().ToJson();
            UISpecGenerator.Generate(UISpec.FromJson(first));
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second, "element animations must round-trip byte-identically");
        }
    }
}
