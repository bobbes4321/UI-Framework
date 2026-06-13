using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI
{
    /// <summary>
    /// Binds a Graphic (Image, TMP text, any UGUI graphic) or SpriteRenderer color to a theme token.
    /// Executes in edit mode: changing a token in the theme asset recolors every bound element live.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("AlterEyes/UI/Theming/Theme Color Target")]
    public class ThemeColorTarget : MonoBehaviour
    {
        [Tooltip("Theme token name, e.g. Primary / Background / TextDefault")]
        public string token;

        [Tooltip("Leave empty to use the active theme from settings")]
        public Theme themeOverride;

        [Tooltip("Multiplied onto the token color (use alpha to fade themed elements)")]
        public Color tint = Color.white;

        private Graphic _graphic;
        private SpriteRenderer _spriteRenderer;

        public Theme theme => themeOverride != null ? themeOverride : ThemeService.activeTheme;

        private void OnEnable()
        {
            CacheTarget();
            ThemeService.OnThemeChanged += HandleThemeChanged;
            ApplyColor();
        }

        private void Start()
        {
            // token/theme may be assigned from code right after AddComponent (post-OnEnable)
            ApplyColor();
        }

        private void OnDisable()
        {
            ThemeService.OnThemeChanged -= HandleThemeChanged;
        }

        private void OnValidate()
        {
            CacheTarget();
            ApplyColor();
        }

        private void CacheTarget()
        {
            if (_graphic == null) _graphic = GetComponent<Graphic>();
            if (_graphic == null && _spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void HandleThemeChanged(Theme changed)
        {
            Theme bound = theme;
            if (bound == null || changed != bound) return;
            ApplyColor();
        }

        public void ApplyColor()
        {
            Theme bound = theme;
            if (bound == null || string.IsNullOrEmpty(token)) return;
            if (!bound.TryGetColor(token, out Color color)) return;
            color *= tint;
            if (_graphic != null) _graphic.color = color;
            else if (_spriteRenderer != null) _spriteRenderer.color = color;
        }
    }
}
