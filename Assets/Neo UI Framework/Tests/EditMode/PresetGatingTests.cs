using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Task 3.2: the pure enable/disable gate shared by the widget-root inspectors' Preset section
    /// (<c>PresetWorkflowGUI</c>) and the scene-view overlay's preset row (<c>NeoSceneOverlay</c>).
    /// Both surfaces derive "can I preset this?" from this one definition, so it is worth pinning:
    /// hasWidget follows "is this a recognized widget spec", hasPreset follows "is it already linked".
    /// </summary>
    public class PresetGatingTests
    {
        [Test]
        public void NullSpec_GatesEverythingOff()
        {
            var gating = new PresetGating(null);
            Assert.IsFalse(gating.hasWidget, "a null export is not a preset-able widget");
            Assert.IsFalse(gating.hasPreset);
            Assert.IsNull(gating.PresetName);
        }

        [Test]
        public void UnlinkedWidget_EnablesApplyAndCreateOnly()
        {
            var gating = new PresetGating(new ElementSpec { kind = "button", preset = null });
            Assert.IsTrue(gating.hasWidget, "a recognized widget enables Apply + Create From Widget");
            Assert.IsFalse(gating.hasPreset, "with no linked preset, Update + Reset stay disabled");
            Assert.IsNull(gating.PresetName);
        }

        [Test]
        public void EmptyPresetName_CountsAsUnlinked()
        {
            var gating = new PresetGating(new ElementSpec { kind = "button", preset = "" });
            Assert.IsTrue(gating.hasWidget);
            Assert.IsFalse(gating.hasPreset, "an empty preset string is not a real link");
        }

        [Test]
        public void LinkedWidget_EnablesAllFourActions()
        {
            var gating = new PresetGating(new ElementSpec { kind = "button", preset = "Primary Button" });
            Assert.IsTrue(gating.hasWidget);
            Assert.IsTrue(gating.hasPreset, "a linked preset enables Update + Reset too");
            Assert.AreEqual("Primary Button", gating.PresetName);
        }
    }
}
