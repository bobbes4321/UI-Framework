using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Neo.UI
{
    /// <summary>
    /// The Pillar B runtime breakpoint driver. One per view root: it adapts the view's layout live as
    /// the viewport aspect/orientation changes, with NO spec parsing at runtime. The generator
    /// pre-resolves every element override's effective <c>LayoutSpec</c> to concrete RectTransform
    /// values (anchorMin/Max, offsetMin/Max, sizeDelta) and bakes them into <see cref="entries"/>,
    /// plus the ordered <see cref="conditions"/> table (force-text, trivially diffable). At runtime
    /// this component watches the canvas size, selects the FIRST matching breakpoint (else the baked
    /// base), and — only when the active breakpoint CHANGES — pushes that breakpoint's pre-resolved
    /// vectors onto each target. WYSIWYG: the prefab is baked at the base breakpoint, so <c>Start</c>
    /// is a no-op unless the current viewport already matches a non-base breakpoint.
    ///
    /// <para>Editor-FREE by contract: this lives in <c>Neo.UI</c> and never references the editor spec
    /// types. The condition evaluation here is a parallel mirror of the editor-side
    /// <c>BreakpointConditions</c> built-ins over the baked <see cref="ResponsiveCondition"/> table.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Containers/Responsive Root")]
    [DisallowMultipleComponent]
    public class UIResponsiveRoot : UIBehaviour, IActiveBreakpoint
    {
        /// <summary> A baked breakpoint condition (force-text mirror of the editor's BreakpointCondition).
        /// Built-in kinds: orientation / minAspect / maxAspect / minWidth / maxWidth. Unset numeric
        /// bounds are NaN. </summary>
        [System.Serializable]
        public class ResponsiveCondition
        {
            public string name;
            public string orientation;     // "portrait" | "landscape" | "" (unset)
            public float minAspect = float.NaN;
            public float maxAspect = float.NaN;
            public float minWidth = float.NaN;
            public float maxWidth = float.NaN;

            /// <summary> An all-unset condition never matches — otherwise it would win unconditionally
            /// and shadow every later breakpoint. Mirrors the editor-side guard in
            /// <c>BreakpointConditions.Evaluate</c>. </summary>
            public bool IsEmpty =>
                string.IsNullOrEmpty(orientation) &&
                float.IsNaN(minAspect) && float.IsNaN(maxAspect) &&
                float.IsNaN(minWidth) && float.IsNaN(maxWidth);

            public bool Matches(float width, float height)
            {
                if (IsEmpty) return false;
                float aspect = height > 0f ? width / height : 0f;
                bool portrait = height >= width;
                if (!string.IsNullOrEmpty(orientation))
                {
                    bool wantPortrait = orientation == "portrait";
                    if (wantPortrait != portrait) return false;
                }
                if (!float.IsNaN(minAspect) && aspect < minAspect) return false;
                if (!float.IsNaN(maxAspect) && aspect > maxAspect) return false;
                if (!float.IsNaN(minWidth) && width < minWidth) return false;
                if (!float.IsNaN(maxWidth) && width > maxWidth) return false;
                return true;
            }
        }

        /// <summary>
        /// A force-text serialized mirror of the ORIGINAL delta LayoutSpec for one override. The runtime
        /// never reads it (it applies the pre-resolved vectors); it exists ONLY so the editor exporter
        /// reconstructs <c>overrides</c> byte-identically rather than re-deriving from resolved vectors
        /// (which alias). Keys/values are parallel arrays so the constraint-keyed offset dict round-trips
        /// in its authored order. Unset numeric fields are NaN; unset strings are empty.
        /// </summary>
        [System.Serializable]
        public class ResponsiveDelta
        {
            public string h = string.Empty;
            public string v = string.Empty;
            public List<string> offsetKeys = new List<string>();
            public List<float> offsetValues = new List<float>();
            public float sizeW = float.NaN;
            public float sizeH = float.NaN;
            public string sizingW = string.Empty;
            public string sizingH = string.Empty;
        }

        /// <summary> One target's pre-resolved RectTransform values for ONE breakpoint. The target is a
        /// direct serialized reference (the prefab nests it), keyed by <see cref="breakpoint"/> name.
        /// <see cref="delta"/> carries the original authored delta for exact export (runtime ignores it). </summary>
        [System.Serializable]
        public class ResponsiveEntry
        {
            public string breakpoint;
            public RectTransform target;
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 offsetMin;
            public Vector2 offsetMax;
            public Vector2 sizeDelta;
            public Vector2 pivot;
            public ResponsiveDelta delta = new ResponsiveDelta();
        }

        /// <summary> The baked base values for a target (the WYSIWYG prefab state) — applied when no
        /// breakpoint matches, so reverting from a breakpoint restores the authored base exactly. </summary>
        [System.Serializable]
        public class ResponsiveBase
        {
            public RectTransform target;
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 offsetMin;
            public Vector2 offsetMax;
            public Vector2 sizeDelta;
            public Vector2 pivot;
        }

        /// <summary> Ordered breakpoint table — first match wins. </summary>
        public List<ResponsiveCondition> conditions = new List<ResponsiveCondition>();
        /// <summary> Pre-resolved per-(breakpoint, target) vectors. </summary>
        public List<ResponsiveEntry> entries = new List<ResponsiveEntry>();
        /// <summary> Baked base vectors per target. </summary>
        public List<ResponsiveBase> bases = new List<ResponsiveBase>();

        /// <summary> The base sentinel: when no breakpoint matches, this is the "active" name. </summary>
        public const string BaseBreakpoint = "";

        private string _active = BaseBreakpoint;
        private bool _started;
        private bool _warnedUnresolved;
        private string _forced; // preview override (IActiveBreakpoint); null = follow the viewport

        protected override void Start()
        {
            base.Start();
            _started = true;
            // WYSIWYG: the prefab is baked at base. Only apply if the live viewport already selects a
            // non-base breakpoint — otherwise this is a no-op and the baked state stands.
            Reevaluate(force: false);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (!_started) return;
            Reevaluate(force: false);
        }

        /// <summary> Current viewport size in this root's local rect space (the root stretches to the
        /// canvas, so this is the canvas reference size). </summary>
        private void CurrentSize(out float width, out float height)
        {
            var rect = transform as RectTransform;
            Rect r = rect != null ? rect.rect : new Rect(0f, 0f, 0f, 0f);
            width = r.width;
            height = r.height;
        }

        /// <summary> The breakpoint name the current (or forced) viewport selects: first matching
        /// condition, else <see cref="BaseBreakpoint"/>. </summary>
        private string SelectBreakpoint()
        {
            if (_forced != null) return _forced;
            CurrentSize(out float width, out float height);
            if (width <= 0f || height <= 0f) return _active; // not laid out yet; keep current
            for (int i = 0; i < conditions.Count; i++)
            {
                ResponsiveCondition c = conditions[i];
                if (c != null && c.Matches(width, height)) return c.name;
            }
            return BaseBreakpoint;
        }

        private void Reevaluate(bool force)
        {
            string selected = SelectBreakpoint();
            if (!force && selected == _active) return; // only on change → no per-frame work
            Apply(selected);
            _active = selected;
        }

        private void Apply(string breakpoint)
        {
            if (breakpoint == BaseBreakpoint)
            {
                foreach (ResponsiveBase b in bases)
                {
                    if (b == null || b.target == null)
                    {
                        WarnUnresolved(BaseBreakpoint);
                        continue;
                    }
                    ApplyVectors(b.target, b.anchorMin, b.anchorMax, b.offsetMin, b.offsetMax, b.sizeDelta, b.pivot);
                }
                return;
            }

            foreach (ResponsiveEntry e in entries)
            {
                if (e == null || e.breakpoint != breakpoint) continue;
                if (e.target == null)
                {
                    WarnUnresolved(breakpoint);
                    continue;
                }
                ApplyVectors(e.target, e.anchorMin, e.anchorMax, e.offsetMin, e.offsetMax, e.sizeDelta, e.pivot);
            }

            // Targets that have a base but no entry for this breakpoint keep their CURRENT layout
            // (which is the base, since selection always transitions through base). This matches the
            // delta-cascade model: an element with no override for the active breakpoint shows its base.
            foreach (ResponsiveBase b in bases)
            {
                if (b == null || b.target == null) continue;
                if (HasEntry(breakpoint, b.target)) continue;
                ApplyVectors(b.target, b.anchorMin, b.anchorMax, b.offsetMin, b.offsetMax, b.sizeDelta, b.pivot);
            }
        }

        private bool HasEntry(string breakpoint, RectTransform target)
        {
            foreach (ResponsiveEntry e in entries)
                if (e != null && e.breakpoint == breakpoint && e.target == target) return true;
            return false;
        }

        private static void ApplyVectors(RectTransform t, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Vector2 sizeDelta, Vector2 pivot)
        {
            t.pivot = pivot;
            t.anchorMin = anchorMin;
            t.anchorMax = anchorMax;
            t.offsetMin = offsetMin;
            t.offsetMax = offsetMax;
            // offsetMin/Max fully determine the rect when anchors differ; for equal anchors sizeDelta
            // carries the extent, so set it last to be authoritative.
            t.sizeDelta = sizeDelta;
        }

        private void WarnUnresolved(string breakpoint)
        {
            if (_warnedUnresolved) return;
            _warnedUnresolved = true;
            Debug.LogWarning($"UIResponsiveRoot on '{name}': a breakpoint target could not be resolved " +
                $"(breakpoint '{(string.IsNullOrEmpty(breakpoint) ? "base" : breakpoint)}'). The override " +
                "will not apply. The target reference was likely removed after baking — regenerate the view.", this);
        }

        // ---------------------------------------------------------------- IActiveBreakpoint (preview)

        /// <summary> Preview-only (Pillar C): force the driver to a named breakpoint regardless of the
        /// live viewport. Pass null / <see cref="BaseBreakpoint"/> handling: an empty string forces the
        /// base; null releases the override and re-follows the viewport. </summary>
        public void SetActiveBreakpoint(string breakpoint)
        {
            _forced = breakpoint;
            Reevaluate(force: true);
        }

        /// <summary> The breakpoint currently applied (empty = base). </summary>
        public string ActiveBreakpoint => _active;

        /// <summary> The breakpoint names this root knows about, in order (base excluded). </summary>
        public IReadOnlyList<string> BreakpointNames
        {
            get
            {
                var list = new List<string>(conditions.Count);
                foreach (ResponsiveCondition c in conditions)
                    if (c != null && !string.IsNullOrEmpty(c.name)) list.Add(c.name);
                return list;
            }
        }
    }
}
