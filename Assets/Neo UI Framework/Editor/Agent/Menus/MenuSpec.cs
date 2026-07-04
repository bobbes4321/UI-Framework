using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One control in a settings/cheats catalog. Keyed by kind (toggle/switch/slider/stepper/dropdown/
    /// button/label/rebind) like <see cref="ElementSpec"/>; maps 1:1 to a runtime MenuItemDefinition.
    /// </summary>
    [Serializable]
    public class MenuItemSpec
    {
        /// <summary>
        /// Every registered menu-item kind's spec string, in registration order (built-ins first — see
        /// <see cref="NeoMenuItemKinds"/>). Wave 7 Task 7.1: this used to be a hardcoded 8-entry array;
        /// it is now derived from <see cref="NeoMenuItemKinds.All"/> so a project that registers a new
        /// kind sees it here (and therefore in <c>MenuCatalogInspector</c>'s kind popup) for free. The
        /// built-in order/length still matches <c>MenuControlKind</c>'s declaration 1:1
        /// (<c>MenuCatalogInspectorTests.MenuItemSpecKinds_MatchesMenuControlKindDeclarationOrder</c>) —
        /// a project-registered kind with no runtime <c>MenuControlKind</c> mapping appends after the
        /// built-ins (see the extensibility note on <see cref="NeoMenuItemKinds.MapKind"/>).
        /// </summary>
        public static string[] Kinds => NeoMenuItemKinds.All.Select(d => d.id).ToArray();

        public string kind;
        public string category;
        public string name;
        public string group;
        public string label;
        public string tooltip;
        public bool persisted = true;
        public float? min;
        public float? max;
        public float? step;
        public bool wholeNumbers;
        public string value;          // stringified default (canonical type emitted per kind)
        public List<string> options;  // dropdown
        public bool emitOnDrag = true;    // slider
        public bool emitOnRelease = true; // slider
        public string inputAction;    // rebind: "ActionMap/Action"
        public int bindingIndex;      // rebind

        public string id => $"{category}/{name}";

        public static MenuItemSpec Parse(Dictionary<string, object> obj)
        {
            foreach (string kind in Kinds)
            {
                Dictionary<string, object> body = JsonReader.GetObject(obj, kind);
                if (body == null) continue;
                var spec = new MenuItemSpec
                {
                    kind = kind,
                    group = JsonReader.GetString(body, "group"),
                    label = JsonReader.GetString(body, "label"),
                    tooltip = JsonReader.GetString(body, "tooltip"),
                    persisted = JsonReader.GetBool(body, "persisted", true),
                    wholeNumbers = JsonReader.GetBool(body, "wholeNumbers"),
                    inputAction = JsonReader.GetString(body, "action"),
                    emitOnDrag = JsonReader.GetBool(body, "emitOnDrag", true),
                    emitOnRelease = JsonReader.GetBool(body, "emitOnRelease", true)
                };
                string idValue = JsonReader.GetString(body, "id");
                if (string.IsNullOrWhiteSpace(idValue))
                    throw new FormatException($"Menu item ('{kind}') is missing required field 'id' (\"Category/Name\")");
                CategoryNameId.Parse(idValue, out spec.category, out spec.name);

                if (body.TryGetValue("min", out object min) && min is double dmin) spec.min = (float)dmin;
                if (body.TryGetValue("max", out object max) && max is double dmax) spec.max = (float)dmax;
                if (body.TryGetValue("step", out object step) && step is double dstep) spec.step = (float)dstep;
                if (body.TryGetValue("bindingIndex", out object bi) && bi is double dbi) spec.bindingIndex = (int)dbi;
                spec.value = ValueToString(body.TryGetValue("value", out object v) ? v : null);

                List<object> optionArray = JsonReader.GetArray(body, "options");
                if (optionArray != null)
                {
                    spec.options = new List<string>();
                    foreach (object option in optionArray)
                        if (option != null) spec.options.Add(option.ToString());
                }
                return spec;
            }
            throw new FormatException($"Menu item must contain one of: {string.Join(", ", Kinds)}");
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var body = new Dictionary<string, object> { ["id"] = id };
            if (!string.IsNullOrEmpty(group)) body["group"] = group;
            if (!string.IsNullOrEmpty(label)) body["label"] = label;
            if (!string.IsNullOrEmpty(tooltip)) body["tooltip"] = tooltip;
            if (!persisted) body["persisted"] = false;
            if (min.HasValue) body["min"] = (double)min.Value;
            if (max.HasValue) body["max"] = (double)max.Value;
            if (step.HasValue) body["step"] = (double)step.Value;
            if (wholeNumbers) body["wholeNumbers"] = true;
            object typed = NeoMenuItemKinds.ValueToTyped(kind, value);
            if (typed != null) body["value"] = typed;
            if (options != null && options.Count > 0)
            {
                var array = new List<object>();
                foreach (string option in options) array.Add(option);
                body["options"] = array;
            }
            if (kind == "slider")
            {
                if (!emitOnDrag) body["emitOnDrag"] = false;
                if (!emitOnRelease) body["emitOnRelease"] = false;
            }
            if (kind == "rebind")
            {
                if (!string.IsNullOrEmpty(inputAction)) body["action"] = inputAction;
                if (bindingIndex != 0) body["bindingIndex"] = (double)bindingIndex;
            }
            return new Dictionary<string, object> { [kind] = body };
        }

        private static string ValueToString(object value)
        {
            if (value == null) return null;
            if (value is bool b) return b ? "True" : "False";
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return value.ToString();
        }
    }

    /// <summary>
    /// A settings or cheats catalog: id, optional groups/tabs and a flat list of controls. Generated
    /// into a SettingsCatalog / CheatCatalog asset; the "settings"/"cheats" view element references it.
    /// </summary>
    [Serializable]
    public class MenuCatalogSpec
    {
        public const string SettingsKind = "settings";
        public const string CheatKind = "cheats";

        public string kind = SettingsKind;
        public string category;
        public string menuName;
        public List<string> groups = new List<string>();
        public string start;
        public bool favourites = true;       // cheats only
        public string inputActionAsset;      // optional asset path for rebind rows
        public List<MenuItemSpec> items = new List<MenuItemSpec>();

        public string id => $"{category}/{menuName}";

        public static MenuCatalogSpec Parse(Dictionary<string, object> obj, string kind)
        {
            var spec = new MenuCatalogSpec { kind = kind };
            string idValue = JsonReader.GetString(obj, "id");
            if (string.IsNullOrWhiteSpace(idValue))
                throw new FormatException("Menu catalog is missing required field 'id' (\"Category/Name\")");
            CategoryNameId.Parse(idValue, out spec.category, out spec.menuName);
            spec.start = JsonReader.GetString(obj, "start");
            spec.favourites = JsonReader.GetBool(obj, "favourites", true);
            spec.inputActionAsset = JsonReader.GetString(obj, "inputActionAsset");

            List<object> groupArray = JsonReader.GetArray(obj, "groups");
            if (groupArray != null)
                foreach (object g in groupArray)
                    if (g != null) spec.groups.Add(g.ToString());

            List<object> itemArray = JsonReader.GetArray(obj, "items");
            if (itemArray != null)
                foreach (object item in itemArray)
                    spec.items.Add(MenuItemSpec.Parse(JsonReader.AsObject(item, "menu item")));
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object> { ["id"] = id };
            if (groups.Count > 0)
            {
                var array = new List<object>();
                foreach (string g in groups) array.Add(g);
                result["groups"] = array;
            }
            if (!string.IsNullOrEmpty(start)) result["start"] = start;
            if (kind == CheatKind && !favourites) result["favourites"] = false;
            if (!string.IsNullOrEmpty(inputActionAsset)) result["inputActionAsset"] = inputActionAsset;
            if (items.Count > 0)
            {
                var array = new List<object>();
                foreach (MenuItemSpec item in items) array.Add(item.ToJsonObject());
                result["items"] = array;
            }
            return result;
        }
    }
}
