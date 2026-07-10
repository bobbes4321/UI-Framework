using System;
using System.Collections.Generic;
using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pure-logic tests for the ID Database Manager: the <see cref="NeoUISettings.AllIdDatabases"/>
    /// enumerator seam (built-ins + project-registered, and that <see cref="NeoUISettings.GetDatabaseFor"/>
    /// agrees with it), the new in-place <see cref="IdDatabase.RenameCategory"/>/<see cref="IdDatabase.RenameName"/>,
    /// and the <see cref="IdUsageScanner"/> orphan/dangling cross-reference. The window's IMGUI is not
    /// unit-tested (per the brief).
    /// </summary>
    public class IdDatabaseManagerTests
    {
        private NeoUISettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<NeoUISettings>();
            _settings.viewIds = ScriptableObject.CreateInstance<ViewIdDatabase>();
            _settings.buttonIds = ScriptableObject.CreateInstance<ButtonIdDatabase>();
            _settings.toggleIds = ScriptableObject.CreateInstance<ToggleIdDatabase>();
            _settings.sliderIds = ScriptableObject.CreateInstance<SliderIdDatabase>();
            _settings.tagIds = ScriptableObject.CreateInstance<TagIdDatabase>();
            _settings.streamIds = ScriptableObject.CreateInstance<StreamIdDatabase>();
            _settings.panelIds = ScriptableObject.CreateInstance<PanelIdDatabase>();
            _settings.dropdownIds = ScriptableObject.CreateInstance<DropdownIdDatabase>();
        }

        [TearDown]
        public void TearDown()
        {
            NeoIdDatabaseKinds.ClearForTests();
            if (_settings == null) return;
            UnityEngine.Object.DestroyImmediate(_settings.viewIds);
            UnityEngine.Object.DestroyImmediate(_settings.buttonIds);
            UnityEngine.Object.DestroyImmediate(_settings.toggleIds);
            UnityEngine.Object.DestroyImmediate(_settings.sliderIds);
            UnityEngine.Object.DestroyImmediate(_settings.tagIds);
            UnityEngine.Object.DestroyImmediate(_settings.streamIds);
            UnityEngine.Object.DestroyImmediate(_settings.panelIds);
            UnityEngine.Object.DestroyImmediate(_settings.dropdownIds);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        // ------------------------------------------------------------------ AllIdDatabases seam

        [Test]
        public void AllIdDatabases_ReturnsEveryBuiltIn()
        {
            List<IdDatabaseDescriptor> all = _settings.AllIdDatabases().ToList();
            Assert.AreEqual(8, all.Count, "the eight built-in databases must all surface through the seam");

            Type[] expected =
            {
                typeof(ViewId), typeof(ButtonId), typeof(ToggleId), typeof(SliderId),
                typeof(TagId), typeof(StreamId), typeof(PanelId), typeof(DropdownId)
            };
            CollectionAssert.AreEquivalent(expected, all.Select(d => d.idType).ToArray());
        }

        [Test]
        public void GetDatabaseFor_AgreesWithAllIdDatabases()
        {
            foreach (IdDatabaseDescriptor descriptor in _settings.AllIdDatabases())
                Assert.AreSame(descriptor.database, _settings.GetDatabaseFor(descriptor.idType),
                    $"GetDatabaseFor({descriptor.idType?.Name}) must resolve through the same seam as AllIdDatabases");
        }

        private sealed class ProjectId : CategoryNameId { }
        private sealed class ProjectIdDatabase : IdDatabase { }

        private sealed class ProjectProvider : INeoIdDatabaseProvider
        {
            public readonly ProjectIdDatabase database = ScriptableObject.CreateInstance<ProjectIdDatabase>();
            public IEnumerable<IdDatabaseDescriptor> Describe(NeoUISettings settings) =>
                new[] { new IdDatabaseDescriptor("Project", typeof(ProjectId), database, "Data") };
        }

        [Test]
        public void AllIdDatabases_SurfacesProjectRegisteredDatabase()
        {
            var provider = new ProjectProvider();
            try
            {
                NeoIdDatabaseKinds.Register(provider);

                List<IdDatabaseDescriptor> all = _settings.AllIdDatabases().ToList();
                Assert.AreEqual(9, all.Count, "a project-registered database must appear alongside the built-ins");
                Assert.IsTrue(all.Any(d => d.idType == typeof(ProjectId)));

                // and GetDatabaseFor resolves the project type through the same seam
                Assert.AreSame(provider.database, _settings.GetDatabaseFor(typeof(ProjectId)));
            }
            finally
            {
                NeoIdDatabaseKinds.ClearForTests();
                UnityEngine.Object.DestroyImmediate(provider.database);
            }
        }

        [Test]
        public void Register_ReplacesProviderOfSameType()
        {
            try
            {
                NeoIdDatabaseKinds.Register(new ProjectProvider());
                NeoIdDatabaseKinds.Register(new ProjectProvider());
                Assert.AreEqual(1, NeoIdDatabaseKinds.All.Count, "re-registering the same provider type must not duplicate");
            }
            finally { NeoIdDatabaseKinds.ClearForTests(); }
        }

        // ------------------------------------------------------------------ rename

        [Test]
        public void RenameCategory_KeepsNamesAndSortOrder()
        {
            var db = ScriptableObject.CreateInstance<ButtonIdDatabase>();
            try
            {
                db.Add("Zebra", "One");
                db.Add("Alpha", "Two");
                Assert.IsTrue(db.RenameCategory("Zebra", "Beta"));
                CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, db.GetCategories().ToArray(),
                    "rename must re-apply ordinal category sort");
                CollectionAssert.AreEqual(new[] { "One" }, db.GetNames("Beta").ToArray(),
                    "names move with the renamed category");
                Assert.IsFalse(db.ContainsCategory("Zebra"));
            }
            finally { UnityEngine.Object.DestroyImmediate(db); }
        }

        [Test]
        public void RenameCategory_FailsOnMissingBlankOrCollision()
        {
            var db = ScriptableObject.CreateInstance<ButtonIdDatabase>();
            try
            {
                db.Add("Alpha", "One");
                db.Add("Beta", "Two");
                Assert.IsFalse(db.RenameCategory("Nope", "X"), "missing category → false");
                Assert.IsFalse(db.RenameCategory("Alpha", "  "), "blank target → false");
                Assert.IsFalse(db.RenameCategory("Alpha", "Beta"), "collision with existing category → false");
                Assert.IsTrue(db.RenameCategory("Alpha", "Alpha"), "no-op rename → true");
            }
            finally { UnityEngine.Object.DestroyImmediate(db); }
        }

        [Test]
        public void RenameName_KeepsSortAndGuardsCollision()
        {
            var db = ScriptableObject.CreateInstance<ButtonIdDatabase>();
            try
            {
                db.Add("Action", "Save");
                db.Add("Action", "Cancel");
                Assert.IsTrue(db.RenameName("Action", "Save", "Apply"));
                CollectionAssert.AreEqual(new[] { "Apply", "Cancel" }, db.GetNames("Action").ToArray(),
                    "rename re-applies ordinal name sort");

                Assert.IsFalse(db.RenameName("Action", "Apply", "Cancel"), "collision with existing name → false");
                Assert.IsFalse(db.RenameName("Action", "Missing", "X"), "missing name → false");
                Assert.IsFalse(db.RenameName("Action", "Apply", " "), "blank target → false");
                Assert.IsTrue(db.RenameName("Action", "Apply", "Apply"), "no-op → true");
            }
            finally { UnityEngine.Object.DestroyImmediate(db); }
        }

        // ------------------------------------------------------------------ usage / orphan scan

        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""Menu/Main"", ""elements"": [
            { ""button"": { ""id"": ""Action/Play"", ""label"": ""Play"",
                            ""onClick"": { ""showView"": ""Menu/Settings"" } } },
            { ""toggle"": { ""id"": ""Audio/Music"", ""label"": ""Music"",
                            ""signal"": { ""category"": ""Audio"", ""name"": ""Muted"" } } }
          ] } ],
          ""flow"": { ""name"": ""UI"", ""start"": ""Main"", ""nodes"": [
            { ""name"": ""Main"", ""view"": ""Menu/Main"",
              ""next"": [ { ""on"": { ""button"": ""Action/Quit"" }, ""to"": ""Main"" } ] }
          ] }
        }";

        [Test]
        public void Collect_GathersReferencesByIdType()
        {
            var usage = new IdUsageScanner.Usage();
            IdUsageScanner.Collect(UISpec.FromJson(SpecJson), usage);

            HashSet<IdUsageScanner.Ref> buttons = usage.For(typeof(ButtonId));
            CollectionAssert.Contains(buttons, new IdUsageScanner.Ref("Action", "Play"), "button element id");
            CollectionAssert.Contains(buttons, new IdUsageScanner.Ref("Action", "Quit"), "flow ButtonClick trigger id");

            CollectionAssert.Contains(usage.For(typeof(ToggleId)), new IdUsageScanner.Ref("Audio", "Music"));
            CollectionAssert.Contains(usage.For(typeof(StreamId)), new IdUsageScanner.Ref("Audio", "Muted"),
                "domain signal stream id");
            CollectionAssert.Contains(usage.For(typeof(ViewId)), new IdUsageScanner.Ref("Menu", "Main"));
            CollectionAssert.Contains(usage.For(typeof(ViewId)), new IdUsageScanner.Ref("Menu", "Settings"),
                "onClick.showView view id");
        }

        [Test]
        public void Reconcile_FlagsOrphansAndDangling()
        {
            var usage = new IdUsageScanner.Usage();
            IdUsageScanner.Collect(UISpec.FromJson(SpecJson), usage);

            var buttonDb = ScriptableObject.CreateInstance<ButtonIdDatabase>();
            try
            {
                buttonDb.Add("Action", "Play");      // referenced → neither orphan nor dangling
                buttonDb.Add("Action", "Unused");    // not referenced → ORPHAN
                // "Action/Quit" is referenced (flow trigger) but not in the DB → DANGLING

                IdUsageScanner.DatabaseReport report = IdUsageScanner.Reconcile(buttonDb, typeof(ButtonId), usage);

                CollectionAssert.Contains(report.orphans, new IdUsageScanner.Ref("Action", "Unused"));
                CollectionAssert.DoesNotContain(report.orphans, new IdUsageScanner.Ref("Action", "Play"));
                CollectionAssert.Contains(report.dangling, new IdUsageScanner.Ref("Action", "Quit"));
                CollectionAssert.DoesNotContain(report.dangling, new IdUsageScanner.Ref("Action", "Play"));
            }
            finally { UnityEngine.Object.DestroyImmediate(buttonDb); }
        }

        [Test]
        public void IdTypeForKind_MatchesIdDatabaseOptionsMapping()
        {
            Assert.AreEqual(typeof(ButtonId), IdUsageScanner.IdTypeForKind("button"));
            Assert.AreEqual(typeof(ButtonId), IdUsageScanner.IdTypeForKind("stepper"));
            Assert.AreEqual(typeof(ToggleId), IdUsageScanner.IdTypeForKind("toggle"));
            Assert.AreEqual(typeof(ToggleId), IdUsageScanner.IdTypeForKind("tab"));
            Assert.AreEqual(typeof(SliderId), IdUsageScanner.IdTypeForKind("slider"));
            Assert.AreEqual(typeof(DropdownId), IdUsageScanner.IdTypeForKind("dropdown"));
            Assert.IsNull(IdUsageScanner.IdTypeForKind("text"));
        }

        // ------------------------------------------------------------------ quick-add parse

        [Test]
        public void ParseQuickAdd_SlashForm_NamesBothHalves()
        {
            Assert.IsTrue(IdDatabaseOptions.ParseQuickAdd("Audio/Muted", "Menu", out string category, out string name));
            Assert.AreEqual("Audio", category);
            Assert.AreEqual("Muted", name);
        }

        [Test]
        public void ParseQuickAdd_PlainName_LandsInCurrentCategory()
        {
            Assert.IsTrue(IdDatabaseOptions.ParseQuickAdd("Play", "Action", out string category, out string name));
            Assert.AreEqual("Action", category);
            Assert.AreEqual("Play", name);
        }

        [Test]
        public void ParseQuickAdd_PlainName_NoCurrentCategory_FallsBackToDefault()
        {
            Assert.IsTrue(IdDatabaseOptions.ParseQuickAdd("Play", "", out string category, out string name));
            Assert.AreEqual(CategoryNameId.DefaultCategory, category);
            Assert.AreEqual("Play", name);

            Assert.IsTrue(IdDatabaseOptions.ParseQuickAdd("Play", null, out category, out _));
            Assert.AreEqual(CategoryNameId.DefaultCategory, category);
        }

        [Test]
        public void ParseQuickAdd_LeadingSlash_FallsBackToDefaultCategory()
        {
            Assert.IsTrue(IdDatabaseOptions.ParseQuickAdd("/Muted", "Menu", out string category, out string name));
            Assert.AreEqual(CategoryNameId.DefaultCategory, category);
            Assert.AreEqual("Muted", name);
        }

        [Test]
        public void ParseQuickAdd_TrimsWhitespaceAroundHalves()
        {
            Assert.IsTrue(IdDatabaseOptions.ParseQuickAdd("  Audio / Muted  ", "Menu", out string category, out string name));
            Assert.AreEqual("Audio", category);
            Assert.AreEqual("Muted", name);
        }

        [Test]
        public void ParseQuickAdd_SplitsOnFirstSlashOnly()
        {
            Assert.IsTrue(IdDatabaseOptions.ParseQuickAdd("A/B/C", null, out string category, out string name));
            Assert.AreEqual("A", category);
            Assert.AreEqual("B/C", name);
        }

        [Test]
        public void ParseQuickAdd_RejectsEmptyAndNameless()
        {
            Assert.IsFalse(IdDatabaseOptions.ParseQuickAdd("", "Menu", out _, out _));
            Assert.IsFalse(IdDatabaseOptions.ParseQuickAdd("   ", "Menu", out _, out _));
            Assert.IsFalse(IdDatabaseOptions.ParseQuickAdd(null, "Menu", out _, out _));
            Assert.IsFalse(IdDatabaseOptions.ParseQuickAdd("Audio/", "Menu", out _, out _), "trailing slash = no name");
        }
    }
}
