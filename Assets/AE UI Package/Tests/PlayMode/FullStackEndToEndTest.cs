using System.Collections;
using AlterEyes.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// Full-stack scenario mirroring the feature spec's worked example at runtime:
    /// themed, animated views + buttons publishing signals + a flow graph navigating between them
    /// + a code-first gameplay hook — the whole pipeline working together.
    /// </summary>
    public class FullStackEndToEndTest : PlayModeTestBase
    {
        private const float AnimationDuration = 0.12f;

        private UIView BuildView(string category, string name, string buttonCategory = null, string buttonName = null, Theme theme = null)
        {
            GameObject go = CreateUIObject($"View_{category}_{name}");
            var view = go.AddComponent<UIView>();
            view.id = new ViewId(category, name);

            var animator = go.AddComponent<UIContainerUIAnimator>();
            animator.controller = view;
            animator.showAnimation.fade.enabled = true;
            animator.showAnimation.fade.fromCustomValue = 0f;
            animator.showAnimation.fade.toCustomValue = 1f;
            animator.showAnimation.fade.settings.duration = AnimationDuration;
            animator.hideAnimation.fade.enabled = true;
            animator.hideAnimation.fade.fromCustomValue = 1f;
            animator.hideAnimation.fade.toCustomValue = 0f;
            animator.hideAnimation.fade.settings.duration = AnimationDuration;

            if (buttonName != null)
            {
                var buttonGo = new GameObject($"Button_{buttonName}", typeof(RectTransform));
                buttonGo.transform.SetParent(go.transform, false);
                var image = buttonGo.AddComponent<Image>();
                var themed = buttonGo.AddComponent<ThemeColorTarget>();
                themed.token = "Primary";
                themed.themeOverride = theme;
                var button = buttonGo.AddComponent<UIButton>();
                button.id = new ButtonId(buttonCategory, buttonName);
            }

            return view;
        }

        [UnityTest]
        public IEnumerator WorkedExample_ThemeAnimationSignalsAndFlow_AllCooperate()
        {
            // ---- theme (§11): one central place controls the button color
            var theme = ScriptableObject.CreateInstance<Theme>();
            theme.SetToken("Primary", new Color(0.23f, 0.53f, 1f));
            ThemeService.activeTheme = theme;

            // ---- views with show/hide animations (§4/§5/§7)
            UIView mainView = BuildView("Menu", "Main", "Action", "Settings", theme);
            UIView settingsView = BuildView("Menu", "Settings", theme: theme);

            // play button with a gameplay signal behaviour (§6/§9)
            UIButton settingsButton = mainView.GetComponentInChildren<UIButton>();
            UIActionBehaviour click = settingsButton.GetOrAddBehaviour(BehaviourTrigger.Click);
            click.sendSignal = true;
            click.signalStream = new StreamId("Gameplay", "OpenSettings");

            // code-first gameplay hook (§13)
            int gameplaySignals = 0;
            System.Action gameplayHook = () => gameplaySignals++;
            Signals.On("Gameplay", "OpenSettings", gameplayHook);

            // ---- flow graph (§10)
            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            var start = graph.AddNode<StartNode>("Start");
            start.outputs.Add(new FlowEdge { toNode = "MainMenu", allowsBack = false });
            var mainMenu = graph.AddNode<UINode>("MainMenu");
            mainMenu.showViews.Add(new UINode.ViewRef("Menu", "Main"));
            mainMenu.hideShownViewsOnExit = true;
            mainMenu.outputs.Add(new FlowEdge
            {
                toNode = "Settings",
                trigger = new FlowTrigger { type = FlowTrigger.TriggerType.ButtonClick, category = "Action", name = "Settings" }
            });
            var settingsNode = graph.AddNode<UINode>("Settings");
            settingsNode.showViews.Add(new UINode.ViewRef("Menu", "Settings"));
            settingsNode.hideShownViewsOnExit = true;
            graph.startNode = "Start";

            GameObject controllerGo = Track(new GameObject("FlowController"));
            controllerGo.SetActive(false);
            var controller = controllerGo.AddComponent<FlowController>();
            controller.flow = graph;
            yield return null;

            try
            {
                // theme applied the token color to the button before anything ran
                Image buttonImage = settingsButton.GetComponent<Image>();
                Assert.That(buttonImage.color.b, Is.EqualTo(1f).Within(0.01f), "theme token should color the button");

                mainView.InstantHide();
                settingsView.InstantHide();

                // ---- start the flow: main menu animates in
                controllerGo.SetActive(true);
                yield return null; // first start is deferred to Start() (the enable-order race fix)
                Assert.AreEqual("MainMenu", controller.activeNode.name);
                Assert.AreEqual(VisibilityState.IsShowing, mainView.visibilityState, "show should be animated, not instant");
                yield return WaitUntil(() => mainView.visibilityState == VisibilityState.Visible, 5f, "main view to finish its show animation");
                Assert.That(mainView.canvasGroup.alpha, Is.EqualTo(1f).Within(0.01f));

                // ---- click: gameplay hook fires AND the flow navigates
                settingsButton.Click();
                Assert.AreEqual(1, gameplaySignals, "code-first Signals.On hook should receive the button's signal");
                Assert.AreEqual("Settings", controller.activeNode.name);

                yield return WaitUntil(() => settingsView.visibilityState == VisibilityState.Visible, 5f, "settings view to animate in");
                yield return WaitUntil(() => mainView.visibilityState == VisibilityState.Hidden, 5f, "main view to animate out");

                // ---- live theme switch recolors the (now hidden) button instantly
                theme.SetToken("Primary", Color.red);
                Assert.That(buttonImage.color.r, Is.EqualTo(1f).Within(0.01f), "token change should recolor bound elements");

                // ---- back navigation through the history stack
                BackButton.EnableByForce();
                yield return WaitSeconds(0.15f); // clear the back-button cooldown window
                Assert.IsTrue(BackButton.ForceFire(this));
                Assert.AreEqual("MainMenu", controller.activeNode.name);
                yield return WaitUntil(() => mainView.visibilityState == VisibilityState.Visible, 5f, "main view to animate back in");
            }
            finally
            {
                Signals.Off("Gameplay", "OpenSettings", gameplayHook);
                ThemeService.activeTheme = null;
                Object.Destroy(theme);
                Object.Destroy(graph);
            }
        }
    }
}
