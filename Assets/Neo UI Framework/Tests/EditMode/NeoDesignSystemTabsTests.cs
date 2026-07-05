using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 2.9: the Design System window's tab set is a <see cref="NeoDesignSystemTabs"/> registry
    /// (Pattern R) so a consuming project can add its own design-system tab without forking the window.
    /// The window/IMGUI itself needs a live editor and is out of scope for an EditMode test — these
    /// exercise the registry directly: the built-in tabs are exactly the expected eight in display
    /// order, and a project-registered tab appears (and is cleaned up).
    /// </summary>
    public class NeoDesignSystemTabsTests
    {
        private const string FakeId = "test-fake-tab";

        [TearDown]
        public void Reset() => NeoDesignSystemTabs.ResetForTests();

        [Test]
        public void Ordered_IsExactlyTheEightBuiltins_InDisplayOrder()
        {
            var expected = new[] { "overview", "colors", "typography", "buttons", "shapes", "presets", "motion", "bundles" };
            CollectionAssert.AreEqual(expected, NeoDesignSystemTabs.Ordered.Select(t => t.id).ToArray());
        }

        [Test]
        public void Register_ProjectTab_AppearsAndIsOrderedBySortKey()
        {
            int before = NeoDesignSystemTabs.Ordered.Count;

            // order 15 slots the tab between the built-in Buttons (10) and Shapes (20).
            NeoDesignSystemTabs.Register(new DesignSystemTabDescriptor(
                FakeId, "Fake", 15, () => null, _ => { }));

            Assert.AreEqual(before + 1, NeoDesignSystemTabs.Ordered.Count, "a novel id appends one tab");
            Assert.IsTrue(NeoDesignSystemTabs.TryGet(FakeId, out DesignSystemTabDescriptor got));
            Assert.AreEqual("Fake", got.title);

            var ids = NeoDesignSystemTabs.Ordered.Select(t => t.id).ToArray();
            CollectionAssert.AreEqual(
                new[] { "overview", "colors", "typography", "buttons", FakeId, "shapes", "presets", "motion", "bundles" }, ids,
                "the tab lands at its sort order (15), between Buttons and Shapes");
        }

        [Test]
        public void ResetForTests_RestoresExactlyTheEightBuiltins()
        {
            NeoDesignSystemTabs.Register(new DesignSystemTabDescriptor(FakeId, "Fake", 15, null, _ => { }));
            Assert.IsTrue(NeoDesignSystemTabs.TryGet(FakeId, out _));

            NeoDesignSystemTabs.ResetForTests();

            Assert.IsFalse(NeoDesignSystemTabs.TryGet(FakeId, out _), "reset drops project registrations");
            Assert.AreEqual(8, NeoDesignSystemTabs.Ordered.Count, "reset re-seeds exactly the eight built-ins");
        }
    }
}
