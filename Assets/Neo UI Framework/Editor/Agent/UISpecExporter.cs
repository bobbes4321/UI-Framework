using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.UI.Menus;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Dumps the current theme, presets, generated views and flow graphs back to spec text,
    /// so agents can read the current UI state without parsing Unity YAML.
    /// </summary>
    public static partial class UISpecExporter
    {
        [MenuItem("Tools/Neo UI/Advanced/Export Spec…", priority = 11)]
        public static void ExportToFileDialog()
        {
            string path = EditorUtility.SaveFilePanel("Export UI spec (JSON)", Application.dataPath, "ui-spec", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, ExportProject().ToJson());
            Debug.Log($"[Neo.UI] Spec exported to {path}");
        }

        /// <summary> Exports everything the generator manages into one spec. </summary>
        public static UISpec ExportProject()
        {
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            var spec = new UISpec
            {
                theme = ExportTheme(settings.theme)
            };

            if (settings.animationPresets != null)
                foreach (UIAnimationPreset preset in settings.animationPresets.Presets.Where(p => p != null))
                    spec.presets.Add(ExportPreset(preset));

            // catalogs (sorted by id so export stays idempotent regardless of asset-scan order)
            var catalogs = new List<MenuCatalog>();
            foreach (string guid in FindGenerated("t:ScriptableObject", $"{UISpecGenerator.GeneratedRoot}/Menus"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<MenuCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog != null) catalogs.Add(catalog);
            }
            foreach (MenuCatalog catalog in catalogs.OrderBy(c => c.Id, System.StringComparer.Ordinal))
            {
                MenuCatalogSpec catalogSpec = ExportCatalog(catalog);
                // Wave 7 Task 7.1: kind resolved via NeoCatalogKinds instead of an "is CheatCatalog" type
                // check, so a project's own MenuCatalog subtype (registered with its own CatalogKind)
                // files into its own spec list too, not just the settings/cheats binary.
                string catalogKindId = NeoCatalogKinds.KindOf(catalog);
                if (NeoCatalogKinds.TryGet(catalogKindId, out CatalogKind catalogKind)) catalogKind.list(spec).Add(catalogSpec);
                else spec.settings.Add(catalogSpec);
            }

            // views (sorted by id so export stays idempotent regardless of asset-scan order)
            var views = new List<UIView>();
            foreach (string guid in FindGenerated("t:Prefab", UISpecGenerator.ViewsFolder))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                UIView view = prefab != null ? prefab.GetComponent<UIView>() : null;
                if (view != null) views.Add(view);
            }
            var responsiveRoots = new List<UIResponsiveRoot>();
            foreach (UIView view in views.OrderBy(v => v.id.ToString(), System.StringComparer.Ordinal))
            {
                spec.views.Add(ExportView(view.gameObject));
                var responsive = view.GetComponent<UIResponsiveRoot>();
                if (responsive != null) responsiveRoots.Add(responsive);
            }

            // Pillar B: top-level breakpoints are global — reconstruct from the (identical) condition
            // tables on the views' UIResponsiveRoots, deduped by name preserving first-seen order.
            ReconstructBreakpoints(spec, responsiveRoots);

            // popups (sorted by name so export stays idempotent regardless of asset-scan order)
            var popups = new List<UIPopup>();
            foreach (string guid in FindGenerated("t:Prefab", UISpecGenerator.PopupsFolder))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                UIPopup popup = prefab != null ? prefab.GetComponent<UIPopup>() : null;
                if (popup != null) popups.Add(popup);
            }
            foreach (UIPopup popup in popups.OrderBy(p => p.popupName, System.StringComparer.Ordinal))
                spec.popups.Add(ExportPopup(popup));

            // flow graphs (sorted by name so which graph exports is deterministic, not scan-order
            // dependent). NOTE: UISpec.flow is a single FlowSpec, not a list, so only the
            // alphabetically-first graph is exported when more than one lives in the shared
            // GeneratedRoot/Flow folder — exporting every flow graph needs an additive schema change
            // (spec.flow -> a list) that is out of scope here; see the caller's task notes.
            var flowGraphs = new List<FlowGraph>();
            foreach (string guid in FindGenerated("t:FlowGraph", UISpecGenerator.FlowFolder))
            {
                var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (graph != null) flowGraphs.Add(graph);
            }
            FlowGraph selectedFlow = flowGraphs
                .OrderBy(g => string.IsNullOrEmpty(g.graphName) ? g.name : g.graphName, System.StringComparer.Ordinal)
                .FirstOrDefault();
            if (selectedFlow != null) spec.flow = ExportFlow(selectedFlow);

            return spec;
        }

        /// <summary>
        /// Assets under one generated subfolder; empty when it doesn't exist. NEVER fall back to
        /// scanning all of Assets — that exports committed demo/starter content as if it were
        /// generated, and the next generate hijacks those popups into the generated folder.
        /// </summary>
        private static IEnumerable<string> FindGenerated(string filter, string folder) =>
            AssetDatabase.IsValidFolder(folder)
                ? AssetDatabase.FindAssets(filter, new[] { folder })
                : Enumerable.Empty<string>();

        /// <summary>
        /// Popup prefab → spec. Title/Message labels and the legacy OK row are factory chrome
        /// (matched by child name); everything else in the card content exports as elements, so
        /// plain popups keep their three-field form and rich popups round-trip their content.
        /// </summary>
        public static PopupSpec ExportPopup(UIPopup popup)
        {
            var popupSpec = new PopupSpec { name = popup.popupName };
            RectTransform card = popup.content;
            if (card == null) return popupSpec;

            popupSpec.close = card.Find(UIWidgetFactory.CloseName) != null;
            Vector2 cardSize = card.sizeDelta;
            if ((cardSize - UIWidgetFactory.PopupDefaultCardSize).sqrMagnitude > 1e-3f)
                popupSpec.size = new[] { cardSize.x, cardSize.y };

            Transform contentHost = card.Find(UIWidgetFactory.ContentName);
            if (contentHost == null) return popupSpec;

            popupSpec.title = FindChildText(contentHost.gameObject, UIWidgetFactory.PopupTitleName)?.text;
            popupSpec.message = FindChildText(contentHost.gameObject, UIWidgetFactory.PopupMessageName)?.text;

            foreach (Transform child in contentHost)
            {
                if (child.name == UIWidgetFactory.PopupTitleName || child.name == UIWidgetFactory.PopupMessageName ||
                    child.name == UIWidgetFactory.PopupButtonsName) continue;
                ElementSpec element = ExportElement(child.gameObject, inLayout: true);
                if (element != null) popupSpec.elements.Add(element);
            }
            return popupSpec;
        }

        public static ThemeSpec ExportTheme(Theme theme)
        {
            var themeSpec = new ThemeSpec();
            if (theme == null) return themeSpec;

            Theme.ThemeVariant defaultVariant = theme.Variants.FirstOrDefault();
            if (defaultVariant != null)
                foreach (Theme.TokenColor token in defaultVariant.colors)
                    themeSpec.tokens[token.token] = ColorUtils.ToHex(token.color);

            foreach (Theme.ThemeVariant variant in theme.Variants.Skip(1))
            {
                var colors = new Dictionary<string, string>();
                foreach (Theme.TokenColor token in variant.colors)
                    colors[token.token] = ColorUtils.ToHex(token.color);
                themeSpec.variants[variant.name] = colors;
            }
            return themeSpec;
        }

        public static PresetSpec ExportPreset(UIAnimationPreset preset)
        {
            UIAnimation animation = preset.animation;
            var presetSpec = new PresetSpec
            {
                name = preset.presetName,
                type = preset.category,
                ease = animation.move.enabled ? animation.move.settings.ease.ToString() : Ease.OutCubic.ToString(),
                duration = animation.move.enabled ? animation.move.settings.duration
                    : animation.fade.enabled ? animation.fade.settings.duration
                    : animation.scale.enabled ? animation.scale.settings.duration
                    : 0.3f
            };

            if (animation.move.enabled)
            {
                presetSpec.move = new PresetChannelSpec
                {
                    enabled = true,
                    from = animation.move.fromDirection != UIMoveDirection.CustomPosition ? animation.move.fromDirection.ToString() : null,
                    to = animation.move.toDirection != UIMoveDirection.CustomPosition ? animation.move.toDirection.ToString() : null,
                    duration = animation.move.settings.duration,
                    ease = animation.move.settings.ease.ToString()
                };
            }
            if (animation.rotate.enabled)
            {
                presetSpec.rotate = new PresetChannelSpec
                {
                    enabled = true,
                    from = Format(animation.rotate.fromCustomValue),
                    to = Format(animation.rotate.toCustomValue),
                    duration = animation.rotate.settings.duration,
                    ease = animation.rotate.settings.ease.ToString()
                };
            }
            if (animation.scale.enabled)
            {
                presetSpec.scale = new PresetChannelSpec
                {
                    enabled = true,
                    from = Format(animation.scale.fromCustomValue),
                    to = Format(animation.scale.toCustomValue),
                    duration = animation.scale.settings.duration,
                    ease = animation.scale.settings.ease.ToString()
                };
            }
            if (animation.fade.enabled)
            {
                presetSpec.fade = new PresetChannelSpec
                {
                    enabled = true,
                    from = animation.fade.fromCustomValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    to = animation.fade.toCustomValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    duration = animation.fade.settings.duration,
                    ease = animation.fade.settings.ease.ToString()
                };
            }
            if (animation.color.enabled)
            {
                presetSpec.color = new PresetChannelSpec
                {
                    enabled = true,
                    from = FormatColorEndpoint(animation.color.from),
                    to = FormatColorEndpoint(animation.color.to),
                    duration = animation.color.settings.duration,
                    ease = animation.color.settings.ease.ToString()
                };
            }
            return presetSpec;
        }

        // Encodes a color endpoint as the from/to string the spec uses: "start"/"current" for the
        // captured/live color, a "#hex" for a custom color, or a bare theme-token name.
        private static string FormatColorEndpoint(ColorAnimationEndpoint endpoint)
        {
            switch (endpoint.reference)
            {
                case ColorReference.StartColor: return "start";
                case ColorReference.CurrentColor: return "current";
                case ColorReference.ThemeToken: return endpoint.themeToken;
                default: return ColorUtils.ToHex(endpoint.customColor);
            }
        }

        private static string Format(Vector3 value) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2}", value.x, value.y, value.z);

        public static ViewSpec ExportView(GameObject prefab)
        {
            UIView view = prefab.GetComponent<UIView>();
            var viewSpec = new ViewSpec
            {
                category = view.id.Category,
                viewName = view.id.Name
            };

            UIContainerUIAnimator animator = prefab.GetComponent<UIContainerUIAnimator>();
            NeoUISettings settings = NeoUISettings.instance;
            if (animator != null && settings != null && settings.animationPresets != null)
            {
                viewSpec.showAnimation = FindMatchingPresetName(settings, animator.showAnimation);
                viewSpec.hideAnimation = FindMatchingPresetName(settings, animator.hideAnimation);
            }

            Transform background = prefab.transform.Find("Background");
            if (background != null)
            {
                var backgroundTarget = background.GetComponent<ThemeColorTarget>();
                if (backgroundTarget != null) viewSpec.background = backgroundTarget.token;
            }

            // Pillar B: capture the GameObject→ElementSpec map for this view so a UIResponsiveRoot's
            // baked entries can be reattached as per-element `overrides` below.
            var sink = new Dictionary<GameObject, ElementSpec>();
            Dictionary<GameObject, ElementSpec> previousSink = s_elementSink;
            s_elementSink = sink;
            try
            {
                foreach (Transform child in prefab.transform)
                {
                    if (child.name == "Background") continue;
                    ElementSpec element = ExportElement(child.gameObject, inLayout: false);
                    if (element != null) viewSpec.elements.Add(element);
                }
            }
            finally
            {
                s_elementSink = previousSink;
            }

            ReconstructOverrides(prefab.GetComponent<UIResponsiveRoot>(), sink);
            return viewSpec;
        }

        /// <summary>
        /// Pillar B: rebuilds the global <c>breakpoints</c> list from the views' (identical) condition
        /// tables, deduped by name (first-seen order preserved). The condition table is force-text and
        /// carries the exact authored kinds, so this round-trips byte-identically.
        /// </summary>
        private static void ReconstructBreakpoints(UISpec spec, List<UIResponsiveRoot> roots)
        {
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (UIResponsiveRoot root in roots)
            {
                if (root == null) continue;
                foreach (UIResponsiveRoot.ResponsiveCondition c in root.conditions)
                {
                    if (c == null || string.IsNullOrEmpty(c.name) || !seen.Add(c.name)) continue;
                    var bp = new BreakpointSpec
                    {
                        name = c.name,
                        when = new BreakpointCondition
                        {
                            orientation = string.IsNullOrEmpty(c.orientation) ? null : c.orientation,
                            minAspect = float.IsNaN(c.minAspect) ? (float?)null : c.minAspect,
                            maxAspect = float.IsNaN(c.maxAspect) ? (float?)null : c.maxAspect,
                            minWidth = float.IsNaN(c.minWidth) ? (float?)null : c.minWidth,
                            maxWidth = float.IsNaN(c.maxWidth) ? (float?)null : c.maxWidth
                        }
                    };
                    spec.breakpoints.Add(bp);
                }
            }
        }

        /// <summary>
        /// Pillar B: reconstructs each element's <c>overrides</c> from the baked
        /// <see cref="UIResponsiveRoot"/> entries, using the stored ORIGINAL delta (not the resolved
        /// vectors, which alias) so export round-trips byte-identically. The top-level
        /// <c>breakpoints</c> are reconstructed separately in <see cref="ExportProject"/> from the
        /// (global) condition table.
        /// </summary>
        private static void ReconstructOverrides(UIResponsiveRoot responsive,
            Dictionary<GameObject, ElementSpec> sink)
        {
            if (responsive == null) return;
            foreach (UIResponsiveRoot.ResponsiveEntry entry in responsive.entries)
            {
                if (entry == null || entry.target == null) continue;
                if (!sink.TryGetValue(entry.target.gameObject, out ElementSpec element) || element == null)
                {
                    Debug.LogWarning($"UISpecExporter: a UIResponsiveRoot entry for breakpoint " +
                        $"'{entry.breakpoint}' references an object not exported as an element; its override " +
                        "is dropped from the spec.");
                    continue;
                }
                LayoutSpec delta = FromResponsiveDelta(entry.delta);
                if (delta == null || delta.IsEmpty) continue;
                if (element.overrides == null) element.overrides = new Dictionary<string, LayoutSpec>();
                element.overrides[entry.breakpoint] = delta;
            }
        }

        /// <summary> Reverses <c>ToResponsiveDelta</c>: the baked force-text delta → a LayoutSpec. </summary>
        private static LayoutSpec FromResponsiveDelta(UIResponsiveRoot.ResponsiveDelta d)
        {
            if (d == null) return null;
            var spec = new LayoutSpec
            {
                h = string.IsNullOrEmpty(d.h) ? null : d.h,
                v = string.IsNullOrEmpty(d.v) ? null : d.v
            };
            if (d.offsetKeys != null && d.offsetKeys.Count > 0)
            {
                var offset = new LayoutOffset();
                for (int i = 0; i < d.offsetKeys.Count && i < d.offsetValues.Count; i++)
                    offset.Set(d.offsetKeys[i], d.offsetValues[i]);
                if (!offset.IsEmpty) spec.offset = offset;
            }
            if (!float.IsNaN(d.sizeW) || !float.IsNaN(d.sizeH))
            {
                var size = new LayoutSize();
                if (!float.IsNaN(d.sizeW)) size.w = d.sizeW;
                if (!float.IsNaN(d.sizeH)) size.h = d.sizeH;
                if (!size.IsEmpty) spec.size = size;
            }
            if (!string.IsNullOrEmpty(d.sizingW) || !string.IsNullOrEmpty(d.sizingH))
            {
                var sizing = new LayoutSizing
                {
                    w = string.IsNullOrEmpty(d.sizingW) ? null : d.sizingW,
                    h = string.IsNullOrEmpty(d.sizingH) ? null : d.sizingH
                };
                if (!sizing.IsEmpty) spec.sizing = sizing;
            }
            return spec.IsEmpty ? null : spec;
        }

        private static string FindMatchingPresetName(NeoUISettings settings, UIAnimation animation)
        {
            // generated views copy preset data; match back by purpose + duration + channels
            foreach (UIAnimationPreset preset in settings.animationPresets.Presets.Where(p => p != null))
            {
                UIAnimation presetAnimation = preset.animation;
                if (presetAnimation.purpose != animation.purpose) continue;
                if (presetAnimation.move.enabled != animation.move.enabled) continue;
                if (presetAnimation.fade.enabled != animation.fade.enabled) continue;
                if (presetAnimation.scale.enabled != animation.scale.enabled) continue;
                if (presetAnimation.move.enabled &&
                    (presetAnimation.move.fromDirection != animation.move.fromDirection ||
                     presetAnimation.move.toDirection != animation.move.toDirection)) continue;
                return preset.presetName;
            }
            return null;
        }

        /// <summary>
        /// Reads one GameObject back into spec form. Widgets are matched by their components
        /// (and the child names UIWidgetFactory uses), so hand-tweaked values — sizes, anchors,
        /// slider ranges, layout padding, shape styles — survive the round trip. Containers
        /// recurse; widget internals do not (the factory owns them).
        /// </summary>
        /// <summary> Per-view GameObject→ElementSpec map, populated during a view export so the
        /// responsive pass can attach each <c>UIResponsiveRoot</c> entry's override to the right
        /// element. Set/cleared around <see cref="ExportView"/>; null (zero-cost) otherwise. </summary>
        private static Dictionary<GameObject, ElementSpec> s_elementSink;

        // internal: the native-authoring "Apply Preset" flow (NeoSceneAuthoring) captures one live widget's
        // spec to re-build it under a preset, reusing the exact per-element export the view exporter uses.
        internal static ElementSpec ExportElement(GameObject go, bool inLayout)
        {
            ElementSpec element = ExportElementBody(go, inLayout);
            if (element == null) return null;
            // Non-widget elements (shape/text/image/container/progress/…) have no NeoId — recover their
            // authored id from the NeoElementId marker the generator stamped, so every spec-addressable
            // element round-trips its id. Widgets already set element.id from their own NeoId above.
            if (string.IsNullOrEmpty(element.id))
            {
                var idTag = go.GetComponent<NeoElementId>();
                if (idTag != null && !string.IsNullOrEmpty(idTag.id)) element.id = idTag.id;
            }
            ExportGeometry(element, (RectTransform)go.transform, inLayout);
            element.effect = ExportEffect(go);
            element.particles = ExportParticles(go);
            element.pointerGlow = ExportPointerGlow(go);
            element.animations = ExportElementAnimations(go);
            var sharedElement = go.GetComponent<NeoSharedElement>();
            if (sharedElement != null && !string.IsNullOrEmpty(sharedElement.key))
                element.sharedElement = sharedElement.key;
            ApplyPresetDelta(go, element);
            if (s_elementSink != null) s_elementSink[go] = element;
            return element;
        }

        /// <summary>
        /// If the widget was generated from a <see cref="NeoWidgetPreset"/> (it carries a
        /// <see cref="WidgetPresetTag"/>), write the preset name back and strip every field that equals the
        /// preset's value — so the spec keeps the LINK plus only the override delta, not a flattened copy.
        /// A missing preset is non-fatal and loud: the link is kept but the fields stay inline (nothing to
        /// diff against), so no data is ever silently dropped.
        /// </summary>
        private static void ApplyPresetDelta(GameObject go, ElementSpec element)
        {
            WidgetPresetTag tag = go.GetComponent<WidgetPresetTag>();
            if (tag == null || string.IsNullOrEmpty(tag.presetName)) return;
            element.preset = tag.presetName;

            if (!NeoWidgetPresets.TryGet(tag.presetName, out NeoWidgetPreset p))
            {
                Debug.LogWarning($"[Neo.UI] Exported element '{element.id ?? element.kind}' references missing " +
                                 $"preset '{tag.presetName}' — keeping the link but exporting its fields inline.");
                return;
            }

            // One descriptor table drives every preset-governed field (audit D1) — a field that still
            // equals the preset's value is cleared, so the export keeps only the override delta. "motion"
            // is one of these fields (a custom get/set/clear — see PresetFields.MotionField): it strips
            // the element's `loop` channel back out when it still equals the preset's motion (a different
            // loop the author set explicitly survives) and drops the whole animations object if nothing
            // else remains.
            foreach (PresetField field in PresetFields.All)
            {
                object elementValue = field.getElement(element);
                object presetValue = field.getPreset(p);
                if (field.equal(elementValue, presetValue)) field.clearElement(element);
            }
        }

        private static ElementSpec ExportElementBody(GameObject go, bool inLayout)
        {
            // Extensibility seam (keystone): a project-registered kind exports through its own provider
            // FIRST. Built-ins are not registered (their GetComponent chain below is untouched), so this
            // is a no-op until a project registers. A provider's TryExport must match a marker component
            // specific to its kind so it never hijacks a built-in.
            foreach (INeoElementKind k in NeoElementKinds.All)
                if (k != null && k.TryExport(go, out ElementSpec extSpec) && extSpec != null)
                    return extSpec;

            // a built menu exports as just its catalog reference — the catalog owns the rows
            var menuPresenter = go.GetComponent<MenuPresenter>();
            if (menuPresenter != null && menuPresenter.catalog != null)
                return new ElementSpec
                {
                    kind = NeoCatalogKinds.KindOf(menuPresenter.catalog),
                    catalog = menuPresenter.catalog.Id
                };

            var safeArea = go.GetComponent<SafeAreaFitter>();
            if (safeArea != null)
            {
                var element = new ElementSpec { kind = "safearea" };
                foreach (Transform child in go.transform)
                {
                    // plain container: children keep their own anchors/positions
                    ElementSpec childElement = ExportElement(child.gameObject, inLayout: false);
                    if (childElement != null) element.children.Add(childElement);
                }
                return element;
            }

            var overlayMarker = go.GetComponent<UIOverlay>();
            if (overlayMarker != null)
            {
                var element = new ElementSpec { kind = "overlay" };
                ExportContainerDecor(go, element);
                foreach (Transform child in go.transform)
                {
                    // z-stack: children keep their own anchors/positions
                    ElementSpec childElement = ExportElement(child.gameObject, inLayout: false);
                    if (childElement != null) element.children.Add(childElement);
                }
                return element;
            }

            var stepper = go.GetComponent<UIStepper>();
            if (stepper != null)
            {
                var element = new ElementSpec
                {
                    kind = "stepper",
                    min = stepper.minValue,
                    max = stepper.maxValue,
                    value = stepper.currentValue,
                    step = stepper.stepSize
                };
                // the stepper itself carries no id — recover it from the derived minus-button id
                if (stepper.minusButton != null && !stepper.minusButton.id.isDefault)
                {
                    string buttonName = stepper.minusButton.id.Name;
                    if (buttonName.EndsWith(UIWidgetFactory.StepperButtonSuffixMinus, System.StringComparison.Ordinal))
                        element.id = $"{stepper.minusButton.id.Category}/" +
                                     buttonName.Substring(0, buttonName.Length - UIWidgetFactory.StepperButtonSuffixMinus.Length);
                }
                return element;
            }

            var input = go.GetComponent<TMP_InputField>();
            if (input != null)
            {
                var placeholderText = input.placeholder as TMP_Text;
                return new ElementSpec
                {
                    kind = "input",
                    label = placeholderText != null ? placeholderText.text : null
                };
            }

            var dropdown = go.GetComponent<UIDropdown>();
            if (dropdown != null)
            {
                var options = new List<string>();
                foreach (TMP_Dropdown.OptionData option in dropdown.options) options.Add(option.text);
                return new ElementSpec
                {
                    kind = "dropdown",
                    id = dropdown.id.ToString(),
                    options = options,
                    value = dropdown.value,
                    signal = ExportDomainSignal(dropdown.domainSignal)
                };
            }

            var slider = go.GetComponent<UISlider>();
            if (slider != null)
            {
                return new ElementSpec
                {
                    kind = "slider",
                    id = slider.id.ToString(),
                    min = slider.minValue,
                    max = slider.maxValue,
                    value = slider.value,
                    signal = ExportDomainSignal(slider.domainSignal)
                };
            }

            var tab = go.GetComponent<UITab>();
            if (tab != null)
            {
                // icon children would shadow GetComponentInChildren — address the label by name
                TMP_Text tabLabel = FindChildText(go, UIWidgetFactory.LabelName);
                // a tab inside a tabbar gets selection from its group; standalone (sidebar) tabs
                // round-trip the baked selected state like toggles do, plus any view-level
                // exclusivity group they declared via "group"
                bool inTabBar = go.transform.parent != null
                                && go.transform.parent.GetComponent<UIToggleGroup>() != null;
                var serializedTab = new SerializedObject(tab);
                bool tabStartsOn = serializedTab.FindProperty("isOnValue").boolValue;
                return new ElementSpec
                {
                    kind = "tab",
                    id = tab.id.ToString(),
                    label = tabLabel?.text,
                    labelColor = tabLabel?.GetComponent<ThemeColorTarget>()?.token,
                    icon = ExportIconName(go),
                    badge = ExportBadgeCount(go),
                    textStyle = tabLabel?.GetComponent<ThemeTextStyleTarget>()?.style,
                    variant = go.GetComponent<WidgetStyleTag>()?.variant,
                    controls = (tab.targetContainer as UIPanel)?.id.ToString(),
                    group = !inTabBar && tab.toggleGroup != null ? tab.toggleGroup.id.Name : null,
                    value = !inTabBar && tabStartsOn ? (float?)1f : null
                };
            }

            var toggle = go.GetComponent<UIToggle>();
            if (toggle != null)
            {
                bool isSwitch = go.transform.Find(UIWidgetFactory.KnobName) != null;
                TMP_Text toggleLabel = isSwitch ? null : FindChildText(go, UIWidgetFactory.LabelName);
                // baked start state (isOnValue) round-trips as value 1; off stays implicit
                var serializedToggle = new SerializedObject(toggle);
                bool startsOn = serializedToggle.FindProperty("isOnValue").boolValue;
                return new ElementSpec
                {
                    kind = isSwitch ? "switch" : "toggle",
                    id = toggle.id.ToString(),
                    label = toggleLabel?.text,
                    labelColor = toggleLabel?.GetComponent<ThemeColorTarget>()?.token,
                    textStyle = toggleLabel?.GetComponent<ThemeTextStyleTarget>()?.style,
                    background = go.GetComponent<ThemeColorTarget>()?.token,
                    group = toggle.toggleGroup != null ? toggle.toggleGroup.id.Name : null,
                    value = startsOn ? (float?)1f : null,
                    signal = ExportDomainSignal(toggle.domainSignal)
                };
            }

            var button = go.GetComponent<UIButton>();
            if (button != null)
            {
                TMP_Text buttonLabel = FindChildText(go, UIWidgetFactory.LabelName);
                var tag = go.GetComponent<WidgetStyleTag>();
                var element = new ElementSpec
                {
                    kind = "button",
                    id = button.id.ToString(),
                    label = buttonLabel?.text,
                    icon = ExportIconName(go),
                    badge = ExportBadgeCount(go),
                    // factory defaults stay implicit so hand-written specs round-trip unchanged
                    variant = tag != null && tag.variant != UIWidgetFactory.VariantPrimary ? tag.variant : null,
                    sizeVariant = tag != null && tag.size != UIWidgetFactory.SizeMedium ? tag.size : null,
                    background = go.GetComponent<ThemeColorTarget>()?.token,
                    labelColor = buttonLabel?.GetComponent<ThemeColorTarget>()?.token,
                    textStyle = buttonLabel?.GetComponent<ThemeTextStyleTarget>()?.style
                };
                UIActionBehaviour click = button.GetBehaviour(BehaviourTrigger.Click);
                if (click != null && click.sendSignal && !click.signalStream.isDefault)
                    element.onClickSignal = new SignalRefSpec { category = click.signalStream.Category, name = click.signalStream.Name };
                // A button can carry BOTH a Show and a Hide command (two separate components) — walk
                // all of them rather than one GetComponent, or the second command is silently lost.
                // A duplicate of the SAME command keeps the first and warns (no-silent-failure).
                bool sawShowCommand = false, sawHideCommand = false;
                foreach (ViewCommandOnClick viewCommand in go.GetComponents<ViewCommandOnClick>())
                {
                    if (viewCommand.command == ViewCommandOnClick.Command.Show)
                    {
                        if (sawShowCommand)
                        {
                            Debug.LogWarning($"[Neo.UI] Element '{button.id}' has more than one onClick " +
                                             $"Show view command — keeping '{element.onClickShowView}', ignoring '{viewCommand.view}'.");
                            continue;
                        }
                        sawShowCommand = true;
                        element.onClickShowView = viewCommand.view.ToString();
                    }
                    else
                    {
                        if (sawHideCommand)
                        {
                            Debug.LogWarning($"[Neo.UI] Element '{button.id}' has more than one onClick " +
                                             $"Hide view command — keeping '{element.onClickHideView}', ignoring '{viewCommand.view}'.");
                            continue;
                        }
                        sawHideCommand = true;
                        element.onClickHideView = viewCommand.view.ToString();
                    }
                }
                var popupOnClick = go.GetComponent<ShowPopupOnClick>();
                if (popupOnClick != null) element.onClickPopup = popupOnClick.popupName;
                element.onClickClose = go.GetComponent<HideContainerOnClick>() != null;
                return element;
            }

            var progressor = go.GetComponent<Progressor>();
            if (progressor != null)
            {
                return new ElementSpec
                {
                    kind = "progress",
                    // the radial dial drives an arc shape instead of an image fill
                    style = progressor.progressTargets.Any(t => t is ShapeProgressTarget) ? "radial" : null,
                    min = progressor.fromValue,
                    max = progressor.toValue,
                    value = progressor.onStartBehaviour == Progressor.StartBehaviour.SetCustomValue
                        ? (float?)progressor.startValue
                        : null
                };
            }

            var toggleGroup = go.GetComponent<UIToggleGroup>();
            if (toggleGroup != null && go.GetComponent<HorizontalLayoutGroup>() != null)
            {
                var element = new ElementSpec
                {
                    kind = "tabbar",
                    id = toggleGroup.id.ToString()
                };
                foreach (Transform child in go.transform)
                {
                    ElementSpec childElement = ExportElement(child.gameObject, inLayout: true);
                    if (childElement != null) element.children.Add(childElement);
                }
                return element;
            }

            var scroll = go.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                var element = new ElementSpec { kind = "list" };
                VerticalLayoutGroup contentLayout = scroll.content != null
                    ? scroll.content.GetComponent<VerticalLayoutGroup>() : null;
                if (contentLayout != null) element.spacing = contentLayout.spacing;
                // a stripped card backing round-trips as the "none" sentinel
                if (go.GetComponent<NeoShape>() == null) element.background = "none";
                // a data-bound list exports as bind + item (the template), never its spawned rows
                if (scroll.content == null || !TryExportBoundList(scroll.content.gameObject, element))
                {
                    if (scroll.content != null)
                        foreach (Transform child in scroll.content)
                        {
                            ElementSpec childElement = ExportElement(child.gameObject, inLayout: true);
                            if (childElement != null) element.children.Add(childElement);
                        }
                }
                return element;
            }

            var grid = go.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                var element = new ElementSpec
                {
                    kind = "grid",
                    spacing = grid.spacing.x,
                    cellSize = new[] { grid.cellSize.x, grid.cellSize.y },
                    cascade = go.GetComponent<UICascadeChildren>() != null,
                    align = ExportGridAlign(grid)
                };
                ExportPadding(grid.padding, element);
                ExportContainerDecor(go, element);
                // a responsive grid mutates constraintCount at runtime — its column count is
                // derived, not authored, so it round-trips as an absent "columns"
                if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount
                    && go.GetComponent<UIResponsiveGridColumns>() == null)
                    element.columns = grid.constraintCount;
                if (!TryExportBoundList(go, element))
                    foreach (Transform child in go.transform)
                    {
                        ElementSpec childElement = ExportElement(child.gameObject, inLayout: true);
                        if (childElement != null) element.children.Add(childElement);
                    }
                return element;
            }

            var panelComponent = go.GetComponent<UIPanel>();
            if (panelComponent != null)
            {
                var panelLayout = go.GetComponent<VerticalLayoutGroup>();
                var element = new ElementSpec
                {
                    kind = "panel",
                    id = panelComponent.id.ToString(),
                    spacing = panelLayout != null ? panelLayout.spacing : 0f,
                    cascade = go.GetComponent<UICascadeChildren>() != null
                };
                ExportPadding(panelLayout != null ? panelLayout.padding : null, element);
                ExportContainerDecor(go, element);
                foreach (Transform child in go.transform)
                {
                    ElementSpec childElement = ExportElement(child.gameObject, inLayout: true);
                    if (childElement != null) element.children.Add(childElement);
                }
                return element;
            }

            var stack = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (stack != null)
            {
                var element = new ElementSpec
                {
                    kind = stack is VerticalLayoutGroup ? "vstack" : "hstack",
                    spacing = stack.spacing,
                    cascade = go.GetComponent<UICascadeChildren>() != null,
                    align = ExportStackAlign(stack)
                };
                ExportPadding(stack.padding, element);
                ExportContainerDecor(go, element);
                foreach (Transform child in go.transform)
                {
                    ElementSpec childElement = ExportElement(child.gameObject, inLayout: true);
                    if (childElement != null) element.children.Add(childElement);
                }
                return element;
            }

            // spacers flex only along their parent stack's main axis, so either flag may be set
            if (go.GetComponent<LayoutElement>() is LayoutElement layoutElement
                && (layoutElement.flexibleWidth > 0f || layoutElement.flexibleHeight > 0f)
                && go.GetComponent<Graphic>() == null)
                return new ElementSpec { kind = "spacer" };

            var counter = go.GetComponent<UICounter>();
            if (counter != null)
            {
                string counterStyle = go.GetComponent<ThemeTextStyleTarget>()?.style;
                return new ElementSpec
                {
                    kind = "counter",
                    value = counter.value,
                    label = counter.format, // the counter's "label" is its number format
                    labelColor = go.GetComponent<ThemeColorTarget>()?.token,
                    textStyle = counterStyle,
                    fontSize = string.IsNullOrEmpty(counterStyle)
                        ? (float?)go.GetComponent<TMP_Text>()?.fontSize
                        : null
                };
            }

            var text = go.GetComponent<TMP_Text>();
            if (text != null)
            {
                // glyphs in the icon font are icon elements, not text
                NeoUISettings settings = NeoUISettings.instance;
                if (settings != null && settings.iconFont != null && text.font == settings.iconFont
                    && !string.IsNullOrEmpty(text.text) && IconMap.TryGetName(text.text[0], out string iconName))
                {
                    return new ElementSpec
                    {
                        kind = "icon",
                        icon = iconName,
                        labelColor = go.GetComponent<ThemeColorTarget>()?.token
                    };
                }

                string textStyle = go.GetComponent<ThemeTextStyleTarget>()?.style;
                var textElement = new ElementSpec
                {
                    kind = "text",
                    label = text.text,
                    labelColor = go.GetComponent<ThemeColorTarget>()?.token,
                    // the style owns the size — raw fontSize is only the styleless fallback
                    textStyle = textStyle,
                    fontSize = string.IsNullOrEmpty(textStyle) ? (float?)text.fontSize : null,
                    align = text.alignment == TextAlignmentOptions.Left ? "left"
                        : text.alignment == TextAlignmentOptions.Right ? "right"
                        : null // center is the default
                };
                // a non-default shared material is an ApplyTextOutline preset — read it back
                if (text.font != null && text.fontSharedMaterial != null
                    && text.fontSharedMaterial != text.font.material
                    && text.fontSharedMaterial.HasProperty(ShaderUtilities.ID_OutlineWidth))
                {
                    float outlineWidth = text.fontSharedMaterial.GetFloat(ShaderUtilities.ID_OutlineWidth);
                    if (outlineWidth > 0f)
                    {
                        // the outline bakes into a shared material (no live token at runtime), so a
                        // theme-token ref is carried separately on ThemeColorTarget.outlineToken —
                        // prefer it over the frozen hex so the token link survives round-trip.
                        string outlineToken = go.GetComponent<ThemeColorTarget>()?.outlineToken;
                        textElement.outlineColor = !string.IsNullOrEmpty(outlineToken)
                            ? outlineToken
                            : ColorUtils.ToHex(text.fontSharedMaterial.GetColor(ShaderUtilities.ID_OutlineColor));
                        textElement.outlineWidth = outlineWidth;
                    }
                }
                return textElement;
            }

            var aeShape = go.GetComponent<NeoShape>();
            if (aeShape != null)
            {
                // a sprite fill makes this an image element (the shape only rounds its corners)
                if (aeShape.sprite != null)
                    return new ElementSpec
                    {
                        kind = "image",
                        src = AssetDatabase.GetAssetPath(aeShape.sprite),
                        fit = aeShape.textureFitMode == ShapeTextureFit.Cover ? "cover" : null,
                        radius = aeShape.cornerRadius > 0f ? (float?)aeShape.cornerRadius : null,
                        gradient = ExportGradient(go),
                        background = go.GetComponent<ThemeColorTarget>()?.token,
                        style = go.GetComponent<ThemeShapeStyleTarget>()?.style
                    };

                bool isArc = aeShape.shape == ShapeType.Ring || aeShape.shape == ShapeType.Arc;
                return new ElementSpec
                {
                    kind = "shape",
                    shape = aeShape.shape.ToString(),
                    radius = aeShape.shape == ShapeType.RoundedRect ? (float?)aeShape.cornerRadius : null,
                    thickness = isArc ? (float?)aeShape.ringThickness : null,
                    arcStart = aeShape.shape == ShapeType.Arc ? (float?)aeShape.arcStart : null,
                    arcSweep = aeShape.shape == ShapeType.Arc ? (float?)aeShape.arcSweep : null,
                    gradient = ExportGradient(go),
                    background = go.GetComponent<ThemeColorTarget>()?.token,
                    style = go.GetComponent<ThemeShapeStyleTarget>()?.style
                };
            }

            var image = go.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
                return new ElementSpec
                {
                    kind = "image",
                    src = image.sprite != null ? AssetDatabase.GetAssetPath(image.sprite) : null,
                    gradient = ExportGradient(go),
                    background = go.GetComponent<ThemeColorTarget>()?.token
                };

            return null;
        }

        private static string ColorRefToString(ThemeColorRef reference) =>
            reference == null ? null
            : reference.useToken ? reference.token
            : ColorUtils.ToHex(reference.color);

        private static GradientSpec ExportGradient(GameObject go)
        {
            var gradient = go.GetComponent<NeoGradient>();
            if (gradient == null) return null;
            return new GradientSpec
            {
                from = ColorRefToString(gradient.colorA),
                to = ColorRefToString(gradient.colorB),
                angle = gradient.angle
            };
        }

        /// <summary>
        /// Recovers the element's per-element animation assignment from the <see cref="NeoAnimationSourceTag"/>
        /// the generator stamps — the preset NAMES (CopyTo bakes the channels but drops the asset link), so
        /// <c>"animations"</c> round-trips byte-identically. Null when the widget has no tag (e.g. the
        /// implicit factory hover/press feel, which is not a per-element assignment).
        /// </summary>
        private static ElementAnimationsSpec ExportElementAnimations(GameObject go)
        {
            var tag = go.GetComponent<NeoAnimationSourceTag>();
            if (tag == null || tag.IsEmpty) return null;
            return new ElementAnimationsSpec
            {
                hover = string.IsNullOrEmpty(tag.hover) ? null : tag.hover,
                press = string.IsNullOrEmpty(tag.press) ? null : tag.press,
                selected = string.IsNullOrEmpty(tag.selected) ? null : tag.selected,
                disabled = string.IsNullOrEmpty(tag.disabled) ? null : tag.disabled,
                loop = string.IsNullOrEmpty(tag.loop) ? null : tag.loop
            };
        }

        /// <summary>
        /// Reads an attached shape effect back to spec form by walking <see cref="ShapeEffectRegistry.All"/>
        /// — the first descriptor whose <see cref="IShapeEffectDescriptor.TryExport"/> succeeds wins
        /// (deterministic detection order, no per-effect switch). Returns null when no effect is attached.
        /// </summary>
        private static EffectSpec ExportEffect(GameObject go)
        {
            foreach (IShapeEffectDescriptor descriptor in ShapeEffectRegistry.All)
                if (descriptor != null && descriptor.TryExport(go, out var parameters))
                    return new EffectSpec
                    {
                        id = descriptor.Id,
                        parameters = parameters as Dictionary<string, object>
                                     ?? (parameters != null ? new Dictionary<string, object>(parameters) : null)
                    };
            return null;
        }

        /// <summary>
        /// Reconstructs a <see cref="NeoParticleEmitter"/> back to spec form: emitter scalars (read via
        /// SerializedObject since they are private serialized fields), the module list via
        /// <see cref="ParticleEffectRegistry"/>, and an optional <see cref="NeoParticleBurstOnSignal"/>
        /// trigger. Returns null when no emitter is attached. Deterministic; never scans all Assets.
        /// </summary>
        private static ParticleSpec ExportParticles(GameObject go)
        {
            var emitter = go.GetComponent<NeoParticleEmitter>();
            if (emitter == null) return null;

            var so = new SerializedObject(emitter);
            var spec = new ParticleSpec
            {
                capacity = so.FindProperty("capacity").intValue,
                burstCount = so.FindProperty("burstCount").intValue,
                rate = so.FindProperty("rate").floatValue,
                emitOnEnable = so.FindProperty("emitOnEnable").boolValue,
                particleShape = ((ShapeType)so.FindProperty("particleShape").enumValueIndex).ToString(),
                cornerRadiusPercent = so.FindProperty("cornerRadiusPercent").floatValue,
                sizeRange = Vec2ToArray(so.FindProperty("sizeRange").vector2Value),
                lifetimeRange = Vec2ToArray(so.FindProperty("lifetimeRange").vector2Value),
                speedRange = Vec2ToArray(so.FindProperty("speedRange").vector2Value),
                emitAngle = so.FindProperty("emitAngle").floatValue,
                emitSpread = so.FindProperty("emitSpread").floatValue,
                angularVelocityRange = Vec2ToArray(so.FindProperty("angularVelocityRange").vector2Value)
            };

            SerializedProperty list = so.FindProperty("moduleConfigs");
            for (int i = 0; i < list.arraySize; i++)
            {
                var config = list.GetArrayElementAtIndex(i).managedReferenceValue as ParticleModuleConfig;
                IParticleModuleDescriptor descriptor = ParticleEffectRegistry.GetForConfig(config);
                if (descriptor == null) continue; // unregistered config type → skip (warned at generate)
                spec.modules.Add(new ParticleModuleSpec
                {
                    id = descriptor.Id,
                    parameters = descriptor.Export(config) as Dictionary<string, object>
                });
            }

            var burst = go.GetComponent<NeoParticleBurstOnSignal>();
            if (burst != null)
            {
                spec.signal = new SignalRefSpec { category = burst.Category, name = burst.SignalName };
                int count = new SerializedObject(burst).FindProperty("count").intValue;
                if (count != 0) spec.signalCount = count;
            }

            if (go.GetComponent<NeoParticlePointerBurst>() != null) spec.atPointer = true;
            return spec;
        }

        /// <summary>
        /// Reconstructs a <see cref="NeoPointerReactor"/> (pointer-follow glow) to spec form. When the
        /// glow was authored from a theme token, <see cref="NeoPointerReactor.ColorToken"/> carries it
        /// and is emitted verbatim (keeping the token link live); otherwise the resolved color is
        /// written as <c>#RRGGBBAA</c> so it round-trips deterministically. Null when none attached.
        /// </summary>
        private static PointerGlowSpec ExportPointerGlow(GameObject go)
        {
            var reactor = go.GetComponent<NeoPointerReactor>();
            if (reactor == null) return null;
            return new PointerGlowSpec
            {
                color = !string.IsNullOrEmpty(reactor.ColorToken)
                    ? reactor.ColorToken
                    : "#" + ColorUtility.ToHtmlStringRGBA(reactor.GlowColor),
                size = reactor.GlowSize,
                softness = reactor.GlowSoftness
            };
        }

        private static float[] Vec2ToArray(Vector2 v) => new[] { v.x, v.y };

        /// <summary>
        /// Card decor on a container's layout host (the generator adds an NeoShape when a stack/grid/
        /// panel carries background/style/gradient/radius) → the same spec fields back out.
        /// </summary>
        private static void ExportContainerDecor(GameObject go, ElementSpec element)
        {
            var shape = go.GetComponent<NeoShape>();
            if (shape == null) return;
            element.gradient = ExportGradient(go);
            element.style = go.GetComponent<ThemeShapeStyleTarget>()?.style;
            if (element.gradient == null)
                element.background = go.GetComponent<ThemeColorTarget>()?.token;
            element.radius = shape.cornerRadius;
        }

        /// <summary>
        /// Reads a layout group's <see cref="RectOffset"/> padding back into spec form.
        /// Round-trip determinism rule (no marker): when the four sides are all equal it emits the
        /// uniform <see cref="ElementSpec.padding"/> (exactly as before — the only form pre-padding4
        /// specs ever produced, so they stay byte-identical); only when the sides differ does it emit
        /// the per-side <see cref="ElementSpec.padding4"/> array [left, top, right, bottom]. Note the
        /// side reorder from RectOffset (left, right, top, bottom). One accepted consequence: a uniform
        /// padding4 like [8,8,8,8] normalizes to padding:8 on round-trip — semantically identical.
        /// </summary>
        private static void ExportPadding(RectOffset padding, ElementSpec element)
        {
            if (padding == null) { element.padding = 0f; return; }
            if (padding.left == padding.right && padding.left == padding.top && padding.left == padding.bottom)
            {
                element.padding = padding.left;
                return;
            }
            element.padding4 = new float[]
            {
                padding.left,    // [0] left
                padding.top,     // [1] top
                padding.right,   // [2] right
                padding.bottom   // [3] bottom
            };
        }

        /// <summary>
        /// childAlignment → "align" (only when it deviates from the factory default, so undecorated
        /// stacks keep exporting without the field).
        /// </summary>
        private static string ExportGridAlign(GridLayoutGroup grid)
        {
            switch (grid.childAlignment)
            {
                case TextAnchor.MiddleCenter: return "center";
                case TextAnchor.MiddleRight: return "right";
                case TextAnchor.MiddleLeft: return "left";
                default: return null; // UpperLeft is the factory default
            }
        }

        private static string ExportStackAlign(HorizontalOrVerticalLayoutGroup stack)
        {
            switch (stack.childAlignment)
            {
                case TextAnchor.MiddleCenter: return "center";
                case TextAnchor.MiddleRight: return "right";
                // MiddleLeft is the hstack default but a deliberate "left" on a vstack
                case TextAnchor.MiddleLeft: return stack is VerticalLayoutGroup ? "left" : null;
                default: return null;
            }
        }

        private static TMP_Text FindChildText(GameObject go, string childName)
        {
            Transform child = go.transform.Find(childName);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        /// <summary>
        /// If the layout host carries a <see cref="UIBoundList"/>, exports it as bind + item (the row
        /// template) and signals the caller to skip child iteration (spawned rows are data, not
        /// structure). Returns false for an ordinary list/grid.
        /// </summary>
        private static bool TryExportBoundList(GameObject host, ElementSpec element)
        {
            var binder = host.GetComponent<UIBoundList>();
            if (binder == null) return false;
            if (!binder.source.isDefault) element.bind = binder.source.ToString();
            if (binder.template != null) element.item = ExportElement(binder.template, inLayout: true);
            return true;
        }

        /// <summary> Reverse-maps a widget's Icon child glyph back to its Lucide name. </summary>
        private static string ExportIconName(GameObject go)
        {
            TMP_Text iconText = FindChildText(go, UIWidgetFactory.IconName);
            if (iconText == null || string.IsNullOrEmpty(iconText.text)) return null;
            return IconMap.TryGetName(iconText.text[0], out string name) ? name : null;
        }

        private static float? ExportBadgeCount(GameObject go)
        {
            Transform badge = go.transform.Find(UIWidgetFactory.BadgeName);
            UIBadge component = badge != null ? badge.GetComponent<UIBadge>() : null;
            return component != null ? (float?)component.count : null;
        }

        /// <summary> A widget's optional first-class domain signal (Plan 3 B); default = unset = null. </summary>
        private static SignalRefSpec ExportDomainSignal(CategoryNameId domainSignal) =>
            domainSignal == null || domainSignal.isDefault
                ? null
                : new SignalRefSpec { category = domainSignal.Category, name = domainSignal.Name };

        /// <summary> Anchor preset, size and position — only the parts the generator re-applies. </summary>
        private static void ExportGeometry(ElementSpec element, RectTransform rect, bool inLayout)
        {
            // normalize to ±180 so an authored -45 ribbon round-trips as -45, not 315
            float zRotation = rect.localEulerAngles.z;
            if (zRotation > 180f) zRotation -= 360f;
            if (Mathf.Abs(zRotation) > 0.01f) element.rotation = zRotation;

            // Constraint+offset model: a NeoLayoutTag marks an element placed through the layout
            // model — emit `layout` (reverse-mapped from the live RectTransform, gated by the tag so
            // anchor/offset aliasing can't break byte-identity) and SKIP the legacy anchor/position/
            // size/flex emit entirely. Absence of the tag ⇒ legacy path below, byte-identical as before.
            var layoutTag = rect.GetComponent<NeoLayoutTag>();
            if (layoutTag != null)
            {
                element.layout = ConstraintLayout.Detect(rect, layoutTag);
                return;
            }

            // a string size variant (sm/md/lg) owns the "size" key — the factory derives the
            // pixel height from it, so exporting geometry too would collide
            bool sizeOwnedByVariant = !string.IsNullOrEmpty(element.sizeVariant);
            if (inLayout)
            {
                if (sizeOwnedByVariant) return;
                var layoutElement = rect.GetComponent<LayoutElement>();
                if (layoutElement != null && (layoutElement.preferredWidth > 0f || layoutElement.preferredHeight > 0f))
                    element.size = new[]
                    {
                        Mathf.Max(0f, layoutElement.preferredWidth), // -1 = unset, never export it
                        Mathf.Max(0f, layoutElement.preferredHeight)
                    };
                // flex collapses to one number — the generator re-derives the axis from the
                // parent stack's orientation (spacers never reach here; they export earlier)
                if (layoutElement != null && element.kind != "list" && element.kind != "scroll")
                {
                    float flex = Mathf.Max(layoutElement.flexibleWidth, layoutElement.flexibleHeight);
                    if (flex > 0f) element.flex = flex;
                }
                return;
            }

            element.anchor = UIWidgetFactory.DetectAnchor(rect);
            Vector2 size = rect.sizeDelta;
            if (!sizeOwnedByVariant && (size.x > 0f || size.y > 0f))
                element.size = new[] { Mathf.Max(0f, size.x), Mathf.Max(0f, size.y) };
            Vector2 position = rect.anchoredPosition;
            if (Mathf.Abs(position.x) > 1e-3f || Mathf.Abs(position.y) > 1e-3f)
                element.position = new[] { position.x, position.y };
        }

        public static FlowSpec ExportFlow(FlowGraph graph)
        {
            var flowSpec = new FlowSpec { name = string.IsNullOrEmpty(graph.graphName) ? graph.name : graph.graphName };

            StartNode start = graph.nodes.OfType<StartNode>().FirstOrDefault();
            flowSpec.start = start?.GetFirstConnectedOutput()?.toNode;

            foreach (UINode node in graph.nodes.OfType<UINode>())
            {
                var nodeSpec = new FlowNodeSpec { name = node.name };
                foreach (UINode.ViewRef shown in node.showViews)
                    nodeSpec.views.Add($"{shown.category}/{shown.viewName}");
                foreach (UINode.ViewRef hidden in node.hideViews)
                    nodeSpec.hide.Add($"{hidden.category}/{hidden.viewName}");
                foreach (FlowEdge edge in node.outputs.Where(e => !string.IsNullOrEmpty(e.toNode)))
                {
                    nodeSpec.next.Add(new FlowEdgeSpec
                    {
                        to = edge.toNode,
                        allowsBack = edge.allowsBack,
                        transition = edge.transition,
                        trigger = edge.trigger
                    });
                }
                flowSpec.nodes.Add(nodeSpec);
            }
            return flowSpec;
        }

        // Menu export (ExportCatalog/ExportItem/UnmapKind) moved to Menus/UISpecExporter.Menus.cs
        // (Wave 7 Task 7.1) — UnmapKind is now NeoMenuItemKinds.UnmapKind.
    }
}
