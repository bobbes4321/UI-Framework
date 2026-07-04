using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar E palette registry (Pattern R): the built-ins cover every <see cref="ElementSpec.Kinds"/>
    /// entry grouped by category, a project-registered <see cref="NeoElementKinds"/> kind appears for
    /// free, and <see cref="NeoWidgetPalette.Register"/> replaces-by-kind. Mirrors
    /// <see cref="ComposerCatalogKindsTests"/>. Wave 4 Task 4.2: migrated onto <see cref="NeoKeyedRegistry{T}"/>.
    /// </summary>
    public class PaletteRegistryTests
    {
        [TearDown]
        public void Reset()
        {
            NeoElementKinds.ResetForTests();
            NeoWidgetPalette.ResetForTests();
        }

        [Test]
        public void Builtins_CoverEveryElementKind()
        {
            var kinds = new HashSet<string>();
            foreach (PaletteEntry e in NeoWidgetPalette.All) kinds.Add(e.kind);
            foreach (string builtin in ElementSpec.Kinds)
                Assert.IsTrue(kinds.Contains(builtin), $"palette is missing built-in kind '{builtin}'");
        }

        [Test]
        public void Builtins_LandInSensibleCategories()
        {
            string CategoryOf(string kind) =>
                NeoWidgetPalette.All.First(e => e.kind == kind).category;

            Assert.AreEqual("Layout", CategoryOf("vstack"));
            Assert.AreEqual("Layout", CategoryOf("grid"));
            Assert.AreEqual("Input", CategoryOf("button"));
            Assert.AreEqual("Input", CategoryOf("slider"));
            Assert.AreEqual("Display", CategoryOf("text"));
            Assert.AreEqual("Display", CategoryOf("shape"));
            Assert.AreEqual("Data", CategoryOf("list"));
            Assert.AreEqual("Menus", CategoryOf("settings"));
        }

        [Test]
        public void Categories_AreInDisplayOrder()
        {
            List<string> categories = NeoWidgetPalette.Categories.ToList();
            // Layout/Input/Display/Data/Menus precede any "Custom" tail
            Assert.AreEqual("Layout", categories[0]);
            CollectionAssert.Contains(categories, "Input");
            CollectionAssert.Contains(categories, "Menus");
            // none repeat
            CollectionAssert.AllItemsAreUnique(categories);
        }

        [Test]
        public void ProjectKind_AppearsAutomatically_InCustom()
        {
            NeoElementKinds.Register(new FakeKind("carousel"));
            PaletteEntry entry = NeoWidgetPalette.All.FirstOrDefault(e => e.kind == "carousel");
            Assert.AreEqual("carousel", entry.kind, "a registered project kind must appear in the palette");
            Assert.AreEqual("Custom", entry.category, "an unregistered project kind defaults to Custom");
            Assert.AreEqual(Color.magenta, NeoWidgetPalette.AccentFor(entry), "it uses its own Accent");
        }

        [Test]
        public void Register_ReplacesByKind_NeverDuplicates()
        {
            int before = NeoWidgetPalette.All.Count;

            // re-register an existing built-in kind into a different category
            NeoWidgetPalette.Register(new PaletteEntry("button", "Custom", "My Button"));
            Assert.AreEqual(before, NeoWidgetPalette.All.Count, "replacing a kind must not add a row");

            PaletteEntry button = NeoWidgetPalette.All.First(e => e.kind == "button");
            Assert.AreEqual("Custom", button.category);
            Assert.AreEqual("My Button", button.label);
        }

        [Test]
        public void Register_NovelKind_Appends()
        {
            int before = NeoWidgetPalette.All.Count;
            NeoWidgetPalette.Register(new PaletteEntry("ribbon", "Display", "Ribbon"));
            Assert.AreEqual(before + 1, NeoWidgetPalette.All.Count);
            Assert.IsTrue(NeoWidgetPalette.All.Any(e => e.kind == "ribbon" && e.category == "Display"));
        }

        [Test]
        public void Register_EmptyKind_WarnsAndIgnores_NeverThrows()
        {
            int before = NeoWidgetPalette.All.Count;
            LogAssert.Expect(LogType.Warning, new Regex("NeoWidgetPalette: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => NeoWidgetPalette.Register(new PaletteEntry("", "Display", "Nameless")));

            Assert.AreEqual(before, NeoWidgetPalette.All.Count, "an invalid registration must not add a row");
        }

        [Test]
        public void All_CachesBetweenCalls_ButInvalidatesOnSourceMutation()
        {
            IReadOnlyList<PaletteEntry> first = NeoWidgetPalette.All;
            Assert.AreSame(first, NeoWidgetPalette.All, "repeated reads with no mutation return the cached instance");

            NeoWidgetPalette.Register(new PaletteEntry("banner", "Display", "Banner"));
            IReadOnlyList<PaletteEntry> afterRegister = NeoWidgetPalette.All;
            Assert.AreNotSame(first, afterRegister, "registering a new tile invalidates the cache");

            NeoElementKinds.Register(new FakeKind("marquee"));
            IReadOnlyList<PaletteEntry> afterKind = NeoWidgetPalette.All;
            Assert.AreNotSame(afterRegister, afterKind, "a project element kind change invalidates the cache");
            Assert.IsTrue(afterKind.Any(e => e.kind == "marquee"));
        }

        // a minimal INeoElementKind for the auto-include test
        private sealed class FakeKind : INeoElementKind
        {
            public FakeKind(string kind) { Kind = kind; }
            public string Kind { get; }
            public GameObject Build(ElementBuildContext ctx) => null;
            public bool TryExport(GameObject go, out ElementSpec spec) { spec = null; return false; }
            public IEnumerable<SpecField> Fields => System.Array.Empty<SpecField>();
            public Color Accent => Color.magenta;
            public string SignalPayload => "none";
        }
    }
}
