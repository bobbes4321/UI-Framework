using System.Collections.Generic;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// <see cref="SpecWalk"/> is the single definition of "how to walk a spec's element tree" (audit
    /// D5 — ~9 private walkers that half-disagreed on whether a bound-list <c>item</c> row template
    /// counts). These tests pin the two contracts every migrated caller depends on: item templates are
    /// visited only when asked, and every caller's token extraction sees the SAME set (audit D4).
    /// </summary>
    public class SpecWalkTests
    {
        private static ElementSpec El(string id, string label = null, ElementSpec item = null,
            params ElementSpec[] children)
        {
            var element = new ElementSpec { kind = "vstack", id = id, label = label, item = item };
            element.children.AddRange(children);
            return element;
        }

        private static ViewSpec ViewWith(params ElementSpec[] elements)
        {
            var view = new ViewSpec { category = "Test", viewName = "View" };
            view.elements.AddRange(elements);
            return view;
        }

        [Test]
        public void Elements_IncludesItemTemplate_OnlyWhenAsked()
        {
            var item = El("Row/Item", "row {name}");
            var list = El("List/Root", children: new ElementSpec[0]);
            list.item = item;
            var view = ViewWith(list);

            var withItems = new List<string>();
            SpecWalk.Elements(view, includeItemTemplates: true, e => withItems.Add(e.id));
            CollectionAssert.AreEquivalent(new[] { "List/Root", "Row/Item" }, withItems);

            var withoutItems = new List<string>();
            SpecWalk.Elements(view, includeItemTemplates: false, e => withoutItems.Add(e.id));
            CollectionAssert.AreEquivalent(new[] { "List/Root" }, withoutItems);
        }

        [Test]
        public void Elements_RecursesChildrenAndNestedItemTemplates()
        {
            // a container's item template that is itself a container with children AND its own
            // nested item — the case the old SpecMigration walker silently dropped (it only visited
            // the item element itself, never descended into it).
            var nestedItem = El("Nested/Item", "nested {value}");
            var rowChild = El("Row/Child", "child {childToken}");
            var rowTemplate = El("Row/Template", "row {rowToken}", item: nestedItem, children: new[] { rowChild });
            var root = El("List/Root", item: rowTemplate);
            var view = ViewWith(root);

            var visited = new List<string>();
            SpecWalk.Elements(view, includeItemTemplates: true, e => visited.Add(e.id));

            CollectionAssert.AreEquivalent(
                new[] { "List/Root", "Row/Template", "Nested/Item", "Row/Child" }, visited);
        }

        [Test]
        public void ElementRootedOverload_VisitsRootItself()
        {
            var child = El("Child/A");
            var root = El("Root/A", children: new[] { child });

            var visited = new List<string>();
            SpecWalk.Elements(root, includeItemTemplates: true, e => visited.Add(e.id));

            CollectionAssert.AreEquivalent(new[] { "Root/A", "Child/A" }, visited);
        }

        [Test]
        public void ParentAwareOverload_ReportsNullParentAtTopLevel_AndImmediateParentBelow()
        {
            var grandchild = El("GC/A");
            var child = El("Child/A", children: new[] { grandchild });
            var view = ViewWith(child);

            var parents = new Dictionary<string, string>();
            SpecWalk.Elements(view, includeItemTemplates: true,
                (element, parent) => parents[element.id] = parent?.id);

            Assert.IsNull(parents["Child/A"], "a top-level view element has no parent");
            Assert.AreEqual("Child/A", parents["GC/A"], "a child's parent is the element that owns it");
        }

        [Test]
        public void TokenExtraction_FromNestedTemplate_MatchesDirectWalk()
        {
            // BindingManifest.CollectTokens sits on top of SpecWalk; its result must match manually
            // walking the same tree with includeItemTemplates: true and pulling {tokens} out of every
            // label — the exact invariant that used to drift between BindingManifest's own walker and
            // the (now-deleted) Composer preview's private token scanner (audit D4).
            var nestedItem = El("Nested/Item", "{innerToken}");
            var rowChild = El("Row/Child", "{childToken}");
            var template = El("Row/Template", "{rowToken}", item: nestedItem, children: new[] { rowChild });

            List<string> viaManifest = BindingManifest.CollectTokens(template);

            var expected = new List<string>();
            SpecWalk.Elements(template, includeItemTemplates: true, e =>
            {
                if (string.IsNullOrEmpty(e.label)) return;
                int open = e.label.IndexOf('{');
                int close = e.label.IndexOf('}');
                if (open >= 0 && close > open)
                    expected.Add(e.label.Substring(open + 1, close - open - 1).Trim());
            });

            CollectionAssert.AreEquivalent(expected, viaManifest);
            CollectionAssert.AreEquivalent(new[] { "rowToken", "innerToken", "childToken" }, viaManifest);
        }

        [Test]
        public void Elements_NullRoot_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => SpecWalk.Elements((ViewSpec)null, true, e => { }));
            Assert.DoesNotThrow(() => SpecWalk.Elements((PopupSpec)null, true, e => { }));
            Assert.DoesNotThrow(() => SpecWalk.Elements((ElementSpec)null, true, e => { }));
        }
    }
}
