using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// "Tools → Neo UI → Setup → Create or Repair Effect Assets": deterministically bakes the
    /// optional art-directed assets for the Tier-2 <b>dissolve</b> shape effect under
    /// <c>Assets/Neo UI Framework/Resources/Effects</c> — a tiling value-noise mask
    /// (<c>NeoNoise.png</c>), a 1D gradient ramp (<c>NeoRamp.png</c>), the ONE shared
    /// <c>NeoDissolve.mat</c> (shader <c>Neo/UI/ShapeDissolve</c>, noise bound to its sampler), and the
    /// <see cref="ShapeEffectDefinition"/> asset <c>Dissolve.asset</c> (id <c>"dissolve"</c>) that a
    /// showcase / spec references via <c>"effect": { "id": "variant", "params": { "definition":
    /// "dissolve" } }</c>.
    ///
    /// <para>One-time and idempotent, exactly the same "run once" model as
    /// <see cref="FontAssetBootstrap"/> (Create or Repair Fonts): every asset is regenerated in place
    /// from a fixed seed — no per-run <see cref="Random"/> — so the committed PNGs/material/definition
    /// are reproducible. The dissolve material is the single shared material for the variant; it is
    /// never copied per instance (see <see cref="NeoShapeVariant"/>).</para>
    /// </summary>
    public static class NoiseAssetBootstrap
    {
        /// <summary> Folder the baked effect assets live in (under Resources so they ship with the package). </summary>
        public const string EffectsFolder = "Assets/Neo UI Framework/Resources/Effects";
        /// <summary> Tiling value-noise dissolve mask (grayscale in RGBA, sRGB off). </summary>
        public const string NoiseTexturePath = EffectsFolder + "/NeoNoise.png";
        /// <summary> 1D horizontal gradient ramp (clamped, sRGB off). </summary>
        public const string RampTexturePath = EffectsFolder + "/NeoRamp.png";
        /// <summary> The ONE shared material for the dissolve variant (shader Neo/UI/ShapeDissolve). </summary>
        public const string MaterialPath = EffectsFolder + "/NeoDissolve.mat";
        /// <summary> The dissolve <see cref="ShapeEffectDefinition"/> asset (id "dissolve"). </summary>
        public const string DefinitionPath = EffectsFolder + "/Dissolve.asset";

        /// <summary> Stable, agent-addressable id of the dissolve effect definition (never a GUID). </summary>
        public const string DissolveEffectId = "dissolve";

        /// <summary> Display name shown in editor dropdowns. </summary>
        public const string DissolveDisplayName = "Dissolve";

        /// <summary> Shader the dissolve material uses. </summary>
        public const string DissolveShaderName = "Neo/UI/ShapeDissolve";

        /// <summary> Shader sampler the baked noise texture is bound to (added in NeoShapeDissolve.shader). </summary>
        public const string NoiseSamplerProperty = "_NoiseTex";

        // ----------------------------------------------------------------- holo foil (Tier-2)

        /// <summary> The ONE shared material for the holo-foil variant (shader Neo/UI/ShapeHoloFoil). </summary>
        public const string HoloFoilMaterialPath = EffectsFolder + "/NeoHoloFoil.mat";
        /// <summary> The holo-foil <see cref="ShapeEffectDefinition"/> asset (id "holoFoil"). </summary>
        public const string HoloFoilDefinitionPath = EffectsFolder + "/HoloFoil.asset";
        /// <summary> Stable, agent-addressable id of the holo-foil effect definition (never a GUID). </summary>
        public const string HoloFoilEffectId = "holoFoil";
        /// <summary> Display name shown in editor dropdowns. </summary>
        public const string HoloFoilDisplayName = "Holo Foil";
        /// <summary> Shader the holo-foil material uses (procedural — no texture). </summary>
        public const string HoloFoilShaderName = "Neo/UI/ShapeHoloFoil";

        // ----------------------------------------------------------------- glitch (Tier-2)

        /// <summary> The ONE shared material for the glitch variant (shader Neo/UI/ShapeGlitch). </summary>
        public const string GlitchMaterialPath = EffectsFolder + "/NeoGlitch.mat";
        /// <summary> The glitch <see cref="ShapeEffectDefinition"/> asset (id "glitch"). </summary>
        public const string GlitchDefinitionPath = EffectsFolder + "/Glitch.asset";
        /// <summary> Stable, agent-addressable id of the glitch effect definition (never a GUID). </summary>
        public const string GlitchEffectId = "glitch";
        /// <summary> Display name shown in editor dropdowns. </summary>
        public const string GlitchDisplayName = "Glitch";
        /// <summary> Shader the glitch material uses (procedural — no texture). </summary>
        public const string GlitchShaderName = "Neo/UI/ShapeGlitch";

        // Fixed lattice resolutions (powers of two so the value noise tiles seamlessly).
        private const int NoiseSize = 256;     // 256² mask
        private const int NoiseOctaves = 4;    // a few octaves of value noise
        private const int NoiseBaseCells = 8;  // base lattice cells across the texture
        private const int RampWidth = 256;
        private const int RampHeight = 4;

        [MenuItem("Tools/Neo UI/Setup/Create or Repair Effect Assets", priority = 104)]
        public static void CreateOrRepairMenu()
        {
            Material material = CreateOrRepair();
            Debug.Log(
                "[Neo.UI] Effect assets (run once, same model as Create or Repair Fonts): " +
                $"noise={NoiseTexturePath}, ramp={RampTexturePath}, material={(material != null)}, " +
                $"definition='{DissolveEffectId}' → {DefinitionPath}; " +
                $"holoFoil='{HoloFoilEffectId}' → {HoloFoilDefinitionPath}; " +
                $"glitch='{GlitchEffectId}' → {GlitchDefinitionPath}");
        }

        /// <summary>
        /// Bakes (or repairs in place) every dissolve effect asset and returns the shared material.
        /// Idempotent: existing assets are overwritten, never duplicated.
        /// </summary>
        public static Material CreateOrRepair()
        {
            EnsureFolder(EffectsFolder);

            Texture2D noise = BakeNoiseTexture();
            BakeRampTexture();
            Material material = EnsureMaterial(noise);
            EnsureDefinition(material);

            // Tier-2 library: additional self-animating shader variants (procedural — no texture).
            EnsureHoloFoilDefinition(EnsureHoloFoilMaterial());
            EnsureGlitchDefinition(EnsureGlitchMaterial());

            AssetDatabase.SaveAssets();
            return material;
        }

        // ----------------------------------------------------------------- noise texture

        /// <summary>
        /// Bakes the tiling value-noise mask: a hash lattice with smoothstep-interpolated value noise
        /// summed over <see cref="NoiseOctaves"/> octaves, written as grayscale RGBA, imported as a
        /// linear (non-sRGB) Repeat/Bilinear data texture with mipmaps.
        /// </summary>
        public static Texture2D BakeNoiseTexture()
        {
            var pixels = new Color32[NoiseSize * NoiseSize];
            for (int y = 0; y < NoiseSize; y++)
            {
                for (int x = 0; x < NoiseSize; x++)
                {
                    float u = (float)x / NoiseSize;
                    float v = (float)y / NoiseSize;

                    float sum = 0f;
                    float amplitude = 1f;
                    float totalAmplitude = 0f;
                    int cells = NoiseBaseCells;
                    for (int o = 0; o < NoiseOctaves; o++)
                    {
                        sum += amplitude * TilingValueNoise(u, v, cells, o);
                        totalAmplitude += amplitude;
                        amplitude *= 0.5f;
                        cells *= 2;
                    }
                    float n = Mathf.Clamp01(sum / totalAmplitude);

                    byte g = (byte)Mathf.RoundToInt(n * 255f);
                    pixels[y * NoiseSize + x] = new Color32(g, g, g, 255);
                }
            }

            WritePng(NoiseTexturePath, NoiseSize, NoiseSize, pixels);
            ConfigureTextureImporter(NoiseTexturePath, TextureWrapMode.Repeat, mipmaps: true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexturePath);
        }

        /// <summary>
        /// One octave of seamlessly tiling value noise: bilinear-smoothstep interpolation over a
        /// <paramref name="cells"/>×<paramref name="cells"/> hash lattice, with corner indices taken
        /// modulo <paramref name="cells"/> so opposite edges share lattice points (tiles cleanly).
        /// </summary>
        private static float TilingValueNoise(float u, float v, int cells, int octaveSeed)
        {
            float fx = u * cells;
            float fy = v * cells;
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            float tx = Smoothstep(fx - x0);
            float ty = Smoothstep(fy - y0);

            float c00 = LatticeValue(x0, y0, cells, octaveSeed);
            float c10 = LatticeValue(x0 + 1, y0, cells, octaveSeed);
            float c01 = LatticeValue(x0, y0 + 1, cells, octaveSeed);
            float c11 = LatticeValue(x0 + 1, y0 + 1, cells, octaveSeed);

            float top = Mathf.Lerp(c00, c10, tx);
            float bottom = Mathf.Lerp(c01, c11, tx);
            return Mathf.Lerp(top, bottom, ty);
        }

        /// <summary> Fixed-seed deterministic hash → [0,1] for a wrapped lattice cell. </summary>
        private static float LatticeValue(int x, int y, int cells, int octaveSeed)
        {
            // Wrap so the right/top edge reuses the left/bottom lattice points (seamless tiling).
            uint wx = (uint)(((x % cells) + cells) % cells);
            uint wy = (uint)(((y % cells) + cells) % cells);
            return Hash(wx, wy, (uint)octaveSeed);
        }

        /// <summary> Deterministic integer hash (PCG-style mix) producing a value in [0,1). Fixed seed. </summary>
        private static float Hash(uint x, uint y, uint seed)
        {
            unchecked
            {
                uint h = 0x9E3779B9u ^ seed * 0x85EBCA6Bu;
                h ^= x * 0xC2B2AE35u;
                h = (h << 13) | (h >> 19);
                h ^= y * 0x27D4EB2Fu;
                h = (h << 17) | (h >> 15);
                h *= 0x165667B1u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000u;
            }
        }

        private static float Smoothstep(float t) => t * t * (3f - 2f * t);

        // ----------------------------------------------------------------- ramp texture

        /// <summary>
        /// Bakes a 1D horizontal black→white gradient ramp (clamped, linear data texture). Useful as a
        /// remap LUT for the dissolve edge; baked alongside the noise so the effect folder is complete.
        /// </summary>
        public static Texture2D BakeRampTexture()
        {
            var pixels = new Color32[RampWidth * RampHeight];
            for (int x = 0; x < RampWidth; x++)
            {
                float t = RampWidth == 1 ? 0f : (float)x / (RampWidth - 1);
                byte g = (byte)Mathf.RoundToInt(t * 255f);
                var c = new Color32(g, g, g, 255);
                for (int y = 0; y < RampHeight; y++)
                    pixels[y * RampWidth + x] = c;
            }

            WritePng(RampTexturePath, RampWidth, RampHeight, pixels);
            ConfigureTextureImporter(RampTexturePath, TextureWrapMode.Clamp, mipmaps: false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(RampTexturePath);
        }

        // ----------------------------------------------------------------- material + definition

        /// <summary>
        /// Creates or repairs the ONE shared dissolve material (shader <see cref="DissolveShaderName"/>)
        /// and binds the baked noise to its <see cref="NoiseSamplerProperty"/>. Never per-instance.
        /// </summary>
        public static Material EnsureMaterial(Texture noise)
        {
            Shader shader = Shader.Find(DissolveShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Neo.UI] Shader '{DissolveShaderName}' not found — cannot create '{MaterialPath}'. " +
                                 "The dissolve effect will fall back to the default NeoShape material.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "NeoDissolve" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (noise != null && material.HasProperty(NoiseSamplerProperty))
                material.SetTexture(NoiseSamplerProperty, noise);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Creates or repairs the dissolve <see cref="ShapeEffectDefinition"/> asset (id
        /// <see cref="DissolveEffectId"/>, <c>batchSafe=false</c>, pointing at the shared material).
        /// This is what <c>ShapeEffectRegistry</c>'s <c>variant</c> descriptor resolves by id.
        /// </summary>
        public static ShapeEffectDefinition EnsureDefinition(Material material)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShapeEffectDefinition>(DefinitionPath);
            bool created = definition == null;
            if (created)
            {
                definition = ScriptableObject.CreateInstance<ShapeEffectDefinition>();
                definition.name = "Dissolve";
            }

            // Serialized fields are private; set them through SerializedObject so the bake matches what
            // a hand-authored definition asset would look like (flat, force-text).
            var so = new SerializedObject(definition);
            so.FindProperty("id").stringValue = DissolveEffectId;
            so.FindProperty("displayName").stringValue = DissolveDisplayName;
            so.FindProperty("sharedMaterial").objectReferenceValue = material;
            so.FindProperty("batchSafe").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (created)
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // ----------------------------------------------------------------- holo foil (Tier-2)

        /// <summary>
        /// Creates or repairs the ONE shared holo-foil material (shader <see cref="HoloFoilShaderName"/>).
        /// Procedural — no texture is bound; the iridescence is generated in the fragment shader and
        /// drifts from _Time. Never per-instance (see <see cref="NeoShapeVariant"/>).
        /// </summary>
        public static Material EnsureHoloFoilMaterial() =>
            EnsureVariantMaterial(HoloFoilShaderName, HoloFoilMaterialPath, "NeoHoloFoil");

        /// <summary>
        /// Creates or repairs the holo-foil <see cref="ShapeEffectDefinition"/> asset (id
        /// <see cref="HoloFoilEffectId"/>, <c>batchSafe=false</c>) with float-param defaults matching the
        /// shader properties. Resolved by the <c>variant</c> descriptor by id.
        /// </summary>
        public static ShapeEffectDefinition EnsureHoloFoilDefinition(Material material) =>
            EnsureVariantDefinition(material, HoloFoilDefinitionPath, "HoloFoil", HoloFoilEffectId, HoloFoilDisplayName,
                new[] { ("_FoilIntensity", 0.6f), ("_FoilScale", 3f), ("_FoilSpeed", 0.4f), ("_Glint", 0.5f), ("_HueOffset", 0f) });

        // ----------------------------------------------------------------- glitch (Tier-2)

        /// <summary>
        /// Creates or repairs the ONE shared glitch material (shader <see cref="GlitchShaderName"/>).
        /// Procedural — no texture binding; the glitch animates entirely from _Time. Never per-instance.
        /// </summary>
        public static Material EnsureGlitchMaterial() =>
            EnsureVariantMaterial(GlitchShaderName, GlitchMaterialPath, "NeoGlitch");

        /// <summary>
        /// Creates or repairs the glitch <see cref="ShapeEffectDefinition"/> asset (id
        /// <see cref="GlitchEffectId"/>, <c>batchSafe=false</c>) with float-param defaults matching the
        /// shader's properties, pointing at the shared glitch material.
        /// </summary>
        public static ShapeEffectDefinition EnsureGlitchDefinition(Material material) =>
            EnsureVariantDefinition(material, GlitchDefinitionPath, "Glitch", GlitchEffectId, GlitchDisplayName,
                new[] { ("_GlitchAmount", 0.3f), ("_RgbSplit", 0.02f), ("_BlockCount", 12f), ("_GlitchSpeed", 8f), ("_Scanline", 0.2f) });

        // ----------------------------------------------------------------- Tier-2 variant plumbing

        /// <summary>
        /// Shared "create or repair a procedural Tier-2 variant material" helper: finds the shader,
        /// creates the .mat if missing or repoints its shader if changed. Returns null (with a warning)
        /// when the shader is absent so the variant falls back to the default NeoShape material.
        /// </summary>
        private static Material EnsureVariantMaterial(string shaderName, string materialPath, string materialName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Neo.UI] Shader '{shaderName}' not found — cannot create '{materialPath}'. " +
                                 "The effect will fall back to the default NeoShape material.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Shared "create or repair a <see cref="ShapeEffectDefinition"/>" helper for Tier-2 variants:
        /// writes id/displayName/sharedMaterial/batchSafe=false plus the given float-param defaults via
        /// SerializedObject (flat, force-text), exactly like the dissolve definition.
        /// </summary>
        private static ShapeEffectDefinition EnsureVariantDefinition(
            Material material, string definitionPath, string assetName, string id, string displayName,
            (string name, float value)[] floatParams)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShapeEffectDefinition>(definitionPath);
            bool created = definition == null;
            if (created)
            {
                definition = ScriptableObject.CreateInstance<ShapeEffectDefinition>();
                definition.name = assetName;
            }

            var so = new SerializedObject(definition);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("sharedMaterial").objectReferenceValue = material;
            so.FindProperty("batchSafe").boolValue = false;

            SerializedProperty floats = so.FindProperty("floatParams");
            floats.ClearArray();
            for (int i = 0; i < floatParams.Length; i++)
            {
                floats.InsertArrayElementAtIndex(i);
                SerializedProperty el = floats.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("name").stringValue = floatParams[i].name;
                el.FindPropertyRelative("value").floatValue = floatParams[i].value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            if (created)
                AssetDatabase.CreateAsset(definition, definitionPath);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // ----------------------------------------------------------------- plumbing

        private static void WritePng(string assetPath, int width, int height, Color32[] pixels)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string systemPath = Path.GetFullPath(assetPath);
            File.WriteAllBytes(systemPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureTextureImporter(string assetPath, TextureWrapMode wrapMode, bool mipmaps)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[Neo.UI] No TextureImporter at '{assetPath}' — importer settings not applied.");
                return;
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;       // data/mask texture, not color
            importer.wrapMode = wrapMode;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = mipmaps;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
