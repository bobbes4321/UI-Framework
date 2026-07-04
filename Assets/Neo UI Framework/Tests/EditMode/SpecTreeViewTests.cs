using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 1 of the composer-catalog unification: the two co-equal "Settings"/"Cheats" tree
    /// sections collapse into one neutral <c>Menus</c> section over <see cref="NeoCatalogKinds"/>,
    /// while each catalog <i>row</i> keeps its real <see cref="SpecPath.Catalog"/> path (selection +
    /// baseline addressing unchanged). Pure model + tree row build — no window.
    /// </summary>
    public class SpecTreeViewTests
    {
        private static SpecTreeView TreeOver(UISpec spec)
        {
            var doc = new SpecDocument();
            doc.Load(spec, null);
            return new SpecTreeView(doc);
        }

        private static UISpec SpecWithBothKinds()
        {
            var spec = new UISpec();
            spec.views.Clear(); // keep the row set about menus only
            spec.settings.Add(ComposerFactory.NewCatalog(MenuCatalogSpec.SettingsKind, "Settings", "Audio"));
            spec.cheats.Add(ComposerFactory.NewCatalog(MenuCatalogSpec.CheatKind, "Cheats", "Debug"));
            return spec;
        }

        [Test]
        public void OneMenusHeader_NoSettingsOrCheatsHeaders()
        {
            IReadOnlyList<SpecNode> rows = TreeOver(SpecWithBothKinds()).RebuildForTest();

            int headers = rows.Count(n => n.kind == SpecNodeKind.MenusHeader);
            Assert.AreEqual(1, headers, "exactly one neutral Menus header");

            SpecNode menus = rows.First(n => n.kind == SpecNodeKind.MenusHeader);
            Assert.AreEqual("menus", menus.path);
            Assert.AreEqual("Menus (2)", menus.label, "the header counts catalogs across all kinds");
        }

        [Test]
        public void BothKinds_AppearAsCatalogRows_UnderTheOneSection()
        {
            IReadOnlyList<SpecNode> rows = TreeOver(SpecWithBothKinds()).RebuildForTest();

            var catalogs = rows.Where(n => n.kind == SpecNodeKind.Catalog).ToList();
            Assert.AreEqual(2, catalogs.Count);
            foreach (SpecNode c in catalogs)
                Assert.AreEqual(1, c.depth, "catalog rows sit one level under the Menus header");

            // each row is tagged with its kind label so the single section stays legible
            Assert.IsTrue(catalogs.Any(c => c.label == "Settings · Settings/Audio"));
            Assert.IsTrue(catalogs.Any(c => c.label == "Cheats · Cheats/Debug"));
        }

        [Test]
        public void EachCatalogRow_KeepsItsRealSpecPath()
        {
            IReadOnlyList<SpecNode> rows = TreeOver(SpecWithBothKinds()).RebuildForTest();

            SpecNode settings = rows.First(n => n.kind == SpecNodeKind.Catalog && n.catalog.kind == MenuCatalogSpec.SettingsKind);
            SpecNode cheats = rows.First(n => n.kind == SpecNodeKind.Catalog && n.catalog.kind == MenuCatalogSpec.CheatKind);

            // the unified header is cosmetic — paths must still be per-section (selection unchanged)
            Assert.AreEqual(SpecPath.Catalog("settings", "Settings/Audio"), settings.path);
            Assert.AreEqual(SpecPath.Catalog("cheats", "Cheats/Debug"), cheats.path);
        }

        [Test]
        public void NoCatalogs_NeutralEmptyMenusSection()
        {
            var spec = new UISpec();
            spec.views.Clear();
            IReadOnlyList<SpecNode> rows = TreeOver(spec).RebuildForTest();

            SpecNode menus = rows.FirstOrDefault(n => n.kind == SpecNodeKind.MenusHeader);
            Assert.IsNotNull(menus);
            Assert.AreEqual("Menus (0)", menus.label, "an empty Menus section asserts nothing about cheats");
            Assert.IsFalse(rows.Any(n => n.kind == SpecNodeKind.Catalog));
        }
    }
}
