using System;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 2.3 — the Presets tab (real editor) and its shared <see cref="WidgetPresetGUI"/> drawer.
    /// These cover the non-IMGUI contract only (the layout paths need a live OnGUI context): the drawer's
    /// null-safety transaction contract and the tab's disposable per-window state. The card grid / create
    /// flow render paths are exercised interactively / by the orchestrator's window smoke pass.
    /// </summary>
    public class PresetsTabTests
    {
        [Test]
        public void WidgetPresetGUI_Draw_NullSerializedObject_DoesNotThrow()
        {
            // The shared drawer must tolerate a null target (no selection) — same guard as AnimationPresetGUI.
            Assert.DoesNotThrow(() => WidgetPresetGUI.Draw(null));
        }

        [Test]
        public void CreateState_ReturnsDisposableState_WithSaneDefaults()
        {
            object state = PresetsTab.CreateState();
            Assert.IsNotNull(state, "Presets tab must supply a per-window state object.");
            Assert.IsInstanceOf<IDisposable>(state, "State must be disposable so the window releases its cache.");

            var s = (PresetsTab.State)state;
            Assert.AreEqual("All", s.kindFilter, "Kind filter starts on 'All'.");
            Assert.IsNull(s.selected, "Nothing is selected on a fresh tab.");
            Assert.IsNull(s.so, "No SerializedObject is cached until a preset is selected.");

            // Dispose is safe with no cached SerializedObject and an empty thumbnail cache.
            Assert.DoesNotThrow(() => ((IDisposable)state).Dispose());
        }
    }
}
