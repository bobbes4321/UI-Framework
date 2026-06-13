using System.Collections.Generic;
using UnityEngine;

namespace AlterEyes.UI.Menus
{
    /// <summary>
    /// Persistence backend for setting values. Deals only in strings — <see cref="UserSettingsService"/>
    /// owns the typed conversion — so a game can swap in a cloud / save-file store without touching the
    /// UI. Mirrors the intent of common's PlayerPrefsHelper but is injectable.
    /// </summary>
    public interface IUserSettingsStore
    {
        bool TryGet(string key, out string value);
        void Set(string key, string value);
        void Delete(string key);
        bool Has(string key);
        /// <summary> Flush to durable storage (PlayerPrefs.Save, file write, …). </summary>
        void Save();
    }

    /// <summary> In-memory store: the default before a game injects its own, and ideal for tests. </summary>
    public sealed class InMemoryUserSettingsStore : IUserSettingsStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public bool TryGet(string key, out string value) => _values.TryGetValue(key, out value);
        public void Set(string key, string value) => _values[key] = value;
        public void Delete(string key) => _values.Remove(key);
        public bool Has(string key) => _values.ContainsKey(key);
        public void Save() { }
        public void Clear() => _values.Clear();
    }

    /// <summary>
    /// PlayerPrefs-backed store. Keys are namespaced with a prefix to avoid collisions. Values are
    /// stored as strings (the service converts), so one PlayerPrefs API covers every value kind.
    /// </summary>
    public sealed class PlayerPrefsUserSettingsStore : IUserSettingsStore
    {
        private readonly string _prefix;

        public PlayerPrefsUserSettingsStore(string prefix = "AEUI.Settings.")
        {
            _prefix = prefix ?? string.Empty;
        }

        private string Key(string key) => _prefix + key;

        public bool TryGet(string key, out string value)
        {
            string full = Key(key);
            if (PlayerPrefs.HasKey(full))
            {
                value = PlayerPrefs.GetString(full);
                return true;
            }
            value = null;
            return false;
        }

        public void Set(string key, string value) => PlayerPrefs.SetString(Key(key), value);
        public void Delete(string key) => PlayerPrefs.DeleteKey(Key(key));
        public bool Has(string key) => PlayerPrefs.HasKey(Key(key));
        public void Save() => PlayerPrefs.Save();
    }
}
