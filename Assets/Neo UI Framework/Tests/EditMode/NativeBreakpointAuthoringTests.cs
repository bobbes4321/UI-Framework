using System.Linq;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Wave 2 Task 2.3: native parity for the (doomed) Composer's <c>BreakpointBar</c>. Exercises
    /// <see cref="NeoSceneAuthoring.CaptureLayoutOverride"/> end to end against a real showcase — a
    /// developer drags/resizes a widget directly in the scene view, then "Capture Layout As Override"
    /// must (a) write the expected delta into <c>element.overrides[breakpoint]</c>, (b) leave any
    /// PRE-EXISTING override on a different element untouched, (c) restore the widget to its base
    /// layout (WYSIWYG), and (d) round-trip byte-identically through a regenerate → export.
    /// </summary>
    public class NativeBreakpointAuthoringTests
    {
        private const string ShowcaseId = "ztest-native-breakpoints";

        private const string SpecJson = @"{
          ""breakpoints"": [
            { ""name"": ""wide"", ""when"": { ""minWidth"": 900 } },
            { ""name"": ""narrow"", ""when"": { ""maxWidth"": 400 } }
          ],
          ""views"": [ { ""id"": ""Bp/Main"", ""elements"": [
            { ""panel"": {
                ""id"": ""Bp/Anchor"",
                ""layout"": { ""h"": ""left"", ""v"": ""top"",
                              ""offset"": { ""left"": 10, ""top"": 10 }, ""size"": { ""w"": 100, ""h"": 40 } },
                ""overrides"": { ""wide"": { ""offset"": { ""left"": 50 } } }
            } },
            { ""panel"": {
                ""id"": ""Bp/Card"",
                ""layout"": { ""h"": ""left"", ""v"": ""top"",
                              ""offset"": { ""left"": 24, ""top"": 48 }, ""size"": { ""w"": 200, ""h"": 100 } }
            } }
          ] } ]
        }";

        private NeoUISettings _settings;
        private Showcase _showcase;
        private GameObject _instance;

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
            _showcase = NeoCapture.CreateShowcase(ShowcaseId, "Breakpoint Capture Test", "Custom");

            GenerateReport report;
            using (NeoWorkspace.Scoped(_showcase))
                report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());

            using (NeoWorkspace.Scoped(_showcase))
            {
                string path = UISpecGenerator.ViewPrefabPath("Bp", "Main");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, "fixture sanity: the view prefab must exist");
                _instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
            AssetDatabase.DeleteAsset($"{ShowcaseRegistry.ShowcasesRoot}/{ShowcaseId}");
            AssetDatabase.DeleteAsset($"{ShowcaseRegistry.ShowcasesRoot}/Specs/{ShowcaseId}.json");
            ShowcaseRegistry.Remove(ShowcaseId);
            ShowcaseRegistry.InvalidateDiscovery();
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void CaptureLayoutOverride_WritesExpectedDelta_PreservesOtherOverrides_AndRestoresBase()
        {
            UIView view = _instance.GetComponent<UIView>();
            Transform cardTransform = _instance.transform.Find("Panel - Bp_Card");
            Assert.IsNotNull(cardTransform, "fixture sanity: the card element must exist");
            GameObject card = cardTransform.gameObject;
            var rect = (RectTransform)card.transform;
            var tag = card.GetComponent<NeoLayoutTag>();
            Assert.IsNotNull(tag, "fixture sanity: the card must be placed through the layout model");

            // Snapshot BASE the way the overlay does — on selection, before any drag.
            ElementSpec beforeExport = NeoSceneAuthoring.TryExportForPresetWorkflow(card);
            Assert.IsNotNull(beforeExport?.layout, "fixture sanity: the card must export a layout");
            LayoutSpec baseLayout = beforeExport.layout;

            // "Drag": move + resize the live rect (same h/v, only offsets/size change) via the same
            // public ConstraintLayout.Apply a real handle-drag ultimately expresses.
            var dragged = new LayoutSpec
            {
                h = "left",
                v = "top",
                offset = new LayoutOffset(),
                size = new LayoutSize { w = 260f, h = 140f }
            };
            dragged.offset.Set("left", 5f);
            dragged.offset.Set("top", 5f);
            ConstraintLayout.Apply(rect, dragged, parentLayout: null);

            SyncResult sr = NeoSceneAuthoring.CaptureLayoutOverride(card, view, _showcase, "narrow", baseLayout);

            Assert.IsNotNull(sr, "capture should have produced a result");
            Assert.IsFalse(sr.refused, $"capture should not refuse a clean widget: {sr.note}");
            Assert.IsTrue(sr.ok, $"capture should succeed: {sr.note}");
            Assert.IsNotNull(sr.merged);

            ElementSpec exportedCard = sr.merged.views.First(v => v.id == "Bp/Main").elements
                .First(e => e.id == "Bp/Card");
            Assert.IsNotNull(exportedCard.overrides, "the new override must round-trip");
            Assert.IsTrue(exportedCard.overrides.ContainsKey("narrow"), "the captured breakpoint must be present");
            LayoutSpec narrowDelta = exportedCard.overrides["narrow"];
            Assert.AreEqual(5f, narrowDelta.offset.GetOr("left", -1f), 1e-3f);
            Assert.AreEqual(5f, narrowDelta.offset.GetOr("top", -1f), 1e-3f);
            Assert.AreEqual(260f, narrowDelta.size.w.Value, 1e-3f);
            Assert.AreEqual(140f, narrowDelta.size.h.Value, 1e-3f);

            // A pre-existing override on a DIFFERENT element must survive untouched.
            ElementSpec exportedAnchor = sr.merged.views.First(v => v.id == "Bp/Main").elements
                .First(e => e.id == "Bp/Anchor");
            Assert.IsNotNull(exportedAnchor.overrides);
            Assert.IsTrue(exportedAnchor.overrides.ContainsKey("wide"), "the pre-existing override must survive");
            Assert.AreEqual(50f, exportedAnchor.overrides["wide"].offset.GetOr("left", -1f), 1e-3f);

            // WYSIWYG: the live rect must be restored to base, not left at the dragged values.
            LayoutSpec afterCapture = ConstraintLayout.Detect(rect, tag);
            Assert.AreEqual(baseLayout.offset.GetOr("left", -999f), afterCapture.offset.GetOr("left", -999f), 1e-3f);
            Assert.AreEqual(baseLayout.offset.GetOr("top", -999f), afterCapture.offset.GetOr("top", -999f), 1e-3f);
            Assert.AreEqual(baseLayout.size.w.Value, afterCapture.size.w.Value, 1e-3f);
            Assert.AreEqual(baseLayout.size.h.Value, afterCapture.size.h.Value, 1e-3f);

            // Round trip: regenerate from the merged spec and re-export — byte-identical.
            using (NeoWorkspace.Scoped(_showcase))
            {
                GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(sr.merged.ToJson()));
                Assert.IsEmpty(regen.issues, regen.ToString());
                UISpec reExported = UISpecExporter.ExportProject();
                Assert.AreEqual(sr.merged.ToJson(), reExported.ToJson(),
                    "a captured breakpoint override must round-trip byte-identically");
            }
        }

        [Test]
        public void CaptureLayoutOverride_NoChangeSinceSelection_WarnsAndReturnsNull()
        {
            UIView view = _instance.GetComponent<UIView>();
            Transform cardTransform = _instance.transform.Find("Panel - Bp_Card");
            GameObject card = cardTransform.gameObject;

            ElementSpec captured = NeoSceneAuthoring.TryExportForPresetWorkflow(card);
            LayoutSpec baseLayout = captured.layout;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*nothing to.*capture.*"));
            SyncResult sr = NeoSceneAuthoring.CaptureLayoutOverride(card, view, _showcase, "narrow", baseLayout);

            Assert.IsNull(sr, "capturing with nothing changed must no-op rather than write an empty override");
        }
    }
}
