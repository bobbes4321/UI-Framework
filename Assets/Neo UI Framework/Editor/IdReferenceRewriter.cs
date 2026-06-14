using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// "Closes the loop" on an id rename: when the ID Database Manager renames a category or a name, this
    /// rewrites every matching reference across the project's UISpec source-of-truth files so the spec —
    /// which is canonical — stays in sync with the database (a regenerate/sync then materializes the
    /// change into prefabs/scenes; we never touch those binary fields directly).
    ///
    /// It drives entirely off the shared <see cref="IdRefSlots.Visit"/> enumeration and the slot's
    /// <see cref="IdRefSlot.IdType"/>, so it visits the EXACT same locations the usage scanner reads and
    /// touches only references of the renamed database's id-type — a ButtonId "Action/Play" is never
    /// rewritten when renaming a ViewId "Action/Play".
    ///
    /// Reference scope (spec source-of-truth only): every <c>*.json</c> UISpec the scanner finds under
    /// <c>Assets/</c> PLUS the hidden <c>.neo-baseline.json</c> baseline (so a later drift check doesn't
    /// show phantom drift). Both come from <see cref="IdUsageScanner.EnumerateSpecFiles"/>, so the
    /// rewriter and the scanner always agree on the file set. Prefabs/scenes and generated binding stubs
    /// are intentionally NOT rewritten.
    /// </summary>
    public static class IdReferenceRewriter
    {
        /// <summary>
        /// A pending id rename. A <see cref="Name"/> rename keeps <see cref="OldCategory"/> == newCategory
        /// and changes only the name; a category rename leaves <see cref="OldName"/>/<see cref="NewName"/>
        /// null and moves every <c>OldCategory/*</c> of the id-type to <c>NewCategory/*</c>.
        /// </summary>
        public readonly struct Rename
        {
            public readonly Type idType;
            public readonly string OldCategory;
            public readonly string NewCategory;
            public readonly string OldName; // null = whole-category rename
            public readonly string NewName;

            private Rename(Type idType, string oldCat, string newCat, string oldName, string newName)
            {
                this.idType = idType;
                OldCategory = oldCat;
                NewCategory = newCat;
                OldName = oldName;
                NewName = newName;
            }

            public bool IsCategoryRename => OldName == null;

            /// <summary> Rename one name within a category: OldCategory/OldName → OldCategory/NewName. </summary>
            public static Rename ForName(Type idType, string category, string oldName, string newName) =>
                new Rename(idType, category, category, oldName, newName);

            /// <summary> Rename a whole category: every OldCategory/* of this id-type → NewCategory/*. </summary>
            public static Rename ForCategory(Type idType, string oldCategory, string newCategory) =>
                new Rename(idType, oldCategory, newCategory, null, null);

            /// <summary> True if this rename actually changes anything (guards no-op renames). </summary>
            public bool IsEffective => IsCategoryRename
                ? !string.Equals(OldCategory, NewCategory, StringComparison.Ordinal)
                : !string.Equals(OldName, NewName, StringComparison.Ordinal);

            /// <summary>
            /// If <paramref name="category"/>/<paramref name="name"/> (of id-type <paramref name="slotType"/>)
            /// matches this rename, yields the replacement and returns true; otherwise false.
            /// </summary>
            public bool TryRewrite(Type slotType, string category, string name,
                out string newCategory, out string newName)
            {
                newCategory = category;
                newName = name;
                if (slotType != idType) return false;
                // normalize blanks the same way Ref/CategoryNameId do, so "" and "None" compare equal
                string c = Norm(category, CategoryNameId.DefaultCategory);
                string n = Norm(name, CategoryNameId.DefaultName);

                if (IsCategoryRename)
                {
                    if (!string.Equals(c, Norm(OldCategory, CategoryNameId.DefaultCategory), StringComparison.Ordinal))
                        return false;
                    newCategory = NewCategory;
                    newName = name;
                    return true;
                }

                if (!string.Equals(c, Norm(OldCategory, CategoryNameId.DefaultCategory), StringComparison.Ordinal)
                    || !string.Equals(n, Norm(OldName, CategoryNameId.DefaultName), StringComparison.Ordinal))
                    return false;
                newCategory = category;
                newName = NewName;
                return true;
            }

            private static string Norm(string value, string fallback) =>
                string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary> The outcome of rewriting one file. </summary>
        public readonly struct FileResult
        {
            public readonly string path;
            public readonly int references; // matching references rewritten in this file

            public FileResult(string path, int references)
            {
                this.path = path;
                this.references = references;
            }
        }

        /// <summary> The outcome of a project-wide reference rewrite. </summary>
        public sealed class Result
        {
            public readonly List<FileResult> changedFiles = new List<FileResult>();
            public readonly List<string> parseErrors = new List<string>();
            public int TotalReferences { get; internal set; }
            public int FilesChanged => changedFiles.Count;
        }

        // -------------------------------------------------------------------- pure rewrite

        /// <summary>
        /// Applies <paramref name="rename"/> to every matching id-reference slot of an in-memory spec and
        /// returns the count rewritten. Pure — no I/O — so it is unit-tested directly. The spec is mutated
        /// in place (callers that need to compare round-trip identity should re-serialize after).
        /// </summary>
        public static int Rewrite(UISpec spec, Rename rename)
        {
            if (spec == null || !rename.IsEffective) return 0;
            int count = 0;
            IdRefSlots.Visit(spec, slot =>
            {
                slot.Get(out string category, out string name);
                if (rename.TryRewrite(slot.IdType, category, name, out string newCategory, out string newName))
                {
                    slot.Set(newCategory, newName);
                    count++;
                }
            });
            return count;
        }

        // -------------------------------------------------------------------- project I/O

        /// <summary>
        /// Computes how many references the rename would touch and in which files, WITHOUT writing —
        /// the data behind the confirm/preview dialog. Reuses the scanner's shared file discovery and
        /// pre-filter so the preview covers exactly the files <see cref="Apply"/> would.
        /// </summary>
        public static Result Preview(Rename rename) => Run(rename, write: false);

        /// <summary>
        /// Rewrites the rename into every spec source-of-truth file (and the baseline), writing back ONLY
        /// files whose content actually changed. Returns the per-file outcome. Generated specs are
        /// byte-stable through <see cref="UISpec.FromJson"/>→<see cref="UISpec.ToJson"/>; hand-authored
        /// specs are normalized to canonical form by the same round-trip.
        /// </summary>
        public static Result Apply(Rename rename) => Run(rename, write: true);

        private static Result Run(Rename rename, bool write)
        {
            var result = new Result();
            if (!rename.IsEffective) return result;

            int total = 0;
            foreach (string path in IdUsageScanner.EnumerateSpecFiles())
            {
                string json;
                try { json = File.ReadAllText(path); }
                catch (Exception e) { result.parseErrors.Add($"{path}: {e.Message}"); continue; }

                if (!IdUsageScanner.LooksLikeSpec(json)) continue;

                UISpec spec;
                try { spec = UISpec.FromJson(json); }
                catch (Exception e) { result.parseErrors.Add($"{path}: {e.Message}"); continue; }

                int references = Rewrite(spec, rename);
                if (references == 0) continue;

                string rewritten = spec.ToJson();
                // only count/record a file when the serialized content actually changed (a no-op rewrite
                // that re-canonicalizes to the identical bytes is not a change worth writing)
                if (string.Equals(rewritten, json, StringComparison.Ordinal)) continue;

                total += references;
                result.changedFiles.Add(new FileResult(path, references));
                if (write)
                {
                    try { File.WriteAllText(path, rewritten); }
                    catch (Exception e) { result.parseErrors.Add($"{path}: write failed: {e.Message}"); }
                }
            }

            result.TotalReferences = total;
            if (write && result.FilesChanged > 0) AssetDatabase.Refresh();
            return result;
        }
    }
}
