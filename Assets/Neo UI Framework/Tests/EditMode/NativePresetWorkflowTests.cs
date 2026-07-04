using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Wave 2 Task 2.2: the preset Create/Update/Reset/Apply workflow ported from the (doomed) Composer's
    /// <c>SpecInspector</c> onto a LIVE scene widget — <see cref="NeoSceneAuthoring"/> captures the widget's
    /// spec via the same <see cref="UISpecExporter.ExportElement"/> seam <c>ApplyPreset</c> already used,
    /// then rebuilds it through <see cref="UISpecGenerator.BuildElementLive"/>, so a native widget's preset
    /// workflow round-trips exactly like a generated one's.
    /// </summary>
    public class NativePresetWorkflowTests
    {
        private const string ScratchFolder = "Assets/NeoUITestScratchPresetWorkflow";

        private NeoUISettings _settings;
        private GameObject _viewRoot;

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
            if (!AssetDatabase.IsValidFolder(ScratchFolder))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratchPresetWorkflow");

            var view = new ViewSpec { category = "PresetWorkflow", viewName = "V" };
            _viewRoot = UISpecGenerator.BuildViewGameObject(view, _settings, new GenerateReport());
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewRoot != null) Object.DestroyImmediate(_viewRoot);
            NeoWidgetPresets.ResetForTests();
            AssetDatabase.DeleteAsset(ScratchFolder);
            AssetDatabase.SaveAssets();
        }

        private GameObject CreateStyledButton(string label = "First")
        {
            var element = new ElementSpec { kind = "button", label = label, variant = "danger", sizeVariant = "lg" };
            GameObject go = UISpecGenerator.BuildElementLive(
                element, (RectTransform)_viewRoot.transform, _settings, new GenerateReport());
            Assert.IsNotNull(go, "test fixture: expected the styled button to build");
            return go;
        }

        private static bool InLayout(GameObject widget) =>
            ((RectTransform)widget.transform.parent).GetComponent<LayoutGroup>() != null;

        [Test]
        public void CreatePresetFromWidget_ProducesPresetWhoseFieldsMatchTheWidget()
        {
            GameObject widget = CreateStyledButton();
            string path = $"{ScratchFolder}/CapturedButton.asset";

            NeoWidgetPreset preset = NeoSceneAuthoring.CreatePresetFromWidget(widget, path);

            Assert.IsNotNull(preset, "CreatePresetFromWidget should have created a preset asset");
            Assert.AreEqual("button", preset.targetKind);
            Assert.AreEqual("danger", preset.variant, "the preset must capture the widget's variant");
            Assert.AreEqual("lg", preset.sizeVariant, "the preset must capture the widget's size");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<NeoWidgetPreset>(path),
                "the preset must be a real, discoverable asset on disk");

            // CreatePresetFromWidget relinks the source widget via ApplyPreset, which rebuilds it in
            // place (destroying the original instance) — read the surviving widget off Selection.
            GameObject relinkedWidget = Selection.activeGameObject;
            Assert.IsNotNull(relinkedWidget, "ApplyPreset should have selected the rebuilt widget");
            ElementSpec relinked = UISpecExporter.ExportElement(relinkedWidget, InLayout(relinkedWidget));
            Assert.AreEqual(preset.presetName, relinked.preset, "the widget must relink to its newly captured preset");
        }

        [Test]
        public void UpdatePresetFromWidget_PushesCurrentStylingIntoTheLinkedPreset()
        {
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            preset.presetName = "UpdateTestPreset";
            preset.targetKind = "button";
            preset.variant = "secondary";
            NeoWidgetPresets.Register(preset);
            try
            {
                var element = new ElementSpec
                {
                    kind = "button", label = "X", preset = preset.presetName, variant = "danger",
                };
                GameObject widget = UISpecGenerator.BuildElementLive(
                    element, (RectTransform)_viewRoot.transform, _settings, new GenerateReport());
                Assert.IsNotNull(widget);

                bool ok = NeoSceneAuthoring.UpdatePresetFromWidget(widget);

                Assert.IsTrue(ok, "UpdatePresetFromWidget should have succeeded for a linked widget");
                Assert.AreEqual("danger", preset.variant, "the preset asset must adopt the widget's overridden variant");
            }
            finally
            {
                NeoWidgetPresets.ResetForTests();
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void ResetWidgetToPreset_ClearsOverriddenFieldsOnTheCapturedSpec()
        {
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            preset.presetName = "ResetTestPreset";
            preset.targetKind = "button";
            preset.variant = "secondary";
            NeoWidgetPresets.Register(preset);
            try
            {
                var element = new ElementSpec
                {
                    kind = "button", label = "X", preset = preset.presetName, sizeVariant = "sm",
                };
                GameObject widget = UISpecGenerator.BuildElementLive(
                    element, (RectTransform)_viewRoot.transform, _settings, new GenerateReport());
                Assert.IsNotNull(widget);

                ElementSpec before = UISpecExporter.ExportElement(widget, InLayout(widget));
                Assert.AreEqual("sm", before.sizeVariant, "fixture sanity: the element overrides the preset's size before reset");

                GameObject rebuilt = NeoSceneAuthoring.ResetWidgetToPreset(widget);

                Assert.IsNotNull(rebuilt, "ResetWidgetToPreset should have rebuilt the widget");
                ElementSpec after = UISpecExporter.ExportElement(rebuilt, InLayout(rebuilt));
                Assert.AreEqual(preset.presetName, after.preset, "the preset link survives the reset");
                Assert.IsNull(after.sizeVariant, "the override must be cleared back to the preset's own value");
            }
            finally
            {
                NeoWidgetPresets.ResetForTests();
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void ApplyPreset_KeepsPlacementAndSiblingOrder()
        {
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            preset.presetName = "ApplyTestPreset";
            preset.targetKind = "button";
            preset.variant = "ghost";
            NeoWidgetPresets.Register(preset);
            try
            {
                GameObject first = CreateStyledButton("First");
                GameObject target = CreateStyledButton("Second");
                GameObject third = CreateStyledButton("Third");
                Transform parent = target.transform.parent;
                int indexBefore = target.transform.GetSiblingIndex();
                Assert.AreEqual(1, indexBefore, "fixture sanity: 'Second' must sit between its siblings");

                GameObject rebuilt = NeoSceneAuthoring.ApplyPreset(target, preset.presetName);

                Assert.IsNotNull(rebuilt, "ApplyPreset should have rebuilt the widget");
                Assert.AreSame(parent, rebuilt.transform.parent, "apply-preset must not reparent the widget");
                Assert.AreEqual(indexBefore, rebuilt.transform.GetSiblingIndex(), "apply-preset must preserve sibling order");
                Assert.IsTrue(first.transform.parent == parent && third.transform.parent == parent,
                    "the untouched siblings must stay exactly where they were");
            }
            finally
            {
                NeoWidgetPresets.ResetForTests();
                Object.DestroyImmediate(preset);
            }
        }
    }
}
