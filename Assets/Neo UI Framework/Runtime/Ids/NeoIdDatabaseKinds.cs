using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// One id database surfaced to the editor tooling: the human-facing <see cref="label"/>, the id
    /// <see cref="System.Type"/> a Category/Name picker addresses it by (e.g. <c>typeof(ButtonId)</c>),
    /// and the <see cref="database"/> asset itself. The single value object both
    /// <see cref="NeoUISettings.GetDatabaseFor"/> and the ID Database Manager window resolve through, so
    /// the two can never drift over which databases exist.
    /// </summary>
    public readonly struct IdDatabaseDescriptor
    {
        /// <summary> Human-facing name for tabs/headers (e.g. "Buttons"). </summary>
        public readonly string label;
        /// <summary> The id asset type addressed by this database, or null for a label-only project database. </summary>
        public readonly Type idType;
        /// <summary> The database asset (may be null if the settings field is unassigned). </summary>
        public readonly IdDatabase database;
        /// <summary> A semantic accent hint (Doozy family color), e.g. "Interactive"/"Containers"/"Signals"/
        /// "Data". The editor maps it to a <c>NeoColors</c> accent; unknown/empty falls back to Data (yellow). </summary>
        public readonly string accent;

        public IdDatabaseDescriptor(string label, Type idType, IdDatabase database, string accent = null)
        {
            this.label = label;
            this.idType = idType;
            this.database = database;
            this.accent = accent;
        }
    }

    /// <summary>
    /// A consuming-project id database surfaced through <see cref="NeoUISettings.AllIdDatabases"/> — a
    /// project that defines its own <see cref="IdDatabase"/> type (and its own <see cref="CategoryNameId"/>
    /// subclass) registers a provider so its database appears in the ID Database Manager and resolves
    /// through <see cref="NeoUISettings.GetDatabaseFor"/>, with no package file edited.
    /// Mirrors the <see cref="INeoTriggerKind"/> seam (Pattern R): the package's eight built-in databases
    /// live as settings FIELDS and flow through the same enumerator; only a project's NOVEL database
    /// flows through this registry.
    /// </summary>
    public interface INeoIdDatabaseProvider
    {
        /// <summary>
        /// Yields the project's database descriptor(s) for the given settings asset. Called when the
        /// editor enumerates databases (never per frame). Return an empty sequence when nothing applies.
        /// </summary>
        IEnumerable<IdDatabaseDescriptor> Describe(NeoUISettings settings);
    }

    /// <summary>
    /// Pattern-R registry of project-supplied id database providers. The package's built-in databases
    /// are NOT registered here — they live as <see cref="NeoUISettings"/> fields and are enumerated
    /// directly, so this list is empty until a project registers (zero risk to built-in behavior).
    /// Register from <c>[RuntimeInitializeOnLoadMethod]</c> (or an editor <c>[InitializeOnLoad]</c>) so
    /// the registration survives a domain reload. Mirrors <see cref="NeoTriggerKinds"/>.
    /// </summary>
    public static class NeoIdDatabaseKinds
    {
        private static List<INeoIdDatabaseProvider> s_providers;

        private static List<INeoIdDatabaseProvider> Providers =>
            s_providers ?? (s_providers = new List<INeoIdDatabaseProvider>());

        /// <summary> Every registered project provider (empty by default — built-ins are not here). </summary>
        public static IReadOnlyList<INeoIdDatabaseProvider> All => Providers;

        /// <summary>
        /// Registers a provider, replacing an already-registered instance of the same concrete type
        /// (so re-registration after a reload never duplicates), otherwise appending.
        /// </summary>
        public static void Register(INeoIdDatabaseProvider provider)
        {
            if (provider == null)
            {
                Debug.LogWarning("NeoIdDatabaseKinds.Register: ignoring null provider.");
                return;
            }
            List<INeoIdDatabaseProvider> providers = Providers;
            for (int i = 0; i < providers.Count; i++)
                if (providers[i].GetType() == provider.GetType()) { providers[i] = provider; return; }
            providers.Add(provider);
        }

        /// <summary> Removes a registered provider by concrete type, if present (inverse of Register). </summary>
        public static void Unregister(INeoIdDatabaseProvider provider)
        {
            if (provider == null || s_providers == null) return;
            for (int i = s_providers.Count - 1; i >= 0; i--)
                if (s_providers[i].GetType() == provider.GetType()) s_providers.RemoveAt(i);
        }

        /// <summary> Test/seam hook: clears the registry (built-ins are unaffected — they aren't here). </summary>
        public static void ClearForTests() => s_providers?.Clear();
    }
}
