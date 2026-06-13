using System.Collections.Generic;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The reference Pattern-R registry (the shape the whole extensibility-seams family mirrors):
    /// <see cref="ComposerCatalogKinds"/> ships the two built-ins through the seam, and
    /// <see cref="ComposerCatalogKinds.Register"/> adds/replaces a kind by id. Mirrors
    /// <c>SpecFieldCatalogTests.AddPicker_IsExactlyTheSpecKindList</c>: the chrome's option list must
    /// equal <see cref="ComposerCatalogKinds.All"/>.
    /// </summary>
    public class ComposerCatalogKindsTests
    {
        [Test]
        public void All_ContainsTheTwoBuiltins()
        {
            var ids = new List<string>();
            foreach (CatalogKind kind in ComposerCatalogKinds.All) ids.Add(kind.id);
            CollectionAssert.Contains(ids, MenuCatalogSpec.SettingsKind);
            CollectionAssert.Contains(ids, MenuCatalogSpec.CheatKind);
        }

        [Test]
        public void Builtins_PointAtTheExistingSpecFields()
        {
            var spec = new UISpec();
            Assert.IsTrue(ComposerCatalogKinds.TryGet(MenuCatalogSpec.SettingsKind, out CatalogKind settings));
            Assert.IsTrue(ComposerCatalogKinds.TryGet(MenuCatalogSpec.CheatKind, out CatalogKind cheats));
            Assert.AreSame(spec.settings, settings.list(spec), "settings kind must store on spec.settings");
            Assert.AreSame(spec.cheats, cheats.list(spec), "cheats kind must store on spec.cheats");
        }

        [Test]
        public void Cheats_DeclareFavourites_SettingsDoNot()
        {
            Assert.IsTrue(ComposerCatalogKinds.TryGet(MenuCatalogSpec.CheatKind, out CatalogKind cheats));
            Assert.IsTrue(ComposerCatalogKinds.TryGet(MenuCatalogSpec.SettingsKind, out CatalogKind settings));
            Assert.IsTrue(cheats.showFavourites);
            Assert.IsFalse(settings.showFavourites);
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(ComposerCatalogKinds.TryGet("nope-not-a-kind", out _));
        }

        [Test]
        public void Register_AppendsNovelKind_ThenReplacesByIdInPlace()
        {
            const string id = "test-debug-kind";
            int before = ComposerCatalogKinds.All.Count;

            var firstStore = new List<MenuCatalogSpec>();
            ComposerCatalogKinds.Register(new CatalogKind(id, "Debug", _ => firstStore));
            Assert.AreEqual(before + 1, ComposerCatalogKinds.All.Count, "a novel id appends");
            Assert.IsTrue(ComposerCatalogKinds.TryGet(id, out CatalogKind got));
            Assert.AreEqual("Debug", got.label);

            // re-registering the same id replaces in place (no duplicate row)
            var secondStore = new List<MenuCatalogSpec>();
            ComposerCatalogKinds.Register(new CatalogKind(id, "Debug2", _ => secondStore));
            Assert.AreEqual(before + 1, ComposerCatalogKinds.All.Count, "same id replaces, never duplicates");
            Assert.IsTrue(ComposerCatalogKinds.TryGet(id, out CatalogKind got2));
            Assert.AreEqual("Debug2", got2.label);
            Assert.AreSame(secondStore, got2.list(new UISpec()));
        }

        [Test]
        public void AddPicker_OptionsEqual_All()
        {
            // the "+ Menu ▾" picker is exactly the registered kind list, in order
            var expected = new List<string>();
            foreach (CatalogKind kind in ComposerCatalogKinds.All) expected.Add(kind.label);
            CollectionAssert.AreEqual(expected, SpecTreeView.CatalogKindLabels());

            // and each label round-trips back to its id
            foreach (CatalogKind kind in ComposerCatalogKinds.All)
                Assert.AreEqual(kind.id, SpecTreeView.CatalogKindIdForLabel(kind.label));
        }
    }
}
