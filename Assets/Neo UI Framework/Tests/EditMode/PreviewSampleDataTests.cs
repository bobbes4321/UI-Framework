using System.Collections.Generic;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar G (preview fidelity): <see cref="PreviewSampleData"/> synthesizes placeholder rows for a
    /// bound list's <c>{key}</c> tokens and pushes them through <see cref="UIData"/> so the Composer
    /// preview shows a populated list. Pure model — no window, no graphics device. Preview-only: it only
    /// touches the in-memory data store (the caller clears it after each render).
    /// </summary>
    public class PreviewSampleDataTests
    {
        private static ViewSpec View(params ElementSpec[] elements)
        {
            var view = new ViewSpec { category = "Spec", viewName = "Bound" };
            view.elements.AddRange(elements);
            return view;
        }

        private static ElementSpec BoundList(string bind, string itemLabel)
        {
            var item = new ElementSpec { kind = "button", id = "Row/Pick", label = itemLabel };
            return new ElementSpec { kind = "list", bind = bind, item = item };
        }

        [TearDown]
        public void ClearStore()
        {
            // sample data is preview-only; wipe anything a test pushed so cases don't bleed
            UIData.Clear("Inv", "Items");
            UIData.Clear("None", "Bare");
            UIData.Clear("Shop", "Deals");
            UIData.Clear("Empty", "Rows");
        }

        [Test]
        public void Populate_SynthesizesRequestedRowCount()
        {
            List<PreviewSampleData.Binding> filled =
                PreviewSampleData.Populate(View(BoundList("Inv/Items", "{name}")), 4);

            Assert.AreEqual(1, filled.Count, "one bound list filled");
            Assert.IsTrue(UIData.TryGet("Inv", "Items", out List<UIData.Row> rows), "data pushed at the bind id");
            Assert.AreEqual(4, rows.Count, "row count matches the toolbar setting");
        }

        [Test]
        public void Populate_FillsTemplateTokensWithPlaceholders()
        {
            PreviewSampleData.Populate(View(BoundList("Inv/Items", "{name}")), 3);

            Assert.IsTrue(UIData.TryGet("Inv", "Items", out List<UIData.Row> rows));
            Assert.IsTrue(rows[0].ContainsKey("name"), "the {name} token gets a value");
            Assert.AreEqual("Item 1", rows[0]["name"], "name-ish tokens read 'Item N'");
            Assert.AreEqual("Item 2", rows[1]["name"], "rows are 1-based and distinct");
        }

        [Test]
        public void Populate_DistinctColumnsPerKey()
        {
            // a multi-token row (name + count) should fill BOTH keys so every column reads
            var item = new ElementSpec { kind = "text", label = "{name} x{count}" };
            var list = new ElementSpec { kind = "list", bind = "Shop/Deals", item = item };

            PreviewSampleData.Populate(View(list), 2);

            Assert.IsTrue(UIData.TryGet("Shop", "Deals", out List<UIData.Row> rows));
            Assert.AreEqual("Item 1", rows[0]["name"]);
            Assert.AreEqual("1", rows[0]["count"], "numeric-ish tokens read a bare number");
            Assert.AreEqual("2", rows[1]["count"]);
        }

        [Test]
        public void Populate_CollectsTokensFromNestedChildren()
        {
            // tokens can live on a descendant of the item template, not just its root
            var child = new ElementSpec { kind = "text", label = "{title}" };
            var item = new ElementSpec { kind = "vstack" };
            item.children.Add(child);
            var list = new ElementSpec { kind = "list", bind = "Inv/Items", item = item };

            PreviewSampleData.Populate(View(list), 2);

            Assert.IsTrue(UIData.TryGet("Inv", "Items", out List<UIData.Row> rows));
            Assert.IsTrue(rows[0].ContainsKey("title"), "tokens on nested children are collected");
            Assert.AreEqual("Item 1", rows[0]["title"]);
        }

        [Test]
        public void Populate_BareBindParsesToNoneCategory()
        {
            // a bare bind name must land on "None/<name>" — exactly how the generator binds the list
            PreviewSampleData.Populate(View(BoundList("Bare", "{name}")), 1);

            Assert.IsTrue(UIData.TryGet("None", "Bare", out List<UIData.Row> rows),
                "bare bind id mirrors CategoryNameId.Parse → None/<name>");
            Assert.AreEqual(1, rows.Count);
        }

        [Test]
        public void Populate_TokenlessTemplateStillYieldsRowCount()
        {
            // a template with no {key} tokens still spawns N repeats, so the count is preserved
            PreviewSampleData.Populate(View(BoundList("Empty/Rows", "Static label")), 5);

            Assert.IsTrue(UIData.TryGet("Empty", "Rows", out List<UIData.Row> rows));
            Assert.AreEqual(5, rows.Count, "tokenless rows still repeat the template N times");
        }

        [Test]
        public void Populate_ZeroRows_PushesEmptyList()
        {
            PreviewSampleData.Populate(View(BoundList("Inv/Items", "{name}")), 0);

            Assert.IsTrue(UIData.TryGet("Inv", "Items", out List<UIData.Row> rows));
            Assert.AreEqual(0, rows.Count, "row count 0 shows the empty list");
        }

        [Test]
        public void Populate_BindWithoutItem_IsSkipped_NoData()
        {
            // a bind with no item template has no row structure to clone — preview convenience, no data.
            // Use a unique id no other test/teardown touches, so TryGet reflects ONLY this call (UIData is
            // a shared static store and Clear leaves an empty-but-registered entry).
            var list = new ElementSpec { kind = "list", bind = "Ghost/None", item = null };

            List<PreviewSampleData.Binding> filled = PreviewSampleData.Populate(View(list), 3);

            Assert.IsEmpty(filled, "no item template → nothing filled");
            Assert.IsFalse(UIData.TryGet("Ghost", "None", out _), "no data pushed for an item-less bind");
        }

        [Test]
        public void Populate_NoBoundLists_FillsNothing()
        {
            var plain = new ElementSpec { kind = "button", id = "Action/Play", label = "Play" };

            List<PreviewSampleData.Binding> filled = PreviewSampleData.Populate(View(plain), 3);

            Assert.IsEmpty(filled, "a view with no bound lists fills nothing");
        }

        [Test]
        public void Populate_NullView_IsSafe()
        {
            Assert.DoesNotThrow(() => PreviewSampleData.Populate(null, 3));
        }
    }
}
