using System.IO;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 2 of native authoring: a hand-built <see cref="UIView"/> captured into a showcase's spec +
    /// baseline through <see cref="NeoCapture"/>. Exercises the orchestration the existing
    /// Sync/Baseline suites don't: showcase creation, attribution resolution, and that a factory-built
    /// view captures cleanly (no off-spec refusal) and writes the human-readable spec file.
    /// </summary>
    public class NativeCaptureTests
    {
        private const string ShowcaseId = "ztest-native-capture";
        private NeoUISettings _settings;
        private GameObject _viewGo;

        [OneTimeSetUp]
        public void Setup()
        {
            _settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            if (_settings != null && _settings.theme != null)
            {
                StarterKitBootstrap.EnsureFactoryTokens(_settings.theme);
                StarterKitBootstrap.EnsureTextStyles(_settings.theme);
            }
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            if (_viewGo != null) Object.DestroyImmediate(_viewGo);
            AssetDatabase.DeleteAsset($"{ShowcaseRegistry.ShowcasesRoot}/{ShowcaseId}");
            AssetDatabase.DeleteAsset($"{ShowcaseRegistry.ShowcasesRoot}/Specs/{ShowcaseId}.json");
            ShowcaseRegistry.Remove(ShowcaseId);
            ShowcaseRegistry.InvalidateDiscovery();
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void CreateShowcase_RegistersAndWritesSpecFile()
        {
            Showcase showcase = NeoCapture.CreateShowcase(ShowcaseId, "Capture Test", "Custom");
            Assert.IsNotNull(showcase, "CreateShowcase returned null");
            Assert.AreEqual(ShowcaseId, showcase.id);
            Assert.IsTrue(ShowcaseRegistry.TryGet(ShowcaseId, out _), "new showcase should be discoverable");
            Assert.IsTrue(File.Exists(showcase.specPath), "spec file should be created");
        }

        [Test]
        public void CaptureView_FactoryBuiltView_CapturesCleanlyAndRoundTrips()
        {
            Showcase showcase = NeoCapture.CreateShowcase(ShowcaseId, "Capture Test", "Custom");

            // A view a developer "hand-built": a UIView root with a factory button dropped in live.
            var viewSpec = new ViewSpec { category = "Cap", viewName = "Main" };
            _viewGo = UISpecGenerator.BuildViewGameObject(viewSpec, _settings, new GenerateReport());
            UISpecGenerator.BuildElementLive(
                SpecFactory.NewElement("button"), (RectTransform)_viewGo.transform,
                _settings, new GenerateReport());
            var view = _viewGo.GetComponent<UIView>();

            // A purely factory-built view has no off-spec edits, so capture must succeed without force.
            SyncResult sr = NeoCapture.CaptureView(view, showcase, force: false);
            Assert.IsNotNull(sr);
            Assert.IsFalse(sr.refused, $"capture should not refuse a factory-built view: {sr.note}");
            Assert.IsTrue(sr.ok, $"capture should succeed: {sr.note}");

            // The merged spec must contain our view, and the human-readable spec file must be written.
            Assert.IsNotNull(sr.merged);
            Assert.IsTrue(sr.merged.views.Exists(v => v.category == "Cap" && v.viewName == "Main"),
                "captured spec should contain the hand-built view");
            string specText = File.ReadAllText(showcase.specPath);
            StringAssert.Contains("Main", specText, "spec file should be rewritten with the captured view");

            // Attribution: the capture stamped the view, so it now resolves home with no prompt.
            Assert.IsTrue(NeoCapture.TryResolveShowcase(view, out Showcase resolved));
            Assert.AreEqual(ShowcaseId, resolved.id);

            // Idempotent: capturing again finds no drift and still succeeds.
            SyncResult again = NeoCapture.CaptureView(view, showcase, force: false);
            Assert.IsTrue(again.ok, $"re-capture should be clean: {again.note}");
        }
    }
}
