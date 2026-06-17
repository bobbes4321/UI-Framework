// ===========================================================================================
// Neo UI — Tier-2 PROOF shader for the ShapeEffectDefinition / NeoShapeVariant seam.
//
// This is a MINIMAL variant of "Neo/UI/Shape": it reads the SAME per-vertex channel layout that
// NeoShape.OnPopulateMesh packs (mirror it exactly), renders the base rounded-rect SDF, then adds
// a cheap scanline + a UV-noise dissolve clip to prove that a project can ship its own fragment
// effect through the SO seam without forking the package. Conventionally lives under Resources/ so
// Shader.Find / Resources.Load can reach it; kept beside the effects here for the single-folder
// deliverable. A shader error here is low-blast-radius — it never breaks C# compilation.
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
//   _DissolveAmount  0..1 erosion (1 = fully dissolved)
//   _EdgeColor       glow color at the dissolve edge
//   _EdgeWidth       width of the dissolve glow band (uv units)
//   _ScanlineStrength / _ScanlineCount  optional scanline overlay
//   _NoiseTex        OPTIONAL art-directed dissolve mask (NoiseAssetBootstrap bakes Resources/Effects/
//                    NeoNoise.png and binds it on the shared NeoDissolve.mat). Defaults to "white";
//                    when left unbound the fragment FALLS BACK to the in-shader hash21 procedural
//                    noise so the shader still works with no texture (current behavior preserved).
//   _NoiseScale      tiling of _NoiseTex across the shape's uv (only used when a noise texture is bound)
// ===========================================================================================
Shader "Neo/UI/ShapeDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _DissolveAmount ("Dissolve Amount", Range(0,1)) = 0
        _EdgeColor ("Dissolve Edge Color", Color) = (1, 0.6, 0.1, 1)
        _EdgeWidth ("Dissolve Edge Width", Range(0,0.3)) = 0.08
        _ScanlineStrength ("Scanline Strength", Range(0,1)) = 0.15
        _ScanlineCount ("Scanline Count", Float) = 80

        // Optional baked dissolve mask. Default "white" ⇒ unbound ⇒ fall back to procedural hash21.
        _NoiseTex ("Dissolve Noise (optional)", 2D) = "white" {}
        _NoiseScale ("Dissolve Noise Scale", Float) = 4

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

            float _DissolveAmount;
            fixed4 _EdgeColor;
            float _EdgeWidth;
            float _ScanlineStrength;
            float _ScanlineCount;

            sampler2D _NoiseTex;
            float4 _NoiseTex_TexelSize;   // .zw = texture dimensions in texels (0,0 when no texture bound)
            float _NoiseScale;

            // signed distance to a rect of half-size b with per-corner radii r (x=TR y=BR z=TL w=BL)
            float sdRoundedRect(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x = (p.y > 0.0) ? r.x : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            // cheap hash noise for the dissolve mask (no texture needed for the proof)
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

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.texcoord.xy;
                float2 halfSize = max(IN.texcoord.zw, 0.01);
                float softnessPx = IN.params.z;

                // base rounded-rect SDF (px, negative inside) — same as NeoShape's solid path
                float d = sdRoundedRect(p, halfSize, IN.shapeData);

                half4 col = IN.color * tex2D(_MainTex, saturate(p / (halfSize * 2.0) + 0.5));

                // ---- base edge coverage (anti-aliased, widened by softness) ----
                float aa = fwidth(d);
                float edgeW = max(aa, softnessPx * 2.0);
                float coverage = 1.0 - smoothstep(-edgeW * 0.5, edgeW * 0.5, d);

                // ---- normalized uv for the procedural effects ----
                float2 uv = p / (halfSize * 2.0) + 0.5;

                // ---- dissolve: clip where noise < amount, glow on the receding edge ----
                // Prefer the baked art-directed mask when one is bound; the built-in "white" default
                // is a 1×1 texture (TexelSize.z <= 1), in which case fall back to the procedural hash
                // so the shader keeps working with no texture assigned (current behavior preserved).
                float n;
                if (_NoiseTex_TexelSize.z > 1.5)
                    n = tex2D(_NoiseTex, uv * _NoiseScale).r;
                else
                    n = hash21(floor(uv * 64.0));
                float erosion = n - _DissolveAmount;          // < 0 ⇒ dissolved away
                float edgeBand = smoothstep(0.0, _EdgeWidth, erosion); // 0 at edge → 1 inside
                coverage *= step(0.0, erosion);
                col.rgb = lerp(_EdgeColor.rgb, col.rgb, edgeBand);

                // ---- scanline overlay ----
                float scan = sin(uv.y * _ScanlineCount * UNITY_PI);
                col.rgb *= 1.0 - _ScanlineStrength * (0.5 + 0.5 * scan);

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
