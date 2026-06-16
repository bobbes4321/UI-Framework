using System.Collections.Generic;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The pure tree drag-to-reparent core (<see cref="TreeDrag"/>) the left pane uses to move elements
    /// by dragging — the list mechanics (cross-list reparent, same-list reorder index fix) and the cycle
    /// guard, independent of IMGUI. Mirrors the canvas reparent semantics so both authoring surfaces agree.
    /// </summary>
    public class TreeDragTests
    {
        private static ElementSpec El(string kind, params ElementSpec[] kids)
        {
            var e = new ElementSpec { kind = kind };
            e.children.AddRange(kids);
            return e;
        }

        [Test]
        public void Move_AcrossLists_RemovesFromSourceAndInsertsIntoDestination()
        {
            ElementSpec moved = El("text");
            var from = new List<ElementSpec> { moved, El("button") };
            var to = new List<ElementSpec> { El("image") };

            int landed = TreeDrag.Move(from, moved, to, to.Count);

            Assert.AreEqual(1, landed);
            CollectionAssert.DoesNotContain(from, moved);
            Assert.AreSame(moved, to[1], "appended into the destination container");
        }

        [Test]
        public void Move_SameList_Downward_AdjustsForTheRemoval()
        {
            ElementSpec a = El("a"), b = El("b"), c = El("c");
            var list = new List<ElementSpec> { a, b, c };

            // intent: drop 'a' after 'c' (insert index 3); removing 'a' first shifts the target down to 2
            int landed = TreeDrag.Move(list, a, list, 3);

            Assert.AreEqual(2, landed);
            CollectionAssert.AreEqual(new[] { b, c, a }, list);
        }

        [Test]
        public void Move_SameList_Upward_DoesNotAdjust()
        {
            ElementSpec a = El("a"), b = El("b"), c = El("c");
            var list = new List<ElementSpec> { a, b, c };

            int landed = TreeDrag.Move(list, c, list, 0); // drop 'c' before 'a'

            Assert.AreEqual(0, landed);
            CollectionAssert.AreEqual(new[] { c, a, b }, list);
        }

        [Test]
        public void Move_ElementNotInSource_ReturnsMinusOne_AndLeavesListsUntouched()
        {
            var from = new List<ElementSpec> { El("a") };
            var to = new List<ElementSpec>();
            Assert.AreEqual(-1, TreeDrag.Move(from, El("stranger"), to, 0));
            Assert.AreEqual(1, from.Count);
            Assert.AreEqual(0, to.Count);
        }

        [Test]
        public void IsAncestorOrSelf_TrueForSelf_Child_AndGrandchild()
        {
            ElementSpec grandchild = El("c");
            ElementSpec child = El("b", grandchild);
            ElementSpec root = El("a", child);

            Assert.IsTrue(TreeDrag.IsAncestorOrSelf(root, root), "self");
            Assert.IsTrue(TreeDrag.IsAncestorOrSelf(root, child), "direct child");
            Assert.IsTrue(TreeDrag.IsAncestorOrSelf(root, grandchild), "descendant");
        }

        [Test]
        public void IsAncestorOrSelf_FalseForSiblingsAndUnrelated()
        {
            ElementSpec a = El("a"), b = El("b");
            Assert.IsFalse(TreeDrag.IsAncestorOrSelf(a, b));
            Assert.IsFalse(TreeDrag.IsAncestorOrSelf(a, null));
            Assert.IsFalse(TreeDrag.IsAncestorOrSelf(null, b));
        }

        [Test]
        public void ZoneFor_SplitsRowIntoBeforeIntoAfterBands()
        {
            Assert.AreEqual(TreeDrag.Zone.Before, TreeDrag.ZoneFor(0.1f));
            Assert.AreEqual(TreeDrag.Zone.Into, TreeDrag.ZoneFor(0.5f));
            Assert.AreEqual(TreeDrag.Zone.After, TreeDrag.ZoneFor(0.9f));
        }
    }
}
