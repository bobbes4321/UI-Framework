using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The "color is runtime-driven, don't hand-edit it" detection seam behind the inspector notices:
    /// NeoColorDrivers.TryDescribe must flag every built-in driver kind on the driven graphic's own
    /// GameObject, stay quiet when a driver is present but inert (channel disabled, apply flag off),
    /// and accept a project-registered custom driver descriptor.
    /// </summary>
    public class ColorDriverNoticeTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ColorDriverNoticeTests");
            _go.AddComponent<NeoShape>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void PlainShape_IsNotDriven()
        {
            Assert.IsFalse(NeoColorDrivers.TryDescribe(_go, out _, out _));
        }

        [Test]
        public void NoColorTarget_IsNotDriven()
        {
            var bare = new GameObject("no-target");
            try
            {
                bare.AddComponent<UIToggleColorAnimator>();
                Assert.IsFalse(NeoColorDrivers.TryDescribe(bare, out _, out _));
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }

        [Test]
        public void ToggleColorAnimator_DrivesTheShape()
        {
            _go.AddComponent<UIToggleColorAnimator>();

            Assert.IsTrue(NeoColorDrivers.TryDescribe(_go, out string drivers, out Component driven));
            StringAssert.Contains("Toggle Color Animator", drivers);
            Assert.AreSame(_go.GetComponent<NeoShape>(), driven);
        }

        [Test]
        public void SelectableColorAnimator_DrivesTheShape()
        {
            _go.AddComponent<UISelectableColorAnimator>();
            Assert.IsTrue(NeoColorDrivers.TryDescribe(_go, out string drivers, out _));
            StringAssert.Contains("Selectable Color Animator", drivers);
        }

        [Test]
        public void ThemeColorTarget_DrivesOnlyWithAToken()
        {
            ThemeColorTarget target = _go.AddComponent<ThemeColorTarget>();
            Assert.IsFalse(NeoColorDrivers.TryDescribe(_go, out _, out _));

            target.token = "Primary";
            Assert.IsTrue(NeoColorDrivers.TryDescribe(_go, out string drivers, out _));
            StringAssert.Contains("Primary", drivers);
        }

        [Test]
        public void ThemeShapeStyleTarget_HonorsApplyFillColor()
        {
            ThemeShapeStyleTarget target = _go.AddComponent<ThemeShapeStyleTarget>();
            target.style = "Card";
            target.applyFillColor = false;
            Assert.IsFalse(NeoColorDrivers.TryDescribe(_go, out _, out _));

            target.applyFillColor = true;
            Assert.IsTrue(NeoColorDrivers.TryDescribe(_go, out string drivers, out _));
            StringAssert.Contains("Card", drivers);
        }

        [Test]
        public void SelectableUIAnimator_DrivesOnlyWithAnEnabledColorChannel()
        {
            UISelectableUIAnimator animator = _go.AddComponent<UISelectableUIAnimator>();
            Assert.IsFalse(NeoColorDrivers.TryDescribe(_go, out _, out _));

            animator.pressedAnimation.color.enabled = true;
            Assert.IsTrue(NeoColorDrivers.TryDescribe(_go, out string drivers, out _));
            StringAssert.Contains("color channel", drivers);
        }

        [Test]
        public void TwoDrivers_BothAppearInTheDescription()
        {
            _go.AddComponent<UIToggleColorAnimator>();
            ThemeColorTarget target = _go.AddComponent<ThemeColorTarget>();
            target.token = "Accent";

            Assert.IsTrue(NeoColorDrivers.TryDescribe(_go, out string drivers, out _));
            StringAssert.Contains("Toggle Color Animator", drivers);
            StringAssert.Contains("Accent", drivers);
        }

        [Test]
        public void ProjectRegisteredDriver_IsDetected()
        {
            const string id = "test.customDriver";
            NeoColorDrivers.Register(new ColorDriverDescriptor
            {
                id = id,
                probe = c => c is BoxCollider ? "Test Custom Driver" : null
            });
            try
            {
                _go.AddComponent<BoxCollider>();
                Assert.IsTrue(NeoColorDrivers.TryDescribe(_go, out string drivers, out _));
                StringAssert.Contains("Test Custom Driver", drivers);
            }
            finally
            {
                NeoColorDrivers.Remove(id);
            }
        }
    }
}
