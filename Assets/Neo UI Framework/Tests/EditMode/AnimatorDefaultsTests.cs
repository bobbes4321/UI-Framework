using System.Reflection;
using Neo.UI;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The project-default animation seam: roles resolve to presets, ApplyDefaultAnimation copies the
    /// configured preset into a slot (and no-ops when unset), and an animator's Reset() seeds its slots
    /// from the active settings asset's choices.
    /// </summary>
    public class AnimatorDefaultsTests
    {
        private NeoUISettings _settings;
        private NeoUISettings _previousInstance;
        private UIAnimationPreset _preset;

        [SetUp]
        public void SetUp()
        {
            _previousInstance = NeoUISettings.instance;
            _settings = ScriptableObject.CreateInstance<NeoUISettings>();
            _preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            _preset.presetName = "TestHover";
            _preset.category = "Hover";
            _preset.animation.scale.enabled = true;
            _preset.animation.scale.toReference = ReferenceValue.CustomValue;
            _preset.animation.scale.toCustomValue = new Vector3(1.2f, 1.2f, 1f);
            NeoUISettings.instance = _settings;
        }

        [TearDown]
        public void TearDown()
        {
            NeoUISettings.instance = _previousInstance;
            Object.DestroyImmediate(_preset);
            Object.DestroyImmediate(_settings);
        }

        [Test]
        public void SetAndGetDefault_RoundTrips_AndUpdatesInPlace()
        {
            Assert.IsFalse(_settings.TryGetDefaultAnimation(NeoAnimatorRoles.ButtonHover, out _));

            _settings.SetDefaultAnimation(NeoAnimatorRoles.ButtonHover, _preset);
            Assert.IsTrue(_settings.TryGetDefaultAnimation(NeoAnimatorRoles.ButtonHover, out UIAnimationPreset got));
            Assert.AreSame(_preset, got);

            // Setting the same role again updates in place — no duplicate entry.
            _settings.SetDefaultAnimation(NeoAnimatorRoles.ButtonHover, _preset);
            Assert.AreEqual(1, _settings.animatorDefaults.Count);
        }

        [Test]
        public void ApplyDefaultAnimation_CopiesWhenConfigured_NoopOtherwise()
        {
            var target = new UIAnimation();
            Assert.IsFalse(NeoUISettings.ApplyDefaultAnimation(NeoAnimatorRoles.ButtonHover, target),
                "no default configured → false, target untouched");
            Assert.IsFalse(target.scale.enabled);

            _settings.SetDefaultAnimation(NeoAnimatorRoles.ButtonHover, _preset);
            Assert.IsTrue(NeoUISettings.ApplyDefaultAnimation(NeoAnimatorRoles.ButtonHover, target));
            Assert.IsTrue(target.scale.enabled);
            Assert.AreEqual(new Vector3(1.2f, 1.2f, 1f), target.scale.toCustomValue,
                "the configured preset is copied into the slot");
        }

        [Test]
        public void SelectableAnimator_Reset_SeedsHoverFromDefault()
        {
            _settings.SetDefaultAnimation(NeoAnimatorRoles.ButtonHover, _preset);
            var go = new GameObject("Btn", typeof(RectTransform));
            try
            {
                var animator = go.AddComponent<UISelectableUIAnimator>();
                // AddComponent does not fire Reset from code — invoke the protected hook directly.
                InvokeReset(animator);
                Assert.IsTrue(animator.highlightedAnimation.scale.enabled,
                    "Reset seeds the hover slot from the configured Button/Hover default");
                Assert.AreEqual(new Vector3(1.2f, 1.2f, 1f), animator.highlightedAnimation.scale.toCustomValue);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Roles_BuiltInsPresent_AndRegisterIsIdempotent()
        {
            Assert.IsTrue(NeoAnimatorRoles.TryGet(NeoAnimatorRoles.ButtonHover, out _));
            int before = NeoAnimatorRoles.All.Count;
            NeoAnimatorRoles.Register(new NeoAnimatorRole(NeoAnimatorRoles.ButtonHover, "dup", "dup"));
            Assert.AreEqual(before, NeoAnimatorRoles.All.Count, "registering an existing id is a no-op");
        }

        private static void InvokeReset(object component)
        {
            MethodInfo reset = component.GetType().GetMethod("Reset",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(reset, "animator must define a Reset hook");
            reset.Invoke(component, null);
        }
    }
}
