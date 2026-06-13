using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A color that is either a hardcoded value or a reference to a theme token.
    /// Used by selectable color sets and color animators so per-state colors can ride the theme.
    /// </summary>
    [Serializable]
    public class ThemeColorRef
    {
        public bool useToken;
        public string token;
        public Color color = Color.white;

        public ThemeColorRef() { }

        public ThemeColorRef(Color value)
        {
            color = value;
        }

        public ThemeColorRef(string themeToken)
        {
            useToken = true;
            token = themeToken;
        }

        public Color Resolve(Theme theme = null)
        {
            if (!useToken || string.IsNullOrEmpty(token)) return color;
            theme = theme != null ? theme : ThemeService.activeTheme;
            return theme != null && theme.TryGetColor(token, out Color themed) ? themed : color;
        }
    }

    /// <summary>
    /// Per-selection-state color set (Normal/Highlighted/Pressed/Selected/Disabled),
    /// each entry optionally referencing a theme token. Feeds the selectable/toggle color animators.
    /// </summary>
    [Serializable]
    public class SelectableColorSet
    {
        public ThemeColorRef normal = new ThemeColorRef(Color.white);
        public ThemeColorRef highlighted = new ThemeColorRef(new Color(0.9f, 0.9f, 0.9f));
        public ThemeColorRef pressed = new ThemeColorRef(new Color(0.7f, 0.7f, 0.7f));
        public ThemeColorRef selected = new ThemeColorRef(new Color(0.9f, 0.9f, 0.9f));
        public ThemeColorRef disabled = new ThemeColorRef(new Color(0.75f, 0.75f, 0.75f, 0.5f));

        public ThemeColorRef GetRef(UISelectionState state)
        {
            switch (state)
            {
                case UISelectionState.Highlighted: return highlighted;
                case UISelectionState.Pressed: return pressed;
                case UISelectionState.Selected: return selected;
                case UISelectionState.Disabled: return disabled;
                default: return normal;
            }
        }

        public Color GetColor(UISelectionState state, Theme theme = null) => GetRef(state).Resolve(theme);
    }

    /// <summary> The five interaction states of a UISelectable. </summary>
    public enum UISelectionState
    {
        Normal = 0,
        Highlighted = 1,
        Pressed = 2,
        Selected = 3,
        Disabled = 4
    }
}
