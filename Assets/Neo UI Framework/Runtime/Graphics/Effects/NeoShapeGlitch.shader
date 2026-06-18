// ===========================================================================================
// Neo UI — Tier-2 shape effect: "Neo/UI/ShapeGlitch" (cyberpunk RGB-split + block displacement).
//
// Like NeoShapeDissolve this is a SELF-CONTAINED variant of "Neo/UI/Shape": it reads the SAME
// per-vertex channel layout that NeoShape.OnPopulateMesh packs (mirror it exactly), reconstructs
// the base SDF silhouette + anti-aliased coverage IDENTICALLY (so masking, stencil, sort and the
// premultiplied composite all match the rest of the UI), then layers a self-animating glitch look
// on top of the solid vertex-color fill — no C# timeline needed, it drives entirely off _Time.y.
//
// The glitch is three stacked tricks:
//   1. RGB channel split — because the fill is a solid SDF (no texture), the chromatic fringe is
//      produced by evaluating the silhouette COVERAGE at horizontally-shifted positions per channel
//      (R sampled at p+offset, B at p-offset, G centered). The shifted coverages tint the vertex
//      color so the edges fringe red/blue like a CRT/datamosh artifact.
//   2. Horizontal block displacement / tear — the vertical axis is quantized into scrolling bands;
//      a hash per band, thresholded so MOST bands are still, shoves the active bands sideways. The
//      intermittency (only a few bands tear at once) is what reads as "glitch" rather than "wobble".
//   3. Scanline darkening + an occasional full-width horizontal jump (also _Time.y / hash driven).
//
// A shader error here is low-blast-radius — it never breaks C# compilation, and the base
// "Neo/UI/Shape" path is unaffected.
//
// VERTEX CHANNEL LAYOUT (must match NeoShape.shader / NeoShape.OnPopulateMesh):
//   uv0 (TEXCOORD0): xy = local position (px, rect-center origin)   zw = rect half-size (px)
//   uv1 (TEXCOORD1): corner radii px (x=TR y=BR z=TL w=BL); glyph/ring repurpose .x
//   uv2 (TEXCOORD2): x = shapeMode + 16*fillMode + 256*texFit   y = border px   z = softness px
//                    w = gradient angle rad
//   uv3 (TEXCOORD3): border color
//   tangent        : gradient color B (color A is the vertex color)
//
// Tier-2 params (defaults supplied by ShapeEffectDefinition.ApplyDefaults onto the SHARED material):
//   _GlitchAmount  0..1  strength of the horizontal block displacement / tear
//   _RgbSplit      0..0.1 chromatic R/B horizontal offset, in normalized local space
//   _BlockCount    number of horizontal bands the height is quantized into
//   _GlitchSpeed   temporal scroll/burst rate of the bands (multiplies _Time.y)
//   _Scanline      0..1  strength of the scanline darkening overlay
// ===========================================================================================
Shader "Neo/UI/ShapeGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _GlitchAmount ("Glitch Amount", Range(0,1)) = 0.3
        _RgbSplit ("RGB Split", Range(0,0.1)) = 0.02
        _BlockCount ("Block Count", Float) = 12
        _GlitchSpeed ("Glitch Speed", Float) = 8
        _Scanline ("Scanline Strength", Range(0,1)) = 0.2

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float4 texcoord : TEXCOORD0;  // xy local px, zw half-size px
                float4 shapeData: TEXCOORD1;  // corner radii px (x=TR y=BR z=TL w=BL)
                float4 params   : TEXCOORD2;  // x packedMode  y border px  z softness px  w grad angle
                float4 borderCol: TEXCOORD3;
                float4 tangent  : TANGENT;    // gradient color B
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 shapeData: TEXCOORD1;
                float4 params   : TEXCOORD2;
                float4 worldPosition : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;

            float _GlitchAmount;
            float _RgbSplit;
            float _BlockCount;
            float _GlitchSpeed;
            float _Scanline;

            // signed distance to a rect of half-size b with per-corner radii r (x=TR y=BR z=TL w=BL)
            float sdRoundedRect(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x = (p.y > 0.0) ? r.x : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            // signed distance to an axis-aligned ellipse of half-size b (negative inside).
            float sdEllipse(float2 p, float2 b)
            {
                b = max(b, 1e-4);
                float k1 = length(p / b);
                float k2 = length(p / (b * b));
                return (k2 <= 1e-6) ? -min(b.x, b.y) : k1 * (k1 - 1.0) / k2;
            }

            // cheap hash noise (same family as NeoShapeDissolve's hash21) — drives the glitch bands.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.color = v.color * _Color;
                OUT.texcoord = v.texcoord;
                OUT.shapeData = v.shapeData;
                OUT.params = v.params;
                return OUT;
            }

            // Anti-aliased silhouette coverage at a local (px) position, for the active shape mode.
            // Reconstructed IDENTICALLY to NeoShape's solid path so masking/compositing match.
            float shapeCoverage(float2 p, float2 halfSize, float4 shapeData, float shapeMode, float softnessPx)
            {
                float d = (shapeMode == 1.0)
                    ? sdEllipse(p, halfSize)
                    : sdRoundedRect(p, halfSize, shapeData);
                float aa = fwidth(d);
                float edgeW = max(aa, softnessPx * 2.0);
                return 1.0 - smoothstep(-edgeW * 0.5, edgeW * 0.5, d);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.texcoord.xy;
                float2 halfSize = max(IN.texcoord.zw, 0.01);
                float softnessPx = IN.params.z;

                // ---- unpack the packed mode exactly like the base shader ----
                float packedMode = IN.params.x;
                float texFit = floor(packedMode / 256.0 + 0.001);
                packedMode -= texFit * 256.0;
                float fillMode = floor(packedMode / 16.0 + 0.001);
                float shapeMode = packedMode - fillMode * 16.0;

                // ---- normalized local space (-0.5..0.5), used for the procedural glitch ----
                float2 nl = p / (halfSize * 2.0);   // 0 at center, ±0.5 at edges

                // ---- (2) horizontal block displacement / tear -------------------------------------
                // Quantize Y into scrolling bands; hash each band; only the bands whose hash crosses a
                // threshold tear (intermittency = the "glitch" read). Displace those bands in X.
                float t = _Time.y * _GlitchSpeed;
                float band = floor(nl.y * _BlockCount + t);
                float bandHash = hash21(float2(band, floor(t)));
                // threshold: keep ~25% of bands tearing — most stay still.
                float tear = step(0.75, bandHash);
                float dir = (hash21(float2(band, floor(t) + 7.0)) - 0.5) * 2.0; // signed shove
                float blockShift = tear * dir * _GlitchAmount * 0.5;            // normalized-space px

                // ---- (3) occasional full-width horizontal jump ------------------------------------
                float jumpHash = hash21(float2(floor(t * 0.5), 13.0));
                float jump = step(0.92, jumpHash) * (hash21(float2(floor(t * 0.5), 31.0)) - 0.5)
                             * _GlitchAmount * 0.4;

                float xShift = blockShift + jump; // total horizontal displacement, normalized space

                // base sampling position (px) after the block tear.
                float2 pBase = float2(p.x - xShift * (halfSize.x * 2.0), p.y);

                // ---- (1) RGB channel split (edge fringe) ------------------------------------------
                // Evaluate the silhouette coverage at horizontally-shifted positions per channel — the
                // chromatic ghost at the card edges. Boost the split inside torn bands so the fringe
                // visibly widens where the image tears.
                float splitPx = _RgbSplit * (halfSize.x * 2.0) * (1.0 + tear * 3.0);
                float covR = shapeCoverage(pBase + float2( splitPx, 0.0), halfSize, IN.shapeData, shapeMode, softnessPx);
                float covG = shapeCoverage(pBase,                          halfSize, IN.shapeData, shapeMode, softnessPx);
                float covB = shapeCoverage(pBase + float2(-splitPx, 0.0), halfSize, IN.shapeData, shapeMode, softnessPx);

                // sprite tint (matches dissolve's _MainTex path so a textured fill still works).
                half4 src = IN.color * tex2D(_MainTex, saturate(p / (halfSize * 2.0) + 0.5));
                float coverage = max(covR, max(covG, covB));

                // ---- INTERNAL glitch (the part that reads on a SOLID fill) -------------------------
                // A flat fill has no detail for the silhouette tricks to show, so synthesize it: torn
                // bands flip to a vivid channel-rotated version of the base, speckled with digital
                // static, with their R/B pulled apart — bright datamosh bars across the body.
                half3 baseRgb = src.rgb;
                float staticNoise = hash21(float2(floor(p.x * 0.6) + band * 3.7, floor(t * 3.0)));
                half3 glitchCol = baseRgb.gbr;                                   // rotate channels → vivid shift
                glitchCol = lerp(glitchCol, half3(staticNoise, staticNoise, staticNoise), 0.35); // static
                // pull R left / B right within torn bands for a chromatic smear that reads on the body
                float chroma = tear * _RgbSplit * 14.0;
                glitchCol.r *= 1.0 + chroma;
                glitchCol.b *= 1.0 + chroma * 0.6;
                half3 rgb = lerp(baseRgb, glitchCol, tear * 0.85);

                // a few thin bright "data" lines flicker through, independent of the band tear.
                float dataLine = step(0.985, hash21(float2(band * 1.3, floor(t * 6.0))));
                rgb += baseRgb * dataLine * 0.6;

                // ---- occasional full-card brightness flash ----------------------------------------
                float flash = step(0.96, hash21(float2(floor(t * 0.7), 91.0))) * 0.25;
                rgb += baseRgb * flash;

                // keep the edge chromatic fringe by weighting the body by per-channel coverage.
                rgb = half3(rgb.r * covR, rgb.g * covG, rgb.b * covB) / max(coverage, 1e-4) * coverage;

                half4 col = half4(rgb, src.a);

                // ---- (3) scanline darkening overlay ----------------------------------------------
                float2 uv = p / (halfSize * 2.0) + 0.5;
                float scan = sin((uv.y + _Time.y * 0.5) * 220.0 * UNITY_PI);
                col.rgb *= 1.0 - _Scanline * (0.5 + 0.5 * scan);

                col.a *= coverage;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                col.rgb *= col.a;
                return col;
            }
            ENDCG
        }
    }
}
