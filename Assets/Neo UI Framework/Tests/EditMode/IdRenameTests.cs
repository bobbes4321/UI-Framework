using System.IO;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The one id → GameObject-name convention (<see cref="NeoIdNaming"/>): the generator's element
    /// naming and the id drawer's rename-to-id tail button must produce the same names, so hand-built
    /// and generated hierarchies read alike.
    /// </summary>
    public class IdRenameTests
    {
        [Test]
        public void SpecIdForm_MatchesGeneratorConvention()
        {
            // what the generator bakes for an authored "Category/Name" element id with no
            // id-bearing widget component (shapes, text, containers — NeoElementId carriers).
            // The string form parses first, so it normalizes the default ("None") category
            // exactly like the category/name pair form below.
            Assert.AreEqual("Tabs_Shop", NeoIdNaming.GameObjectNameFor("Tabs/Shop"));
            Assert.AreEqual("Back", NeoIdNaming.GameObjectNameFor("Back"));
            Assert.AreEqual("Back", NeoIdNaming.GameObjectNameFor("None/Back"));
            Assert.AreEqual("A_B_C", NeoIdNaming.GameObjectNameFor("A/B/C"));
        }

        [Test]
        public void SpecIdForm_WithIdOwner_GetsTypePrefix()
        {
            var go = new GameObject("probe");
            try
            {
                UIButton button = go.AddComponent<UIButton>();
                Assert.AreEqual("Button - Tabs_Shop", NeoIdNaming.GameObjectNameFor(button, "Tabs/Shop"));
                Assert.AreEqual("Tabs_Shop", NeoIdNaming.GameObjectNameFor((Component)null, "Tabs/Shop"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CategoryNameForm_DropsDefaultCategory()
        {
            Assert.AreEqual("Tabs_Shop", NeoIdNaming.GameObjectNameFor("Tabs", "Shop"));
            Assert.AreEqual("Back", NeoIdNaming.GameObjectNameFor(CategoryNameId.DefaultCategory, "Back"));
            Assert.AreEqual("Back", NeoIdNaming.GameObjectNameFor("", "Back"));
            Assert.AreEqual("Back", NeoIdNaming.GameObjectNameFor((string)null, "Back"));
        }

        [Test]
        public void CategoryNameForm_SanitizesInvalidNameCharacters()
        {
            // a name containing a path separator can't break the object name
            Assert.AreEqual("Nav_A_B", NeoIdNaming.GameObjectNameFor("Nav", "A/B"));
        }

        [Test]
        public void Sanitize_ReplacesSlashesAndInvalidFileNameCharacters()
        {
            Assert.AreEqual("A_B", NeoIdNaming.Sanitize("A/B"));
            Assert.AreEqual("A_B", NeoIdNaming.Sanitize("A" + Path.GetInvalidFileNameChars()[0] + "B"));
            Assert.AreEqual("", NeoIdNaming.Sanitize(null));
        }

        /// <summary> A project's custom id-bearing widget — implements <see cref="INeoIdOwner"/> and
        /// gets prefix + marker-skip treatment with no package edit (the seam). </summary>
        private class NeoFancyKnob : MonoBehaviour, INeoIdOwner
        {
            public CategoryNameId OwnId => new CategoryNameId("Custom", "Knob");
        }

        [Test]
        public void ProjectWidget_ImplementingINeoIdOwner_GetsDerivedPrefix()
        {
            var go = new GameObject("probe");
            try
            {
                Assert.AreEqual("Fancy Knob", NeoIdNaming.TypePrefix(go.AddComponent<NeoFancyKnob>()),
                    "custom widgets get a nicified prefix from their type name — no registry needed");
                Assert.IsNotNull(go.GetComponent<INeoIdOwner>(),
                    "the generator's OwnIdComponent lookup resolves custom widgets through the interface");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TypePrefix_DerivesFromComponentType()
        {
            var go = new GameObject("prefix probe");
            try
            {
                Assert.AreEqual("Button", NeoIdNaming.TypePrefix(go.AddComponent<UIButton>()));
                Assert.AreEqual("Toggle Group", NeoIdNaming.TypePrefix(go.AddComponent<UIToggleGroup>()),
                    "type names nicify — no Doozy-style per-inspector hardcoded prefixes");
                Assert.IsNull(NeoIdNaming.TypePrefix(go.AddComponent<UIView>()),
                    "view roots are prefab roots (name pinned to the asset file) — never prefixed");
                Assert.IsNull(NeoIdNaming.TypePrefix(null));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RenameToId_RenamesWithTypePrefix_AndSupportsUndo()
        {
            var go = new GameObject("Button_3");
            try
            {
                UIButton button = go.AddComponent<UIButton>();
                button.id = new ButtonId("Nav", "Quit");

                Assert.IsTrue(NeoIdNaming.RenameToId(button, button.id));
                Assert.AreEqual("Button - Nav_Quit", go.name);

                // second click is a no-op — the name already matches
                Assert.IsFalse(NeoIdNaming.RenameToId(button, button.id));

                Undo.PerformUndo();
                Assert.AreEqual("Button_3", go.name);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RenameToId_DefaultOrMissingId_IsANoOp()
        {
            var go = new GameObject("Untouched");
            try
            {
                UIButton button = go.AddComponent<UIButton>();
                Assert.IsFalse(NeoIdNaming.RenameToId(button, button.id), "default id must not rename");
                Assert.IsFalse(NeoIdNaming.RenameToId(button, null));
                Assert.IsFalse(NeoIdNaming.RenameToId(null, new ButtonId("A", "B")));
                Assert.AreEqual("Untouched", go.name);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
