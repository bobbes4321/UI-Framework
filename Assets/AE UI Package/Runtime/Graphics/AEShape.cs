using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI
{
    /// <summary> The geometric primitives <see cref="AEShape"/> can render. </summary>
    public enum ShapeType
    {
        RoundedRect = 0,
        Circle = 1,
        Pill = 2,
        Checkmark = 3,
        Chevron = 4,
        Cross = 5,
        /// <summary> Annulus hugging the rect's inscribed circle; see <see cref="AEShape.ringThickness"/>. </summary>
        Ring = 6,
        /// <summary> Ring segment (rounded caps) for radial progress/dials; start/sweep in degrees clockwise from 12 o'clock. </summary>
        Arc = 7
    }

    /// <summary> How <see cref="AEShape"/> corner radii are interpreted. </summary>
    public enum ShapeRadiusUnit
    {
        /// <summary> Radius in canvas pixels. </summary>
        Pixels = 0,
        /// <summary> 0-100% of half the smaller rect dimension (100 = fully rounded). </summary>
        Percent = 1
    }

    /// <summary> How <see cref="AEShape"/> fills its interior. </summary>
    public enum ShapeFillMode
    {
        Solid = 0,
        LinearGradient = 1,
        RadialGradient = 2
    }

    /// <summary> How an <see cref="AEShape"/> texture fill maps onto the rect. </summary>
    public enum ShapeTextureFit
    {
        /// <summary> Texture stretches to the rect (distorts off-aspect art). </summary>
        Stretch = 0,
        /// <summary> Centered crop fills the rect, preserving texture aspect (CSS cover). </summary>
        Cover = 1
    }

    /// <summary>
    /// Procedural vector shape for UGUI: rounded rectangle (per-corner radius), circle, pill and
    /// stroke glyphs (checkmark/chevron/cross) with border, gradient fill and soft shadow — all
    /// rendered by a single SDF shader, no sprite assets. Shape parameters travel in vertex
    /// channels (UV1-UV3 + tangent) so every AEShape shares ONE material and batches normally.
    /// The inherited <see cref="Graphic.color"/> is the fill color, so ThemeColorTarget, color
    /// animators and tweens drive an AEShape exactly like an Image.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Rendering/Shape")]
    [RequireComponent(typeof(CanvasRenderer))]
    public class AEShape : MaskableGraphic
    {
        public const string ShaderName = "AlterEyes/UI/Shape";
        /// <summary> Resources name of the shader asset (fallback when Shader.Find misses). </summary>
        public const string ShaderResourcesPath = "AEShape";

        // shader-side shape codes; ShapeType collapses (Pill = rounded rect at max radius,
        // Circle = ellipse) so the shader only branches on what changes the math
        private const float ShaderRoundedRect = 0f;
        private const float ShaderEllipse = 1f;
        private const float ShaderCheckmark = 2f;
        private const float ShaderChevron = 3f;
        private const float ShaderCross = 4f;
        private const float ShaderArc = 5f; // ring = arc with a full sweep

        private const float EdgePaddingPx = 2f; // mesh slack so anti-aliasing is never clipped

        private const AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3 |
            AdditionalCanvasShaderChannels.Tangent;

        [SerializeField] private ShapeType shapeType = ShapeType.RoundedRect;

        [Tooltip("Pixels: radius in canvas px. Percent: 0-100 of half the smaller rect dimension (100 = pill).")]
        [SerializeField] private ShapeRadiusUnit radiusUnit = ShapeRadiusUnit.Pixels;
        [Tooltip("Use one radius for all four corners")]
        [SerializeField] private bool uniformRadius = true;
        [SerializeField] private float radius = 12f;
        [Tooltip("Per-corner radius: x=top-left, y=top-right, z=bottom-right, w=bottom-left")]
        [SerializeField] private Vector4 radiusPerCorner = new Vector4(12f, 12f, 12f, 12f);

        [SerializeField] private ShapeFillMode fillMode = ShapeFillMode.Solid;
        [Tooltip("Second gradient color; Graphic.color is the first")]
        [SerializeField] private Color fillColorB = Color.white;
        [Tooltip("Linear gradient direction in degrees (0 = left to right, 90 = bottom to top)")]
        [Range(0f, 360f)]
        [SerializeField] private float gradientAngle = 90f;

        [Tooltip("Border width in px; for glyph shapes this is the stroke thickness (0 = auto)")]
        [SerializeField] private float borderWidth;
        [SerializeField] private Color borderColor = Color.black;

        [Tooltip("Edge blur in px: 0 = crisp anti-aliased edge, higher turns the shape into a soft shadow/glow")]
        [SerializeField] private float softness;

        [Tooltip("Ring/Arc band width in px")]
        [SerializeField] private float thickness = 8f;
        [Tooltip("Arc start angle in degrees, clockwise from 12 o'clock")]
        [SerializeField] private float arcStartAngle;
        [Tooltip("Arc sweep in degrees (360 = full ring)")]
        [Range(0f, 360f)]
        [SerializeField] private float arcSweepAngle = 270f;

        [Tooltip("Optional texture fill, clipped by the shape (rounded-corner card art). " +
                 "Full-rect sprites only — tightly packed atlas sprites are not UV-remapped.")]
        [SerializeField] private Sprite fillSprite;
        [Tooltip("Stretch distorts the texture to the rect; Cover crops a centered sub-rect to " +
                 "fill it (CSS background-size: cover) — full-bleed card art at any aspect.")]
        [SerializeField] private ShapeTextureFit textureFit = ShapeTextureFit.Stretch;

        private static Material s_sharedMaterial;

        protected AEShape()
        {
            useLegacyMeshGeneration = false;
        }

        // ------------------------------------------------------------------ properties

        public ShapeType shape { get => shapeType; set => SetField(ref shapeType, value); }
        public ShapeRadiusUnit cornerRadiusUnit { get => radiusUnit; set => SetField(ref radiusUnit, value); }
        public bool useUniformRadius { get => uniformRadius; set => SetField(ref uniformRadius, value); }
        public float cornerRadius { get => radius; set => SetField(ref radius, value); }
        /// <summary> x=top-left, y=top-right, z=bottom-right, w=bottom-left. </summary>
        public Vector4 cornerRadii { get => radiusPerCorner; set => SetField(ref radiusPerCorner, value); }
        public ShapeFillMode fill { get => fillMode; set => SetField(ref fillMode, value); }
        public Color colorB { get => fillColorB; set => SetField(ref fillColorB, value); }
        public float gradientAngleDegrees { get => gradientAngle; set => SetField(ref gradientAngle, value); }
        public float border { get => borderWidth; set => SetField(ref borderWidth, value); }
        public Color outlineColor { get => borderColor; set => SetField(ref borderColor, value); }
        public float edgeSoftness { get => softness; set => SetField(ref softness, Mathf.Max(0f, value)); }
        /// <summary> Ring/Arc band width in px. </summary>
        public float ringThickness { get => thickness; set => SetField(ref thickness, Mathf.Max(0.5f, value)); }
        /// <summary> Arc start angle in degrees, clockwise from 12 o'clock. </summary>
        public float arcStart { get => arcStartAngle; set => SetField(ref arcStartAngle, value); }
        /// <summary> Arc sweep in degrees (360 = full ring). </summary>
        public float arcSweep { get => arcSweepAngle; set => SetField(ref arcSweepAngle, Mathf.Clamp(value, 0f, 360f)); }

        /// <summary>
        /// Optional texture fill: the sprite's texture multiplies the fill color (keep the fill
        /// white for unmodified art) and is clipped by the shape — rounded-corner images without
        /// mask components. The texture binds per CanvasRenderer, so the shared material survives;
        /// batching still splits per texture. Full-rect sprites only (no atlas UV remap).
        /// </summary>
        public Sprite sprite
        {
            get => fillSprite;
            set
            {
                if (fillSprite == value) return;
                fillSprite = value;
                SetMaterialDirty(); // rebinds mainTexture on the canvas renderer
            }
        }

        /// <summary> How the texture fill maps onto the rect (stretch vs cover-crop). </summary>
        public ShapeTextureFit textureFitMode { get => textureFit; set => SetField(ref textureFit, value); }

        public override Texture mainTexture => fillSprite != null ? fillSprite.texture : s_WhiteTexture;

        private void SetField<T>(ref T field, T value)
        {
            if (Equals(field, value)) return;
            field = value;
            SetVerticesDirty();
        }

        // ------------------------------------------------------------------ material

        /// <summary> The single material every AEShape shares (masked variants derive from it). </summary>
        public static Material sharedShapeMaterial
        {
            get
            {
                if (s_sharedMaterial != null) return s_sharedMaterial;
                Shader shader = Shader.Find(ShaderName);
                if (shader == null) shader = Resources.Load<Shader>(ShaderResourcesPath);
                if (shader == null)
                {
                    Debug.LogWarning($"[AlterEyes.UI] Shader '{ShaderName}' not found; AEShape falls back to UI/Default");
                    return defaultGraphicMaterial;
                }
                s_sharedMaterial = new Material(shader)
                {
                    name = "AEShape (Shared)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                return s_sharedMaterial;
            }
        }

        public override Material defaultMaterial => sharedShapeMaterial;

        // ------------------------------------------------------------------ lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasChannels();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnsureCanvasChannels();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            EnsureCanvasChannels();
        }

        /// <summary>
        /// The shader reads UV1-UV3 + tangent, which canvases strip by default; opt the host
        /// canvas in once (cheap flag check every call, write only when missing).
        /// </summary>
        private void EnsureCanvasChannels()
        {
            Canvas host = canvas;
            if (host == null) return;
            if ((host.additionalShaderChannels & RequiredChannels) == RequiredChannels) return;
            host.additionalShaderChannels |= RequiredChannels;
        }

        // ------------------------------------------------------------------ mesh

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect pixelRect = GetPixelAdjustedRect();
            var halfSize = new Vector2(Mathf.Max(pixelRect.width * 0.5f, 0.01f),
                Mathf.Max(pixelRect.height * 0.5f, 0.01f));
            Vector2 center = pixelRect.center;
            float minHalf = Mathf.Min(halfSize.x, halfSize.y);

            bool isGlyph = shapeType == ShapeType.Checkmark
                || shapeType == ShapeType.Chevron
                || shapeType == ShapeType.Cross;

            bool isArc = shapeType == ShapeType.Ring || shapeType == ShapeType.Arc;

            // uv1: corner radii in shader order (TR, BR, TL, BL); those params are meaningless
            // for glyphs (.x = stroke px) and ring/arc (.x = band px, .y = mid angle rad,
            // .z = half-sweep rad), so the channel is free per-mode
            Vector4 shapeData;
            float shaderShape;
            float borderPx;
            if (isGlyph)
            {
                shaderShape = shapeType == ShapeType.Checkmark ? ShaderCheckmark
                    : shapeType == ShapeType.Chevron ? ShaderChevron
                    : ShaderCross;
                float stroke = borderWidth > 0f ? borderWidth : Mathf.Max(2f, minHalf * 0.32f);
                shapeData = new Vector4(stroke, 0f, 0f, 0f);
                borderPx = 0f; // glyphs have no fill/border split — the stroke IS the fill
            }
            else if (isArc)
            {
                shaderShape = ShaderArc;
                float band = Mathf.Clamp(thickness, 0.5f, minHalf);
                float sweep = shapeType == ShapeType.Ring ? 360f : arcSweepAngle;
                float midDeg = arcStartAngle + sweep * 0.5f;
                shapeData = new Vector4(band, midDeg * Mathf.Deg2Rad, sweep * 0.5f * Mathf.Deg2Rad, 0f);
                borderPx = Mathf.Max(0f, borderWidth);
            }
            else
            {
                shaderShape = shapeType == ShapeType.Circle ? ShaderEllipse : ShaderRoundedRect;
                Vector4 corners = ResolveCornerRadii(halfSize, minHalf);
                shapeData = new Vector4(corners.y, corners.z, corners.x, corners.w);
                borderPx = Mathf.Max(0f, borderWidth);
            }

            float packedMode = shaderShape + 16f * (int)fillMode + 256f * (int)textureFit;
            var paramsData = new Vector4(packedMode, borderPx, Mathf.Max(0f, softness),
                gradientAngle * Mathf.Deg2Rad);
            var borderData = (Vector4)borderColor;
            var tangentData = (Vector4)fillColorB;

            float expand = Mathf.Max(0f, softness) + EdgePaddingPx;

            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;
            vert.uv1 = shapeData;
            vert.uv2 = paramsData;
            vert.uv3 = borderData;
            vert.tangent = tangentData;

            for (int i = 0; i < 4; i++)
            {
                float sx = i == 0 || i == 3 ? -1f : 1f;
                float sy = i < 2 ? -1f : 1f;
                var local = new Vector2(sx * (halfSize.x + expand), sy * (halfSize.y + expand));
                vert.position = new Vector3(center.x + local.x, center.y + local.y, 0f);
                vert.uv0 = new Vector4(local.x, local.y, halfSize.x, halfSize.y);
                vh.AddVert(vert);
            }

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        /// <summary>
        /// Radii as px in component order (TL, TR, BR, BL): resolves pill/percent and applies the
        /// CSS overlap rule (scale all radii down so adjacent corners never overlap an edge).
        /// </summary>
        public Vector4 ResolveCornerRadii(Vector2 halfSize, float minHalf)
        {
            Vector4 corners;
            if (shapeType == ShapeType.Pill)
            {
                corners = new Vector4(minHalf, minHalf, minHalf, minHalf);
            }
            else
            {
                corners = uniformRadius ? new Vector4(radius, radius, radius, radius) : radiusPerCorner;
                if (radiusUnit == ShapeRadiusUnit.Percent)
                    corners *= minHalf * 0.01f;
            }

            corners.x = Mathf.Max(0f, corners.x);
            corners.y = Mathf.Max(0f, corners.y);
            corners.z = Mathf.Max(0f, corners.z);
            corners.w = Mathf.Max(0f, corners.w);

            float width = halfSize.x * 2f;
            float height = halfSize.y * 2f;
            float scale = 1f;
            if (corners.x + corners.y > width) scale = Mathf.Min(scale, width / (corners.x + corners.y));
            if (corners.w + corners.z > width) scale = Mathf.Min(scale, width / (corners.w + corners.z));
            if (corners.x + corners.w > height) scale = Mathf.Min(scale, height / (corners.x + corners.w));
            if (corners.y + corners.z > height) scale = Mathf.Min(scale, height / (corners.y + corners.z));
            return corners * scale;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_sharedMaterial = null;
    }
}
