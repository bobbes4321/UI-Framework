using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Runtime behavior of data-bound lists: pushing rows via <see cref="UIData"/> spawns one row
    /// per data item with {token} text filled, re-pushing rebuilds, and clearing empties — the loop
    /// real dynamic UI (inventories, shops, leaderboards) needs.
    /// </summary>
    public class DataBoundListRuntimeTests : PlayModeTestBase
    {
        private UIBoundList _list;

        // builds the list inactive, configures it, THEN activates — mirroring prefab instantiation
        // (fields deserialized before OnEnable). Adding a component to an already-active object would
        // fire OnEnable before source/template are set.
        private UIBoundList BuildList()
        {
            GameObject host = CreateUIObject("BoundList");
            host.SetActive(false);
            var binder = host.AddComponent<UIBoundList>();
            binder.source = new CategoryNameId("Inv", "Items");

            var templateGo = new GameObject("ItemTemplate", typeof(RectTransform));
            templateGo.transform.SetParent(host.transform, false);
            templateGo.AddComponent<TextMeshProUGUI>().text = "{name} x{count}";
            templateGo.SetActive(false);
            binder.template = templateGo;

            host.SetActive(true);
            return binder;
        }

        private static IDictionary<string, string> Row(string name, string count) =>
            new Dictionary<string, string> { ["name"] = name, ["count"] = count };

        // only active rows (the template stays inactive); a frame after a rebuild deferred-destroys old rows
        private string[] SpawnedTexts() => _list.GetComponentsInChildren<TMP_Text>(false)
            .Select(t => t.text).ToArray();

        [UnityTest]
        public IEnumerator SetData_SpawnsBoundRows()
        {
            _list = BuildList();
            yield return null;

            UIData.Set("Inv", "Items", new[] { Row("Blade", "3"), Row("Aegis", "1"), Row("Elixir", "9") });
            Assert.AreEqual(3, _list.spawnedCount, "one row per data item");
            yield return null;
            CollectionAssert.AreEquivalent(new[] { "Blade x3", "Aegis x1", "Elixir x9" }, SpawnedTexts(),
                "{name}/{count} tokens filled from the row");
        }

        [UnityTest]
        public IEnumerator SetData_RebuildsAndClears()
        {
            _list = BuildList();
            yield return null;

            UIData.Set("Inv", "Items", new[] { Row("A", "1"), Row("B", "2"), Row("C", "3") });
            Assert.AreEqual(3, _list.spawnedCount);
            yield return null;

            UIData.Set("Inv", "Items", new[] { Row("Only", "1") });
            Assert.AreEqual(1, _list.spawnedCount, "re-pushing rebuilds from scratch");
            yield return null; // let the previous rows' deferred Destroy complete
            CollectionAssert.AreEquivalent(new[] { "Only x1" }, SpawnedTexts());

            UIData.Clear("Inv", "Items");
            Assert.AreEqual(0, _list.spawnedCount, "clearing empties the list");
        }

        [UnityTest]
        public IEnumerator DataSetBeforeList_IsPickedUpOnEnable()
        {
            // data pushed before the list exists must still populate it when it enables
            UIData.Set("Inv", "Items", new[] { Row("Pre", "7") });
            _list = BuildList();
            yield return null;

            Assert.AreEqual(1, _list.spawnedCount, "list pulls existing data on enable");
            CollectionAssert.AreEquivalent(new[] { "Pre x7" }, SpawnedTexts());
        }
    }
}
