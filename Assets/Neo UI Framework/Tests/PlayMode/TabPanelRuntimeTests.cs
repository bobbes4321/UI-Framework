using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Runtime regression for the bug a user hit first: clicking a tab "did nothing" because the
    /// base <see cref="UIContainer"/> never drives CanvasGroup alpha (that's an animator's job) and
    /// panels carry no animator. Tab panels instead hide by deactivating the GameObject
    /// (disableGameObjectWhenHidden), and the tab pushes its selection to the panel — so toggling a
    /// tab must actually activate/deactivate (and show/hide) its panel.
    /// </summary>
    public class TabPanelRuntimeTests : PlayModeTestBase
    {
        [UnityTest]
        public IEnumerator TogglingTab_ActivatesAndDeactivatesItsPanel()
        {
            GameObject panelGo = CreateUIObject("Panel");
            var panel = panelGo.AddComponent<UIPanel>();
            panel.id = new PanelId("Panels", "A");
            panel.disableGameObjectWhenHidden = true; // how the generator bakes tab panels

            GameObject tabGo = CreateUIObject("Tab");
            var tab = tabGo.AddComponent<UITab>();
            // assigning the container makes the tab push its (currently off) state → panel hides
            tab.targetContainer = panel;
            yield return null;

            Assert.IsFalse(panel.gameObject.activeSelf, "an off tab's panel must deactivate (vacates layout, disappears)");

            tab.SetIsOn(true, animateChange: false);
            yield return null;
            Assert.IsTrue(panel.gameObject.activeSelf, "selecting the tab must re-activate its panel");
            Assert.AreEqual(VisibilityState.Visible, panel.visibilityState, "and show it");

            tab.SetIsOn(false, animateChange: false);
            yield return null;
            Assert.IsFalse(panel.gameObject.activeSelf, "deselecting must hide + deactivate the panel again");
        }

        [UnityTest]
        public IEnumerator TabGroup_ShowsOnlyTheSelectedPanel()
        {
            GameObject groupGo = CreateUIObject("TabBar");
            var group = groupGo.AddComponent<UIToggleGroup>();
            group.controlMode = UIToggleGroup.ControlMode.OneToggleOnEnforced;

            UIPanel a = MakePanel("A");
            UIPanel b = MakePanel("B");
            UITab tabA = MakeTab("A", group, a, startOn: true);
            UITab tabB = MakeTab("B", group, b, startOn: false);
            yield return null;

            Assert.IsTrue(a.gameObject.activeSelf, "the selected tab's panel shows");
            Assert.IsFalse(b.gameObject.activeSelf, "the other panel is hidden");

            tabB.SetIsOn(true, animateChange: false);
            yield return null;
            Assert.IsTrue(b.gameObject.activeSelf, "selecting B shows B's panel");
            Assert.IsFalse(a.gameObject.activeSelf, "and hides A's panel");
            Assert.IsFalse(tabA.isOn, "the group enforces a single selected tab");
        }

        private UIPanel MakePanel(string name)
        {
            GameObject go = CreateUIObject($"Panel_{name}");
            var panel = go.AddComponent<UIPanel>();
            panel.id = new PanelId("Panels", name);
            panel.disableGameObjectWhenHidden = true;
            return panel;
        }

        private UITab MakeTab(string name, UIToggleGroup group, UIPanel panel, bool startOn)
        {
            GameObject go = CreateUIObject($"Tab_{name}");
            var tab = go.AddComponent<UITab>();
            tab.id = new ToggleId("Tabs", name);
            tab.toggleGroup = group;
            tab.SetIsOn(startOn, animateChange: false);
            tab.targetContainer = panel;
            return tab;
        }
    }
}
