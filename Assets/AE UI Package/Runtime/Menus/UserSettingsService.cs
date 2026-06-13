using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AlterEyes.UI.Menus
{
    /// <summary>
    /// Static value store + binding hub for settings and cheats — the runtime half of the data-driven
    /// menu subsystem (the catalog is the declarative half). Resolves a control's value from, in order:
    /// a code binding (live), the persisted <see cref="Store"/>, then the catalog default. Writes
    /// persist (unless the control opts out), drive any binding, and emit a typed signal so game code
    /// reacts with <c>Signals.On&lt;T&gt;(UserSettingsService.SettingsCategory, "Audio/Master", ...)</c>.
    /// Shaped after <see cref="Signals"/>/ThemeService: a static service with subsystem-reset statics.
    /// </summary>
    public static class UserSettingsService
    {
        /// <summary> Signal stream category committed setting changes fire on. </summary>
        public const string SettingsCategory = "Settings";
        /// <summary> Signal stream category cheat fires use. </summary>
        public const string CheatCategory = "Cheat";
        /// <summary> Suffix appended to a category for continuous (non-committed) preview signals. </summary>
        public const string PreviewSuffix = ".Preview";

        private sealed class Binding
        {
            public Func<object> getter;
            public Action<object> setter;
            public bool persist;
        }

        private sealed class Entry
        {
            public MenuItemDefinition def;
            public string signalCategory;
        }

        private static IUserSettingsStore s_store;
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Binding> Bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);

        /// <summary> Fired after any committed change: (category, name, value). </summary>
        public static event Action<string, string, object> OnChanged;

        /// <summary> The persistence backend. Defaults to PlayerPrefs; assign to swap (cloud/save/tests). </summary>
        public static IUserSettingsStore Store
        {
            get => s_store ?? (s_store = new PlayerPrefsUserSettingsStore());
            set => s_store = value;
        }

        // ------------------------------------------------------------------ catalogs

        /// <summary> Registers a catalog so the service knows control defaults, value kinds and signal
        /// categories. Call from a presenter's Awake; safe to call repeatedly. </summary>
        public static void RegisterCatalog(MenuCatalog catalog)
        {
            if (catalog == null) return;
            foreach (MenuItemDefinition item in catalog.items)
            {
                if (item == null) continue;
                Entries[item.Id] = new Entry { def = item, signalCategory = catalog.ChangeSignalCategory };
            }
        }

        public static void UnregisterCatalog(MenuCatalog catalog)
        {
            if (catalog == null) return;
            foreach (MenuItemDefinition item in catalog.items)
                if (item != null) Entries.Remove(item.Id);
        }

        public static MenuItemDefinition GetDefinition(string category, string name) =>
            Entries.TryGetValue(Key(category, name), out Entry e) ? e.def : null;

        // ------------------------------------------------------------------ code bindings (CBN parity)

        /// <summary> Binds a control to live game state: <paramref name="getter"/> becomes the source of
        /// truth for reads and <paramref name="setter"/> is driven on writes. With <paramref name="persist"/>
        /// false the value is owned by the game's own save system (the store is not written). </summary>
        public static void Bind<T>(string category, string name, Func<T> getter, Action<T> setter, bool persist = false)
        {
            string key = Key(category, name);
            Bindings[key] = new Binding
            {
                getter = getter == null ? (Func<object>)null : () => getter(),
                setter = setter == null ? (Action<object>)null : v => setter(ConvertTo<T>(v)),
                persist = persist
            };
        }

        public static void Unbind(string category, string name) => Bindings.Remove(Key(category, name));

        // ------------------------------------------------------------------ typed get/set

        public static T Get<T>(string category, string name)
        {
            string key = Key(category, name);

            if (Bindings.TryGetValue(key, out Binding binding) && binding.getter != null)
                return ConvertTo<T>(binding.getter());

            if (Store.TryGet(key, out string stored))
                return FromString<T>(stored);

            if (Entries.TryGetValue(key, out Entry entry))
                return FromString<T>(entry.def.defaultValue);

            if (binding == null)
                Debug.LogWarning($"[UserSettingsService] Get on unknown setting '{key}' — no binding, store value or catalog entry. Returning default.");
            return default;
        }

        public static bool TryGet<T>(string category, string name, out T value)
        {
            string key = Key(category, name);
            if (Bindings.TryGetValue(key, out Binding binding) && binding.getter != null)
            {
                value = ConvertTo<T>(binding.getter());
                return true;
            }
            if (Store.TryGet(key, out string stored))
            {
                value = FromString<T>(stored);
                return true;
            }
            if (Entries.TryGetValue(key, out Entry entry))
            {
                value = FromString<T>(entry.def.defaultValue);
                return true;
            }
            value = default;
            return false;
        }

        /// <summary> Writes a value. <paramref name="commit"/> false is a live preview (drives bindings,
        /// fires the <c>.Preview</c> signal, does not persist) — the slider-drag vs slider-release split. </summary>
        public static void Set<T>(string category, string name, T value, bool commit = true)
        {
            string key = Key(category, name);
            Entries.TryGetValue(key, out Entry entry);
            Bindings.TryGetValue(key, out Binding binding);
            if (entry == null && binding == null)
                Debug.LogWarning($"[UserSettingsService] Set on unknown setting '{key}' — no binding or catalog entry. Value will still persist + signal.");

            string signalCategory = entry?.signalCategory ?? SettingsCategory;

            binding?.setter?.Invoke(value);

            if (commit)
            {
                bool persist = (binding == null || binding.persist) && (entry == null || entry.def.persisted);
                if (persist)
                {
                    Store.Set(key, ToString(value));
                    Store.Save();
                }
                Signals.Send<T>(signalCategory, key, value);
                OnChanged?.Invoke(category, name, value);
            }
            else
            {
                Signals.Send<T>(signalCategory + PreviewSuffix, key, value);
            }
        }

        // ------------------------------------------------------------------ object-typed convenience (presenters)

        /// <summary> Reads the control's value boxed per its <see cref="MenuItemDefinition.ValueKind"/>. </summary>
        public static object GetValue(MenuItemDefinition item)
        {
            switch (item.ValueKind)
            {
                case MenuValueKind.Bool: return Get<bool>(item.Category, item.Name);
                case MenuValueKind.Float: return Get<float>(item.Category, item.Name);
                case MenuValueKind.Int: return Get<int>(item.Category, item.Name);
                case MenuValueKind.String: return Get<string>(item.Category, item.Name);
                default: return null;
            }
        }

        /// <summary> Writes the control's value, dispatching by its <see cref="MenuItemDefinition.ValueKind"/>. </summary>
        public static void SetValue(MenuItemDefinition item, object value, bool commit = true)
        {
            switch (item.ValueKind)
            {
                case MenuValueKind.Bool: Set(item.Category, item.Name, ConvertTo<bool>(value), commit); break;
                case MenuValueKind.Float: Set(item.Category, item.Name, ConvertTo<float>(value), commit); break;
                case MenuValueKind.Int: Set(item.Category, item.Name, ConvertTo<int>(value), commit); break;
                case MenuValueKind.String: Set(item.Category, item.Name, ConvertTo<string>(value), commit); break;
            }
        }

        /// <summary> Fires a cheat (parameterless or carrying a value) on the cheat signal stream. </summary>
        public static void FireCheat(string category, string name, object payload = null)
        {
            string key = Key(category, name);
            if (payload == null) Signals.Send(CheatCategory, key);
            else Signals.Send<object>(CheatCategory, key, payload);
        }

        // ------------------------------------------------------------------ reset

        public static void ResetToDefault(MenuItemDefinition item)
        {
            if (item == null || !item.HasValue) return;
            Store.Delete(Key(item.Category, item.Name));
            Store.Save();
            SetValue(item, GetValue(item)); // re-emit committed signal at the default value
        }

        public static void ResetAll(MenuCatalog catalog)
        {
            if (catalog == null) return;
            foreach (MenuItemDefinition item in catalog.items)
                if (item != null && item.HasValue) ResetToDefault(item);
        }

        // ------------------------------------------------------------------ conversion

        private static string Key(string category, string name)
        {
            category = string.IsNullOrWhiteSpace(category) ? CategoryNameId.DefaultCategory : category.Trim();
            name = string.IsNullOrWhiteSpace(name) ? CategoryNameId.DefaultName : name.Trim();
            return $"{category}/{name}";
        }

        private static string ToString(object value)
        {
            if (value == null) return string.Empty;
            if (value is bool b) return b ? "True" : "False";
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static T FromString<T>(string s)
        {
            if (string.IsNullOrEmpty(s)) return default;
            Type t = typeof(T);
            try
            {
                if (t == typeof(string)) return (T)(object)s;
                if (t == typeof(bool))
                {
                    if (s == "1") return (T)(object)true;
                    if (s == "0") return (T)(object)false;
                    return (T)(object)bool.Parse(s);
                }
                if (t == typeof(int)) return (T)(object)(int)Math.Round(double.Parse(s, CultureInfo.InvariantCulture));
                if (t == typeof(float)) return (T)(object)float.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(double)) return (T)(object)double.Parse(s, CultureInfo.InvariantCulture);
                if (t.IsEnum) return (T)Enum.Parse(t, s, true);
                return (T)Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
            }
            catch
            {
                Debug.LogWarning($"[UserSettingsService] Could not parse '{s}' as {t.Name}; returning default.");
                return default;
            }
        }

        private static T ConvertTo<T>(object value)
        {
            if (value == null) return default;
            if (value is T already) return already;
            Type t = typeof(T);
            try
            {
                if (t == typeof(string)) return (T)(object)ToString(value);
                if (t.IsEnum) return (T)Enum.ToObject(t, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                return (T)Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
            }
            catch
            {
                return FromString<T>(ToString(value));
            }
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary> Clears catalogs, bindings and (if in-memory) the store. Test isolation / reset. </summary>
        public static void ClearAll()
        {
            Entries.Clear();
            Bindings.Clear();
            (s_store as InMemoryUserSettingsStore)?.Clear();
            OnChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Entries.Clear();
            Bindings.Clear();
            OnChanged = null;
            s_store = null;
        }
    }
}
