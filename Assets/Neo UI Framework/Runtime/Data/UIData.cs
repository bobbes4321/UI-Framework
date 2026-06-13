using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Agent-first data registry for data-bound lists: push rows by category/name and every
    /// <see cref="UIBoundList"/> bound to that id rebuilds. Rows are plain string→string maps so
    /// the data layer stays decoupled from the UI (no shared model types) and bindings stay
    /// greppable — a row's values fill <c>{key}</c> tokens in the template's text.
    /// <code>
    /// UIData.Set("Inventory", "Items", new[] {
    ///     new Dictionary&lt;string,string&gt; { ["name"]="Blade", ["count"]="3" },
    ///     new Dictionary&lt;string,string&gt; { ["name"]="Aegis", ["count"]="1" },
    /// });
    /// </code>
    /// </summary>
    public static class UIData
    {
        public sealed class Row : Dictionary<string, string>
        {
            public Row() { }
            public Row(IDictionary<string, string> source) : base(source) { }
        }

        private static readonly Dictionary<string, List<Row>> Store = new Dictionary<string, List<Row>>();
        private static readonly HashSet<UIBoundList> Lists = new HashSet<UIBoundList>();

        private static string Key(string category, string name) => $"{category}/{name}";

        /// <summary> Sets the rows for an id and rebuilds every list bound to it. </summary>
        public static void Set(string category, string name, IEnumerable<IDictionary<string, string>> rows)
        {
            var stored = rows == null
                ? new List<Row>()
                : rows.Select(r => new Row(r)).ToList();
            Store[Key(category, name)] = stored;
            foreach (UIBoundList list in Lists.Where(l => l != null && l.source.Matches(category, name)).ToList())
                list.Rebuild(stored);
        }

        /// <summary> Clears an id's rows (bound lists empty out). </summary>
        public static void Clear(string category, string name) => Set(category, name, null);

        public static bool TryGet(string category, string name, out List<Row> rows) =>
            Store.TryGetValue(Key(category, name), out rows);

        // bound lists register so a later Set() finds them; they also pull current data on enable
        internal static void Register(UIBoundList list) => Lists.Add(list);
        internal static void Unregister(UIBoundList list) => Lists.Remove(list);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Store.Clear();
            Lists.Clear();
        }
    }
}
