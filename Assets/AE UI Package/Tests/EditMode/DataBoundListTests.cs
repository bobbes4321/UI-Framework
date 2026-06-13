using System.Linq;
using AlterEyes.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// Data-bound lists: a list/grid with `bind` + `item` generates a UIBoundList whose row template
    /// is captured (inactive) and round-trips through export as bind + item (never its spawned rows).
    /// </summary>
    public class DataBoundListTests
    {
        private const string Spec = @"{
          ""views"": [ { ""id"": ""Spec/Bound"", ""elements"": [
            { ""list"": { ""bind"": ""Inv/Items"", ""item"": {
                ""button"": { ""id"": ""Row/Pick"", ""label"": ""{name}"", ""onClick"": { ""signal"": ""Inv/Pick"" } } } } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject Generate()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(Spec));
            Assert.IsEmpty(report.collisions, report.ToString());
            Assert.IsEmpty(report.issues, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Spec_Bound.prefab");
            Assert.IsNotNull(prefab, "bound list view missing");
            return prefab;
        }

        [Test]
        public void BoundList_GeneratesBinderWithInactiveTemplate()
        {
            GameObject prefab = Generate();

            var binder = prefab.GetComponentInChildren<UIBoundList>(true);
            Assert.IsNotNull(binder, "list with `bind`/`item` must get a UIBoundList");
            Assert.IsTrue(binder.source.Matches("Inv", "Items"), "binder bound to the spec's data id");
            Assert.IsNotNull(binder.template, "row template must be captured");
            Assert.IsFalse(binder.template.activeSelf, "the template must stay inactive (never a stray row)");
            Assert.IsNotNull(binder.template.GetComponent<UIButton>(), "template keeps its authored structure");

            // the binder sits on the scroll content; the template is its only child until runtime
            var scroll = prefab.GetComponentInChildren<ScrollRect>(true);
            Assert.AreSame(binder, scroll.content.GetComponent<UIBoundList>());
            Assert.AreEqual(1, scroll.content.childCount, "only the template exists at author time");
        }

        [Test]
        public void BoundList_RoundTripsAsBindAndItem()
        {
            Generate();
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Spec/Bound");
            ElementSpec list = view.elements.First(e => e.kind == "list");

            Assert.AreEqual("Inv/Items", list.bind, "data id must round-trip");
            Assert.IsNotNull(list.item, "row template must round-trip as `item`");
            Assert.AreEqual("button", list.item.kind);
            Assert.AreEqual("{name}", list.item.label, "binding tokens survive export verbatim");
            Assert.IsEmpty(list.children, "spawned rows are data, never exported as children");

            string first = exported.ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(first));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            Assert.AreEqual(first, UISpecExporter.ExportProject().ToJson(),
                "export → generate → export must stay byte-identical");
        }
    }
}
