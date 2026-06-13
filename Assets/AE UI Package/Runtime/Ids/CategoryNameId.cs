using System;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Category + Name string identifier with value equality — the addressing scheme used everywhere
    /// in the package (views, buttons, toggles, streams, tags). Strings, never GUIDs, so every
    /// reference stays greppable by humans and agents.
    /// </summary>
    [Serializable]
    public class CategoryNameId : IEquatable<CategoryNameId>
    {
        public const string DefaultCategory = "None";
        public const string DefaultName = "None";

        [SerializeField] private string category = DefaultCategory;
        [SerializeField] private string name = DefaultName;

        public string Category
        {
            get => string.IsNullOrWhiteSpace(category) ? DefaultCategory : category.Trim();
            set => category = string.IsNullOrWhiteSpace(value) ? DefaultCategory : value.Trim();
        }

        public string Name
        {
            get => string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
            set => name = string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
        }

        public bool isDefault => Category == DefaultCategory && Name == DefaultName;

        public CategoryNameId() { }

        public CategoryNameId(string category, string name)
        {
            Category = category;
            Name = name;
        }

        public bool Matches(string otherCategory, string otherName) =>
            string.Equals(Category, otherCategory, StringComparison.Ordinal) &&
            string.Equals(Name, otherName, StringComparison.Ordinal);

        public bool Equals(CategoryNameId other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Matches(other.Category, other.Name);
        }

        public override bool Equals(object obj) => obj is CategoryNameId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Category.GetHashCode() * 397) ^ Name.GetHashCode();
            }
        }

        public override string ToString() => $"{Category}/{Name}";

        /// <summary> Parses "Category/Name" (a bare name becomes "None/name"). </summary>
        public static void Parse(string value, out string category, out string name)
        {
            category = DefaultCategory;
            name = DefaultName;
            if (string.IsNullOrWhiteSpace(value)) return;
            int slash = value.IndexOf('/');
            if (slash < 0)
            {
                name = value.Trim();
                return;
            }
            category = value.Substring(0, slash).Trim();
            name = value.Substring(slash + 1).Trim();
        }
    }

    [Serializable] public class ViewId : CategoryNameId
    {
        public ViewId() { }
        public ViewId(string category, string name) : base(category, name) { }
    }

    [Serializable] public class ButtonId : CategoryNameId
    {
        public ButtonId() { }
        public ButtonId(string category, string name) : base(category, name) { }
    }

    [Serializable] public class ToggleId : CategoryNameId
    {
        public ToggleId() { }
        public ToggleId(string category, string name) : base(category, name) { }
    }

    [Serializable] public class SliderId : CategoryNameId
    {
        public SliderId() { }
        public SliderId(string category, string name) : base(category, name) { }
    }

    [Serializable] public class TagId : CategoryNameId
    {
        public TagId() { }
        public TagId(string category, string name) : base(category, name) { }
    }

    [Serializable] public class PanelId : CategoryNameId
    {
        public PanelId() { }
        public PanelId(string category, string name) : base(category, name) { }
    }

    [Serializable] public class StreamId : CategoryNameId
    {
        public StreamId() { }
        public StreamId(string category, string name) : base(category, name) { }
    }

    [Serializable] public class DropdownId : CategoryNameId
    {
        public DropdownId() { }
        public DropdownId(string category, string name) : base(category, name) { }
    }
}
