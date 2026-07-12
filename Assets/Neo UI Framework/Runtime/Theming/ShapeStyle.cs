using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// The independently-ownable styling aspects of a <see cref="ShapeStyle"/>. A
    /// <see cref="ThemeShapeStyleTarget"/> applies only the aspects it owns, so a widget that overrides
    /// one aspect per-instance (e.g. a custom border) still live-follows the theme for the rest. Mirrors
    /// the color seam's <c>applyFillColor</c> opt-out, one flag per shape aspect.
    /// </summary>
    [Flags]
    public enum ShapeStyleAspects
    {
        None = 0,
        /// <summary> Radius unit, uniform flag, radius and per-corner radii. </summary>
        Radius = 1 << 0,
        /// <summary> Border width + color. </summary>
        Border = 1 << 1,
        /// <summary> Edge softness. </summary>
        Softness = 1 << 2,
        /// <summary> Fill mode + gradient second color/angle (not the primary fill color — that's applyFillColor). </summary>
        Fill = 1 << 3,
        All = Radius | Border | Softness | Fill
    }

    /// <summary>
    /// A named surface treatment for <see cref="NeoShape"/>s: corner radius, border and colors
    /// (as theme token refs) plus edge softness. Styles live on the <see cref="Theme"/> and are
    /// addressed by name via <see cref="ThemeShapeStyleTarget"/>, so restyling every card/button
    /// surface in the project is a single theme edit.
    /// </summary>
    [Serializable]
    public class ShapeStyle
    {
        public string name = "Surface";

        public ShapeRadiusUnit radiusUnit = ShapeRadiusUnit.Pixels;
        public bool uniformRadius = true;
        public float radius = 12f;
        [Tooltip("Per-corner radius: x=top-left, y=top-right, z=bottom-right, w=bottom-left")]
        public Vector4 radiusPerCorner = new Vector4(12f, 12f, 12f, 12f);

        public float borderWidth;
        public ThemeColorRef fillColor = new ThemeColorRef(Color.white);
        public ThemeColorRef borderColor = new ThemeColorRef(Color.black);

        [Tooltip("Edge blur in px: 0 = crisp, higher = soft shadow/glow surfaces")]
        public float softness;

        [Tooltip("Gradient fill: Graphic.color (or the fill token) blends toward Gradient To")]
        public ShapeFillMode fillMode = ShapeFillMode.Solid;
        [Tooltip("Second gradient color (the first is the fill color)")]
        public ThemeColorRef fillColorB = new ThemeColorRef(Color.white);
        [Tooltip("Linear gradient direction in degrees (0 = left to right, 90 = bottom to top)")]
        [Range(0f, 360f)]
        public float gradientAngle = 90f;

        [Tooltip("Drop-shadow level 0-3. Consumed at EDITOR BUILD TIME by composite widgets that call " +
                 "UIWidgetFactory.WithElevation (currently: Card, and popups since they're built from " +
                 "a Card) — it overrides that widget's own default level when set > 0; 0 defers to the " +
                 "widget's default rather than forcing 'no shadow' (0 and 'never authored' are the same " +
                 "value here). Shape styles bound to a plain NeoShape via ThemeShapeStyleTarget do NOT " +
                 "grow a shadow from this field: ApplyTo/ApplyStyle only recolor/reshape the ONE shape " +
                 "they're attached to, and — per the WYSIWYG + no-runtime-GameObject-churn rules — must " +
                 "never structurally add a shadow sibling on a theme change.")]
        [Range(0, 3)]
        public int elevation;

        /// <summary>
        /// Copies every styling field of this style onto a shape (not the fill color, not elevation —
        /// elevation is a bake-time widget-construction concern, see <see cref="elevation"/>).
        /// </summary>
        public void ApplyTo(NeoShape shape, Theme theme = null) => ApplyTo(shape, theme, ShapeStyleAspects.All);

        /// <summary>
        /// Copies only the selected <paramref name="aspects"/> of this style onto a shape (never the fill
        /// color — that's <c>applyFillColor</c> on the target — and never elevation). Aspects a widget
        /// overrides per-instance are excluded by <see cref="ThemeShapeStyleTarget"/> so the style's live
        /// theme-follow never clobbers them.
        /// </summary>
        public void ApplyTo(NeoShape shape, Theme theme, ShapeStyleAspects aspects)
        {
            if (shape == null) return;
            if ((aspects & ShapeStyleAspects.Radius) != 0)
            {
                shape.cornerRadiusUnit = radiusUnit;
                shape.useUniformRadius = uniformRadius;
                shape.cornerRadius = radius;
                shape.cornerRadii = radiusPerCorner;
            }
            if ((aspects & ShapeStyleAspects.Border) != 0)
            {
                shape.border = borderWidth;
                shape.outlineColor = borderColor.Resolve(theme);
            }
            if ((aspects & ShapeStyleAspects.Softness) != 0)
                shape.edgeSoftness = softness;
            if ((aspects & ShapeStyleAspects.Fill) != 0)
            {
                shape.fill = fillMode;
                if (fillMode != ShapeFillMode.Solid)
                {
                    shape.colorB = fillColorB.Resolve(theme);
                    shape.gradientAngleDegrees = gradientAngle;
                }
            }
        }
    }
}
