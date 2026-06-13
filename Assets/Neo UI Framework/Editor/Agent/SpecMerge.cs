using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary> Who wins when a node changed in BOTH the human-drifted project and the incoming spec. </summary>
    public enum ConflictPolicy
    {
        /// <summary> Agent intent wins on collision (default). </summary>
        PreferTheirs,
        /// <summary> Human edit wins on collision. </summary>
        PreferOurs,
        /// <summary> Record the conflict and keep theirs, but flag the merge as failed. </summary>
        Fail
    }

    public sealed class MergeResult
    {
        public UISpec merged;
        public List<SpecChange> applied = new List<SpecChange>();    // human changes folded in
        public List<SpecChange> conflicts = new List<SpecChange>();  // changed in BOTH sides, different values
        public List<OffSpecFinding> dropped = new List<OffSpecFinding>(); // off-spec edits that can't be merged
        /// <summary> True when <see cref="ConflictPolicy.Fail"/> hit at least one conflict. </summary>
        public bool failed;
    }

    /// <summary>
    /// Three-way merge of <see cref="UISpec"/> trees: <c>base</c> = the last generated baseline,
    /// <c>ours</c> = the human-drifted (exported) project, <c>theirs</c> = the incoming spec (e.g.
    /// the agent's new version). Produces ours∪theirs with collisions surfaced rather than swallowed
    /// — the core "no lost work" guarantee. Works on the canonical JSON tree so identity and field
    /// rules stay identical to <see cref="SpecDiff"/>.
    /// </summary>
    public static class SpecMerge
    {
        private static readonly object Missing = new object();

        public static MergeResult Merge(UISpec baseLine, UISpec ours, UISpec theirs,
            ConflictPolicy policy = ConflictPolicy.PreferTheirs)
        {
            var result = new MergeResult();
            object b = baseLine != null ? MiniJson.Parse(baseLine.ToJson()) : new Dictionary<string, object>();
            object o = ours != null ? MiniJson.Parse(ours.ToJson()) : new Dictionary<string, object>();
            object t = theirs != null ? MiniJson.Parse(theirs.ToJson()) : new Dictionary<string, object>();

            object merged = MergeValue(b, o, t, "", policy, result);
            string json = MiniJson.Serialize(ReferenceEquals(merged, Missing) ? new Dictionary<string, object>() : merged);
            result.merged = UISpec.FromJson(json);
            return result;
        }

        private static object MergeValue(object b, object o, object t, string path,
            ConflictPolicy policy, MergeResult result)
        {
            bool oAbsent = ReferenceEquals(o, Missing);
            bool tAbsent = ReferenceEquals(t, Missing);

            // both sides present and structurally compatible → recurse for leaf-level granularity
            if (!oAbsent && !tAbsent)
            {
                if (o is Dictionary<string, object> od && t is Dictionary<string, object> td)
                {
                    var bd = b as Dictionary<string, object>;
                    return MergeDict(bd, od, td, path, policy, result);
                }
                if (o is List<object> ol && t is List<object> tl)
                {
                    var bl = b as List<object>;
                    return MergeList(bl, ol, tl, path, policy, result);
                }
            }
            return MergeScalar(b, o, t, path, policy, result);
        }

        /// <summary> Canonical three-way scalar merge, extended for additions and removals. </summary>
        private static object MergeScalar(object b, object o, object t, string path,
            ConflictPolicy policy, MergeResult result)
        {
            bool bAbsent = ReferenceEquals(b, Missing);
            bool oAbsent = ReferenceEquals(o, Missing);
            bool tAbsent = ReferenceEquals(t, Missing);

            bool oChanged = !ValEqual(o, b);
            bool tChanged = !ValEqual(t, b);

            if (!oChanged && !tChanged) return tAbsent ? Missing : t; // nobody touched it
            if (!oChanged) return tAbsent ? Missing : t;               // theirs only (incl. their removal)
            if (!tChanged)                                             // ours only
            {
                Record(result.applied, b, o, path);
                return oAbsent ? Missing : o;
            }

            // both changed
            if (ValEqual(o, t)) return tAbsent ? Missing : t; // same change on both sides

            // remove-vs-modify: a removal only wins if the OTHER side didn't modify the node
            if (oAbsent && !bAbsent) { return tAbsent ? Missing : t; } // ours removed, theirs modified → keep theirs
            if (tAbsent && !bAbsent) { Record(result.applied, b, o, path); return o; } // theirs removed, ours modified → keep ours

            // genuine value conflict
            var conflict = new SpecChange
            {
                kind = SpecChangeKind.Modified,
                path = path,
                section = SpecPath.SectionOf(path),
                before = oAbsent ? null : SpecPath.Scalar(o),  // the human value at risk
                after = tAbsent ? null : SpecPath.Scalar(t),   // the incoming value
                roundTrips = true
            };
            result.conflicts.Add(conflict);

            switch (policy)
            {
                case ConflictPolicy.PreferOurs:
                    return oAbsent ? Missing : o;
                case ConflictPolicy.Fail:
                    result.failed = true;
                    return tAbsent ? Missing : t;
                default:
                    return tAbsent ? Missing : t;
            }
        }

        private static object MergeDict(Dictionary<string, object> b, Dictionary<string, object> o,
            Dictionary<string, object> t, string path, ConflictPolicy policy, MergeResult result)
        {
            var merged = new Dictionary<string, object>();
            // theirs key order first (preserve agent's canonical ordering), then ours-only keys
            var keys = new List<string>();
            var seen = new HashSet<string>();
            foreach (string k in t.Keys) if (seen.Add(k)) keys.Add(k);
            foreach (string k in o.Keys) if (seen.Add(k)) keys.Add(k);
            if (b != null) foreach (string k in b.Keys) if (seen.Add(k)) keys.Add(k);

            foreach (string key in keys)
            {
                object bv = b != null && b.TryGetValue(key, out object bvv) ? bvv : Missing;
                object ov = o.TryGetValue(key, out object ovv) ? ovv : Missing;
                object tv = t.TryGetValue(key, out object tvv) ? tvv : Missing;
                object child = MergeValue(bv, ov, tv, Combine(path, key), policy, result);
                if (!ReferenceEquals(child, Missing)) merged[key] = child;
            }
            return merged;
        }

        private static object MergeList(List<object> b, List<object> o, List<object> t,
            string path, ConflictPolicy policy, MergeResult result)
        {
            string listName = SpecPath.ListName(path);
            Dictionary<string, object> bMap = KeyMap(b, listName);
            Dictionary<string, object> oMap = KeyMap(o, listName);
            Dictionary<string, object> tMap = KeyMap(t, listName);

            // theirs order, then ours-only adds, then base-only leftovers
            var keys = new List<string>();
            var seen = new HashSet<string>();
            void Collect(List<object> list)
            {
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    string key = SpecPath.ListKey(listName, list[i], i);
                    if (seen.Add(key)) keys.Add(key);
                }
            }
            Collect(t);
            Collect(o);
            Collect(b);

            var merged = new List<object>();
            foreach (string key in keys)
            {
                object bv = bMap.TryGetValue(key, out object bvv) ? bvv : Missing;
                object ov = oMap.TryGetValue(key, out object ovv) ? ovv : Missing;
                object tv = tMap.TryGetValue(key, out object tvv) ? tvv : Missing;
                // use the candidate index from whichever side carries it, for the change path
                object representative = !ReferenceEquals(tv, Missing) ? tv : (!ReferenceEquals(ov, Missing) ? ov : bv);
                string childPath = SpecPath.ChildPath(path, listName, representative, merged.Count);
                object child = MergeValue(bv, ov, tv, childPath, policy, result);
                if (!ReferenceEquals(child, Missing)) merged.Add(child);
            }
            return merged;
        }

        private static Dictionary<string, object> KeyMap(List<object> list, string listName)
        {
            var map = new Dictionary<string, object>();
            if (list == null) return map;
            for (int i = 0; i < list.Count; i++)
            {
                string key = SpecPath.ListKey(listName, list[i], i);
                if (!map.ContainsKey(key)) map[key] = list[i];
            }
            return map;
        }

        private static void Record(List<SpecChange> into, object b, object o, string path)
        {
            bool bAbsent = ReferenceEquals(b, Missing);
            bool oAbsent = ReferenceEquals(o, Missing);
            into.Add(new SpecChange
            {
                kind = bAbsent ? SpecChangeKind.Added : oAbsent ? SpecChangeKind.Removed : SpecChangeKind.Modified,
                path = path,
                section = SpecPath.SectionOf(path),
                before = bAbsent ? null : SpecPath.Scalar(b),
                after = oAbsent ? null : SpecPath.Scalar(o),
                roundTrips = true
            });
        }

        private static bool ValEqual(object a, object b)
        {
            if (ReferenceEquals(a, Missing) || ReferenceEquals(b, Missing))
                return ReferenceEquals(a, Missing) && ReferenceEquals(b, Missing);
            return SpecDiff.JsonEqual(a, b);
        }

        private static string Combine(string path, string key) =>
            string.IsNullOrEmpty(path) ? key : $"{path}/{key}";
    }
}
