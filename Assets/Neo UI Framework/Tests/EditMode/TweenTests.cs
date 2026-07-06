using System.Collections.Generic;
using System.Text.RegularExpressions;
using Neo.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    public class TweenTests
    {
        private float _value;
        private FloatTween _tween;

        [SetUp]
        public void SetUp()
        {
            UITick.Clear();
            TweenPool.Clear();
            _value = 0f;
            _tween = new FloatTween();
            _tween.SetTarget(() => _value, v => _value = v);
            _tween.settings.ease = Ease.Linear;
            _tween.settings.duration = 1f;
        }

        [TearDown]
        public void TearDown() => UITick.Clear();

        private void Tick(float dt) => UITick.Tick(dt);

        [Test]
        public void Play_AnimatesFromTo_AndFinishes()
        {
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play();

            Assert.AreEqual(TweenState.Playing, _tween.state);
            Tick(0.5f);
            Assert.That(_value, Is.EqualTo(5f).Within(1e-4f));
            Tick(0.5f);
            Assert.That(_value, Is.EqualTo(10f).Within(1e-4f));
            Assert.AreEqual(TweenState.Idle, _tween.state);
            Assert.AreEqual(0, UITick.count, "finished tween should unregister from the ticker");
        }

        [Test]
        public void Callbacks_FireInOrder()
        {
            var log = new List<string>();
            _tween.onPlay = () => log.Add("play");
            _tween.onUpdate = _ => { if (!log.Contains("update")) log.Add("update"); };
            _tween.onStop = () => log.Add("stop");
            _tween.onFinish = () => log.Add("finish");

            _tween.SetFrom(0f);
            _tween.SetTo(1f);
            _tween.Play();
            Tick(0.4f);
            Tick(0.7f);

            CollectionAssert.AreEqual(new[] { "play", "update", "stop", "finish" }, log);
        }

        [Test]
        public void Stop_DoesNotFireFinish()
        {
            bool finished = false, stopped = false;
            _tween.onFinish = () => finished = true;
            _tween.onStop = () => stopped = true;

            _tween.SetFrom(0f);
            _tween.SetTo(1f);
            _tween.Play();
            Tick(0.3f);
            _tween.Stop();

            Assert.IsTrue(stopped);
            Assert.IsFalse(finished);
            Assert.AreEqual(TweenState.Idle, _tween.state);
        }

        [Test]
        public void StartDelay_DelaysPlayback()
        {
            _tween.settings.startDelay = 0.5f;
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play();

            Assert.AreEqual(TweenState.StartDelay, _tween.state);
            Tick(0.25f);
            Assert.AreEqual(TweenState.StartDelay, _tween.state);
            Tick(0.5f); // 0.25 overflow into playback
            Assert.AreEqual(TweenState.Playing, _tween.state);
            Assert.That(_value, Is.EqualTo(2.5f).Within(1e-3f));
        }

        [Test]
        public void Reverse_MidFlight_KeepsPlayhead()
        {
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play();
            Tick(0.6f);
            Assert.That(_value, Is.EqualTo(6f).Within(1e-4f));

            _tween.Reverse();
            Assert.AreEqual(PlayDirection.Reverse, _tween.direction);
            Tick(0.3f);
            Assert.That(_value, Is.EqualTo(3f).Within(1e-4f));
            Tick(0.4f);
            Assert.That(_value, Is.EqualTo(0f).Within(1e-4f));
            Assert.AreEqual(TweenState.Idle, _tween.state);
        }

        [Test]
        public void PlayReverse_StartsAtEnd()
        {
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play(PlayDirection.Reverse);
            Tick(0.25f);
            Assert.That(_value, Is.EqualTo(7.5f).Within(1e-4f));
        }

        [Test]
        public void Loops_RepeatAndFireOnLoop()
        {
            int loops = 0;
            _tween.settings.loops = 2;
            _tween.onLoop = () => loops++;
            _tween.SetFrom(0f);
            _tween.SetTo(1f);
            _tween.Play();

            Tick(1f); // end of first pass → loop 1
            Assert.AreEqual(1, loops);
            Assert.AreEqual(TweenState.Playing, _tween.state);
            Tick(1f); // loop 2
            Assert.AreEqual(2, loops);
            Tick(1f); // finished
            Assert.AreEqual(TweenState.Idle, _tween.state);
        }

        [Test]
        public void LoopDelay_WaitsBetweenLoops()
        {
            _tween.settings.loops = 1;
            _tween.settings.loopDelay = 0.5f;
            _tween.SetFrom(0f);
            _tween.SetTo(1f);
            _tween.Play();

            Tick(1f);
            Assert.AreEqual(TweenState.LoopDelay, _tween.state);
            Tick(0.5f);
            Assert.AreEqual(TweenState.Playing, _tween.state);
        }

        [Test]
        public void PingPong_AlternatesDirection()
        {
            _tween.settings.playMode = TweenPlayMode.PingPong;
            _tween.settings.loops = 1;
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play();

            Tick(1f); // reached To, loop → direction flips
            Assert.AreEqual(PlayDirection.Reverse, _tween.direction);
            Tick(0.5f);
            Assert.That(_value, Is.EqualTo(5f).Within(1e-4f));
            Tick(0.5f);
            Assert.That(_value, Is.EqualTo(0f).Within(1e-4f), "ping-pong should return to From");
            Assert.AreEqual(TweenState.Idle, _tween.state);
        }

        [Test]
        public void PauseAndResume_Work()
        {
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play();
            Tick(0.3f);
            _tween.Pause();
            Assert.AreEqual(TweenState.Paused, _tween.state);
            Tick(5f);
            Assert.That(_value, Is.EqualTo(3f).Within(1e-4f), "paused tween should not advance");
            _tween.Resume();
            Tick(0.2f);
            Assert.That(_value, Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void SetProgressAt_AppliesValueWithoutPlaying()
        {
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.SetProgressAt(0.5f);
            Assert.That(_value, Is.EqualTo(5f).Within(1e-4f));
            Assert.AreEqual(TweenState.Idle, _tween.state);

            _tween.SetProgressAtOne();
            Assert.That(_value, Is.EqualTo(10f).Within(1e-4f));
            _tween.SetProgressAtZero();
            Assert.That(_value, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void PlayToProgress_PlaysPartialRange()
        {
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.SetProgressAtZero();
            _tween.PlayToProgress(0.5f);
            Tick(0.25f);
            Assert.That(_value, Is.EqualTo(2.5f).Within(1e-3f));
            Tick(0.25f);
            Assert.That(_value, Is.EqualTo(5f).Within(1e-3f));
            Assert.AreEqual(TweenState.Idle, _tween.state, "should finish at target progress");
        }

        [Test]
        public void CurrentValueReference_ResolvesAtPlayTime()
        {
            _value = 4f;
            _tween.fromReferenceValue = ReferenceValue.CurrentValue;
            _tween.SetTo(10f);
            _tween.Play();
            Assert.That(_tween.fromValue, Is.EqualTo(4f).Within(1e-4f));
        }

        [Test]
        public void StartValueReference_UsesCapturedStart()
        {
            _value = 7f;
            _tween.CaptureStartValue();
            _value = 99f;
            _tween.fromReferenceValue = ReferenceValue.StartValue;
            _tween.SetTo(10f);
            _tween.Play();
            Assert.That(_tween.fromValue, Is.EqualTo(7f).Within(1e-4f));
        }

        [Test]
        public void Spring_ReturnsToFromValue()
        {
            _tween.settings.playMode = TweenPlayMode.Spring;
            _tween.settings.duration = 0.5f;
            _tween.SetFrom(5f);
            _tween.SetTo(2f); // oscillation extent
            _tween.Play();

            for (int i = 0; i < 60 && _tween.isActive; i++) Tick(0.02f);
            Assert.AreEqual(TweenState.Idle, _tween.state);
            Assert.That(_value, Is.EqualTo(5f).Within(1e-3f), "spring should settle back at From");
        }

        [Test]
        public void Shake_ReturnsToFromValue()
        {
            _tween.settings.playMode = TweenPlayMode.Shake;
            _tween.settings.duration = 0.5f;
            _tween.SetFrom(3f);
            _tween.SetTo(1f);
            _tween.Play();

            for (int i = 0; i < 60 && _tween.isActive; i++) Tick(0.02f);
            Assert.AreEqual(TweenState.Idle, _tween.state);
            Assert.That(_value, Is.EqualTo(3f).Within(1e-3f), "shake should settle back at From");
        }

        [Test]
        public void RandomDuration_IsReRolledWithinRange()
        {
            _tween.settings.useRandomDuration = true;
            _tween.settings.randomDuration = new Vector2(0.5f, 1.5f);
            _tween.SetFrom(0f);
            _tween.SetTo(1f);

            for (int i = 0; i < 10; i++)
            {
                _tween.Play();
                Assert.That(_tween.currentDuration, Is.InRange(0.5f, 1.5f));
                _tween.Stop(silent: true);
            }
        }

        [Test]
        public void Pool_RecyclesInstances()
        {
            var pooled = TweenPool.Get<FloatTween>();
            TweenPool.Release(pooled);
            Assert.AreEqual(1, TweenPool.CountPooled<FloatTween>());
            Assert.IsTrue(pooled.isPooled);

            var reused = TweenPool.Get<FloatTween>();
            Assert.AreSame(pooled, reused);
            Assert.AreEqual(TweenState.Idle, reused.state);
            Assert.AreEqual(0, TweenPool.CountPooled<FloatTween>());
        }

        [Test]
        public void Finish_JumpsToEndAndFiresCallbacks()
        {
            bool finished = false;
            _tween.onFinish = () => finished = true;
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play();
            Tick(0.2f);
            _tween.Finish();

            Assert.That(_value, Is.EqualTo(10f).Within(1e-4f));
            Assert.IsTrue(finished);
            Assert.AreEqual(TweenState.Idle, _tween.state);
        }

        [Test]
        public void InfiniteLoops_KeepPlaying()
        {
            _tween.settings.loops = TweenSettings.InfiniteLoops;
            _tween.SetFrom(0f);
            _tween.SetTo(1f);
            _tween.Play();
            for (int i = 0; i < 20; i++) Tick(1f);
            Assert.IsTrue(_tween.isActive, "infinite-loop tween should still be active");
            _tween.Stop();
        }

        [Test]
        public void LifetimeOwnerDestroyed_SelfStopsInsteadOfThrowing()
        {
            var owner = new GameObject("tween owner");
            try
            {
                _tween.SetTarget(owner, () => _value, v => _value = v);
                _tween.SetFrom(0f);
                _tween.SetTo(10f);
                _tween.Play();
                Tick(0.3f);
                Assert.That(_value, Is.EqualTo(3f).Within(1e-4f));

                Object.DestroyImmediate(owner);
                Tick(0.3f); // the regression: this used to keep invoking the setter on a dead target

                Assert.AreEqual(TweenState.Idle, _tween.state, "orphaned tween should self-stop");
                Assert.AreEqual(0, UITick.count, "orphaned tween should unregister from the ticker");
                Assert.That(_value, Is.EqualTo(3f).Within(1e-4f), "no value writes after the owner died");
            }
            finally
            {
                if (owner != null) Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void Reset_ClearsLifetimeOwner()
        {
            var owner = new GameObject("tween owner");
            try
            {
                _tween.SetTarget(owner, () => _value, v => _value = v);
                _tween.Reset();
                Object.DestroyImmediate(owner);

                _tween.SetTarget(() => _value, v => _value = v);
                _tween.settings.duration = 1f;
                _tween.settings.ease = Ease.Linear; // Reset() replaced the settings from SetUp
                _tween.SetFrom(0f);
                _tween.SetTo(10f);
                _tween.Play();
                Tick(0.5f);
                Assert.That(_value, Is.EqualTo(5f).Within(1e-4f), "a recycled tween must not inherit the old owner");
            }
            finally
            {
                if (owner != null) Object.DestroyImmediate(owner);
            }
        }

        private class ThrowingTickable : ITickable
        {
            public int ticks;
            public void Tick(float deltaTime)
            {
                ticks++;
                throw new System.InvalidOperationException("tickable boom");
            }
        }

        [Test]
        public void UITick_ThrowingTickable_IsDroppedAndOthersStillTick()
        {
            var throwing = new ThrowingTickable();
            UITick.Register(throwing); // registered FIRST so it throws before the tween's turn
            _tween.SetFrom(0f);
            _tween.SetTo(10f);
            _tween.Play();

            LogAssert.Expect(LogType.Exception, new Regex("tickable boom"));
            LogAssert.Expect(LogType.Warning, new Regex("Unregistered tickable"));
            Tick(0.25f);

            Assert.IsFalse(UITick.IsRegistered(throwing), "a throwing tickable must be dropped from the loop");
            Assert.That(_value, Is.EqualTo(2.5f).Within(1e-4f), "later tickables must still tick");

            Tick(0.25f); // no further exception expected — the offender ticked exactly once
            Assert.AreEqual(1, throwing.ticks);
        }
    }
}
