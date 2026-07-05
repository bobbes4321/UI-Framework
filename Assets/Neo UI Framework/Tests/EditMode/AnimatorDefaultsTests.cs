using System.Reflection;
using System.Text.RegularExpressions;
using Neo.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void Roles_BuiltInsPresent()
        {
            Assert.IsTrue(NeoAnimatorRoles.TryGet(NeoAnimatorRoles.ButtonHover, out _));
            // The per-state picker maps every selectable state to a role — these must always exist.
            Assert.IsTrue(NeoAnimatorRoles.TryGet(NeoAnimatorRoles.SelectableNormal, out _));
            Assert.IsTrue(NeoAnimatorRoles.TryGet(NeoAnimatorRoles.SelectableSelected, out _));
            Assert.IsTrue(NeoAnimatorRoles.TryGet(NeoAnimatorRoles.SelectableDisabled, out _));
        }

        [Test]
        public void CopyTo_StampsSourcePreset()
        {
            var target = new UIAnimation();
            Assert.IsTrue(string.IsNullOrEmpty(target.sourcePreset), "a hand-built animation has no source");

            _preset.CopyTo(target);
            Assert.AreEqual("Hover/TestHover", target.sourcePreset,
                "CopyTo stamps the preset full name so the inspector can show what's applied");
        }

        [Test]
        public void SelectableAnimator_Reset_SeedsSelectedAndDisabledFromDefaults()
        {
            _settings.SetDefaultAnimation(NeoAnimatorRoles.SelectableSelected, _preset);
            _settings.SetDefaultAnimation(NeoAnimatorRoles.SelectableDisabled, _preset);
            var go = new GameObject("Btn", typeof(RectTransform));
            try
            {
                var animator = go.AddComponent<UISelectableUIAnimator>();
                InvokeReset(animator);
                Assert.IsTrue(animator.selectedAnimation.scale.enabled,
                    "Reset seeds the selected slot from the Selectable/Selected default");
                Assert.IsTrue(animator.disabledAnimation.scale.enabled,
                    "Reset seeds the disabled slot from the Selectable/Disabled default");
                Assert.IsFalse(animator.normalAnimation.scale.enabled,
                    "an unset role leaves its slot untouched");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Register_SameId_WarnsThenReplaces_NeverDuplicates()
        {
            int before = NeoAnimatorRoles.All.Count;
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex($"NeoAnimatorRoles: role '{Regex.Escape(NeoAnimatorRoles.ButtonHover)}' is already registered"));
                NeoAnimatorRoles.Register(new NeoAnimatorRole(NeoAnimatorRoles.ButtonHover, "dup", "dup"));

                Assert.AreEqual(before, NeoAnimatorRoles.All.Count, "same-id registration replaces, never duplicates");
                Assert.IsTrue(NeoAnimatorRoles.TryGet(NeoAnimatorRoles.ButtonHover, out NeoAnimatorRole got));
                Assert.AreEqual("dup", got.DisplayName, "the later registration wins");
            }
            finally
            {
                // Restore the built-in so sibling tests see the normal Button/Hover role again.
                NeoAnimatorRoles.ResetForTests();
            }
        }

        [Test]
        public void Register_NullOrEmptyId_WarnsAndIgnores_NeverThrows()
        {
            LogAssert.Expect(LogType.Warning, new Regex("NeoAnimatorRoles: ignored a null/invalid entry"));
            Assert.DoesNotThrow(() => NeoAnimatorRoles.Register(null));

            LogAssert.Expect(LogType.Warning, new Regex("NeoAnimatorRoles: ignored a null/invalid entry"));
            Assert.DoesNotThrow(() => NeoAnimatorRoles.Register(new NeoAnimatorRole("", "blank", "blank")));
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
