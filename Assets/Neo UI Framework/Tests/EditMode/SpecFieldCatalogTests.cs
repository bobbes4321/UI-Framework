using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The Composer's field catalog (Plan 2) is the single source mapping element kind → editable
    /// fields and feeding the "+ Add" pickers. These tests guard that it stays in step with
    /// <see cref="ElementSpec"/>: every kind resolves to at least one field, the add picker is exactly
    /// the spec's kind list, and the exposed-field set is pinned (so adding an ElementSpec field
    /// forces a conscious catalog + test update rather than silently going un-editable).
    /// </summary>
    public class SpecFieldCatalogTests
    {
        [Test]
        public void EveryElementKind_HasAtLeastOneField()
        {
            foreach (string kind in ElementSpec.Kinds)
                Assert.Greater(SpecFieldCatalog.For(kind).Count, 0, $"kind '{kind}' exposes no inspector fields");
        }

        [Test]
        public void AddPicker_IsExactlyTheSpecKindList()
        {
            CollectionAssert.AreEqual(ElementSpec.Kinds, SpecFieldCatalog.ElementKinds.ToArray());
        }

        [Test]
        public void Button_ExposesVariantAndSize()
        {
            var keys = SpecFieldCatalog.For("button").Select(f => f.key).ToList();
            CollectionAssert.Contains(keys, "variant");
            CollectionAssert.Contains(keys, "sizeVariant");
            CollectionAssert.Contains(keys, "label");
        }

        [Test]
        public void Grid_ExposesColumnsAndCellSize()
        {
            var keys = SpecFieldCatalog.For("grid").Select(f => f.key).ToList();
            CollectionAssert.Contains(keys, "columns");
            CollectionAssert.Contains(keys, "cellSize");
            CollectionAssert.Contains(keys, "spacing");
        }

        [Test]
        public void Tab_ExposesControlsAndGroup()
        {
            var keys = SpecFieldCatalog.For("tab").Select(f => f.key).ToList();
            CollectionAssert.Contains(keys, "controls");
            CollectionAssert.Contains(keys, "group");
        }

        [Test]
        public void Shape_ExposesArcFields()
        {
            var keys = SpecFieldCatalog.For("shape").Select(f => f.key).ToList();
            CollectionAssert.Contains(keys, "shape");
            CollectionAssert.Contains(keys, "thickness");
            CollectionAssert.Contains(keys, "arcStart");
            CollectionAssert.Contains(keys, "arcSweep");
        }

        [Test]
        public void EveryFieldKey_IsUnique()
        {
            var keys = SpecFieldCatalog.AllKeys();
            CollectionAssert.AllItemsAreUnique(keys);
        }

        /// <summary>
        /// Pins the full exposed-field set. ElementSpec fields handled OUTSIDE the generic catalog —
        /// <c>kind</c>/<c>children</c> (structure), <c>item</c> (row template), <c>gradient</c> and the
        /// <c>onClick*</c> family (bespoke inspector sections) — are intentionally absent. If you add a
        /// generically-editable ElementSpec field, add it to the catalog and to this list.
        /// </summary>
        [Test]
        public void ExposedFieldSet_IsPinned()
        {
            var expected = new HashSet<string>
            {
                "anchor", "size", "position", "rotation", "flex",
                "background", "style", "radius",
                "label", "labelColor", "textStyle", "fontSize", "align", "outlineColor", "outlineWidth",
                "padding", "spacing", "cascade", "columns", "cellSize",
                "variant", "sizeVariant", "icon", "badge",
                "controls", "group",
                "min", "max", "value", "step",
                "shape", "thickness", "arcStart", "arcSweep",
                "src", "fit",
                "options", "bind", "id", "catalog",
            };
            CollectionAssert.AreEquivalent(expected, SpecFieldCatalog.AllKeys());
        }
    }
}
