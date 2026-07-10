using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Builds the package's functional widget hierarchies in editor code: every widget is
    /// NeoShape-based (no sprites), theme-bound (color tokens + shape styles by name) and wired to
    /// the interactive layer (UIButton/UIToggle/UISlider/Progressor). Used by the starter kit
    /// ("Create or Repair Starter Kit") and by the spec generator, so a spec'd slider and a
    /// starter slider are the same hierarchy — which is what lets the exporter read them back.
    /// </summary>
    public static class UIWidgetFactory
    {
        // theme token names the starter theme defines (widgets reference them by name only)
        public const string TokenBackground = "Background";
        public const string TokenSurface = "Surface";
        public const string TokenSurfaceElevated = "SurfaceElevated";
        public const string TokenOutline = "Outline";
        public const string TokenPrimary = "Primary";
        public const string TokenPrimaryHover = "PrimaryHover";
        public const string TokenPrimaryPressed = "PrimaryPressed";
        public const string TokenTextOnPrimary = "TextOnPrimary";
        public const string TokenTextStrong = "TextStrong";
        public const string TokenTextDefault = "TextDefault";
        public const string TokenTextMuted = "TextMuted";
        public const string TokenShadow = "Shadow";
        public const string TokenDanger = "Danger";
        public const string TokenDangerHover = "DangerHover";
        public const string TokenDangerPressed = "DangerPressed";
        public const string TokenSuccess = "Success";
        public const string TokenSuccessHover = "SuccessHover";
        public const string TokenSuccessPressed = "SuccessPressed";

        // shape style names the starter theme defines
        public const string StyleCard = "Card";
        public const string StylePanel = "Panel";
        public const string StyleControl = "Control";
        public const string StyleControlPill = "ControlPill";
        public const string StyleShadow = "ShadowSoft";

        // text style names the starter theme defines
        public const string TextStyleDisplay = "Display";
        public const string TextStyleTitle = "Title";
        public const string TextStyleHeading = "Heading";
        public const string TextStyleBody = "Body";
        public const string TextStyleCaption = "Caption";
        public const string TextStyleButtonLabel = "ButtonLabel";
        public const string TextStyleButtonLabelSmall = "ButtonLabelSmall";
        public const string TextStyleButtonLabelLarge = "ButtonLabelLarge";

        // button variant / size vocabulary (spec "variant" and string-form "size")
        public const string VariantPrimary = "primary";
        public const string VariantSecondary = "secondary";
        public const string VariantGhost = "ghost";
        public const string VariantDanger = "danger";
        // tab-only variant: translucent Surface at rest, solid Surface + Primary label selected
        public const string VariantLight = "light";
        public const string SizeSmall = "sm";
        public const string SizeMedium = "md";
        public const string SizeLarge = "lg";

        // widget-internal child names — the exporter recognizes widgets by these, keep in sync
        public const string LabelName = "Label";
        public const string IconName = "Icon";
        public const string BadgeName = "Badge";
        public const string BoxName = "Box";
        public const string CheckName = "Check";
        public const string KnobName = "Knob";
        public const string TrackName = "Track";
        public const string FillName = "Fill";
        public const string FillAreaName = "Fill Area";
        public const string HandleAreaName = "Handle Slide Area";
        public const string HandleName = "Handle";
        public const string ViewportName = "Viewport";
        public const string ContentName = "Content";
        public const string OverlayName = "Overlay";
        public const string CardName = "Card";
        public const string CloseName = "Close";
        public const string ShadowName = "Shadow";
        public const string SurfaceName = "Surface";
        public const string TextAreaName = "Text Area";
        public const string TextName = "Text";
        public const string PlaceholderName = "Placeholder";
        public const string ValueName = "Value";
        public const string MinusName = "Minus";
        public const string PlusName = "Plus";
        public const string StepperButtonSuffixMinus = UIStepper.ButtonSuffixMinus;
        public const string StepperButtonSuffixPlus = UIStepper.ButtonSuffixPlus;

        // tab child-name prefix — the exporter/generator address a tabbar's tab GameObjects by this
        public const string TabPrefix = "Tab_";
        public static string TabName(string name) => $"{TabPrefix}{name}";

        // popup chrome child names — matched by name on export to distinguish factory chrome from
        // authored content elements (an authored element id sanitizing to one of these is chrome-shadowed)
        public const string PopupTitleName = "Title";
        public const string PopupMessageName = "Message";
        public const string PopupButtonsName = "Buttons";

        // ------------------------------------------------------------------ anchor presets

        private struct AnchorPreset
        {
            public Vector2 min, max, pivot;

            public AnchorPreset(float minX, float minY, float maxX, float maxY, float pivotX, float pivotY)
            {
                min = new Vector2(minX, minY);
                max = new Vector2(maxX, maxY);
                pivot = new Vector2(pivotX, pivotY);
            }
        }

        private static readonly Dictionary<string, AnchorPreset> AnchorPresets =
            new Dictionary<string, AnchorPreset>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["TopLeft"] = new AnchorPreset(0, 1, 0, 1, 0, 1),
                ["Top"] = new AnchorPreset(0.5f, 1, 0.5f, 1, 0.5f, 1),
                ["TopRight"] = new AnchorPreset(1, 1, 1, 1, 1, 1),
                ["Left"] = new AnchorPreset(0, 0.5f, 0, 0.5f, 0, 0.5f),
                ["Center"] = new AnchorPreset(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f),
                ["Right"] = new AnchorPreset(1, 0.5f, 1, 0.5f, 1, 0.5f),
                ["BottomLeft"] = new AnchorPreset(0, 0, 0, 0, 0, 0),
                ["Bottom"] = new AnchorPreset(0.5f, 0, 0.5f, 0, 0.5f, 0),
                ["BottomRight"] = new AnchorPreset(1, 0, 1, 0, 1, 0),
                ["Stretch"] = new AnchorPreset(0, 0, 1, 1, 0.5f, 0.5f),
                ["StretchTop"] = new AnchorPreset(0, 1, 1, 1, 0.5f, 1),
                ["StretchBottom"] = new AnchorPreset(0, 0, 1, 0, 0.5f, 0),
                ["StretchLeft"] = new AnchorPreset(0, 0, 0, 1, 0, 0.5f),
                ["StretchRight"] = new AnchorPreset(1, 0, 1, 1, 1, 0.5f),
                ["StretchHorizontal"] = new AnchorPreset(0, 0.5f, 1, 0.5f, 0.5f, 0.5f),
                ["StretchVertical"] = new AnchorPreset(0.5f, 0, 0.5f, 1, 0.5f, 0.5f)
            };

        public static IEnumerable<string> AnchorPresetNames => AnchorPresets.Keys;

        public static bool TryApplyAnchor(RectTransform rect, string preset)
        {
            if (string.IsNullOrEmpty(preset) || !AnchorPresets.TryGetValue(preset, out AnchorPreset anchor))
                return false;
            Vector2 size = rect.sizeDelta;
            rect.anchorMin = anchor.min;
            rect.anchorMax = anchor.max;
            rect.pivot = anchor.pivot;
            // stretch axes keep zero offsets; fixed axes keep their size
            rect.sizeDelta = new Vector2(
                Mathf.Approximately(anchor.min.x, anchor.max.x) ? size.x : 0f,
                Mathf.Approximately(anchor.min.y, anchor.max.y) ? size.y : 0f);
            return true;
        }

        /// <summary> Reverse lookup for the exporter: anchors → preset name (null when custom). </summary>
        public static string DetectAnchor(RectTransform rect)
        {
            foreach (KeyValuePair<string, AnchorPreset> entry in AnchorPresets)
            {
                if ((rect.anchorMin - entry.Value.min).sqrMagnitude < 1e-6f
                    && (rect.anchorMax - entry.Value.max).sqrMagnitude < 1e-6f
                    && (rect.pivot - entry.Value.pivot).sqrMagnitude < 1e-6f)
                    return entry.Key;
            }
            return null;
        }

        /// <summary>
        /// OR a force-expand request onto a layout group AFTER it is configured — Unity's
        /// force-expand is a group-level flag, so a single child requesting "fill" turns it on for
        /// the whole group (documented limitation; per-child fill within a non-expanding group still
        /// works via flexibleWidth/Height=1). Never turns force-expand OFF — that would stomp the
        /// factory defaults (a vstack force-expands width so rows fill the column).
        /// </summary>
        public static void ForceExpandAxis(HorizontalOrVerticalLayoutGroup layout, bool horizontal)
        {
            if (layout == null) return;
            if (horizontal) layout.childForceExpandWidth = true;
            else layout.childForceExpandHeight = true;
        }

        // ------------------------------------------------------------------ primitives

        public static GameObject CreateRect(RectTransform parent, string name, Vector2 size, string anchor = "Center")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            if (parent != null) rect.SetParent(parent, worldPositionStays: false);
            rect.sizeDelta = size;
            TryApplyAnchor(rect, anchor);
            return go;
        }

        public static NeoShape AddShape(GameObject go, ShapeType shape, float radius = 12f,
            string fillToken = null, string style = null, bool styleOwnsFill = true)
        {
            var aeShape = go.AddComponent<NeoShape>();
            aeShape.shape = shape;
            aeShape.cornerRadius = radius;
            if (!string.IsNullOrEmpty(fillToken)) AddColorToken(go, fillToken);
            if (!string.IsNullOrEmpty(style))
            {
                var styleTarget = go.AddComponent<ThemeShapeStyleTarget>();
                styleTarget.style = style;
                // a color token / state animator on the same object owns Graphic.color
                styleTarget.applyFillColor = styleOwnsFill && string.IsNullOrEmpty(fillToken);
            }
            return aeShape;
        }

        /// <summary>
        /// Binds a Graphic's color to a fill value via <see cref="ThemeColorTarget"/>. The single
        /// place fills are bound, so the hex-vs-token branch lives here for shape, image and
        /// container card fills alike: a "#RRGGBB"/"#RRGGBBAA" value bakes a LITERAL color (alpha
        /// preserved — translucent stays translucent), anything else is a theme token name.
        /// An unparseable "#…" value warns loudly at bake time rather than silently rendering white.
        /// </summary>
        public static ThemeColorTarget AddColorToken(GameObject go, string token)
        {
            // Validate hex literals at bake time — ThemeColorTarget bakes the actual color when it
            // applies, but a bad value should surface here (with the element named) not silently.
            if (!string.IsNullOrEmpty(token) && token.StartsWith("#") && !ColorUtils.TryParseHex(token, out _))
                Debug.LogWarning($"AddColorToken on '{go.name}': could not parse hex color '{token}' — fill left unset.");
            var target = go.AddComponent<ThemeColorTarget>();
            target.token = token;
            return target;
        }

        /// <summary>
        /// Gives a TMP text an SDF outline by assigning a cached material preset. Presets live
        /// under <c>{UISpecGenerator.GeneratedRoot}/Materials</c> and are keyed by font + color +
        /// width, so prefabs keep stable references, repeat generates reuse them, and TMP batching
        /// only splits per distinct outline — never per text. Deriving the folder from
        /// <see cref="UISpecGenerator.GeneratedRoot"/> (rather than a hardcoded path) keeps
        /// showcase/scratch generates from leaking materials into the committed shared root.
        /// </summary>
        public static void ApplyTextOutline(TMP_Text text, Color color, float width)
        {
            if (text == null || text.font == null) return;
            string dir = $"{UISpecGenerator.GeneratedRoot}/Materials";
            EnsureFolder(dir);
            string key = $"{text.font.name}_Outline_{ColorUtility.ToHtmlStringRGBA(color)}_{Mathf.RoundToInt(width * 100f)}";
            string path = $"{dir}/{key}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(text.font.material);
                material.EnableKeyword(ShaderUtilities.Keyword_Outline);
                material.SetColor(ShaderUtilities.ID_OutlineColor, color);
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
                // fatten the face by most of the outline width — TMP outlines straddle the SDF
                // edge, so an undilated thin glyph gets swallowed (all-outline-colored) while
                // small widths disappear; dilation pushes the outline outside a chunky core
                material.SetFloat(ShaderUtilities.ID_FaceDilate, width * 0.8f);
                AssetDatabase.CreateAsset(material, path);
            }
            text.fontSharedMaterial = material;
        }

        /// <summary>
        /// Creates every missing folder level of <paramref name="path"/> (mirrors the per-bootstrap
        /// <c>EnsureFolder</c> helpers elsewhere in the package — <see cref="AssetDatabase.CreateFolder"/>
        /// only accepts an existing parent + a single new leaf, so nested roots like
        /// <c>Assets/Showcases/{id}/Generated/Materials</c> need each level created in turn).
        /// </summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static TextMeshProUGUI CreateLabel(RectTransform parent, string text,
            string colorToken = TokenTextDefault, float fontSize = 24f, string name = LabelName,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center, string textStyle = null)
        {
            GameObject go = CreateRect(parent, name, Vector2.zero, "Stretch");
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text ?? "";
            label.fontSize = fontSize; // styleless fallback; a textStyle overrides it below
            label.alignment = alignment;
            label.raycastTarget = false;
            if (!string.IsNullOrEmpty(colorToken)) AddColorToken(go, colorToken);
            if (!string.IsNullOrEmpty(textStyle))
            {
                var styleTarget = go.AddComponent<ThemeTextStyleTarget>();
                styleTarget.style = textStyle;
                // a color token on the same object owns the text color
                styleTarget.applyColor = string.IsNullOrEmpty(colorToken);
                // bake WYSIWYG: prefabs and screenshots must show the styled type, not the fallback
                styleTarget.ApplyStyle();
            }
            return label;
        }

        /// <summary>
        /// A glyph from the committed Lucide icon font, sized square and theme-colored.
        /// Unknown names warn and fall back to circle-help (no silent failures) — the spec
        /// generator additionally reports them as issues.
        /// </summary>
        public static TextMeshProUGUI CreateIcon(RectTransform parent, string iconName, float size = 32f,
            string colorToken = TokenTextDefault, string name = IconName)
        {
            if (!IconMap.TryResolveIcon(iconName, out ResolvedIcon resolved))
            {
                Debug.LogWarning($"[Neo.UI] Unknown icon '{iconName}' — using 'circle-help'. " +
                                 $"Valid names: see IconMap (Lucide set) + the project's IconMapOverlay.");
                IconMap.TryResolveIcon("circle-help", out resolved);
            }
            GameObject go = CreateRect(parent, name, new Vector2(size, size));
            var text = go.AddComponent<TextMeshProUGUI>();
            if (resolved.isSprite)
            {
                text.spriteAsset = resolved.spriteAsset;
                text.richText = true; // sprite tags need rich text
            }
            else
            {
                TMP_FontAsset iconFont = FontAssetBootstrap.EnsureIconFont(NeoUISettings.instance);
                if (iconFont != null)
                {
                    text.font = iconFont;
                    // bake-on-use: full-table glyphs outside the pre-baked curated set land in the
                    // committed atlas NOW, so builds never depend on runtime dynamic-font addition
                    if (!iconFont.HasCharacter(resolved.glyph))
                        iconFont.TryAddCharacters(resolved.glyph.ToString());
                }
            }
            text.text = resolved.BakedText;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Overflow; // glyph metrics may exceed the square rect
            text.raycastTarget = false;
            // name-addressed identity (canonical, so aliases never leak into exported specs);
            // the exporter reads this back instead of reverse-sniffing the glyph
            go.AddComponent<NeoIcon>().icon = resolved.name;
            if (!string.IsNullOrEmpty(colorToken)) AddColorToken(go, colorToken);
            WithLayoutSize(go, size, size, flexibleWidth: 0f, flexibleHeight: 0f);
            return text;
        }

        /// <summary> Rolling-number label (UICounter); label text bakes the start value (WYSIWYG). </summary>
        public static TextMeshProUGUI CreateCounter(RectTransform parent, float value, string format = "0",
            string colorToken = TokenTextStrong, float fontSize = 30f, string textStyle = null)
        {
            if (string.IsNullOrEmpty(format)) format = "0";
            string baked = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            TextMeshProUGUI text = CreateLabel(parent, baked, colorToken, fontSize,
                name: "Counter", textStyle: textStyle);
            var counter = text.gameObject.AddComponent<UICounter>();
            counter.value = value;
            counter.format = format;
            // re-bake LAST: if generation runs while the editor plays (bridge requests),
            // AddComponent fires Awake/OnEnable which writes the unconfigured value over the label
            text.text = baked;
            return text;
        }

        /// <summary> Notification badge pinned to a widget's top-right corner (count 0 = hidden). </summary>
        public static GameObject CreateBadge(GameObject widget, int count)
        {
            GameObject go = CreateRect((RectTransform)widget.transform, BadgeName, new Vector2(28f, 24f), "TopRight");
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = new Vector2(8f, 8f);
            // the host widget may run a layout group (icon+label rows) — badges float on the corner
            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            NeoShape pill = AddShape(go, ShapeType.Pill, fillToken: TokenDanger);
            pill.raycastTarget = false;

            TextMeshProUGUI label = CreateLabel((RectTransform)go.transform,
                count > 99 ? "99+" : count.ToString(), TokenTextOnPrimary, fontSize: 14f);

            var badge = go.AddComponent<UIBadge>();
            badge.label = label;
            // SetCount AFTER wiring (not field assignment): a play-mode OnEnable may already have
            // applied the default count — this re-applies deterministically and bakes WYSIWYG
            badge.SetCount(count);
            if (count <= 0)
            {
                pill.enabled = false;
                label.enabled = false;
            }
            return go;
        }

        /// <summary>
        /// Binds (or rebinds) a widget's label child to a named theme text style and bakes it.
        /// Used by the spec generator for "textStyle" overrides on button/toggle/tab labels.
        /// </summary>
        public static void SetLabelTextStyle(GameObject widget, string styleName)
        {
            if (widget == null || string.IsNullOrEmpty(styleName)) return;
            Transform label = widget.transform.Find(LabelName);
            TMP_Text text = label != null ? label.GetComponent<TMP_Text>() : null;
            if (text == null) return;
            var styleTarget = text.GetComponent<ThemeTextStyleTarget>();
            if (styleTarget == null)
            {
                styleTarget = text.gameObject.AddComponent<ThemeTextStyleTarget>();
                styleTarget.applyColor = text.GetComponent<ThemeColorTarget>() == null;
            }
            styleTarget.style = styleName;
            styleTarget.ApplyStyle();
        }

        // ------------------------------------------------------------------ widgets

        public static GameObject CreateButton(RectTransform parent, string category, string name, string label,
            string icon = null, string variant = VariantPrimary, string size = SizeMedium)
        {
            variant = string.IsNullOrEmpty(variant) ? VariantPrimary : variant;
            size = string.IsNullOrEmpty(size) ? SizeMedium : size;
            (float height, string labelStyle) = ButtonSize(size);

            GameObject go = CreateRect(parent, "Button", new Vector2(240f, height));
            // ghost has no surface: skip the bordered Control style, keep only the radius
            NeoShape shape = variant == VariantGhost
                ? AddShape(go, ShapeType.RoundedRect, 12f)
                : AddShape(go, ShapeType.RoundedRect, 12f, style: StyleControl, styleOwnsFill: false);

            var button = go.AddComponent<UIButton>();
            button.id = new ButtonId(category, name);
            button.transition = Selectable.Transition.None;

            // the exporter reads variant/size back from this tag (inferring them from baked
            // token references would be fragile)
            var tag = go.AddComponent<WidgetStyleTag>();
            tag.variant = variant;
            tag.size = size;

            var stateColors = go.AddComponent<UISelectableColorAnimator>();
            stateColors.colors = VariantColors(variant, out string contentToken);
            if (variant == VariantGhost)
            {
                // a transparent-WHITE rest color makes the first hover tween flash bright (the RGB
                // lerps from white toward the dark hover fill) — rest on the hover RGB at alpha 0
                Color hover = stateColors.colors.highlighted.Resolve();
                stateColors.colors.normal = new ThemeColorRef(new Color(hover.r, hover.g, hover.b, 0f));
            }
            // state animators only run at runtime — bake the resting color so prefabs,
            // edit-mode views and screenshots show the real look
            shape.color = stateColors.colors.normal.Resolve();

            AddHoverAndPressFeel(go);

            var rect = (RectTransform)go.transform;
            bool iconOnly = !string.IsNullOrEmpty(icon) && string.IsNullOrEmpty(label);
            if (iconOnly)
            {
                CreateIcon(rect, icon, Mathf.Round(height * 0.5f), contentToken);
                rect.sizeDelta = new Vector2(height, height);
                WithLayoutSize(go, height, height, flexibleWidth: 0f);
            }
            else if (!string.IsNullOrEmpty(icon))
            {
                var row = go.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(20, 20, 0, 0);
                row.spacing = 10f;
                row.childAlignment = TextAnchor.MiddleCenter;
                row.childControlWidth = true;
                row.childControlHeight = true;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
                CreateIcon(rect, icon, Mathf.Round(height * 0.45f), contentToken);
                CreateLabel(rect, label, contentToken, textStyle: labelStyle);
                WithLayoutSize(go, -1f, height, flexibleHeight: 0f);
            }
            else
            {
                // a text-only button must HUG its label: give it an internal HorizontalLayoutGroup
                // (so the label lives inside a width-controlling group and never wraps a glyph per
                // line) plus a horizontal-fit ContentSizeFitter so the button reports a real
                // preferred width upward — without this a layout-group parent gives it width 0 and
                // the label collapses to one character per line (the icon+label path already gets a
                // preferred width from its own layout group; this brings the plain path to parity).
                var row = go.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(20, 20, 0, 0);
                row.childAlignment = TextAnchor.MiddleCenter;
                row.childControlWidth = true;
                row.childControlHeight = true;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
                var fitter = go.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                TextMeshProUGUI buttonLabel = CreateLabel(rect, label, contentToken, textStyle: labelStyle);
                buttonLabel.enableWordWrapping = false;
                WithLayoutSize(go, -1f, height, flexibleHeight: 0f);
            }
            return go;
        }

        /// <summary>
        /// Default interaction juice shared by buttons and tabs: hovering scales up slightly,
        /// pressing dips below rest. The project's chosen Button/Hover + Button/Press defaults
        /// (Setup wizard / Design System) win when configured — so generated UI feels exactly like a
        /// hand-added animator — and the built-in scale-pop is the fallback when a role is unset, so a
        /// project that never picks a default still gets good feel. Leaving a state whose animation has
        /// no enabled channels restores the rest scale (see UISelectableUIAnimator.OnSelectionStateChanged).
        /// </summary>
        private static void AddHoverAndPressFeel(GameObject go, float hoverScale = 1.05f)
        {
            var feel = go.AddComponent<UISelectableUIAnimator>();

            if (!NeoUISettings.ApplyDefaultAnimation(NeoAnimatorRoles.ButtonHover, feel.highlightedAnimation))
            {
                feel.highlightedAnimation.scale.enabled = true;
                feel.highlightedAnimation.scale.fromReference = ReferenceValue.StartValue;
                feel.highlightedAnimation.scale.toReference = ReferenceValue.CustomValue;
                feel.highlightedAnimation.scale.toCustomValue = new Vector3(hoverScale, hoverScale, 1f);
                feel.highlightedAnimation.scale.settings.duration = 0.12f;
                feel.highlightedAnimation.scale.settings.ease = Ease.OutQuad;
            }

            if (!NeoUISettings.ApplyDefaultAnimation(NeoAnimatorRoles.ButtonPress, feel.pressedAnimation))
            {
                feel.pressedAnimation.scale.enabled = true;
                feel.pressedAnimation.scale.fromReference = ReferenceValue.StartValue;
                feel.pressedAnimation.scale.toReference = ReferenceValue.CustomValue;
                feel.pressedAnimation.scale.toCustomValue = new Vector3(0.96f, 0.96f, 1f);
                feel.pressedAnimation.scale.settings.duration = 0.08f;
                feel.pressedAnimation.scale.settings.ease = Ease.OutQuad;
            }
        }

        private static (float height, string labelStyle) ButtonSize(string size)
        {
            // Pattern A seam (extensibility-seam-widget-attributes-plan.md): a project-authored size
            // on the settings asset wins; the built-in sm/md/lg switch is the fallback (unchanged).
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.TryGetButtonSize(size, out float h, out string style))
                return (h, string.IsNullOrEmpty(style) ? TextStyleButtonLabel : style);
            switch (size)
            {
                case SizeSmall: return (40f, TextStyleButtonLabelSmall);
                case SizeLarge: return (72f, TextStyleButtonLabelLarge);
                default: return (56f, TextStyleButtonLabel);
            }
        }

        /// <summary> Per-variant state colors; contentToken colors the label and icon.
        /// <c>NeoUISettings.buttonVariants</c> is consulted first and is now the PRIMARY source for
        /// the canonical variants too — <c>StarterKitBootstrap.EnsureButtonVariants</c> seeds
        /// primary/secondary/ghost/danger/success into it (token-bound, editable in the Design
        /// System window's Buttons tab) on every "Create or Repair Starter Kit" run. The built-in
        /// 4-case switch below survives only as a last-resort fallback for assets created before
        /// that seeding existed (or that never ran the starter kit) — it is intentionally NOT given
        /// a `success` case, since that variant only ever comes from seeded data (design-system-
        /// cohesion-plan.md Phase 2.6). </summary>
        private static SelectableColorSet VariantColors(string variant, out string contentToken)
        {
            // Pattern A seam (extensibility-seam-widget-attributes-plan.md): project-authored
            // variant wins; the built-in switch below is byte-identical fallback, kept only for
            // settings assets that predate StarterKitBootstrap.EnsureButtonVariants.
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.TryGetVariantColors(variant, out SelectableColorSet projectColors,
                    out string projectToken))
            {
                contentToken = string.IsNullOrEmpty(projectToken) ? TokenTextOnPrimary : projectToken;
                return projectColors;
            }
            switch (variant)
            {
                case VariantSecondary:
                    contentToken = TokenTextStrong;
                    return new SelectableColorSet
                    {
                        normal = new ThemeColorRef(TokenSurface),
                        highlighted = new ThemeColorRef(TokenSurfaceElevated),
                        pressed = new ThemeColorRef(TokenBackground),
                        selected = new ThemeColorRef(TokenSurfaceElevated),
                        disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.3f))
                    };
                case VariantGhost:
                    contentToken = TokenPrimary;
                    return new SelectableColorSet
                    {
                        normal = new ThemeColorRef(new Color(1f, 1f, 1f, 0f)),
                        highlighted = new ThemeColorRef(TokenSurfaceElevated),
                        pressed = new ThemeColorRef(TokenSurface),
                        selected = new ThemeColorRef(TokenSurfaceElevated),
                        disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.15f))
                    };
                case VariantDanger:
                    contentToken = TokenTextOnPrimary;
                    return new SelectableColorSet
                    {
                        normal = new ThemeColorRef(TokenDanger),
                        highlighted = new ThemeColorRef(TokenDangerHover),
                        pressed = new ThemeColorRef(TokenDangerPressed),
                        selected = new ThemeColorRef(TokenDangerHover),
                        disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.5f))
                    };
                default:
                    contentToken = TokenTextOnPrimary;
                    return new SelectableColorSet
                    {
                        normal = new ThemeColorRef(TokenPrimary),
                        highlighted = new ThemeColorRef(TokenPrimaryHover),
                        pressed = new ThemeColorRef(TokenPrimaryPressed),
                        selected = new ThemeColorRef(TokenPrimaryHover),
                        disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.5f))
                    };
            }
        }

        /// <summary> Left inset of the toggle's check box inside its rect (labels clear it + a gap). </summary>
        public const float ToggleBoxInset = 14f;

        public static GameObject CreateToggle(RectTransform parent, string category, string name, string label)
        {
            GameObject go = CreateRect(parent, "Toggle", new Vector2(240f, 40f));
            var toggle = go.AddComponent<UIToggle>();
            toggle.id = new ToggleId(category, name);
            toggle.transition = Selectable.Transition.None;

            GameObject box = CreateRect((RectTransform)go.transform, BoxName, new Vector2(28f, 28f), "Left");
            ((RectTransform)box.transform).anchoredPosition = new Vector2(ToggleBoxInset, 0f);
            NeoShape boxShape = AddShape(box, ShapeType.RoundedRect, 8f, style: StyleControl, styleOwnsFill: false);
            var boxColors = box.AddComponent<UIToggleColorAnimator>();
            boxColors.onColor = new ThemeColorRef(TokenPrimary);
            boxColors.offColor = new ThemeColorRef(TokenSurfaceElevated);
            boxShape.color = boxColors.offColor.Resolve();

            GameObject check = CreateRect((RectTransform)box.transform, CheckName, new Vector2(18f, 18f));
            AddShape(check, ShapeType.Checkmark, fillToken: TokenTextOnPrimary);
            check.GetComponent<NeoShape>().raycastTarget = false;
            check.AddComponent<CanvasGroup>().alpha = 0f; // baked off-state; the fade animator owns it at runtime
            var checkAnimator = check.AddComponent<UIToggleUIAnimator>();
            ConfigureFade(checkAnimator.onAnimation, 0f, 1f);
            ConfigureFade(checkAnimator.offAnimation, 1f, 0f);

            TextMeshProUGUI text = CreateLabel((RectTransform)go.transform, label, TokenTextDefault,
                fontSize: 22f, alignment: TextAlignmentOptions.MidlineLeft, textStyle: TextStyleBody);
            var textRect = (RectTransform)text.transform;
            textRect.offsetMin = new Vector2(52f, 0f); // box spans x 14..42 — clear it plus a gap
            WithLayoutSize(go, -1f, 40f);
            return go;
        }

        // knob LEFT-EDGE x stops inside the 64px track (the knob's pivot sits on its left edge)
        private const float SwitchKnobOffX = 4f;
        private const float SwitchKnobOnX = 36f;

        public static GameObject CreateSwitch(RectTransform parent, string category, string name)
        {
            GameObject go = CreateRect(parent, "Switch", new Vector2(64f, 32f));
            var toggle = go.AddComponent<UIToggle>();
            toggle.id = new ToggleId(category, name);
            toggle.transition = Selectable.Transition.None;

            GameObject track = CreateRect((RectTransform)go.transform, TrackName, Vector2.zero, "Stretch");
            NeoShape trackShape = AddShape(track, ShapeType.Pill);
            var trackColors = track.AddComponent<UIToggleColorAnimator>();
            trackColors.onColor = new ThemeColorRef(TokenPrimary);
            // off track must read as a FILL against the page background — Outline is a border
            // color and tends to match light backdrops (an off switch became invisible on the
            // lavender theme); TextDefault is lint-guaranteed to contrast Background
            trackColors.offColor = new ThemeColorRef(TokenTextDefault);
            trackShape.color = trackColors.offColor.Resolve();

            GameObject knob = CreateRect((RectTransform)go.transform, KnobName, new Vector2(24f, 24f), "Left");
            var knobRect = (RectTransform)knob.transform;
            // the Left preset pivots the knob on its LEFT EDGE — x is the knob's left edge offset
            // from the track's left edge, so the stops are 4 (off) and 64-24-4=36 (on)
            knobRect.anchoredPosition = new Vector2(SwitchKnobOffX, 0f);
            // the knob must contrast its TRACK in both states: on = Primary track → TextOnPrimary,
            // off = Outline track → TextStrong (a static TextOnPrimary knob disappears on dark
            // off-tracks — the SoftFantasy palette exposed this)
            NeoShape knobShape = AddShape(knob, ShapeType.Circle);
            knobShape.raycastTarget = false;
            var knobColors = knob.AddComponent<UIToggleColorAnimator>();
            knobColors.onColor = new ThemeColorRef(TokenTextOnPrimary);
            knobColors.offColor = new ThemeColorRef(TokenTextStrong);
            knobShape.color = knobColors.offColor.Resolve(); // baked off-state; BakeToggleOn flips it
            var knobAnimator = knob.AddComponent<UIToggleUIAnimator>();
            // springy knob travel (Ease.Spring overshoots then settles on the target)
            ConfigureMove(knobAnimator.onAnimation, new Vector3(SwitchKnobOffX, 0f, 0f), new Vector3(SwitchKnobOnX, 0f, 0f),
                duration: 0.35f, ease: Ease.Spring);
            ConfigureMove(knobAnimator.offAnimation, new Vector3(SwitchKnobOnX, 0f, 0f), new Vector3(SwitchKnobOffX, 0f, 0f),
                duration: 0.35f, ease: Ease.Spring);
            WithLayoutSize(go, 64f, 32f, flexibleWidth: 0f); // keep its pill width inside stacks
            return go;
        }

        /// <summary>
        /// Bakes a toggle/switch into its ON resting state — serialized <c>isOnValue</c>, on-colors,
        /// visible check, knob at the on stop. WYSIWYG: state animators only run at runtime, so a
        /// control whose start value is "on" must LOOK on in the prefab and in edit-mode renders.
        /// </summary>
        public static void BakeToggleOn(GameObject go)
        {
            var toggle = go.GetComponent<UIToggle>();
            if (toggle == null) return;
            var serialized = new SerializedObject(toggle);
            serialized.FindProperty("isOnValue").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (UIToggleColorAnimator colors in go.GetComponentsInChildren<UIToggleColorAnimator>(true))
            {
                // bake every animated graphic, not just shapes — tab labels/icons ride the same
                // animator and otherwise keep their off-state color under an on-state surface
                var graphic = colors.GetComponent<Graphic>();
                if (graphic != null) graphic.color = colors.onColor.Resolve();
            }
            Transform check = go.transform.Find($"{BoxName}/{CheckName}");
            if (check != null && check.TryGetComponent(out CanvasGroup checkGroup)) checkGroup.alpha = 1f;
            Transform knob = go.transform.Find(KnobName);
            if (knob != null) ((RectTransform)knob).anchoredPosition = new Vector2(SwitchKnobOnX, 0f); // the on-animation's target stop
        }

        public static GameObject CreateSlider(RectTransform parent, string category, string name,
            float min = 0f, float max = 1f, float value = 0.5f)
        {
            GameObject go = CreateRect(parent, "Slider", new Vector2(320f, 32f));
            var slider = go.AddComponent<UISlider>();
            if (!string.IsNullOrEmpty(name)) slider.id = new SliderId(category, name);
            slider.transition = Selectable.Transition.None;

            GameObject track = CreateRect((RectTransform)go.transform, TrackName, new Vector2(0f, 10f), "StretchHorizontal");
            AddShape(track, ShapeType.Pill, fillToken: TokenSurfaceElevated);

            GameObject fillArea = CreateRect((RectTransform)go.transform, FillAreaName, new Vector2(0f, 10f), "StretchHorizontal");
            GameObject fill = CreateRect((RectTransform)fillArea.transform, FillName, Vector2.zero, "Stretch");
            AddShape(fill, ShapeType.Pill, fillToken: TokenPrimary);
            fill.GetComponent<NeoShape>().raycastTarget = false;

            GameObject handleArea = CreateRect((RectTransform)go.transform, HandleAreaName, Vector2.zero, "Stretch");
            var handleAreaRect = (RectTransform)handleArea.transform;
            // inset so the 24px handle stays circular: Slider stretches the handle's cross axis
            // over this area, so the area itself must be 24px tall
            handleAreaRect.offsetMin = new Vector2(12f, 4f);
            handleAreaRect.offsetMax = new Vector2(-12f, -4f);
            GameObject handle = CreateRect((RectTransform)handleArea.transform, HandleName, new Vector2(24f, 0f));
            // the handle rests on the track/background, not on Primary — TextOnPrimary can vanish
            // against dark surfaces (it only guarantees contrast WITH Primary); TextStrong contrasts
            // the surface family in every theme
            NeoShape handleShape = AddShape(handle, ShapeType.Circle, fillToken: TokenTextStrong);
            // the Slider stretches the handle's cross axis over the handle area — at any slider
            // height other than the 32px design height that turns the circle into an ellipse;
            // width must follow the stretched height to stay round
            var handleFit = handle.AddComponent<AspectRatioFitter>();
            handleFit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            handleFit.aspectRatio = 1f;

            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handleShape;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            WithLayoutSize(go, -1f, 32f);
            return go;
        }

        public static GameObject CreateProgressBar(RectTransform parent, float min = 0f, float max = 1f,
            float? value = null)
        {
            GameObject go = CreateRect(parent, "ProgressBar", new Vector2(320f, 14f));
            var progressor = go.AddComponent<Progressor>();
            progressor.fromValue = min;
            progressor.toValue = max;
            // WYSIWYG: the runtime start state must equal the baked visual state — the default
            // SetFromValue behaviour would wipe the authored fill to empty on Play
            float start = Mathf.Clamp(value ?? Mathf.Lerp(min, max, 0.6f), Mathf.Min(min, max), Mathf.Max(min, max));
            progressor.onStartBehaviour = Progressor.StartBehaviour.SetCustomValue;
            progressor.startValue = start;

            AddShape(go, ShapeType.Pill, fillToken: TokenSurfaceElevated);

            // sprite-free fill: an NeoShape pill spanning anchors 0..progress keeps rounded caps
            // (an Image fillAmount cuts a square edge and shows a notch at the track's left cap).
            // Flush (no inset): the fill's left cap exactly overlays the track's, no dark crescent.
            float progress = Mathf.Approximately(max, min) ? 1f : Mathf.InverseLerp(min, max, start);
            GameObject fill = CreateRect((RectTransform)go.transform, FillName, Vector2.zero, "Stretch");
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(Mathf.Max(progress, 0.04f), 1f); // baked WYSIWYG span
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            NeoShape fillShape = AddShape(fill, ShapeType.Pill, fillToken: TokenPrimary);
            fillShape.raycastTarget = false;
            fillShape.enabled = progress > 0f;

            var target = fill.AddComponent<RectFillProgressTarget>();
            target.fill = fillRect;
            progressor.progressTargets.Add(target);
            WithLayoutSize(go, -1f, 14f);
            return go;
        }

        /// <summary> Ring track + arc fill driven by a Progressor — radial cooldowns/dials. </summary>
        public static GameObject CreateRadialProgress(RectTransform parent, float min = 0f, float max = 1f,
            float? value = null)
        {
            GameObject go = CreateRect(parent, "RadialProgress", new Vector2(96f, 96f));
            var progressor = go.AddComponent<Progressor>();
            progressor.fromValue = min;
            progressor.toValue = max;
            // WYSIWYG: runtime start state must equal the baked visual state (same rule as the bar)
            float start = Mathf.Clamp(value ?? Mathf.Lerp(min, max, 0.6f), Mathf.Min(min, max), Mathf.Max(min, max));
            progressor.onStartBehaviour = Progressor.StartBehaviour.SetCustomValue;
            progressor.startValue = start;

            NeoShape track = AddShape(go, ShapeType.Ring, fillToken: TokenSurfaceElevated);
            track.ringThickness = 10f;

            GameObject fill = CreateRect((RectTransform)go.transform, FillName, Vector2.zero, "Stretch");
            NeoShape arc = AddShape(fill, ShapeType.Arc, fillToken: TokenPrimary);
            arc.ringThickness = 10f;
            arc.arcStart = 0f;
            float progress = Mathf.Approximately(max, min) ? 1f : Mathf.InverseLerp(min, max, start);
            arc.arcSweep = progress * 360f; // baked resting sweep
            arc.raycastTarget = false;

            var target = fill.AddComponent<ShapeProgressTarget>();
            target.shape = arc;
            progressor.progressTargets.Add(target);

            WithLayoutSize(go, 96f, 96f, flexibleWidth: 0f, flexibleHeight: 0f);
            return go;
        }

        public static GameObject CreateTabBar(RectTransform parent, string category,
            IReadOnlyList<(string name, string label)> tabs)
        {
            var withIcons = new List<(string, string, string)>();
            if (tabs != null)
                foreach ((string name, string label) tab in tabs)
                    withIcons.Add((tab.name, tab.label, null));
            return CreateTabBar(parent, category, withIcons);
        }

        public static GameObject CreateTabBar(RectTransform parent, string category,
            IReadOnlyList<(string name, string label, string icon)> tabs)
        {
            GameObject go = CreateRect(parent, "TabBar", new Vector2(480f, 56f));
            AddShape(go, ShapeType.RoundedRect, 14f, style: StylePanel, styleOwnsFill: false);
            AddColorToken(go, TokenBackground); // inset look: darker bar, Surface tabs read as buttons

            var group = go.AddComponent<UIToggleGroup>();
            group.id = new ToggleId(category, "TabBar");
            group.controlMode = UIToggleGroup.ControlMode.OneToggleOnEnforced;

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            if (tabs != null)
            {
                bool first = true;
                foreach ((string name, string label, string icon) tab in tabs)
                {
                    CreateTab((RectTransform)go.transform, category, tab.name, tab.label, group,
                        startOn: first, icon: tab.icon);
                    first = false;
                }
            }
            // the inner force-expand makes the group report flexibleHeight upward — pin it,
            // or parent stacks hand the tab bar all their leftover space
            WithLayoutSize(go, -1f, 56f, flexibleHeight: 0f);
            return go;
        }

        public static GameObject CreateTab(RectTransform parent, string category, string name,
            string label, UIToggleGroup group, bool startOn = false, string icon = null,
            string variant = null)
        {
            GameObject go = CreateRect(parent, TabName(name), new Vector2(120f, 44f));
            NeoShape tabShape = AddShape(go, ShapeType.RoundedRect, 10f);
            var tab = go.AddComponent<UITab>();
            tab.id = new ToggleId(category, name);
            tab.transition = Selectable.Transition.None;
            tab.toggleGroup = group;

            // tab variants restyle the off/on surfaces for non-card contexts: "ghost" is a
            // borderless sidebar entry (transparent at rest, Primary pill selected), "light" sits
            // on saturated backdrops (translucent Surface at rest, solid Surface + Primary label
            // selected). The exporter reads the variant back from WidgetStyleTag, like buttons.
            var colors = go.AddComponent<UIToggleColorAnimator>();
            string onContentToken = TokenTextOnPrimary, offContentToken = TokenTextDefault;
            Color surface = new ThemeColorRef(TokenSurface).Resolve();

            // Pattern A seam (extensibility-seam-widget-attributes-plan.md): a project-authored
            // variant on the settings asset wins, mirroring CreateButton/VariantColors — its 5-state
            // SelectableColorSet maps onto the tab's on/off pair (selected -> on, normal -> off); the
            // built-in switch below is the unchanged fallback.
            NeoUISettings tabSettings = NeoUISettings.instance;
            if (tabSettings != null && tabSettings.TryGetVariantColors(variant,
                    out SelectableColorSet projectColors, out string projectToken))
            {
                colors.onColor = projectColors.selected;
                colors.offColor = projectColors.normal;
                onContentToken = string.IsNullOrEmpty(projectToken) ? TokenTextOnPrimary : projectToken;
            }
            else
            {
                switch (variant)
                {
                    case VariantGhost:
                        colors.onColor = new ThemeColorRef(TokenPrimary);
                        colors.offColor = new ThemeColorRef(new Color(surface.r, surface.g, surface.b, 0f));
                        offContentToken = TokenTextStrong;
                        break;
                    case VariantLight:
                        colors.onColor = new ThemeColorRef(TokenSurface);
                        // fully transparent at rest: light tabs sit on an authored backdrop (the
                        // header-tab sprite or art) that must show through until selected
                        colors.offColor = new ThemeColorRef(new Color(surface.r, surface.g, surface.b, 0f));
                        onContentToken = TokenPrimary;
                        offContentToken = TokenTextStrong;
                        // browser-tab silhouette: rounded shoulders, flat base
                        tabShape.useUniformRadius = false;
                        tabShape.cornerRadii = new Vector4(16f, 16f, 0f, 0f);
                        break;
                    default:
                        colors.onColor = new ThemeColorRef(TokenPrimary);
                        colors.offColor = new ThemeColorRef(TokenSurface);
                        break;
                }
            }
            if (!string.IsNullOrEmpty(variant))
            {
                var tag = go.AddComponent<WidgetStyleTag>();
                tag.variant = variant;
            }
            AddHoverAndPressFeel(go, hoverScale: 1.04f);
            // bake the resting state (animators only run at runtime); the first tab of a bar
            // starts selected so prefabs and screenshots show a real selection
            tabShape.color = (startOn ? colors.onColor : colors.offColor).Resolve();
            if (startOn)
            {
                var serializedTab = new SerializedObject(tab);
                serializedTab.FindProperty("isOnValue").boolValue = true;
                serializedTab.ApplyModifiedPropertiesWithoutUndo();
            }

            var rect = (RectTransform)go.transform;
            bool iconOnly = !string.IsNullOrEmpty(icon) && string.IsNullOrEmpty(label);
            if (iconOnly)
            {
                BindTabContentColor(CreateIcon(rect, icon, 22f, colorToken: null).gameObject, startOn,
                    onContentToken, offContentToken);
            }
            else if (!string.IsNullOrEmpty(icon))
            {
                var row = go.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(12, 12, 0, 0);
                row.spacing = 8f;
                row.childAlignment = TextAnchor.MiddleCenter;
                row.childControlWidth = true;
                row.childControlHeight = true;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
                BindTabContentColor(CreateIcon(rect, icon, 20f, colorToken: null).gameObject, startOn,
                    onContentToken, offContentToken);
                BindTabContentColor(CreateLabel(rect, label, null, fontSize: 20f,
                    textStyle: TextStyleButtonLabel).gameObject, startOn, onContentToken, offContentToken);
            }
            else
            {
                BindTabContentColor(CreateLabel(rect, label, null, fontSize: 20f,
                    textStyle: TextStyleButtonLabel).gameObject, startOn, onContentToken, offContentToken);
            }
            return go;
        }

        /// <summary>
        /// Tab labels/icons must flip color with the selection state: the selected tab's surface is
        /// Primary, where TextDefault can be unreadable (the SoftFantasy orange exposed this) — the
        /// content rides a toggle color animator (on = TextOnPrimary, off = TextDefault) instead of
        /// a static ThemeColorTarget, with the resting state baked for WYSIWYG.
        /// </summary>
        private static void BindTabContentColor(GameObject content, bool startOn,
            string onToken = TokenTextOnPrimary, string offToken = TokenTextDefault)
        {
            // the text style must not reapply its own color token over the state animator
            var styleTarget = content.GetComponent<ThemeTextStyleTarget>();
            if (styleTarget != null) styleTarget.applyColor = false;

            var colors = content.AddComponent<UIToggleColorAnimator>();
            colors.onColor = new ThemeColorRef(onToken);
            colors.offColor = new ThemeColorRef(offToken);
            var graphic = content.GetComponent<Graphic>();
            if (graphic != null) graphic.color = (startOn ? colors.onColor : colors.offColor).Resolve();
        }

        public static GameObject CreateListView(RectTransform parent)
        {
            GameObject go = CreateRect(parent, "ListView", new Vector2(360f, 420f));
            AddShape(go, ShapeType.RoundedRect, 16f, style: StyleCard, styleOwnsFill: false);
            AddColorToken(go, TokenSurface);
            var scroll = go.AddComponent<ScrollRect>();

            GameObject viewport = CreateRect((RectTransform)go.transform, ViewportName, Vector2.zero, "Stretch");
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.offsetMin = new Vector2(8f, 8f);
            viewportRect.offsetMax = new Vector2(-8f, -8f);
            viewport.AddComponent<RectMask2D>();

            GameObject content = CreateRect((RectTransform)viewport.transform, ContentName, Vector2.zero, "StretchTop");
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 8f;
            ConfigureStackSizing(layout, vertical: true);
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = (RectTransform)content.transform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 24f;
            WithLayoutSize(go, -1f, 420f);
            return go;
        }

        /// <summary>
        /// Builds the standardized drop-shadow sibling (a stretched soft NeoShape behind the
        /// widget's surface) for an elevation level 1-3; level 0 builds nothing. The shadow is
        /// inserted as the FIRST child so it always renders behind. Idempotent: an existing
        /// Shadow child is reconfigured, not duplicated. Bake-time only (called from editor widget
        /// construction, e.g. <see cref="CreateCard"/> via <see cref="ResolveElevation"/>) — never
        /// invoked at runtime, so a theme edit never spawns/destroys GameObjects on a live scene;
        /// <see cref="ThemeShapeStyleTarget"/> intentionally does not read
        /// <see cref="ShapeStyle.elevation"/> for that reason.
        /// </summary>
        public static GameObject WithElevation(GameObject go, int level)
        {
            Transform existing = go.transform.Find(ShadowName);
            if (level <= 0)
            {
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                return null;
            }

            ElevationRecipe.Level recipe = ElevationRecipe.Get(level);
            GameObject shadow = existing != null
                ? existing.gameObject
                : CreateRect((RectTransform)go.transform, ShadowName, Vector2.zero, "Stretch");
            shadow.transform.SetAsFirstSibling();
            var shadowRect = (RectTransform)shadow.transform;
            shadowRect.offsetMin = recipe.OffsetMin;
            shadowRect.offsetMax = recipe.OffsetMax;

            NeoShape shadowShape = shadow.GetComponent<NeoShape>();
            if (shadowShape == null)
                shadowShape = AddShape(shadow, ShapeType.RoundedRect, 18f,
                    fillToken: TokenShadow, style: StyleShadow, styleOwnsFill: false);
            shadowShape.edgeSoftness = recipe.softness;
            shadowShape.raycastTarget = false;
            var shadowColor = shadow.GetComponent<ThemeColorTarget>();
            if (shadowColor != null) shadowColor.tint = new Color(1f, 1f, 1f, recipe.alphaScale);
            return shadow;
        }

        /// <summary>
        /// Resolves the shadow level a style-governed composite widget should bake: the active
        /// theme's <c>styleName</c> <see cref="ShapeStyle.elevation"/> when a project has explicitly
        /// raised it above 0, else <paramref name="fallbackLevel"/> (the widget's built-in default —
        /// e.g. Card's classic level-2 shadow). Elevation defaults to 0 on every style until a project
        /// authors otherwise (Design System → Shapes), and 0 is indistinguishable from "never touched"
        /// for this int field, so 0 always defers to the widget's own default rather than stripping its
        /// shadow — a project that wants a genuinely flat card should drop the shadow structurally
        /// (its own composite via <see cref="WithElevation"/> at level 0), not via this field.
        /// </summary>
        public static int ResolveElevation(string styleName, int fallbackLevel)
        {
            Theme theme = NeoUISettings.instance != null ? NeoUISettings.instance.theme : null;
            if (theme != null && !string.IsNullOrEmpty(styleName)
                && theme.TryGetShapeStyle(styleName, out ShapeStyle style) && style.elevation > 0)
                return style.elevation;
            return fallbackLevel;
        }

        /// <summary> Soft-shadow + surface + padded vertical content stack. </summary>
        public static GameObject CreateCard(RectTransform parent, Vector2 size)
        {
            GameObject go = CreateRect(parent, CardName, size);

            // the canonical card shadow — level 2 by default, overridden when the theme's "Card"
            // ShapeStyle sets elevation > 0 (ResolveElevation)
            WithElevation(go, ResolveElevation(StyleCard, fallbackLevel: 2));

            GameObject surface = CreateRect((RectTransform)go.transform, SurfaceName, Vector2.zero, "Stretch");
            AddShape(surface, ShapeType.RoundedRect, 16f, fillToken: TokenSurface, style: StyleCard, styleOwnsFill: false);

            GameObject content = CreateRect((RectTransform)go.transform, ContentName, Vector2.zero, "Stretch");
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 12f;
            ConfigureStackSizing(layout, vertical: true);
            return go;
        }

        /// <summary> The factory-default popup card size (omitted spec "size" round-trips as absent). </summary>
        public static readonly Vector2 PopupDefaultCardSize = new Vector2(360f, 240f);

        /// <summary>
        /// Popup chassis: stretched root (UIPopup + scrim overlay) holding a card; rich popups
        /// stack their own elements into the returned content host.
        /// </summary>
        public static GameObject CreatePopupShell(string popupName, Vector2 size, out RectTransform content)
        {
            GameObject go = CreateRect(null, $"Popup_{popupName}", Vector2.zero, "Stretch");
            go.AddComponent<CanvasGroup>();
            var popup = go.AddComponent<UIPopup>();
            popup.popupName = popupName;
            popup.hideOnClickOverlay = true;

            GameObject overlay = CreateRect((RectTransform)go.transform, OverlayName, Vector2.zero, "Stretch");
            NeoShape overlayShape = AddShape(overlay, ShapeType.RoundedRect, 0f, fillToken: TokenShadow);
            overlayShape.color = new Color(0f, 0f, 0f, 0.6f);

            GameObject card = CreateCard((RectTransform)go.transform, size);
            content = (RectTransform)card.transform.Find(ContentName);
            popup.content = (RectTransform)card.transform;
            return go;
        }

        public static GameObject CreatePopup(string popupName, string title, string message)
        {
            GameObject go = CreatePopupShell(popupName, PopupDefaultCardSize, out RectTransform content);
            var popup = go.GetComponent<UIPopup>();

            TextMeshProUGUI titleLabel = CreateLabel(content, title, TokenTextStrong, 28f, PopupTitleName,
                textStyle: TextStyleHeading);
            TextMeshProUGUI messageLabel = CreateLabel(content, message, TokenTextDefault, 22f, PopupMessageName,
                textStyle: TextStyleBody);

            GameObject buttonRow = CreateRect(content, PopupButtonsName, new Vector2(0f, 56f));
            var row = buttonRow.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = false;
            row.childControlHeight = false;
            GameObject ok = CreateButton((RectTransform)buttonRow.transform, "Popup", $"{popupName}_OK", "OK");
            ok.AddComponent<HideContainerOnClick>();

            popup.labels = new List<TMP_Text> { titleLabel, messageLabel };
            return go;
        }

        /// <summary>
        /// X dismiss button overhanging the popup card's top-right corner (spec "close": true).
        /// Lives on the card root (not the content stack) so the layout never moves it.
        /// </summary>
        public static GameObject CreatePopupCloseButton(GameObject popupRoot, string popupName)
        {
            var popup = popupRoot.GetComponent<UIPopup>();
            GameObject close = CreateButton(popup.content, "Popup", $"{popupName}_Close", null,
                icon: "x", variant: VariantDanger, size: SizeSmall);
            close.name = CloseName;
            var rect = (RectTransform)close.transform;
            TryApplyAnchor(rect, "TopRight");
            rect.sizeDelta = new Vector2(48f, 48f);
            rect.anchoredPosition = new Vector2(16f, 16f);
            var floating = close.GetComponent<LayoutElement>();
            if (floating == null) floating = close.AddComponent<LayoutElement>();
            floating.ignoreLayout = true;
            close.AddComponent<HideContainerOnClick>();
            return close;
        }

        public static GameObject CreateTooltip(string text = "Tooltip")
        {
            GameObject go = CreateRect(null, "Tooltip", new Vector2(240f, 56f));
            go.AddComponent<CanvasGroup>();
            var tooltip = go.AddComponent<UITooltip>();

            GameObject card = CreateRect((RectTransform)go.transform, CardName, Vector2.zero, "Stretch");
            NeoShape shape = AddShape(card, ShapeType.RoundedRect, 10f, fillToken: TokenSurfaceElevated);
            shape.border = 1f;
            shape.raycastTarget = false;

            TextMeshProUGUI label = CreateLabel((RectTransform)card.transform, text, TokenTextDefault, 20f,
                textStyle: TextStyleCaption);
            tooltip.labels = new List<TMP_Text> { label };
            return go;
        }

        public static GameObject CreateInputField(RectTransform parent, string placeholder)
        {
            GameObject go = CreateRect(parent, "Input", new Vector2(320f, 48f));
            AddShape(go, ShapeType.RoundedRect, 12f, fillToken: TokenSurfaceElevated,
                style: StyleControl, styleOwnsFill: false);

            GameObject textArea = CreateRect((RectTransform)go.transform, TextAreaName, Vector2.zero, "Stretch");
            var textAreaRect = (RectTransform)textArea.transform;
            textAreaRect.offsetMin = new Vector2(14f, 6f);
            textAreaRect.offsetMax = new Vector2(-14f, -6f);
            textArea.AddComponent<RectMask2D>();

            // no textStyle here: the style binder would re-apply Normal over the italic at runtime
            TextMeshProUGUI placeholderLabel = CreateLabel(textAreaRect, placeholder, TokenTextMuted,
                fontSize: 22f, name: PlaceholderName, alignment: TextAlignmentOptions.MidlineLeft);
            placeholderLabel.fontStyle = FontStyles.Italic;

            TextMeshProUGUI text = CreateLabel(textAreaRect, "", TokenTextDefault,
                fontSize: 22f, name: TextName, alignment: TextAlignmentOptions.MidlineLeft,
                textStyle: TextStyleBody);

            var input = go.AddComponent<TMP_InputField>();
            input.transition = Selectable.Transition.None;
            input.textViewport = textAreaRect;
            input.textComponent = text;
            input.placeholder = placeholderLabel;

            WithLayoutSize(go, -1f, 48f);
            return go;
        }

        public static GameObject CreateStepper(RectTransform parent, string category, string name,
            float min, float max, float value, float step)
        {
            GameObject go = CreateRect(parent, "Stepper", new Vector2(240f, 48f));

            var stepper = go.AddComponent<UIStepper>();
            stepper.minValue = min;
            stepper.maxValue = max;
            stepper.stepSize = step;
            stepper.wholeNumbers = Mathf.Approximately(step, Mathf.Round(step))
                                   && Mathf.Approximately(min, Mathf.Round(min));

            // center-clustered (±100 around the value) so a stretching parent stack — vstacks
            // force-expand child widths regardless of flexibleWidth — can't spread the buttons
            GameObject minus = CreateStepperButton(go, MinusName, new Vector2(-100f, 0f), "−",
                category, name, StepperButtonSuffixMinus);
            GameObject plus = CreateStepperButton(go, PlusName, new Vector2(100f, 0f), "+",
                category, name, StepperButtonSuffixPlus);

            TextMeshProUGUI valueLabel = CreateLabel((RectTransform)go.transform,
                FormatStepperValue(value), TokenTextStrong, fontSize: 24f, name: ValueName,
                textStyle: TextStyleBody);
            var valueRect = (RectTransform)valueLabel.transform;
            valueRect.anchorMin = valueRect.anchorMax = new Vector2(0.5f, 0.5f);
            valueRect.pivot = new Vector2(0.5f, 0.5f);
            valueRect.sizeDelta = new Vector2(104f, 48f);
            valueRect.anchoredPosition = Vector2.zero;

            // the label must FOLLOW the value at runtime — a static label reads as "buttons broken"
            var labelBinding = valueLabel.gameObject.AddComponent<UIStepperValueLabel>();
            labelBinding.stepper = stepper;
            labelBinding.label = valueLabel;

            stepper.minusButton = minus.GetComponent<UIButton>();
            stepper.plusButton = plus.GetComponent<UIButton>();
            stepper.currentValue = value;

            WithLayoutSize(go, 240f, 48f, flexibleWidth: 0f);
            return go;
        }

        public static string FormatStepperValue(float value) =>
            value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private static GameObject CreateStepperButton(GameObject stepper, string childName,
            Vector2 position, string glyph, string category, string name, string suffix)
        {
            GameObject go = CreateButton((RectTransform)stepper.transform, null, null, glyph);
            go.name = childName;
            var rect = (RectTransform)go.transform;
            TryApplyAnchor(rect, "Center");
            rect.sizeDelta = new Vector2(48f, 48f);
            rect.anchoredPosition = position;

            UIButton button = go.GetComponent<UIButton>();
            if (!string.IsNullOrEmpty(name))
                button.id = new ButtonId(category, name + suffix);
            // fixed square inside the stepper — never let stacks resize it
            WithLayoutSize(go, 48f, 48f, flexibleWidth: 0f, flexibleHeight: 0f);
            return go;
        }

        public static GameObject CreateSafeArea(RectTransform parent)
        {
            GameObject go = CreateRect(parent, "SafeArea", Vector2.zero, "Stretch");
            go.AddComponent<SafeAreaFitter>();
            return go;
        }

        // widget-internal child names for the dropdown (exporter recognizes the widget by these)
        public const string ArrowName = "Arrow";
        public const string TemplateName = "Template";
        public const string ItemName = "Item";
        public const string ItemLabelName = "Item Label";
        public const string ItemBackgroundName = "Item Background";
        public const string ItemCheckmarkName = "Item Checkmark";

        /// <summary>
        /// A single-choice dropdown (UIDropdown : TMP_Dropdown). Builds the standard TMP template
        /// (collapsed caption + arrow, an inactive scrollable item template) entirely from NeoShape so
        /// it batches with everything else. Options populate the list; the value is the selected index.
        /// </summary>
        public static GameObject CreateDropdown(RectTransform parent, string category, string name,
            IReadOnlyList<string> options, int value = 0)
        {
            GameObject go = CreateRect(parent, "Dropdown", new Vector2(320f, 48f));
            // Surface, not SurfaceElevated: an elevated fill reads as a disabled control on
            // light backgrounds (it matches the page wash instead of popping like a field)
            AddShape(go, ShapeType.RoundedRect, 12f, fillToken: TokenSurface,
                style: StyleControl, styleOwnsFill: false);
            var dropdown = go.AddComponent<UIDropdown>();
            dropdown.id = new DropdownId(category, name);
            dropdown.transition = Selectable.Transition.None;

            string captionText = options != null && options.Count > 0
                ? options[Mathf.Clamp(value, 0, options.Count - 1)] : "";
            TextMeshProUGUI caption = CreateLabel((RectTransform)go.transform, captionText, TokenTextDefault,
                fontSize: 22f, name: LabelName, alignment: TextAlignmentOptions.MidlineLeft, textStyle: TextStyleBody);
            var capRect = (RectTransform)caption.transform;
            capRect.offsetMin = new Vector2(14f, 0f);
            capRect.offsetMax = new Vector2(-40f, 0f);

            TextMeshProUGUI arrow = CreateIcon((RectTransform)go.transform, "chevron-down", 18f, TokenTextMuted, ArrowName);
            var arrowRect = (RectTransform)arrow.transform;
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(1f, 0.5f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-12f, 0f);

            // template (the popup list) — inactive in the closed state. Geometry follows TMP_Dropdown's
            // canonical template: content pivoted at top, the ITEM anchored middle-stretch
            // (anchors 0..1 × 0.5) — TMP stacks clones by the item's height, so any other anchor
            // setup breaks the row pitch and rows overlap.
            GameObject template = CreateRect((RectTransform)go.transform, TemplateName, new Vector2(0f, 232f), "StretchBottom");
            var templateRect = (RectTransform)template.transform;
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -2f);
            AddShape(template, ShapeType.RoundedRect, 12f, fillToken: TokenSurface, style: StyleCard, styleOwnsFill: false);
            var scroll = template.AddComponent<ScrollRect>();

            GameObject viewport = CreateRect(templateRect, ViewportName, Vector2.zero, "Stretch");
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.offsetMin = new Vector2(4f, 4f);
            viewportRect.offsetMax = new Vector2(-4f, -4f);
            viewport.AddComponent<RectMask2D>();

            GameObject content = CreateRect(viewportRect, ContentName, new Vector2(0f, 44f), "StretchTop");
            var contentRect = (RectTransform)content.transform;

            GameObject item = CreateRect(contentRect, ItemName, new Vector2(0f, 44f), "StretchHorizontal");
            var itemToggle = item.AddComponent<Toggle>();
            itemToggle.transition = Selectable.Transition.None;
            GameObject itemBg = CreateRect((RectTransform)item.transform, ItemBackgroundName, Vector2.zero, "Stretch");
            var itemBgRect = (RectTransform)itemBg.transform;
            itemBgRect.offsetMin = new Vector2(2f, 2f);
            itemBgRect.offsetMax = new Vector2(-2f, -2f);
            NeoShape itemBgShape = AddShape(itemBg, ShapeType.RoundedRect, 6f, fillToken: TokenSurfaceElevated);
            GameObject itemCheck = CreateRect((RectTransform)item.transform, ItemCheckmarkName, new Vector2(18f, 18f), "Left");
            ((RectTransform)itemCheck.transform).anchoredPosition = new Vector2(14f, 0f);
            NeoShape itemCheckShape = AddShape(itemCheck, ShapeType.Checkmark, fillToken: TokenPrimary);
            TextMeshProUGUI itemLabel = CreateLabel((RectTransform)item.transform, "Option", TokenTextDefault,
                fontSize: 22f, name: ItemLabelName, alignment: TextAlignmentOptions.MidlineLeft,
                textStyle: TextStyleBody);
            ((RectTransform)itemLabel.transform).offsetMin = new Vector2(44f, 0f);
            itemToggle.targetGraphic = itemBgShape;
            itemToggle.graphic = itemCheckShape;

            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            dropdown.template = templateRect;
            template.SetActive(false);

            if (options != null && options.Count > 0)
            {
                var list = new List<string>(options);
                dropdown.ClearOptions();
                dropdown.AddOptions(list);
            }
            dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, Mathf.Max(0, dropdown.options.Count - 1)));
            dropdown.RefreshShownValue();

            WithLayoutSize(go, -1f, 48f);
            return go;
        }

        // ------------------------------------------------------------------ menu rows

        // row child names — the exporter / presenter find the control + label by these
        public const string RowControlName = "Control";
        public const string RowControlSlotName = "ControlSlot";
        public const string ResetButtonName = "Reset";
        public const string RebindButtonName = "Rebind";

        /// <summary>
        /// Fixed control-column width: every row's control shares the same left/right edges. Sized as a
        /// CONTROL column, not the whole row — the flexible label takes the remaining width. Must stay
        /// well under a row's width or the label is starved and word-wraps ("Master\nVolume").
        /// </summary>
        public const float RowControlWidth = 200f;

        /// <summary>
        /// A settings/cheats row: a flexible left label + the control in a FIXED-width right column.
        /// (Giving controls flexible widths instead would let each label's text length shift its
        /// control's left edge — ragged checkbox/slider columns across rows.) Wide controls stretch
        /// across the slot; intrinsically sized ones (toggle box, switch pill) pin to its left edge.
        /// </summary>
        public static GameObject CreateMenuRow(RectTransform parent, string rowName, string label,
            GameObject control)
        {
            GameObject go = CreateRect(parent, rowName, new Vector2(480f, 48f));
            var row = go.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 4, 4);
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            TextMeshProUGUI labelText = CreateLabel((RectTransform)go.transform, label, TokenTextDefault,
                fontSize: 22f, name: LabelName, alignment: TextAlignmentOptions.MidlineLeft, textStyle: TextStyleBody);
            // single-line: a row label must never break a word ("Master\nVolume") when the panel is
            // narrow — it stays on one line and the flexible width gives it whatever space is left of
            // the fixed control column. Ellipsis (not Overflow) keeps a too-narrow label from spilling
            // over its control.
            labelText.enableWordWrapping = false;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            var labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;
            labelText.raycastTarget = false;

            if (control != null)
            {
                GameObject slot = CreateRect((RectTransform)go.transform, RowControlSlotName, new Vector2(RowControlWidth, 40f));
                WithLayoutSize(slot, RowControlWidth, 40f, flexibleWidth: 0f, flexibleHeight: 0f);

                control.name = RowControlName;
                var controlRect = (RectTransform)control.transform;
                controlRect.SetParent(slot.transform, worldPositionStays: false);
                if (control.GetComponent<UIToggle>() != null)
                {
                    // toggle box / switch pill keep their intrinsic size at the column's left edge;
                    // a toggle's VISIBLE box sits ToggleBoxInset inside its rect — shift so the box
                    // itself (not the invisible rect) lines up with the sliders and dropdowns
                    bool isToggleBox = control.transform.Find(BoxName) != null;
                    controlRect.anchorMin = new Vector2(0f, 0.5f);
                    controlRect.anchorMax = new Vector2(0f, 0.5f);
                    controlRect.pivot = new Vector2(0f, 0.5f);
                    controlRect.anchoredPosition = new Vector2(isToggleBox ? -ToggleBoxInset : 0f, 0f);
                }
                else
                {
                    // slider / dropdown / stepper / button fill the column
                    float height = controlRect.sizeDelta.y;
                    controlRect.anchorMin = new Vector2(0f, 0.5f);
                    controlRect.anchorMax = new Vector2(1f, 0.5f);
                    controlRect.pivot = new Vector2(0.5f, 0.5f);
                    controlRect.sizeDelta = new Vector2(0f, height);
                    controlRect.anchoredPosition = Vector2.zero;
                }
            }
            WithLayoutSize(go, -1f, 48f, flexibleHeight: 0f);
            return go;
        }

        // ------------------------------------------------------------------ layout containers

        /// <summary>
        /// Builds the layout-group <see cref="RectOffset"/> for a container. The uniform
        /// <paramref name="padding"/> is the default; when the optional per-side
        /// <paramref name="padding4"/> (spec array order [left, top, right, bottom]) is supplied it
        /// WINS. Note the side reorder: Unity's RectOffset ctor is (left, right, top, bottom), so the
        /// spec's [left, top, right, bottom] maps to (p[0], p[2], p[1], p[3]).
        /// </summary>
        private static RectOffset MakePadding(float padding, float[] padding4)
        {
            if (padding4 != null && padding4.Length >= 4)
                return new RectOffset(
                    Mathf.RoundToInt(padding4[0]),  // left
                    Mathf.RoundToInt(padding4[2]),  // right
                    Mathf.RoundToInt(padding4[1]),  // top
                    Mathf.RoundToInt(padding4[3])); // bottom
            int pad = Mathf.RoundToInt(padding);
            return new RectOffset(pad, pad, pad, pad);
        }

        public static GameObject CreateStack(RectTransform parent, bool vertical, float padding, float spacing,
            float[] padding4 = null)
        {
            GameObject go = CreateRect(parent, vertical ? "VStack" : "HStack", new Vector2(400f, 400f));
            HorizontalOrVerticalLayoutGroup layout = vertical
                ? go.AddComponent<VerticalLayoutGroup>()
                : (HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = MakePadding(padding, padding4);
            layout.spacing = spacing;
            ConfigureStackSizing(layout, vertical);
            return go;
        }

        /// <summary>
        /// Stacks control the cross axis and size the main axis from preferred sizes (factory
        /// widgets carry LayoutElement preferred heights; text reports its own) — never
        /// force-expand the main axis, or everything smears across the container.
        /// </summary>
        private static void ConfigureStackSizing(HorizontalOrVerticalLayoutGroup layout, bool vertical)
        {
            // both axes child-controlled: sized elements ride min=preferred LayoutElements, and
            // "flex" children can absorb leftover width — bars fill any aspect ratio instead of
            // relying on authored pixel sums (uncontrolled width ignores flexibleWidth entirely)
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = vertical;
            layout.childForceExpandHeight = false;
            if (!vertical) layout.childAlignment = TextAnchor.MiddleLeft;
        }

        /// <summary>
        /// A <see cref="UIPanel"/>: a name-addressed container (vertical layout, own CanvasGroup) that a
        /// <see cref="UITab"/> shows/hides. The tab↔panel link and start visibility are wired by the
        /// generator once every sibling is built.
        /// </summary>
        public static GameObject CreatePanel(RectTransform parent, string category, string name,
            float padding, float spacing, float[] padding4 = null)
        {
            GameObject go = CreateRect(parent, "Panel", new Vector2(480f, 480f));
            go.AddComponent<CanvasGroup>();
            var panel = go.AddComponent<UIPanel>();
            panel.id = new PanelId(category, name);
            // a hidden tab panel must vacate the layout (and actually disappear) — the base container
            // only toggles raycasts/alpha-via-animator, and panels carry no animator, so drive
            // visibility by activating the GameObject; the controlling tab pushes the state.
            panel.disableGameObjectWhenHidden = true;

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = MakePadding(padding, padding4);
            layout.spacing = spacing;
            ConfigureStackSizing(layout, vertical: true);
            return go;
        }

        public static GameObject CreateGrid(RectTransform parent, float padding, float spacing,
            int columns, Vector2 cellSize, float[] padding4 = null)
        {
            GameObject go = CreateRect(parent, "Grid", new Vector2(400f, 400f));
            var layout = go.AddComponent<GridLayoutGroup>();
            layout.padding = MakePadding(padding, padding4);
            layout.spacing = new Vector2(spacing, spacing);
            layout.cellSize = cellSize;
            if (columns > 0)
            {
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = columns;
            }
            return go;
        }

        /// <summary>
        /// Gives a widget its design size inside layout groups (stacks control children from
        /// preferred sizes). Pass -1 to leave an axis layout-driven; flexibleWidth 0 pins the
        /// width even when the stack force-expands.
        /// </summary>
        public static LayoutElement WithLayoutSize(GameObject go, float preferredWidth,
            float preferredHeight, float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            var layoutElement = go.GetComponent<LayoutElement>();
            if (layoutElement == null) layoutElement = go.AddComponent<LayoutElement>();
            if (preferredWidth > 0f) layoutElement.preferredWidth = preferredWidth;
            if (preferredHeight > 0f) layoutElement.preferredHeight = preferredHeight;
            if (flexibleWidth >= 0f) layoutElement.flexibleWidth = flexibleWidth;
            if (flexibleHeight >= 0f) layoutElement.flexibleHeight = flexibleHeight;
            return layoutElement;
        }

        // ------------------------------------------------------------------ animation helpers

        private static void ConfigureFade(UIAnimation animation, float from, float to)
        {
            animation.fade.enabled = true;
            animation.fade.fromReference = ReferenceValue.CustomValue;
            animation.fade.toReference = ReferenceValue.CustomValue;
            animation.fade.fromCustomValue = from;
            animation.fade.toCustomValue = to;
            animation.fade.settings.duration = 0.15f;
            animation.fade.settings.ease = Ease.OutQuad;
        }

        private static void ConfigureMove(UIAnimation animation, Vector3 from, Vector3 to,
            float duration = 0.18f, Ease ease = Ease.OutCubic)
        {
            animation.move.enabled = true;
            animation.move.fromDirection = UIMoveDirection.CustomPosition;
            animation.move.toDirection = UIMoveDirection.CustomPosition;
            animation.move.fromReference = ReferenceValue.CustomValue;
            animation.move.toReference = ReferenceValue.CustomValue;
            animation.move.fromCustomValue = from;
            animation.move.toCustomValue = to;
            animation.move.settings.duration = duration;
            animation.move.settings.ease = ease;
        }
    }

    /// <summary>
    /// The bidirectional converter between the spec's <see cref="LayoutSpec"/> and a RectTransform's
    /// anchor/pivot/offset configuration, plus the per-child sizing pass. This is the single owner of
    /// the constraint write path (the generator calls <see cref="Apply"/>, the exporter calls
    /// <see cref="Detect"/>). Constraint and sizing kinds come from the <see cref="LayoutConstraints"/>
    /// / <see cref="LayoutSizingModes"/> registries — adding a kind never touches this class.
    /// </summary>
    public static class ConstraintLayout
    {
        public const string DefaultH = LayoutConstraints.Left;
        public const string DefaultV = LayoutConstraints.Top;

        /// <summary>
        /// The 16 legacy anchor presets re-expressed as (horizontal, vertical) constraint pairs. The
        /// legacy presets are NOT deleted (TryApplyAnchor/DetectAnchor stay for un-migrated specs);
        /// this map is what the opt-in migration pass (A4) uses to rewrite a preset into the
        /// equivalent constraint model. Edge presets map to edge constraints; the Stretch family maps
        /// to leftRight/topBottom on the stretched axis.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, (string h, string v)> PresetConstraints =
            new Dictionary<string, (string h, string v)>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["TopLeft"] = (LayoutConstraints.Left, LayoutConstraints.Top),
                ["Top"] = (LayoutConstraints.Center, LayoutConstraints.Top),
                ["TopRight"] = (LayoutConstraints.Right, LayoutConstraints.Top),
                ["Left"] = (LayoutConstraints.Left, LayoutConstraints.Center),
                ["Center"] = (LayoutConstraints.Center, LayoutConstraints.Center),
                ["Right"] = (LayoutConstraints.Right, LayoutConstraints.Center),
                ["BottomLeft"] = (LayoutConstraints.Left, LayoutConstraints.Bottom),
                ["Bottom"] = (LayoutConstraints.Center, LayoutConstraints.Bottom),
                ["BottomRight"] = (LayoutConstraints.Right, LayoutConstraints.Bottom),
                ["Stretch"] = (LayoutConstraints.LeftRight, LayoutConstraints.TopBottom),
                ["StretchTop"] = (LayoutConstraints.LeftRight, LayoutConstraints.Top),
                ["StretchBottom"] = (LayoutConstraints.LeftRight, LayoutConstraints.Bottom),
                ["StretchLeft"] = (LayoutConstraints.Left, LayoutConstraints.TopBottom),
                ["StretchRight"] = (LayoutConstraints.Right, LayoutConstraints.TopBottom),
                ["StretchHorizontal"] = (LayoutConstraints.LeftRight, LayoutConstraints.Center),
                ["StretchVertical"] = (LayoutConstraints.Center, LayoutConstraints.TopBottom)
            };

        /// <summary>
        /// Applies a <see cref="LayoutSpec"/> to <paramref name="rect"/>, stamping a
        /// <see cref="NeoLayoutTag"/> so the exporter can reverse-map deterministically. Placement
        /// (constraints) applies to a FREE element; per-child sizing applies when the element is a
        /// child of a layout group (<paramref name="parentLayout"/> non-null). Returns the tag.
        /// </summary>
        public static NeoLayoutTag Apply(RectTransform rect, LayoutSpec layout,
            HorizontalOrVerticalLayoutGroup parentLayout)
        {
            if (rect == null || layout == null) return null;

            var tag = rect.GetComponent<NeoLayoutTag>();
            if (tag == null) tag = rect.gameObject.AddComponent<NeoLayoutTag>();

            bool inLayout = parentLayout != null;

            if (!inLayout)
                ApplyConstraints(rect, layout, tag);
            else
                StampConstraintDefaults(tag);

            ApplySizing(rect.gameObject, layout, parentLayout, tag);
            return tag;
        }

        private static void StampConstraintDefaults(NeoLayoutTag tag)
        {
            // a layout-group child has no free constraint; record nothing placement-wise so the
            // exporter doesn't emit a spurious h/v/offset for it.
            tag.h = null;
            tag.v = null;
            tag.widthSize = -1f;
            tag.heightSize = -1f;
        }

        private static void ApplyConstraints(RectTransform rect, LayoutSpec layout, NeoLayoutTag tag)
        {
            string hId = string.IsNullOrEmpty(layout.h) ? DefaultH : layout.h;
            string vId = string.IsNullOrEmpty(layout.v) ? DefaultV : layout.v;

            ILayoutConstraint hc = LayoutConstraints.Get(hId, LayoutAxis.Horizontal);
            ILayoutConstraint vc = LayoutConstraints.Get(vId, LayoutAxis.Vertical);

            float? width = layout.size != null ? layout.size.w : null;
            float? height = layout.size != null ? layout.size.h : null;

            LayoutOffsetValue hOff = ResolveOffset(layout, hc, LayoutAxis.Horizontal);
            LayoutOffsetValue vOff = ResolveOffset(layout, vc, LayoutAxis.Vertical);

            if (hc != null) hc.Apply(rect, hOff, hc.Stretches ? null : width);
            if (vc != null) vc.Apply(rect, vOff, vc.Stretches ? null : height);

            tag.h = hId;
            tag.v = vId;
            tag.hOffset0 = hOff.primary;
            tag.hOffset1 = hOff.secondary;
            tag.vOffset0 = vOff.primary;
            tag.vOffset1 = vOff.secondary;
            tag.widthSize = (hc != null && !hc.Stretches && width.HasValue) ? width.Value : -1f;
            tag.heightSize = (vc != null && !vc.Stretches && height.HasValue) ? height.Value : -1f;
        }

        /// <summary> Reads the offset for an axis out of the constraint-keyed offset dict. </summary>
        private static LayoutOffsetValue ResolveOffset(LayoutSpec layout, ILayoutConstraint constraint,
            LayoutAxis axis)
        {
            LayoutOffset off = layout.offset;
            if (constraint == null || off == null) return default;

            switch (constraint.Id)
            {
                case LayoutConstraints.Left: return new LayoutOffsetValue(off.GetOr("left", 0f));
                case LayoutConstraints.Right: return new LayoutOffsetValue(off.GetOr("right", 0f));
                case LayoutConstraints.Top: return new LayoutOffsetValue(off.GetOr("top", 0f));
                case LayoutConstraints.Bottom: return new LayoutOffsetValue(off.GetOr("bottom", 0f));
                case LayoutConstraints.Center:
                    return new LayoutOffsetValue(off.GetOr(axis == LayoutAxis.Horizontal ? "h" : "v", 0f));
                case LayoutConstraints.LeftRight:
                    return new LayoutOffsetValue(off.GetOr("left", 0f), off.GetOr("right", 0f));
                case LayoutConstraints.TopBottom:
                    return new LayoutOffsetValue(off.GetOr("top", 0f), off.GetOr("bottom", 0f));
                case LayoutConstraints.Scale:
                    return axis == LayoutAxis.Horizontal
                        ? new LayoutOffsetValue(off.GetOr("left", 0f), off.GetOr("right", 1f))
                        : new LayoutOffsetValue(off.GetOr("bottom", 0f), off.GetOr("top", 1f));
                default:
                    // project constraint: feed primary/secondary from generic axis keys
                    return axis == LayoutAxis.Horizontal
                        ? new LayoutOffsetValue(off.GetOr("left", 0f), off.GetOr("right", 0f))
                        : new LayoutOffsetValue(off.GetOr("bottom", 0f), off.GetOr("top", 0f));
            }
        }

        /// <summary> Per-child Fixed/Hug/Fill, driving LayoutElement/ContentSizeFitter + group force-expand. </summary>
        private static void ApplySizing(GameObject go, LayoutSpec layout,
            HorizontalOrVerticalLayoutGroup parentLayout, NeoLayoutTag tag)
        {
            if (layout.sizing == null || layout.sizing.IsEmpty || parentLayout == null) return;

            float? width = layout.size != null ? layout.size.w : null;
            float? height = layout.size != null ? layout.size.h : null;

            if (!string.IsNullOrEmpty(layout.sizing.w))
            {
                ILayoutSizingMode mode = LayoutSizingModes.Get(layout.sizing.w);
                if (mode != null)
                {
                    mode.Apply(go, horizontal: true, width);
                    if (mode.WantsForceExpand) UIWidgetFactory.ForceExpandAxis(parentLayout, horizontal: true);
                    tag.sizingW = mode.Id;
                }
            }
            if (!string.IsNullOrEmpty(layout.sizing.h))
            {
                ILayoutSizingMode mode = LayoutSizingModes.Get(layout.sizing.h);
                if (mode != null)
                {
                    mode.Apply(go, horizontal: false, height);
                    if (mode.WantsForceExpand) UIWidgetFactory.ForceExpandAxis(parentLayout, horizontal: false);
                    tag.sizingH = mode.Id;
                }
            }
        }

        /// <summary>
        /// Reverse-maps a tagged RectTransform into a <see cref="LayoutSpec"/>. Reads the
        /// <see cref="NeoLayoutTag"/> for the resolved constraint ids (anchors alias, so the tag is
        /// authoritative), then re-detects offsets/sizing from the live RectTransform so hand edits
        /// to the prefab still round-trip. Returns null when the tag carries nothing.
        /// </summary>
        public static LayoutSpec Detect(RectTransform rect, NeoLayoutTag tag)
        {
            if (rect == null || tag == null) return null;
            var spec = new LayoutSpec();

            if (!string.IsNullOrEmpty(tag.h) || !string.IsNullOrEmpty(tag.v))
            {
                var offset = new LayoutOffset();
                var size = new LayoutSize();

                DetectAxis(rect, tag.h, LayoutAxis.Horizontal, spec, offset, size);
                DetectAxis(rect, tag.v, LayoutAxis.Vertical, spec, offset, size);

                if (!offset.IsEmpty) spec.offset = offset;
                if (!size.IsEmpty) spec.size = size;
            }

            if (!string.IsNullOrEmpty(tag.sizingW) || !string.IsNullOrEmpty(tag.sizingH))
            {
                var sizing = new LayoutSizing();
                if (DetectSizing(rect.gameObject, tag.sizingW, horizontal: true, out string wMode)) sizing.w = wMode;
                if (DetectSizing(rect.gameObject, tag.sizingH, horizontal: false, out string hMode)) sizing.h = hMode;
                if (!sizing.IsEmpty) spec.sizing = sizing;

                // "fixed" sizing carries an authored extent (min=preferred): round-trip it through
                // layout.size so a regenerate re-applies the same rigid size.
                var le = rect.GetComponent<LayoutElement>();
                if (le != null)
                {
                    LayoutSize size = spec.size ?? new LayoutSize();
                    if (sizing.w == LayoutSizingModes.Fixed && le.preferredWidth > 0f) size.w = le.preferredWidth;
                    if (sizing.h == LayoutSizingModes.Fixed && le.preferredHeight > 0f) size.h = le.preferredHeight;
                    if (!size.IsEmpty) spec.size = size;
                }
            }

            return spec.IsEmpty ? null : spec;
        }

        private static void DetectAxis(RectTransform rect, string constraintId, LayoutAxis axis,
            LayoutSpec spec, LayoutOffset offset, LayoutSize size)
        {
            if (string.IsNullOrEmpty(constraintId)) return;
            ILayoutConstraint c = LayoutConstraints.Get(constraintId, axis);
            if (c == null) return;

            if (axis == LayoutAxis.Horizontal) spec.h = constraintId;
            else spec.v = constraintId;

            if (!c.TryDetect(rect, out LayoutOffsetValue value, out float? detectedSize)) return;

            WriteOffset(offset, c, axis, value);
            if (!c.Stretches && detectedSize.HasValue && detectedSize.Value > 0f)
            {
                if (axis == LayoutAxis.Horizontal) size.w = detectedSize.Value;
                else size.h = detectedSize.Value;
            }
        }

        private static void WriteOffset(LayoutOffset offset, ILayoutConstraint constraint, LayoutAxis axis,
            LayoutOffsetValue value)
        {
            switch (constraint.Id)
            {
                case LayoutConstraints.Left: offset.Set("left", value.primary); break;
                case LayoutConstraints.Right: offset.Set("right", value.primary); break;
                case LayoutConstraints.Top: offset.Set("top", value.primary); break;
                case LayoutConstraints.Bottom: offset.Set("bottom", value.primary); break;
                case LayoutConstraints.Center:
                    offset.Set(axis == LayoutAxis.Horizontal ? "h" : "v", value.primary); break;
                case LayoutConstraints.LeftRight:
                    offset.Set("left", value.primary); offset.Set("right", value.secondary); break;
                case LayoutConstraints.TopBottom:
                    offset.Set("top", value.primary); offset.Set("bottom", value.secondary); break;
                case LayoutConstraints.Scale:
                    if (axis == LayoutAxis.Horizontal) { offset.Set("left", value.primary); offset.Set("right", value.secondary); }
                    else { offset.Set("bottom", value.primary); offset.Set("top", value.secondary); }
                    break;
                default:
                    if (axis == LayoutAxis.Horizontal) { offset.Set("left", value.primary); offset.Set("right", value.secondary); }
                    else { offset.Set("bottom", value.primary); offset.Set("top", value.secondary); }
                    break;
            }
        }

        private static bool DetectSizing(GameObject go, string declaredMode, bool horizontal, out string detected)
        {
            detected = null;
            if (string.IsNullOrEmpty(declaredMode)) return false;
            // Prefer the declared mode if it still matches; else re-detect in registry order (a hand
            // edit may have changed it). Keeps round-trip honest without aliasing.
            ILayoutSizingMode declared = LayoutSizingModes.Get(declaredMode);
            if (declared != null && declared.TryDetect(go, horizontal)) { detected = declared.Id; return true; }
            foreach (ILayoutSizingMode mode in LayoutSizingModes.All)
                if (mode.TryDetect(go, horizontal)) { detected = mode.Id; return true; }
            // nothing matches anymore (edit cleared it): drop the field rather than lie.
            return false;
        }
    }
}
