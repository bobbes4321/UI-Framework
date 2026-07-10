using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The PNG → named-icon pipeline: wraps a project sprite in its own single-sprite
    /// <see cref="TMP_SpriteAsset"/> and registers it as an <see cref="IconMapOverlay"/> sprite
    /// entry (creating and assigning the overlay asset itself when the project has none), so
    /// "drop a texture, name it, use it everywhere" needs no font authoring and no codepoints.
    /// Used by the Design System Icons tab; safe to re-run (create-or-update per icon name).
    /// </summary>
    public static class NeoIconSpriteAssets
    {
        /// <summary> Project-owned home for icon sprite assets + the overlay (outside the package
        /// so a package update never touches it). </summary>
        public const string DefaultFolder = "Assets/Neo UI Icons";

        /// <summary>
        /// Ensures the texture imports as a Single sprite and returns its Sprite sub-asset
        /// (a texture imported without sprite mode has no Sprite to reference — same trap the
        /// bridge's importSprites action guards).
        /// </summary>
        public static Sprite EnsureSprite(Texture2D texture)
        {
            if (texture == null) return null;
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return null;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                (importer.textureType != TextureImporterType.Sprite ||
                 importer.spriteImportMode != SpriteImportMode.Single))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Creates (or rebuilds) a single-sprite TMP sprite asset at <paramref name="assetPath"/>
        /// whose one sprite character is named <paramref name="iconName"/>. FaceInfo is sized so
        /// the sprite renders at roughly 1em regardless of the text's font size.
        /// </summary>
        public static TMP_SpriteAsset CreateOrUpdate(Sprite sprite, string iconName, string assetPath)
        {
            if (sprite == null || string.IsNullOrEmpty(iconName)) return null;
            var asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.spriteSheet = sprite.texture;
            if (asset.material == null)
            {
                var material = new Material(Shader.Find("TextMeshPro/Sprite")) { name = "Sprite Material" };
                material.mainTexture = sprite.texture;
                asset.material = material;
                AssetDatabase.AddObjectToAsset(material, asset);
            }
            else
            {
                asset.material.mainTexture = sprite.texture;
            }

            Rect rect = sprite.rect;
            var glyph = new TMP_SpriteGlyph
            {
                index = 0,
                glyphRect = new GlyphRect((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height),
                // bearingY ≈ 0.9 * height sits the art on the baseline like the Lucide glyphs do
                metrics = new GlyphMetrics(rect.width, rect.height, 0f, rect.height * 0.9f, rect.width),
                scale = 1f,
                sprite = sprite
            };
            asset.spriteGlyphTable.Clear();
            asset.spriteGlyphTable.Add(glyph);
            asset.spriteCharacterTable.Clear();
            asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, glyph) { name = iconName, scale = 1f });
            asset.UpdateLookupTables();

            // version 1.1.0 + faceInfo drive TMP's modern sprite scaling (currentFontSize / pointSize);
            // both serialize privately, so write them through a SerializedObject
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            SerializedProperty face = serialized.FindProperty("m_FaceInfo");
            face.FindPropertyRelative("m_PointSize").floatValue = rect.height;
            face.FindPropertyRelative("m_Scale").floatValue = 1f;
            face.FindPropertyRelative("m_AscentLine").floatValue = rect.height * 0.9f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// The one-call "add my PNG to the icon list": ensure sprite import, wrap in a sprite
        /// asset under <paramref name="folder"/> (default <see cref="DefaultFolder"/>), upsert the
        /// overlay sprite entry — creating/assigning the overlay asset on first use — and save.
        /// Returns null (with a logged error) when the texture can't become a sprite.
        /// </summary>
        public static IconMapOverlay.SpriteEntry AddProjectIcon(
            Texture2D texture, string iconName, bool tint, string folder = null)
        {
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null)
            {
                Debug.LogError("[Neo.UI] No NeoUISettings — run Tools → Neo UI → Setup → Create or Repair Settings first.");
                return null;
            }
            Sprite sprite = EnsureSprite(texture);
            if (sprite == null)
            {
                Debug.LogError($"[Neo.UI] '{(texture != null ? texture.name : "<null>")}' is not an " +
                               "importable texture asset — cannot create an icon sprite from it.");
                return null;
            }
            if (string.IsNullOrEmpty(folder)) folder = DefaultFolder;
            DesignSystemGUI.EnsureFolder(folder);

            string safeName = string.Join("-", iconName.Split(System.IO.Path.GetInvalidFileNameChars()));
            TMP_SpriteAsset spriteAsset = CreateOrUpdate(sprite, iconName, $"{folder}/{safeName} Icon.asset");
            if (spriteAsset == null) return null;

            IconMapOverlay overlay = EnsureOverlay(settings, folder);
            if (overlay == null) return null;
            IconMapOverlay.SpriteEntry entry = overlay.sprites.Find(
                e => e != null && string.Equals(e.name, iconName, System.StringComparison.Ordinal));
            if (entry == null)
            {
                entry = new IconMapOverlay.SpriteEntry { name = iconName };
                overlay.sprites.Add(entry);
            }
            entry.spriteAsset = spriteAsset;
            entry.spriteName = iconName;
            entry.tint = tint;
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssets();
            return entry;
        }

        /// <summary> The project's overlay asset, created under <paramref name="folder"/> (default
        /// <see cref="DefaultFolder"/>) and assigned to the settings on first use. </summary>
        public static IconMapOverlay EnsureOverlay(NeoUISettings settings, string folder = null)
        {
            if (settings == null)
            {
                Debug.LogError("[Neo.UI] No NeoUISettings — run Tools → Neo UI → Setup → Create or Repair Settings first.");
                return null;
            }
            if (settings.iconOverlay != null) return settings.iconOverlay;
            if (string.IsNullOrEmpty(folder)) folder = DefaultFolder;
            DesignSystemGUI.EnsureFolder(folder);
            var overlay = ScriptableObject.CreateInstance<IconMapOverlay>();
            AssetDatabase.CreateAsset(overlay, $"{folder}/IconMapOverlay.asset");
            settings.iconOverlay = overlay;
            EditorUtility.SetDirty(settings);
            return overlay;
        }
    }
}
