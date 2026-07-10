// Generated from Lucide 1.17.0 codepoints.json — curated subset for agent specs.
// Lucide is ISC licensed; see "Assets/Neo UI Framework/Fonts/Lucide License - ISC.txt".
// To add icons: pick the name from https://lucide.dev, copy its codepoint from the
// release's codepoints.json, and add a line below (keep the list sorted by theme).
using System.Collections.Generic;

namespace Neo.UI
{
    /// <summary>
    /// Name → glyph map for the committed Lucide icon font, resolvable at RUNTIME so
    /// <see cref="NeoIcon.SetIcon"/> can swap glyphs by name in a build. A project overlay
    /// (<see cref="NeoUISettings.iconOverlay"/>) is consulted before the built-in dictionary.
    /// Agents reference icons by these names in specs ("icon": "play"); the exporter
    /// reverse-maps glyphs back to names. Editor code goes through the <c>IconMap</c> façade.
    /// </summary>
    public static partial class IconLibrary
    {
        private static readonly Dictionary<string, char> Glyphs = new Dictionary<string, char>
        {
            ["play"] = '\uE13C',
            ["pause"] = '\uE12E',
            ["square"] = '\uE167',
            ["skip-forward"] = '\uE160',
            ["skip-back"] = '\uE15F',
            ["fast-forward"] = '\uE0BD',
            ["rewind"] = '\uE147',
            ["settings"] = '\uE154',
            ["settings-2"] = '\uE245',
            ["sliders-horizontal"] = '\uE29A',
            ["x"] = '\uE1B2',
            ["check"] = '\uE06C',
            ["check-check"] = '\uE38E',
            ["chevron-up"] = '\uE070',
            ["chevron-down"] = '\uE06D',
            ["chevron-left"] = '\uE06E',
            ["chevron-right"] = '\uE06F',
            ["chevrons-up"] = '\uE074',
            ["chevrons-down"] = '\uE071',
            ["chevrons-left"] = '\uE072',
            ["chevrons-right"] = '\uE073',
            ["arrow-up"] = '\uE04A',
            ["arrow-down"] = '\uE042',
            ["arrow-left"] = '\uE048',
            ["arrow-right"] = '\uE049',
            ["arrow-up-right"] = '\uE04D',
            ["undo-2"] = '\uE2A1',
            ["redo-2"] = '\uE2A0',
            ["heart"] = '\uE0F2',
            ["heart-crack"] = '\uE2D6',
            ["star"] = '\uE176',
            ["star-half"] = '\uE20B',
            ["sparkles"] = '\uE412',
            ["coins"] = '\uE097',
            ["gem"] = '\uE242',
            ["diamond"] = '\uE2D2',
            ["shield"] = '\uE158',
            ["shield-check"] = '\uE1FF',
            ["shield-alert"] = '\uE1FE',
            ["sword"] = '\uE2B3',
            ["swords"] = '\uE2B4',
            ["axe"] = '\uE050',
            ["crosshair"] = '\uE0AC',
            ["target"] = '\uE180',
            ["wand-sparkles"] = '\uE357',
            ["shopping-bag"] = '\uE15B',
            ["shopping-cart"] = '\uE15C',
            ["backpack"] = '\uE2C8',
            ["package"] = '\uE129',
            ["gift"] = '\uE0E1',
            ["lock"] = '\uE10B',
            ["lock-open"] = '\uE10C',
            ["key"] = '\uE0FD',
            ["volume"] = '\uE1A9',
            ["volume-1"] = '\uE1AA',
            ["volume-2"] = '\uE1AB',
            ["volume-x"] = '\uE1AC',
            ["volume-off"] = '\uE626',
            ["music"] = '\uE122',
            ["mic"] = '\uE118',
            ["mic-off"] = '\uE119',
            ["bell"] = '\uE059',
            ["bell-off"] = '\uE05A',
            ["user"] = '\uE19F',
            ["users"] = '\uE1A4',
            ["user-plus"] = '\uE1A2',
            ["circle-user"] = '\uE461',
            ["smile"] = '\uE164',
            ["frown"] = '\uE0DB',
            ["trash"] = '\uE18D',
            ["trash-2"] = '\uE18E',
            ["pencil"] = '\uE1F9',
            ["plus"] = '\uE13D',
            ["minus"] = '\uE11C',
            ["circle-plus"] = '\uE081',
            ["circle-minus"] = '\uE07E',
            ["info"] = '\uE0F9',
            ["circle-alert"] = '\uE077',
            ["triangle-alert"] = '\uE193',
            ["circle-help"] = '\uE082',
            ["circle-check"] = '\uE226',
            ["circle-x"] = '\uE084',
            ["octagon-alert"] = '\uE127',
            ["house"] = '\uE0F5',
            ["search"] = '\uE151',
            ["zoom-in"] = '\uE1B6',
            ["zoom-out"] = '\uE1B7',
            ["refresh-cw"] = '\uE145',
            ["refresh-ccw"] = '\uE144',
            ["rotate-cw"] = '\uE149',
            ["rotate-ccw"] = '\uE148',
            ["share-2"] = '\uE156',
            ["external-link"] = '\uE0B9',
            ["download"] = '\uE0B2',
            ["upload"] = '\uE19E',
            ["save"] = '\uE14D',
            ["trophy"] = '\uE373',
            ["medal"] = '\uE36F',
            ["crown"] = '\uE1D6',
            ["award"] = '\uE04F',
            ["map"] = '\uE110',
            ["map-pin"] = '\uE111',
            ["compass"] = '\uE09B',
            ["flag"] = '\uE0D1',
            ["clock"] = '\uE087',
            ["timer"] = '\uE1E0',
            ["hourglass"] = '\uE296',
            ["calendar"] = '\uE063',
            ["zap"] = '\uE1B4',
            ["flame"] = '\uE0D2',
            ["droplet"] = '\uE0B4',
            ["snowflake"] = '\uE165',
            ["sun"] = '\uE178',
            ["moon"] = '\uE11E',
            ["cloud"] = '\uE088',
            ["eye"] = '\uE0BA',
            ["eye-off"] = '\uE0BB',
            ["camera"] = '\uE064',
            ["image"] = '\uE0F6',
            ["video"] = '\uE1A5',
            ["film"] = '\uE0D0',
            ["gamepad-2"] = '\uE0DF',
            ["dice-5"] = '\uE28B',
            ["puzzle"] = '\uE29C',
            ["ghost"] = '\uE20E',
            ["skull"] = '\uE221',
            ["bot"] = '\uE1BB',
            ["rocket"] = '\uE286',
            ["wrench"] = '\uE1B1',
            ["hammer"] = '\uE0EC',
            ["menu"] = '\uE115',
            ["ellipsis"] = '\uE0B6',
            ["ellipsis-vertical"] = '\uE0B7',
            ["list"] = '\uE106',
            ["layout-grid"] = '\uE0FF',
            ["filter"] = '\uE0DC',
            ["send"] = '\uE152',
            ["mail"] = '\uE10F',
            ["message-circle"] = '\uE116',
            ["message-square"] = '\uE117',
            ["phone"] = '\uE133',
            ["wifi"] = '\uE1AE',
            ["wifi-off"] = '\uE1AF',
            ["battery"] = '\uE053',
            ["battery-charging"] = '\uE054',
            ["battery-low"] = '\uE056',
            ["bluetooth"] = '\uE05C',
            ["power"] = '\uE140',
            ["log-in"] = '\uE10D',
            ["log-out"] = '\uE10E',
            ["maximize"] = '\uE112',
            ["minimize"] = '\uE11A',
            ["copy"] = '\uE09E',
            ["clipboard"] = '\uE085',
            ["link"] = '\uE102',
            ["paperclip"] = '\uE12D',
            ["pin"] = '\uE259',
            ["bookmark"] = '\uE060',
            ["tag"] = '\uE17F',
            ["folder"] = '\uE0D7',
            ["file"] = '\uE0C0',
            ["file-text"] = '\uE0CC',
            ["book"] = '\uE05E',
            ["book-open"] = '\uE05F',
            ["lightbulb"] = '\uE1C2',
            ["palette"] = '\uE1DD',
            ["brush"] = '\uE1D3',
            ["pipette"] = '\uE13B',
            ["move"] = '\uE121',
            ["hand"] = '\uE1D7',
            ["pointer"] = '\uE1E8',
            ["thumbs-up"] = '\uE18A',
            ["thumbs-down"] = '\uE189',
            ["banknote"] = '\uE052',
            ["wallet"] = '\uE204',
            ["credit-card"] = '\uE0AA',
            ["scale"] = '\uE212',
            ["leaf"] = '\uE2DE',
            ["mountain"] = '\uE231',
            ["anchor"] = '\uE03F',
            ["plane"] = '\uE1DE',
            ["car"] = '\uE1D5',
            ["bike"] = '\uE1D2',
            ["footprints"] = '\uE3B9',
            ["castle"] = '\uE3E0',
        };

