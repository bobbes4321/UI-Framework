using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Regression for the settings-menu blank-out: tab components register with their toggle group
    /// one at a time, and enable order is NOT hierarchy-guaranteed (prefab reimport while a scene is
    /// open, domain reloads). When an OFF tab registered before the baked-ON one, the group's
    /// "enforced" rule force-activated it, then deactivated the baked selection when it arrived —
    /// the wrong tab highlighted with every panel hidden, and (combined with the baked-inactive
    /// Show() early-out) no click could ever bring a panel back. UISelectable is ExecuteAlways, so
    /// the whole storm reproduces synchronously in edit mode.
    /// </summary>
    public class TabGroupEnableOrderTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root", typeof(RectTransform));
            _root.SetActive(false); // build everything disabled, then unleash the OnEnable storm
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        private GameObject Child(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            return go;
        }

        private UIPanel MakePanel(string name, bool bakedActive)
        {
            GameObject go = Child($"Panel_{name}");
            var panel = go.AddComponent<UIPanel>();
            panel.id = new PanelId("Tabs", name);
            panel.disableGameObjectWhenHidden = true;
            go.SetActive(bakedActive); // how the generator bakes: selected panel active, rest not
            return panel;
        }

        private UITab MakeTab(string name, UIToggleGroup group, UIPanel panel, bool bakedOn)
        {
            GameObject go = Child($"Tab_{name}");
            var tab = go.AddComponent<UITab>();
            tab.id = new ToggleId("Tabs", name);
            tab.targetContainer = panel;
            // bake the selection the way the generator does — straight into the serialized field,
            // no SetIsOn (the component is disabled; nothing may run yet)
            var so = new SerializedObject(tab);
            so.FindProperty("isOnValue").boolValue = bakedOn;
            so.FindProperty("groupReference").objectReferenceValue = group;
            so.ApplyModifiedPropertiesWithoutUndo();
            return tab;
        }

        [Test]
        public void BakedSelection_SurvivesOffTabsRegisteringFirst()
        {
            GameObject groupGo = Child("TabBar");
            var group = groupGo.AddComponent<UIToggleGroup>();
            group.controlMode = UIToggleGroup.ControlMode.OneToggleOnEnforced;

            // hierarchy (= enable) order is ADVERSE: the off tabs come first, baked-on tab LAST
            UIPanel controlsPanel = MakePanel("Controls", bakedActive: false);
            UIPanel videoPanel = MakePanel("Video", bakedActive: false);
            UIPanel audioPanel = MakePanel("Audio", bakedActive: true);
            UITab controlsTab = MakeTab("Controls", group, controlsPanel, bakedOn: false);
            UITab videoTab = MakeTab("Video", group, videoPanel, bakedOn: false);
            UITab audioTab = MakeTab("Audio", group, audioPanel, bakedOn: true);

            _root.SetActive(true); // the storm: Controls registers first, Audio last

            Assert.IsTrue(audioTab.isOn, "the baked-on tab must keep the selection");
            Assert.IsFalse(controlsTab.isOn, "an off tab must not steal the selection just by enabling first");
            Assert.IsFalse(videoTab.isOn);
            Assert.IsTrue(audioPanel.gameObject.activeSelf, "the baked selection's panel must stay visible");
            Assert.IsFalse(controlsPanel.gameObject.activeSelf);

            // and the panels still switch on a real selection change
            videoTab.SetIsOn(true, animateChange: false);
            Assert.IsTrue(videoPanel.gameObject.activeSelf, "selecting another tab must show its panel");
            Assert.IsFalse(audioPanel.gameObject.activeSelf);
        }

        [Test]
        public void Show_OnBakedInactiveContainer_ActivatesIt()
        {
            _root.SetActive(true);
            GameObject go = Child("Panel");
            var panel = go.AddComponent<UIPanel>();
            panel.disableGameObjectWhenHidden = true;
            go.SetActive(false); // baked hidden — never went through Hide(), so the state lies

            panel.Show();

            Assert.IsTrue(go.activeSelf, "Show on a baked-inactive container must reactivate it");
            Assert.AreEqual(VisibilityState.Visible, panel.visibilityState);
        }
    }
}
