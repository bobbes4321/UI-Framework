using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Preview-only sample-row synthesis for data-bound lists. A list/grid with <c>bind</c> + <c>item</c>
    /// bakes its row template inactive (so it never renders a stray row) — which means the static preview
    /// shows an EMPTY list, not the populated one the author is designing. This synthesizes N placeholder
    /// rows from the template's <c>{key}</c> tokens and pushes them through <see cref="UIData.Set"/> against
    /// the preview scene's data context, so the author sees a realistic, filled list.
    ///
    /// <para>This is render-time ONLY: it touches the in-memory preview's <see cref="UIData"/> store, never
    /// the baked prefab or the spec's persisted truth (the template still bakes inactive, and export still
    /// emits <c>bind</c>+<c>item</c>, never spawned rows). The preview pane clears the synthesized data after
    /// each render so a real game's <see cref="UIData"/> store is never polluted.</para>
    ///
    /// <para>No silent failure for real errors: a <c>bind</c> referencing an unknown source simply leaves the
    /// empty template (a preview convenience, not an error) — verbose-logged only.</para>
    /// </summary>
    internal static class PreviewSampleData
    {
        /// <summary> A data id the preview filled, so the pane can clear it again after the render. </summary>
        public readonly struct Binding
        {
            public readonly string category;
            public readonly string name;
            public Binding(string category, string name) { this.category = category; this.name = name; }
        }

        /// <summary>
        /// Walks the view's elements, and for every <c>bind</c>+<c>item</c> list/grid synthesizes
        /// <paramref name="rowCount"/> placeholder rows and pushes them via <see cref="UIData.Set"/>. Returns
        /// the data ids it filled (so the caller clears them after rendering). Pure model walk — safe without
        /// a graphics device or a built scene; the registered <see cref="UIBoundList"/>s rebuild off the Set.
        /// </summary>
        public static List<Binding> Populate(ViewSpec view, int rowCount)
        {
            var filled = new List<Binding>();
            if (view == null) return filled;
            rowCount = Mathf.Clamp(rowCount, 0, 200);
            foreach (ElementSpec element in view.elements)
                PopulateElement(element, rowCount, filled);
            return filled;
        }

        private static void PopulateElement(ElementSpec element, int rowCount, List<Binding> filled)
        {
            if (element == null) return;

            if (!string.IsNullOrWhiteSpace(element.bind) && element.item != null)
            {
                // mirror exactly how the generator binds the UIBoundList (CategoryNameId.Parse), so the
                // synthesized id matches the binder the build just registered. A bare name parses to
                // "None/name" — same as the generator — so the data still lands on the right list.
                CategoryNameId.Parse(element.bind, out string category, out string name);
                List<string> keys = CollectTokens(element.item);
                List<UIData.Row> rows = Synthesize(keys, rowCount);
                UIData.Set(category, name, rows);
                filled.Add(new Binding(category, name));
            }
            else if (!string.IsNullOrWhiteSpace(element.bind) && element.item == null)
            {
                // preview convenience, not an error: a bind with no item template has no row structure to
                // clone, so the list stays empty (current behavior). Verbose log only — never a warning.
                Debug.Log($"[Neo.UI] Preview sample data: list bound to '{element.bind}' has no 'item' template; leaving it empty.");
            }

            if (element.children != null)
                foreach (ElementSpec child in element.children)
                    PopulateElement(child, rowCount, filled);
        }

        /// <summary> Builds <paramref name="rowCount"/> rows; each key gets a 1-based placeholder
        /// ("Item 1", "name 2", …) so distinct columns read distinctly in the preview. A template with no
        /// <c>{key}</c> tokens still yields N rows (the row visuals repeat) via a single sentinel key. </summary>
        private static List<UIData.Row> Synthesize(List<string> keys, int rowCount)
        {
            var rows = new List<UIData.Row>(rowCount);
            bool tokenless = keys.Count == 0;
            for (int i = 0; i < rowCount; i++)
            {
                var row = new UIData.Row();
                if (tokenless)
                {
                    // no tokens to fill, but UIData.Set still spawns one instance per row — keep a row so
                    // the list shows the right COUNT of template repeats
                }
                else
                {
                    foreach (string key in keys)
                        row[key] = Placeholder(key, i + 1);
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary> "Item 1" for the canonical name-ish keys, else "key N" so every column reads. </summary>
        private static string Placeholder(string key, int oneBased)
        {
            if (string.IsNullOrEmpty(key)) return $"Item {oneBased}";
            string lower = key.ToLowerInvariant();
            if (lower == "name" || lower == "title" || lower == "label" || lower == "text")
                return $"Item {oneBased}";
            if (lower == "count" || lower == "value" || lower == "qty" || lower == "amount" || lower == "price")
                return oneBased.ToString();
            // capitalize the key as a readable column placeholder, suffixed with the row number
            return $"{char.ToUpperInvariant(key[0])}{key.Substring(1)} {oneBased}";
        }

        /// <summary> Every distinct <c>{key}</c> token across the template's text-bearing fields, in first-seen
        /// order. Mirrors what <see cref="UIBoundList.ApplyTokens"/> fills at runtime: <c>label</c> on the
        /// template and its descendants. </summary>
        private static List<string> CollectTokens(ElementSpec template)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>();
            CollectTokensInto(template, keys, seen);
            return keys;
        }

        private static void CollectTokensInto(ElementSpec element, List<string> keys, HashSet<string> seen)
        {
            if (element == null) return;
            ScanText(element.label, keys, seen);
            if (element.children != null)
                foreach (ElementSpec child in element.children)
                    CollectTokensInto(child, keys, seen);
        }

        private static void ScanText(string text, List<string> keys, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return;
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) break;
                int close = text.IndexOf('}', open + 1);
                if (close < 0) break;
                string key = text.Substring(open + 1, close - open - 1);
                if (!string.IsNullOrEmpty(key) && seen.Add(key)) keys.Add(key);
                i = close + 1;
            }
        }
    }
}
