using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// ScriptableObject database of category/name entries backing the editor dropdown pickers.
    /// One concrete database type per ID type. Flat, force-text, agent-readable layout.
    /// Databases are editor conveniences — runtime lookup goes through the live registries,
    /// so a missing database entry never breaks behavior.
    /// </summary>
    public abstract class IdDatabase : ScriptableObject
    {
        [Serializable]
        public class IdCategory
        {
            public string category = CategoryNameId.DefaultCategory;
            public List<string> names = new List<string>();
        }

        [SerializeField] private List<IdCategory> categories = new List<IdCategory>();

        public IReadOnlyList<IdCategory> Categories => categories;

        public IEnumerable<string> GetCategories() => categories.Select(c => c.category);

        public IEnumerable<string> GetNames(string category)
        {
            IdCategory entry = Find(category);
            return entry != null ? entry.names : Enumerable.Empty<string>();
        }

        public bool ContainsCategory(string category) => Find(category) != null;

        public bool Contains(string category, string name)
        {
            IdCategory entry = Find(category);
            return entry != null && entry.names.Contains(name);
        }

        /// <summary> Adds an entry (creating the category if needed). Returns true if anything changed. </summary>
        public bool Add(string category, string name)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name)) return false;
            category = category.Trim();
            name = name.Trim();
            IdCategory entry = Find(category);
            if (entry == null)
            {
                entry = new IdCategory { category = category };
                categories.Add(entry);
                categories.Sort((a, b) => string.CompareOrdinal(a.category, b.category));
            }
            if (entry.names.Contains(name)) return false;
            entry.names.Add(name);
            entry.names.Sort(string.CompareOrdinal);
            return true;
        }

        public bool Remove(string category, string name)
        {
            IdCategory entry = Find(category);
            if (entry == null) return false;
            bool removed = entry.names.Remove(name);
            if (removed && entry.names.Count == 0) categories.Remove(entry);
            return removed;
        }

        /// <summary>
        /// Removes an entire category and all its names. Returns true if a category was removed.
        /// </summary>
        public bool RemoveCategory(string category)
        {
            IdCategory entry = Find(category);
            return entry != null && categories.Remove(entry);
        }

        /// <summary>
        /// Renames a category in place, preserving its names and the overall category sort order.
        /// Returns false when <paramref name="oldCategory"/> doesn't exist, the new name is blank, or a
        /// different category already uses <paramref name="newCategory"/> (no merge — that's a separate
        /// intent). A no-op rename to the same name returns true.
        /// NOTE: this is a DATABASE-level rename only — it does NOT rewrite the matching id references
        /// inside UISpecs/prefabs that use the old Category/Name. Re-point those through a regenerate.
        /// </summary>
        public bool RenameCategory(string oldCategory, string newCategory)
        {
            if (string.IsNullOrWhiteSpace(newCategory)) return false;
            newCategory = newCategory.Trim();
            IdCategory entry = Find(oldCategory);
            if (entry == null) return false;
            if (string.Equals(entry.category, newCategory, StringComparison.Ordinal)) return true; // no-op
            if (Find(newCategory) != null) return false; // would collide with an existing category
            entry.category = newCategory;
            categories.Sort((a, b) => string.CompareOrdinal(a.category, b.category));
            return true;
        }

        /// <summary>
        /// Renames a name within a category in place, preserving the names' sort order. Returns false
        /// when the category/old name is missing, the new name is blank, or the new name already exists
        /// in the category. A no-op rename to the same name returns true.
        /// NOTE: database-level only — does not rewrite spec/prefab references (see <see cref="RenameCategory"/>).
        /// </summary>
        public bool RenameName(string category, string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return false;
            newName = newName.Trim();
            IdCategory entry = Find(category);
            if (entry == null) return false;
            int index = entry.names.IndexOf(oldName);
            if (index < 0) return false;
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) return true; // no-op
            if (entry.names.Contains(newName)) return false; // would collide
            entry.names[index] = newName;
            entry.names.Sort(string.CompareOrdinal);
            return true;
        }

        public void Clear() => categories.Clear();

        private IdCategory Find(string category) =>
            categories.FirstOrDefault(c => string.Equals(c.category, category, StringComparison.Ordinal));
    }
}
