using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar E templates: every curated scaffold is a valid <see cref="UISpec"/> that inserts into a
    /// document through <see cref="SpecDocument.ApplyEdit"/>, generates without issues, and round-trips
    /// byte-identically (export → generate → export). Also covers the registry seam + collision handling.
    /// </summary>
    public class TemplateInsertTests
    {
        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static SpecDocument EmptyDocument()
        {
            var doc = new SpecDocument();
            doc.Load(new UISpec(), null); // truly empty — no default view
            return doc;
        }

        [Test]
        public void Builtins_AreRegistered()
        {
            var ids = ComposerTemplates.All.Select(t => t.id).ToList();
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
            SpecDocument doc = EmptyDocument();
            Assert.IsTrue(ComposerTemplates.TryGet(id, out TemplateEntry entry), $"template '{id}' missing");

            string select = ComposerTemplates.Insert(doc, entry, out List<string> warnings);
            Assert.IsEmpty(warnings, "a fresh insert into an empty spec must not collide");
            Assert.IsNotNull(select, "insert must return a selection path for the first inserted screen");
            Assert.IsTrue(doc.Spec.views.Count > 0 || doc.Spec.popups.Count > 0,
                "the template must contribute at least one view or popup");

            GenerateReport report = UISpecGenerator.Generate(doc.Spec);
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
            SpecDocument doc = EmptyDocument();
            ComposerTemplates.TryGet(id, out TemplateEntry entry);
            ComposerTemplates.Insert(doc, entry, out _);

            UISpecGenerator.Generate(doc.Spec);
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
            SpecDocument doc = EmptyDocument();
            ComposerTemplates.TryGet("main-menu", out TemplateEntry entry);

            ComposerTemplates.Insert(doc, entry, out List<string> first);
            Assert.IsEmpty(first);
            int viewsAfterFirst = doc.Spec.views.Count;

            // inserting the same template again collides on Menu/Main → must rename + warn, not overwrite
            ComposerTemplates.Insert(doc, entry, out List<string> second);
            Assert.IsNotEmpty(second, "a colliding insert must surface a warning");
            Assert.AreEqual(viewsAfterFirst * 2, doc.Spec.views.Count, "the second copy is added, not merged over");
            Assert.AreEqual(1, doc.Spec.views.Count(v => v.id == "Menu/Main"), "the original is untouched");
            Assert.IsTrue(doc.Spec.views.Any(v => v.id == "Menu/Main2"), "the collision is suffixed");
        }

        [Test]
        public void Register_ReplacesById()
        {
            int before = ComposerTemplates.All.Count;
            ComposerTemplates.Register(new TemplateEntry("main-menu", "Replaced", () => "{ \"views\": [] }"));
            Assert.AreEqual(before, ComposerTemplates.All.Count, "same id replaces in place");
            Assert.IsTrue(ComposerTemplates.TryGet("main-menu", out TemplateEntry got));
            Assert.AreEqual("Replaced", got.label);

            // restore the built-in so other tests / the editor see the shipped template
            ComposerTemplates.Register(new TemplateEntry("main-menu", "Main Menu",
                () => System.IO.File.ReadAllText(System.IO.Path.Combine(
                    Application.dataPath, "Neo UI Framework", "Editor", "Composer", "Templates~", "main-menu.json"))));
        }
    }
}
