using System.Collections.Generic;
using Neo.UI.Menus;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The exporter's menus (settings/cheats) pipeline — moved verbatim out of
    /// <c>UISpecExporter.cs</c> (Wave 7 Task 7.1, audit E3). <c>UnmapKind</c> is gone — kind mapping
    /// now routes through <see cref="NeoMenuItemKinds.UnmapKind"/> so a project-registered kind's
    /// export path is picked up for free (see the runtime-boundary note on
    /// <see cref="MenuItemKindDescriptor"/> for what that does and doesn't cover today).
    /// </summary>
    public static partial class UISpecExporter
    {
        public static MenuCatalogSpec ExportCatalog(MenuCatalog catalog)
        {
            var spec = new MenuCatalogSpec
            {
                kind = NeoCatalogKinds.KindOf(catalog),
                category = catalog.category,
                menuName = catalog.menuName,
                groups = catalog.groups != null ? new List<string>(catalog.groups) : new List<string>(),
                start = string.IsNullOrEmpty(catalog.startGroup) ? null : catalog.startGroup,
                favourites = (catalog as CheatCatalog)?.favouritesEnabled ?? true,
                inputActionAsset = string.IsNullOrEmpty(catalog.inputActionAssetPath) ? null : catalog.inputActionAssetPath
            };
            foreach (MenuItemDefinition item in catalog.items)
                if (item != null) spec.items.Add(ExportItem(item));
            return spec;
        }

        private static MenuItemSpec ExportItem(MenuItemDefinition def)
        {
            var item = new MenuItemSpec
            {
                kind = NeoMenuItemKinds.UnmapKind(def.kind),
                category = def.Category,
                name = def.Name,
                group = string.IsNullOrEmpty(def.group) ? null : def.group,
                label = string.IsNullOrEmpty(def.label) ? null : def.label,
                tooltip = string.IsNullOrEmpty(def.tooltip) ? null : def.tooltip,
                persisted = def.persisted,
                wholeNumbers = def.wholeNumbers,
                value = string.IsNullOrEmpty(def.defaultValue) ? null : def.defaultValue,
                emitOnDrag = def.emitOnDrag,
                emitOnRelease = def.emitOnRelease,
                inputAction = string.IsNullOrEmpty(def.inputAction) ? null : def.inputAction,
                bindingIndex = def.bindingIndex
            };
            if (def.kind == MenuControlKind.Slider || def.kind == MenuControlKind.Stepper)
            {
                item.min = def.min;
                item.max = def.max;
            }
            if (def.kind == MenuControlKind.Stepper) item.step = def.step;
            if (def.kind == MenuControlKind.Dropdown && def.options != null && def.options.Count > 0)
                item.options = new List<string>(def.options);
            return item;
        }
    }
}
