using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Lints the package's assets — theme tokens vs bound targets, popup database, preset database,
    /// flow graphs vs view/button databases — and reports broken references as plain text.
    /// Callable from the menu, from code, or from batch mode / CI via
    /// <c>-executeMethod Neo.UI.Editor.AgentValidation.ValidateFromBatchMode</c>.
    /// </summary>
    public static class AgentValidation
    {
        [MenuItem("Tools/Neo UI/Advanced/Validate", priority = 12)]
        public static void ValidateMenu()
        {
            List<string> issues = ValidateAll();
            string message = issues.Count == 0
                ? "Validation passed — no issues found."
                : string.Join("\n", issues);
            Debug.Log($"[Neo.UI] Validation:\n{message}");
            EditorUtility.DisplayDialog("Neo UI Validation", message, "OK");
        }

        /// <summary> Batch-mode entry point: exits 0 when clean, 1 when issues are found. </summary>
        public static void ValidateFromBatchMode()
        {
            List<string> issues = ValidateAll();
            foreach (string issue in issues) Console.WriteLine($"[VALIDATION] {issue}");
            EditorApplication.Exit(issues.Count == 0 ? 0 : 1);
        }

        public static List<string> ValidateAll()
        {
            var issues = new List<string>();

            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null)
            {
                issues.Add("No NeoUISettings asset in a Resources folder — run Tools/Neo UI/Create or Repair Settings");
                return issues;
            }

            ValidateTheme(settings, issues);
            ValidatePopupDatabase(settings, issues);
            ValidatePresetDatabase(settings, issues);
            ValidateGeneratedViews(settings, issues);
            ValidateFlowGraphs(settings, issues);
            ValidateInteractivity(settings, issues);
            ValidateMenuBindings(issues);

            // Project-registered HARD rules run AFTER the built-in contracts. The built-ins above
            // always run regardless of registration, so a missing registration can never weaken a
            // hard contract (per the validation-rules plan). Design/Interactivity rules NEVER run
            // here — they belong to ValidateDesign so this list stays the hard-fail set.
            NeoValidationRules.Run(ValidationBucket.Hard,
                new ValidationContext(settings, GeneratedViewPrefabs(), issues));

            return issues;
        }

        /// <summary>
        /// Design lint (soft warnings, separate from <see cref="ValidateAll"/> so they never fail
        /// builds): WCAG contrast for the theme's text/surface token pairs, raw font sizes where
        /// a text style exists, and off-scale container spacing (scale: 4/8/12/16/24/32/48/64).
        /// </summary>
        public static List<string> ValidateDesign()
        {
            var warnings = new List<string>();
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null || settings.theme == null) return warnings;
            Theme theme = settings.theme;

            // ---- contrast: text token on surface token, per variant (WCAG AA-ish)
            // Pairs come from settings so a project on a different token scheme can override them;
            // an empty list falls back to the package defaults below (defaults live editor-side
            // because they reference the editor's UIWidgetFactory token-name constants).
            (string text, string surface, float minimum)[] pairs = ContrastPairs(settings);
            foreach (Theme.ThemeVariant variant in theme.Variants)
            {
                foreach ((string text, string surface, float minimum) pair in pairs)
                {
                    if (!variant.TryGetColor(pair.text, out Color textColor)) continue;
                    if (!variant.TryGetColor(pair.surface, out Color surfaceColor)) continue;
                    float ratio = ColorUtils.ContrastRatio(textColor, surfaceColor);
                    if (ratio < pair.minimum)
                        warnings.Add($"Variant '{variant.name}': '{pair.text}' on '{pair.surface}' " +
                                     $"contrast {ratio:0.0}:1 is below {pair.minimum:0.0}:1");
                }
            }

            bool hasTextStyles = theme.TextStyles.Count > 0;
            float[] scale = SpacingScale(settings);
            bool OnScale(float value) => scale.Any(s => Mathf.Approximately(s, value));

            // Cost honesty for shape effects / UI particles: one warning per DISTINCT Tier-2 variant
            // (each is its own shared material = one extra batch break, not per instance).
            var warnedVariants = new HashSet<string>();

            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                if (prefab.GetComponent<UIView>() == null) continue;

                // icons that resolve by name but can't actually RENDER (glyph missing from the icon
                // font atlas + fallbacks, or a sprite name absent from its sprite asset) draw tofu
                foreach (NeoIcon icon in prefab.GetComponentsInChildren<NeoIcon>(true))
                {
                    if (string.IsNullOrEmpty(icon.icon)) continue;
                    if (!IconMap.TryResolveIcon(icon.icon, out ResolvedIcon resolved))
                    {
                        warnings.Add($"'{prefab.name}': icon '{icon.icon}' no longer resolves " +
                                     "(removed overlay entry?) — it will keep its baked visual but can't re-bake");
                        continue;
                    }
                    if (resolved.isSprite)
                    {
                        if (resolved.spriteAsset == null ||
                            resolved.spriteAsset.GetSpriteIndexFromName(resolved.spriteName) < 0)
                            warnings.Add($"'{prefab.name}': sprite icon '{icon.icon}' has no sprite " +
                                         $"character '{resolved.spriteName}' in its sprite asset — it will render blank");
                    }
                    else if (settings.iconFont != null && !settings.iconFont.HasCharacter(resolved.glyph, true))
                    {
                        warnings.Add($"'{prefab.name}': icon '{icon.icon}' glyph U+{(int)resolved.glyph:X4} " +
                                     "is not in the icon font atlas or its fallbacks — it will render as a square " +
                                     "(re-run Tools → Neo UI → Setup → Create or Repair Fonts)");
                    }
                }

                if (hasTextStyles)
                {
                    foreach (TMPro.TMP_Text text in prefab.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    {
                        if (text.GetComponent<ThemeTextStyleTarget>() != null) continue;
                        if (settings.iconFont != null && text.font == settings.iconFont) continue;
                        // factory-owned internals (input text/placeholder, badges, steppers) stay raw
                        if (text.GetComponentInParent<TMPro.TMP_InputField>(true) != null) continue;
                        if (text.GetComponentInParent<UIStepper>(true) != null) continue;
                        if (text.GetComponentInParent<UIBadge>(true) != null) continue;
                        warnings.Add($"'{prefab.name}/{text.name}' uses raw fontSize {text.fontSize:0} — " +
                                     "bind a theme textStyle instead");
                    }
                }

                // spacing scale only on agent-authored containers (generated names are kind_index or ids)
                foreach (UnityEngine.UI.HorizontalOrVerticalLayoutGroup group in
                         prefab.GetComponentsInChildren<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(true))
                {
                    string name = group.gameObject.name;
                    if (!name.StartsWith("vstack") && !name.StartsWith("hstack")) continue;
                    if (!OnScale(group.spacing))
                        warnings.Add($"'{prefab.name}/{name}' spacing {group.spacing:0.#} is off the " +
                                     "4/8/12/16/24/32/48/64 scale");
                    if (!OnScale(group.padding.left))
                        warnings.Add($"'{prefab.name}/{name}' padding {group.padding.left} is off the " +
                                     "4/8/12/16/24/32/48/64 scale");
                }

                // interactive elements should show something and be comfortably tappable
                foreach (UnityEngine.UI.Selectable selectable in
                         prefab.GetComponentsInChildren<UnityEngine.UI.Selectable>(true))
                {
                    bool isButtonOrTab = selectable is UIButton || selectable is UITab;
                    if (!isButtonOrTab && !(selectable is UIToggle)) continue;
                    if (selectable.GetComponentInParent<UIStepper>(true) != null) continue; // stepper internals

                    // a button/tab with neither label text nor an icon glyph is a mystery click target
                    if (isButtonOrTab)
                    {
                        bool hasContent = selectable.GetComponentsInChildren<TMPro.TMP_Text>(true)
                            .Any(t => !string.IsNullOrWhiteSpace(t.text));
                        if (!hasContent)
                            warnings.Add($"'{prefab.name}/{selectable.name}' is interactive but shows no label or icon");
                    }

                    // tap target — only when the element sizes itself (fixed anchors), so a layout
                    // group driving the size can't produce false positives
                    var rt = (RectTransform)selectable.transform;
                    if (rt.anchorMin == rt.anchorMax)
                    {
                        Vector2 size = rt.sizeDelta;
                        if (size.x > 0f && size.y > 0f && Mathf.Min(size.x, size.y) < 44f)
                            warnings.Add($"'{prefab.name}/{selectable.name}' tap target {size.x:0}×{size.y:0}px " +
                                         "is below the 44px mobile minimum");
                    }
                }

                // Tier-2 material variants break the shared NeoShape batch — warn once per distinct
                // variant (BatchSafe==false is the deliberate, named split, shared per definition).
                foreach (NeoShapeVariant variant in prefab.GetComponentsInChildren<NeoShapeVariant>(true))
                {
                    string effectId = variant.EffectId;
                    if (string.IsNullOrEmpty(effectId)) effectId = variant.name;
                    if (warnedVariants.Add(effectId))
                        warnings.Add($"Tier-2 shape effect variant '{effectId}' uses its own shared " +
                                     "material — it breaks the single-NeoShape batch (one extra draw call per variant)");
                }

                // Continuous (rate>0) or high-capacity emitters do per-frame work — flag the cost.
                foreach (NeoParticleEmitter emitter in prefab.GetComponentsInChildren<NeoParticleEmitter>(true))
                {
                    var so = new SerializedObject(emitter);
                    float rate = so.FindProperty("rate").floatValue;
                    int capacity = so.FindProperty("capacity").intValue;
                    if (rate > 0f)
                        warnings.Add($"'{prefab.name}/{emitter.name}' particle emitter is continuous " +
                                     $"(rate {rate:0.#}/s) — it does per-frame work; prefer a one-shot burst for UI");
                    else if (capacity > ParticleCapacityWarn)
                        warnings.Add($"'{prefab.name}/{emitter.name}' particle capacity {capacity} is high " +
                                     $"(> {ParticleCapacityWarn}) — each live particle is a pooled NeoShape GameObject");
                }

                ValidateForceExpandStomp(prefab, warnings);
                ValidateImageAspect(prefab, warnings);
            }

            ValidateWidgetContrast(settings, warnings);

            // Project-registered DESIGN rules run after the built-in soft checks, into the same
            // warnings list. They are surfaced as designWarnings and never reach ValidateAll.
            NeoValidationRules.Run(ValidationBucket.Design,
                new ValidationContext(settings, GeneratedViewPrefabs(), warnings));

            return warnings;
        }

        // ── Design-lint config seam (read from NeoUISettings; the project's single source of truth) ─

        /// <summary> Emitter capacity above which a burst is flagged as costly (each particle is a pooled GameObject). </summary>
        private const int ParticleCapacityWarn = 128;

        /// <summary>
        /// Width:height (or height:width) ratio above which an image's full-rect sprite fill is
        /// considered extreme — a square-ish sprite stretches into a smear (fit:stretch) or crops to
        /// an awkward sliver (fit:cover) at this aspect.
        /// </summary>
        private const float ExtremeAspectRatio = 3f;

        /// <summary> Package-default contrast pairs (used when settings.contrastPairs is empty). </summary>
        private static readonly (string text, string surface, float minimum)[] DefaultContrastPairs =
        {
            (UIWidgetFactory.TokenTextStrong, UIWidgetFactory.TokenSurface, 4.5f),
            (UIWidgetFactory.TokenTextDefault, UIWidgetFactory.TokenSurface, 4.5f),
            (UIWidgetFactory.TokenTextDefault, UIWidgetFactory.TokenBackground, 4.5f),
            (UIWidgetFactory.TokenTextMuted, UIWidgetFactory.TokenSurface, 3f),
            // button labels are 24px semibold = WCAG large text, so 3:1
            (UIWidgetFactory.TokenTextOnPrimary, UIWidgetFactory.TokenPrimary, 3f)
        };

        /// <summary> The blessed spacing scale (settings override, else the package default). </summary>
        private static float[] SpacingScale(NeoUISettings settings)
        {
            if (settings != null && settings.spacingScale != null && settings.spacingScale.Length > 0)
                return settings.spacingScale;
            return new[] { 0f, 4f, 8f, 12f, 16f, 24f, 32f, 48f, 64f };
        }

        private static (string text, string surface, float minimum)[] ContrastPairs(NeoUISettings settings)
        {
            if (settings != null && settings.contrastPairs != null && settings.contrastPairs.Length > 0)
                return settings.contrastPairs
                    .Select(p => (p.text, p.surface, p.minimum))
                    .ToArray();
            return DefaultContrastPairs;
        }

        private static float TextContrastMinimum(NeoUISettings settings) =>
            settings != null && settings.textContrastMin > 0f ? settings.textContrastMin : 3f;

        private static float AffordanceContrastMinimum(NeoUISettings settings) =>
            settings != null && settings.affordanceContrastMin > 0f ? settings.affordanceContrastMin : 2f;

        /// <summary>
        /// Widget-level contrast lint: walks REAL generated widgets and checks the baked + state
        /// colors of pairs that actually sit on top of each other — every text/icon against its
        /// nearest opaque backdrop (including toggle/selectable state colors on either side), the
        /// slider handle against its track, the switch knob against its on/off track colors. The
        /// token-pair checks above only cover the naming conventions; this catches any theme ×
        /// widget combination that ships unreadable, no matter which tokens built it.
        /// </summary>
        private static void ValidateWidgetContrast(NeoUISettings settings, List<string> warnings)
        {
            float textContrastMinimum = TextContrastMinimum(settings);
            float affordanceContrastMinimum = AffordanceContrastMinimum(settings);
            var seen = new HashSet<string>(); // identical widgets repeat across views — report each pair once

            void Check(GameObject prefab, Component content, string contentState, Color contentColor,
                Component backdrop, string backdropState, Color backdropColor, float minimum)
            {
                if (contentColor.a < 0.9f || backdropColor.a < 0.9f) return; // composited result unknown
                float ratio = ColorUtils.ContrastRatio(contentColor, backdropColor);
                if (ratio >= minimum) return;
                string state = contentState ?? backdropState;
                string message = $"'{prefab.name}/{content.name}'{(state != null ? $" ({state})" : "")} contrast " +
                                 $"{ratio:0.0}:1 against '{backdrop.name}' is below {minimum:0.#}:1";
                if (seen.Add(message)) warnings.Add(message);
            }

            // a graphic's possible colors: toggle animator states, selectable states, or the baked color
            List<(string state, Color color)> States(GameObject go, Color baked)
            {
                var toggleColors = go.GetComponent<UIToggleColorAnimator>();
                if (toggleColors != null)
                    return new List<(string, Color)>
                        { ("on", toggleColors.onColor.Resolve()), ("off", toggleColors.offColor.Resolve()) };
                var selectableColors = go.GetComponent<UISelectableColorAnimator>();
                if (selectableColors != null)
                    return new List<(string, Color)>
                    {
                        ("normal", selectableColors.colors.normal.Resolve()),
                        ("highlighted", selectableColors.colors.highlighted.Resolve()),
                        ("pressed", selectableColors.colors.pressed.Resolve())
                    };
                return new List<(string, Color)> { (null, baked) };
            }

            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                if (prefab.GetComponent<UIView>() == null) continue;

                // every text/icon vs the nearest opaque graphic behind it
                foreach (TMPro.TMP_Text text in prefab.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    UnityEngine.UI.Graphic backdrop = null;
                    for (Transform parent = text.transform.parent; parent != null; parent = parent.parent)
                    {
                        UnityEngine.UI.Graphic graphic = parent.GetComponent<NeoShape>();
                        if (graphic == null && parent.GetComponent<UnityEngine.UI.Image>() != null)
                            graphic = parent.GetComponent<UnityEngine.UI.Image>();
                        if (graphic != null) { backdrop = graphic; break; }
                        if (parent == prefab.transform) break;
                    }
                    if (backdrop == null) continue;

                    var contentStates = States(text.gameObject, text.color);
                    var backdropStates = States(backdrop.gameObject, backdrop.color);
                    bool bothToggleDriven = text.GetComponent<UIToggleColorAnimator>() != null
                                            && backdrop.GetComponent<UIToggleColorAnimator>() != null;
                    if (bothToggleDriven)
                    {
                        // state-coupled pair (tab label + tab surface): only matching states co-occur
                        for (int i = 0; i < contentStates.Count; i++)
                            Check(prefab, text, contentStates[i].state, contentStates[i].color,
                                backdrop, backdropStates[i].state, backdropStates[i].color, textContrastMinimum);
                    }
                    else
                    {
                        foreach ((string contentState, Color contentColor) in contentStates)
                            foreach ((string backdropState, Color backdropColor) in backdropStates)
                                Check(prefab, text, contentState, contentColor,
                                    backdrop, backdropState, backdropColor, textContrastMinimum);
                    }
                }

                // slider handle vs its track
                foreach (UISlider slider in prefab.GetComponentsInChildren<UISlider>(true))
                {
                    NeoShape handle = slider.handleRect != null ? slider.handleRect.GetComponent<NeoShape>() : null;
                    NeoShape track = slider.transform.Find(UIWidgetFactory.TrackName)?.GetComponent<NeoShape>();
                    if (handle != null && track != null)
                        Check(prefab, handle, null, handle.color, track, null, track.color, affordanceContrastMinimum);
                }

                // switch knob vs its track, per state
                foreach (UIToggle toggle in prefab.GetComponentsInChildren<UIToggle>(true))
                {
                    Transform knob = toggle.transform.Find(UIWidgetFactory.KnobName);
                    Transform track = toggle.transform.Find(UIWidgetFactory.TrackName);
                    if (knob == null || track == null) continue;
                    var knobStates = States(knob.gameObject, knob.GetComponent<NeoShape>()?.color ?? Color.white);
                    var trackStates = States(track.gameObject, track.GetComponent<NeoShape>()?.color ?? Color.white);
                    int pairs = Mathf.Min(knobStates.Count, trackStates.Count);
                    for (int i = 0; i < pairs; i++)
                        Check(prefab, knob.GetComponent<NeoShape>(), knobStates[i].state, knobStates[i].color,
                            track.GetComponent<NeoShape>(), trackStates[i].state, trackStates[i].color,
                            affordanceContrastMinimum);
                }
            }
        }

        /// <summary>
        /// Layout-stomp lint: an element with an authored fixed extent (a <see cref="UnityEngine.UI.LayoutElement"/>
        /// carrying a real <c>preferredWidth</c>/<c>preferredHeight</c>) sitting as a direct child of a
        /// force-expanding layout group on that same axis is silently stretched to fill the group — its
        /// authored size is ignored, a WYSIWYG break (the layout/aspect defect class that shipped bad
        /// showcases). The opt-out is a <see cref="UnityEngine.UI.ContentSizeFitter"/> on that axis
        /// (<c>sizing:"fixed"</c>/<c>hug</c> add one), so an element that carries one has explicitly
        /// escaped force-expand and is NOT flagged. Both axes are checked symmetrically.
        /// </summary>
        private static void ValidateForceExpandStomp(GameObject prefab, List<string> warnings)
        {
            foreach (UnityEngine.UI.LayoutElement element in
                     prefab.GetComponentsInChildren<UnityEngine.UI.LayoutElement>(true))
            {
                Transform parent = element.transform.parent;
                if (parent == null) continue;
                var group = parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>();
                if (group == null) continue;

                var fitter = element.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                bool fitsWidth = fitter != null &&
                                 fitter.horizontalFit != UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
                bool fitsHeight = fitter != null &&
                                  fitter.verticalFit != UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;

                if (group.childForceExpandWidth && element.preferredWidth > 0f && !fitsWidth)
                    warnings.Add($"'{prefab.name}/{element.name}' has an authored width " +
                                 $"({element.preferredWidth:0}px) but its parent '{group.name}' force-expands " +
                                 "width — the width is ignored; use sizing:\"fixed\"/\"hug\" or a grid/hstack");

                if (group.childForceExpandHeight && element.preferredHeight > 0f && !fitsHeight)
                    warnings.Add($"'{prefab.name}/{element.name}' has an authored height " +
                                 $"({element.preferredHeight:0}px) but its parent '{group.name}' force-expands " +
                                 "height — the height is ignored; use sizing:\"fixed\"/\"hug\" or a grid/vstack");
            }
        }

        /// <summary>
        /// Aspect lint: an <c>image</c> element fills its whole rect with a sprite (an
        /// <see cref="NeoShape"/> texture fill, or a plain <see cref="UnityEngine.UI.Image"/>), so an
        /// extreme rect aspect (≳ <see cref="ExtremeAspectRatio"/>:1 either way) stretches the texture
        /// into a smear or — with <c>fit:"cover"</c> — crops it to an awkward sliver. Flagged from the
        /// authored RectTransform extents.
        /// </summary>
        private static void ValidateImageAspect(GameObject prefab, List<string> warnings)
        {
            // image element = an NeoShape with a sprite fill, or a plain Image with a sprite
            // (mirrors how UISpecExporter recognizes "image" elements).
            var imageRects = new HashSet<RectTransform>();
            foreach (NeoShape shape in prefab.GetComponentsInChildren<NeoShape>(true))
                if (shape.sprite != null && shape.transform is RectTransform shapeRect)
                    imageRects.Add(shapeRect);
            foreach (UnityEngine.UI.Image image in prefab.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (image.sprite != null && image.transform is RectTransform imageRect)
                    imageRects.Add(imageRect);

            foreach (RectTransform rect in imageRects)
            {
                float width = Mathf.Abs(rect.rect.width);
                float height = Mathf.Abs(rect.rect.height);
                if (width <= 0f || height <= 0f) continue; // size driven by a layout group — extent unknown here
                float ratio = Mathf.Max(width / height, height / width);
                if (ratio > ExtremeAspectRatio)
                    warnings.Add($"'{prefab.name}/{rect.name}' image rect is {width:0}×{height:0}px " +
                                 $"({ratio:0.#}:1) — the full-rect sprite fill will distort or crop badly; " +
                                 "match the rect to the sprite's aspect");
            }
        }

        /// <summary>
        /// Dead-interaction lint: every clickable thing in a generated view must DO something —
        /// a flow trigger, a signal, a view command, a popup or a wired event. "Renders fine,
        /// does nothing when clicked" is the most common way generated UI disappoints.
        /// </summary>
        private static void ValidateInteractivity(NeoUISettings settings, List<string> issues)
        {
            var flowButtonIds = new HashSet<string>();
            var flowToggleIds = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:FlowGraph"))
            {
                var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (graph == null) continue;
                foreach (FlowNode node in graph.nodes.Where(n => n != null))
                {
                    var triggers = node.outputs.Select(e => e.trigger);
                    if (node is PortalNode portal) triggers = triggers.Append(portal.trigger);
                    foreach (FlowTrigger trigger in triggers.Where(t => t != null))
                    {
                        string id = $"{trigger.category}/{trigger.name}";
                        if (trigger.type == FlowTrigger.TriggerType.ButtonClick) flowButtonIds.Add(id);
                        else if (trigger.type == FlowTrigger.TriggerType.ToggleOn ||
                                 trigger.type == FlowTrigger.TriggerType.ToggleOff) flowToggleIds.Add(id);
                    }
                }
            }

            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                if (prefab.GetComponent<UIView>() == null) continue;

                foreach (UIButton button in prefab.GetComponentsInChildren<UIButton>(true))
                {
                    // includeInactive: prefab ASSETS are inactive hierarchies — without it the
                    // stepper's code-wired buttons get falsely flagged
                    if (button.GetComponentInParent<UIStepper>(true) != null) continue; // wired in code
                    // menu controls forward to UserSettingsService through a binder at runtime
                    if (button.GetComponentInParent<Neo.UI.Menus.MenuPresenter>(true) != null) continue;
                    bool wired = button.GetComponent<ViewCommandOnClick>() != null
                                 || button.GetComponent<ShowPopupOnClick>() != null
                                 || button.GetComponent<HideContainerOnClick>() != null
                                 || flowButtonIds.Contains(button.id.ToString())
                                 // a project's custom wiring component can declare itself live
                                 || NeoInteractivityProviders.ClaimsWired(button.gameObject);
                    if (!wired)
                    {
                        foreach (UIActionBehaviour behaviour in button.behaviours)
                        {
                            if ((behaviour.sendSignal && behaviour.signalStream != null && !behaviour.signalStream.isDefault)
                                || (behaviour.Event != null && behaviour.Event.GetPersistentEventCount() > 0))
                            {
                                wired = true;
                                break;
                            }
                        }
                    }
                    if (!wired)
                        issues.Add($"Button '{button.id}' in '{prefab.name}' does nothing when clicked " +
                                   "(no flow trigger, signal, popup, view command or event)");
                }

                foreach (UITab tab in prefab.GetComponentsInChildren<UITab>(true))
                {
                    if (tab.targetContainer == null && !flowToggleIds.Contains(tab.id.ToString())
                        && !NeoInteractivityProviders.ClaimsWired(tab.gameObject))
                        issues.Add($"Tab '{tab.id}' in '{prefab.name}' highlights but controls nothing " +
                                   "(no container reference and no flow trigger listens to it)");
                }
            }

            // Project-registered INTERACTIVITY rules run after the built-in dead-interaction check.
            NeoValidationRules.Run(ValidationBucket.Interactivity,
                new ValidationContext(settings, GeneratedViewPrefabs(), issues));
        }

        /// <summary>
        /// Dead-binding lint for menus: every <see cref="Neo.UI.Menus.MenuControlBinder"/> /
        /// rebind control must resolve to a real catalog entry, and a rebind row must name an action.
        /// </summary>
        private static void ValidateMenuBindings(List<string> issues)
        {
            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                if (prefab.GetComponent<UIView>() == null) continue;

                foreach (var binder in prefab.GetComponentsInChildren<Neo.UI.Menus.MenuControlBinder>(true))
                {
                    if (binder.catalog == null)
                        issues.Add($"Menu control '{binder.category}/{binder.itemName}' in '{prefab.name}' has no catalog");
                    else if (binder.Definition == null)
                        issues.Add($"Menu control '{binder.category}/{binder.itemName}' in '{prefab.name}' " +
                                   $"is not in catalog '{binder.catalog.Id}'");
                }

                foreach (var rebind in prefab.GetComponentsInChildren<Neo.UI.Menus.UIRebindControl>(true))
                {
                    if (rebind.Definition == null)
                        issues.Add($"Rebind control '{rebind.category}/{rebind.itemName}' in '{prefab.name}' " +
                                   "is not in its catalog");
                    else if (string.IsNullOrEmpty(rebind.Definition.inputAction))
                        issues.Add($"Rebind '{rebind.category}/{rebind.itemName}' in '{prefab.name}' names no input action");
                }
            }
        }

        private static void ValidateTheme(NeoUISettings settings, List<string> issues)
        {
            if (settings.theme == null)
            {
                issues.Add("Settings has no theme assigned");
                return;
            }

            var tokens = new HashSet<string>(settings.theme.GetTokenNames());

            // every variant should cover every token
            foreach (Theme.ThemeVariant variant in settings.theme.Variants)
            {
                foreach (string token in tokens)
                {
                    if (!variant.TryGetColor(token, out _))
                        issues.Add($"Theme variant '{variant.name}' is missing token '{token}'");
                }
            }

            // bound targets in generated prefabs must reference existing tokens
            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                foreach (ThemeColorTarget target in prefab.GetComponentsInChildren<ThemeColorTarget>(true))
                {
                    if (target.themeOverride != null) continue;
                    if (string.IsNullOrEmpty(target.token))
                        issues.Add($"'{prefab.name}/{target.name}' has a ThemeColorTarget with no token");
                    // A '#'-prefixed value is a literal hex fill (not a theme token) — validate it parses
                    // rather than reporting it as an unknown token.
                    else if (target.token[0] == '#')
                    {
                        if (!ColorUtils.TryParseHex(target.token, out _))
                            issues.Add($"'{prefab.name}/{target.name}' has an unparseable hex color '{target.token}'");
                    }
                    else if (!tokens.Contains(target.token))
                        issues.Add($"'{prefab.name}/{target.name}' references unknown theme token '{target.token}'");
                }
            }
        }

        private static void ValidatePopupDatabase(NeoUISettings settings, List<string> issues)
        {
            if (settings.popupDatabase == null) return;
            foreach (PopupDatabase.PopupLink link in settings.popupDatabase.Popups)
            {
                if (string.IsNullOrWhiteSpace(link.popupName))
                {
                    issues.Add("Popup database contains an entry with no name");
                    continue;
                }
                if (link.prefab == null)
                    issues.Add($"Popup '{link.popupName}' has no prefab assigned");
                else if (link.prefab.GetComponent<UIPopup>() == null)
                    issues.Add($"Popup '{link.popupName}' prefab has no UIPopup component");
            }
        }

        private static void ValidatePresetDatabase(NeoUISettings settings, List<string> issues)
        {
            if (settings.animationPresets == null) return;
            var names = new HashSet<string>();
            foreach (UIAnimationPreset preset in settings.animationPresets.Presets)
            {
                if (preset == null)
                {
                    issues.Add("Animation preset database contains a null entry");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(preset.presetName))
                    issues.Add($"Animation preset asset '{preset.name}' has no preset name");
                else if (!names.Add(preset.presetName))
                    issues.Add($"Duplicate animation preset name '{preset.presetName}'");
            }
        }

        private static void ValidateGeneratedViews(NeoUISettings settings, List<string> issues)
        {
            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                UIView view = prefab.GetComponent<UIView>();
                if (view == null) continue;
                if (view.id.isDefault)
                {
                    issues.Add($"Generated view prefab '{prefab.name}' has a default (None/None) ViewId");
                    continue;
                }
                if (settings.viewIds != null && !settings.viewIds.Contains(view.id.Category, view.id.Name))
                    issues.Add($"View '{view.id}' is not registered in the ViewId database");

                foreach (UIButton button in prefab.GetComponentsInChildren<UIButton>(true))
                {
                    if (button.id.isDefault) continue;
                    if (settings.buttonIds != null && !settings.buttonIds.Contains(button.id.Category, button.id.Name))
                        issues.Add($"Button '{button.id}' in view '{view.id}' is not registered in the ButtonId database");
                }
            }
        }

        private static void ValidateFlowGraphs(NeoUISettings settings, List<string> issues)
        {
            var knownViews = new HashSet<string>();
            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                UIView view = prefab.GetComponent<UIView>();
                if (view != null) knownViews.Add(view.id.ToString());
            }

            // Scan ONLY the current GeneratedRoot — the same scope as every other per-asset lint here.
            // Validation checks what THIS root's generation produced; a project-wide t:FlowGraph scan
            // would pull in every committed showcase's flow graph (each lives in its own isolated
            // Assets/Showcases/{id}/Generated root) and lint it against the current root's views —
            // every reference reads as "unknown". A showcase's own flows get validated when validate
            // runs inside its NeoWorkspace scope (GeneratedRoot = that showcase's root).
            if (!AssetDatabase.IsValidFolder(UISpecGenerator.GeneratedRoot)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:FlowGraph", new[] { UISpecGenerator.GeneratedRoot }))
            {
                var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (graph == null) continue;

                foreach (string issue in graph.Validate())
                    issues.Add($"Flow '{graph.name}': {issue}");

                foreach (UINode node in graph.nodes.OfType<UINode>())
                {
                    foreach (UINode.ViewRef viewRef in node.showViews.Concat(node.hideViews))
                    {
                        string id = $"{viewRef.category}/{viewRef.viewName}";
                        bool inDatabase = settings.viewIds != null && settings.viewIds.Contains(viewRef.category, viewRef.viewName);
                        if (!knownViews.Contains(id) && !inDatabase)
                            issues.Add($"Flow '{graph.name}' node '{node.name}' references unknown view '{id}'");
                    }

                    foreach (FlowEdge edge in node.outputs)
                    {
                        if (edge.trigger == null || edge.trigger.type != FlowTrigger.TriggerType.ButtonClick) continue;
                        if (settings.buttonIds != null && !settings.buttonIds.Contains(edge.trigger.category, edge.trigger.name))
                            issues.Add($"Flow '{graph.name}' node '{node.name}' triggers on unknown button '{edge.trigger.category}/{edge.trigger.name}'");
                    }
                }
            }
        }

        /// <summary>
        /// Generated UIView-rooted prefabs, materialized as a list, for the <see cref="ValidationContext"/>
        /// handed to registered rules — the same set the built-in design/interactivity checks walk.
        /// </summary>
        private static IReadOnlyList<GameObject> GeneratedViewPrefabs()
        {
            var views = new List<GameObject>();
            foreach (GameObject prefab in LoadGeneratedPrefabs())
                if (prefab.GetComponent<UIView>() != null) views.Add(prefab);
            return views;
        }

        private static IEnumerable<GameObject> LoadGeneratedPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(UISpecGenerator.GeneratedRoot)) yield break;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { UISpecGenerator.GeneratedRoot }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent<GeneratedMarker>() != null) yield return prefab;
            }
        }
    }
}
