using System;
using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The grouping + search machinery shared by <see cref="AnimationPresetBrowserPopup"/> (the animator
    /// slot picker) and the Design System window's Motion tab library browser (Phase 2.4) — extracted so
    /// the two surfaces share ONE implementation of how discovered <see cref="UIAnimationPreset"/>s are
    /// grouped, sorted, expanded and filtered. Pure data: no IMGUI, no GUIStyles, no editor-tick — so each
    /// host renders the rows its own way (a fixed-size popup that closes on click, or an inline list that
    /// selects a preset for editing) over identical model behavior.
    /// <para>
    /// Categories come from whatever the discovered assets DECLARE (<see cref="UIAnimationPreset.category"/>,
    /// "Custom" when blank) — never a hardcoded set, so a project's own category appears for free. A role's
    /// <see cref="NeoAnimatorRole.SuggestedCategories"/> sort to the top and start expanded; relevance is a
    /// sort, never a hard filter. When searching, matched presets show under their (always-open) header and
    /// expansion is ignored; a category-name hit surfaces all of that category's presets.
    /// </para>
    /// </summary>
    internal sealed class AnimationPresetBrowserModel
    {
        internal sealed class Group
        {
            public string category;
            public bool suggested;
            public readonly List<UIAnimationPreset> presets = new List<UIAnimationPreset>();
        }

        internal struct Row
        {
            public Group header;             // non-null → a category header row
            public UIAnimationPreset preset; // non-null → a preset row
        }

        private readonly List<Group> _groups = new List<Group>();
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly List<Row> _rows = new List<Row>();

        /// <summary> The grouped, sorted categories (suggested first). </summary>
        internal IReadOnlyList<Group> Groups => _groups;

        /// <param name="presets">The discovered presets to group (e.g. <see cref="AnimationPresetRegistry.All"/>).</param>
        /// <param name="suggested">A role's suggested categories — sorted first and expanded (may be null/empty).</param>
        /// <param name="current">The applied preset's full-name ("Category/Name"), whose category is force-expanded (may be null).</param>
        internal AnimationPresetBrowserModel(IEnumerable<UIAnimationPreset> presets, string[] suggested, string current)
        {
            suggested ??= Array.Empty<string>();

            var byCategory = new Dictionary<string, Group>(StringComparer.Ordinal);
            foreach (UIAnimationPreset preset in presets)
            {
                if (preset == null || string.IsNullOrEmpty(preset.presetName)) continue;
                string category = string.IsNullOrEmpty(preset.category) ? "Custom" : preset.category;
                if (!byCategory.TryGetValue(category, out Group group))
                {
                    group = new Group { category = category, suggested = Array.IndexOf(suggested, category) >= 0 };
                    byCategory[category] = group;
                    _groups.Add(group);
                }
                group.presets.Add(preset);
            }
            foreach (Group group in _groups)
                group.presets.Sort((a, b) => string.CompareOrdinal(a.presetName, b.presetName));
            _groups.Sort((a, b) =>
            {
                if (a.suggested != b.suggested) return a.suggested ? -1 : 1;
                if (a.suggested) return Array.IndexOf(suggested, a.category) - Array.IndexOf(suggested, b.category);
                return string.CompareOrdinal(a.category, b.category);
            });

            foreach (Group group in _groups)
                if (group.suggested) _expanded.Add(group.category);
            // No role or no suggested category present: everything open beats a wall of closed folds.
            if (_expanded.Count == 0)
                foreach (Group group in _groups) _expanded.Add(group.category);
            // The applied preset's own category is always worth seeing open.
            if (!string.IsNullOrEmpty(current))
            {
                int slash = current.IndexOf('/');
                if (slash > 0) _expanded.Add(current.Substring(0, slash));
            }
        }

        internal bool IsExpanded(string category) => _expanded.Contains(category);

        internal void ToggleExpanded(string category)
        {
            if (!_expanded.Add(category)) _expanded.Remove(category);
        }

        /// <summary>
        /// Carries the user's expand/collapse choices forward across a rebuild (a host that persists the
        /// model — the Motion tab — recreates it when the discovered set changes on create/delete, and
        /// calls this so the folds don't snap back to defaults). No-op for a null <paramref name="other"/>.
        /// </summary>
        internal void CopyExpansionFrom(AnimationPresetBrowserModel other)
        {
            if (other == null) return;
            _expanded.Clear();
            foreach (string category in other._expanded) _expanded.Add(category);
        }

        /// <summary>
        /// Flattens the groups to a header/preset row list for the given search needle. Empty/null filter:
        /// a header per group plus its presets when expanded. Non-empty filter: matched presets under their
        /// (always-open) header, expansion ignored. Rebuilt into a REUSED list each call (no per-row
        /// allocation) — callers must consume it before the next call.
        /// </summary>
        internal IReadOnlyList<Row> BuildRows(string filter)
        {
            _rows.Clear();
            string needle = string.IsNullOrEmpty(filter) ? null : filter.ToLowerInvariant();
            foreach (Group group in _groups)
            {
                if (needle != null)
                {
                    // Searching: flat matched rows under their (always-open) headers, expansion ignored.
                    bool categoryHit = group.category.ToLowerInvariant().Contains(needle);
                    bool headerAdded = false;
                    foreach (UIAnimationPreset preset in group.presets)
                    {
                        if (!categoryHit && !preset.presetName.ToLowerInvariant().Contains(needle)) continue;
                        if (!headerAdded) { headerAdded = true; _rows.Add(new Row { header = group }); }
                        _rows.Add(new Row { preset = preset });
                    }
                    continue;
                }
                _rows.Add(new Row { header = group });
                if (_expanded.Contains(group.category))
                    foreach (UIAnimationPreset preset in group.presets)
                        _rows.Add(new Row { preset = preset });
            }
            return _rows;
        }
    }
}
