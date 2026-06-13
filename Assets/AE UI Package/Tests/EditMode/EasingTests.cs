using System;
using AlterEyes.UI;
using NUnit.Framework;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    public class EasingTests
    {
        [Test]
        public void AllEases_StartAtZero_EndAtOne()
        {
            foreach (Ease ease in (Ease[])Enum.GetValues(typeof(Ease)))
            {
                Assert.That(Easing.Evaluate(ease, 0f), Is.EqualTo(0f).Within(1e-4f), $"{ease}(0) should be 0");
                Assert.That(Easing.Evaluate(ease, 1f), Is.EqualTo(1f).Within(1e-4f), $"{ease}(1) should be 1");
            }
        }

        [Test]
        public void Linear_IsIdentity()
        {
            for (float t = 0f; t <= 1f; t += 0.1f)
                Assert.That(Easing.Evaluate(Ease.Linear, t), Is.EqualTo(t).Within(1e-5f));
        }

        [Test]
        public void InQuad_MatchesFormula()
        {
            Assert.That(Easing.Evaluate(Ease.InQuad, 0.5f), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(Easing.Evaluate(Ease.OutQuad, 0.5f), Is.EqualTo(0.75f).Within(1e-5f));
        }

        [Test]
        public void OutBack_Overshoots()
        {
            bool overshot = false;
            for (float t = 0.05f; t < 1f; t += 0.05f)
                if (Easing.Evaluate(Ease.OutBack, t) > 1f) overshot = true;
            Assert.IsTrue(overshot, "OutBack should overshoot past 1 mid-curve");
        }

        [Test]
        public void OutBounce_StaysWithinExpectedRange()
        {
            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                float value = Easing.Evaluate(Ease.OutBounce, t);
                Assert.That(value, Is.InRange(-0.01f, 1.01f), $"OutBounce({t}) out of range: {value}");
            }
        }

        [Test]
        public void MonotonicEases_AreMonotonic()
        {
            foreach (Ease ease in new[] { Ease.InSine, Ease.OutSine, Ease.InOutCubic, Ease.InExpo, Ease.OutCirc, Ease.InOutQuint })
            {
                float previous = 0f;
                for (float t = 0f; t <= 1.0001f; t += 0.02f)
                {
                    float value = Easing.Evaluate(ease, Mathf.Clamp01(t));
                    Assert.That(value, Is.GreaterThanOrEqualTo(previous - 1e-4f), $"{ease} should be monotonic at t={t}");
                    previous = value;
                }
            }
        }

        [Test]
        public void AnimationCurveMode_UsesCurve()
        {
            var settings = new TweenSettings
            {
                easeMode = EaseMode.AnimationCurve,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 0.5f)
            };
            Assert.That(settings.Evaluate(1f), Is.EqualTo(0.5f).Within(1e-4f));
        }
    }
}
