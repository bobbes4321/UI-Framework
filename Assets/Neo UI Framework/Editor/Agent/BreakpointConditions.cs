using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary> The viewport environment a breakpoint condition is evaluated against. </summary>
    public struct BreakpointEnv
    {
        public float width;
        public float height;
        public float aspect;   // width / height (0 when height is 0)
        public bool portrait;  // height >= width

        public static BreakpointEnv FromSize(float width, float height) => new BreakpointEnv
        {
            width = width,
            height = height,
            aspect = height > 0f ? width / height : 0f,
            portrait = height >= width
        };
    }

    /// <summary>
    /// One project-extensible breakpoint condition kind (Pattern R seam — NOT an enum). A kind reads
    /// its parameter off the spec's <see cref="BreakpointCondition"/> and reports whether it applies
    /// (<see cref="IsActive"/>) and, when active, whether the env matches (<see cref="Matches"/>). The
    /// built-ins (orientation / minAspect / maxAspect / minWidth / maxWidth) register themselves; a
    /// project adds e.g. a <c>safeAreaInset</c> kind through <see cref="BreakpointConditions.Register"/>
    /// without forking the package. The runtime mirror lives in <c>UIResponsiveRoot</c> over the baked
    /// table, so runtime stays editor-free.
    /// </summary>
    public interface IBreakpointCondition
    {
        /// <summary> Stable id (e.g. "orientation", "minAspect"). </summary>
        string Id { get; }

        /// <summary> True when <paramref name="condition"/> sets this kind's parameter (so it
        /// participates in the AND). </summary>
        bool IsActive(BreakpointCondition condition);

        /// <summary> Whether the env satisfies this kind (only consulted when <see cref="IsActive"/>). </summary>
        bool Matches(BreakpointCondition condition, BreakpointEnv env);
    }

    /// <summary>
    /// Pattern-R registry of breakpoint condition kinds. Built-ins register in the static ctor;
    /// <see cref="Evaluate"/> ANDs every active kind. Mirrors <see cref="NeoElementKinds"/> /
    /// <see cref="LayoutConstraints"/>: <see cref="All"/> / <see cref="TryGet"/> / <see cref="Register"/>
    /// (replace-by-Id, else append).
    /// </summary>
    public static class BreakpointConditions
    {
        private static readonly List<IBreakpointCondition> _all = new List<IBreakpointCondition>();

        static BreakpointConditions()
        {
            RegisterBuiltins();
        }

        private static void RegisterBuiltins()
        {
            Register(new OrientationCondition());
            Register(new MinAspectCondition());
            Register(new MaxAspectCondition());
            Register(new MinWidthCondition());
            Register(new MaxWidthCondition());
        }

        /// <summary> Every registered kind (built-ins first, in registration order). </summary>
        public static IReadOnlyList<IBreakpointCondition> All => _all;

        /// <summary> Finds the kind with the given id. </summary>
        public static bool TryGet(string id, out IBreakpointCondition result)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (IBreakpointCondition c in _all)
                    if (c != null && c.Id == id) { result = c; return true; }
            result = null;
            return false;
        }

        /// <summary> Registers a kind, replacing any existing one with the same <see cref="IBreakpointCondition.Id"/>. </summary>
        public static void Register(IBreakpointCondition condition)
        {
            if (condition == null || string.IsNullOrEmpty(condition.Id))
            {
                Debug.LogWarning("BreakpointConditions.Register ignored a condition with a null/empty Id.");
                return;
            }
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == condition.Id) { _all[i] = condition; return; }
            _all.Add(condition);
        }

        /// <summary>
        /// Whether <paramref name="condition"/> matches the env. Every active kind must match (AND); a
        /// condition with no active kinds matches nothing (it would otherwise win unconditionally and
        /// shadow later breakpoints). Mirrors the runtime evaluation in <c>UIResponsiveRoot</c>.
        /// </summary>
        public static bool Evaluate(BreakpointCondition condition, BreakpointEnv env)
        {
            if (condition == null || condition.IsEmpty) return false;
            bool anyActive = false;
            foreach (IBreakpointCondition kind in _all)
            {
                if (kind == null || !kind.IsActive(condition)) continue;
                anyActive = true;
                if (!kind.Matches(condition, env)) return false;
            }
            return anyActive;
        }

        /// <summary> Test/seam hook: clear and re-seed the built-ins (static state survives a test run). </summary>
        internal static void ResetForTests()
        {
            _all.Clear();
            RegisterBuiltins();
        }

        // ----------------------------------------------------------------- built-in kinds

        private sealed class OrientationCondition : IBreakpointCondition
        {
            public string Id => "orientation";
            public bool IsActive(BreakpointCondition c) => !string.IsNullOrEmpty(c.orientation);
            public bool Matches(BreakpointCondition c, BreakpointEnv env) =>
                (c.orientation == "portrait") == env.portrait;
        }

        private sealed class MinAspectCondition : IBreakpointCondition
        {
            public string Id => "minAspect";
            public bool IsActive(BreakpointCondition c) => c.minAspect.HasValue;
            public bool Matches(BreakpointCondition c, BreakpointEnv env) => env.aspect >= c.minAspect.Value;
        }

        private sealed class MaxAspectCondition : IBreakpointCondition
        {
            public string Id => "maxAspect";
            public bool IsActive(BreakpointCondition c) => c.maxAspect.HasValue;
            public bool Matches(BreakpointCondition c, BreakpointEnv env) => env.aspect <= c.maxAspect.Value;
        }

        private sealed class MinWidthCondition : IBreakpointCondition
        {
            public string Id => "minWidth";
            public bool IsActive(BreakpointCondition c) => c.minWidth.HasValue;
            public bool Matches(BreakpointCondition c, BreakpointEnv env) => env.width >= c.minWidth.Value;
        }

        private sealed class MaxWidthCondition : IBreakpointCondition
        {
            public string Id => "maxWidth";
            public bool IsActive(BreakpointCondition c) => c.maxWidth.HasValue;
            public bool Matches(BreakpointCondition c, BreakpointEnv env) => env.width <= c.maxWidth.Value;
        }
    }
}
