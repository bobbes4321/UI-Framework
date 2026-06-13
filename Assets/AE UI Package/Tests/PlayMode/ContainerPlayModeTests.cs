using System.Collections;
using System.Collections.Generic;
using AlterEyes.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterEyes.UI.Tests
{
    public class ContainerPlayModeTests : PlayModeTestBase
    {
        private UIContainer CreateContainer(out UIContainerUIAnimator animator, float duration = 0.15f)
        {
            GameObject go = CreateUIObject("Container");
            var container = go.AddComponent<UIContainer>();
            animator = go.AddComponent<UIContainerUIAnimator>();
            animator.controller = container;
            animator.showAnimation.fade.enabled = true;
            animator.showAnimation.fade.fromCustomValue = 0f;
            animator.showAnimation.fade.toCustomValue = 1f;
            animator.showAnimation.fade.settings.duration = duration;
            animator.showAnimation.fade.settings.ease = Ease.Linear;
            animator.hideAnimation.fade.enabled = true;
            animator.hideAnimation.fade.fromCustomValue = 1f;
            animator.hideAnimation.fade.toCustomValue = 0f;
            animator.hideAnimation.fade.settings.duration = duration;
            animator.hideAnimation.fade.settings.ease = Ease.Linear;
            return container;
        }

        [UnityTest]
        public IEnumerator InstantShowAndHide_SetStatesAndFireEvents()
        {
            GameObject go = CreateUIObject("Plain");
            var container = go.AddComponent<UIContainer>();
            yield return null; // let Awake/Start run

            var events = new List<string>();
            container.OnShow += () => events.Add("show");
            container.OnVisible += () => events.Add("visible");
            container.OnHide += () => events.Add("hide");
            container.OnHidden += () => events.Add("hidden");

            container.InstantHide();
            Assert.AreEqual(VisibilityState.Hidden, container.visibilityState);
            container.InstantShow();
            Assert.AreEqual(VisibilityState.Visible, container.visibilityState);

            CollectionAssert.AreEqual(new[] { "hide", "hidden", "show", "visible" }, events);
        }

        [UnityTest]
        public IEnumerator AnimatedShow_TransitionsThroughIsShowing()
        {
            UIContainer container = CreateContainer(out _);
            yield return null;
            container.InstantHide();

            bool visibleFired = false;
            container.OnVisible += () => visibleFired = true;

            container.Show();
            Assert.AreEqual(VisibilityState.IsShowing, container.visibilityState);
            Assert.IsFalse(visibleFired, "OnVisible must wait for the animators");

            yield return WaitUntil(() => container.visibilityState == VisibilityState.Visible, 5f, "show transition to finish");
            Assert.IsTrue(visibleFired);
            Assert.That(container.canvasGroup.alpha, Is.EqualTo(1f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator HideDuringShow_ReversesAndEndsHidden()
        {
            UIContainer container = CreateContainer(out UIContainerUIAnimator animator, duration: 0.4f);
            yield return null;
            container.InstantHide();

            container.Show();
            yield return WaitSeconds(0.1f); // mid-transition
            Assert.AreEqual(VisibilityState.IsShowing, container.visibilityState);
            float alphaAtInterrupt = container.canvasGroup.alpha;
            Assert.That(alphaAtInterrupt, Is.GreaterThan(0f).And.LessThan(1f), "should be mid-fade");

            container.Hide();
            Assert.AreEqual(VisibilityState.IsHiding, container.visibilityState);

            yield return WaitUntil(() => container.visibilityState == VisibilityState.Hidden, 5f, "interrupted hide to finish");
            Assert.That(container.canvasGroup.alpha, Is.EqualTo(0f).Within(0.01f), "reversed show should end at alpha 0");
        }

        [UnityTest]
        public IEnumerator TotalShowDuration_ReflectsAnimator()
        {
            UIContainer container = CreateContainer(out _, duration: 0.25f);
            yield return null;
            Assert.That(container.totalShowDuration, Is.EqualTo(0.25f).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator AutoHide_HidesAfterDelay()
        {
            GameObject go = CreateUIObject("AutoHide");
            var container = go.AddComponent<UIContainer>();
            container.autoHideAfterShow = true;
            container.autoHideAfterShowDelay = 0.2f;
            yield return null;

            container.InstantHide();
            container.Show(); // no animators → instant complete, schedules auto-hide
            Assert.AreEqual(VisibilityState.Visible, container.visibilityState);

            yield return WaitUntil(() => container.visibilityState == VisibilityState.Hidden, 5f, "auto-hide");
        }

        [UnityTest]
        public IEnumerator WhenHidden_DisablesGameObjectIfConfigured()
        {
            GameObject go = CreateUIObject("Disabler");
            var container = go.AddComponent<UIContainer>();
            container.disableGameObjectWhenHidden = true;
            yield return null;

            container.InstantHide();
            Assert.IsFalse(go.activeSelf);

            container.Show();
            Assert.IsTrue(go.activeSelf, "Show must re-enable the GameObject");
            Assert.AreEqual(VisibilityState.Visible, container.visibilityState);
        }
    }
}
