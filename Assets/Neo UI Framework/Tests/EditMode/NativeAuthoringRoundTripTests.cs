using Neo.UI.Editor;
using Neo.UI.Editor.Composer; // ComposerFactory
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The keystone guarantee of native authoring: a widget created live in a scene through
    /// <see cref="UISpecGenerator.BuildElementLive"/> (the path the GameObject → Neo UI menu uses) exports
    /// to the EXACT same spec as one produced by the generator. If these ever diverge, native creation would
    /// silently drift from generation and break the round-trip — so this is asserted per widget kind.
    /// </summary>
    public class NativeAuthoringRoundTripTests
    {
        // Kinds whose ComposerFactory default is self-contained (no required children / external refs).
        private static readonly string[] Kinds =
        {
            "button", "toggle", "switch", "slider", "stepper", "dropdown",
            "text", "image", "icon", "shape", "progress",
            "vstack", "hstack", "grid", "panel",
        };

        private NeoUISettings _settings;

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

        [Test]
        public void CreatedWidget_ExportsIdenticalToGenerated([ValueSource(nameof(Kinds))] string kind)
        {
            // Both views share an identical id, so once exported the ONLY thing that can differ between
            // the two specs is the element content — exactly what we're asserting parity on.
            const string category = "NativeParity";
            const string viewName = "V";

            // Path A — generation: the kind sits in the view spec and is built by BuildViewGameObject.
            var viewA = new ViewSpec { category = category, viewName = viewName };
            viewA.elements.Add(ComposerFactory.NewElement(kind));
            GameObject rootA = UISpecGenerator.BuildViewGameObject(viewA, _settings, new GenerateReport());

            // Path B — native: an empty view, then the same element dropped in live.
            var viewB = new ViewSpec { category = category, viewName = viewName };
            GameObject rootB = UISpecGenerator.BuildViewGameObject(viewB, _settings, new GenerateReport());
            GameObject created = UISpecGenerator.BuildElementLive(
                ComposerFactory.NewElement(kind), (RectTransform)rootB.transform, _settings, new GenerateReport());
            Assert.IsNotNull(created, $"BuildElementLive returned null for kind '{kind}'");

            try
            {
                var specA = new UISpec();
                specA.views.Add(UISpecExporter.ExportView(rootA));
                var specB = new UISpec();
                specB.views.Add(UISpecExporter.ExportView(rootB));

                Assert.AreEqual(specA.ToJson(), specB.ToJson(),
                    $"native-created '{kind}' must export identically to a generated one");
            }
            finally
            {
                Object.DestroyImmediate(rootA);
                Object.DestroyImmediate(rootB);
            }
        }
    }
}
