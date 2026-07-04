using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The declarative UI spec model — the text format agents author. JSON schema (all sections optional):
    /// <code>
    /// {
    ///   "theme":   { "tokens": { "Primary": "#3A86FF" }, "variants": { "Dark": { "Primary": "#123456" } } },
    ///   "presets": [ { "name": "SlideInLeft", "type": "Show", "move": { "from": "Left" },
    ///                  "fade": { "from": 0 }, "duration": 0.3, "ease": "OutCubic" } ],
    ///   "views":   [ { "id": "Menu/Main", "showAnimation": "SlideInLeft", "hideAnimation": "SlideOutLeft",
    ///                  "background": "Background",
    ///                  "elements": [ { "button": { "id": "Action/Play", "label": "Play",
    ///                                  "labelColor": "TextDefault", "background": "Primary",
    ///                                  "onClick": { "signal": { "category": "Gameplay", "name": "StartPainting" } } } },
    ///                                { "text":   { "label": "Title", "color": "TextDefault" } },
    ///                                { "toggle": { "id": "Mute/Music", "label": "Music" } } ] } ],
    ///   "flow":    { "name": "UI", "start": "MainMenu",
    ///                "nodes": [ { "name": "MainMenu", "view": "Menu/Main",
    ///                             "next": [ { "on": { "button": "Action/Settings" }, "to": "Settings" } ] },
    ///                           { "name": "Settings", "view": "Menu/Settings",
    ///                             "next": [ { "on": { "back": true }, "to": "MainMenu" } ] } ] }
    /// }
    /// </code>
    /// Triggers ("on"): { "button": "Cat/Name" } | { "signal": "Cat/Name" } | { "toggleOn"/"toggleOff": "Cat/Name" }
    /// | { "viewShown"/"viewHidden": "Cat/Name" } | { "back": true } | { "timer": seconds }.
    /// </summary>
    [Serializable]
    public class UISpec
    {
        public ThemeSpec theme;
        public List<PresetSpec> presets = new List<PresetSpec>();
        public List<ViewSpec> views = new List<ViewSpec>();
        public List<PopupSpec> popups = new List<PopupSpec>();
        /// <summary> Settings catalogs (generated as SettingsCatalog assets). </summary>
        public List<MenuCatalogSpec> settings = new List<MenuCatalogSpec>();
        /// <summary> Cheat catalogs (generated as CheatCatalog assets). </summary>
        public List<MenuCatalogSpec> cheats = new List<MenuCatalogSpec>();
        /// <summary>
        /// Pillar B: global, ordered responsive breakpoints (first match wins at runtime). Each names a
        /// condition (orientation / aspect / width) that an element's per-breakpoint <see cref="ElementSpec.overrides"/>
        /// key into. Empty by default → emits nothing, so legacy specs stay byte-identical.
        /// </summary>
        public List<BreakpointSpec> breakpoints = new List<BreakpointSpec>();
        public FlowSpec flow;

        public static UISpec FromJson(string json)
        {
            var root = JsonReader.AsObject(MiniJson.Parse(json), "spec root");
            var spec = new UISpec();

            Dictionary<string, object> themeObj = JsonReader.GetObject(root, "theme");
            if (themeObj != null) spec.theme = ThemeSpec.Parse(themeObj);

            List<object> presetArray = JsonReader.GetArray(root, "presets");
            if (presetArray != null)
                foreach (object item in presetArray)
                    spec.presets.Add(PresetSpec.Parse(JsonReader.AsObject(item, "preset")));

            List<object> settingsArray = JsonReader.GetArray(root, "settings");
            if (settingsArray != null)
                foreach (object item in settingsArray)
                    spec.settings.Add(MenuCatalogSpec.Parse(JsonReader.AsObject(item, "settings catalog"), MenuCatalogSpec.SettingsKind));

            List<object> cheatsArray = JsonReader.GetArray(root, "cheats");
            if (cheatsArray != null)
                foreach (object item in cheatsArray)
                    spec.cheats.Add(MenuCatalogSpec.Parse(JsonReader.AsObject(item, "cheat catalog"), MenuCatalogSpec.CheatKind));

            List<object> viewArray = JsonReader.GetArray(root, "views");
            if (viewArray != null)
                foreach (object item in viewArray)
                    spec.views.Add(ViewSpec.Parse(JsonReader.AsObject(item, "view")));

            List<object> popupArray = JsonReader.GetArray(root, "popups");
            if (popupArray != null)
                foreach (object item in popupArray)
                    spec.popups.Add(PopupSpec.Parse(JsonReader.AsObject(item, "popup")));

            List<object> breakpointArray = JsonReader.GetArray(root, "breakpoints");
            if (breakpointArray != null)
                foreach (object item in breakpointArray)
                    spec.breakpoints.Add(BreakpointSpec.Parse(JsonReader.AsObject(item, "breakpoint")));

            Dictionary<string, object> flowObj = JsonReader.GetObject(root, "flow");
            if (flowObj != null) spec.flow = FlowSpec.Parse(flowObj);

            return spec;
        }

        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            if (theme != null) root["theme"] = theme.ToJsonObject();
            if (presets.Count > 0)
            {
                var array = new List<object>();
                foreach (PresetSpec preset in presets) array.Add(preset.ToJsonObject());
                root["presets"] = array;
            }
            if (views.Count > 0)
            {
                var array = new List<object>();
                foreach (ViewSpec view in views) array.Add(view.ToJsonObject());
                root["views"] = array;
            }
            if (settings.Count > 0)
            {
                var array = new List<object>();
                foreach (MenuCatalogSpec catalog in settings) array.Add(catalog.ToJsonObject());
                root["settings"] = array;
            }
            if (cheats.Count > 0)
            {
                var array = new List<object>();
                foreach (MenuCatalogSpec catalog in cheats) array.Add(catalog.ToJsonObject());
                root["cheats"] = array;
            }
            if (popups.Count > 0)
            {
                var array = new List<object>();
                foreach (PopupSpec popup in popups) array.Add(popup.ToJsonObject());
                root["popups"] = array;
            }
            if (breakpoints.Count > 0)
            {
                var array = new List<object>();
                foreach (BreakpointSpec breakpoint in breakpoints) array.Add(breakpoint.ToJsonObject());
                root["breakpoints"] = array;
            }
            if (flow != null) root["flow"] = flow.ToJsonObject();
            return MiniJson.Serialize(root);
        }
    }

    /// <summary>
    /// One named, ordered responsive breakpoint (Pillar B). The runtime driver
    /// (<c>UIResponsiveRoot</c>) evaluates the breakpoint list top-to-bottom and the FIRST whose
    /// <see cref="when"/> matches the current viewport wins (else the baked base layout). The
    /// <see cref="name"/> is the key an element's <see cref="ElementSpec.overrides"/> dict uses.
    /// </summary>
    [Serializable]
    public class BreakpointSpec
    {
        public string name;
        public BreakpointCondition when = new BreakpointCondition();

        public static BreakpointSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new BreakpointSpec
            {
                name = JsonReader.GetString(obj, "name"),
                when = BreakpointCondition.Parse(JsonReader.GetObject(obj, "when"))
            };
            if (string.IsNullOrWhiteSpace(spec.name))
                throw new FormatException("Breakpoint is missing required field 'name'");
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object> { ["name"] = name };
            if (when != null && !when.IsEmpty) result["when"] = when.ToJsonObject();
            return result;
        }
    }

    /// <summary>
    /// A breakpoint condition. The built-in kinds (orientation / minAspect / maxAspect / minWidth /
    /// maxWidth) are sugar over registered <c>IBreakpointCondition</c> evaluators — a project adds a
    /// new kind through that registry (see <c>BreakpointConditions</c>) without forking the package.
    /// Exactly one kind is normally set; deterministic emit order: orientation, minAspect, maxAspect,
    /// minWidth, maxWidth.
    /// </summary>
    [Serializable]
    public class BreakpointCondition
    {
        public string orientation;  // "portrait" | "landscape"
        public float? minAspect;    // width/height >=
        public float? maxAspect;    // width/height <=
        public float? minWidth;     // reference-px width >=
        public float? maxWidth;     // reference-px width <=

        public bool IsEmpty =>
            string.IsNullOrEmpty(orientation)
            && !minAspect.HasValue && !maxAspect.HasValue
            && !minWidth.HasValue && !maxWidth.HasValue;

        public static BreakpointCondition Parse(Dictionary<string, object> obj)
        {
            var result = new BreakpointCondition();
            if (obj == null) return result;
            result.orientation = JsonReader.GetString(obj, "orientation");
            if (obj.TryGetValue("minAspect", out object minA) && minA is double mad) result.minAspect = (float)mad;
            if (obj.TryGetValue("maxAspect", out object maxA) && maxA is double mxad) result.maxAspect = (float)mxad;
            if (obj.TryGetValue("minWidth", out object minW) && minW is double mwd) result.minWidth = (float)mwd;
            if (obj.TryGetValue("maxWidth", out object maxW) && maxW is double mxwd) result.maxWidth = (float)mxwd;
            return result;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(orientation)) result["orientation"] = orientation;
            if (minAspect.HasValue) result["minAspect"] = (double)minAspect.Value;
            if (maxAspect.HasValue) result["maxAspect"] = (double)maxAspect.Value;
            if (minWidth.HasValue) result["minWidth"] = (double)minWidth.Value;
            if (maxWidth.HasValue) result["maxWidth"] = (double)maxWidth.Value;
            return result;
        }
    }

    [Serializable]
    public class ThemeSpec
    {
        /// <summary>
        /// Curated bundle name (CleanSlate / NeonArcade / SoftFantasy) applied FIRST;
        /// explicit tokens/variants override afterwards. Never exported — the bundle expands
        /// into plain tokens on generate.
        /// </summary>
        public string bundle;
        /// <summary> Token → hex color of the default variant. </summary>
        public Dictionary<string, string> tokens = new Dictionary<string, string>();
        /// <summary> Variant name → (token → hex color) overrides. </summary>
        public Dictionary<string, Dictionary<string, string>> variants = new Dictionary<string, Dictionary<string, string>>();

        public static ThemeSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new ThemeSpec();
            spec.bundle = JsonReader.GetString(obj, "bundle");
            Dictionary<string, object> tokenObj = JsonReader.GetObject(obj, "tokens");
            if (tokenObj != null)
                foreach (KeyValuePair<string, object> entry in tokenObj)
                    spec.tokens[entry.Key] = entry.Value?.ToString();

            Dictionary<string, object> variantObj = JsonReader.GetObject(obj, "variants");
            if (variantObj != null)
            {
                foreach (KeyValuePair<string, object> variant in variantObj)
                {
                    var colors = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, object> entry in JsonReader.AsObject(variant.Value, $"variant '{variant.Key}'"))
                        colors[entry.Key] = entry.Value?.ToString();
                    spec.variants[variant.Key] = colors;
                }
            }
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            var tokenObj = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> entry in tokens) tokenObj[entry.Key] = entry.Value;
            result["tokens"] = tokenObj;
            if (variants.Count > 0)
            {
                var variantObj = new Dictionary<string, object>();
                foreach (KeyValuePair<string, Dictionary<string, string>> variant in variants)
                {
                    var colors = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, string> entry in variant.Value) colors[entry.Key] = entry.Value;
                    variantObj[variant.Key] = colors;
                }
                result["variants"] = variantObj;
            }
            return result;
        }
    }

    [Serializable]
    public class PresetChannelSpec
    {
        public bool enabled;
        public string from; // direction name, number or "x,y,z" depending on channel
        public string to;
        public float? duration;
        public string ease;

        public static PresetChannelSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            var channel = new PresetChannelSpec { enabled = true };
            if (obj.TryGetValue("from", out object fromValue) && fromValue != null) channel.from = ValueToString(fromValue);
            if (obj.TryGetValue("to", out object toValue) && toValue != null) channel.to = ValueToString(toValue);
            if (obj.TryGetValue("duration", out object d) && d is double dd) channel.duration = (float)dd;
            channel.ease = JsonReader.GetString(obj, "ease");
            return channel;
        }

        private static string ValueToString(object value)
        {
            if (value is List<object> list) return string.Join(",", list);
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return value.ToString();
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(from)) result["from"] = NumberOrString(from);
            if (!string.IsNullOrEmpty(to)) result["to"] = NumberOrString(to);
            if (duration.HasValue) result["duration"] = (double)duration.Value;
            if (!string.IsNullOrEmpty(ease)) result["ease"] = ease;
            return result;
        }

        private static object NumberOrString(string value) =>
            double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) ? (object)d : value;
    }

    [Serializable]
    public class PresetSpec
    {
        public string name;
        public string type = "Custom"; // Show / Hide / Loop / Button / State / Custom
        public float duration = 0.3f;
        public string ease = "OutCubic";
        public PresetChannelSpec move;
        public PresetChannelSpec rotate;
        public PresetChannelSpec scale;
        public PresetChannelSpec fade;
        // Color/tint channel: from/to are a "#hex" color, a theme-token name, or the keywords
        // "start"/"current" (the captured / live color), matching the GradientSpec from/to convention.
        public PresetChannelSpec color;

        public static PresetSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new PresetSpec
            {
                name = JsonReader.GetString(obj, "name"),
                type = JsonReader.GetString(obj, "type", "Custom"),
                duration = JsonReader.GetFloat(obj, "duration", 0.3f),
                ease = JsonReader.GetString(obj, "ease", "OutCubic"),
                move = PresetChannelSpec.Parse(JsonReader.GetObject(obj, "move")),
                rotate = PresetChannelSpec.Parse(JsonReader.GetObject(obj, "rotate")),
                scale = PresetChannelSpec.Parse(JsonReader.GetObject(obj, "scale")),
                fade = PresetChannelSpec.Parse(JsonReader.GetObject(obj, "fade")),
                color = PresetChannelSpec.Parse(JsonReader.GetObject(obj, "color"))
            };
            if (string.IsNullOrWhiteSpace(spec.name))
                throw new FormatException("Preset is missing required field 'name'");
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>
            {
                ["name"] = name,
                ["type"] = type,
                ["duration"] = (double)duration,
                ["ease"] = ease
            };
            if (move != null && move.enabled) result["move"] = move.ToJsonObject();
            if (rotate != null && rotate.enabled) result["rotate"] = rotate.ToJsonObject();
            if (scale != null && scale.enabled) result["scale"] = scale.ToJsonObject();
            if (fade != null && fade.enabled) result["fade"] = fade.ToJsonObject();
            if (color != null && color.enabled) result["color"] = color.ToJsonObject();
            return result;
        }
    }

    [Serializable]
    public class SignalRefSpec
    {
        public string category;
        public string name;

        public static SignalRefSpec Parse(object value, string context)
        {
            var spec = new SignalRefSpec();
            if (value is string s)
            {
                CategoryNameId.Parse(s, out spec.category, out spec.name);
                return spec;
            }
            var obj = JsonReader.AsObject(value, context);
            spec.category = JsonReader.GetString(obj, "category");
            spec.name = JsonReader.GetString(obj, "name");
            return spec;
        }

        public Dictionary<string, object> ToJsonObject() =>
            new Dictionary<string, object> { ["category"] = category, ["name"] = name };
    }

    /// <summary>
    /// A two-stop gradient: "from"/"to" are theme tokens or "#hex" colors, angle in degrees
    /// (0 = left to right, 90 = bottom to top). Rides NeoGradient so theming stays live.
    /// </summary>
    [Serializable]
    public class GradientSpec
    {
        public string from;
        public string to;
        public float angle = 90f;

        public static GradientSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            return new GradientSpec
            {
                from = JsonReader.GetString(obj, "from"),
                to = JsonReader.GetString(obj, "to"),
                angle = JsonReader.GetFloat(obj, "angle", 90f)
            };
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(from)) result["from"] = from;
            if (!string.IsNullOrEmpty(to)) result["to"] = to;
            result["angle"] = (double)angle;
            return result;
        }
    }

    /// <summary>
    /// An OPEN-bag shape effect on an element: <c>"effect": { "id": "glowPulse", "params": { ... } }</c>.
    /// The <see cref="id"/> selects a descriptor from <c>ShapeEffectRegistry</c> (the editor-side seam
    /// that owns parse/bake/export); <see cref="parameters"/> is the descriptor's flat param bag,
    /// carried verbatim as the project's JSON object model so the core spec never grows a per-effect
    /// switch. Built-in ids: glowPulse / sheenSweep / gradientCycle (Tier-1) and variant (Tier-2).
    /// </summary>
    [Serializable]
    public class EffectSpec
    {
        public string id;
        public Dictionary<string, object> parameters; // descriptor-owned, opaque to the core spec

        public static EffectSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            return new EffectSpec
            {
                id = JsonReader.GetString(obj, "id"),
                parameters = JsonReader.GetObject(obj, "params")
            };
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(id)) result["id"] = id;
            if (parameters != null && parameters.Count > 0) result["params"] = parameters;
            return result;
        }
    }

    /// <summary> One particle module in a <see cref="ParticleSpec"/>: an open-bag id + params pair. </summary>
    [Serializable]
    public class ParticleModuleSpec
    {
        public string id;
        public Dictionary<string, object> parameters; // descriptor-owned (ParticleEffectRegistry)

        public static ParticleModuleSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            return new ParticleModuleSpec
            {
                id = JsonReader.GetString(obj, "id"),
                parameters = JsonReader.GetObject(obj, "params")
            };
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(id)) result["id"] = id;
            if (parameters != null && parameters.Count > 0) result["params"] = parameters;
            return result;
        }
    }

    /// <summary>
    /// A UI particle emitter on an element: <c>"particles": { ...scalars..., "modules": [ {id,params} ],
    /// "signal": {category,name,count}, "preset": "Category/Name" }</c>. The emitter scalars round-trip
    /// directly here; the OPEN <see cref="modules"/> list rides <c>ParticleEffectRegistry</c> so the
    /// module set stays extensible without a switch. An optional <see cref="preset"/> seeds the emitter
    /// from a named <c>NeoParticleEmitterPreset</c> before the inline fields apply; an optional
    /// <see cref="signal"/> adds a <c>NeoParticleBurstOnSignal</c>.
    /// </summary>
    [Serializable]
    public class ParticleSpec
    {
        public int? capacity;
        public int? burstCount;
        public float? rate;
        public bool emitOnEnable;
        public string particleShape;        // ShapeType name (Circle, RoundedRect, …)
        public float? cornerRadiusPercent;
        public float[] sizeRange;           // [min,max]
        public float[] lifetimeRange;       // [min,max]
        public float[] speedRange;          // [min,max]
        public float? emitAngle;
        public float? emitSpread;
        public float[] angularVelocityRange; // [min,max]
        public string preset;               // optional "Category/Name" of a NeoParticleEmitterPreset
        public List<ParticleModuleSpec> modules = new List<ParticleModuleSpec>();
        public SignalRefSpec signal;        // optional NeoParticleBurstOnSignal trigger
        public int? signalCount;            // burst count on the signal (<=0 = emitter default)
        public bool atPointer;              // burst at the click point on pointer-down (NeoParticlePointerBurst)

        public static ParticleSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            var spec = new ParticleSpec
            {
                rate = obj.ContainsKey("rate") ? (float?)JsonReader.GetFloat(obj, "rate") : null,
                emitOnEnable = JsonReader.GetBool(obj, "emitOnEnable"),
                particleShape = JsonReader.GetString(obj, "particleShape"),
                cornerRadiusPercent = obj.ContainsKey("cornerRadiusPercent") ? (float?)JsonReader.GetFloat(obj, "cornerRadiusPercent") : null,
                sizeRange = GetFloatArray(obj, "sizeRange"),
                lifetimeRange = GetFloatArray(obj, "lifetimeRange"),
                speedRange = GetFloatArray(obj, "speedRange"),
                emitAngle = obj.ContainsKey("emitAngle") ? (float?)JsonReader.GetFloat(obj, "emitAngle") : null,
                emitSpread = obj.ContainsKey("emitSpread") ? (float?)JsonReader.GetFloat(obj, "emitSpread") : null,
                angularVelocityRange = GetFloatArray(obj, "angularVelocityRange"),
                preset = JsonReader.GetString(obj, "preset"),
                atPointer = JsonReader.GetBool(obj, "atPointer")
            };
            if (obj.TryGetValue("capacity", out object cap) && cap is double cd) spec.capacity = (int)cd;
            if (obj.TryGetValue("burstCount", out object bc) && bc is double bd) spec.burstCount = (int)bd;

            List<object> moduleArray = JsonReader.GetArray(obj, "modules");
            if (moduleArray != null)
                foreach (object m in moduleArray)
                {
                    ParticleModuleSpec module = ParticleModuleSpec.Parse(m as Dictionary<string, object>);
                    if (module != null) spec.modules.Add(module);
                }

            Dictionary<string, object> signalObj = JsonReader.GetObject(obj, "signal");
            if (signalObj != null)
            {
                spec.signal = SignalRefSpec.Parse(signalObj, "particles.signal");
                if (signalObj.TryGetValue("count", out object sc) && sc is double scd) spec.signalCount = (int)scd;
            }
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            // Deterministic, fixed key order so export stays byte-identical.
            var result = new Dictionary<string, object>();
            if (capacity.HasValue) result["capacity"] = (double)capacity.Value;
            if (burstCount.HasValue) result["burstCount"] = (double)burstCount.Value;
            if (rate.HasValue) result["rate"] = (double)rate.Value;
            if (emitOnEnable) result["emitOnEnable"] = true;
            if (!string.IsNullOrEmpty(particleShape)) result["particleShape"] = particleShape;
            if (cornerRadiusPercent.HasValue) result["cornerRadiusPercent"] = (double)cornerRadiusPercent.Value;
            if (sizeRange != null) result["sizeRange"] = ToJsonArray(sizeRange);
            if (lifetimeRange != null) result["lifetimeRange"] = ToJsonArray(lifetimeRange);
            if (speedRange != null) result["speedRange"] = ToJsonArray(speedRange);
            if (emitAngle.HasValue) result["emitAngle"] = (double)emitAngle.Value;
            if (emitSpread.HasValue) result["emitSpread"] = (double)emitSpread.Value;
            if (angularVelocityRange != null) result["angularVelocityRange"] = ToJsonArray(angularVelocityRange);
            if (!string.IsNullOrEmpty(preset)) result["preset"] = preset;
            if (modules != null && modules.Count > 0)
            {
                var array = new List<object>();
                foreach (ParticleModuleSpec module in modules) array.Add(module.ToJsonObject());
                result["modules"] = array;
            }
            if (signal != null)
            {
                var signalObj = signal.ToJsonObject();
                if (signalCount.HasValue) signalObj["count"] = (double)signalCount.Value;
                result["signal"] = signalObj;
            }
            if (atPointer) result["atPointer"] = true;
            return result;
        }

        private static float[] GetFloatArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object value) || !(value is List<object> list)) return null;
            var result = new float[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = list[i] is double d ? (float)d : 0f;
            return result;
        }

        private static List<object> ToJsonArray(float[] values)
        {
            var array = new List<object>(values.Length);
            foreach (float value in values) array.Add((double)value);
            return array;
        }
    }

    /// <summary>
    /// A pointer-follow glow on an element: <c>"pointerGlow": { "color": "#FFFFFFAA", "size": 140,
    /// "softness": 48 }</c>. The generator attaches a <c>NeoPointerReactor</c> that, at runtime, sits a
    /// soft highlight under the cursor while the element is hovered (the mobile-game "glow exactly where
    /// my mouse is" feel). Batch-safe — it only moves a child shape on the shared SDF material. All keys
    /// optional (the reactor ships sensible defaults). Color round-trips as <c>#RRGGBBAA</c>.
    /// </summary>
    [Serializable]
    public class PointerGlowSpec
    {
        public string color;     // hex (#RRGGBB / #RRGGBBAA) or theme token "Category/Name"
        public float? size;      // follower diameter px
        public float? softness;  // follower edge softness px

        public static PointerGlowSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            return new PointerGlowSpec
            {
                color = JsonReader.GetString(obj, "color"),
                size = obj.ContainsKey("size") ? (float?)JsonReader.GetFloat(obj, "size") : null,
                softness = obj.ContainsKey("softness") ? (float?)JsonReader.GetFloat(obj, "softness") : null
            };
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(color)) result["color"] = color;
            if (size.HasValue) result["size"] = (double)size.Value;
            if (softness.HasValue) result["softness"] = (double)softness.Value;
            return result;
        }
    }

    /// <summary>
    /// Per-element animation assignment: copies named library presets into the widget's interaction
    /// animators — <c>"animations": { "hover": "ScaleUpBig", "press": "PressDip", "loop": "Breathe" }</c>.
    /// <c>hover</c>/<c>press</c>/<c>selected</c>/<c>disabled</c> drive a <c>UISelectableUIAnimator</c> (so the
    /// element should be a selectable — button/tab/toggle); <c>loop</c> adds a <c>UIAnimator</c> played on
    /// start (ambient motion on any element). Each value is a preset name resolved through the
    /// <c>AnimationPresetRegistry</c> (exactly like a view's <c>showAnimation</c>); the applied names are
    /// stamped onto a <c>NeoAnimationSourceTag</c> so they round-trip. Distinct from the project-wide
    /// defaults (Setup wizard) — this is per-widget, and overrides the default feel where set.
    /// </summary>
    [Serializable]
    public class ElementAnimationsSpec
    {
        public string hover;
        public string press;
        public string selected;
        public string disabled;
        public string loop;

        public bool IsEmpty =>
            string.IsNullOrEmpty(hover) && string.IsNullOrEmpty(press) && string.IsNullOrEmpty(selected)
            && string.IsNullOrEmpty(disabled) && string.IsNullOrEmpty(loop);

        public static ElementAnimationsSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            var spec = new ElementAnimationsSpec
            {
                hover = JsonReader.GetString(obj, "hover"),
                press = JsonReader.GetString(obj, "press"),
                selected = JsonReader.GetString(obj, "selected"),
                disabled = JsonReader.GetString(obj, "disabled"),
                loop = JsonReader.GetString(obj, "loop")
            };
            return spec.IsEmpty ? null : spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(hover)) result["hover"] = hover;
            if (!string.IsNullOrEmpty(press)) result["press"] = press;
            if (!string.IsNullOrEmpty(selected)) result["selected"] = selected;
            if (!string.IsNullOrEmpty(disabled)) result["disabled"] = disabled;
            if (!string.IsNullOrEmpty(loop)) result["loop"] = loop;
            return result;
        }
    }

    /// <summary>
    /// The Figma-style constraint+offset placement model — the additive, preferred alternative to
    /// the legacy <c>anchor</c>/<c>position</c>/<c>size</c>/<c>flex</c> fields. When present on an
    /// element it WINS; when absent the legacy fields drive generation exactly as before (zero
    /// behavior change for un-migrated specs). Per axis a constraint declares intent — stick to an
    /// edge, stretch both edges, center, or scale proportionally — so the offset is stored relative
    /// to that intent, not as absolute canvas pixels (the structural fix for "moved it in portrait,
    /// it disappears in landscape").
    ///
    /// JSON shape (all keys optional):
    /// <code>
    /// "layout": {
    ///   "h": "leftRight",                  // left|right|leftRight|center|scale   (default "left")
    ///   "v": "center",                     // top|bottom|topBottom|center|scale   (default "top")
    ///   "offset": { "left": 24, "right": 24, "v": 0 },  // keyed BY CONSTRAINT (self-documenting):
    ///                                       //   left/right/top/bottom = edge distance; h/v = signed
    ///                                       //   center offset; leftRight/topBottom reuse left/right/
    ///                                       //   top/bottom as [start,end] insets; scale reuses them
    ///                                       //   as [startFraction,endFraction]
    ///   "size":   { "w": 320, "h": 96 },    // ignored on a stretched axis
    ///   "sizing": { "w": "fill", "h": "fixed" }   // per-child mode in a layout-group parent
    /// }
    /// </code>
    /// Deterministic <see cref="ToJsonObject"/> key order: h, v, offset, size, sizing.
    /// </summary>
    [Serializable]
    public class LayoutSpec
    {
        /// <summary> Horizontal constraint id (default "left" when omitted at apply time). </summary>
        public string h;
        /// <summary> Vertical constraint id (default "top" when omitted at apply time). </summary>
        public string v;
        /// <summary> Per-constraint offsets (see class doc). Forward-compatible string→float dict. </summary>
        public LayoutOffset offset;
        /// <summary> Fixed-axis sizes; ignored on a stretched axis. </summary>
        public LayoutSize size;
        /// <summary> Per-child sizing mode (fixed/hug/fill) in a layout-group parent. </summary>
        public LayoutSizing sizing;

        public bool IsEmpty =>
            string.IsNullOrEmpty(h) && string.IsNullOrEmpty(v)
            && (offset == null || offset.IsEmpty)
            && (size == null || size.IsEmpty)
            && (sizing == null || sizing.IsEmpty);

        public static LayoutSpec Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            var spec = new LayoutSpec
            {
                h = JsonReader.GetString(obj, "h"),
                v = JsonReader.GetString(obj, "v"),
                offset = LayoutOffset.Parse(JsonReader.GetObject(obj, "offset")),
                size = LayoutSize.Parse(JsonReader.GetObject(obj, "size")),
                sizing = LayoutSizing.Parse(JsonReader.GetObject(obj, "sizing"))
            };
            return spec.IsEmpty ? null : spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(h)) result["h"] = h;
            if (!string.IsNullOrEmpty(v)) result["v"] = v;
            if (offset != null && !offset.IsEmpty) result["offset"] = offset.ToJsonObject();
            if (size != null && !size.IsEmpty) result["size"] = size.ToJsonObject();
            if (sizing != null && !sizing.IsEmpty) result["sizing"] = sizing.ToJsonObject();
            return result;
        }

        /// <summary>
        /// The cascade primitive (Pillar B): this layout treated as the BASE, merged field-by-field
        /// with <paramref name="delta"/> (a breakpoint override). Set fields in the delta win; unset
        /// fields inherit the base. Merge granularity matches the model: <see cref="LayoutOffset"/>
        /// merges per key (left/right/top/bottom/h/v), <see cref="LayoutSize"/>/<see cref="LayoutSizing"/>
        /// merge per axis, and the scalar <c>h</c>/<c>v</c> constraints replace when set. A null/empty
        /// delta returns a copy of the base; a null base treats the delta as the whole layout.
        /// </summary>
        public LayoutSpec MergedWith(LayoutSpec delta)
        {
            if (delta == null || delta.IsEmpty) return Clone();
            var result = new LayoutSpec
            {
                h = !string.IsNullOrEmpty(delta.h) ? delta.h : h,
                v = !string.IsNullOrEmpty(delta.v) ? delta.v : v,
                offset = LayoutOffset.Merge(offset, delta.offset),
                size = LayoutSize.Merge(size, delta.size),
                sizing = LayoutSizing.Merge(sizing, delta.sizing)
            };
            return result;
        }

        /// <summary> A deep copy (so a merge never aliases the base's sub-objects). </summary>
        public LayoutSpec Clone() => new LayoutSpec
        {
            h = h,
            v = v,
            offset = offset != null && !offset.IsEmpty ? LayoutOffset.Merge(offset, null) : null,
            size = size != null && !size.IsEmpty ? LayoutSize.Merge(size, null) : null,
            sizing = sizing != null && !sizing.IsEmpty ? LayoutSizing.Merge(sizing, null) : null
        };
    }

    /// <summary>
    /// Per-constraint offsets, keyed by constraint name (left/right/top/bottom/h/v) so the JSON is
    /// self-documenting — a given axis has exactly one constraint, so a key set is unambiguous.
    /// Backed by an ordered string→float dict to stay forward-compatible with project constraints.
    /// Deterministic emit order: left, right, top, bottom, h, v (then any extra keys, sorted).
    /// </summary>
    [Serializable]
    public class LayoutOffset
    {
        private static readonly string[] CanonicalOrder = { "left", "right", "top", "bottom", "h", "v" };

        public Dictionary<string, float> values = new Dictionary<string, float>();

        public bool IsEmpty => values == null || values.Count == 0;

        public bool TryGet(string key, out float value) => values.TryGetValue(key, out value);
        public float GetOr(string key, float fallback) => values.TryGetValue(key, out float v) ? v : fallback;
        public void Set(string key, float value) { if (!string.IsNullOrEmpty(key)) values[key] = value; }

        public static LayoutOffset Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            var result = new LayoutOffset();
            foreach (KeyValuePair<string, object> entry in obj)
                if (entry.Value is double d) result.values[entry.Key] = (float)d;
            return result.IsEmpty ? null : result;
        }

        /// <summary> Per-key merge: base keys, then delta keys override. Either side may be null. </summary>
        public static LayoutOffset Merge(LayoutOffset baseOffset, LayoutOffset delta)
        {
            bool hasBase = baseOffset != null && !baseOffset.IsEmpty;
            bool hasDelta = delta != null && !delta.IsEmpty;
            if (!hasBase && !hasDelta) return null;
            var result = new LayoutOffset();
            if (hasBase)
                foreach (KeyValuePair<string, float> entry in baseOffset.values) result.values[entry.Key] = entry.Value;
            if (hasDelta)
                foreach (KeyValuePair<string, float> entry in delta.values) result.values[entry.Key] = entry.Value;
            return result.IsEmpty ? null : result;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            foreach (string key in CanonicalOrder)
                if (values.TryGetValue(key, out float v)) result[key] = (double)v;
            var extras = new List<string>();
            foreach (string key in values.Keys)
                if (System.Array.IndexOf(CanonicalOrder, key) < 0) extras.Add(key);
            extras.Sort(System.StringComparer.Ordinal);
            foreach (string key in extras) result[key] = (double)values[key];
            return result;
        }
    }

    /// <summary> Fixed-axis sizes { w, h }; ignored on a stretched axis. </summary>
    [Serializable]
    public class LayoutSize
    {
        public float? w;
        public float? h;

        public bool IsEmpty => !w.HasValue && !h.HasValue;

        public static LayoutSize Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            var result = new LayoutSize();
            if (obj.TryGetValue("w", out object wv) && wv is double wd) result.w = (float)wd;
            if (obj.TryGetValue("h", out object hv) && hv is double hd) result.h = (float)hd;
            return result.IsEmpty ? null : result;
        }

        /// <summary> Per-axis merge: a set delta axis wins, else the base axis. Either side may be null. </summary>
        public static LayoutSize Merge(LayoutSize baseSize, LayoutSize delta)
        {
            bool hasBase = baseSize != null && !baseSize.IsEmpty;
            bool hasDelta = delta != null && !delta.IsEmpty;
            if (!hasBase && !hasDelta) return null;
            var result = new LayoutSize
            {
                w = delta != null && delta.w.HasValue ? delta.w : baseSize?.w,
                h = delta != null && delta.h.HasValue ? delta.h : baseSize?.h
            };
            return result.IsEmpty ? null : result;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (w.HasValue) result["w"] = (double)w.Value;
            if (h.HasValue) result["h"] = (double)h.Value;
            return result;
        }
    }

    /// <summary> Per-child sizing modes { w, h } ∈ {fixed, hug, fill} for a layout-group child. </summary>
    [Serializable]
    public class LayoutSizing
    {
        public string w;
        public string h;

        public bool IsEmpty => string.IsNullOrEmpty(w) && string.IsNullOrEmpty(h);

        public static LayoutSizing Parse(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            var result = new LayoutSizing
            {
                w = JsonReader.GetString(obj, "w"),
                h = JsonReader.GetString(obj, "h")
            };
            return result.IsEmpty ? null : result;
        }

        /// <summary> Per-axis merge: a set delta axis wins, else the base axis. Either side may be null. </summary>
        public static LayoutSizing Merge(LayoutSizing baseSizing, LayoutSizing delta)
        {
            bool hasBase = baseSizing != null && !baseSizing.IsEmpty;
            bool hasDelta = delta != null && !delta.IsEmpty;
            if (!hasBase && !hasDelta) return null;
            var result = new LayoutSizing
            {
                w = delta != null && !string.IsNullOrEmpty(delta.w) ? delta.w : baseSizing?.w,
                h = delta != null && !string.IsNullOrEmpty(delta.h) ? delta.h : baseSizing?.h
            };
            return result.IsEmpty ? null : result;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(w)) result["w"] = w;
            if (!string.IsNullOrEmpty(h)) result["h"] = h;
            return result;
        }
    }

    /// <summary>
    /// One element in a view. Widgets: button, toggle, switch, tab, slider, progress, tabbar,
    /// list, text, image, shape. Layout containers: vstack, hstack, grid, scroll, spacer —
    /// containers carry padding/spacing(/columns/cellSize) and nest anything via "children".
    /// Common optional fields: anchor (preset name: TopLeft…BottomRight, Stretch, StretchTop, …),
    /// size [w,h], position [x,y], style (theme shape style), background (color token).
    /// </summary>
    [Serializable]
    public class ElementSpec
    {
        public static readonly string[] Kinds =
        {
            "button", "toggle", "switch", "tab", "slider", "progress", "tabbar", "list", "scroll",
            "vstack", "hstack", "grid", "panel", "overlay", "spacer", "text", "image", "shape", "icon",
            "input", "stepper", "safearea", "counter", "dropdown", "settings", "cheats"
        };

        /// <summary> The built-in <see cref="Kinds"/> unioned with any project-registered
        /// <see cref="NeoElementKinds"/>. Validators, parsers and pickers read THIS so a registered novel
        /// kind isn't flagged invalid; <see cref="Kinds"/> stays the pure built-in list. </summary>
        public static IReadOnlyList<string> KnownKinds
        {
            get
            {
                var list = new List<string>(Kinds);
                foreach (INeoElementKind k in NeoElementKinds.All)
                    if (k != null && !string.IsNullOrEmpty(k.Kind) && !list.Contains(k.Kind))
                        list.Add(k.Kind);
                return list;
            }
        }

        public string kind;
        public string id;   // "Category/Name" for interactive elements
        public string label;
        public string labelColor;  // theme token
        public string background;  // theme token
        public string style;       // theme shape style name (NeoShape surfaces)
        public string shape;       // for kind "shape": roundedRect / circle / pill / checkmark / chevron / cross / ring / arc
        public GradientSpec gradient; // shape/image: theme-riding two-stop gradient
        public EffectSpec effect;     // open-bag shape effect (ShapeEffectRegistry owns parse/bake/export)
        public ParticleSpec particles; // UI particle emitter (modules ride ParticleEffectRegistry)
        public PointerGlowSpec pointerGlow; // pointer-follow glow (NeoPointerReactor)
        public ElementAnimationsSpec animations; // per-element interaction presets (hover/press/loop…)
        public float? thickness;   // ring/arc band width px
        public float? arcStart;    // arc start angle, degrees cw from 12 o'clock
        public float? arcSweep;    // arc sweep, degrees
        public string icon;        // Lucide icon name — kind "icon" ("name" key) or button/tab slot ("icon" key)
        public string src;         // image: sprite asset path ("Assets/...") — rendered as an NeoShape
                                   // texture fill so "radius" rounds the corners; full-rect sprites only
        public string fit;         // image: "cover" crops a centered sub-rect to fill the rect
                                   // (preserves art aspect); absent = stretch
        public string variant;     // button: primary / secondary / ghost / danger
        public string sizeVariant; // button: sm / md / lg (JSON key "size" — string form)
        public string preset;      // reusable widget style this element references by name (NeoWidgetPreset).
                                   // Resolved at generate as the BASE; element-level fields override. Exports
                                   // as preset name + only the override delta (the link survives round-trip).
        public bool cascade;       // vstack/hstack/grid: staggered child entrance on show
        public float? badge;       // button/tab: notification badge count (0 = hidden)
        public float? radius;      // corner radius override (px)
        public string anchor;      // anchor preset name (legacy placement)
        public LayoutSpec layout;  // Figma-style constraint+offset placement; when set it WINS over
                                   // anchor/position/size/flex (which stay valid when layout is null)
        public Dictionary<string, LayoutSpec> overrides; // Pillar B: breakpoint name → delta LayoutSpec.
                                   // Only changed fields per breakpoint; merges OVER the base layout at
                                   // runtime (LayoutSpec.MergedWith). Null/empty → emits nothing.
        public float[] size;       // [w,h] (JSON key "size" — array form)
        public float? flex;        // in stacks: share of leftover space on the parent's main axis
                                   // (size becomes the minimum); 0/absent = rigid authored size
        public float? rotation;    // z rotation in degrees (corner ribbons, slanted banners)
        public string outlineColor; // text: SDF outline color (hex or theme token, baked to a
                                    // cached material — outlined card names over art)
        public float? outlineWidth; // text: SDF outline width 0..1 (default 0.25 when only the
                                    // color is given)
        public float[] position;   // [x,y] anchored position
        public float? padding;     // containers (uniform)
        public float[] padding4;   // containers: per-side [left, top, right, bottom]; when present it
                                   // WINS over uniform "padding". Absent → the uniform path is unchanged.
        public float? spacing;     // containers
        public int? columns;       // grid
        public float[] cellSize;   // grid [w,h]
        public float? min;         // slider / progress / stepper
        public float? max;
        public float? value;       // slider / stepper
        public float? step;        // stepper increment
        public float? fontSize;    // text (styleless fallback — a textStyle owns the size)
        public string textStyle;   // theme text style name (text + button/toggle/tab labels)
        public string align;       // text: left | center | right (center default); stacks: childAlignment (left default)
        public string controls;    // tab: id of the sibling "panel" this tab shows/hides
        public string group;       // tab: standalone tabs sharing a group name in one view get
                                   // one-on exclusivity (a shared UIToggleGroup on the view root)
        public string catalog;     // settings/cheats: id of the catalog this menu element presents
        public List<string> options; // dropdown: option labels in order (value is the selected index)
        public string bind;          // list/grid: UIData source id ("Category/Name") feeding rows at runtime
        public ElementSpec item;     // list/grid: row template, cloned per data row ({key} tokens in text)
        public SignalRefSpec onClickSignal;
        public SignalRefSpec signal;   // toggle/slider/dropdown: domain stream the widget publishes its
                                       // typed value to (bool/float/int), IN ADDITION to the standard
                                       // "UIToggle/UISlider/UIDropdown Behaviour" stream — so game code can
                                       // Signals.On<T>(category, name, …) without branching the firehose
        public string onClickShowView; // "Category/Name"
        public string onClickHideView;
        public string onClickPopup;    // popup name from the popups section
        public bool onClickClose;      // button: hides the enclosing container (popup CTA / dismiss)
        public List<ElementSpec> children = new List<ElementSpec>();

        public static ElementSpec Parse(Dictionary<string, object> obj)
        {
            foreach (string kind in KnownKinds)
            {
                Dictionary<string, object> body = JsonReader.GetObject(obj, kind);
                if (body == null) continue;
                // "scroll" is a forgiving alias for "list" — normalized here (parse time) rather
                // than dual-accepted downstream, so an authored "scroll" element is byte-stable on
                // export (it always re-serializes as "list", the canonical kind).
                string resolvedKind = kind == "scroll" ? "list" : kind;
                var spec = new ElementSpec
                {
                    kind = resolvedKind,
                    id = JsonReader.GetString(body, "id"),
                    label = JsonReader.GetString(body, "label"),
                    labelColor = JsonReader.GetString(body, "labelColor") ?? JsonReader.GetString(body, "color"),
                    background = JsonReader.GetString(body, "background"),
                    style = JsonReader.GetString(body, "style"),
                    shape = JsonReader.GetString(body, "shape"),
                    variant = JsonReader.GetString(body, "variant"),
                    preset = JsonReader.GetString(body, "preset"),
                    anchor = JsonReader.GetString(body, "anchor"),
                    layout = LayoutSpec.Parse(JsonReader.GetObject(body, "layout")),
                    radius = GetNullableFloat(body, "radius"),
                    size = GetFloatArray(body, "size"),
                    position = GetFloatArray(body, "position"),
                    padding = GetNullableFloat(body, "padding"),
                    padding4 = GetFloatArray(body, "padding4"),
                    spacing = GetNullableFloat(body, "spacing"),
                    cellSize = GetFloatArray(body, "cellSize"),
                    min = GetNullableFloat(body, "min"),
                    max = GetNullableFloat(body, "max"),
                    value = GetNullableFloat(body, "value"),
                    step = GetNullableFloat(body, "step"),
                    flex = GetNullableFloat(body, "flex"),
                    rotation = GetNullableFloat(body, "rotation"),
                    outlineColor = JsonReader.GetString(body, "outlineColor"),
                    outlineWidth = GetNullableFloat(body, "outlineWidth"),
                    fontSize = GetNullableFloat(body, "fontSize"),
                    textStyle = JsonReader.GetString(body, "textStyle"),
                    align = JsonReader.GetString(body, "align"),
                    controls = JsonReader.GetString(body, "controls"),
                    group = JsonReader.GetString(body, "group"),
                    src = JsonReader.GetString(body, "src"),
                    fit = JsonReader.GetString(body, "fit"),
                    bind = JsonReader.GetString(body, "bind"),
                    catalog = JsonReader.GetString(body, "catalog"),
                    gradient = GradientSpec.Parse(JsonReader.GetObject(body, "gradient")),
                    effect = EffectSpec.Parse(JsonReader.GetObject(body, "effect")),
                    particles = ParticleSpec.Parse(JsonReader.GetObject(body, "particles")),
                    pointerGlow = PointerGlowSpec.Parse(JsonReader.GetObject(body, "pointerGlow")),
                    animations = ElementAnimationsSpec.Parse(JsonReader.GetObject(body, "animations")),
                    thickness = GetNullableFloat(body, "thickness"),
                    arcStart = GetNullableFloat(body, "arcStart"),
                    arcSweep = GetNullableFloat(body, "arcSweep")
                };
                float? columnsValue = GetNullableFloat(body, "columns");
                if (columnsValue.HasValue) spec.columns = (int)columnsValue.Value;

                // "size" is polymorphic: a string is the button size variant, an array is [w,h]
                if (body.TryGetValue("size", out object sizeValue) && sizeValue is string sizeName)
                    spec.sizeVariant = sizeName;

                spec.cascade = JsonReader.GetBool(body, "cascade");
                spec.badge = GetNullableFloat(body, "badge");

                // icon elements take "name"; widget slots take "icon" — also accept a scalar size
                spec.icon = JsonReader.GetString(body, "icon");
                if (kind == "icon")
                {
                    if (string.IsNullOrEmpty(spec.icon)) spec.icon = JsonReader.GetString(body, "name");
                    if (spec.size == null)
                    {
                        float? scalar = GetNullableFloat(body, "size");
                        if (scalar.HasValue) spec.size = new[] { scalar.Value, scalar.Value };
                    }
                }

                // first-class domain signal on toggle/slider/dropdown (button uses onClick.signal)
                if (body.TryGetValue("signal", out object domainSignal))
                    spec.signal = SignalRefSpec.Parse(domainSignal, "signal");

                Dictionary<string, object> onClick = JsonReader.GetObject(body, "onClick");
                if (onClick != null)
                {
                    if (onClick.TryGetValue("signal", out object signal))
                        spec.onClickSignal = SignalRefSpec.Parse(signal, "onClick.signal");
                    spec.onClickShowView = JsonReader.GetString(onClick, "showView");
                    spec.onClickHideView = JsonReader.GetString(onClick, "hideView");
                    spec.onClickPopup = JsonReader.GetString(onClick, "popup");
                    spec.onClickClose = JsonReader.GetBool(onClick, "close");
                }

                List<object> optionArray = JsonReader.GetArray(body, "options");
                if (optionArray != null)
                {
                    spec.options = new List<string>();
                    foreach (object option in optionArray)
                        if (option != null) spec.options.Add(option.ToString());
                }

                // Pillar B: per-breakpoint layout deltas, keyed by breakpoint name.
                Dictionary<string, object> overridesObj = JsonReader.GetObject(body, "overrides");
                if (overridesObj != null)
                {
                    var map = new Dictionary<string, LayoutSpec>();
                    foreach (KeyValuePair<string, object> entry in overridesObj)
                    {
                        LayoutSpec delta = LayoutSpec.Parse(JsonReader.AsObject(entry.Value, $"override '{entry.Key}'"));
                        if (delta != null) map[entry.Key] = delta;
                    }
                    if (map.Count > 0) spec.overrides = map;
                }

                Dictionary<string, object> itemObj = JsonReader.GetObject(body, "item");
                if (itemObj != null) spec.item = Parse(itemObj);

                List<object> childArray = JsonReader.GetArray(body, "children");
                if (childArray != null)
                    foreach (object item in childArray)
                        spec.children.Add(Parse(JsonReader.AsObject(item, "child element")));
                return spec;
            }
            throw new FormatException($"Element must contain one of: {string.Join(", ", KnownKinds)}");
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(id)) body["id"] = id;
            if (!string.IsNullOrEmpty(label)) body["label"] = label;
            if (!string.IsNullOrEmpty(icon)) body[kind == "icon" ? "name" : "icon"] = icon;
            if (!string.IsNullOrEmpty(labelColor))
                body[kind == "text" || kind == "icon" ? "color" : "labelColor"] = labelColor;
            if (!string.IsNullOrEmpty(background)) body["background"] = background;
            if (!string.IsNullOrEmpty(style)) body["style"] = style;
            if (!string.IsNullOrEmpty(shape)) body["shape"] = shape;
            if (gradient != null) body["gradient"] = gradient.ToJsonObject();
            if (effect != null) body["effect"] = effect.ToJsonObject();
            if (particles != null) body["particles"] = particles.ToJsonObject();
            if (pointerGlow != null) body["pointerGlow"] = pointerGlow.ToJsonObject();
            if (animations != null) body["animations"] = animations.ToJsonObject();
            if (thickness.HasValue) body["thickness"] = (double)thickness.Value;
            if (arcStart.HasValue) body["arcStart"] = (double)arcStart.Value;
            if (arcSweep.HasValue) body["arcSweep"] = (double)arcSweep.Value;
            if (!string.IsNullOrEmpty(variant)) body["variant"] = variant;
            if (!string.IsNullOrEmpty(preset)) body["preset"] = preset;
            if (radius.HasValue) body["radius"] = (double)radius.Value;
            if (!string.IsNullOrEmpty(anchor)) body["anchor"] = anchor;
            if (layout != null && !layout.IsEmpty) body["layout"] = layout.ToJsonObject();
            if (overrides != null && overrides.Count > 0)
            {
                // deterministic key order so export is byte-stable regardless of dict insertion order
                var keys = new List<string>(overrides.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                var map = new Dictionary<string, object>();
                foreach (string key in keys)
                {
                    LayoutSpec delta = overrides[key];
                    if (delta != null && !delta.IsEmpty) map[key] = delta.ToJsonObject();
                }
                if (map.Count > 0) body["overrides"] = map;
            }
            // string size variant and [w,h] share the "size" key — the variant owns it when set
            if (!string.IsNullOrEmpty(sizeVariant)) body["size"] = sizeVariant;
            else if (size != null) body["size"] = ToJsonArray(size);
            if (position != null) body["position"] = ToJsonArray(position);
            if (padding.HasValue) body["padding"] = (double)padding.Value;
            if (padding4 != null) body["padding4"] = ToJsonArray(padding4);
            if (spacing.HasValue) body["spacing"] = (double)spacing.Value;
            if (columns.HasValue) body["columns"] = (double)columns.Value;
            if (cellSize != null) body["cellSize"] = ToJsonArray(cellSize);
            if (min.HasValue) body["min"] = (double)min.Value;
            if (max.HasValue) body["max"] = (double)max.Value;
            if (value.HasValue) body["value"] = (double)value.Value;
            if (step.HasValue) body["step"] = (double)step.Value;
            if (flex.HasValue) body["flex"] = (double)flex.Value;
            if (rotation.HasValue) body["rotation"] = (double)rotation.Value;
            if (!string.IsNullOrEmpty(outlineColor)) body["outlineColor"] = outlineColor;
            if (outlineWidth.HasValue) body["outlineWidth"] = (double)outlineWidth.Value;
            if (fontSize.HasValue) body["fontSize"] = (double)fontSize.Value;
            if (!string.IsNullOrEmpty(textStyle)) body["textStyle"] = textStyle;
            if (!string.IsNullOrEmpty(align)) body["align"] = align;
            if (!string.IsNullOrEmpty(controls)) body["controls"] = controls;
            if (!string.IsNullOrEmpty(group)) body["group"] = group;
            if (!string.IsNullOrEmpty(catalog)) body["catalog"] = catalog;
            if (!string.IsNullOrEmpty(src)) body["src"] = src;
            if (!string.IsNullOrEmpty(fit)) body["fit"] = fit;
            if (!string.IsNullOrEmpty(bind)) body["bind"] = bind;
            if (item != null) body["item"] = item.ToJsonObject();
            if (options != null && options.Count > 0)
            {
                var array = new List<object>();
                foreach (string option in options) array.Add(option);
                body["options"] = array;
            }
            if (cascade) body["cascade"] = true;
            if (badge.HasValue) body["badge"] = (double)badge.Value;
            if (signal != null) body["signal"] = signal.ToJsonObject();
            if (onClickSignal != null || !string.IsNullOrEmpty(onClickShowView)
                || !string.IsNullOrEmpty(onClickHideView) || !string.IsNullOrEmpty(onClickPopup)
                || onClickClose)
            {
                var onClick = new Dictionary<string, object>();
                if (onClickSignal != null) onClick["signal"] = onClickSignal.ToJsonObject();
                if (!string.IsNullOrEmpty(onClickShowView)) onClick["showView"] = onClickShowView;
                if (!string.IsNullOrEmpty(onClickHideView)) onClick["hideView"] = onClickHideView;
                if (!string.IsNullOrEmpty(onClickPopup)) onClick["popup"] = onClickPopup;
                if (onClickClose) onClick["close"] = true;
                body["onClick"] = onClick;
            }
            if (children.Count > 0)
            {
                var array = new List<object>();
                foreach (ElementSpec child in children) array.Add(child.ToJsonObject());
                body["children"] = array;
            }
            return new Dictionary<string, object> { [kind] = body };
        }

        /// <summary>
        /// A member-wise copy used when resolving a preset into an "effective" element: the generator
        /// clones, then overlays preset values onto the fields the element leaves unset. MemberwiseClone
        /// copies every field automatically (so new fields stay covered); list/child references are shared
        /// (the generate pass never mutates them).
        /// </summary>
        public ElementSpec ShallowClone() => (ElementSpec)MemberwiseClone();

        private static float? GetNullableFloat(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out object value) && value is double d ? (float?)(float)d : null;

        private static float[] GetFloatArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out object value) || !(value is List<object> list)) return null;
            var result = new float[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = list[i] is double d ? (float)d : 0f;
            return result;
        }

        private static List<object> ToJsonArray(float[] values)
        {
            var array = new List<object>(values.Length);
            foreach (float value in values) array.Add((double)value);
            return array;
        }
    }

    /// <summary>
    /// A modal popup template: generated as a UIPopup prefab and database entry. Plain form
    /// (title/message only) gets the canonical OK button; the rich form adds "elements" (the same
    /// element vocabulary as views, stacked in the card), an optional "size" [w,h] card override
    /// and "close": true for an X dismiss button pinned to the card's top-right corner.
    /// </summary>
    [Serializable]
    public class PopupSpec
    {
        public string name;
        public string title;
        public string message;
        public float[] size;       // card [w,h]; omitted = factory default
        public bool close;         // X button on the card corner
        public List<ElementSpec> elements = new List<ElementSpec>();

        public static PopupSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new PopupSpec
            {
                name = JsonReader.GetString(obj, "name"),
                title = JsonReader.GetString(obj, "title"),
                message = JsonReader.GetString(obj, "message"),
                close = JsonReader.GetBool(obj, "close")
            };
            if (string.IsNullOrWhiteSpace(spec.name))
                throw new FormatException("Popup is missing required field 'name'");

            if (obj.TryGetValue("size", out object sizeValue) && sizeValue is List<object> sizeList)
            {
                spec.size = new float[sizeList.Count];
                for (int i = 0; i < sizeList.Count; i++)
                    spec.size[i] = sizeList[i] is double d ? (float)d : 0f;
            }

            List<object> elementArray = JsonReader.GetArray(obj, "elements");
            if (elementArray != null)
                foreach (object item in elementArray)
                    spec.elements.Add(ElementSpec.Parse(JsonReader.AsObject(item, "popup element")));
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object> { ["name"] = name };
            if (!string.IsNullOrEmpty(title)) result["title"] = title;
            if (!string.IsNullOrEmpty(message)) result["message"] = message;
            if (size != null && size.Length >= 2)
            {
                var array = new List<object>();
                foreach (float value in size) array.Add((double)value);
                result["size"] = array;
            }
            if (close) result["close"] = true;
            if (elements.Count > 0)
            {
                var array = new List<object>();
                foreach (ElementSpec element in elements) array.Add(element.ToJsonObject());
                result["elements"] = array;
            }
            return result;
        }
    }

    [Serializable]
    public class ViewSpec
    {
        public string category;
        public string viewName;
        public string showAnimation; // preset name
        public string hideAnimation;
        public string background;    // theme token for a full-bleed background image
        public List<ElementSpec> elements = new List<ElementSpec>();

        public string id => $"{category}/{viewName}";

        public static ViewSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new ViewSpec();
            string idValue = JsonReader.GetString(obj, "id");
            if (string.IsNullOrWhiteSpace(idValue))
                throw new FormatException("View is missing required field 'id' (\"Category/Name\")");
            CategoryNameId.Parse(idValue, out spec.category, out spec.viewName);
            spec.showAnimation = JsonReader.GetString(obj, "showAnimation");
            spec.hideAnimation = JsonReader.GetString(obj, "hideAnimation");
            spec.background = JsonReader.GetString(obj, "background");

            List<object> elementArray = JsonReader.GetArray(obj, "elements");
            if (elementArray != null)
                foreach (object item in elementArray)
                    spec.elements.Add(ElementSpec.Parse(JsonReader.AsObject(item, "element")));
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object> { ["id"] = id };
            if (!string.IsNullOrEmpty(showAnimation)) result["showAnimation"] = showAnimation;
            if (!string.IsNullOrEmpty(hideAnimation)) result["hideAnimation"] = hideAnimation;
            if (!string.IsNullOrEmpty(background)) result["background"] = background;
            if (elements.Count > 0)
            {
                var array = new List<object>();
                foreach (ElementSpec element in elements) array.Add(element.ToJsonObject());
                result["elements"] = array;
            }
            return result;
        }
    }

    [Serializable]
    public class FlowEdgeSpec
    {
        public string to;
        public bool allowsBack = true;
        public FlowTrigger trigger = new FlowTrigger();

        public static FlowEdgeSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new FlowEdgeSpec
            {
                to = JsonReader.GetString(obj, "to"),
                allowsBack = JsonReader.GetBool(obj, "allowsBack", true)
            };
            if (string.IsNullOrWhiteSpace(spec.to))
                throw new FormatException("Flow edge is missing required field 'to'");

            Dictionary<string, object> on = JsonReader.GetObject(obj, "on");
            if (on != null) spec.trigger = ParseTrigger(on);
            return spec;
        }

        public static FlowTrigger ParseTrigger(Dictionary<string, object> on)
        {
            var trigger = new FlowTrigger();
            if (on.TryGetValue("button", out object button))
            {
                trigger.type = FlowTrigger.TriggerType.ButtonClick;
                CategoryNameId.Parse(button.ToString(), out trigger.category, out trigger.name);
            }
            else if (on.TryGetValue("signal", out object signal))
            {
                trigger.type = FlowTrigger.TriggerType.Signal;
                SignalRefSpec reference = SignalRefSpec.Parse(signal, "on.signal");
                trigger.category = reference.category;
                trigger.name = reference.name;
            }
            else if (on.TryGetValue("toggleOn", out object toggleOn))
            {
                trigger.type = FlowTrigger.TriggerType.ToggleOn;
                CategoryNameId.Parse(toggleOn.ToString(), out trigger.category, out trigger.name);
            }
            else if (on.TryGetValue("toggleOff", out object toggleOff))
            {
                trigger.type = FlowTrigger.TriggerType.ToggleOff;
                CategoryNameId.Parse(toggleOff.ToString(), out trigger.category, out trigger.name);
            }
            else if (on.TryGetValue("viewShown", out object viewShown))
            {
                trigger.type = FlowTrigger.TriggerType.ViewShown;
                CategoryNameId.Parse(viewShown.ToString(), out trigger.category, out trigger.name);
            }
            else if (on.TryGetValue("viewHidden", out object viewHidden))
            {
                trigger.type = FlowTrigger.TriggerType.ViewHidden;
                CategoryNameId.Parse(viewHidden.ToString(), out trigger.category, out trigger.name);
            }
            else if (on.ContainsKey("back"))
            {
                trigger.type = FlowTrigger.TriggerType.Back;
            }
            else if (on.TryGetValue("timer", out object timer) && timer is double seconds)
            {
                trigger.type = FlowTrigger.TriggerType.Timer;
                trigger.timerDuration = (float)seconds;
            }
            else
            {
                // Extensibility seam: fall through to a project-registered custom trigger kind, keyed
                // by its JSON key. Its payload mirrors the built-in "Category/Name" form.
                bool matched = false;
                foreach (INeoTriggerKind kind in NeoTriggerKinds.All)
                {
                    if (!on.TryGetValue(kind.JsonKey, out object custom)) continue;
                    trigger.type = FlowTrigger.TriggerType.Custom;
                    trigger.customKind = kind.Id;
                    CategoryNameId.Parse(custom?.ToString(), out trigger.category, out trigger.name);
                    matched = true;
                    break;
                }
                // No silent failures: an "on" object whose only key matches neither a built-in nor a
                // registered kind is almost certainly a typo or an unloaded project trigger.
                if (!matched && on.Count > 0)
                    Debug.LogWarning($"FlowEdge 'on' trigger has no recognized key ({string.Join(", ", on.Keys)}); edge will never fire. Built-in keys: button, signal, toggleOn, toggleOff, viewShown, viewHidden, back, timer — or register a custom kind in NeoTriggerKinds.");
            }
            return trigger;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object> { ["to"] = to };
            if (!allowsBack) result["allowsBack"] = false;
            Dictionary<string, object> on = TriggerToJson(trigger);
            if (on != null) result["on"] = on;
            return result;
        }

        public static Dictionary<string, object> TriggerToJson(FlowTrigger trigger)
        {
            if (trigger == null) return null;
            switch (trigger.type)
            {
                case FlowTrigger.TriggerType.ButtonClick:
                    return new Dictionary<string, object> { ["button"] = $"{trigger.category}/{trigger.name}" };
                case FlowTrigger.TriggerType.Signal:
                    return new Dictionary<string, object>
                    {
                        ["signal"] = new Dictionary<string, object> { ["category"] = trigger.category, ["name"] = trigger.name }
                    };
                case FlowTrigger.TriggerType.ToggleOn:
                    return new Dictionary<string, object> { ["toggleOn"] = $"{trigger.category}/{trigger.name}" };
                case FlowTrigger.TriggerType.ToggleOff:
                    return new Dictionary<string, object> { ["toggleOff"] = $"{trigger.category}/{trigger.name}" };
                case FlowTrigger.TriggerType.ViewShown:
                    return new Dictionary<string, object> { ["viewShown"] = $"{trigger.category}/{trigger.name}" };
                case FlowTrigger.TriggerType.ViewHidden:
                    return new Dictionary<string, object> { ["viewHidden"] = $"{trigger.category}/{trigger.name}" };
                case FlowTrigger.TriggerType.Back:
                    return new Dictionary<string, object> { ["back"] = true };
                case FlowTrigger.TriggerType.Timer:
                    return new Dictionary<string, object> { ["timer"] = (double)trigger.timerDuration };
                case FlowTrigger.TriggerType.Custom:
                    if (NeoTriggerKinds.TryGet(trigger.customKind, out INeoTriggerKind kind))
                        return new Dictionary<string, object> { [kind.JsonKey] = $"{trigger.category}/{trigger.name}" };
                    Debug.LogWarning($"FlowTrigger.TriggerToJson: custom trigger kind '{trigger.customKind}' is not registered; trigger dropped from export. Register it in NeoTriggerKinds.");
                    return null;
                default:
                    return null;
            }
        }
    }

    [Serializable]
    public class FlowNodeSpec
    {
        public string name;
        /// <summary> Views shown by this node ("Category/Name"). Spec accepts a single "view" or a
        /// "views" array; canonical export writes "view" for one, "views" for several. </summary>
        public List<string> views = new List<string>();
        /// <summary> Views hidden when this node activates ("hide" array). </summary>
        public List<string> hide = new List<string>();
        public List<FlowEdgeSpec> next = new List<FlowEdgeSpec>();

        /// <summary> Back-compat accessor: the single/first shown view. </summary>
        public string view
        {
            get => views.Count > 0 ? views[0] : null;
            set
            {
                views.Clear();
                if (!string.IsNullOrEmpty(value)) views.Add(value);
            }
        }

        public static FlowNodeSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new FlowNodeSpec
            {
                name = JsonReader.GetString(obj, "name")
            };
            if (string.IsNullOrWhiteSpace(spec.name))
                throw new FormatException("Flow node is missing required field 'name'");

            string singleView = JsonReader.GetString(obj, "view");
            if (!string.IsNullOrEmpty(singleView)) spec.views.Add(singleView);
            List<object> viewArray = JsonReader.GetArray(obj, "views");
            if (viewArray != null)
                foreach (object item in viewArray)
                    if (item != null) spec.views.Add(item.ToString());

            List<object> hideArray = JsonReader.GetArray(obj, "hide");
            if (hideArray != null)
                foreach (object item in hideArray)
                    if (item != null) spec.hide.Add(item.ToString());

            List<object> nextArray = JsonReader.GetArray(obj, "next");
            if (nextArray != null)
                foreach (object item in nextArray)
                    spec.next.Add(FlowEdgeSpec.Parse(JsonReader.AsObject(item, "flow edge")));
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object> { ["name"] = name };
            if (views.Count == 1) result["view"] = views[0];
            else if (views.Count > 1)
            {
                var array = new List<object>();
                foreach (string entry in views) array.Add(entry);
                result["views"] = array;
            }
            if (hide.Count > 0)
            {
                var array = new List<object>();
                foreach (string entry in hide) array.Add(entry);
                result["hide"] = array;
            }
            if (next.Count > 0)
            {
                var array = new List<object>();
                foreach (FlowEdgeSpec edge in next) array.Add(edge.ToJsonObject());
                result["next"] = array;
            }
            return result;
        }
    }

    [Serializable]
    public class FlowSpec
    {
        public string name = "UI";
        public string start;
        public List<FlowNodeSpec> nodes = new List<FlowNodeSpec>();

        public static FlowSpec Parse(Dictionary<string, object> obj)
        {
            var spec = new FlowSpec
            {
                name = JsonReader.GetString(obj, "name", "UI"),
                start = JsonReader.GetString(obj, "start")
            };
            List<object> nodeArray = JsonReader.GetArray(obj, "nodes");
            if (nodeArray != null)
                foreach (object item in nodeArray)
                    spec.nodes.Add(FlowNodeSpec.Parse(JsonReader.AsObject(item, "flow node")));
            return spec;
        }

        public Dictionary<string, object> ToJsonObject()
        {
            var result = new Dictionary<string, object> { ["name"] = name };
            if (!string.IsNullOrEmpty(start)) result["start"] = start;
            if (nodes.Count > 0)
            {
                var array = new List<object>();
                foreach (FlowNodeSpec node in nodes) array.Add(node.ToJsonObject());
                result["nodes"] = array;
            }
            return result;
        }
    }

    /// <summary>
    /// One control in a settings/cheats catalog. Keyed by kind (toggle/switch/slider/stepper/dropdown/
    /// button/label/rebind) like <see cref="ElementSpec"/>; maps 1:1 to a runtime MenuItemDefinition.
    /// </summary>
    [Serializable]
    public class MenuItemSpec
    {
        public static readonly string[] Kinds =
        {
            "label", "button", "toggle", "switch", "slider", "stepper", "dropdown", "rebind"
        };

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
            object typed = ValueToTyped(kind, value);
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

        /// <summary> The canonical JSON value for a stored string, per kind (so export round-trips). </summary>
        private static object ValueToTyped(string kind, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            switch (kind)
            {
                case "toggle":
                case "switch":
                    return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || value == "1";
                case "slider":
                case "stepper":
                    return double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double d) ? (object)d : null;
                case "dropdown":
                    return int.TryParse(value, out int i) ? (object)(double)i : null;
                default:
                    return null;
            }
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
