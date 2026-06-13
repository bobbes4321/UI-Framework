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
    }
}
