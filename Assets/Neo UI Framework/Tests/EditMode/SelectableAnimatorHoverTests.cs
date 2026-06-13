using Neo.UI;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Regression coverage for the hover scale-feel on buttons/tabs (UISelectableUIAnimator).
    /// The bug: un-hovering while the Highlighted scale-up was still playing left the button stuck
    /// at the enlarged scale, because the Normal (no-channel) transition only settled back to rest
    /// when the previous animation was NOT active. These tests drive UITick deterministically.
    /// </summary>
    public class SelectableAnimatorHoverTests
    {
        private const float HoverScale = 1.05f;
        private const float HoverDuration = 0.12f;

        private GameObject _go;
        private RectTransform _rect;
        private UISelectableUIAnimator _animator;

        [SetUp]
        public void SetUp()
        {
            UITick.Clear();
            TweenPool.Clear();

            _go = new GameObject("Button", typeof(RectTransform));
            _rect = _go.GetComponent<RectTransform>();
            _rect.localScale = Vector3.one;

            _animator = _go.AddComponent<UISelectableUIAnimator>();
            // Mirror UIWidgetFactory.AddHoverAndPressFeel: hover grows from rest to HoverScale.
            ScaleAnimation hover = _animator.highlightedAnimation.scale;
            hover.enabled = true;
            hover.fromReference = ReferenceValue.StartValue;
            hover.toReference = ReferenceValue.CustomValue;
            hover.toCustomValue = new Vector3(HoverScale, HoverScale, 1f);
            hover.settings.duration = HoverDuration;
            hover.settings.ease = Ease.OutQuad;
            // Normal stays with no enabled channels — un-hover must return to rest implicitly.

            // Capture rest values at scale 1 (Awake does not run for AddComponent in EditMode).
            _animator.BindTarget();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            UITick.Clear();
        }

        private static void Tick(float dt) => UITick.Tick(dt);

        private void Hover() => _animator.OnSelectionStateChanged(UISelectionState.Highlighted, instant: false);

        private void Unhover(bool instant = false) =>
            _animator.OnSelectionStateChanged(UISelectionState.Normal, instant);

        [Test]
        public void Hover_GrowsTowardHoverScale()
        {
            Hover();
            Tick(HoverDuration);
            Assert.That(_rect.localScale.x, Is.EqualTo(HoverScale).Within(1e-3f),
                "hovering should grow the button to the configured hover scale");
        }

        [Test]
        public void UnhoverAfterHoverCompletes_ReturnsToRest()
        {
            Hover();
            Tick(HoverDuration); // hover finishes, scale at HoverScale
            Assume.That(_rect.localScale.x, Is.EqualTo(HoverScale).Within(1e-3f));

            Unhover();
            Tick(HoverDuration * 2f); // let the return animation finish

            Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                "un-hovering after the grow finished must return to rest scale");
            Assert.AreEqual(0, UITick.count, "return-to-rest tween should unregister when finished");
        }

        [Test]
        public void UnhoverMidGrow_ReturnsToRest()
        {
            // The original bug: un-hovering while the grow tween was STILL playing skipped the
            // settle-to-rest, leaving the button stuck enlarged.
            Hover();
            Tick(HoverDuration * 0.5f); // mid-flight, partially grown
            Assume.That(_rect.localScale.x, Is.GreaterThan(1f),
                "precondition: the button is mid grow-up when un-hovered");

            Unhover();
            Tick(HoverDuration * 2f); // let the reverse finish

            Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                "un-hovering mid-grow must still return the button to rest scale");
            Assert.AreEqual(0, UITick.count, "reverse tween should unregister when finished");
        }

        [Test]
        public void InstantUnhoverMidGrow_SnapsToRest()
        {
            Hover();
            Tick(HoverDuration * 0.5f);
            Assume.That(_rect.localScale.x, Is.GreaterThan(1f));

            Unhover(instant: true);

            Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-4f),
                "an instant un-hover must snap straight back to rest scale");
            Assert.AreEqual(0, UITick.count, "instant un-hover should leave no active tween");
        }

        [Test]
        public void RepeatedHoverUnhover_AlwaysSettlesAtRest()
        {
            for (int i = 0; i < 3; i++)
            {
                Hover();
                Tick(HoverDuration * 0.5f);
                Unhover();
                Tick(HoverDuration * 2f);
                Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                    $"cycle {i}: button must rest at scale 1 after each hover/un-hover");
            }
        }
    }
}
