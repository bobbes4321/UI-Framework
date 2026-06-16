using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Neo.UI.Editor
{
    /// <summary>
    /// "Tools → Neo UI → Create or Repair Fonts": generates the committed TMP SDF font
    /// assets for the package's Inter family (Regular/SemiBold/Bold) next to their TTFs under
    /// <c>Assets/Neo UI Framework/Fonts</c>. One-time and idempotent — existing assets are reused,
    /// never regenerated (font asset generation is slow and Unity-version-sensitive, so the
    /// generated .asset files are committed, exactly like the spec's theme assets).
    /// </summary>
    public static class FontAssetBootstrap
    {
        public const string FontsFolder = "Assets/Neo UI Framework/Fonts";
        public const string InterRegularAssetPath = FontsFolder + "/Inter-Regular SDF.asset";
        public const string InterSemiBoldAssetPath = FontsFolder + "/Inter-SemiBold SDF.asset";
        public const string InterBoldAssetPath = FontsFolder + "/Inter-Bold SDF.asset";
        public const string LucideAssetPath = FontsFolder + "/Lucide SDF.asset";

        /// <summary>
        /// Pre-baked glyph set: ASCII plus the typographic punctuation widgets actually use
        /// (the stepper's true minus U+2212, dashes, curly quotes, ellipsis, °×±•…).
        /// The assets stay Dynamic so anything missing is still added on demand in the editor.
        /// </summary>
        private const string PrebakedCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~" +
            " ¡¢£¤¥¦§¨©ª«¬®¯°±²³´µ¶·¸¹º»¼½¾¿×÷" +
            "–—‘’‚“”„†‡•…‰‹›−€™";

        [MenuItem("Tools/Neo UI/Setup/Create or Repair Fonts", priority = 102)]
        public static void CreateOrRepairMenu()
        {
            TMP_FontAsset regular = EnsureFontAsset($"{FontsFolder}/Inter-Regular.ttf", InterRegularAssetPath);
            TMP_FontAsset semiBold = EnsureFontAsset($"{FontsFolder}/Inter-SemiBold.ttf", InterSemiBoldAssetPath);
            TMP_FontAsset bold = EnsureFontAsset($"{FontsFolder}/Inter-Bold.ttf", InterBoldAssetPath);
            TMP_FontAsset icons = EnsureIconFont(NeoUISettings.instance);
            Debug.Log($"[Neo.UI] Fonts: regular={(regular != null)}, semiBold={(semiBold != null)}, " +
                      $"bold={(bold != null)}, icons={(icons != null)}");
        }

        public static TMP_FontAsset InterRegular => EnsureFontAsset($"{FontsFolder}/Inter-Regular.ttf", InterRegularAssetPath);
        public static TMP_FontAsset InterSemiBold => EnsureFontAsset($"{FontsFolder}/Inter-SemiBold.ttf", InterSemiBoldAssetPath);
        public static TMP_FontAsset InterBold => EnsureFontAsset($"{FontsFolder}/Inter-Bold.ttf", InterBoldAssetPath);

        /// <summary>
        /// The Lucide icon font asset, pre-baked with the curated <see cref="IconMap"/> glyph set
        /// and registered on the settings asset (the exporter detects icon texts by this font).
        /// </summary>
        public static TMP_FontAsset EnsureIconFont(NeoUISettings settings)
        {
            TMP_FontAsset icons = EnsureFontAsset($"{FontsFolder}/Lucide.ttf", LucideAssetPath, IconMap.AllGlyphs());
            if (settings != null && icons != null && settings.iconFont != icons)
            {
                settings.iconFont = icons;
                EditorUtility.SetDirty(settings);
            }
            return icons;
        }

        /// <summary>
        /// Loads the TMP font asset at <paramref name="assetPath"/>, creating it from the TTF on
        /// first run. Also used for the icon font; pass a custom glyph set via
        /// <paramref name="characters"/>.
        /// </summary>
        public static TMP_FontAsset EnsureFontAsset(string ttfPath, string assetPath, string characters = null)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogWarning($"[Neo.UI] Font file '{ttfPath}' not found — cannot create '{assetPath}'");
                return null;
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font, 90, 9,
                GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
            if (fontAsset == null)
            {
                Debug.LogWarning($"[Neo.UI] TMP could not create a font asset from '{ttfPath}'");
                return null;
            }

            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            fontAsset.name = assetName;
            fontAsset.TryAddCharacters(characters ?? PrebakedCharacters, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[Neo.UI] '{assetName}': glyphs missing from source font: {missing}");

            // name sub-objects BEFORE CreateAsset — the create-import serializes the whole group
            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                fontAsset.atlasTextures[0].name = $"{assetName} Atlas";
            if (fontAsset.material != null)
                fontAsset.material.name = $"{assetName} Material";

            AssetDatabase.CreateAsset(fontAsset, assetPath);
            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            if (fontAsset.material != null)
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }
    }
}
