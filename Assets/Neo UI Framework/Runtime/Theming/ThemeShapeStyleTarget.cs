using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Binds an <see cref="NeoShape"/> to a named <see cref="ShapeStyle"/> on the theme.
    /// Executes in edit mode: editing the style on the theme asset restyles every bound shape
    /// live, exactly like <see cref="ThemeColorTarget"/> does for colors.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Neo/UI/Theming/Theme Shape Style Target")]
    [RequireComponent(typeof(NeoShape))]
    public class ThemeShapeStyleTarget : MonoBehaviour
    {
        [Tooltip("Shape style name on the theme, e.g. Surface/Card")]
        public string style;

        [Tooltip("Leave empty to use the active theme from settings")]
        public Theme themeOverride;

        [Tooltip("Also apply the style's fill color. Turn off when a ThemeColorTarget or a color animator owns Graphic.color.")]
        public bool applyFillColor = true;

        private NeoShape _shape;

        public Theme theme => themeOverride != null ? themeOverride : ThemeService.activeTheme;

        private void OnEnable()
        {
            CacheTarget();
            ThemeService.OnThemeChanged += HandleThemeChanged;
            ApplyStyle();
        }

        private void Start()
        {
            // style/theme may be assigned from code right after AddComponent (post-OnEnable)
            ApplyStyle();
        }

        private void OnDisable()
        {
            ThemeService.OnThemeChanged -= HandleThemeChanged;
        }

        private void OnValidate()
        {
            CacheTarget();
            ApplyStyle();
        }

        private void CacheTarget()
        {
            if (_shape == null) _shape = GetComponent<NeoShape>();
        }

        private void HandleThemeChanged(Theme changed)
        {
            Theme bound = theme;
            if (bound == null || changed != bound) return;
            ApplyStyle();
        }

        public void ApplyStyle()
        {
            Theme bound = theme;
            if (bound == null || _shape == null || string.IsNullOrEmpty(style)) return;
            if (!bound.TryGetShapeStyle(style, out ShapeStyle shapeStyle)) return;
            shapeStyle.ApplyTo(_shape, bound);
            if (applyFillColor) _shape.color = shapeStyle.fillColor.Resolve(bound);
        }
    }
}
