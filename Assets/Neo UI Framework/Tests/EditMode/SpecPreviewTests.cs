using System.Collections.Generic;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The spec preview builds views entirely in-memory (the fast feedback / agent render loop) —
    /// it must produce a real, correct view hierarchy WITHOUT committing any prefab to the project.
    /// (The PNG render itself needs a GPU and is exercised by the acceptance run, not here.)
    /// </summary>
    public class SpecPreviewTests
    {
        private const string Spec = @"{
          ""views"": [ { ""id"": ""Prev/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""children"": [
              { ""text"": { ""label"": ""Hello"", ""color"": ""TextStrong"" } },
              { ""button"": { ""id"": ""Prev/Go"", ""label"": ""Go"" } }
            ] } }
          ] } ]
        }";

        [Test]
        public void BuildViews_ProducesHierarchy_WithoutWritingPrefabs()
        {
            List<GameObject> roots = UISpecPreview.BuildViews(UISpec.FromJson(Spec));
            try
            {
                Assert.AreEqual(1, roots.Count, "one root per view");
                UIView view = roots[0].GetComponent<UIView>();
                Assert.IsNotNull(view, "preview root is a real UIView");
                Assert.IsTrue(view.id.Matches("Prev", "Screen"));
                Assert.IsNotNull(roots[0].GetComponentInChildren<UIButton>(true), "widgets are built");

                // the whole point: nothing was committed to the generated folder
                Assert.IsNull(
                    AssetDatabase.LoadAssetAtPath<GameObject>($"{UISpecGenerator.GeneratedRoot}/Views/Prev_Screen.prefab"),
                    "preview must not write prefab assets");
            }
            finally
            {
                foreach (GameObject root in roots)
                    if (root != null) Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The WYSIWYG canvas (Plan 2 Phase 4) hit-tests and drags by mapping a rendered object back to
        /// the exact <see cref="ElementSpec"/> it must mutate. That map is the generator's
        /// <see cref="UISpecGenerator.ElementObjectSink"/>: when set, every built element records its
        /// GameObject keyed by its OWN spec instance (reference equality). This pins that contract —
        /// every element in the tree (nested children included) lands in the sink with a live object.
        /// </summary>
        [Test]
        public void ElementObjectSink_MapsEverySpecElement_ToItsBuiltObject()
        {
            UISpec spec = UISpec.FromJson(Spec);
            ElementSpec vstack = spec.views[0].elements[0];
            ElementSpec text = vstack.children[0];
            ElementSpec button = vstack.children[1];

            var sink = new Dictionary<ElementSpec, GameObject>();
            UISpecGenerator.ElementObjectSink = sink;
            List<GameObject> roots = null;
            try
            {
                roots = UISpecPreview.BuildViews(spec);

                Assert.IsTrue(sink.ContainsKey(vstack), "the container element is recorded");
                Assert.IsTrue(sink.ContainsKey(text), "a nested child is recorded");
                Assert.IsTrue(sink.ContainsKey(button), "every nested child is recorded");
                Assert.IsNotNull(sink[button], "the recorded object is a live GameObject");
                // reference identity, not name-matching: the button's spec maps to a UIButton object
                Assert.IsNotNull(sink[button].GetComponentInChildren<UIButton>(true));
            }
            finally
            {
                UISpecGenerator.ElementObjectSink = null;
                if (roots != null)
                    foreach (GameObject root in roots)
                        if (root != null) Object.DestroyImmediate(root);
            }
        }

        /// <summary> The sink is opt-in: normal generation/preview never touches it, so it can't leak
        /// objects or cost anything outside the Composer. </summary>
        [Test]
        public void ElementObjectSink_IsNullByDefault()
        {
            Assert.IsNull(UISpecGenerator.ElementObjectSink,
                "the sink must default to null — only the Composer sets it, around a single build");
        }
    }
}
