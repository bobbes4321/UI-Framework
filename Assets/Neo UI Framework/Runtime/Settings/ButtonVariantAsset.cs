using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A project-authored button variant (Pattern A — see
    /// <c>extensibility-seam-widget-attributes-plan.md</c>). Holds the variant name (matched
    /// case-insensitively against a spec's <c>variant</c> string), the per-state color set and the
    /// content token that colors the label/icon. Listed on <see cref="NeoUISettings.buttonVariants"/>;
    /// <c>UIWidgetFactory.VariantColors</c> consults the list before its built-in switch, so a
    /// project ships e.g. a <c>success</c> or <c>warning</c> variant with no package edit. Because
    /// <c>WidgetStyleTag</c> already stores the variant as a free string, the variant round-trips.
    /// </summary>
    [Serializable]
    public class ButtonVariantAsset
    {
        [Tooltip("Variant id used in specs (\"variant\": \"success\"); matched case-insensitively")]
        public string name;

        [Tooltip("Per-state fill colors (token refs keep the variant theme-bound)")]
        public SelectableColorSet colors = new SelectableColorSet();

        [Tooltip("Theme token coloring the button's label + icon")]
        public string contentToken = "TextOnPrimary";
    }

    /// <summary>
    /// A project-authored button size (Pattern A). Maps a size name (spec string-form
    /// <c>size</c>, e.g. <c>xl</c>) to the button height and the label text style.
    /// Listed on <see cref="NeoUISettings.buttonSizes"/>; <c>UIWidgetFactory.ButtonSize</c>
    /// consults it before the built-in sm/md/lg switch.
    /// </summary>
    [Serializable]
    public class ButtonSizeAsset
    {
        [Tooltip("Size id used in specs (\"size\": \"xl\"); matched case-insensitively")]
        public string name;

        [Tooltip("Button height in px for this size")]
        public float height = 56f;

        [Tooltip("Theme TextStyle name for the button label at this size")]
        public string labelStyle = "ButtonLabel";
    }
}
