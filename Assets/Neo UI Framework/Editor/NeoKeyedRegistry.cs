using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Pattern R's "Shape A" base: a keyed, string-addressed code registry. Fixes the copy-paste drift
    /// the architecture audit found across 11 near-identical registries (throw-vs-warn, first-wins-vs-
    /// replace, live-list leaks — see <c>neo-ui-architecture-audit.md</c> §4/A6) by making the policy an
    /// explicit constructor argument instead of an accident of which copy an author started from.
    /// <para>
    /// A registry holds built-ins (seeded lazily, once, in the order the <paramref name="builtins"/>
    /// factory yields them) plus whatever a consuming project <see cref="Register"/>s afterward — a
    /// same-key registration always REPLACES in place, never duplicates, so a project can override a
    /// built-in by re-registering its id. Invalid registrations (null value, empty/null key, or a value
    /// that fails <c>validate</c>) are warned-and-ignored — never thrown — because several adopters
    /// (e.g. Composer catalog registries) register from <c>[InitializeOnLoad]</c> static constructors,
    /// where a thrown exception poisons the whole domain with a <see cref="TypeInitializationException"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The entry type held by the registry.</typeparam>
    public class NeoKeyedRegistry<T>
    {
        private readonly Func<T, string> _key;
        private readonly StringComparison _comparison;
        private readonly Func<IEnumerable<T>> _builtins;
        private readonly Func<T, bool> _validate;
        private readonly string _registryName;

        private readonly List<T> _entries = new List<T>();
        private bool _builtinsSeeded;
        private IReadOnlyList<T> _snapshot;

        /// <summary> Constructs a registry over entries of type <typeparamref name="T"/>. </summary>
        /// <param name="key">Extracts the lookup key from an entry. Never null.</param>
        /// <param name="comparison">
        /// String comparison used for key matching. Defaults to <see cref="StringComparison.Ordinal"/>;
        /// pass <see cref="StringComparison.OrdinalIgnoreCase"/> for a case-insensitive registry (and
        /// document why at the call site — most registries are ordinal).
        /// </param>
        /// <param name="builtins">
        /// Optional factory for the code-seeded built-in entries. Called lazily (once, on first access)
        /// so a registry with no built-ins never pays for an empty enumeration, and so built-ins can
        /// reference other statics that may not be initialized yet at registry-construction time.
        /// </param>
        /// <param name="validate">Optional extra guard a candidate entry must pass to be registered.</param>
        /// <param name="registryName">
        /// Name used in the shared warning messages (e.g. "ShowcaseRegistry"). Defaults to the entry
        /// type's name when omitted.
        /// </param>
        public NeoKeyedRegistry(
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

        /// <summary> Registry name used in shared warning messages. Exposed for subclasses. </summary>
        protected string RegistryName => _registryName;

        /// <summary> The key-comparison policy this registry was constructed with. Exposed for subclasses. </summary>
        protected StringComparison Comparison => _comparison;

        /// <summary> Extracts the lookup key from an entry using this registry's key selector. </summary>
        protected string KeyOf(T value) => _key(value);

        /// <summary>
        /// All registered entries — built-ins first, then registrations in the order they were made
        /// (same-key registrations replace in place rather than appending). This is always a cached
        /// SNAPSHOT, rebuilt lazily the first time it's read after a mutation — callers can never observe
        /// (or corrupt) the registry's live backing list.
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
        /// miss. Use this at the point a spec/string-addressed lookup is resolved so a typo'd key never
        /// fails silently (the package's "no silent failures" invariant).
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
        /// REPLACES the existing one in place — never duplicates — so a project can override a built-in
        /// (or an earlier registration) just by registering the same key again.
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
        /// Test-only: clears every registration and forces a fresh built-ins seed on next access, so a
        /// suite that registers probe entries leaves the registry clean for sibling suites in the same
        /// domain.
        /// </summary>
        internal virtual void ResetForTests()
        {
            _entries.Clear();
            _snapshot = null;
            _builtinsSeeded = false;
        }

        /// <summary>
        /// True when <paramref name="value"/> is non-null, has a non-empty key, and (if a <c>validate</c>
        /// guard was supplied to the constructor) passes it. Exposed for subclasses that need to apply
        /// the same admission policy outside of <see cref="Register"/> (e.g. when merging discovered
        /// assets in <see cref="NeoAssetRegistry{TAsset,TEntry}"/>).
        /// </summary>
        protected bool IsValid(T value)
        {
            // object.Equals rather than "value == null": T is unconstrained here, so the == operator
            // isn't legal in general, and this also correctly honors a Unity Object's overridden
            // Equals (fake-null for a destroyed-but-not-yet-collected object) when T is one.
            if (object.Equals(value, null)) return false;
            string key = _key(value);
            if (string.IsNullOrEmpty(key)) return false;
            if (_validate != null && !_validate(value)) return false;
            return true;
        }

        /// <summary>
        /// Inserts <paramref name="value"/> into <paramref name="list"/> by this registry's key/
        /// comparison policy: replaces an existing same-key entry in place, else appends. Exposed for
        /// subclasses maintaining their own auxiliary lists (e.g. a "manual registrations" list that must
        /// survive an asset-discovery rebuild).
        /// </summary>
        protected void UpsertInto(List<T> list, T value)
        {
            string key = _key(value);
            int existing = list.FindIndex(e => string.Equals(_key(e), key, _comparison));
            if (existing >= 0) list[existing] = value;
            else list.Add(value);
        }

        /// <summary>
        /// Replaces the entire live entry set in one shot (bypassing per-item validation, which the
        /// caller is expected to have already applied) and invalidates the snapshot. Used by
        /// <see cref="NeoAssetRegistry{TAsset,TEntry}"/> to install a freshly rebuilt discovered+manual
        /// merge without going through <see cref="Register"/> once per entry.
        /// </summary>
        protected void ReplaceAll(IEnumerable<T> items)
        {
            _entries.Clear();
            _entries.AddRange(items);
            _snapshot = null;
        }

        /// <summary>
        /// Seeds the built-ins exactly once (lazily, on first access), builtins-first. Safe to call
        /// re-entrantly — the seeded flag is set before iterating, so a builtins factory that itself
        /// triggers another access (unusual, but not forbidden) can't recurse into a second seed pass.
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