        /// <summary> Forgiving aliases: common agent vocabulary → canonical Lucide names. </summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            ["home"] = "house",
            ["stop"] = "square",
            ["warning"] = "triangle-alert",
            ["alert"] = "triangle-alert",
            ["error"] = "circle-x",
            ["help"] = "circle-help",
            ["close"] = "x",
            ["coin"] = "coins",
            ["bag"] = "shopping-bag",
            ["cart"] = "shopping-cart",
            ["unlock"] = "lock-open",
            ["refresh"] = "refresh-cw",
            ["share"] = "share-2",
            ["more"] = "ellipsis",
            ["edit"] = "pencil",
            ["delete"] = "trash-2",
            ["wand"] = "wand-sparkles",
            ["volume-mute"] = "volume-x",
            ["grid"] = "layout-grid",
            ["sound"] = "volume-2"
        };

        private static Dictionary<char, string> s_reverse;

        private static IconMapOverlay Overlay
        {
            get
            {
                NeoUISettings settings = NeoUISettings.instance;
                return settings != null ? settings.iconOverlay : null;
            }
        }

        /// <summary> Every resolvable name: overlay (sprites + glyphs), then the curated subset,
        /// then the full Lucide table — deduped, in that order, so pickers show the project's own
        /// and the featured icons first (relevance is a sort, never a hard filter). </summary>
        public static IEnumerable<string> Names
        {
            get
            {
                var seen = new HashSet<string>();
                foreach (string name in FeaturedNames)
                    if (seen.Add(name)) yield return name;
                foreach (string name in FullGlyphs.Keys)
                    if (seen.Add(name)) yield return name;
            }
        }

