using System;
using TMPro;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// A named typographic treatment for TMP texts: font asset, size, style flags, spacing and a
    /// default color (as a theme token ref). Styles live on the <see cref="Theme"/> and are
    /// addressed by name via <see cref="ThemeTextStyleTarget"/> — the text mirror of
    /// <see cref="ShapeStyle"/>, so retyping every title/body/button label in the project is a
    /// single theme edit.
    /// </summary>
    [Serializable]
    public class TextStyle
    {
        public string name = "Body";

        public TMP_FontAsset font;
        public float size = 24f;
        public FontStyles fontStyle = FontStyles.Normal;

        [Tooltip("Extra tracking between characters (TMP characterSpacing units)")]
        public float characterSpacing;
        [Tooltip("Extra leading between lines (TMP lineSpacing units)")]
        public float lineSpacing;

        [Tooltip("Default text color; bound texts with their own ThemeColorTarget keep it instead")]
        public ThemeColorRef color = new ThemeColorRef("TextDefault");

        /// <summary> Copies every typographic field of this style onto a text (not the color). </summary>
        public void ApplyTo(TMP_Text text, Theme theme = null)
        {
            if (text == null) return;
            if (font != null) text.font = font;
            text.fontSize = size;
            text.fontStyle = fontStyle;
            text.characterSpacing = characterSpacing;
            text.lineSpacing = lineSpacing;
        }
    }
}
