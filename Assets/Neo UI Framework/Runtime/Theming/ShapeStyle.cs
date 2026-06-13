using System;
using UnityEngine;

namespace Neo.UI
{
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

        [Tooltip("Drop-shadow level 0-3 — consumed by the widget factory at build time (see ElevationRecipe)")]
        [Range(0, 3)]
        public int elevation;

        /// <summary> Copies every styling field of this style onto a shape (not the fill color). </summary>
        public void ApplyTo(NeoShape shape, Theme theme = null)
        {
            if (shape == null) return;
            shape.cornerRadiusUnit = radiusUnit;
            shape.useUniformRadius = uniformRadius;
            shape.cornerRadius = radius;
            shape.cornerRadii = radiusPerCorner;
            shape.border = borderWidth;
            shape.outlineColor = borderColor.Resolve(theme);
            shape.edgeSoftness = softness;
            shape.fill = fillMode;
            if (fillMode != ShapeFillMode.Solid)
            {
                shape.colorB = fillColorB.Resolve(theme);
                shape.gradientAngleDegrees = gradientAngle;
            }
        }
    }
}
