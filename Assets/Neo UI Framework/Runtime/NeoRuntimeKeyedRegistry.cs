using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Runtime-asmdef twin of <c>Neo.UI.Editor.NeoKeyedRegistry&lt;T&gt;</c> — the same Pattern R "Shape
    /// A" keyed code registry contract, but usable from <c>Neo.UI</c> (no <c>UnityEditor</c> reference),
    /// for registries that must resolve at runtime rather than only in the editor (e.g. <c>NeoAnimatorRoles</c>,
    /// migrated onto this base in Wave 4). Named distinctly from the editor class — rather than reusing
    /// <c>NeoKeyedRegistry&lt;T&gt;</c> in the <c>Neo.UI</c> namespace — so editor code that
    /// <c>using</c>s both <c>Neo.UI</c> and <c>Neo.UI.Editor</c> can never hit an ambiguous-reference
    /// error between the two twins.
    /// <para>
    /// Keep this in lockstep with the editor version's public contract (constructor shape, `All`
    /// snapshot semantics, warn-not-throw, replace-on-duplicate, `GetOrWarn`, `ResetForTests`) — see
    /// <c>neo-ui-architecture-audit.md</c> §4 and <c>neo-ui-remediation-plan.md</c> Task 1.1. There is no
    /// asset-discovery ("Shape B") counterpart here: discovering <c>ScriptableObject</c> assets requires
    /// <c>UnityEditor</c>, which this asmdef may never reference.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The entry type held by the registry.</typeparam>
    public class NeoRuntimeKeyedRegistry<T>
    {
        private readonly Func<T, string> _key;
        private readonly StringComparison _comparison;
        private readonly Func<IEnumerable<T>> _builtins;
        private readonly Func<T, bool> _validate;
        private readonly string _registryName;

        private readonly List<T> _entries = new List<T>();
        private bool _builtinsSeeded;
        private IReadOnlyList<T> _snapshot;

        /// <param name="key">Extracts the lookup key from an entry. Never null.</param>
        /// <param name="comparison">String comparison used for key matching. Defaults to ordinal.</param>
        /// <param name="builtins">Optional factory for code-seeded built-in entries. Called lazily, once.</param>
        /// <param name="validate">Optional extra guard a candidate entry must pass to be registered.</param>
        /// <param name="registryName">Name used in shared warning messages. Defaults to the entry type's name.</param>
        public NeoRuntimeKeyedRegistry(
            Func<T, string> key,
            StringComparison comparison = StringComparison.Ordinal,
            Func<IEnumerable<T>> builtins = null,
            Func<T, bool> validate = null,
            string registryName = null)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _comparison = comparison;
            _builtins = builtins;
            _validate = validate;
            _registryName = string.IsNullOrEmpty(registryName) ? typeof(T).Name : registryName;
        }

        /// <summary>
        /// All registered entries — built-ins first, then registrations in the order they were made
        /// (same-key registrations replace in place rather than appending). Always a cached snapshot,
        /// rebuilt lazily the first time it's read after a mutation.
        /// </summary>
        public virtual IReadOnlyList<T> All
        {
            get
            {
                EnsureBuiltins();
                return _snapshot ?? (_snapshot = _entries.ToArray());
            }
        }

        /// <summary> Ordinal/`comparison`-matched lookup by key. False (and a default value) on miss. </summary>
        public virtual bool TryGet(string key, out T value)
        {
            EnsureBuiltins();
            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (string.Equals(_key(_entries[i]), key, _comparison))
                    {
                        value = _entries[i];
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// The get-with-warning variant: returns the entry, or logs one
        /// <c>"[Neo.UI] {registryName}: no entry '{key}'."</c> warning and returns the default value on
        /// miss — the package's "no silent failures" invariant.
        /// </summary>
        public T GetOrWarn(string key)
        {
            if (TryGet(key, out T value)) return value;
            Debug.LogWarning($"[Neo.UI] {_registryName}: no entry '{key}'.");
            return default;
        }

        /// <summary>
        /// Registers an entry. A null value, an entry with a null/empty key, or one that fails the
        /// constructor's <c>validate</c> guard is warned-and-ignored (never thrown). A same-key entry
        /// REPLACES the existing one in place — never duplicates.
        /// </summary>
        public virtual void Register(T value)
        {
            if (!IsValid(value))
            {
                Debug.LogWarning($"[Neo.UI] {_registryName}: ignored a null/invalid entry.");
                return;
            }
            EnsureBuiltins();
            UpsertInto(_entries, value);
            _snapshot = null;
        }

        /// <summary> Test-only: removes a registered entry by key. Returns true if one was removed. </summary>
        internal virtual bool Remove(string key)
        {
            EnsureBuiltins();
            int removed = _entries.RemoveAll(e => string.Equals(_key(e), key, _comparison));
            if (removed > 0) _snapshot = null;
            return removed > 0;
        }

        /// <summary>
        /// Test-only: clears every registration and forces a fresh built-ins seed on next access.
        /// </summary>
        internal virtual void ResetForTests()
        {
            _entries.Clear();
            _snapshot = null;
            _builtinsSeeded = false;
        }

        /// <summary> Registry name used in shared warning messages. Exposed for subclasses. </summary>
        protected string RegistryName => _registryName;

        /// <summary> The key-comparison policy this registry was constructed with. Exposed for subclasses. </summary>
        protected StringComparison Comparison => _comparison;

        /// <summary> Extracts the lookup key from an entry using this registry's key selector. </summary>
        protected string KeyOf(T value) => _key(value);

        /// <summary>
        /// True when <paramref name="value"/> is non-null, has a non-empty key, and (if a <c>validate</c>
        /// guard was supplied to the constructor) passes it.
        /// </summary>
        protected bool IsValid(T value)
        {
            // object.Equals rather than "value == null": T is unconstrained, so == isn't legal in general.
            if (object.Equals(value, null)) return false;
            string key = _key(value);
            if (string.IsNullOrEmpty(key)) return false;
            if (_validate != null && !_validate(value)) return false;
            return true;
        }

        /// <summary>
        /// Inserts <paramref name="value"/> into <paramref name="list"/> by this registry's key/
        /// comparison policy: replaces an existing same-key entry in place, else appends.
        /// </summary>
        protected void UpsertInto(List<T> list, T value)
        {
            string key = _key(value);
            int existing = list.FindIndex(e => string.Equals(_key(e), key, _comparison));
            if (existing >= 0) list[existing] = value;
            else list.Add(value);
        }

        /// <summary>
        /// Seeds the built-ins exactly once (lazily, on first access), builtins-first. Safe to call
        /// re-entrantly — the seeded flag is set before iterating.
        /// </summary>
        protected void EnsureBuiltins()
        {
            if (_builtinsSeeded) return;
            _builtinsSeeded = true;
            if (_builtins == null) return;
            foreach (T item in _builtins())
            {
                Register(item);
            }
        }
    }
}
