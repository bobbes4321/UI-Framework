using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A project-authored overlay over the built-in Lucide icon set (Pattern A — see
    /// <c>extensibility-seam-widget-attributes-plan.md</c>). Referenced from
    /// <see cref="NeoUISettings.iconOverlay"/>; <c>IconMap.TryGetGlyph</c> / <c>Names</c> consult it
    /// BEFORE the built-in dictionary, so a project adds brand glyphs (and forgiving aliases for
    /// them) without forking <c>IconMap</c>. The built-in ~170-glyph dict and alias table are left
    /// untouched — this only blends in.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Icon Map Overlay", fileName = "IconMapOverlay")]
    public class IconMapOverlay : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Icon name referenced in specs (\"icon\": \"brand-logo\")")]
            public string name;

            [Tooltip("Glyph codepoint in hex, e.g. F101 (a single private-use char in the project's icon font)")]
            public string codepoint;
        }

        [Serializable]
        public class Alias
        {
            [Tooltip("Vocabulary the agent might use")]
            public string alias;

            [Tooltip("Canonical icon name it resolves to (overlay glyph or built-in)")]
            public string target;
        }

        [Serializable]
        public class SpriteEntry
        {
            [Tooltip("Icon name referenced in specs (\"icon\": \"currency\")")]
            public string name;

            [Tooltip("TMP sprite asset wrapping the icon art (create one via the Design System Icons tab)")]
            public TMPro.TMP_SpriteAsset spriteAsset;

            [Tooltip("Sprite character name inside the asset; empty = same as the icon name")]
            public string spriteName;

            [Tooltip("Tint with the label color (white/alpha art) instead of rendering as-is (color art)")]
            public bool tint;
        }

        [Tooltip("Custom name → glyph entries; blended in before the built-in Lucide dict")]
        public List<Entry> glyphs = new List<Entry>();

        [Tooltip("Custom name → sprite entries (PNG art via TMP sprite assets); resolved before everything else")]
        public List<SpriteEntry> sprites = new List<SpriteEntry>();

        [Tooltip("Forgiving aliases for the overlay glyphs; consulted before the built-in aliases")]
        public List<Alias> aliases = new List<Alias>();

        /// <summary> Resolves a name (or overlay alias) to an overlay glyph. </summary>
        public bool TryGetGlyph(string name, out char glyph)
        {
            glyph = default;
            if (string.IsNullOrEmpty(name)) return false;
            if (aliases != null)
                foreach (Alias a in aliases)
                    if (a != null && string.Equals(a.alias, name, StringComparison.Ordinal))
                    {
                        name = a.target;
                        break;
                    }
            if (glyphs == null) return false;
            foreach (Entry e in glyphs)
            {
                if (e == null || !string.Equals(e.name, name, StringComparison.Ordinal)) continue;
                if (TryParseGlyph(e.codepoint, out glyph)) return true;
            }
            return false;
        }

        /// <summary> Reverse lookup: glyph → overlay name (for the exporter). </summary>
        public bool TryGetName(char glyph, out string name)
        {
            name = null;
            if (glyphs == null) return false;
            foreach (Entry e in glyphs)
                if (e != null && TryParseGlyph(e.codepoint, out char g) && g == glyph)
                {
                    name = e.name;
                    return true;
                }
            return false;
        }

        /// <summary> Resolves a name (or overlay alias) to a sprite-backed entry. </summary>
        public bool TryGetSprite(string name, out SpriteEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(name) || sprites == null) return false;
            if (aliases != null)
                foreach (Alias a in aliases)
                    if (a != null && string.Equals(a.alias, name, StringComparison.Ordinal))
                    {
                        name = a.target;
                        break;
                    }
            foreach (SpriteEntry e in sprites)
            {
                if (e == null || e.spriteAsset == null) continue;
                if (!string.Equals(e.name, name, StringComparison.Ordinal)) continue;
                entry = e;
                return true;
            }
            return false;
        }

        /// <summary> All overlay icon names — sprites first, then glyphs (picker / font baking). </summary>
        public IEnumerable<string> Names()
        {
            if (sprites != null)
                foreach (SpriteEntry e in sprites)
                    if (e != null && !string.IsNullOrEmpty(e.name)) yield return e.name;
            if (glyphs == null) yield break;
            foreach (Entry e in glyphs)
                if (e != null && !string.IsNullOrEmpty(e.name)) yield return e.name;
        }

        private static bool TryParseGlyph(string codepoint, out char glyph)
        {
            glyph = default;
            if (string.IsNullOrEmpty(codepoint)) return false;
            string hex = codepoint.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
                ? codepoint.Substring(2)
                : codepoint;
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int code) &&
                code > 0 && code <= char.MaxValue)
            {
                glyph = (char)code;
                return true;
            }
            return false;
        }
    }
}
