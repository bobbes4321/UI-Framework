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
    /// Beautification P6 (game-feel): default press-scale on factory buttons, springy switch
    /// knobs, the cascade stagger, counter/badge juice widgets and their deterministic export.
    /// </summary>
    public class JuiceTests
    {
        private const string JuiceSpecJson = @"{
          ""views"": [ { ""id"": ""Juice/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 12, ""cascade"": true, ""children"": [
              { ""counter"": { ""value"": 1250, ""label"": ""#,0"", ""textStyle"": ""Title"" } },
              { ""button"": { ""id"": ""Juice/Inbox"", ""label"": ""Inbox"", ""badge"": 7 } },
              { ""switch"": { ""id"": ""Juice/Sound"" } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateJuiceView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(JuiceSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Juice_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        [Test]
        public void Buttons_GetDefaultPressFeel()
        {
            GameObject prefab = GenerateJuiceView();
            UIButton button = prefab.GetComponentsInChildren<UIButton>(true)
                .First(b => b.id.Matches("Juice", "Inbox"));

            var animator = button.GetComponent<UISelectableUIAnimator>();
            Assert.IsNotNull(animator, "factory buttons carry the press-feel animator");
            Assert.IsTrue(animator.pressedAnimation.scale.enabled);
            Assert.AreEqual(new Vector3(0.96f, 0.96f, 1f), animator.pressedAnimation.scale.toCustomValue);
            Assert.IsFalse(animator.normalAnimation.hasEnabledChannels,
                "normal state restores rest values instead of animating");
        }

        [Test]
        public void SwitchKnob_TravelsWithSpring()
        {
            GameObject prefab = GenerateJuiceView();
            UIToggle toggle = prefab.GetComponentsInChildren<UIToggle>(true)
                .First(t => t.id.Matches("Juice", "Sound"));
            Transform knob = toggle.transform.Find(UIWidgetFactory.KnobName);
            var animator = knob.GetComponent<UIToggleUIAnimator>();
            Assert.AreEqual(Ease.Spring, animator.onAnimation.move.settings.ease);
            Assert.AreEqual(Ease.Spring, animator.offAnimation.move.settings.ease);
        }

        [Test]
        public void Cascade_Counter_Badge_GenerateAndBake()
        {
            GameObject prefab = GenerateJuiceView();

            var stack = prefab.GetComponentsInChildren<UnityEngine.UI.VerticalLayoutGroup>(true)
                .First(g => g.name.StartsWith("vstack"));
            Assert.IsNotNull(stack.GetComponent<UICascadeChildren>(), "cascade: true adds the stagger component");

            UICounter counter = prefab.GetComponentInChildren<UICounter>(true);
            Assert.IsNotNull(counter);
            Assert.AreEqual(1250f, counter.value);
            Assert.AreEqual("#,0", counter.format);
            Assert.AreEqual("1,250", counter.GetComponent<TMP_Text>().text, "the start value is baked (WYSIWYG)");

            UIButton button = prefab.GetComponentsInChildren<UIButton>(true)
                .First(b => b.id.Matches("Juice", "Inbox"));
            Transform badge = button.transform.Find(UIWidgetFactory.BadgeName);
            Assert.IsNotNull(badge, "badge: 7 builds the Badge child");
            Assert.AreEqual(7, badge.GetComponent<UIBadge>().count);
            Assert.AreEqual("7", badge.GetComponentInChildren<TMP_Text>(true).text);
        }

        /// <summary>
        /// Regression: settling a state animation back to rest must only restore the channels it
        /// animated. The press-feel animation owns scale ONLY — restoring the captured position
        /// teleported buttons to stale layout positions after the vstack re-laid them out for a
        /// different resolution.
        /// </summary>
        [Test]
        public void StateAnimation_Restore_LeavesLayoutOwnedChannelsAlone()
        {
            var go = new GameObject("Button", typeof(RectTransform));
            try
            {
                var rect = (RectTransform)go.transform;
                rect.anchoredPosition3D = new Vector3(10f, 20f, 0f);

                var animation = new UIAnimation { purpose = AnimationPurpose.State };
                animation.scale.enabled = true;
                animation.SetTarget(rect); // captures start values (position 10,20 / scale 1)

                rect.localScale = new Vector3(0.96f, 0.96f, 1f);          // pressed state
                rect.anchoredPosition3D = new Vector3(300f, -50f, 0f);    // layout re-positioned it

                animation.RestoreStartValues();

                Assert.AreEqual(new Vector3(300f, -50f, 0f), rect.anchoredPosition3D,
                    "position is layout-owned — a scale-only animation must not touch it");
                Assert.AreEqual(Vector3.one, rect.localScale, "the animated channel is restored");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Badge_ZeroHides()
        {
            var widget = new GameObject("Widget", typeof(RectTransform));
            try
            {
                GameObject badgeGo = UIWidgetFactory.CreateBadge(widget, 3);
                var badge = badgeGo.GetComponent<UIBadge>();
                TMP_Text label = badgeGo.GetComponentInChildren<TMP_Text>(true);
                Assert.AreEqual("3", label.text);

                badge.SetCount(0);
                Assert.IsFalse(label.gameObject.activeSelf, "zero hides the badge visuals");

                badge.SetCount(120);
                Assert.AreEqual("99+", label.text);
                Assert.IsTrue(label.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(widget);
            }
        }

        [Test]
        public void Export_JuiceFeatures_RoundTrip()
        {
            GenerateJuiceView();
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Juice/Screen");
            Assert.IsNotNull(view);

            ElementSpec stack = view.elements.First(e => e.kind == "vstack");
            Assert.IsTrue(stack.cascade, "cascade must export");

            ElementSpec counter = stack.children.First(e => e.kind == "counter");
            Assert.AreEqual(1250f, counter.value);
            Assert.AreEqual("#,0", counter.label);
            Assert.AreEqual("Title", counter.textStyle);
            Assert.IsNull(counter.fontSize, "the text style owns the size");

            ElementSpec button = stack.children.First(e => e.kind == "button");
            Assert.AreEqual(7f, button.badge);
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_WithJuice()
        {
            GenerateJuiceView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "cascade/counter/badge must round-trip byte-identically");
        }
    }
}
