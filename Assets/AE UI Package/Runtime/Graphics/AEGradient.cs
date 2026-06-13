using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI
{
    /// <summary>
    /// Vertex-color gradient for any Graphic (Image, TMP text, AEShape): multiplies a two-stop
    /// linear gradient onto the existing vertex colors, so text anti-aliasing and sprite tints
    /// survive. Both stops are <see cref="ThemeColorRef"/>s, so gradients can ride the theme and
    /// recolor live when tokens or the active variant change.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("AlterEyes/UI/Rendering/Gradient")]
    [RequireComponent(typeof(Graphic))]
    public class AEGradient : BaseMeshEffect
    {
        [Tooltip("Color at the gradient start (the side the angle points away from)")]
        public ThemeColorRef colorA = new ThemeColorRef(Color.white);
        [Tooltip("Color at the gradient end (the side the angle points toward)")]
        public ThemeColorRef colorB = new ThemeColorRef(new Color(0.7f, 0.7f, 0.7f));
        [Tooltip("Gradient direction in degrees (0 = left to right, 90 = bottom to top)")]
        [Range(0f, 360f)]
        public float angle = 90f;

        private static UIVertex s_vertex;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            // bounds pass — gradient spans whatever the mesh covers (rect for shapes, glyphs for text)
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            int count = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref s_vertex, i);
                min = Vector2.Min(min, s_vertex.position);
                max = Vector2.Max(max, s_vertex.position);
            }

            Vector2 center = (min + max) * 0.5f;
            Vector2 halfSize = Vector2.Max((max - min) * 0.5f, new Vector2(0.001f, 0.001f));
            float radians = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            float extent = Mathf.Abs(dir.x) * halfSize.x + Mathf.Abs(dir.y) * halfSize.y;

            Color a = colorA.Resolve();
            Color b = colorB.Resolve();

            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref s_vertex, i);
                Vector2 local = (Vector2)s_vertex.position - center;
                float t = Vector2.Dot(local, dir) / extent * 0.5f + 0.5f;
                Color blend = Color.LerpUnclamped(a, b, Mathf.Clamp01(t));
                s_vertex.color *= blend;
                vh.SetUIVertex(s_vertex, i);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ThemeService.OnThemeChanged += HandleThemeChanged;
        }

        protected override void OnDisable()
        {
            ThemeService.OnThemeChanged -= HandleThemeChanged;
            base.OnDisable();
        }

        private void HandleThemeChanged(Theme theme)
        {
            if ((colorA.useToken || colorB.useToken) && graphic != null)
                graphic.SetVerticesDirty();
        }
    }
}
