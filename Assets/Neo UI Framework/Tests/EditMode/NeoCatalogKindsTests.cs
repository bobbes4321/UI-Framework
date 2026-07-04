using System.Collections.Generic;
using System.Text.RegularExpressions;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The reference Pattern-R registry (the shape the whole extensibility-seams family mirrors):
    /// <see cref="NeoCatalogKinds"/> ships the two built-ins through the seam, and
    /// <see cref="NeoCatalogKinds.Register"/> adds/replaces a kind by id. Wave 4 Task 4.2: migrated onto
    /// <see cref="NeoKeyedRegistry{T}"/> — the pre-made policy for this registry flips an invalid
    /// <see cref="NeoCatalogKinds.Register"/> call from throwing to warn-and-ignore (audit A6).
    /// </summary>
    public class NeoCatalogKindsTests
    {
        [TearDown]
        public void Reset() => NeoCatalogKinds.ResetForTests();

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

        [Test]
        public void Register_EmptyId_WarnsAndIgnores_NeverThrows()
        {
            int before = NeoCatalogKinds.All.Count;
            LogAssert.Expect(LogType.Warning, new Regex("NeoCatalogKinds: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => NeoCatalogKinds.Register(new CatalogKind("", "Debug", s => s.settings)));

            Assert.AreEqual(before, NeoCatalogKinds.All.Count, "an invalid registration must not add a row");
        }

        [Test]
        public void Register_NullListAccessor_WarnsAndIgnores_NeverThrows()
        {
            int before = NeoCatalogKinds.All.Count;
            LogAssert.Expect(LogType.Warning, new Regex("NeoCatalogKinds: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => NeoCatalogKinds.Register(new CatalogKind("no-list-kind", "Debug", null)));

            Assert.AreEqual(before, NeoCatalogKinds.All.Count, "a null list accessor must not add a row");
            Assert.IsFalse(NeoCatalogKinds.TryGet("no-list-kind", out _));
        }

        [Test]
        public void ResetForTests_ClearsRegistrations_AndRestoresExactlyTheBuiltins()
        {
            NeoCatalogKinds.Register(new CatalogKind("test-reset-kind", "Debug", s => s.settings));
            Assert.IsTrue(NeoCatalogKinds.TryGet("test-reset-kind", out _));

            NeoCatalogKinds.ResetForTests();

            Assert.IsFalse(NeoCatalogKinds.TryGet("test-reset-kind", out _), "reset drops project registrations");
            Assert.AreEqual(2, NeoCatalogKinds.All.Count, "reset re-seeds exactly the two built-ins");
        }
    }
}
