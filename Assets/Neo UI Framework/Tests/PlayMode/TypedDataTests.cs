using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Plan 3 C: the typed <c>UIData.Set&lt;T&gt;</c> + a one-time projection populates a
    /// <see cref="UIBoundList"/>, and the row-level <c>Update/Add/RemoveAt</c> ops patch a single
    /// spawned row instead of rebuilding the whole list. The string API stays untouched.
    /// </summary>
    public class TypedDataTests
    {
        private sealed class Item
        {
            public string Name;
            public int Count;
        }

        private static IReadOnlyDictionary<string, string> Project(Item i) =>
            new Dictionary<string, string> { ["name"] = i.Name, ["count"] = i.Count.ToString() };

        private const string Category = "Inv";
        private const string Name = "Items";

        private readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            UIData.Clear(Category, Name);
            foreach (Object obj in _cleanup) if (obj != null) Object.Destroy(obj);
            _cleanup.Clear();
        }

        private UIBoundList MakeList()
        {
            var content = new GameObject("Content", typeof(RectTransform));
            _cleanup.Add(content);
            var template = new GameObject("ItemTemplate", typeof(RectTransform));
            template.transform.SetParent(content.transform, false);
            template.AddComponent<TextMeshProUGUI>().text = "{name} x{count}";
            template.SetActive(false);

            var list = content.AddComponent<UIBoundList>();
            list.template = template;
            list.source = new CategoryNameId(Category, Name);
            return list;
        }

        private static string RowText(UIBoundList list, int index)
        {
            var rows = new List<string>();
            foreach (Transform child in list.transform)
            {
                if (!child.gameObject.activeSelf) continue; // skip the inactive template
                TMP_Text text = child.GetComponentInChildren<TMP_Text>(false);
                if (text != null) rows.Add(text.text);
            }
            return rows[index];
        }

        [UnityTest]
        public IEnumerator SetTyped_PopulatesFromProjection()
        {
            UIBoundList list = MakeList();
            UIData.Set<Item>(Category, Name, new[]
            {
                new Item { Name = "Blade", Count = 3 },
                new Item { Name = "Aegis", Count = 1 }
            }, Project);
            yield return null;

            Assert.AreEqual(2, list.spawnedCount);
            Assert.AreEqual("Blade x3", RowText(list, 0));
            Assert.AreEqual("Aegis x1", RowText(list, 1));
        }

        [UnityTest]
        public IEnumerator UpdateRow_PatchesOneRow_LeavingOthers()
        {
            UIBoundList list = MakeList();
            UIData.Set<Item>(Category, Name, new[]
            {
                new Item { Name = "Blade", Count = 3 },
                new Item { Name = "Aegis", Count = 1 }
            }, Project);
            yield return null;

            UIData.Update(Category, Name, 0, new Item { Name = "Spear", Count = 9 });
            yield return null; // let the deferred Destroy of the old row resolve

            Assert.AreEqual(2, list.spawnedCount, "the row count is unchanged");
            Assert.AreEqual("Spear x9", RowText(list, 0), "only row 0 is re-tokened");
            Assert.AreEqual("Aegis x1", RowText(list, 1), "row 1 is left untouched");
        }

        [UnityTest]
        public IEnumerator Add_And_RemoveAt_SpawnSingleRows()
        {
            UIBoundList list = MakeList();
            UIData.Set<Item>(Category, Name, new[] { new Item { Name = "Blade", Count = 3 } }, Project);
            yield return null;

            UIData.Add(Category, Name, new Item { Name = "Aegis", Count = 1 });
            yield return null;
            Assert.AreEqual(2, list.spawnedCount);
            Assert.AreEqual("Aegis x1", RowText(list, 1), "the appended row binds its own tokens");

            UIData.RemoveAt(Category, Name, 0);
            yield return null;
            Assert.AreEqual(1, list.spawnedCount);
            Assert.AreEqual("Aegis x1", RowText(list, 0), "the survivor stays bound");
        }

        [UnityTest]
        public IEnumerator StringApi_StillPopulates()
        {
            UIBoundList list = MakeList();
            UIData.Set(Category, Name, new[]
            {
                new Dictionary<string, string> { ["name"] = "Bow", ["count"] = "7" }
            });
            yield return null;

            Assert.AreEqual(1, list.spawnedCount);
            Assert.AreEqual("Bow x7", RowText(list, 0));
        }
    }
}
