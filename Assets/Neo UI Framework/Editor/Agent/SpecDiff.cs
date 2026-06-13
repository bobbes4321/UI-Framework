using System.Collections.Generic;

namespace Neo.UI.Editor
{
    public enum SpecChangeKind { Added, Removed, Modified }

    /// <summary>
    /// One difference between two specs, addressed by a stable <see cref="SpecPath"/> string.
    /// <see cref="roundTrips"/> is true for everything <see cref="SpecDiff"/> produces (both sides
    /// are specs, so every change is representable); <see cref="OffSpecLint"/> reports the edits
    /// that do NOT round-trip separately.
    /// </summary>
    public sealed class SpecChange
    {
        public SpecChangeKind kind;
        public string path;       // e.g. "views/Menu/Main/elements[2]/background"
        public string section;    // "theme" | "view" | "popup" | "settings" | "cheats" | "flow" | "preset"
        public string before;     // serialized scalar, "(node)" for a structural node, or null
        public string after;
        public bool roundTrips = true;

        public override string ToString() =>
            kind == SpecChangeKind.Modified ? $"~ {path}: {before} → {after}"
            : kind == SpecChangeKind.Added ? $"+ {path}: {after}"
            : $"- {path}: {before}";

        public Dictionary<string, object> ToJsonObject() => new Dictionary<string, object>
        {
            ["kind"] = kind.ToString(),
            ["path"] = path,
            ["section"] = section,
            ["before"] = before,
            ["after"] = after,
            ["roundTrips"] = roundTrips
        };
    }

    /// <summary>
    /// Structural diff of two <see cref="UISpec"/> instances. Compares the canonical JSON trees
    /// node-by-node (identity rules in <see cref="SpecPath.ListKey"/>) and reports per-field
    /// modifications and per-node additions/removals. <c>Compare(spec, spec)</c> is always empty.
    /// </summary>
    public static class SpecDiff
    {
        private static readonly object Missing = new object();

        public static List<SpecChange> Compare(UISpec baseline, UISpec candidate)
        {
            var changes = new List<SpecChange>();
            object b = baseline != null ? MiniJson.Parse(baseline.ToJson()) : new Dictionary<string, object>();
            object c = candidate != null ? MiniJson.Parse(candidate.ToJson()) : new Dictionary<string, object>();
            DiffValue(b, c, "", changes);
            return changes;
        }

        private static void DiffValue(object b, object c, string path, List<SpecChange> changes)
        {
            bool bAbsent = ReferenceEquals(b, Missing);
            bool cAbsent = ReferenceEquals(c, Missing);
            if (bAbsent && cAbsent) return;

            if (!bAbsent && !cAbsent
                && b is Dictionary<string, object> bd && c is Dictionary<string, object> cd)
            {
                DiffDict(bd, cd, path, changes);
                return;
            }
            if (!bAbsent && !cAbsent && b is List<object> bl && c is List<object> cl)
            {
                DiffList(bl, cl, path, changes);
                return;
            }

            // scalar, presence change, or a node added/removed wholesale
            if (bAbsent)
                changes.Add(Change(SpecChangeKind.Added, path, null, SpecPath.Scalar(c)));
            else if (cAbsent)
                changes.Add(Change(SpecChangeKind.Removed, path, SpecPath.Scalar(b), null));
            else if (!JsonEqual(b, c))
                changes.Add(Change(SpecChangeKind.Modified, path, SpecPath.Scalar(b), SpecPath.Scalar(c)));
        }

        private static void DiffDict(Dictionary<string, object> b, Dictionary<string, object> c,
            string path, List<SpecChange> changes)
        {
            // candidate keys first (added/modified in candidate order), then baseline-only (removed)
            var handled = new HashSet<string>();
            foreach (KeyValuePair<string, object> entry in c)
            {
                handled.Add(entry.Key);
                DiffValue(b.TryGetValue(entry.Key, out object bv) ? bv : Missing, entry.Value,
                    Combine(path, entry.Key), changes);
            }
            foreach (KeyValuePair<string, object> entry in b)
                if (!handled.Contains(entry.Key))
                    DiffValue(entry.Value, Missing, Combine(path, entry.Key), changes);
        }

        private static void DiffList(List<object> b, List<object> c, string path, List<SpecChange> changes)
        {
            string listName = SpecPath.ListName(path);

            var baseByKey = new Dictionary<string, (object item, int index)>();
            for (int i = 0; i < b.Count; i++)
            {
                string key = SpecPath.ListKey(listName, b[i], i);
                if (!baseByKey.ContainsKey(key)) baseByKey[key] = (b[i], i);
            }

            var matched = new HashSet<string>();
            for (int i = 0; i < c.Count; i++)
            {
                string key = SpecPath.ListKey(listName, c[i], i);
                string childPath = SpecPath.ChildPath(path, listName, c[i], i);
                if (baseByKey.TryGetValue(key, out (object item, int index) bv))
                {
                    matched.Add(key);
                    DiffValue(bv.item, c[i], childPath, changes);
                }
                else
                {
                    DiffValue(Missing, c[i], childPath, changes);
                }
            }
            for (int i = 0; i < b.Count; i++)
            {
                string key = SpecPath.ListKey(listName, b[i], i);
                if (matched.Contains(key)) continue;
                // a duplicate key already paired counts as matched
                if (baseByKey.TryGetValue(key, out (object item, int index) bv) && bv.index != i) continue;
                DiffValue(b[i], Missing, SpecPath.ChildPath(path, listName, b[i], i), changes);
            }
        }

        private static string Combine(string path, string key) =>
            string.IsNullOrEmpty(path) ? key : $"{path}/{key}";

        private static SpecChange Change(SpecChangeKind kind, string path, string before, string after) =>
            new SpecChange
            {
                kind = kind,
                path = path,
                section = SpecPath.SectionOf(path),
                before = before,
                after = after,
                roundTrips = true
            };

        /// <summary> Deep value equality over MiniJson shapes (dict / list / scalar). </summary>
        internal static bool JsonEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return a == null && b == null;
            if (a is Dictionary<string, object> ad && b is Dictionary<string, object> bd)
            {
                if (ad.Count != bd.Count) return false;
                foreach (KeyValuePair<string, object> kv in ad)
                    if (!bd.TryGetValue(kv.Key, out object bv) || !JsonEqual(kv.Value, bv)) return false;
                return true;
            }
            if (a is List<object> al && b is List<object> bl)
            {
                if (al.Count != bl.Count) return false;
                for (int i = 0; i < al.Count; i++)
                    if (!JsonEqual(al[i], bl[i])) return false;
                return true;
            }
            if (a is double da && b is double db) return da.Equals(db);
            return a.Equals(b);
        }
    }
}
