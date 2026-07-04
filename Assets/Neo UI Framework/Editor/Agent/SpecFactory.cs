using System.Collections.Generic;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Builds new spec nodes with sensible defaults, and deep-clones existing ones. Clones go through
    /// the spec's own JSON round-trip (<c>ToJsonObject</c> → <c>Parse</c>) so a duplicate is exactly
    /// what a regenerate would see — no field is ever missed. Shared by native-authoring
    /// (<see cref="Authoring.NeoSceneAuthoring"/>) and anything else that needs to stamp out a default
    /// spec node.
    /// </summary>
    public static class SpecFactory
    {
        public static ElementSpec NewElement(string kind)
        {
            // "scroll" is a forgiving alias for "list" (ElementSpec.Parse normalizes the same way for
            // spec-authored elements) — native-authoring callers (NeoCreateMenu's "Scroll" item,
            // NeoWidgetPalette) mint an ElementSpec directly here, bypassing Parse, so the alias is
            // normalized here too rather than reintroducing a dual-accept in the generator switch.
            if (kind == "scroll") kind = "list";
            var element = new ElementSpec { kind = kind };
            switch (kind)
            {
                case "button":
                    element.label = "Button";
                    element.id = "Action/Button";
                    element.variant = "primary";
                    break;
                case "text":
                    element.label = "Text";
                    break;
                case "toggle":
                    element.label = "Toggle";
                    element.id = "Toggle/New";
                    break;
                case "switch":
                    element.label = "Switch";
                    element.id = "Switch/New";
                    break;
                case "tab":
                    element.label = "Tab";
                    element.id = "Tab/New";
                    break;
                case "slider":
                    element.id = "Slider/New";
                    element.min = 0; element.max = 1; element.value = 0.5f;
                    break;
                case "progress":
                    element.min = 0; element.max = 1; element.value = 0.5f;
                    break;
                case "stepper":
                    element.id = "Stepper/New";
                    element.min = 0; element.max = 10; element.value = 0; element.step = 1;
                    break;
                case "vstack":
                case "hstack":
                    element.padding = 16; element.spacing = 8;
                    break;
                case "grid":
                    element.padding = 16; element.spacing = 8; element.columns = 2;
                    break;
                case "icon":
                    element.icon = "star";
                    break;
                case "shape":
                    element.shape = "roundedRect";
                    break;
            }
            return element;
        }

        /// <summary> Creates a panel sized to fill, with a generated id, for a tab to control. </summary>
        public static ElementSpec NewPanel(string id)
        {
            return new ElementSpec { kind = "panel", id = id, anchor = "Stretch" };
        }

        public static ViewSpec NewView(string category, string viewName) =>
            new ViewSpec { category = category, viewName = viewName };

        public static PopupSpec NewPopup(string name) =>
            new PopupSpec { name = name, title = name, message = "Message" };

        public static MenuCatalogSpec NewCatalog(string kind, string category, string menuName) =>
            new MenuCatalogSpec { kind = kind, category = category, menuName = menuName };

        public static MenuItemSpec NewMenuItem(string kind)
        {
            var item = new MenuItemSpec { kind = kind, category = "Settings", name = "NewItem", label = "New Item" };
            switch (kind)
            {
                case "slider":
                    item.min = 0; item.max = 1; item.value = "0.5";
                    break;
                case "stepper":
                    item.min = 0; item.max = 10; item.step = 1; item.value = "0"; item.wholeNumbers = true;
                    break;
                case "toggle":
                case "switch":
                    item.value = "True";
                    break;
                case "dropdown":
                    item.options = new List<string> { "Low", "Medium", "High" };
                    item.value = "0";
                    break;
            }
            return item;
        }

        // ---- deep clones via the spec's own serialization (lossless by construction) ----

        public static ElementSpec Clone(ElementSpec element) => ElementSpec.Parse(element.ToJsonObject());
        public static ViewSpec Clone(ViewSpec view) => ViewSpec.Parse(view.ToJsonObject());
        public static PopupSpec Clone(PopupSpec popup) => PopupSpec.Parse(popup.ToJsonObject());

        public static MenuCatalogSpec Clone(MenuCatalogSpec catalog) =>
            MenuCatalogSpec.Parse(catalog.ToJsonObject(), catalog.kind);

        public static MenuItemSpec Clone(MenuItemSpec item) => MenuItemSpec.Parse(item.ToJsonObject());
    }
}
