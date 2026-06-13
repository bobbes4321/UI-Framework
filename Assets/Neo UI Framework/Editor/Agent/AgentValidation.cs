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
        [MenuItem("Tools/Neo UI/Validate", priority = 3)]
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

            foreach (GameObject prefab in LoadGeneratedPrefabs())
            {
                if (prefab.GetComponent<UIView>() == null) continue;

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
            }

            ValidateWidgetContrast(settings, warnings);

            // Project-registered DESIGN rules run after the built-in soft checks, into the same
            // warnings list. They are surfaced as designWarnings and never reach ValidateAll.
            NeoValidationRules.Run(ValidationBucket.Design,
                new ValidationContext(settings, GeneratedViewPrefabs(), warnings));

            return warnings;
        }

        // ── Design-lint config seam (read from NeoUISettings; the project's single source of truth) ─

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

            foreach (string guid in AssetDatabase.FindAssets("t:FlowGraph"))
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
