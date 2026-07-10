using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The ONE id → GameObject-name convention, shared by the generator (elements with an authored
    /// id) and the inspectors' rename-to-id tail button, so hand-named and generated objects read
    /// the same: "Button - Category_Name" — a type prefix derived from the id-bearing component
    /// (so the hierarchy shows WHAT each id is, Doozy-style, but from one derivation instead of a
    /// per-inspector hardcoded string — Doozy shipped sliders renaming to "Toggle - …" that way),
    /// then the id with slashes/invalid file-name characters as underscores and the default
    /// ("None") category dropped — in BOTH the "Category/Name" spec-id form and the category/name
    /// pair form.
    /// </summary>
    public static class NeoIdNaming
    {
        /// <summary>
        /// The hierarchy type prefix for an id-bearing component: its type name minus the package
        /// prefix, nicified ("UIButton" → "Button", "UIToggleGroup" → "Toggle Group") — a project's
        /// own widget type gets a sensible prefix for free, no registry. A <see cref="UIView"/> gets
        /// NONE: view roots are prefab roots, and Unity pins a prefab root's name to the asset file
        /// name (Category_Name), so a prefix would fight the prefab system.
        /// </summary>
        public static string TypePrefix(Component idOwner)
        {
            if (idOwner == null || idOwner is UIView) return null;
            string typeName = idOwner.GetType().Name;
            if (typeName.StartsWith("UI", StringComparison.Ordinal)) typeName = typeName.Substring(2);
            else if (typeName.StartsWith("Neo", StringComparison.Ordinal)) typeName = typeName.Substring(3);
            return typeName.Length == 0 ? null : ObjectNames.NicifyVariableName(typeName);
        }
        /// <summary> The name for a spec-form id string ("Category/Name" or bare "Name") — the exact
        /// transform the generator applies to <c>element.id</c>. Parses first, so this drops the
        /// default ("None") category exactly like the category/name pair form below. </summary>
        public static string GameObjectNameFor(string specId)
        {
            CategoryNameId.Parse(specId, out string category, out string name);
            return GameObjectNameFor(category, name);
        }

        /// <summary> The spec-id-form name with <paramref name="idOwner"/>'s type prefix — what the
        /// generator names an element whose GameObject carries its own id component. </summary>
        public static string GameObjectNameFor(Component idOwner, string specId) =>
            Prefixed(TypePrefix(idOwner), GameObjectNameFor(specId));

        /// <summary> The category/name-form name with <paramref name="idOwner"/>'s type prefix —
        /// what the rename-to-id button produces. </summary>
        public static string GameObjectNameFor(Component idOwner, string category, string name) =>
            Prefixed(TypePrefix(idOwner), GameObjectNameFor(category, name));

        private static string Prefixed(string prefix, string baseName) =>
            string.IsNullOrEmpty(prefix) ? baseName : $"{prefix} - {baseName}";

        /// <summary> The name for a category/name pair; a default/empty category is dropped. </summary>
        public static string GameObjectNameFor(string category, string name)
        {
            string trimmedName = string.IsNullOrWhiteSpace(name) ? CategoryNameId.DefaultName : name.Trim();
            string trimmedCategory = string.IsNullOrWhiteSpace(category) ? CategoryNameId.DefaultCategory : category.Trim();
            return trimmedCategory == CategoryNameId.DefaultCategory
                ? Sanitize(trimmedName)
                : Sanitize($"{trimmedCategory}/{trimmedName}");
        }

        /// <summary> Replaces '/' and any invalid file-name character with '_'; null is treated as
        /// empty. The one copy shared by every id/prefab-name transform in the editor. </summary>
        public static string Sanitize(string value)
        {
            string result = (value ?? "").Replace('/', '_');
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return result;
        }

        /// <summary>
        /// Renames <paramref name="target"/>'s GameObject to match <paramref name="id"/> (one undo
        /// step). A null/default id or an already-matching name is a no-op; returns whether a rename
        /// actually happened.
        /// </summary>
        public static bool RenameToId(Component target, CategoryNameId id)
        {
            if (target == null || id == null || id.isDefault) return false;
            string newName = GameObjectNameFor(target, id.Category, id.Name);
            if (string.IsNullOrEmpty(newName) || target.gameObject.name == newName) return false;
            Undo.RecordObject(target.gameObject, "Rename GameObject To Id");
            target.gameObject.name = newName;
            EditorUtility.SetDirty(target.gameObject);
            return true;
        }
    }
}
