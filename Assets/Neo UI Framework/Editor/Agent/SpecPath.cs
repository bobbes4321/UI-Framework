using System.Collections.Generic;
using System.Globalization;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The stable address scheme shared by <see cref="SpecDiff"/>, <see cref="SpecMerge"/>,
    /// <see cref="OffSpecLint"/> and the drift window — so every layer agrees on how a node in a
    /// <see cref="UISpec"/> is named. Paths are computed against the canonical JSON tree
    /// (<see cref="UISpec.ToJson"/> → <see cref="MiniJson.Parse"/>), so they are deterministic.
    ///
    /// <para>Named entities use their identity in the path (<c>views/Menu/Main</c>,
    /// <c>popups/Confirm</c>, <c>flow/nodes/MainMenu</c>); element/children lists use a positional
    /// index (<c>views/Menu/Main/elements[2]/background</c>) because most layout elements carry no
    /// id. This means reordering id-less siblings reads as a pair of modifies rather than a move —
    /// a documented v1 limitation.</para>
    /// </summary>
    public static class SpecPath
    {
        public const string ThemeSection = "theme";
        public const string ViewSection = "view";
        public const string PopupSection = "popup";
        public const string SettingsSection = "settings";
        public const string CheatsSection = "cheats";
        public const string FlowSection = "flow";
        public const string PresetSection = "preset";

        public static string View(string id) => $"views/{id}";
        public static string Popup(string name) => $"popups/{name}";
        public static string Preset(string name) => $"presets/{name}";
        public static string Catalog(string section, string id) => $"{section}/{id}";
        public static string Flow() => "flow";
        public static string FlowNode(string name) => $"flow/nodes/{name}";

        /// <summary> A theme token's path: default-variant tokens live under <c>theme/tokens</c>,
        /// variant overrides under <c>theme/variants/&lt;variant&gt;</c>. </summary>
        public static string ThemeToken(string variant, string token) =>
            string.IsNullOrEmpty(variant) ? $"theme/tokens/{token}" : $"theme/variants/{variant}/{token}";

        /// <summary> An element nested under an owner (view/popup) by its index chain:
        /// <c>owner/elements[i]/children[j]…</c>. </summary>
        public static string Element(string ownerPath, params int[] indexChain)
        {
            string path = ownerPath;
            for (int depth = 0; depth < indexChain.Length; depth++)
                path += (depth == 0 ? "/elements[" : "/children[") + indexChain[depth] + "]";
            return path;
        }

        public static string Field(string path, string field) => $"{path}/{field}";

        /// <summary> The top-level <see cref="SpecChange.section"/> a path belongs to. </summary>
        public static string SectionOf(string path)
        {
            string head = path;
            int slash = path.IndexOf('/');
            if (slash >= 0) head = path.Substring(0, slash);
            switch (head)
            {
                case "theme": return ThemeSection;
                case "views": return ViewSection;
                case "popups": return PopupSection;
                case "settings": return SettingsSection;
                case "cheats": return CheatsSection;
                case "flow": return FlowSection;
                case "presets": return PresetSection;
                default: return head;
            }
        }

        /// <summary> The last <c>/</c>-delimited segment of a path (a list's owning field name). </summary>
        public static string ListName(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        /// <summary>
        /// The pairing key used to match a list element across two specs. Named entities key by
        /// their identity; id-bearing elements/menu-items key by id; id-less elements key by
        /// position (so a reorder shows as modify pairs, never a phantom add+remove); plain scalar
        /// arrays key by position.
        /// </summary>
        public static string ListKey(string listName, object item, int index)
        {
            switch (listName)
            {
                case "views":
                case "settings":
                case "cheats":
                    return Identity(item, "id", index);
                case "popups":
                case "presets":
                case "nodes":
                    return Identity(item, "name", index);
                case "next":
                    return EdgeKey(item, index);
                case "elements":
                case "children":
                case "items":
                    return WidgetKey(item, index);
                default:
                    return "@" + index; // scalar arrays (options/size/groups/…) are positional
            }
        }

        /// <summary>
        /// The display path of a list element: identity for named entities, <c>[index]</c> for
        /// element/children/scalar lists. <paramref name="index"/> is the element's own index in
        /// the list it is being described from.
        /// </summary>
        public static string ChildPath(string listPath, string listName, object item, int index)
        {
            switch (listName)
            {
                case "views":
                case "settings":
                case "cheats":
                    return $"{listPath}/{StringField(item, "id", index)}";
                case "popups":
                case "presets":
                case "nodes":
                    return $"{listPath}/{StringField(item, "name", index)}";
                case "next":
                    return $"{listPath}/{EdgeKey(item, index)}";
                default:
                    return $"{listPath}[{index}]";
            }
        }

        // ------------------------------------------------------------------ identity helpers

        private static string Identity(object item, string field, int index) =>
            item is Dictionary<string, object> dict && dict.TryGetValue(field, out object v) && v != null
                ? v.ToString()
                : "@" + index;

        private static string StringField(object item, string field, int index) => Identity(item, field, index);

        /// <summary> Element / menu-item identity: the body's id when present, else position. The
        /// element JSON is wrapped as <c>{ kind: { id, … } }</c>. </summary>
        private static string WidgetKey(object item, int index)
        {
            if (item is Dictionary<string, object> wrapper && wrapper.Count == 1)
                foreach (KeyValuePair<string, object> entry in wrapper)
                    if (entry.Value is Dictionary<string, object> body
                        && body.TryGetValue("id", out object id) && id != null)
                        return "#" + id;
            return "@" + index;
        }

        /// <summary> Flow edge identity: <c>to</c> plus its trigger shape. </summary>
        private static string EdgeKey(object item, int index)
        {
            if (!(item is Dictionary<string, object> edge)) return "@" + index;
            string to = edge.TryGetValue("to", out object t) && t != null ? t.ToString() : "?";
            string trigger = "default";
            if (edge.TryGetValue("on", out object on) && on is Dictionary<string, object> onObj)
            {
                foreach (KeyValuePair<string, object> kv in onObj)
                {
                    // { "button": "Cat/Name" } / { "back": true } / { "timer": 2 } / { "signal": {…} }
                    string detail = kv.Value is Dictionary<string, object> sig
                        ? (sig.TryGetValue("category", out object c) ? c + "/" + (sig.TryGetValue("name", out object n) ? n : "") : "")
                        : kv.Value?.ToString();
                    trigger = string.IsNullOrEmpty(detail) || detail == "True"
                        ? kv.Key
                        : $"{kv.Key}:{detail}";
                    break;
                }
            }
            return $"{to}:{trigger}";
        }

        /// <summary> Renders a JSON scalar/container as the <c>before</c>/<c>after</c> text in a
        /// <see cref="SpecChange"/> — containers collapse to <c>(node)</c>. </summary>
        public static string Scalar(object value)
        {
            switch (value)
            {
                case null: return "null";
                case Dictionary<string, object> _:
                case List<object> _:
                    return "(node)";
                case bool b: return b ? "true" : "false";
                case double d: return d.ToString(CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }
    }
}
