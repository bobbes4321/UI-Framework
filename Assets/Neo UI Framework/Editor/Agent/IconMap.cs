using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Editor façade over the runtime <see cref="IconLibrary"/> — the one editor-side entry point
    /// the factory/exporter/spec tooling resolve icon names through. The glyph dictionary itself
    /// lives runtime-side (Runtime/Settings/IconLibrary.cs) so <see cref="NeoIcon.SetIcon"/> can
    /// resolve names in a build; add new icons there. A project overlay
    /// (<see cref="NeoUISettings.iconOverlay"/>) is consulted before the built-in Lucide dict.
    /// </summary>
    public static class IconMap
    {
        /// <summary> Every resolvable name: overlay, curated subset, then the full Lucide table. </summary>
        public static IEnumerable<string> Names => IconLibrary.Names;

        /// <summary> Overlay + curated names only (what the spec reference lists / pickers feature). </summary>
        public static IEnumerable<string> FeaturedNames => IconLibrary.FeaturedNames;

        /// <summary> Full resolution incl. sprite-backed overlay icons (project PNG art). </summary>
        public static bool TryResolveIcon(string name, out ResolvedIcon icon) =>
            IconLibrary.TryResolveIcon(name, out icon);

        public static int Count => IconLibrary.Count;

        /// <summary> All curated glyph characters — the set the icon font asset pre-bakes. </summary>
        public static string AllGlyphs() => IconLibrary.AllGlyphs();

        /// <summary> Resolves an icon name (or alias) to its font glyph. </summary>
        public static bool TryGetGlyph(string name, out char glyph) =>
            IconLibrary.TryGetGlyph(name, out glyph);

        /// <summary> Resolves a name AND returns its canonical form ("home" → "house") — what
        /// <see cref="NeoIcon"/> stamps so aliases never leak into exported specs. </summary>
        public static bool TryResolve(string name, out string canonicalName, out char glyph) =>
            IconLibrary.TryResolve(name, out canonicalName, out glyph);

        /// <summary> Reverse lookup for the exporter: glyph → canonical icon name. </summary>
        public static bool TryGetName(char glyph, out string name) =>
            IconLibrary.TryGetName(glyph, out name);
    }
}
