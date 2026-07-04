using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar E templates: every curated scaffold is a valid <see cref="UISpec"/> that merges into a
    /// spec via <see cref="NeoLayoutTemplates.Insert(UISpec, TemplateEntry, out List{string})"/>,
    /// generates without issues, and round-trips byte-identically (export → generate → export). Also
    /// covers the registry seam + collision handling. (Formerly ran through the Composer's
    /// <c>SpecDocument</c> — that overload died with the window in Wave 3; these tests now exercise the
    /// same collision-suffix/registry logic through the plain-<see cref="UISpec"/> overload.) Wave 4
    /// Task 4.2: <see cref="NeoLayoutTemplates"/> migrated onto <see cref="NeoKeyedRegistry{T}"/>.
    /// </summary>
    public class TemplateInsertTests
    {
        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Builtins_AreRegistered()
        {
            var ids = NeoLayoutTemplates.All.Select(t => t.id).ToList();
            foreach (string expected in new[] { "main-menu", "settings-screen", "hud", "pause-menu", "popup" })
                CollectionAssert.Contains(ids, expected, $"built-in template '{expected}' must be registered");
        }

        [TestCase("main-menu")]
        [TestCase("settings-screen")]
        [TestCase("hud")]
        [TestCase("pause-menu")]
        [TestCase("popup")]
        public void Template_InsertsIntoEmptySpec_AndGenerates(string id)
        {
            var spec = new UISpec();
            Assert.IsTrue(NeoLayoutTemplates.TryGet(id, out TemplateEntry entry), $"template '{id}' missing");

            string select = NeoLayoutTemplates.Insert(spec, entry, out List<string> warnings);
            Assert.IsEmpty(warnings, "a fresh insert into an empty spec must not collide");
            Assert.IsNotNull(select, "insert must return a selection path for the first inserted screen");
            Assert.IsTrue(spec.views.Count > 0 || spec.popups.Count > 0,
                "the template must contribute at least one view or popup");

            GenerateReport report = UISpecGenerator.Generate(spec);
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
        }

        [TestCase("main-menu")]
        [TestCase("settings-screen")]
        [TestCase("hud")]
        [TestCase("pause-menu")]
        [TestCase("popup")]
        public void Template_RoundTripsByteIdentical(string id)
        {
            var spec = new UISpec();
            NeoLayoutTemplates.TryGet(id, out TemplateEntry entry);
            NeoLayoutTemplates.Insert(spec, entry, out _);

            UISpecGenerator.Generate(spec);
            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                $"template '{id}' must export → generate → export byte-identically");

            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
        }

        [Test]
        public void Insert_NameCollision_SuffixesAndWarns_NeverOverwrites()
        {
            var spec = new UISpec();
            NeoLayoutTemplates.TryGet("main-menu", out TemplateEntry entry);

            NeoLayoutTemplates.Insert(spec, entry, out List<string> first);
            Assert.IsEmpty(first);
            int viewsAfterFirst = spec.views.Count;

            // inserting the same template again collides on Menu/Main → must rename + warn, not overwrite
            NeoLayoutTemplates.Insert(spec, entry, out List<string> second);
            Assert.IsNotEmpty(second, "a colliding insert must surface a warning");
            Assert.AreEqual(viewsAfterFirst * 2, spec.views.Count, "the second copy is added, not merged over");
            Assert.AreEqual(1, spec.views.Count(v => v.id == "Menu/Main"), "the original is untouched");
            Assert.IsTrue(spec.views.Any(v => v.id == "Menu/Main2"), "the collision is suffixed");
        }

        [Test]
        public void Register_ReplacesById()
        {
            int before = NeoLayoutTemplates.All.Count;
            NeoLayoutTemplates.Register(new TemplateEntry("main-menu", "Replaced", () => "{ \"views\": [] }"));
            Assert.AreEqual(before, NeoLayoutTemplates.All.Count, "same id replaces in place");
            Assert.IsTrue(NeoLayoutTemplates.TryGet("main-menu", out TemplateEntry got));
            Assert.AreEqual("Replaced", got.label);

            // restore the built-in so other tests / the editor see the shipped template
            NeoLayoutTemplates.Register(new TemplateEntry("main-menu", "Main Menu",
                () => System.IO.File.ReadAllText(System.IO.Path.Combine(
                    Application.dataPath, "Neo UI Framework", "Editor", "Authoring", "Templates~", "main-menu.json"))));
        }

        [Test]
        public void Register_EmptyId_WarnsAndIgnores_NeverThrows()
        {
            int before = NeoLayoutTemplates.All.Count;
            LogAssert.Expect(LogType.Warning, new Regex("NeoLayoutTemplates: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => NeoLayoutTemplates.Register(new TemplateEntry("", "Nameless", () => "{}")));

            Assert.AreEqual(before, NeoLayoutTemplates.All.Count, "an invalid registration must not add a row");
        }

        [Test]
        public void Register_NullLoader_WarnsAndIgnores_NeverThrows()
        {
            int before = NeoLayoutTemplates.All.Count;
            LogAssert.Expect(LogType.Warning, new Regex("NeoLayoutTemplates: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => NeoLayoutTemplates.Register(new TemplateEntry("no-loader", "No Loader", null)));

            Assert.AreEqual(before, NeoLayoutTemplates.All.Count, "a null loader must not add a row");
            Assert.IsFalse(NeoLayoutTemplates.TryGet("no-loader", out _));
        }

        [Test]
        public void ResetForTests_ClearsProjectRegistrations_AndRestoresTheBuiltins()
        {
            NeoLayoutTemplates.Register(new TemplateEntry("test-reset-template", "Debug", () => "{}"));
            Assert.IsTrue(NeoLayoutTemplates.TryGet("test-reset-template", out _));
            int builtinCount = NeoLayoutTemplates.All.Count - 1;

            NeoLayoutTemplates.ResetForTests();

            Assert.IsFalse(NeoLayoutTemplates.TryGet("test-reset-template", out _), "reset drops project registrations");
            Assert.AreEqual(builtinCount, NeoLayoutTemplates.All.Count, "reset re-seeds exactly the built-ins");
            Assert.IsTrue(NeoLayoutTemplates.TryGet("main-menu", out _), "built-ins are still present after reset");
        }

        // ------------------------------------------------------------------ native-authoring parity

        /// <summary> The native-authoring counterpart to <see cref="Template_InsertsIntoEmptySpec_AndGenerates"/>:
        /// <see cref="NeoSceneAuthoring.InsertTemplate"/> (the "GameObject → Neo UI → Insert Template…" menu's
        /// worker) builds the SAME element tree live under an existing view, via
        /// <see cref="UISpecGenerator.BuildElementLive"/> — no spec document, no generated prefab. </summary>
        [Test]
        public void NativeInsertTemplate_BuildsElementTreeUnderSelectedView()
        {
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            if (settings != null && settings.theme != null)
            {
                StarterKitBootstrap.EnsureFactoryTokens(settings.theme);
                StarterKitBootstrap.EnsureTextStyles(settings.theme);
            }

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            var viewSpec = new ViewSpec { category = "TemplateNative", viewName = "V" };
            GameObject viewGo = UISpecGenerator.BuildViewGameObject(viewSpec, settings, new GenerateReport());
            viewGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);

            try
            {
                Assert.IsTrue(NeoLayoutTemplates.TryGet("main-menu", out TemplateEntry entry));

                GameObject firstRoot = NeoSceneAuthoring.InsertTemplate(entry, viewGo);

                Assert.IsNotNull(firstRoot, "InsertTemplate must build and return the first created root");
                Assert.AreSame(viewGo.transform, firstRoot.transform.parent,
                    "the template's top-level element must be parented under the selected view");

                UIButton[] buttons = viewGo.GetComponentsInChildren<UIButton>(true);
                CollectionAssert.IsNotEmpty(buttons, "the main-menu template's buttons must be built under the view");
                Assert.IsTrue(buttons.Any(b => b.id.Matches("Action", "Play")), "the Play button must be present");
                Assert.IsTrue(buttons.Any(b => b.id.Matches("Action", "Quit")), "the Quit button must be present");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }
    }
}
