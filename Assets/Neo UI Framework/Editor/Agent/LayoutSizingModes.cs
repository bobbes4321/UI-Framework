using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A per-child sizing mode (Figma Fixed/Hug/Fill) applied to one axis of a layout-group child.
    /// Configures the child's <see cref="LayoutElement"/> / <see cref="ContentSizeFitter"/> and
    /// reports whether the parent group must force-expand that axis (Unity's force-expand is a
    /// group-level flag, so the generator ORs <see cref="WantsForceExpand"/> across the children).
    /// </summary>
    public interface ILayoutSizingMode
    {
        /// <summary> Spec id: "fixed","hug","fill" (extensible — a project may add "clamp", …). </summary>
        string Id { get; }

        /// <summary> True when this mode wants the parent layout group to force-expand the axis. </summary>
        bool WantsForceExpand { get; }

        /// <summary> Configures <paramref name="go"/> for this mode on <paramref name="horizontal"/>'s
        /// axis. <paramref name="size"/> is the authored fixed extent (used by "fixed"). </summary>
        void Apply(GameObject go, bool horizontal, float? size);

        /// <summary> Reverse-detects this mode from a child's components; false when it doesn't match. </summary>
        bool TryDetect(GameObject go, bool horizontal);
    }

    /// <summary>
    /// Pattern-R registry of per-child sizing modes (the documented seam — a project can add a
    /// min/max "clamp" mode without forking). A thin public-static facade over the shared
    /// <see cref="NeoKeyedRegistry{T}"/> base (Wave 4 Task 4.3) — built-ins fixed/hug/fill are seeded
    /// lazily (once, in a fixed order) on first access; the exporter detects in <see cref="All"/>
    /// order, first match wins.
    /// </summary>
    public static class LayoutSizingModes
    {
        public const string Fixed = "fixed";
        public const string Hug = "hug";
        public const string Fill = "fill";

        // Thin forwarder over the shared keyed base (Task 4.3) — no caller changes.
        private static readonly NeoKeyedRegistry<ILayoutSizingMode> _registry =
            new NeoKeyedRegistry<ILayoutSizingMode>(
                key: m => m.Id,
                builtins: Builtins,
                registryName: "LayoutSizingModes");

        private static IEnumerable<ILayoutSizingMode> Builtins()
        {
            yield return new FixedSizing();
            yield return new HugSizing();
            yield return new FillSizing();
        }

        /// <summary> Every registered mode (built-ins first, in registration order). </summary>
        public static IReadOnlyList<ILayoutSizingMode> All => _registry.All;

        /// <summary> Finds a mode by id; null + warning when missing (no silent failure). </summary>
        public static ILayoutSizingMode Get(string id)
        {
            if (_registry.TryGet(id, out ILayoutSizingMode m)) return m;
            Debug.LogWarning($"LayoutSizingModes.Get: no sizing mode '{id}' registered; the child keeps its default sizing. Register one in LayoutSizingModes, or check the spec.");
            return null;
        }

        /// <summary> Registers a mode, replacing any existing one with the same Id. </summary>
        public static void Register(ILayoutSizingMode mode) => _registry.Register(mode);

        /// <summary> Test/seam hook: clears every registration and forces a fresh built-ins seed on next access. </summary>
        internal static void ResetForTests() => _registry.ResetForTests();

        private static LayoutElement GetOrAddLayoutElement(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            return le;
        }

        /// <summary> Adds (or reuses) a <see cref="ContentSizeFitter"/> and sets the requested axis to
        /// <see cref="ContentSizeFitter.FitMode.PreferredSize"/>, leaving the other axis untouched.
        /// Idempotent — a child sized on both axes ends up with one fitter fitting both. </summary>
        private static void FitAxisToPreferred(GameObject go, bool horizontal)
        {
            var fitter = go.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = go.AddComponent<ContentSizeFitter>();
            if (horizontal) fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            else fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary> Fixed: rigid authored extent. min = preferred = size, flexible = 0, plus a
        /// <see cref="ContentSizeFitter"/> on the axis so the child honors its size even inside a
        /// force-expanding parent group (the same trick a button uses to keep its width). </summary>
        private sealed class FixedSizing : ILayoutSizingMode
        {
            public string Id => Fixed;
            public bool WantsForceExpand => false;

            public void Apply(GameObject go, bool horizontal, float? size)
            {
                LayoutElement le = GetOrAddLayoutElement(go);
                float s = size.HasValue && size.Value > 0f ? size.Value : -1f;
                if (horizontal)
                {
                    if (s > 0f) { le.minWidth = s; le.preferredWidth = s; }
                    le.flexibleWidth = 0f;
                }
                else
                {
                    if (s > 0f) { le.minHeight = s; le.preferredHeight = s; }
                    le.flexibleHeight = 0f;
                }

                // A LayoutElement alone is ignored by a force-expanding parent group (childForceExpandWidth
                // stretches the child regardless). PreferredSize fitting pins the rect to the authored
                // min=preferred extent, overriding the parent's force-expand — exactly how a button escapes.
                FitAxisToPreferred(go, horizontal);
            }

            public bool TryDetect(GameObject go, bool horizontal)
            {
                var le = go.GetComponent<LayoutElement>();
                if (le == null) return false;
                if (horizontal) return le.flexibleWidth <= 0f && le.preferredWidth > 0f && le.minWidth > 0f;
                return le.flexibleHeight <= 0f && le.preferredHeight > 0f && le.minHeight > 0f;
            }
        }

        /// <summary> Hug: shrink to content. ContentSizeFitter PreferredSize on the axis. </summary>
        private sealed class HugSizing : ILayoutSizingMode
        {
            public string Id => Hug;
            public bool WantsForceExpand => false;

            public void Apply(GameObject go, bool horizontal, float? size)
            {
                LayoutElement le = GetOrAddLayoutElement(go);
                if (horizontal) { le.minWidth = -1f; le.preferredWidth = -1f; le.flexibleWidth = 0f; }
                else { le.minHeight = -1f; le.preferredHeight = -1f; le.flexibleHeight = 0f; }

                FitAxisToPreferred(go, horizontal);
            }

            public bool TryDetect(GameObject go, bool horizontal)
            {
                var fitter = go.GetComponent<ContentSizeFitter>();
                if (fitter == null) return false;
                return horizontal
                    ? fitter.horizontalFit == ContentSizeFitter.FitMode.PreferredSize
                    : fitter.verticalFit == ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        /// <summary> Fill: absorb leftover space. flexible = 1 + group force-expand. </summary>
        private sealed class FillSizing : ILayoutSizingMode
        {
            public string Id => Fill;
            public bool WantsForceExpand => true;

            public void Apply(GameObject go, bool horizontal, float? size)
            {
                LayoutElement le = GetOrAddLayoutElement(go);
                if (horizontal) le.flexibleWidth = 1f;
                else le.flexibleHeight = 1f;
            }

            public bool TryDetect(GameObject go, bool horizontal)
            {
                var le = go.GetComponent<LayoutElement>();
                if (le == null) return false;
                return horizontal ? le.flexibleWidth >= 1f : le.flexibleHeight >= 1f;
            }
        }
    }
}
