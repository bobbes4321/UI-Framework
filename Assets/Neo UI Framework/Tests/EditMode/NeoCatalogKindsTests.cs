using System.Collections.Generic;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The reference Pattern-R registry (the shape the whole extensibility-seams family mirrors):
    /// <see cref="NeoCatalogKinds"/> ships the two built-ins through the seam, and
    /// <see cref="NeoCatalogKinds.Register"/> adds/replaces a kind by id.
    /// </summary>
    public class NeoCatalogKindsTests
    {
        [Test]
        public void All_ContainsTheTwoBuiltins()
        {
            var ids = new List<string>();
            foreach (CatalogKind kind in NeoCatalogKinds.All) ids.Add(kind.id);
            CollectionAssert.Contains(ids, MenuCatalogSpec.SettingsKind);
            CollectionAssert.Contains(ids, MenuCatalogSpec.CheatKind);
        }

        [Test]
        public void Builtins_PointAtTheExistingSpecFields()
        {
            var spec = new UISpec();
            Assert.IsTrue(NeoCatalogKinds.TryGet(MenuCatalogSpec.SettingsKind, out CatalogKind settings));
            Assert.IsTrue(NeoCatalogKinds.TryGet(MenuCatalogSpec.CheatKind, out CatalogKind cheats));
            Assert.AreSame(spec.settings, settings.list(spec), "settings kind must store on spec.settings");
            Assert.AreSame(spec.cheats, cheats.list(spec), "cheats kind must store on spec.cheats");
        }

        [Test]
        public void Cheats_DeclareFavourites_SettingsDoNot()
        {
            Assert.IsTrue(NeoCatalogKinds.TryGet(MenuCatalogSpec.CheatKind, out CatalogKind cheats));
            Assert.IsTrue(NeoCatalogKinds.TryGet(MenuCatalogSpec.SettingsKind, out CatalogKind settings));
            Assert.IsTrue(cheats.showFavourites);
            Assert.IsFalse(settings.showFavourites);
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(NeoCatalogKinds.TryGet("nope-not-a-kind", out _));
        }

        [Test]
        public void Register_AppendsNovelKind_ThenReplacesByIdInPlace()
        {
            const string id = "test-debug-kind";
            int before = NeoCatalogKinds.All.Count;

            var firstStore = new List<MenuCatalogSpec>();
            NeoCatalogKinds.Register(new CatalogKind(id, "Debug", _ => firstStore));
            Assert.AreEqual(before + 1, NeoCatalogKinds.All.Count, "a novel id appends");
            Assert.IsTrue(NeoCatalogKinds.TryGet(id, out CatalogKind got));
            Assert.AreEqual("Debug", got.label);

            // re-registering the same id replaces in place (no duplicate row)
            var secondStore = new List<MenuCatalogSpec>();
            NeoCatalogKinds.Register(new CatalogKind(id, "Debug2", _ => secondStore));
            Assert.AreEqual(before + 1, NeoCatalogKinds.All.Count, "same id replaces, never duplicates");
            Assert.IsTrue(NeoCatalogKinds.TryGet(id, out CatalogKind got2));
            Assert.AreEqual("Debug2", got2.label);
            Assert.AreSame(secondStore, got2.list(new UISpec()));
        }
    }
}
