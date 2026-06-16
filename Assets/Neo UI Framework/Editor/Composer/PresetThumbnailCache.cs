using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// A static, editor-only cache of preset/kind thumbnails so the Composer never renders per
    /// <c>OnGUI</c> pass (the "no editor-tick visuals" performance rule). Each entry is rendered once via
    /// <see cref="PresetThumbnailRenderer"/> and reused; the cache OWNS the textures (callers never
    /// destroy them). Entries are keyed by (preset name OR element kind) + requested size + the active
    /// theme variant name + a content hash of the preset's styling fields, so editing a preset, resizing,
    /// or switching the theme variant produces a fresh entry rather than a stale picture.
    /// <para>
    /// <see cref="Invalidate"/> drops all entries (theme/bundle/preset change); <see cref="Clear"/> does
    /// the same on window close. Both <c>DestroyImmediate</c> the cached textures first. A cache HIT
    /// allocates nothing on the hot path. Null-safe throughout — a null preset / headless render simply
    /// caches and returns null.
    /// </para>
    /// </summary>
    public static class PresetThumbnailCache
    {
        private readonly struct Key : IEquatable<Key>
        {
            public readonly string id;       // preset name, or "kind:<kind>" for a bare kind
            public readonly int size;
            public readonly string variant;  // active theme variant name (null-safe)
            public readonly int contentHash;  // styling-field hash so an edit invalidates

            public Key(string id, int size, string variant, int contentHash)
            {
                this.id = id;
                this.size = size;
                this.variant = variant;
                this.contentHash = contentHash;
            }

            public bool Equals(Key other) =>
                size == other.size
                && contentHash == other.contentHash
                && string.Equals(id, other.id, StringComparison.Ordinal)
                && string.Equals(variant, other.variant, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = id != null ? StringComparer.Ordinal.GetHashCode(id) : 0;
                    hash = (hash * 397) ^ size;
                    hash = (hash * 397) ^ (variant != null ? StringComparer.Ordinal.GetHashCode(variant) : 0);
                    hash = (hash * 397) ^ contentHash;
                    return hash;
                }
            }
        }

        private static readonly Dictionary<Key, Texture2D> _entries = new Dictionary<Key, Texture2D>();

        /// <summary> Number of cached entries (including cached-null misses). </summary>
        public static int Count => _entries.Count;

        /// <summary>
        /// Returns the cached thumbnail for <paramref name="preset"/> at <paramref name="size"/>, rendering
        /// (and caching) it on the first request for that key. Returns null gracefully for a null preset or
        /// a headless/failed render — the null is cached so we don't retry every frame. The returned
        /// texture is owned by the cache; do not destroy it.
        /// </summary>
        public static Texture2D GetOrRender(NeoWidgetPreset preset, int size = PresetThumbnailRenderer.PaletteSize)
        {
            if (preset == null) return null;
            var key = new Key(preset.presetName ?? string.Empty, size, ActiveVariantName(), ContentHash(preset));
            if (_entries.TryGetValue(key, out Texture2D cached)) return cached;
            Texture2D rendered = PresetThumbnailRenderer.Render(preset, size);
            _entries[key] = rendered; // cache null too — avoids re-rendering a headless/failed thumbnail
            return rendered;
        }

        /// <summary>
        /// Returns the cached thumbnail for a bare element <paramref name="kind"/> (no preset) at
        /// <paramref name="size"/>, rendering it on first request. Same ownership / null-safety as
        /// <see cref="GetOrRender(NeoWidgetPreset,int)"/>.
        /// </summary>
        public static Texture2D GetOrRenderKind(string kind, int size = PresetThumbnailRenderer.PaletteSize)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            var key = new Key("kind:" + kind, size, ActiveVariantName(), 0);
            if (_entries.TryGetValue(key, out Texture2D cached)) return cached;
            Texture2D rendered = PresetThumbnailRenderer.Render(kind, size);
            _entries[key] = rendered;
            return rendered;
        }

        /// <summary>
        /// Drops every cached thumbnail (call when the theme/variant/bundle changes or a preset is edited
        /// in a way the content hash can't see, e.g. an upstream token recolor). Destroys the textures.
        /// </summary>
        public static void Invalidate() => ReleaseAll();

        /// <summary>
        /// Releases every cached texture and empties the cache (call on Composer window close so the
        /// ~tens-of-KB-per-thumb textures don't linger).
        /// </summary>
        public static void Clear() => ReleaseAll();

        private static void ReleaseAll()
        {
            foreach (Texture2D texture in _entries.Values)
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            _entries.Clear();
        }

        // ------------------------------------------------------------------ key inputs

        /// <summary> Active theme variant NAME (null-safe), so a variant switch keys a fresh thumbnail. </summary>
        private static string ActiveVariantName()
        {
            Theme theme = NeoUISettings.instance != null ? NeoUISettings.instance.theme : null;
            return theme != null && theme.activeVariant != null ? theme.activeVariant.name : null;
        }

        /// <summary>
        /// A stable hash over the preset's styling fields — anything that changes the rendered look. Two
        /// presets with identical styling hash the same; editing any styling field changes the hash so the
        /// old thumbnail is bypassed (and replaced on next access).
        /// </summary>
        private static int ContentHash(NeoWidgetPreset preset)
        {
            if (preset == null) return 0;
            var sb = new StringBuilder(128);
            sb.Append(preset.targetKind).Append('|')
              .Append(preset.variant).Append('|')
              .Append(preset.sizeVariant).Append('|')
              .Append(preset.textStyle).Append('|')
              .Append(preset.shapeStyle).Append('|')
              .Append(preset.motion).Append('|')
              .Append(preset.background).Append('|')
              .Append(preset.labelColor).Append('|')
              .Append(preset.icon).Append('|')
              .Append(preset.radius.ToString("R")).Append('|')
              .Append(preset.padding.ToString("R")).Append('|')
              .Append(preset.spacing.ToString("R")).Append('|');
            if (preset.padding4 != null)
                foreach (float v in preset.padding4) sb.Append(v.ToString("R")).Append(',');
            return StringComparer.Ordinal.GetHashCode(sb.ToString());
        }
    }
}
