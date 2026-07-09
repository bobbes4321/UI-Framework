using System.Text.RegularExpressions;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The palette window's spawn layer (<see cref="NeoSceneAuthoring"/>): a preset tile's LINKED spawn
    /// must let the preset drive the styling (the kind defaults <c>SpecFactory.NewElement</c> seeds —
    /// e.g. a button's <c>variant = "primary"</c> — used to shadow the preset at generate, so a "Ghost
    /// Button" tile spawned primary), and the DETACHED spawn (Doozy's "Clone" analog) must bake the
    /// preset's styling into the element with no preset link at all.
    /// </summary>
    public class PaletteSpawnTests
    {
        private NeoUISettings _settings;
        private GameObject _canvasRoot;
        private GameObject _viewRoot;
        private NeoWidgetPreset _preset;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            if (_settings != null && _settings.theme != null)
            {
                StarterKitBootstrap.EnsureFactoryTokens(_settings.theme);
                StarterKitBootstrap.EnsureTextStyles(_settings.theme);
            }
        }

        [SetUp]
        public void SetUp()
        {
            // CreateWidget resolves its parent through the selection→Canvas rules, so the view must
            // really live under a Canvas or the spawn would bootstrap (and leak) its own.
            _canvasRoot = new GameObject("PaletteTestCanvas", typeof(Canvas));
            var view = new ViewSpec { category = "PaletteSpawn", viewName = "V" };
            _viewRoot = UISpecGenerator.BuildViewGameObject(view, _settings, new GenerateReport());
            _viewRoot.transform.SetParent(_canvasRoot.transform, worldPositionStays: false);

            _preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            _preset.presetName = "PaletteTestGhost";
            _preset.targetKind = "button";
            _preset.variant = "ghost";
            _preset.sizeVariant = "lg";
            NeoWidgetPresets.Register(_preset);
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasRoot != null) Object.DestroyImmediate(_canvasRoot);
            NeoWidgetPresets.ResetForTests();
            if (_preset != null) Object.DestroyImmediate(_preset);
            Selection.activeGameObject = null;
        }

        private static bool InLayout(GameObject widget) =>
            ((RectTransform)widget.transform.parent).GetComponent<LayoutGroup>() != null;

        [Test]
        public void CreateWidget_WithPreset_LinksAndLetsThePresetDriveStyling()
        {
            GameObject widget = NeoSceneAuthoring.CreateWidget("button", "PaletteTestGhost", _viewRoot);

            Assert.IsNotNull(widget, "the linked preset spawn should build a widget");
            var tag = widget.GetComponent<WidgetPresetTag>();
            Assert.IsNotNull(tag, "a linked spawn must stamp the WidgetPresetTag link");
            Assert.AreEqual("PaletteTestGhost", tag.presetName);

            ElementSpec exported = UISpecExporter.ExportElement(widget, InLayout(widget));
            Assert.AreEqual("PaletteTestGhost", exported.preset, "the link must round-trip through export");
            Assert.IsTrue(string.IsNullOrEmpty(exported.variant),
                "the element must carry no variant override — NewElement's kind default ('primary') " +
                "used to shadow the preset's variant at generate");
        }

        [Test]
        public void CreateWidgetDetached_BakesStylingWithoutAPresetLink()
        {
            GameObject widget = NeoSceneAuthoring.CreateWidgetDetached("button", "PaletteTestGhost", _viewRoot);

            Assert.IsNotNull(widget, "the detached preset spawn should build a widget");
            var tag = widget.GetComponent<WidgetPresetTag>();
            Assert.IsTrue(tag == null || string.IsNullOrEmpty(tag.presetName),
                "a detached spawn must not link the preset");

            ElementSpec exported = UISpecExporter.ExportElement(widget, InLayout(widget));
            Assert.IsTrue(string.IsNullOrEmpty(exported.preset),
                "a detached spawn must not reference the preset in its exported spec");
            Assert.AreEqual("ghost", exported.variant,
                "the preset's styling must be baked into the element itself");
            Assert.AreEqual("lg", exported.sizeVariant,
                "the preset's size must be baked into the element itself");
        }

        [Test]
        public void CreateWidgetDetached_UnknownPreset_WarnsAndFallsBackToBareKind()
        {
            LogAssert.Expect(LogType.Warning, new Regex("not found"));
            GameObject widget = NeoSceneAuthoring.CreateWidgetDetached("button", "NoSuchPalettePreset", _viewRoot);
            Assert.IsNotNull(widget, "an unresolvable preset must still create the bare kind (never nothing)");
        }
    }
}
