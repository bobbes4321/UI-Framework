// ===========================================================================================
// Neo UI — Tier-2 "holo foil" shader for the ShapeEffectDefinition / NeoShapeVariant seam.
//
// A holographic / iridescent trading-card foil: over the shape's base fill it layers a hue that
// drifts diagonally across the surface (rainbow bands) plus 1-2 sharpened "glint" bands for the
// metallic-foil pop. It SELF-ANIMATES from _Time.y (no C# timeline needed) — the existing Tier-2
// NeoMaterialFloatCycle driver can ALSO animate any of these floats via the variant descriptor's
// `animate` param if extra motion is wanted.
//
// It reads the SAME per-vertex channel layout that NeoShape.OnPopulateMesh packs (mirror it
// exactly), reconstructs the base rounded-rect/ellipse SDF + AA + stencil/blend/premultiply EXACTLY
// like NeoShapeDissolve, then only changes the COLOR layered on top — so masking, sort order and
// compositing match the rest of the UI. A shader error here is low-blast-radius — it never breaks
// C# compilation, and the base "Neo/UI/Shape" path is unaffected.
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
//   _FoilIntensity  0..1  blend amount of the iridescence over the base fill          (default 0.6)
//   _FoilScale            how many rainbow bands across the surface                    (default 3)
//   _FoilSpeed            drift speed (multiplied by _Time.y)                          (default 0.4)
//   _Glint          0..1  specular glint-band strength                                 (default 0.5)
//   _HueOffset      0..1  base hue rotation                                            (default 0)
// ===========================================================================================
Shader "Neo/UI/ShapeHoloFoil"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _FoilIntensity ("Foil Intensity", Range(0,1)) = 0.6
        _FoilScale ("Foil Scale (bands)", Float) = 3
        _FoilSpeed ("Foil Speed", Float) = 0.4
        _Glint ("Glint Strength", Range(0,1)) = 0.5
        _HueOffset ("Hue Offset", Range(0,1)) = 0

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

            float _FoilIntensity;
            float _FoilScale;
            float _FoilSpeed;
            float _Glint;
            float _HueOffset;

            // signed distance to a rect of half-size b with per-corner radii r (x=TR y=BR z=TL w=BL)
            float sdRoundedRect(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x = (p.y > 0.0) ? r.x : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            // signed distance to an ellipse of half-size b (scaled-circle approximation, as NeoShape)
            float sdEllipse(float2 p, float2 b)
            {
                b = max(b, 1e-4);
                float2 q = p / b;
                float k = length(q);
                // distance in px space: pull the normalized distance back through the smaller axis
                return (k - 1.0) * min(b.x, b.y);
            }

            // hue (0..1) → rgb. Standard HSV→RGB with full saturation/value.
            float3 hsv2rgb(float h)
            {
                float3 c = abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return saturate(c - 1.0);
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

                // ---- unpack the packed mode (shapeMode is all we need to pick the SDF) ----
                float packedMode = IN.params.x;
                float texFit    = floor(packedMode / 256.0 + 0.001);
                packedMode     -= texFit * 256.0;
                float fillMode  = floor(packedMode / 16.0 + 0.001);
                float shapeMode = packedMode - fillMode * 16.0;

                // ---- base silhouette SDF (px, negative inside) — same as NeoShape's solid path ----
                float d = (shapeMode == 1.0)
                    ? sdEllipse(p, halfSize)
                    : sdRoundedRect(p, halfSize, IN.shapeData);

                half4 col = IN.color * tex2D(_MainTex, saturate(p / (halfSize * 2.0) + 0.5));

                // ---- base edge coverage (anti-aliased, widened by softness) ----
                float aa = fwidth(d);
                float edgeW = max(aa, softnessPx * 2.0);
                float coverage = 1.0 - smoothstep(-edgeW * 0.5, edgeW * 0.5, d);

                // ---- normalized uv for the procedural effects (rect-center → 0..1) ----
                float2 uv = p / (halfSize * 2.0) + 0.5;

                float t = _Time.y * _FoilSpeed;

                // ---- iridescent sheen: hue varies along a DIAGONAL direction + drifts in time ----
                float diag = (uv.x + uv.y) * 0.5;            // 0..1 across the diagonal
                float hue = frac(diag * _FoilScale + t + _HueOffset);
                float3 foil = hsv2rgb(hue);

                // ---- 1-2 sharpened specular "glint" bands sliding across (metallic pop) ----
                float band = sin((diag * _FoilScale - t) * UNITY_TWO_PI);
                float glint = pow(saturate(band), 16.0);     // primary tight band
                band = sin((diag * _FoilScale * 0.5 + t * 1.7) * UNITY_TWO_PI);
                glint += 0.5 * pow(saturate(band), 24.0);    // secondary, finer/faster band
                glint = saturate(glint) * _Glint;

                // ---- layer the iridescence over the base fill, then add the glint highlight ----
                col.rgb = lerp(col.rgb, foil, _FoilIntensity);
                col.rgb += glint;                            // additive specular sparkle

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
