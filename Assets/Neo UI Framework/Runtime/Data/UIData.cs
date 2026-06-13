using System;
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
    /// The typed overload lets game code keep its own model type and supply the projection to row
    /// tokens once, with row-level <see cref="Update{T}"/>/<see cref="Add{T}"/>/<see cref="RemoveAt"/>
    /// that patch a single spawned row instead of rebuilding the whole list:
    /// <code>
    /// UIData.Set&lt;Deal&gt;("Shop", "Deals", deals,
    ///     d =&gt; new Dictionary&lt;string,string&gt; { ["name"]=d.Name, ["price"]=d.Price.ToString() });
    /// UIData.Update("Shop", "Deals", 0, deals[0]); // re-tokens only row 0
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
        // remembered projection per id so the typed row-level ops (Update/Add) can re-derive tokens
        // from a domain row without the caller passing the projection again at every call site
        private static readonly Dictionary<string, Func<object, IReadOnlyDictionary<string, string>>> Projections =
            new Dictionary<string, Func<object, IReadOnlyDictionary<string, string>>>();
        private static readonly HashSet<UIBoundList> Lists = new HashSet<UIBoundList>();

        private static string Key(string category, string name) => $"{category}/{name}";

        // ------------------------------------------------------------------ string API (unchanged)

        /// <summary> Sets the rows for an id and rebuilds every list bound to it. </summary>
        public static void Set(string category, string name, IEnumerable<IDictionary<string, string>> rows)
        {
            var stored = rows == null
                ? new List<Row>()
                : rows.Select(r => new Row(r)).ToList();
            string key = Key(category, name);
            Store[key] = stored;
            Projections.Remove(key); // a raw string Set has no domain projection to remember
            RebuildBound(category, name, stored);
        }

        /// <summary> Clears an id's rows (bound lists empty out). </summary>
        public static void Clear(string category, string name) => Set(category, name, null);

        public static bool TryGet(string category, string name, out List<Row> rows) =>
            Store.TryGetValue(Key(category, name), out rows);

        // ------------------------------------------------------------------ typed API (additive)

        /// <summary>
        /// Sets the rows from a typed sequence, projecting each item to its <c>{key}</c> tokens once.
        /// The projection is remembered so <see cref="Update{T}"/>/<see cref="Add{T}"/> can patch a
        /// single row without the caller rebuilding the dictionary by hand.
        /// </summary>
        public static void Set<T>(string category, string name, IEnumerable<T> rows,
            Func<T, IReadOnlyDictionary<string, string>> project)
        {
            if (project == null)
            {
                Debug.LogWarning($"[Neo.UI] UIData.Set<{typeof(T).Name}>('{category}/{name}') needs a projection " +
                                 "(domain row → token map); ignoring.");
                return;
            }
            string key = Key(category, name);
            Projections[key] = o => project((T)o);
            var stored = rows == null
                ? new List<Row>()
                : rows.Select(r => ToRow(project(r))).ToList();
            Store[key] = stored;
            RebuildBound(category, name, stored);
        }

        /// <summary> Replaces one row in place, re-tokening only its spawned instance (no full rebuild). </summary>
        public static void Update<T>(string category, string name, int index, T row)
        {
            string key = Key(category, name);
            if (!Store.TryGetValue(key, out List<Row> rows))
            {
                Debug.LogWarning($"[Neo.UI] UIData.Update('{category}/{name}', {index}) — no data set for this id; ignoring.");
                return;
            }
            if (index < 0 || index >= rows.Count)
            {
                Debug.LogWarning($"[Neo.UI] UIData.Update('{category}/{name}', {index}) — index out of range (count {rows.Count}); ignoring.");
                return;
            }
            if (!TryProject(key, row, out Row projected, "Update")) return;
            rows[index] = projected;
            foreach (UIBoundList list in BoundLists(category, name)) list.UpdateRow(index, projected);
        }

        /// <summary> Appends one row, spawning a single new instance (no full rebuild). </summary>
        public static void Add<T>(string category, string name, T row)
        {
            string key = Key(category, name);
            if (!Store.TryGetValue(key, out List<Row> rows))
            {
                // first Add without a prior Set has nothing to remember a projection from
                Debug.LogWarning($"[Neo.UI] UIData.Add('{category}/{name}') — no data set for this id (call Set<T> first); ignoring.");
                return;
            }
            if (!TryProject(key, row, out Row projected, "Add")) return;
            rows.Add(projected);
            int index = rows.Count - 1;
            foreach (UIBoundList list in BoundLists(category, name)) list.InsertRow(index, projected);
        }

        /// <summary> Removes one row, destroying only its spawned instance (no full rebuild). </summary>
        public static void RemoveAt(string category, string name, int index)
        {
            string key = Key(category, name);
            if (!Store.TryGetValue(key, out List<Row> rows))
            {
                Debug.LogWarning($"[Neo.UI] UIData.RemoveAt('{category}/{name}', {index}) — no data set for this id; ignoring.");
                return;
            }
            if (index < 0 || index >= rows.Count)
            {
                Debug.LogWarning($"[Neo.UI] UIData.RemoveAt('{category}/{name}', {index}) — index out of range (count {rows.Count}); ignoring.");
                return;
            }
            rows.RemoveAt(index);
            foreach (UIBoundList list in BoundLists(category, name)) list.RemoveRow(index);
        }

        // ------------------------------------------------------------------ internals

        private static bool TryProject<T>(string key, T row, out Row projected, string op)
        {
            projected = null;
            if (!Projections.TryGetValue(key, out Func<object, IReadOnlyDictionary<string, string>> project))
            {
                Debug.LogWarning($"[Neo.UI] UIData.{op}('{key}') — no projection remembered (populate with Set<T> first); ignoring.");
                return false;
            }
            projected = ToRow(project(row));
            return true;
        }

        private static Row ToRow(IReadOnlyDictionary<string, string> source)
        {
            var row = new Row();
            if (source != null)
                foreach (KeyValuePair<string, string> kv in source) row[kv.Key] = kv.Value;
            return row;
        }

        private static List<UIBoundList> BoundLists(string category, string name) =>
            Lists.Where(l => l != null && l.source.Matches(category, name)).ToList();

        private static void RebuildBound(string category, string name, List<Row> rows)
        {
            foreach (UIBoundList list in BoundLists(category, name)) list.Rebuild(rows);
        }

        // bound lists register so a later Set() finds them; they also pull current data on enable
        internal static void Register(UIBoundList list) => Lists.Add(list);
        internal static void Unregister(UIBoundList list) => Lists.Remove(list);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Store.Clear();
            Projections.Clear();
            Lists.Clear();
        }
    }
}
