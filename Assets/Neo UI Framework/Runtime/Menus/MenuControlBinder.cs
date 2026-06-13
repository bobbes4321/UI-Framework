using TMPro;
using UnityEngine;

namespace Neo.UI.Menus
{
    /// <summary>
    /// Binds a built widget (UIToggle / UISlider / UIStepper / UIDropdown / UIButton) to a
    /// <see cref="MenuItemDefinition"/> and <see cref="UserSettingsService"/>. The single piece both the
    /// author-time generator (which bakes widgets into a view prefab) and the runtime presenter (which
    /// instantiates row prefabs) rely on: on Start it applies the stored/default value to the widget
    /// (WYSIWYG) and forwards every change back to the service. Lives on the control's GameObject (or a
    /// row root containing it).
    /// </summary>
    [AddComponentMenu("Neo/UI/Menus/Menu Control Binder")]
    public class MenuControlBinder : MonoBehaviour
    {
        [Tooltip("Catalog owning the definition; registered with the service on Awake.")]
        public MenuCatalog catalog;
        [Tooltip("Definition addressing — must match an item in the catalog.")]
        public string category = CategoryNameId.DefaultCategory;
        public string itemName = CategoryNameId.DefaultName;
        [Tooltip("Optional label text element set from the definition's label.")]
        public TMP_Text labelTarget;

        private MenuItemDefinition _def;
        private bool _wired;

        public MenuItemDefinition Definition =>
            _def ?? (_def = catalog != null ? catalog.Find(category, itemName) : null);

        /// <summary> Editor/generator entry point: assign the catalog + addressing in one call. </summary>
        public void Configure(MenuCatalog owningCatalog, MenuItemDefinition item, TMP_Text label = null)
        {
            catalog = owningCatalog;
            category = item.Category;
            itemName = item.Name;
            _def = item;
            if (label != null) labelTarget = label;
        }

        private void Awake()
        {
            if (catalog != null) UserSettingsService.RegisterCatalog(catalog);
        }

        private void Start()
        {
            Wire();
        }

        public void Wire()
        {
            if (_wired) return;
            MenuItemDefinition def = Definition;
            if (def == null)
            {
                Debug.LogWarning($"[MenuControlBinder] No catalog entry for '{category}/{itemName}' on '{name}'.", this);
                return;
            }
            _wired = true;

            if (labelTarget != null && !string.IsNullOrEmpty(def.label)) labelTarget.text = def.label;

            switch (def.kind)
            {
                case MenuControlKind.Toggle:
                case MenuControlKind.Switch: WireToggle(def); break;
                case MenuControlKind.Slider: WireSlider(def); break;
                case MenuControlKind.Stepper: WireStepper(def); break;
                case MenuControlKind.Dropdown: WireDropdown(def); break;
                case MenuControlKind.Button: WireButton(def); break;
                // KeyRebind is handled by UIRebindControl; Label has no value.
            }
        }

        private void WireToggle(MenuItemDefinition def)
        {
            var toggle = GetComponentInChildren<UIToggle>(true);
            if (toggle == null) return;
            toggle.SetIsOn(UserSettingsService.Get<bool>(def.Category, def.Name), animateChange: false);
            toggle.OnValueChanged += (isOn, _) => UserSettingsService.Set(def.Category, def.Name, isOn);
        }

        private void WireSlider(MenuItemDefinition def)
        {
            var slider = GetComponentInChildren<UISlider>(true);
            if (slider == null) return;
            slider.minValue = def.min;
            slider.maxValue = def.max;
            slider.wholeNumbers = def.wholeNumbers;
            slider.SetValueWithoutNotify(UserSettingsService.Get<float>(def.Category, def.Name));
            if (def.emitOnDrag)
                slider.onValueChanged.AddListener(v => UserSettingsService.SetValue(def, v, commit: false));
            if (def.emitOnRelease)
                slider.OnValueCommitted.AddListener(v => UserSettingsService.SetValue(def, v, commit: true));
            else if (!def.emitOnDrag)
                slider.onValueChanged.AddListener(v => UserSettingsService.SetValue(def, v, commit: true));
        }

        private void WireStepper(MenuItemDefinition def)
        {
            var stepper = GetComponentInChildren<UIStepper>(true);
            if (stepper == null) return;
            stepper.minValue = def.min;
            stepper.maxValue = def.max;
            stepper.stepSize = def.step;
            stepper.wholeNumbers = def.wholeNumbers;
            stepper.currentValue = UserSettingsService.Get<float>(def.Category, def.Name);
            stepper.OnValueChanged.AddListener(v => UserSettingsService.SetValue(def, v, commit: true));
        }

        private void WireDropdown(MenuItemDefinition def)
        {
            var dropdown = GetComponentInChildren<UIDropdown>(true);
            if (dropdown == null) return;
            dropdown.SetStringOptions(def.options);
            dropdown.SetValueWithoutNotify(UserSettingsService.Get<int>(def.Category, def.Name));
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(i => UserSettingsService.SetValue(def, i, commit: true));
        }

        private void WireButton(MenuItemDefinition def)
        {
            var button = GetComponentInChildren<UIButton>(true);
            if (button == null) return;
            bool isCheat = catalog != null && catalog.ChangeSignalCategory == UserSettingsService.CheatCategory;
            button.onClickEvent.AddListener(() =>
            {
                if (isCheat) UserSettingsService.FireCheat(def.Category, def.Name);
                else Signals.Send(UserSettingsService.SettingsCategory, $"{def.Category}/{def.Name}");
            });
        }
    }
}
