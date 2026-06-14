using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Cross-references the ids actually used by UISpecs against the entries declared in the id
    /// databases — the ID Database Manager's "usage / orphan" view. It collects, per id
    /// <see cref="System.Type"/>, every "Category/Name" a spec references (the same element-kind →
    /// id-type mapping the generator and <see cref="IdDatabaseOptions.ForElementKind"/> use, plus view /
    /// panel / stream / tab references), then per database reports:
    /// <list type="bullet">
    /// <item>ORPHAN — present in the database but referenced by no scanned spec.</item>
    /// <item>DANGLING — referenced by a spec but absent from any database of that id type.</item>
    /// </list>
    /// Scanning is explicit (a "Scan" button), never per-OnGUI: parsing every spec JSON under Assets is
    /// not cheap. The scan SURFACES what it covered (file count + parse errors) rather than silently
    /// capping coverage. Reference collection is pure and is exercised directly by EditMode tests.
    /// </summary>
    public static class IdUsageScanner
    {
        /// <summary> A "Category/Name" reference (value-equal, so it dedupes in a HashSet). </summary>
        public readonly struct Ref : IEquatable<Ref>
        {
            public readonly string category;
            public readonly string name;

            public Ref(string category, string name)
            {
                this.category = string.IsNullOrWhiteSpace(category) ? CategoryNameId.DefaultCategory : category.Trim();
                this.name = string.IsNullOrWhiteSpace(name) ? CategoryNameId.DefaultName : name.Trim();
            }

            public static Ref Parse(string slashed)
            {
                CategoryNameId.Parse(slashed, out string c, out string n);
                return new Ref(c, n);
            }

            public bool IsDefault => category == CategoryNameId.DefaultCategory && name == CategoryNameId.DefaultName;

            public bool Equals(Ref other) =>
                string.Equals(category, other.category, StringComparison.Ordinal)
                && string.Equals(name, other.name, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is Ref other && Equals(other);
            public override int GetHashCode() { unchecked { return (category.GetHashCode() * 397) ^ name.GetHashCode(); } }
            public override string ToString() => $"{category}/{name}";
        }

        /// <summary> The referenced ids found across the scanned specs, bucketed by id <see cref="Type"/>. </summary>
        public sealed class Usage
        {
            /// <summary> id Type → the set of Category/Name references found for it. </summary>
            public readonly Dictionary<Type, HashSet<Ref>> referencesByType = new Dictionary<Type, HashSet<Ref>>();
            /// <summary> Spec files parsed successfully. </summary>
            public int filesScanned;
            /// <summary> "path: message" for every spec that failed to parse (surfaced, never swallowed). </summary>
            public readonly List<string> parseErrors = new List<string>();

            public HashSet<Ref> For(Type idType)
            {
                if (idType == null) return new HashSet<Ref>();
                return referencesByType.TryGetValue(idType, out HashSet<Ref> set) ? set : new HashSet<Ref>();
            }

            internal void Add(Type idType, string category, string name)
            {
                if (idType == null) return;
                var reference = new Ref(category, name);
                if (reference.IsDefault) return; // "None/None" is the unset sentinel, not a real reference
                if (!referencesByType.TryGetValue(idType, out HashSet<Ref> set))
                    referencesByType[idType] = set = new HashSet<Ref>();
                set.Add(reference);
            }

            internal void Add(Type idType, string slashed)
            {
                if (string.IsNullOrWhiteSpace(slashed)) return;
                CategoryNameId.Parse(slashed, out string c, out string n);
                Add(idType, c, n);
            }
        }

        /// <summary> Orphan / dangling outcome for ONE database. </summary>
        public sealed class DatabaseReport
        {
            /// <summary> Declared in the database but referenced by no scanned spec. </summary>
            public readonly List<Ref> orphans = new List<Ref>();
            /// <summary> Referenced by a spec but absent from the database (broken/unresolved id). </summary>
            public readonly List<Ref> dangling = new List<Ref>();
        }

        // -------------------------------------------------------------------- reference collection (pure)

        /// <summary>
        /// Collects every id reference from a single spec into <paramref name="usage"/>. Drives off the
        /// single <see cref="IdRefSlots.Visit"/> enumeration — the ONE place that encodes which fields of a
        /// UISpec hold an id reference — so the reader (this) and the rewriter
        /// (<see cref="IdReferenceRewriter"/>) can never drift over what an id reference is.
        /// </summary>
        public static void Collect(UISpec spec, Usage usage)
        {
            if (spec == null || usage == null) return;
            IdRefSlots.Visit(spec, slot =>
            {
                slot.Get(out string category, out string name);
                usage.Add(slot.IdType, category, name);
            });
        }

        /// <summary>
        /// Element/menu-item kind → addressed id type. The same mapping as
        /// <see cref="IdDatabaseOptions.ForElementKind"/>, by TYPE so it is settings-independent and
        /// unit-testable (the options API returns the resolved database instance). A project kind
        /// supplies its own preferred type through <see cref="IElementKindIdDatabase"/>.
        /// </summary>
        public static Type IdTypeForKind(string kind)
        {
            switch (kind)
            {
                case "button":
                case "stepper": return typeof(ButtonId);
                case "toggle":
                case "switch":
                case "tab":     return typeof(ToggleId);
                case "slider":  return typeof(SliderId);
                case "dropdown": return typeof(DropdownId);
                default:
                    if (NeoElementKinds.TryGet(kind, out INeoElementKind ek)
                        && ek is IElementKindIdDatabase withDb)
                        return withDb.PreferredIdType;
                    return null;
            }
        }

        // -------------------------------------------------------------------- cross-reference (pure)

        /// <summary>
        /// Compares a database's declared entries against the references found for <paramref name="idType"/>
        /// in <paramref name="usage"/>, producing orphans (in DB, unreferenced) and dangling refs
        /// (referenced, not in DB). Pure — no asset I/O — so it is unit-tested directly.
        /// </summary>
        public static DatabaseReport Reconcile(IdDatabase database, Type idType, Usage usage)
        {
            var report = new DatabaseReport();
            HashSet<Ref> referenced = usage != null ? usage.For(idType) : new HashSet<Ref>();

            var declared = new HashSet<Ref>();
            if (database != null)
                foreach (IdDatabase.IdCategory category in database.Categories)
                {
                    if (category?.names == null) continue;
                    foreach (string name in category.names)
                    {
                        var reference = new Ref(category.category, name);
                        declared.Add(reference);
                        if (!referenced.Contains(reference)) report.orphans.Add(reference);
                    }
                }

            foreach (Ref reference in referenced)
                if (!declared.Contains(reference)) report.dangling.Add(reference);

            report.orphans.Sort(CompareRef);
            report.dangling.Sort(CompareRef);
            return report;
        }

        private static int CompareRef(Ref a, Ref b)
        {
            int byCategory = string.CompareOrdinal(a.category, b.category);
            return byCategory != 0 ? byCategory : string.CompareOrdinal(a.name, b.name);
        }

        // -------------------------------------------------------------------- project scan (editor I/O)

        /// <summary>
        /// Parses every UISpec JSON found under <c>Assets/</c> (by file extension/heuristic) and
        /// collects its referenced ids. EXPLICIT (call from a Scan button), never per-OnGUI. Files that
        /// don't parse as a UISpec are recorded in <see cref="Usage.parseErrors"/> and skipped — the
        /// caller surfaces the count so coverage is visible, not silently capped.
        /// </summary>
        public static Usage ScanProject()
        {
            var usage = new Usage();
            foreach (string path in EnumerateSpecFiles())
                TryScanFile(path, usage);
            return usage;
        }

        /// <summary>
        /// Every UISpec source-of-truth JSON file the tooling treats as authoritative, de-duplicated:
        /// the <c>*.json</c> text assets under <c>Assets/</c> (mockups, generated exports) PLUS the
        /// hidden <c>.neo-baseline.json</c> baseline (a dotfile the AssetDatabase ignores, so it must be
        /// added by hand — otherwise a rename leaves it stale and a later drift check shows phantom
        /// drift). Shared by the usage scan AND <see cref="IdReferenceRewriter"/> so both operate over
        /// the exact same set of files (no second, drifting discovery path).
        /// </summary>
        public static IEnumerable<string> EnumerateSpecFiles()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // the hidden baseline first — it lives under GeneratedRoot and Unity does not index dotfiles
            string baseline = NeoBaseline.Path;
            if (!string.IsNullOrEmpty(baseline) && File.Exists(baseline) && seen.Add(NormalizePath(baseline)))
                yield return baseline;

            // *.json under Assets — UISpecs live as .json. We try-parse each; non-spec JSON simply fails
            // the parse and is reported, not crashed on.
            string[] guids = AssetDatabase.FindAssets("t:TextAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(NormalizePath(path))) continue;
                yield return path;
            }
        }

        private static string NormalizePath(string path) =>
            string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');

        /// <summary>
        /// Cheap pre-filter: only files that look like a UISpec are worth a full parse. Shared so the
        /// scanner and the rewriter agree on which files to even open.
        /// </summary>
        public static bool LooksLikeSpec(string json) =>
            !string.IsNullOrEmpty(json)
            && (json.IndexOf("\"views\"", StringComparison.Ordinal) >= 0
                || json.IndexOf("\"popups\"", StringComparison.Ordinal) >= 0
                || json.IndexOf("\"flow\"", StringComparison.Ordinal) >= 0
                || json.IndexOf("\"settings\"", StringComparison.Ordinal) >= 0
                || json.IndexOf("\"cheats\"", StringComparison.Ordinal) >= 0);

        private static void TryScanFile(string path, Usage usage)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception e) { usage.parseErrors.Add($"{path}: {e.Message}"); return; }

            if (!LooksLikeSpec(json)) return;

            try
            {
                UISpec spec = UISpec.FromJson(json);
                Collect(spec, usage);
                usage.filesScanned++;
            }
            catch (Exception e)
            {
                usage.parseErrors.Add($"{path}: {e.Message}");
            }
        }
    }
}
