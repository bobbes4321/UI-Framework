using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Regression for the start-on-the-wrong-view bug: a view generated WITHOUT show/hide animations
    /// carries a UIContainerUIAnimator with no enabled channels — hiding flipped the state but never
    /// drove CanvasGroup alpha, so every "hidden" view kept rendering at full alpha and the topmost
    /// one covered whatever the flow actually showed. An empty animator now owns the minimal visual
    /// contract: hidden = alpha 0, visible = alpha 1.
    /// </summary>
    public class EmptyContainerAnimatorTests : PlayModeTestBase
    {
        [UnityTest]
        public IEnumerator AnimationlessView_HidesAndShowsItsAlpha()
        {
            GameObject go = CreateUIObject("View");
            go.AddComponent<CanvasGroup>().alpha = 1f; // baked WYSIWYG: prefabs save at full alpha
            var view = go.AddComponent<UIView>();
            view.id = new ViewId("Test", "Animationless");
            var animator = go.AddComponent<UIContainerUIAnimator>();
            animator.controller = view; // no channels configured on either animation
            yield return null;

            view.InstantHide();
            Assert.AreEqual(VisibilityState.Hidden, view.visibilityState);
            Assert.That(view.canvasGroup.alpha, Is.EqualTo(0f).Within(0.01f),
                "hiding an animation-less view must actually make it invisible");

            view.Show();
            yield return null;
            Assert.AreEqual(VisibilityState.Visible, view.visibilityState);
            Assert.That(view.canvasGroup.alpha, Is.EqualTo(1f).Within(0.01f),
                "showing it must bring the alpha back");
        }
    }
}
