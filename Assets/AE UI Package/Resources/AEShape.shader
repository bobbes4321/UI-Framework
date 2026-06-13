// AlterEyes UI procedural shape shader. One material serves every AEShape in the project:
// all shape parameters arrive per-vertex (UV0-UV3 + tangent), packed by AEShape.OnPopulateMesh.
//   uv0: xy = local position (px, rect-center origin)   zw = rect half-size (px)
//   uv1: corner radii px (x=TR y=BR z=TL w=BL)          glyphs: x = stroke thickness px
//        ring/arc: x = band px  y = mid angle rad (cw from 12h)  z = half-sweep rad
//   uv2: x = shapeMode + 16*fillMode  y = border px  z = softness px  w = gradient angle rad
//   uv3: border color
//   tangent: gradient color B (color A is the vertex color)
Shader "AlterEyes/UI/Shape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

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
                float4 texcoord : TEXCOORD0;
                float4 shapeData: TEXCOORD1;
                float4 params   : TEXCOORD2;
                float4 borderCol: TEXCOORD3;
                float4 tangent  : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 shapeData: TEXCOORD1;
                float4 params   : TEXCOORD2;
                float4 borderCol: TEXCOORD3;
                float4 gradColB : TEXCOORD4;
                float4 worldPosition : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float4 _ClipRect;

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
                OUT.borderCol = v.borderCol;
                OUT.gradColB = v.tangent;
                return OUT;
            }

            // signed distance to a rect of half-size b with per-corner radii r (x=TR y=BR z=TL w=BL)
            float sdRoundedRect(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x = (p.y > 0.0) ? r.x : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
            }

            // scaled-distance ellipse approximation — exact for circles, good enough for UI ovals
            float sdEllipse(float2 p, float2 ab)
            {
                float k = length(p / ab);
                return (k - 1.0) * min(ab.x, ab.y);
            }

            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.texcoord.xy;
                float2 halfSize = max(IN.texcoord.zw, 0.01);

                float packedMode = IN.params.x;
                float texFit = floor(packedMode / 256.0 + 0.001); // 1 = cover-crop texture fill
                packedMode -= texFit * 256.0;
                float fillMode = floor(packedMode / 16.0 + 0.001);
                float shapeMode = packedMode - fillMode * 16.0;
                float borderPx = IN.params.y;
                float softnessPx = IN.params.z;

                // ---- signed distance (px, negative inside)
                float d;
                if (shapeMode < 0.5)
                {
                    d = sdRoundedRect(p, halfSize, IN.shapeData);
                }
                else if (shapeMode < 1.5)
                {
                    d = sdEllipse(p, halfSize);
                }
                else if (shapeMode > 4.5)   // ring / arc segment with rounded caps
                {
                    float band = IN.shapeData.x;
                    float mid = IN.shapeData.y;     // arc midpoint, radians cw from 12 o'clock
                    float ap = IN.shapeData.z;      // half the sweep, radians (PI = full ring)
                    // rotate so the arc midpoint lands on +Y, then mirror (iq's sdArc)
                    float s0 = sin(mid);
                    float c0 = cos(mid);
                    float2 q = float2(abs(p.x * c0 - p.y * s0), p.x * s0 + p.y * c0);
                    float ra = min(halfSize.x, halfSize.y) - band * 0.5;
                    float2 sc = float2(sin(ap), cos(ap));
                    d = ((sc.y * q.x > sc.x * q.y) ? length(q - sc * ra) : abs(length(q) - ra)) - band * 0.5;
                }
                else
                {
                    float s = min(halfSize.x, halfSize.y);
                    float stroke = IN.shapeData.x;
                    float seg;
                    if (shapeMode < 2.5)        // checkmark
                    {
                        float2 m = float2(-0.12, -0.45) * s;
                        seg = min(sdSegment(p, float2(-0.55, 0.0) * s, m),
                                  sdSegment(p, m, float2(0.6, 0.4) * s));
                    }
                    else if (shapeMode < 3.5)   // chevron (points right; rotate the RectTransform)
                    {
                        float2 m = float2(0.35, 0.0) * s;
                        seg = min(sdSegment(p, float2(-0.3, 0.6) * s, m),
                                  sdSegment(p, m, float2(-0.3, -0.6) * s));
                    }
                    else                        // cross
                    {
                        seg = min(sdSegment(p, float2(-0.55, -0.55) * s, float2(0.55, 0.55) * s),
                                  sdSegment(p, float2(-0.55, 0.55) * s, float2(0.55, -0.55) * s));
                    }
                    d = seg - stroke * 0.5;
                }

                // ---- fill color (solid / linear / radial gradient) × optional sprite texture
                // _MainTex rides the CanvasRenderer (AEShape.mainTexture), white when no sprite —
                // uv spans the unpadded rect so the texture is clipped by the SDF, not the quad.
                // Cover fit samples a centered sub-rect matching the rect's aspect (CSS
                // background-size: cover) instead of stretching the full texture
                float2 texUV = p / (halfSize * 2.0);
                if (texFit > 0.5)
                {
                    float texAspect = _MainTex_TexelSize.z / max(_MainTex_TexelSize.w, 0.0001);
                    float rectAspect = halfSize.x / halfSize.y;
                    texUV *= float2(min(1.0, rectAspect / texAspect), min(1.0, texAspect / rectAspect));
                }
                half4 fillCol = IN.color * tex2D(_MainTex, saturate(texUV + 0.5));
                if (fillMode > 0.5)
                {
                    float t;
                    if (fillMode < 1.5)
                    {
                        float2 dir = float2(cos(IN.params.w), sin(IN.params.w));
                        float extent = abs(dir.x) * halfSize.x + abs(dir.y) * halfSize.y;
                        t = dot(p, dir) / max(extent, 0.0001) * 0.5 + 0.5;
                    }
                    else
                    {
                        t = length(p / halfSize);
                    }
                    fillCol = lerp(fillCol, (half4)IN.gradColB, saturate(t));
                }

                // ---- edge width: anti-aliasing, widened by softness for shadows/glows
                float aa = fwidth(d);
                float edgeW = max(aa, softnessPx * 2.0);

                half4 col = fillCol;
                if (borderPx > 0.0)
                {
                    float innerCov = 1.0 - smoothstep(-edgeW * 0.5, edgeW * 0.5, d + borderPx);
                    col = lerp((half4)IN.borderCol, fillCol, innerCov);
                }

                float coverage = 1.0 - smoothstep(-edgeW * 0.5, edgeW * 0.5, d);
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
