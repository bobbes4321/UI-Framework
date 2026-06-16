using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One particle-module descriptor: maps a module <c>id</c> + flat param bag ↔ the matching
    /// runtime <see cref="ParticleModuleConfig"/> subclass. The param bag is the project's existing
    /// JSON object model (a <see cref="Dictionary{String,Object}"/>, numbers as <see cref="double"/>,
    /// arrays as <see cref="List{Object}"/>) — the same one <see cref="GradientSpec"/> uses; no
    /// parallel JSON layer.
    ///
    /// <para>Keeps the particle MODULE set open: a consuming project ships a new
    /// <see cref="ParticleModuleConfig"/> subclass and registers a descriptor for it — no switch, no
    /// fork of the core spec pipeline.</para>
    /// </summary>
    public interface IParticleModuleDescriptor
    {
        /// <summary> Stable, agent-addressable module id (mirrors <see cref="ParticleModuleConfig.Id"/>), e.g. "gravity". </summary>
        string Id { get; }

        /// <summary> The config subclass this descriptor reads back (so the exporter can match by type). </summary>
        Type ConfigType { get; }

        /// <summary> Builds the serialized module config from a flat param bag (may be null → defaults). </summary>
        ParticleModuleConfig Build(IDictionary<string, object> parameters);

        /// <summary> Reads a module config's fields into a deterministic param bag (stable key order). </summary>
        IDictionary<string, object> Export(ParticleModuleConfig config);
    }

    /// <summary>
    /// Pattern-R registry of particle-module descriptors (mirrors <see cref="LayoutConstraints"/> /
    /// <see cref="ShapeEffectRegistry"/>). Built-ins register in the static ctor in a FIXED order;
    /// the exporter resolves a config back to its descriptor via <see cref="GetForConfig"/>.
    /// <see cref="All"/> / <see cref="Get"/> / <see cref="GetForConfig"/> / <see cref="Register"/>
    /// (replace-by-Id) / <see cref="ResetForTests"/>.
    /// </summary>
    public static class ParticleEffectRegistry
    {
        /// <summary> Built-in constant-acceleration module id. </summary>
        public const string Gravity = "gravity";
        /// <summary> Built-in velocity-damping module id. </summary>
        public const string Drag = "drag";
        /// <summary> Built-in color-over-life module id. </summary>
        public const string ColorOverLife = "colorOverLife";
        /// <summary> Built-in size-over-life module id. </summary>
        public const string SizeOverLife = "sizeOverLife";

        private static readonly List<IParticleModuleDescriptor> _all = new List<IParticleModuleDescriptor>();

        static ParticleEffectRegistry()
        {
            RegisterBuiltins();
        }

        private static void RegisterBuiltins()
        {
            Register(new GravityDescriptor());
            Register(new DragDescriptor());
            Register(new ColorOverLifeDescriptor());
            Register(new SizeOverLifeDescriptor());
        }

        /// <summary> Every registered descriptor (built-ins first, in registration order). </summary>
        public static IReadOnlyList<IParticleModuleDescriptor> All => _all;

        /// <summary> Finds the descriptor with the given id; null + warning when missing (no silent failure). </summary>
        public static IParticleModuleDescriptor Get(string id)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (IParticleModuleDescriptor d in _all)
                    if (d != null && d.Id == id) return d;
            Debug.LogWarning($"ParticleEffectRegistry.Get: no particle module '{id}' registered — the module will be skipped. Register one in ParticleEffectRegistry, or check the spec.");
            return null;
        }

        /// <summary> Finds the descriptor whose <see cref="IParticleModuleDescriptor.ConfigType"/> matches a config; null when unregistered. </summary>
        public static IParticleModuleDescriptor GetForConfig(ParticleModuleConfig config)
        {
            if (config == null) return null;
            Type t = config.GetType();
            foreach (IParticleModuleDescriptor d in _all)
                if (d != null && d.ConfigType == t) return d;
            return null;
        }

        /// <summary> Registers a descriptor, replacing any existing one with the same Id. </summary>
        public static void Register(IParticleModuleDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id))
            {
                Debug.LogWarning("ParticleEffectRegistry.Register ignored a descriptor with a null/empty Id.");
                return;
            }
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == descriptor.Id) { _all[i] = descriptor; return; }
            _all.Add(descriptor);
        }

        /// <summary> Test/seam hook: clear and re-seed the built-ins (static state survives a test run). </summary>
        internal static void ResetForTests()
        {
            _all.Clear();
            RegisterBuiltins();
        }

        // ----------------------------------------------------------------- param-bag helpers

        internal static float GetFloat(IDictionary<string, object> p, string key, float fallback) =>
            p != null && p.TryGetValue(key, out object v) && v is double d ? (float)d : fallback;

        internal static string GetString(IDictionary<string, object> p, string key, string fallback) =>
            p != null && p.TryGetValue(key, out object v) && v != null ? v.ToString() : fallback;

        internal static Vector2 GetVector2(IDictionary<string, object> p, string key, Vector2 fallback)
        {
            if (p == null || !p.TryGetValue(key, out object v) || !(v is List<object> list) || list.Count < 2)
                return fallback;
            float x = list[0] is double dx ? (float)dx : fallback.x;
            float y = list[1] is double dy ? (float)dy : fallback.y;
            return new Vector2(x, y);
        }

        internal static List<object> ToArray(Vector2 v) => new List<object> { (double)v.x, (double)v.y };

        internal static Ease ParseEase(IDictionary<string, object> p, string key, Ease fallback)
        {
            string s = GetString(p, key, null);
            return !string.IsNullOrEmpty(s) && Enum.TryParse(s, out Ease parsed) ? parsed : fallback;
        }

        // ----------------------------------------------------------------- built-in descriptors

        private sealed class GravityDescriptor : IParticleModuleDescriptor
        {
            public string Id => Gravity;
            public Type ConfigType => typeof(GravityModuleConfig);

            public ParticleModuleConfig Build(IDictionary<string, object> p)
            {
                var c = new GravityModuleConfig();
                c.acceleration = GetVector2(p, "acceleration", c.acceleration);
                return c;
            }

            public IDictionary<string, object> Export(ParticleModuleConfig config)
            {
                var c = (GravityModuleConfig)config;
                return new Dictionary<string, object> { ["acceleration"] = ToArray(c.acceleration) };
            }
        }

        private sealed class DragDescriptor : IParticleModuleDescriptor
        {
            public string Id => Drag;
            public Type ConfigType => typeof(DragModuleConfig);

            public ParticleModuleConfig Build(IDictionary<string, object> p)
            {
                var c = new DragModuleConfig();
                c.drag = GetFloat(p, "drag", c.drag);
                return c;
            }

            public IDictionary<string, object> Export(ParticleModuleConfig config)
            {
                var c = (DragModuleConfig)config;
                return new Dictionary<string, object> { ["drag"] = (double)c.drag };
            }
        }

        private sealed class ColorOverLifeDescriptor : IParticleModuleDescriptor
        {
            public string Id => ColorOverLife;
            public Type ConfigType => typeof(ColorOverLifeModuleConfig);

            public ParticleModuleConfig Build(IDictionary<string, object> p)
            {
                var c = new ColorOverLifeModuleConfig();
                string start = GetString(p, "start", null);
                string end = GetString(p, "end", null);
                if (!string.IsNullOrEmpty(start)) c.start = ParseColorRef(start);
                if (!string.IsNullOrEmpty(end)) c.end = ParseColorRef(end);
                c.ease = ParseEase(p, "ease", c.ease);
                return c;
            }

            public IDictionary<string, object> Export(ParticleModuleConfig config)
            {
                var c = (ColorOverLifeModuleConfig)config;
                return new Dictionary<string, object>
                {
                    ["start"] = ColorRefToString(c.start),
                    ["end"] = ColorRefToString(c.end),
                    ["ease"] = c.ease.ToString()
                };
            }
        }

        private sealed class SizeOverLifeDescriptor : IParticleModuleDescriptor
        {
            public string Id => SizeOverLife;
            public Type ConfigType => typeof(SizeOverLifeModuleConfig);

            public ParticleModuleConfig Build(IDictionary<string, object> p)
            {
                var c = new SizeOverLifeModuleConfig();
                c.startScale = GetFloat(p, "startScale", c.startScale);
                c.endScale = GetFloat(p, "endScale", c.endScale);
                c.ease = ParseEase(p, "ease", c.ease);
                return c;
            }

            public IDictionary<string, object> Export(ParticleModuleConfig config)
            {
                var c = (SizeOverLifeModuleConfig)config;
                return new Dictionary<string, object>
                {
                    ["startScale"] = (double)c.startScale,
                    ["endScale"] = (double)c.endScale,
                    ["ease"] = c.ease.ToString()
                };
            }
        }

        // ----------------------------------------------------------------- color ref round-trip

        /// <summary> "#hex" / "Category/Token" string → a theme-aware <see cref="ThemeColorRef"/>. </summary>
        internal static ThemeColorRef ParseColorRef(string value)
        {
            if (string.IsNullOrEmpty(value)) return new ThemeColorRef(Color.white);
            if (value.StartsWith("#"))
                return ColorUtils.TryParseHex(value, out Color c)
                    ? new ThemeColorRef(c)
                    : new ThemeColorRef(Color.white);
            return new ThemeColorRef(value); // a theme token ("Category/Name")
        }

        /// <summary> A <see cref="ThemeColorRef"/> → its token string (or "#hex" when raw). </summary>
        internal static string ColorRefToString(ThemeColorRef reference) =>
            reference == null ? "#FFFFFFFF"
            : reference.useToken ? reference.token
            : ColorUtils.ToHex(reference.color);
    }
}
