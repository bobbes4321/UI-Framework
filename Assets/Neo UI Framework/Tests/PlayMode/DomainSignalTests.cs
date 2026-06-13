using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Plan 3 B: a toggle/slider/dropdown with a domain <c>signal</c> publishes its typed value on
    /// that stream IN ADDITION to its standard "…/Behaviour" stream — so game code subscribes with
    /// <c>Signals.On&lt;T&gt;</c> directly, while flow triggers (which listen on the standard stream)
    /// keep working. Additive, never a replacement.
    /// </summary>
    public class DomainSignalTests
    {
        private readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object obj in _cleanup) if (obj != null) Object.Destroy(obj);
            _cleanup.Clear();
            Signals.ClearAll();
        }

        private GameObject Track(GameObject go) { _cleanup.Add(go); return go; }

        [UnityTest]
        public IEnumerator Toggle_DomainSignal_FiresAlongsideStandardStream()
        {
            bool domainFired = false, domainValue = false, standardFired = false;
            Signals.On<bool>("Audio", "Muted", v => { domainFired = true; domainValue = v; });
            Signals.On<ToggleSignalData>(UIToggle.StreamCategory, UIToggle.StreamName,
                d => { if (d.category == "Audio" && d.toggleName == "Mute") standardFired = true; });

            var go = Track(new GameObject("Toggle", typeof(RectTransform)));
            var toggle = go.AddComponent<UIToggle>();
            toggle.id = new ToggleId("Audio", "Mute");
            toggle.domainSignal = new StreamId("Audio", "Muted");
            yield return null; // OnEnable

            toggle.SetIsOn(true, animateChange: false);

            Assert.IsTrue(domainFired, "Signals.On<bool>('Audio','Muted') must fire directly — no ToggleSignalData branching");
            Assert.IsTrue(domainValue, "the payload is the new bool value");
            Assert.IsTrue(standardFired, "the standard ToggleSignalData stream (flow triggers) still fires");
        }

        [UnityTest]
        public IEnumerator Toggle_WithoutDomainSignal_OnlyFiresStandardStream()
        {
            bool domainFired = false, standardFired = false;
            Signals.On<bool>("Audio", "Muted", _ => domainFired = true);
            Signals.On<ToggleSignalData>(UIToggle.StreamCategory, UIToggle.StreamName, _ => standardFired = true);

            var go = Track(new GameObject("Toggle", typeof(RectTransform)));
            var toggle = go.AddComponent<UIToggle>();
            toggle.id = new ToggleId("Audio", "Mute"); // no domainSignal assigned (stays default)
            yield return null;

            toggle.SetIsOn(true, animateChange: false);

            Assert.IsFalse(domainFired, "no domain signal id = no extra stream (back-compat)");
            Assert.IsTrue(standardFired, "standard stream is unconditional");
        }

        [UnityTest]
        public IEnumerator Slider_DomainSignal_FiresFloatOnCommit()
        {
            float got = float.NaN;
            Signals.On<float>("Audio", "MusicVolume", v => got = v);

            var go = Track(new GameObject("Slider", typeof(RectTransform)));
            var slider = go.AddComponent<UISlider>();
            slider.id = new SliderId("Audio", "Volume");
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.domainSignal = new StreamId("Audio", "MusicVolume");
            yield return null;

            slider.value = 0.7f; // a change outside an active drag commits immediately

            Assert.AreEqual(0.7f, got, 0.001f, "Signals.On<float> receives the committed value");
        }

        [UnityTest]
        public IEnumerator Dropdown_DomainSignal_FiresIndexOnChange()
        {
            int got = -1;
            Signals.On<int>("Graphics", "Quality", v => got = v);

            var go = Track(new GameObject("Dropdown", typeof(RectTransform)));
            var dropdown = go.AddComponent<UIDropdown>();
            dropdown.id = new DropdownId("Graphics", "QualityControl");
            dropdown.domainSignal = new StreamId("Graphics", "Quality");
            dropdown.SetStringOptions(new[] { "Low", "High" });
            yield return null;

            dropdown.value = 1;

            Assert.AreEqual(1, got, "Signals.On<int> receives the selected index");
        }
    }
}
