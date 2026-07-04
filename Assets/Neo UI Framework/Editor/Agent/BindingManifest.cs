using System.Collections.Generic;
using System.Text;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The contract a generated UI exposes to game code, derived from the in-memory <see cref="UISpec"/>
    /// (the spec is the source of truth — never the prefabs). Lists every signal a developer can react to,
    /// every <see cref="UIData"/> source that feeds a list, every settings/cheat control, and every view id —
    /// so wiring a generated menu is fill-in-the-blank, not archaeology through the spec.
    ///
    /// Consumed by <see cref="BindingStubGenerator"/> (emits a partial-class C# stub) and surfaced by the
    /// AgentBridge <c>bindings</c> action. Plan 3, deliverable A.
    /// </summary>
    public sealed class BindingManifest
    {
        /// <summary> A signal the UI publishes. <see cref="domain"/> entries are first-class domain streams
        /// (Plan 3 B / button onClick.signal) a developer subscribes to with <c>Signals.On&lt;T&gt;</c>; the
        /// rest are the standard "…/Behaviour" firehose streams, listed for completeness. </summary>
        public sealed class SignalBinding
        {
            public string category;
            public string name;
            public string payload;   // none | bool | float | int | string  (domain) or the standard struct name
            public string source;
            public bool domain;      // true = subscribe directly; false = standard firehose stream

            public Dictionary<string, object> ToJsonObject() => new Dictionary<string, object>
            {
                ["category"] = category,
                ["name"] = name,
                ["payload"] = payload,
                ["source"] = source,
                ["domain"] = domain
            };
        }

        /// <summary> A <see cref="UIData"/> source feeding a bound list/grid, with the row template's tokens. </summary>
        public sealed class DataSourceBinding
        {
            public string id;
            public List<string> tokens = new List<string>();
            public string source;

            public Dictionary<string, object> ToJsonObject() => new Dictionary<string, object>
            {
                ["id"] = id,
                ["tokens"] = new List<object>(tokens),
                ["source"] = source
            };
        }

        /// <summary> A settings or cheat control, with the C# value type per kind. </summary>
        public sealed class SettingBinding
        {
            public string category;
            public string name;
            public string kind;
            public string type;       // bool | float | int | none
            public string defaultValue;

            public Dictionary<string, object> ToJsonObject()
            {
                var obj = new Dictionary<string, object>
                {
                    ["category"] = category,
                    ["name"] = name,
                    ["kind"] = kind,
                    ["type"] = type
                };
                if (!string.IsNullOrEmpty(defaultValue)) obj["default"] = defaultValue;
                return obj;
            }
        }

        public sealed class ViewBinding
        {
            public string id;
            public string category;
            public string name;

            public Dictionary<string, object> ToJsonObject() => new Dictionary<string, object>
            {
                ["id"] = id,
                ["category"] = category,
                ["name"] = name
            };
        }

        public string flowName;
        public readonly List<SignalBinding> signals = new List<SignalBinding>();
        public readonly List<DataSourceBinding> dataSources = new List<DataSourceBinding>();
        public readonly List<SettingBinding> settings = new List<SettingBinding>();
        public readonly List<SettingBinding> cheats = new List<SettingBinding>();
        public readonly List<ViewBinding> views = new List<ViewBinding>();

        // ------------------------------------------------------------------ derivation

        /// <summary> Derives the manifest from a spec (deterministic order: spec order, then dedup). </summary>
        public static BindingManifest Derive(UISpec spec)
        {
            var manifest = new BindingManifest { flowName = spec?.flow?.name };
            if (spec == null) return manifest;

            // standard-stream entries are deduped to the distinct stream (one per widget kind present)
            var standardSeen = new HashSet<string>();
            var domainSeen = new HashSet<string>();

            foreach (ViewSpec view in spec.views)
            {
                manifest.views.Add(new ViewBinding { id = view.id, category = view.category, name = view.viewName });
                SpecWalk.Elements(view, includeItemTemplates: true,
                    element => manifest.WalkElement(element, view.id, standardSeen, domainSeen));
            }

            foreach (MenuCatalogSpec catalog in spec.settings)
                foreach (MenuItemSpec item in catalog.items)
                    manifest.settings.Add(ToSetting(item));
            foreach (MenuCatalogSpec catalog in spec.cheats)
                foreach (MenuItemSpec item in catalog.items)
                    manifest.cheats.Add(ToSetting(item));

            return manifest;
        }

        /// <summary> Derives the bindings owned by <paramref name="element"/> itself — tree recursion
        /// (children + item template) is handled once, by <see cref="SpecWalk"/>. </summary>
        private void WalkElement(ElementSpec element, string viewId, HashSet<string> standardSeen, HashSet<string> domainSeen)
        {
            if (element == null) return;

            // Extensibility seam (keystone): a project-registered kind surfaces its domain signal and/or
            // data binding so game code can wire it — a registered kind invisible to the manifest is the
            // same 90%-then-stuck failure the binding guide's invariant exists to prevent. Built-ins are
            // not registered, so this pre-check is a no-op until a project registers.
            if (NeoElementKinds.TryGet(element.kind, out INeoElementKind ext))
            {
                string payload = string.IsNullOrEmpty(ext.SignalPayload) ? "none" : ext.SignalPayload;
                if (element.signal != null)
                    AddDomainSignal(domainSeen, element.signal.category, element.signal.name,
                        payload, $"{element.kind} {element.id}");
                if (element.onClickSignal != null)
                    AddDomainSignal(domainSeen, element.onClickSignal.category, element.onClickSignal.name,
                        "none", $"{element.kind} {element.id}");
                if (!string.IsNullOrEmpty(element.bind))
                    dataSources.Add(new DataSourceBinding
                    {
                        id = element.bind,
                        tokens = CollectTokens(element.item),
                        source = $"{element.kind} in view {viewId}"
                    });
                return;
            }

            switch (element.kind)
            {
                case "button":
                    if (element.onClickSignal != null)
                        AddDomainSignal(domainSeen, element.onClickSignal.category, element.onClickSignal.name,
                            "none", $"button {element.id}");
                    AddStandardSignal(standardSeen, UIButton.StreamCategory, UIButton.StreamName, "ButtonSignalData");
                    break;
                case "toggle":
                case "switch":
                    if (element.signal != null)
                        AddDomainSignal(domainSeen, element.signal.category, element.signal.name,
                            "bool", $"{element.kind} {element.id}");
                    AddStandardSignal(standardSeen, UIToggle.StreamCategory, UIToggle.StreamName, "ToggleSignalData");
                    break;
                case "slider":
                    if (element.signal != null)
                        AddDomainSignal(domainSeen, element.signal.category, element.signal.name,
                            "float", $"slider {element.id}");
                    AddStandardSignal(standardSeen, UISlider.StreamCategory, UISlider.StreamName, "SliderSignalData");
                    break;
                case "dropdown":
                    if (element.signal != null)
                        AddDomainSignal(domainSeen, element.signal.category, element.signal.name,
                            "int", $"dropdown {element.id}");
                    AddStandardSignal(standardSeen, UIDropdown.StreamCategory, UIDropdown.StreamName, "DropdownSignalData");
                    break;
                case "list":
                case "scroll":
                case "grid":
                    if (!string.IsNullOrEmpty(element.bind))
                        dataSources.Add(new DataSourceBinding
                        {
                            id = element.bind,
                            tokens = CollectTokens(element.item),
                            source = $"{element.kind} in view {viewId}"
                        });
                    break;
            }
        }

        private void AddDomainSignal(HashSet<string> seen, string category, string name, string payload, string source)
        {
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(name)) return;
            if (!seen.Add($"{category}/{name}")) return;
            signals.Add(new SignalBinding { category = category, name = name, payload = payload, source = source, domain = true });
        }

        private void AddStandardSignal(HashSet<string> seen, string category, string name, string payload)
        {
            if (!seen.Add($"{category}/{name}")) return;
            signals.Add(new SignalBinding
            {
                category = category, name = name, payload = payload, domain = false,
                source = $"standard stream for all {category} widgets"
            });
        }

        private static SettingBinding ToSetting(MenuItemSpec item) => new SettingBinding
        {
            category = item.category,
            name = item.name,
            kind = item.kind,
            type = TypeForKind(item.kind),
            defaultValue = item.value
        };

        /// <summary> C# value type a settings/cheat control reads/writes ("bool"/"float"/"int"/"none").
        /// Wave 7 Task 7.1: resolves the Phase-2 TODO this doc used to carry — routes through
        /// <see cref="NeoMenuItemKinds.TypeForKind"/> (the registered descriptor's <c>valueType</c>), so
        /// a project's own registered menu-item kind surfaces its C# value type here too, not just the
        /// 8 built-ins. </summary>
        public static string TypeForKind(string kind) => NeoMenuItemKinds.TypeForKind(kind);

        /// <summary> The distinct <c>{token}</c> names referenced by a row template's text labels.
        /// Tree recursion (children + nested item templates) is handled once, by <see cref="SpecWalk"/>
        /// — this used to be its own private walker that disagreed with <see cref="WalkElement"/> on
        /// whether a nested item template counts (audit D5, the same drift class as D4). </summary>
        public static List<string> CollectTokens(ElementSpec template)
        {
            var tokens = new List<string>();
            var seen = new HashSet<string>();
            SpecWalk.Elements(template, includeItemTemplates: true, element => ExtractTokens(element.label, tokens, seen));
            return tokens;
        }

        private static void ExtractTokens(string text, List<string> tokens, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) break;
                int close = text.IndexOf('}', open + 1);
                if (close < 0) break;
                string token = text.Substring(open + 1, close - open - 1).Trim();
                if (token.Length > 0 && seen.Add(token)) tokens.Add(token);
                i = close + 1;
            }
        }

        // ------------------------------------------------------------------ serialization

        public Dictionary<string, object> ToJsonObject()
        {
            var root = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(flowName)) root["flow"] = flowName;
            root["signals"] = MapList(signals, s => s.ToJsonObject());
            root["dataSources"] = MapList(dataSources, d => d.ToJsonObject());
            root["settings"] = MapList(settings, s => s.ToJsonObject());
            root["cheats"] = MapList(cheats, s => s.ToJsonObject());
            root["views"] = MapList(views, v => v.ToJsonObject());
            return root;
        }

        public string ToJson() => MiniJson.Serialize(ToJsonObject());

        private static List<object> MapList<T>(List<T> items, System.Func<T, Dictionary<string, object>> map)
        {
            var list = new List<object>(items.Count);
            foreach (T item in items) list.Add(map(item));
            return list;
        }
    }
}
