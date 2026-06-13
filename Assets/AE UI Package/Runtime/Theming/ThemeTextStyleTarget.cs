using TMPro;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Binds a TMP text to a named <see cref="TextStyle"/> on the theme.
    /// Executes in edit mode: editing the style on the theme asset retypes every bound text
    /// live, exactly like <see cref="ThemeShapeStyleTarget"/> does for shapes.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("AlterEyes/UI/Theming/Theme Text Style Target")]
    [RequireComponent(typeof(TMP_Text))]
    public class ThemeTextStyleTarget : MonoBehaviour
    {
        [Tooltip("Text style name on the theme, e.g. Title/Body/Caption")]
        public string style;

        [Tooltip("Leave empty to use the active theme from settings")]
        public Theme themeOverride;

        [Tooltip("Also apply the style's color. Turn off when a ThemeColorTarget or a color animator owns the text color.")]
        public bool applyColor = true;

        private TMP_Text _text;

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
            if (_text == null) _text = GetComponent<TMP_Text>();
        }

        private void HandleThemeChanged(Theme changed)
        {
            Theme bound = theme;
            if (bound == null || changed != bound) return;
            ApplyStyle();
        }

        public void ApplyStyle()
        {
            CacheTarget(); // callable right after AddComponent — the factory bakes styles WYSIWYG
            Theme bound = theme;
            if (bound == null || _text == null || string.IsNullOrEmpty(style)) return;
            if (!bound.TryGetTextStyle(style, out TextStyle textStyle)) return;
            textStyle.ApplyTo(_text, bound);
            if (applyColor) _text.color = textStyle.color.Resolve(bound);
        }
    }
}
