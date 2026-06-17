using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Binds a Graphic (Image, TMP text, any UGUI graphic) or SpriteRenderer color to a theme token.
    /// Executes in edit mode: changing a token in the theme asset recolors every bound element live.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Neo/UI/Theming/Theme Color Target")]
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
        // last token we warned about, so an unresolved token logs once — not every theme refresh
        private string _warnedToken;

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
            if (string.IsNullOrEmpty(token)) return;

            Color color;
            // a "#RRGGBB"/"#RRGGBBAA" token is a literal fill, not a theme name: bake it
            // directly so hex backgrounds survive (and translucent ones stay translucent)
            // instead of missing the theme lookup and leaving the Graphic opaque white.
            if (token.StartsWith("#"))
            {
                if (!ColorUtils.TryParseHex(token, out color))
                {
                    if (_warnedToken != token)
                    {
                        _warnedToken = token;
                        Debug.LogWarning($"ThemeColorTarget on '{name}': could not parse hex color '{token}'.", this);
                    }
                    return;
                }
            }
            else
            {
                Theme bound = theme;
                if (bound == null) return;
                if (!bound.TryGetColor(token, out color))
                {
                    // no silent failure: an unknown token left the Graphic an unexplained white.
                    // Warn once per distinct token so theme refreshes don't spam the console.
                    if (_warnedToken != token)
                    {
                        _warnedToken = token;
                        Debug.LogWarning($"ThemeColorTarget on '{name}': theme token '{token}' not found in theme '{(bound.name)}'.", this);
                    }
                    return;
                }
            }

            _warnedToken = null;
            color *= tint;
            if (_graphic != null) _graphic.color = color;
            else if (_spriteRenderer != null) _spriteRenderer.color = color;
        }
    }
}
