using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI.Menus
{
    /// <summary>
    /// Builds a settings/cheats menu from a <see cref="MenuCatalog"/> at runtime by cloning row prefabs
    /// from a <see cref="MenuWidgetLibrary"/> and wiring each through a <see cref="MenuControlBinder"/>.
    /// This is the dynamic / CBN-style population path. For the author-time path the spec generator bakes
    /// the same widgets+binders into a view prefab — there <see cref="buildOnStart"/> is false and this
    /// component only registers the catalog (the baked binders self-wire).
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Menus/Menu Presenter")]
    public class MenuPresenter : MonoBehaviour
    {
        [Tooltip("The catalog to present.")]
        public MenuCatalog catalog;
        [Tooltip("Row prefab library; falls back to AEUISettings.menuWidgets when unset.")]
        public MenuWidgetLibrary library;
        [Tooltip("Where control rows (or per-group panels) are parented.")]
        public RectTransform contentRoot;
        [Tooltip("Where category tabs are parented (optional — needed for grouped menus).")]
        public RectTransform categoryNavRoot;
        [Tooltip("Build the menu by instantiating rows on Start. Leave off for baked/author-time views.")]
        public bool buildOnStart = true;

        private readonly Dictionary<string, GameObject> _groupPanels = new Dictionary<string, GameObject>();
        private readonly List<MenuControlBinder> _binders = new List<MenuControlBinder>();
        private bool _built;

        public IReadOnlyList<MenuControlBinder> Binders => _binders;

        protected MenuWidgetLibrary Library =>
            library != null ? library : (AEUISettings.instance != null ? AEUISettings.instance.menuWidgets : null);

        protected virtual void Awake()
        {
            if (catalog != null) UserSettingsService.RegisterCatalog(catalog);
        }

        protected virtual void Start()
        {
            if (buildOnStart) Build();
        }

        /// <summary> Instantiates the menu. Idempotent. </summary>
        public void Build()
        {
            if (_built || catalog == null || contentRoot == null) return;
            MenuWidgetLibrary lib = Library;
            if (lib == null)
            {
                Debug.LogWarning($"[MenuPresenter] No MenuWidgetLibrary for '{catalog.Id}'.", this);
                return;
            }
            _built = true;
            UserSettingsService.RegisterCatalog(catalog);

            List<string> groups = EffectiveGroups();
            bool grouped = groups.Count > 1 && categoryNavRoot != null;

            foreach (string group in groups)
            {
                RectTransform parent = grouped ? CreateGroupPanel(group, lib) : contentRoot;
                foreach (MenuItemDefinition item in catalog.ItemsInGroup(group))
                    BuildRow(item, parent, lib);
            }

            if (grouped)
            {
                BuildNav(groups, lib);
                ShowGroup(string.IsNullOrEmpty(catalog.startGroup) ? groups[0] : catalog.startGroup);
            }
        }

        private List<string> EffectiveGroups()
        {
            var groups = new List<string>();
            if (catalog.groups != null)
                foreach (string g in catalog.groups)
                    if (!string.IsNullOrEmpty(g)) groups.Add(g);
            if (groups.Count == 0) groups.Add(string.Empty);
            return groups;
        }

        private RectTransform CreateGroupPanel(string group, MenuWidgetLibrary lib)
        {
            GameObject panel;
            if (lib.categoryPanel != null)
            {
                panel = Instantiate(lib.categoryPanel, contentRoot);
            }
            else
            {
                panel = new GameObject($"Panel_{group}", typeof(RectTransform), typeof(VerticalLayoutGroup));
                panel.transform.SetParent(contentRoot, false);
            }
            panel.name = $"Panel_{group}";
            _groupPanels[group] = panel;
            return (RectTransform)panel.transform;
        }

        private void BuildRow(MenuItemDefinition item, RectTransform parent, MenuWidgetLibrary lib)
        {
            GameObject prefab = lib.RowFor(item.kind);
            if (prefab == null)
            {
                Debug.LogWarning($"[MenuPresenter] No row prefab for kind '{item.kind}' ('{item.Id}').", this);
                return;
            }
            GameObject row = Instantiate(prefab, parent);
            row.name = $"Row_{item.Name}";
            MenuControlBinder binder = row.GetComponent<MenuControlBinder>();
            if (binder == null) binder = row.AddComponent<MenuControlBinder>();
            TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
            binder.Configure(catalog, item, label);
            binder.Wire();
            _binders.Add(binder);
        }

        private void BuildNav(List<string> groups, MenuWidgetLibrary lib)
        {
            if (lib.categoryTab == null) return;
            foreach (string group in groups)
            {
                GameObject tab = Instantiate(lib.categoryTab, categoryNavRoot);
                tab.name = $"Tab_{group}";
                TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = group;
                UIButton button = tab.GetComponentInChildren<UIButton>(true);
                if (button != null)
                {
                    string captured = group;
                    button.onClickEvent.AddListener(() => ShowGroup(captured));
                }
            }
        }

        public void ShowGroup(string group)
        {
            foreach (KeyValuePair<string, GameObject> pair in _groupPanels)
                pair.Value.SetActive(string.Equals(pair.Key, group, System.StringComparison.Ordinal));
        }
    }
}
