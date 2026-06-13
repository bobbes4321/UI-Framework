using System;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Holds the active theme and broadcasts theme changes to bound targets.
    /// The active theme defaults to the one referenced by <see cref="AEUISettings"/>.
    /// </summary>
    public static class ThemeService
    {
        private static Theme s_activeTheme;

        /// <summary> Raised when a theme's colors change or the active theme/variant switches. </summary>
        public static event Action<Theme> OnThemeChanged;

        public static Theme activeTheme
        {
            get
            {
                if (s_activeTheme != null) return s_activeTheme;
                s_activeTheme = AEUISettings.instance != null ? AEUISettings.instance.theme : null;
                return s_activeTheme;
            }
            set
            {
                if (s_activeTheme == value) return;
                s_activeTheme = value;
                if (value != null) NotifyThemeChanged(value);
            }
        }

        /// <summary> Switches the active variant on the active theme (e.g. "Dark" → "Light"). </summary>
        public static bool SetVariant(string variantName)
        {
            Theme theme = activeTheme;
            if (theme == null || theme.GetVariant(variantName) == null) return false;
            theme.ActiveVariantName = variantName;
            return true;
        }

        public static bool TryGetColor(string token, out Color color)
        {
            Theme theme = activeTheme;
            if (theme != null) return theme.TryGetColor(token, out color);
            color = Color.white;
            return false;
        }

        public static void NotifyThemeChanged(Theme theme) => OnThemeChanged?.Invoke(theme);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_activeTheme = null;
            OnThemeChanged = null;
        }
    }
}