        /// <summary> Overlay names plus the curated subset only — what the spec reference lists
        /// and the picker features on top (the full table is reachable through search). </summary>
        public static IEnumerable<string> FeaturedNames
        {
            get
            {
                var seen = new HashSet<string>();
                IconMapOverlay overlay = Overlay;
                if (overlay != null)
                    foreach (string name in overlay.Names())
                        if (seen.Add(name)) yield return name;
                foreach (string name in Glyphs.Keys)
                    if (seen.Add(name)) yield return name;
            }
        }

        public static int Count => Glyphs.Count;

        /// <summary> True when the name is in the curated (featured, pre-baked) subset. </summary>
        public static bool IsCurated(string name) =>
            !string.IsNullOrEmpty(name) && Glyphs.ContainsKey(name);

        /// <summary> The built-in alias table (read-only) — pickers fold these into search so
        /// typing "home" finds "house". </summary>
        public static IReadOnlyDictionary<string, string> BuiltInAliases => Aliases;

        /// <summary> All curated glyph characters — the set the icon font asset pre-bakes. </summary>
        public static string AllGlyphs()
        {
            var characters = new char[Glyphs.Count];
            int i = 0;
            foreach (char glyph in Glyphs.Values) characters[i++] = glyph;
            return new string(characters);
        }

