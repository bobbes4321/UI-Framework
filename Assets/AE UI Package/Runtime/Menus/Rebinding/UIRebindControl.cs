using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlterEyes.UI.Menus
{
    /// <summary>
    /// A rebind row: shows a binding's current display string and rebinds it interactively on click.
    /// Resolves its <see cref="InputAction"/> from the referenced asset using the catalog item's
    /// <see cref="MenuItemDefinition.inputAction"/> path + <see cref="MenuItemDefinition.bindingIndex"/>.
    /// The new binding's display string is written back as the setting's value (so it signals + persists
    /// alongside the JSON overrides the service stores).
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Menus/UI Rebind Control")]
    public class UIRebindControl : MonoBehaviour
    {
        [Tooltip("Catalog owning the KeyRebind definition.")]
        public MenuCatalog catalog;
        public string category = CategoryNameId.DefaultCategory;
        public string itemName = CategoryNameId.DefaultName;

        [Tooltip("The action asset whose binding is rebound (overrides persist per asset).")]
        public InputActionAsset actionAsset;

        [Header("UI")]
        public TMP_Text labelTarget;
        public TMP_Text bindingLabel;
        public UIButton rebindButton;
        public UIButton resetButton;

        [Tooltip("Shown while waiting for input.")]
        public string waitingText = "Press a key…";

        private InputActionRebindingExtensions.RebindingOperation _operation;
        private MenuItemDefinition _def;

        public MenuItemDefinition Definition =>
            _def ?? (_def = catalog != null ? catalog.Find(category, itemName) : null);

        public void Configure(MenuCatalog owningCatalog, MenuItemDefinition item, InputActionAsset asset)
        {
            catalog = owningCatalog;
            category = item.Category;
            itemName = item.Name;
            _def = item;
            actionAsset = asset;
        }

        private void Awake()
        {
            if (catalog != null) UserSettingsService.RegisterCatalog(catalog);
            if (actionAsset != null) InputRebindService.LoadOverrides(actionAsset);
        }

        private void Start()
        {
            MenuItemDefinition def = Definition;
            if (def != null && labelTarget != null && !string.IsNullOrEmpty(def.label)) labelTarget.text = def.label;
            if (rebindButton != null) rebindButton.onClickEvent.AddListener(BeginRebind);
            if (resetButton != null) resetButton.onClickEvent.AddListener(ResetBinding);
            RefreshDisplay();
        }

        private InputAction ResolveAction()
        {
            MenuItemDefinition def = Definition;
            if (def == null || actionAsset == null || string.IsNullOrEmpty(def.inputAction)) return null;
            return actionAsset.FindAction(def.inputAction);
        }

        public void RefreshDisplay()
        {
            InputAction action = ResolveAction();
            if (bindingLabel == null) return;
            bindingLabel.text = action != null
                ? InputRebindService.DisplayString(action, Definition.bindingIndex)
                : "—";
        }

        public void BeginRebind()
        {
            InputAction action = ResolveAction();
            if (action == null) return;
            if (bindingLabel != null) bindingLabel.text = waitingText;
            _operation = InputRebindService.StartRebind(
                action, Definition.bindingIndex, actionAsset,
                onComplete: OnRebound,
                onCancel: RefreshDisplay);
        }

        private void OnRebound()
        {
            RefreshDisplay();
            MenuItemDefinition def = Definition;
            InputAction action = ResolveAction();
            if (def != null && action != null)
                UserSettingsService.Set(def.Category, def.Name, InputRebindService.DisplayString(action, def.bindingIndex));
        }

        public void ResetBinding()
        {
            InputAction action = ResolveAction();
            if (action == null) return;
            InputRebindService.ResetBinding(action, Definition.bindingIndex, actionAsset);
            OnRebound();
        }

        private void OnDisable()
        {
            _operation?.Dispose();
            _operation = null;
        }
    }
}
