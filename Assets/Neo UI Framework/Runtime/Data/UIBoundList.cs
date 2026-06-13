using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Populates a list/grid from a <see cref="UIData"/> source: clones an inactive item template
    /// once per row and fills <c>{key}</c> tokens in the row's text. Lives on the layout content
    /// host. The template stays inactive (so it never renders as a stray row) and is the single
    /// source of row structure; bound rows are spawned as siblings after it.
    /// </summary>
    [AddComponentMenu("Neo/UI/Data/UI Bound List")]
    public class UIBoundList : MonoBehaviour
    {
        [Tooltip("Data id this list binds to (UIData.Set with the same category/name fills it)")]
        public CategoryNameId source = new CategoryNameId();

        [Tooltip("Inactive child cloned once per row")]
        [SerializeField] private GameObject itemTemplate;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        public GameObject template
        {
            get => itemTemplate;
            set => itemTemplate = value;
        }

        public int spawnedCount => _spawned.Count;

        private void OnEnable()
        {
            UIData.Register(this);
            // pick up data that was set before this list existed
            if (UIData.TryGet(source.Category, source.Name, out List<UIData.Row> rows)) Rebuild(rows);
        }

        private void OnDisable() => UIData.Unregister(this);

        /// <summary> Clears spawned rows and rebuilds one per data row, binding text tokens. </summary>
        public void Rebuild(IReadOnlyList<IDictionary<string, string>> rows)
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Destroy(_spawned[i]);
            _spawned.Clear();

            if (itemTemplate == null || rows == null) return;

            foreach (IDictionary<string, string> row in rows)
            {
                GameObject instance = Instantiate(itemTemplate, transform);
                instance.SetActive(true);
                Bind(instance, row);
                _spawned.Add(instance);
            }
        }

        private static void Bind(GameObject row, IDictionary<string, string> data)
        {
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
                text.text = ApplyTokens(text.text, data);
        }

        /// <summary> Replaces every <c>{key}</c> with the row's value (unknown tokens stay literal). </summary>
        internal static string ApplyTokens(string template, IDictionary<string, string> data)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template;
            var sb = new StringBuilder(template.Length);
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0) { sb.Append(template, i, template.Length - i); break; }
                int close = template.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(template, i, template.Length - i); break; }
                sb.Append(template, i, open - i);
                string key = template.Substring(open + 1, close - open - 1);
                sb.Append(data != null && data.TryGetValue(key, out string value) ? value : template.Substring(open, close - open + 1));
                i = close + 1;
            }
            return sb.ToString();
        }
    }
}
