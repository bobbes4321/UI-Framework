using System.Collections;
using AlterEyes.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterEyes.UI.Tests
{
    public class ViewAndInteractiveTests : PlayModeTestBase
    {
        private UIView CreateView(string category, string name)
        {
            GameObject go = CreateUIObject($"View_{category}_{name}");
            var view = go.AddComponent<UIView>();
            view.id = new ViewId(category, name);
            return view;
        }

        [UnityTest]
        public IEnumerator StaticApi_ShowsAndHidesByAddress()
        {
            UIView main = CreateView("Menu", "Main");
            UIView settings = CreateView("Menu", "Settings");
            UIView hud = CreateView("HUD", "Top");
            yield return null;

            UIView.HideAllViews(instant: true);
            Assert.AreEqual(VisibilityState.Hidden, main.visibilityState);
            Assert.AreEqual(VisibilityState.Hidden, settings.visibilityState);
            Assert.AreEqual(VisibilityState.Hidden, hud.visibilityState);

            UIView.Show("Menu", "Main", instant: true);
            Assert.AreEqual(VisibilityState.Visible, main.visibilityState);
            Assert.AreEqual(VisibilityState.Hidden, settings.visibilityState);

            UIView.ShowCategory("Menu", instant: true);
            Assert.AreEqual(VisibilityState.Visible, settings.visibilityState);
            Assert.AreEqual(VisibilityState.Hidden, hud.visibilityState);

            UIView.HideCategory("Menu", instant: true);
            Assert.AreEqual(VisibilityState.Hidden, main.visibilityState);
            Assert.AreEqual(VisibilityState.Hidden, settings.visibilityState);

            Assert.AreSame(main, UIView.GetFirstView("Menu", "Main"));
        }

        [UnityTest]
        public IEnumerator ViewVisibility_IsPublishedAsSignal()
        {
            UIView view = CreateView("Menu", "Signals");
            yield return null;

            ViewVisibilityData lastData = default;
            int received = 0;
            System.Action<ViewVisibilityData> handler = data =>
            {
                lastData = data;
                received++;
            };
            Signals.On(UIView.StreamCategory, UIView.VisibilityStreamName, handler);

            view.InstantHide();
            Assert.Greater(received, 0);
            Assert.AreEqual("Signals", lastData.viewName);
            Assert.AreEqual(VisibilityState.Hidden, lastData.state);

            Signals.Off(UIView.StreamCategory, UIView.VisibilityStreamName, handler);
        }

        [UnityTest]
        public IEnumerator Button_ClickFiresEventAndSignal()
        {
            GameObject go = CreateUIObject("Button");
            var button = go.AddComponent<UIButton>();
            button.id = new ButtonId("Action", "Play");
            yield return null;

            int clicks = 0;
            button.onClickEvent.AddListener(() => clicks++);

            ButtonSignalData signalData = default;
            int signals = 0;
            System.Action<ButtonSignalData> handler = data =>
            {
                signalData = data;
                signals++;
            };
            Signals.On(UIButton.StreamCategory, UIButton.StreamName, handler);

            button.Click();
            Assert.AreEqual(1, clicks);
            Assert.AreEqual(1, signals);
            Assert.AreEqual("Action", signalData.category);
            Assert.AreEqual("Play", signalData.buttonName);
            Assert.AreEqual(BehaviourTrigger.Click, signalData.trigger);

            Signals.Off(UIButton.StreamCategory, UIButton.StreamName, handler);
        }

        [UnityTest]
        public IEnumerator Button_CooldownBlocksRapidExecution()
        {
            GameObject go = CreateUIObject("CooldownButton");
            var button = go.AddComponent<UIButton>();
            yield return null;

            UIActionBehaviour click = button.GetOrAddBehaviour(BehaviourTrigger.Click);
            click.cooldown = 10f;
            int executions = 0;
            click.onExecute = () => executions++;

            Assert.IsTrue(click.Execute());
            Assert.IsFalse(click.Execute(), "second execute within cooldown must be rejected");
            Assert.AreEqual(1, executions);
        }

        [UnityTest]
        public IEnumerator ToggleGroup_OneToggleOn_EnforcesExclusivity()
        {
            GameObject groupGo = CreateUIObject("Group");
            var group = groupGo.AddComponent<UIToggleGroup>();
            group.controlMode = UIToggleGroup.ControlMode.OneToggleOn;

            var toggles = new UIToggle[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject toggleGo = CreateUIObject($"Toggle{i}");
                toggles[i] = toggleGo.AddComponent<UIToggle>();
                toggles[i].toggleGroup = group;
            }
            yield return null;

            toggles[0].isOn = true;
            Assert.IsTrue(toggles[0].isOn);

            toggles[1].isOn = true;
            Assert.IsTrue(toggles[1].isOn);
            Assert.IsFalse(toggles[0].isOn, "turning one on must turn the other off");

            toggles[1].isOn = false;
            Assert.IsFalse(toggles[1].isOn, "OneToggleOn allows all off");
        }

        [UnityTest]
        public IEnumerator Toggle_EventsAndInstantVariant()
        {
            GameObject go = CreateUIObject("Toggle");
            var toggle = go.AddComponent<UIToggle>();
            yield return null;

            int changed = 0, on = 0, off = 0;
            bool lastAnimate = true;
            toggle.onValueChanged.AddListener(_ => changed++);
            toggle.onToggleOn.AddListener(() => on++);
            toggle.onToggleOff.AddListener(() => off++);
            toggle.OnValueChanged += (_, animate) => lastAnimate = animate;

            toggle.isOn = true;
            Assert.AreEqual(1, changed);
            Assert.AreEqual(1, on);
            Assert.IsTrue(lastAnimate);

            toggle.InstantToggleOff();
            Assert.AreEqual(2, changed);
            Assert.AreEqual(1, off);
            Assert.IsFalse(lastAnimate, "instant variant must report animateChange = false");
        }

        [UnityTest]
        public IEnumerator Tab_SyncsWithContainerVisibility()
        {
            GameObject containerGo = CreateUIObject("TabTarget");
            var container = containerGo.AddComponent<UIContainer>();

            GameObject tabGo = CreateUIObject("Tab");
            var tab = tabGo.AddComponent<UITab>();
            tab.targetContainer = container;
            yield return null;

            // tab drives container
            tab.targetContainer = container;
            tab.isOn = false;
            container.InstantShow();

            tab.SetIsOn(false, animateChange: false);
            yield return WaitUntil(() => container.visibilityState == VisibilityState.Hidden, 5f, "tab to hide container");

            tab.SetIsOn(true, animateChange: false);
            yield return WaitUntil(() => container.visibilityState == VisibilityState.Visible, 5f, "tab to show container");

            // container drives tab
            container.InstantHide();
            Assert.IsFalse(tab.isOn, "hiding the container must turn the tab off");
        }

        [UnityTest]
        public IEnumerator Stepper_StepsAndClamps()
        {
            GameObject go = CreateUIObject("Stepper");
            var stepper = go.AddComponent<UIStepper>();
            stepper.minValue = 0f;
            stepper.maxValue = 2f;
            stepper.stepSize = 1f;
            yield return null;

            int reachedMax = 0;
            stepper.OnValueReachedMax.AddListener(() => reachedMax++);

            stepper.StepUp();
            Assert.AreEqual(1f, stepper.currentValue);
            stepper.StepUp();
            Assert.AreEqual(2f, stepper.currentValue);
            Assert.AreEqual(1, reachedMax);
            stepper.StepUp();
            Assert.AreEqual(2f, stepper.currentValue, "must clamp at max");

            stepper.StepDown();
            Assert.AreEqual(1f, stepper.currentValue);
        }

        [UnityTest]
        public IEnumerator UITag_RegistryLookupWorks()
        {
            GameObject go = CreateUIObject("Tagged");
            var uiTag = go.AddComponent<UITag>();
            uiTag.id = new TagId("Popup", "Anchor");
            yield return null;

            Assert.AreSame(uiTag, UITag.GetFirstTag("Popup", "Anchor"));
            Assert.IsNull(UITag.GetFirstTag("Popup", "Missing"));

            go.SetActive(false);
            Assert.IsNull(UITag.GetFirstTag("Popup", "Anchor"), "disabled tags leave the registry");
        }

        [UnityTest]
        public IEnumerator ThemeColorTarget_AppliesAndFollowsTokenChanges()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            theme.SetToken("Primary", Color.red);
            ThemeService.activeTheme = theme;

            try
            {
                GameObject go = CreateUIObject("Themed");
                var image = go.AddComponent<UnityEngine.UI.Image>();
                var target = go.AddComponent<ThemeColorTarget>();
                target.token = "Primary";
                yield return null;

                Assert.AreEqual(Color.red, image.color);

                theme.SetToken("Primary", Color.green);
                Assert.AreEqual(Color.green, image.color, "bound element must recolor when the token changes");
            }
            finally
            {
                ThemeService.activeTheme = null;
                Object.Destroy(theme);
            }
        }
    }
}
