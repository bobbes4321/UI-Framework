using System.Collections.Generic;
using System.Linq;
using Neo.UI.Menus;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The generator's menus (settings/cheats) pipeline — moved verbatim out of
    /// <c>UISpecGenerator.cs</c> (Wave 7 Task 7.1, audit E3) into its own partial-class file so the
    /// god-file shrinks and the menu-kind vocabulary has a home next to <see cref="NeoMenuItemKinds"/>.
    /// The old <c>MapKind</c>/<c>BuildMenuRow</c> switches are gone — kind → runtime
    /// <see cref="MenuControlKind"/> mapping and per-kind row construction now route through the
    /// registry, so a project-registered <see cref="MenuItemKindDescriptor"/> is picked up here for
    /// free. See <see cref="NeoMenuItemKinds.MapKind"/> for the runtime-boundary caveat: a kind with no
    /// <see cref="MenuControlKind"/> mapping still parses/exports/shows in the inspector, but its baked
    /// row degrades to a non-interactive Label (logged) because <see cref="MenuItemDefinition.kind"/>
    /// is a closed runtime enum — extending that is out of this task's scope (Runtime/ is off limits).
    /// </summary>
    public static partial class UISpecGenerator
    {
        // catalogs generated this run, so the view-embedded "settings"/"cheats" elements can find them
        private static readonly Dictionary<string, MenuCatalog> s_catalogs =
            new Dictionary<string, MenuCatalog>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, InputActionAsset> s_catalogInputAssets =
            new Dictionary<string, InputActionAsset>(System.StringComparer.Ordinal);

        private static void GenerateMenuCatalog(MenuCatalogSpec spec, NeoUISettings settings, GenerateReport report)
        {
            EnsureFolder($"{GeneratedRoot}/Menus");
            bool isCheat = spec.kind == MenuCatalogSpec.CheatKind;
            string assetName = Sanitize($"{spec.category}_{spec.menuName}");
            string path = $"{GeneratedRoot}/Menus/{assetName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<MenuCatalog>(path);
            bool typeMismatch = existing != null && (isCheat ? !(existing is CheatCatalog) : !(existing is SettingsCatalog));
            bool created = existing == null || typeMismatch;
            MenuCatalog catalog;
            if (created)
            {
                if (typeMismatch) AssetDatabase.DeleteAsset(path);
                catalog = (MenuCatalog)ScriptableObject.CreateInstance(isCheat ? typeof(CheatCatalog) : typeof(SettingsCatalog));
            }
            else catalog = existing;

            catalog.category = spec.category;
            catalog.menuName = spec.menuName;
            catalog.groups = new List<string>(spec.groups);
            catalog.startGroup = spec.start;
            catalog.inputActionAssetPath = spec.inputActionAsset;
            if (catalog is CheatCatalog cheatCatalog) cheatCatalog.favouritesEnabled = spec.favourites;

            catalog.items.Clear();
            foreach (MenuItemSpec item in spec.items)
            {
                catalog.items.Add(ToDefinition(item));
                // Extension seam: a registered kind names which id database (if any) its items
                // pre-register into — replaces the old hand-written switch over MapKind(item.kind).
                if (NeoMenuItemKinds.TryGet(item.kind, out MenuItemKindDescriptor descriptor)
                    && descriptor.preRegisterDatabase != null)
                {
                    IdDatabase database = descriptor.preRegisterDatabase(settings);
                    RegisterId(database, item.category, item.name);
                }
            }

            if (created) AssetDatabase.CreateAsset(catalog, path);
            EditorUtility.SetDirty(catalog);

            s_catalogs[catalog.Id] = catalog;
            if (!string.IsNullOrEmpty(spec.inputActionAsset))
            {
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(spec.inputActionAsset);
                if (asset == null)
                    report.issues.Add($"Catalog '{catalog.Id}': input action asset '{spec.inputActionAsset}' not found");
                else s_catalogInputAssets[catalog.Id] = asset;
            }

            (created ? report.created : report.updated).Add(
                $"{(isCheat ? "Cheat" : "Settings")} catalog '{catalog.Id}' ({catalog.items.Count} items) → {path}");
        }

        /// <summary>
        /// Populates the in-memory catalog registry from a spec's settings/cheats WITHOUT writing any
        /// asset — so an in-memory render (the agent <c>preview</c> path, which never commits prefabs or
        /// catalog SOs) can still resolve a view's embedded <c>settings</c>/<c>cheats</c> elements and
        /// show real menu rows. Mirrors <see cref="GenerateMenuCatalog"/>'s catalog population exactly;
        /// the only difference is no <c>AssetDatabase.CreateAsset</c>. Clears the registry first so a
        /// preview never reads a stale catalog left by an earlier generate.
        /// </summary>
        public static void PrepareCatalogsInMemory(UISpec spec, NeoUISettings settings)
        {
            s_catalogs.Clear();
            s_catalogInputAssets.Clear();
            if (spec == null) return;
            var report = new GenerateReport();
            if (spec.settings != null)
                foreach (MenuCatalogSpec catalog in spec.settings) BuildCatalogInMemory(catalog, settings, report);
            if (spec.cheats != null)
                foreach (MenuCatalogSpec catalog in spec.cheats) BuildCatalogInMemory(catalog, settings, report);
        }

        private static void BuildCatalogInMemory(MenuCatalogSpec spec, NeoUISettings settings, GenerateReport report)
        {
            bool isCheat = spec.kind == MenuCatalogSpec.CheatKind;
            var catalog = (MenuCatalog)ScriptableObject.CreateInstance(isCheat ? typeof(CheatCatalog) : typeof(SettingsCatalog));
            catalog.category = spec.category;
            catalog.menuName = spec.menuName;
            catalog.groups = new List<string>(spec.groups);
            catalog.startGroup = spec.start;
            catalog.inputActionAssetPath = spec.inputActionAsset;
            if (catalog is CheatCatalog cheatCatalog) cheatCatalog.favouritesEnabled = spec.favourites;
            catalog.items.Clear();
            foreach (MenuItemSpec item in spec.items) catalog.items.Add(ToDefinition(item));
            s_catalogs[catalog.Id] = catalog;
            if (!string.IsNullOrEmpty(spec.inputActionAsset))
            {
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(spec.inputActionAsset);
                if (asset != null) s_catalogInputAssets[catalog.Id] = asset;
            }
        }

        private static MenuItemDefinition ToDefinition(MenuItemSpec item) => new MenuItemDefinition
        {
            category = item.category,
            name = item.name,
            kind = NeoMenuItemKinds.MapKind(item.kind),
            label = item.label,
            tooltip = item.tooltip,
            group = item.group,
            persisted = item.persisted,
            min = item.min ?? 0f,
            max = item.max ?? 1f,
            step = item.step ?? 1f,
            wholeNumbers = item.wholeNumbers,
            defaultValue = item.value,
            options = item.options != null ? new List<string>(item.options) : new List<string>(),
            emitOnDrag = item.emitOnDrag,
            emitOnRelease = item.emitOnRelease,
            inputAction = item.inputAction,
            bindingIndex = item.bindingIndex
        };

        private static MenuCatalog LookupCatalog(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            CategoryNameId.Parse(id, out string category, out string name);
            string key = $"{category}/{name}";
            if (s_catalogs.TryGetValue(key, out MenuCatalog catalog)) return catalog;
            string assetName = Sanitize($"{category}_{name}");
            return AssetDatabase.LoadAssetAtPath<MenuCatalog>($"{GeneratedRoot}/Menus/{assetName}.asset");
        }

        /// <summary>
        /// Builds a baked menu (tabbar + per-group panels, or a flat row stack) from a generated catalog,
        /// attaching a <see cref="MenuControlBinder"/> per control. A <see cref="MenuPresenter"/> on the
        /// root registers the catalog at runtime; the baked binders self-wire (no runtime rebuild).
        /// </summary>
        private static GameObject BuildMenuElement(ElementSpec element, RectTransform parent,
            NeoUISettings settings, GenerateReport report, ViewBuild build)
        {
            MenuCatalog catalog = LookupCatalog(element.catalog);
            GameObject root = UIWidgetFactory.CreateStack(parent, vertical: true, padding: 0f, spacing: 8f);
            root.name = "Menu";

            if (catalog == null)
            {
                report.issues.Add($"Menu element references catalog '{element.catalog}' which was not generated");
                return root;
            }

            MenuPresenter presenter = catalog is CheatCatalog
                ? root.AddComponent<CheatMenu>()
                : root.AddComponent<SettingsMenu>();
            presenter.catalog = catalog;
            presenter.buildOnStart = false;
            presenter.contentRoot = (RectTransform)root.transform;

            InputActionAsset rebindAsset = s_catalogInputAssets.TryGetValue(catalog.Id, out InputActionAsset a) ? a : null;
            List<string> groups = catalog.groups != null
                ? catalog.groups.Where(g => !string.IsNullOrEmpty(g)).ToList() : new List<string>();

            if (groups.Count > 0)
            {
                var tabs = groups.Select(g => (g, g, (string)null)).ToList();
                GameObject tabBar = UIWidgetFactory.CreateTabBar((RectTransform)root.transform, catalog.category, tabs);
                foreach (string group in groups)
                {
                    Transform tab = tabBar.transform.Find(UIWidgetFactory.TabName(group));
                    if (tab != null) build.tabPanelLinks.Add((tab.GetComponent<UITab>(), group));
                }
                foreach (string group in groups)
                {
                    GameObject panel = UIWidgetFactory.CreatePanel((RectTransform)root.transform, catalog.category, group, 12f, 8f);
                    panel.name = $"Panel_{group}";
                    UIPanel panelComponent = panel.GetComponent<UIPanel>();
                    string panelKey = panelComponent.id.ToString();
                    if (!build.panels.ContainsKey(panelKey)) build.panels[panelKey] = panelComponent;
                    RegisterId(settings.panelIds, catalog.category, group);
                    foreach (MenuItemDefinition def in catalog.ItemsInGroup(group))
                        BuildMenuRow(catalog, def, (RectTransform)panel.transform, rebindAsset, settings, report);
                }
            }
            else
            {
                foreach (MenuItemDefinition def in catalog.items)
                    BuildMenuRow(catalog, def, (RectTransform)root.transform, rebindAsset, settings, report);
            }
            return root;
        }

        /// <summary>
        /// Builds one control row. Wave 7 Task 7.1: this used to be a hand-written switch over
        /// <see cref="MenuControlKind"/> (audit E3) — it now dispatches through the
        /// <see cref="NeoMenuItemKinds"/> descriptor whose <see cref="MenuItemKindDescriptor.controlKind"/>
        /// matches, so a project that registers a new kind sharing (or extending) the row-build seam is
        /// picked up automatically. An unmapped kind (should not happen for anything that made it through
        /// <see cref="NeoMenuItemKinds.MapKind"/>) is warned and skipped — no silent failure.
        /// </summary>
        private static void BuildMenuRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            if (NeoMenuItemKinds.TryGetByControlKind(def.kind, out MenuItemKindDescriptor descriptor)
                && descriptor.buildRow != null)
            {
                descriptor.buildRow(catalog, def, parent, rebindAsset, settings, report);
                return;
            }
            Debug.LogWarning($"[Neo.UI] Menu row '{def.Id}': no build recipe registered for control kind " +
                $"'{def.kind}' — row skipped.");
        }

        // ---- per-kind row builders (registered as MenuItemKindDescriptor.buildRow by NeoMenuItemKinds) ----

        internal static void BuildLabelRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            UIWidgetFactory.CreateLabel(parent, def.label, UIWidgetFactory.TokenTextMuted, 20f,
                name: "Header", alignment: TMPro.TextAlignmentOptions.MidlineLeft,
                textStyle: UIWidgetFactory.TextStyleCaption);
        }

        internal static void BuildButtonRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            GameObject button = UIWidgetFactory.CreateButton(parent, def.Category, def.Name, def.label,
                variant: UIWidgetFactory.VariantSecondary);
            AddBinder(button, catalog, def, null);
            RegisterId(settings.buttonIds, def.Category, def.Name);
        }

        internal static void BuildKeyRebindRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            GameObject rebindBtn = UIWidgetFactory.CreateButton(parent, def.Category, def.Name + "_Rebind",
                "—", variant: UIWidgetFactory.VariantSecondary);
            GameObject row = UIWidgetFactory.CreateMenuRow(parent, $"Row_{def.Name}", def.label, rebindBtn);
            var rebind = row.AddComponent<UIRebindControl>();
            rebind.Configure(catalog, def, rebindAsset);
            rebind.rebindButton = rebindBtn.GetComponent<UIButton>();
            rebind.bindingLabel = rebindBtn.transform.Find(UIWidgetFactory.LabelName)?.GetComponent<TMP_Text>();
            rebind.labelTarget = row.transform.Find(UIWidgetFactory.LabelName)?.GetComponent<TMP_Text>();
            RegisterId(settings.buttonIds, def.Category, def.Name + "_Rebind");
        }

        internal static void BuildToggleRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            GameObject control = UIWidgetFactory.CreateToggle(parent, def.Category, def.Name, "");
            if (ParseBool(def.defaultValue)) UIWidgetFactory.BakeToggleOn(control);
            RegisterId(settings.toggleIds, def.Category, def.Name);
            FinishControlRow(control, catalog, def, parent);
        }

        internal static void BuildSwitchRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            GameObject control = UIWidgetFactory.CreateSwitch(parent, def.Category, def.Name);
            if (ParseBool(def.defaultValue)) UIWidgetFactory.BakeToggleOn(control);
            RegisterId(settings.toggleIds, def.Category, def.Name);
            FinishControlRow(control, catalog, def, parent);
        }

        internal static void BuildSliderRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            GameObject control = UIWidgetFactory.CreateSlider(parent, def.Category, def.Name,
                def.min, def.max, ParseFloat(def.defaultValue, def.min));
            RegisterId(settings.sliderIds, def.Category, def.Name);
            FinishControlRow(control, catalog, def, parent);
        }

        internal static void BuildStepperRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            GameObject control = UIWidgetFactory.CreateStepper(parent, def.Category, def.Name,
                def.min, def.max, ParseFloat(def.defaultValue, def.min), def.step);
            RegisterId(settings.buttonIds, def.Category, def.Name + UIWidgetFactory.StepperButtonSuffixMinus);
            RegisterId(settings.buttonIds, def.Category, def.Name + UIWidgetFactory.StepperButtonSuffixPlus);
            FinishControlRow(control, catalog, def, parent);
        }

        internal static void BuildDropdownRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            GameObject control = UIWidgetFactory.CreateDropdown(parent, def.Category, def.Name, def.options,
                (int)ParseFloat(def.defaultValue, 0f));
            RegisterId(settings.dropdownIds, def.Category, def.Name);
            FinishControlRow(control, catalog, def, parent);
        }

        /// <summary> Shared tail for every value-bearing control row: wrap in a labeled menu row, then
        /// attach the binder. Extracted from the old switch's fallthrough tail so each per-kind builder
        /// above stays a one-recipe method a project's own descriptor can mirror. </summary>
        private static void FinishControlRow(GameObject control, MenuCatalog catalog, MenuItemDefinition def,
            RectTransform parent)
        {
            GameObject rowGo = UIWidgetFactory.CreateMenuRow(parent, $"Row_{def.Name}", def.label, control);
            AddBinder(control, catalog, def, rowGo.transform.Find(UIWidgetFactory.LabelName)?.GetComponent<TMP_Text>());
        }

        private static void AddBinder(GameObject control, MenuCatalog catalog, MenuItemDefinition def, TMP_Text label)
        {
            var binder = control.AddComponent<MenuControlBinder>();
            binder.Configure(catalog, def, label);
        }
    }
}
