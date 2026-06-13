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

        public void Clear() => categories.Clear();

        private IdCategory Find(string category) =>
            categories.FirstOrDefault(c => string.Equals(c.category, category, StringComparison.Ordinal));
    }
}
