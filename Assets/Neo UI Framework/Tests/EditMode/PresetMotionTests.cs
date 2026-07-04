using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// A widget preset's <c>motion</c> field is functional: it seeds the element's on-start/loop
    /// animation channel at generate (a play-on-start <see cref="UIAnimator"/>), the element's own loop
    /// still overrides it, and the link round-trips — export strips the motion-derived loop back out so
    /// the spec keeps only the override delta, byte-identically.
    /// </summary>
    public class PresetMotionTests
    {
        private const string AnimFolder = "Assets/NeoUITestScratchPresetMotion";
        private const string MotionPreset = "PMScale";
        private const string OtherLoop = "PMSpin";
        private const string WidgetPresetName = "PMHero";

        private NeoWidgetPreset _preset;

        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""PM/Screen"", ""elements"": [ { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
            { ""button"": { ""id"": ""PM/Plain"",    ""label"": ""Go"", ""preset"": ""PMHero"" } },
            { ""button"": { ""id"": ""PM/OwnLoop"",  ""label"": ""Go"", ""preset"": ""PMHero"", ""animations"": { ""loop"": ""PMSpin"" } } }
          ] } } ] } ]
        }";

        private static UIAnimationPreset MakeAnim(string name)
        {
            var anim = ScriptableObject.CreateInstance<UIAnimationPreset>();
            anim.category = "Loop";
            anim.presetName = name;
            anim.animation.scale.enabled = true;
            anim.animation.scale.toReference = ReferenceValue.CustomValue;
            anim.animation.scale.toCustomValue = new Vector3(1.1f, 1.1f, 1f);
            return anim;
        }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(AnimFolder))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratchPresetMotion");

            AssetDatabase.CreateAsset(MakeAnim(MotionPreset), $"{AnimFolder}/Loop_PMScale.asset");
            AssetDatabase.CreateAsset(MakeAnim(OtherLoop), $"{AnimFolder}/Loop_PMSpin.asset");
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();

            _preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            _preset.presetName = WidgetPresetName;
            _preset.targetKind = "button";
            _preset.variant = "primary";
            _preset.motion = MotionPreset;
            NeoWidgetPresets.Register(_preset);
        }

        [TearDown]
        public void TearDown()
        {
            NeoWidgetPresets.ResetForTests();
            if (_preset != null) Object.DestroyImmediate(_preset);
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.DeleteAsset(AnimFolder);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
        }

        private static GameObject Generate()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/PM_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        private static GameObject Button(GameObject prefab, string name) =>
            prefab.GetComponentsInChildren<UIButton>(true).First(b => b.id.Matches("PM", name)).gameObject;

        [Test]
        public void Motion_SeedsPlayOnStartLoopAnimator()
        {
            GameObject plain = Button(Generate(), "Plain");

            var loop = plain.GetComponent<UIAnimator>();
            Assert.IsNotNull(loop, "preset motion attaches a play-on-start UIAnimator");
            Assert.AreEqual(AnimatorStartBehaviour.PlayForward, loop.onStartBehaviour);
            Assert.IsTrue(loop.animation.scale.enabled, "the motion preset's animation copied in");

            var tag = plain.GetComponent<NeoAnimationSourceTag>();
            Assert.AreEqual(MotionPreset, tag?.loop, "the motion name is stamped for round-trip");
        }

        [Test]
        public void Export_StripsMotionDerivedLoop_KeepingTheLink()
        {
            Generate();
            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec stack = exported.views.First(v => v.id == "PM/Screen").elements.First(e => e.kind == "vstack");

            ElementSpec plain = stack.children.First(e => e.id == "PM/Plain");
            Assert.AreEqual(WidgetPresetName, plain.preset, "the preset link survives");
            Assert.IsTrue(plain.animations == null || string.IsNullOrEmpty(plain.animations.loop),
                "a loop equal to the preset's motion is stripped (delta only — not re-emitted inline)");
        }

        [Test]
        public void ElementLoop_OverridesPresetMotion_AndSurvivesAsDelta()
        {
            GameObject own = Button(Generate(), "OwnLoop");

            var tag = own.GetComponent<NeoAnimationSourceTag>();
            Assert.AreEqual(OtherLoop, tag?.loop, "the element's own loop wins over the preset's motion");

            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec stack = exported.views.First(v => v.id == "PM/Screen").elements.First(e => e.kind == "vstack");
            ElementSpec own2 = stack.children.First(e => e.id == "PM/OwnLoop");
            Assert.AreEqual(WidgetPresetName, own2.preset, "the preset link survives");
            Assert.AreEqual(OtherLoop, own2.animations?.loop,
                "a loop that differs from the preset motion stays in the delta (not stripped)");

            string first = exported.ToJson();
            UISpecGenerator.Generate(UISpec.FromJson(first));
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second, "preset motion + an overriding loop round-trip byte-identically");
        }
    }
}
