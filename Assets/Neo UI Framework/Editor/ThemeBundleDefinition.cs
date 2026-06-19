using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The ScriptableObject form of a <see cref="ThemeBundles.Bundle"/> — the no-C# seam for authoring a
    /// complete theme personality (token palette + radius/gradient/shadow shape feel + motion). A project
    /// (or designer) drops one of these assets and <see cref="ThemeBundleRegistry"/> folds it in, so it
    /// appears in the Apply-Theme-Bundle menu / inspector and resolves through the spec
    /// <c>"theme":{"bundle":"…"}</c> path — no need to construct a <c>Bundle</c> in code. Editor-only,
    /// mirroring <see cref="ShowcaseDefinition"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Theme Bundle Definition", fileName = "ThemeBundleDefinition")]
    public class ThemeBundleDefinition : ScriptableObject
    {
        [System.Serializable]
        public class TokenColor
        {
            public string token;
            public Color color = Color.white;
        }

        [System.Serializable]
        public class Variant
        {
            [Tooltip("Variant name (e.g. Dark / Light). The FIRST variant becomes active when applied.")]
            public string name = "Dark";
            public List<TokenColor> tokens = new List<TokenColor>();
        }

        [Tooltip("Bundle name referenced by the menu / spec \"theme\":{\"bundle\":\"…\"}. Falls back to the asset name.")]
        public string bundleName;

        [TextArea] public string description;

        [Tooltip("Token palettes per variant; the first variant becomes active when the bundle is applied.")]
        public List<Variant> variants = new List<Variant>();

        [Header("Shape personality (px)")]
        public float cardRadius = 16f;
        public float panelRadius = 16f;
        public float controlRadius = 10f;
        [Tooltip("Card surface gradient target token (glow look); blank = solid.")]
        public string cardGradientToToken;
        [Tooltip("Shadow softness px (higher = glow).")]
        public float shadowSoftness = 18f;

        [Header("Motion personality")]
        public float motionDuration = 0.25f;
        public string motionEase = "OutCubic";
        [Tooltip("Tracking on Display/Title text styles (arcade lettering vs book type).")]
        public float headlineSpacing = -0.5f;

        /// <summary> Projects this definition into the plain <see cref="ThemeBundles.Bundle"/> the registry stores. </summary>
        public ThemeBundles.Bundle ToBundle()
        {
            var palettes = new List<(string variant, Dictionary<string, Color> tokens)>();
            foreach (Variant v in variants)
            {
                if (v == null || string.IsNullOrEmpty(v.name)) continue;
                var dict = new Dictionary<string, Color>();
                foreach (TokenColor tc in v.tokens)
                    if (tc != null && !string.IsNullOrEmpty(tc.token)) dict[tc.token] = tc.color;
                palettes.Add((v.name, dict));
            }
            return new ThemeBundles.Bundle
            {
                name = string.IsNullOrWhiteSpace(bundleName) ? name : bundleName.Trim(),
                description = description,
                palettes = palettes,
                cardRadius = cardRadius,
                panelRadius = panelRadius,
                controlRadius = controlRadius,
                cardGradientToToken = string.IsNullOrEmpty(cardGradientToToken) ? null : cardGradientToToken,
                shadowSoftness = shadowSoftness,
                motionDuration = motionDuration,
                motionEase = motionEase,
                headlineSpacing = headlineSpacing,
            };
        }
    }
}