        /// <summary> Resolves an icon name (or alias) to its font glyph. A project overlay on the
        /// settings asset is consulted first, then the built-in Lucide dict + forgiving aliases. </summary>
        public static bool TryGetGlyph(string name, out char glyph)
        {
            return TryResolve(name, out _, out glyph);
        }

        /// <summary>
        /// Like <see cref="TryGetGlyph"/> but also returns the canonical name ("home" → "house") —
        /// the name <see cref="NeoIcon"/> stamps and the exporter reads back, so aliases never
        /// leak into specs.
        /// </summary>
        public static bool TryResolve(string name, out string canonicalName, out char glyph)
        {
            canonicalName = null;
            glyph = default;
            if (string.IsNullOrEmpty(name)) return false;
            IconMapOverlay overlay = Overlay;
            if (overlay != null && overlay.TryGetGlyph(name, out glyph))
            {
                canonicalName = overlay.TryGetName(glyph, out string overlayName) ? overlayName : name;
                return true;
            }
            if (Aliases.TryGetValue(name, out string canonical)) name = canonical;
            if (!Glyphs.TryGetValue(name, out glyph) && !FullGlyphs.TryGetValue(name, out glyph))
                return false;
            canonicalName = name;
            return true;
        }

        /// <summary>
        /// Full resolution including sprite-backed overlay icons (a project's own PNG art wrapped
        /// in a TMP sprite asset). Order: overlay sprites → overlay glyphs → curated Lucide subset
        /// → full Lucide table. <see cref="ResolvedIcon.BakedText"/> is what a TMP text bakes.
        /// </summary>
        public static bool TryResolveIcon(string name, out ResolvedIcon icon)
        {
            icon = default;
            if (string.IsNullOrEmpty(name)) return false;
            IconMapOverlay overlay = Overlay;
            if (overlay != null && overlay.TryGetSprite(name, out IconMapOverlay.SpriteEntry sprite))
            {
                icon = new ResolvedIcon
                {
                    name = sprite.name,
                    isSprite = true,
                    spriteAsset = sprite.spriteAsset,
                    spriteName = string.IsNullOrEmpty(sprite.spriteName) ? sprite.name : sprite.spriteName,
                    tint = sprite.tint
                };
                return true;
            }
            if (!TryResolve(name, out string canonical, out char glyph)) return false;
            icon = new ResolvedIcon { name = canonical, glyph = glyph };
            return true;
        }

        /// <summary> Reverse lookup for the exporter: glyph → canonical icon name. The project
        /// overlay is consulted first so custom glyphs export under their overlay name. </summary>
        public static bool TryGetName(char glyph, out string name)
        {
            IconMapOverlay overlay = Overlay;
            if (overlay != null && overlay.TryGetName(glyph, out name)) return true;
            if (s_reverse == null)
            {
                // full table first so curated names win on any codepoint collision
                s_reverse = new Dictionary<char, string>(FullGlyphs.Count);
                foreach (KeyValuePair<string, char> entry in FullGlyphs)
                    s_reverse[entry.Value] = entry.Key;
                foreach (KeyValuePair<string, char> entry in Glyphs)
                    s_reverse[entry.Value] = entry.Key;
            }
            return s_reverse.TryGetValue(glyph, out name);
        }
    }

    /// <summary> A resolved icon: either a font glyph or a sprite-backed overlay entry.
    /// <see cref="BakedText"/> is the TMP text that renders it. </summary>
    public struct ResolvedIcon
    {
        public string name;
        public bool isSprite;
        public char glyph;
        public TMPro.TMP_SpriteAsset spriteAsset;
        public string spriteName;
        public bool tint;

        public string BakedText => isSprite
            ? (tint ? $"<sprite name=\"{spriteName}\" tint=1>" : $"<sprite name=\"{spriteName}\">")
            : glyph.ToString();
    }
}
