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
        private const float PressScale = 0.96f;
        private const float PressDuration = 0.08f;

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
            // Mirror the press dip too: pressing scales below rest.
            ScaleAnimation press = _animator.pressedAnimation.scale;
            press.enabled = true;
            press.fromReference = ReferenceValue.StartValue;
            press.toReference = ReferenceValue.CustomValue;
            press.toCustomValue = new Vector3(PressScale, PressScale, 1f);
            press.settings.duration = PressDuration;
            press.settings.ease = Ease.OutQuad;
            // Normal AND Selected stay with no enabled channels — leaving them must return to rest.

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

        private void Press() => _animator.OnSelectionStateChanged(UISelectionState.Pressed, instant: false);

        private void Select() => _animator.OnSelectionStateChanged(UISelectionState.Selected, instant: false);

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

        [Test]
        public void ClickSequence_SelectedThenNormal_SettlesAtRest()
        {
            // The drift bug: a real click is Highlighted → Pressed → Selected → Normal. Selected has
            // no channels (settles _current back to rest), and so does Normal. The SECOND no-channel
            // transition used to re-reverse the already-settled pressed tween — replaying it FORWARD
            // and stranding the widget at the pressed end scale (0.96). On a full-width button that
            // reads as "no longer fills its container"; on a tab it reads as a gap.
            Hover();
            Tick(HoverDuration * 0.4f);
            Press();
            Tick(PressDuration); // press dip completes
            Assume.That(_rect.localScale.x, Is.LessThan(1f), "precondition: pressed dips below rest");

            Select();
            Tick(PressDuration * 2f); // first no-channel: settle back toward rest
            Unhover();                // second no-channel (deselect): MUST stay at rest, not replay forward
            Tick(PressDuration * 2f);

            Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                "Selected → Normal (two no-channel states) must leave the widget at rest scale 1");
            Assert.AreEqual(0, UITick.count, "no tween should remain active once settled at rest");
        }

        [Test]
        public void TwoConsecutiveNoChannelStates_AfterHover_StayAtRest()
        {
            // Minimal form of the same bug: hover, then two no-channel states back to back.
            Hover();
            Tick(HoverDuration);
            Assume.That(_rect.localScale.x, Is.EqualTo(HoverScale).Within(1e-3f));

            Select();              // first no-channel: reverse highlighted back to rest
            Tick(HoverDuration * 2f);
            Unhover();             // second no-channel: must NOT replay highlighted forward
            Tick(HoverDuration * 2f);

            Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                "two no-channel states in a row must not re-grow the widget to the hover scale");
            Assert.AreEqual(0, UITick.count);
        }

        private const float TiltDegrees = 6f;

        private void EnableHoverTilt()
        {
            // Mirror the library's Hover/TiltLeft-style presets: hover rotates, press only scales.
            RotateAnimation tilt = _animator.highlightedAnimation.rotate;
            tilt.enabled = true;
            tilt.fromReference = ReferenceValue.StartValue;
            tilt.toReference = ReferenceValue.StartValue;
            tilt.toOffset = new Vector3(0f, 0f, TiltDegrees);
            tilt.settings.duration = HoverDuration;
            tilt.settings.ease = Ease.OutQuad;
        }

        private static float SignedZ(RectTransform rect)
        {
            float z = rect.localEulerAngles.z;
            return z > 180f ? z - 360f : z;
        }

        [Test]
        public void PressDuringHoverTilt_RestoresRotation_UncoveredChannel()
        {
            // The stuck-tilt bug: hover tilts (rotate channel), press only scales. Pressing
            // stopped the tilt mid-flight without restoring rotation, and the later
            // return-to-Normal only reversed the PRESS animation — the button stayed
            // permanently tilted after click + mouse-away.
            EnableHoverTilt();
            Hover();
            Tick(HoverDuration); // fully tilted
            Assume.That(SignedZ(_rect), Is.EqualTo(TiltDegrees).Within(1e-2f),
                "precondition: hover tilted the button");

            Press();
            Assert.That(SignedZ(_rect), Is.EqualTo(0f).Within(1e-2f),
                "pressing must restore the rotation the press animation does not drive");

            Tick(PressDuration);
            Unhover();
            Tick(PressDuration * 2f);

            Assert.That(SignedZ(_rect), Is.EqualTo(0f).Within(1e-2f),
                "after click + mouse-away the button must not stay tilted");
            Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                "scale must also settle back at rest");
        }

        [Test]
        public void ReturnToRest_MidHover_RestoresRestPose()
        {
            // Clicking a button that navigates away disables it while still Highlighted/Pressed —
            // OnDisable routes through ReturnToRest so the view comes back untilted. (Lifecycle
            // methods don't run for AddComponent in EditMode, so the seam is exercised directly.)
            EnableHoverTilt();
            Hover();
            Tick(HoverDuration * 0.5f); // mid-flight: partially tilted and grown
            Assume.That(SignedZ(_rect), Is.GreaterThan(0.1f));
            Assume.That(_rect.localScale.x, Is.GreaterThan(1f));

            _animator.ReturnToRest();

            Assert.That(SignedZ(_rect), Is.EqualTo(0f).Within(1e-2f),
                "disabling mid-hover must restore the rest rotation");
            Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                "disabling mid-hover must restore the rest scale");
        }

        [Test]
        public void RepeatedClickCycles_NeverDrift()
        {
            // Full interaction loop hammered repeatedly — the symptom only emerged "after a while".
            for (int i = 0; i < 5; i++)
            {
                Hover();
                Tick(HoverDuration * 0.5f);
                Press();
                Tick(PressDuration * 0.5f);
                Select();
                Tick(PressDuration);
                Unhover();
                Tick(HoverDuration * 2f);
                Assert.That(_rect.localScale.x, Is.EqualTo(1f).Within(1e-3f),
                    $"cycle {i}: full hover→press→select→leave must always settle at rest scale 1");
            }
        }
    }
}
