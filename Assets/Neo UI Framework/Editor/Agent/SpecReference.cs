using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Generates an always-accurate, agent-facing spec authoring reference straight from code —
    /// element kinds, the field catalog, and every vocabulary (anchors, shapes, variants, theme
    /// tokens/styles/bundles, flow triggers, the Lucide icon set). The element field list and the
    /// JSON Schema's element properties are both driven by reflecting <see cref="ElementSpec"/>'s
    /// live field set (<see cref="BuildSchema"/>/<see cref="BuildElementFieldOverrides"/>) — a
    /// SMALL override table only supplies JSON-name divergences (a field whose JSON key differs
    /// from its C# name, e.g. <c>labelColor</c> exporting as <c>color</c> on text/icon) and richer
    /// per-field typing (enums, sub-object shapes) than a bare reflected type can express. A new
    /// plain <see cref="ElementSpec"/> field therefore always appears here (generically typed) even
    /// before anyone teaches it a nicer schema — it can no longer silently vanish the way a
    /// hand-curated property list could. Emitted via the menu, or the Agent Bridge
    /// <c>{"action":"specReference"}</c>.
    /// </summary>
    public static class SpecReference
    {
        public const string DefaultPath = "Assets/docs/spec-reference.md";
        public const string DefaultSchemaPath = "Assets/docs/neo-spec.schema.json";

        [MenuItem("Tools/Neo UI/Advanced/Generate Spec Reference", priority = 18)]
        public static void GenerateToDefaultPath()
        {
            string md = Write(DefaultPath);
            string schema = WriteSchema(DefaultSchemaPath);
            Debug.Log($"[Neo.UI] Spec reference → {md}\nJSON schema → {schema}");
            AssetDatabase.Refresh();
        }

        /// <summary> Builds the reference and writes it to <paramref name="path"/>; returns the path. </summary>
        public static string Write(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, Build());
            return path;
        }

        /// <summary>
        /// Writes a JSON Schema (draft-07) for the spec, generated from the same reflected types and
        /// vocabularies as the markdown — point an editor's JSON schema mapping at it for inline
        /// autocomplete + validation of hand-authored specs. Lenient (additionalProperties) so
        /// comment keys like "_recipe" don't error.
        /// </summary>
        public static string WriteSchema(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, MiniJson.Serialize(BuildSchema()));
            return path;
        }

        // ----------------------------------------------------------------------------------- schema

        /// <summary>
        /// Fields whose JSON representation is NOT a plain "same-named property" — either because
        /// they only ever appear nested under another key, or because they share one JSON key with
        /// a sibling field (polymorphic). Handled explicitly in <see cref="BuildSchema"/> instead of
        /// through the generic per-field loop.
        /// </summary>
        private static readonly HashSet<string> NestedOrMergedFields = new HashSet<string>
        {
            "children", "item", // recursive element refs, built explicitly
            "sizeVariant",      // shares the "size" JSON key with "size" ([w,h]) — merged into one property
            "onClickSignal", "onClickShowView", "onClickHideView", "onClickPopup", "onClickClose" // nested under "onClick"
        };

        /// <summary> A field whose JSON key differs from its C# name (both keys are emitted). </summary>
        private static readonly Dictionary<string, string[]> JsonKeyAliases = new Dictionary<string, string[]>
        {
            // widget slot ("icon") vs kind "icon" itself (JSON key "name") — see ElementSpec.Parse/ToJsonObject
            ["icon"] = new[] { "icon", "name" },
            // element label tint ("labelColor") vs text/icon foreground ("color") — same field, two keys
            ["labelColor"] = new[] { "labelColor", "color" }
        };

        private static Dictionary<string, object> BuildSchema()
        {
            var definitions = new Dictionary<string, object>
            {
                ["gradient"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Two-stop gradient; from/to are theme tokens or #hex (rides NeoGradient — tokens stay live).",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["from"] = Typed("string"), ["to"] = Typed("string"), ["angle"] = Typed("number", "degrees, 0 = left-to-right, 90 = bottom-to-top")
                    }
                },
                ["effect"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Open-bag shape effect (ShapeEffectRegistry owns parse/bake/export — a project registers " +
                                       "its own id without forking the spec). params is descriptor-owned and opaque to the core " +
                                       "spec, but every descriptor shares: duration/loop/pingPong/ease/restingPhase (the timeline); " +
                                       "an optional pointer gate trigger (\"hover\"|\"press\"|\"always\") + triggerMode " +
                                       "(\"hold\"|\"playOnce\"); an optional live bindings array " +
                                       "[{ \"signal\":\"Cat/Name\", \"param\":\"...\", \"min\":0, \"max\":1 }] (param \"enabled\" " +
                                       "toggles the whole effect, optional \"invert\"). A Tier-2 \"variant\" additionally takes " +
                                       "\"definition\" (a ShapeEffectDefinition id) and optionally animate/from/to (a material float).",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["id"] = EnumOf(ShapeEffectRegistry.All.Select(d => d.Id)),
                        ["params"] = new Dictionary<string, object> { ["type"] = "object", ["additionalProperties"] = true }
                    }
                },
                ["particleModule"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "One particle module (ParticleEffectRegistry owns parse/export). params is module-owned.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["id"] = EnumOf(ParticleEffectRegistry.All.Select(m => m.Id)),
                        ["params"] = new Dictionary<string, object> { ["type"] = "object", ["additionalProperties"] = true }
                    }
                },
                ["particles"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "UI particle emitter (NeoParticleEmitter, pooled NeoShape instances on the shared material).",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["capacity"] = Typed("integer"),
                        ["burstCount"] = Typed("integer"),
                        ["rate"] = Typed("number", "0 = burst-only; >0 enables continuous emission"),
                        ["emitOnEnable"] = Typed("boolean"),
                        ["particleShape"] = EnumOf(Enum.GetNames(typeof(ShapeType))),
                        ["cornerRadiusPercent"] = Typed("number"),
                        ["sizeRange"] = NumberArray("[min,max]"),
                        ["lifetimeRange"] = NumberArray("[min,max]"),
                        ["speedRange"] = NumberArray("[min,max]"),
                        ["emitAngle"] = Typed("number"),
                        ["emitSpread"] = Typed("number"),
                        ["angularVelocityRange"] = NumberArray("[min,max]"),
                        ["preset"] = Typed("string", "\"Category/Name\" of a NeoParticleEmitterPreset seeding the emitter before inline fields apply"),
                        ["modules"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Ref("particleModule") },
                        ["signal"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["description"] = "adds a NeoParticleBurstOnSignal trigger",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["category"] = Typed("string"), ["name"] = Typed("string"),
                                ["count"] = Typed("integer", "burst count on the signal; <=0 = emitter default")
                            }
                        },
                        ["atPointer"] = Typed("boolean", "burst at the click point on pointer-down (NeoParticlePointerBurst)")
                    }
                },
                ["pointerGlow"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Pointer-follow glow (NeoPointerReactor) — a soft highlight under the cursor while hovered.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["color"] = Typed("string", "hex (#RRGGBB / #RRGGBBAA) or theme token \"Category/Name\""),
                        ["size"] = Typed("number", "follower diameter px"),
                        ["softness"] = Typed("number", "follower edge softness px")
                    }
                },
                ["animations"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Per-element animation presets (preset names): hover/press/selected/disabled drive a " +
                                       "selectable animator (element should be a button/tab/toggle), loop adds a play-on-start animator.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["hover"] = Typed("string"), ["press"] = Typed("string"),
                        ["selected"] = Typed("string"), ["disabled"] = Typed("string"),
                        ["loop"] = Typed("string")
                    }
                },
                ["layoutSpec"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Figma-style per-axis constraint+offset placement; WINS over anchor/position/size/flex " +
                                       "when present. h defaults to \"left\", v defaults to \"top\" when omitted.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["h"] = EnumOf(LayoutConstraints.All.Where(c => c.Axis == LayoutAxis.Horizontal).Select(c => c.Id).Distinct()),
                        ["v"] = EnumOf(LayoutConstraints.All.Where(c => c.Axis == LayoutAxis.Vertical).Select(c => c.Id).Distinct()),
                        ["offset"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["description"] = "Per-constraint offsets keyed BY CONSTRAINT (left/right/top/bottom = edge distance; " +
                                               "h/v = signed center offset; leftRight/topBottom reuse left/right/top/bottom as " +
                                               "[start,end] insets; scale reuses them as [startFraction,endFraction]).",
                            ["additionalProperties"] = Typed("number")
                        },
                        ["size"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["description"] = "Fixed-axis size; ignored on a stretched axis.",
                            ["properties"] = new Dictionary<string, object> { ["w"] = Typed("number"), ["h"] = Typed("number") }
                        },
                        ["sizing"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["description"] = "Per-child sizing mode in a layout-group parent.",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["w"] = EnumOf(LayoutSizingModes.All.Select(m => m.Id)),
                                ["h"] = EnumOf(LayoutSizingModes.All.Select(m => m.Id))
                            }
                        }
                    }
                },
                ["presetChannel"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "from/to are a direction name, a number, \"x,y,z\", a theme token, \"#hex\", or the " +
                                       "keywords \"start\"/\"current\" — meaning depends on the channel (move: direction/vector; " +
                                       "color: token/hex/start/current).",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["from"] = new Dictionary<string, object>(), ["to"] = new Dictionary<string, object>(),
                        ["duration"] = Typed("number"), ["ease"] = Typed("string")
                    }
                },
                ["preset"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["required"] = new List<object> { "name" },
                    ["description"] = "A named, reusable UIAnimation (top-level \"presets\" array) — referenced by a view's " +
                                       "showAnimation/hideAnimation or an element's \"animations\"/\"preset\".motion.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["name"] = Typed("string"),
                        ["type"] = EnumOf(new[] { "Show", "Hide", "Loop", "Button", "State", "Custom" }),
                        ["duration"] = Typed("number"),
                        ["ease"] = Typed("string"),
                        ["move"] = Ref("presetChannel"),
                        ["rotate"] = Ref("presetChannel"),
                        ["scale"] = Ref("presetChannel"),
                        ["fade"] = Ref("presetChannel"),
                        ["color"] = Ref("presetChannel")
                    }
                },
                ["breakpointCondition"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Exactly one kind is normally set. Built-ins shown; a project can register new kinds " +
                                       "via BreakpointConditions without forking the package.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["orientation"] = Lits("portrait", "landscape"),
                        ["minAspect"] = Typed("number", "width/height >="),
                        ["maxAspect"] = Typed("number", "width/height <="),
                        ["minWidth"] = Typed("number", "reference-px width >="),
                        ["maxWidth"] = Typed("number", "reference-px width <=")
                    }
                },
                ["breakpoint"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["required"] = new List<object> { "name" },
                    ["description"] = "One named, ordered responsive breakpoint; first whose \"when\" matches the viewport wins " +
                                       "at runtime. \"name\" is the key an element's \"overrides\" dict uses.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["name"] = Typed("string"),
                        ["when"] = Ref("breakpointCondition")
                    }
                },
                ["flowEdge"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["required"] = new List<object> { "to" },
                    ["description"] = "One outgoing edge ('next' entry) on a flow node.",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["to"] = Typed("string", "target flow node name"),
                        ["allowsBack"] = Typed("boolean", "default true; whether the Back trigger can return along this edge"),
                        ["transition"] = Typed("string", "view transition full name (\"Category/Name\", e.g. \"Push/SlideLeft\") " +
                                                          "played on this navigation cut — resolves via ViewTransitionRegistry / " +
                                                          "ViewTransitionAsset full names; empty/absent = the project default " +
                                                          "(NeoUISettings.defaultViewTransition)"),
                        ["on"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["description"] = "the trigger that fires this edge — see the Flow triggers section"
                        }
                    }
                },
                ["flowNode"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["required"] = new List<object> { "name" },
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["name"] = Typed("string"),
                        ["view"] = Typed("string", "single shown view (\"Category/Name\"); alternative to \"views\""),
                        ["views"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Typed("string") },
                        ["hide"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Typed("string") },
                        ["next"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Ref("flowEdge") }
                    }
                }
            };

            // an element is { "<kind>": { ...fields } } — one property, the kind
            var kindProps = new Dictionary<string, object>();
            foreach (string kind in ElementSpec.Kinds)
                kindProps[kind] = Ref("elementBody");

            var elementProperties = new Dictionary<string, object>();
            Dictionary<string, Dictionary<string, object>> overrides = BuildElementFieldOverrides();
            foreach (FieldInfo field in typeof(ElementSpec)
                         .GetFields(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (field.Name == "kind" || NestedOrMergedFields.Contains(field.Name)) continue;
                Dictionary<string, object> schema = overrides.TryGetValue(field.Name, out Dictionary<string, object> over)
                    ? over
                    : DefaultSchemaForField(field);
                foreach (string jsonKey in JsonKeyAliases.TryGetValue(field.Name, out string[] aliases) ? aliases : new[] { field.Name })
                    elementProperties[jsonKey] = schema;
            }

            // "size" is polymorphic: a button's string variant (sm/md/lg), an icon's scalar (square), or
            // a [w,h] array all share this one JSON key — combining ElementSpec.sizeVariant + .size.
            elementProperties["size"] = new Dictionary<string, object>
            {
                ["description"] = "button: size variant name. icon: scalar number (square) or [w,h]. others: [w,h]. " +
                                   "All share this one JSON key (polymorphic).",
                ["oneOf"] = new List<object> { EnumOf(FactoryConstants("Size")), Typed("number"), NumberArray() }
            };
            elementProperties["item"] = Ref("element");
            elementProperties["children"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Ref("element") };
            // onClickSignal/onClickShowView/onClickHideView/onClickPopup/onClickClose only ever appear
            // nested here (see ElementSpec.Parse/ToJsonObject) — never as top-level keys.
            elementProperties["onClick"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["signal"] = new Dictionary<string, object> { ["description"] = "\"Cat/Name\" or { category, name }" },
                    ["showView"] = Typed("string"), ["hideView"] = Typed("string"), ["popup"] = Typed("string"),
                    ["close"] = Typed("boolean", "hides the enclosing popup/view")
                }
            };

            var elementBody = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = true,
                ["properties"] = elementProperties
            };
            definitions["elementBody"] = elementBody;

            definitions["element"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["minProperties"] = 1d,
                ["maxProperties"] = 1d,
                ["additionalProperties"] = false,
                ["properties"] = kindProps
            };

            definitions["view"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["required"] = new List<object> { "id" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["id"] = Typed("string", "\"Category/Name\""),
                    ["showAnimation"] = Typed("string"),
                    ["hideAnimation"] = Typed("string"),
                    ["background"] = Typed("string", "theme token"),
                    ["elements"] = ArrayOf("element")
                }
            };
            definitions["popup"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["required"] = new List<object> { "name" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["name"] = Typed("string"),
                    ["title"] = Typed("string"),
                    ["message"] = Typed("string"),
                    ["size"] = NumberArray(),
                    ["close"] = Typed("boolean", "X dismiss button on the card corner"),
                    ["elements"] = ArrayOf("element")
                }
            };

            return new Dictionary<string, object>
            {
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["title"] = "Neo UI Spec",
                ["type"] = "object",
                ["additionalProperties"] = true,
                ["properties"] = new Dictionary<string, object>
                {
                    ["theme"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["bundle"] = EnumOf(ThemeBundles.Names),
                            ["tokens"] = new Dictionary<string, object> { ["type"] = "object" },
                            ["variants"] = new Dictionary<string, object> { ["type"] = "object" }
                        }
                    },
                    ["presets"] = ArrayOf("preset"),
                    ["views"] = ArrayOf("view"),
                    ["settings"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["description"] = "Settings menu catalogs — see Editor/Agent/Menus (MenuCatalogSpec/NeoMenuItemKinds)."
                    },
                    ["cheats"] = new Dictionary<string, object>
                    {
                        ["type"] = "array",
                        ["description"] = "Cheat menu catalogs — see Editor/Agent/Menus (MenuCatalogSpec/NeoMenuItemKinds)."
                    },
                    ["popups"] = ArrayOf("popup"),
                    ["breakpoints"] = ArrayOf("breakpoint"),
                    ["flow"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["name"] = Typed("string"),
                            ["start"] = Typed("string", "name of the starting flow node"),
                            ["nodes"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Ref("flowNode") }
                        }
                    }
                },
                ["definitions"] = definitions
            };
        }

        /// <summary>
        /// The JSON-name-divergence + rich-typing overrides for <see cref="ElementSpec"/> fields whose
        /// generic reflected schema (<see cref="DefaultSchemaForField"/>) wouldn't be precise enough
        /// (enums, refs to a sub-schema). Any field NOT listed here still appears in the schema — via
        /// the generic fallback — so a newly added plain field can never silently disappear.
        /// </summary>
        private static Dictionary<string, Dictionary<string, object>> BuildElementFieldOverrides() =>
            new Dictionary<string, Dictionary<string, object>>
            {
                ["id"] = Typed("string", "\"Category/Name\""),
                ["labelColor"] = Typed("string", "theme token (JSON key \"labelColor\"; \"color\" on kind text/icon)"),
                ["background"] = Typed("string", "theme token or #hex"),
                // shape styles for most kinds, PLUS the progress-only literal "radial" (arc dial via
                // ShapeProgressTarget — not a ShapeStyle asset, see UISpecGenerator/UISpecExporter).
                ["style"] = new Dictionary<string, object>
                {
                    ["enum"] = FactoryConstants("Style").Append("radial").OrderBy(v => v, StringComparer.Ordinal).Select(v => (object)v).ToList(),
                    ["description"] = "shape style name for most kinds; \"radial\" is progress-only (arc dial)"
                },
                ["shape"] = EnumOf(Enum.GetNames(typeof(ShapeType))),
                ["gradient"] = Ref("gradient"),
                ["effect"] = Ref("effect"),
                ["particles"] = Ref("particles"),
                ["pointerGlow"] = Ref("pointerGlow"),
                ["animations"] = Ref("animations"),
                // A plain string, not a strict enum: IconMap.Names is itself extensible (a project
                // registers more via NeoUISettings.iconOverlay — the same "drop it in, it's
                // discovered" seam as the other registries), so hard-enumerating today's built-ins
                // here would make the schema reject a perfectly valid project-added icon name.
                ["icon"] = Typed("string", "Lucide icon name (see the Icons section of spec-reference.md); " +
                                            "a project can register more via NeoUISettings.iconOverlay"), // also emitted under "name" via JsonKeyAliases
                ["src"] = Typed("string", "image: sprite asset path (\"Assets/...\"); radius rounds the corners"),
                ["fit"] = new Dictionary<string, object>
                {
                    ["enum"] = new List<object> { "cover" },
                    ["description"] = "image: \"cover\" crops a centered sub-rect to fill the rect (preserves art aspect); absent = stretch"
                },
                ["variant"] = EnumOf(FactoryConstants("Variant")),
                ["preset"] = Typed("string", "name of a reusable NeoWidgetPreset; resolved at generate as the base, element fields override"),
                ["sharedElement"] = Typed("string", "hero-transition match key (NeoSharedElement) — an element with the same " +
                                                     "key on both sides of a navigation cut is matched/morphed by a hero-capable " +
                                                     "ViewTransitionAsset instead of cutting/crossfading normally"),
                ["anchor"] = EnumOf(UIWidgetFactory.AnchorPresetNames),
                ["layout"] = Ref("layoutSpec"),
                ["overrides"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Breakpoint name → delta LayoutSpec; merges OVER the base \"layout\" at runtime.",
                    ["additionalProperties"] = Ref("layoutSpec")
                },
                ["flex"] = Typed("number", "stacks: share of leftover space on the parent's main axis (size becomes the minimum)"),
                ["rotation"] = Typed("number", "z rotation in degrees"),
                ["outlineColor"] = Typed("string", "text: SDF outline color, hex or theme token"),
                ["outlineWidth"] = Typed("number", "text: SDF outline width 0..1 (default 0.25 when only the color is given)"),
                ["padding4"] = new Dictionary<string, object>
                {
                    ["type"] = "array", ["items"] = Typed("number"),
                    ["description"] = "containers: per-side [left, top, right, bottom]; wins over uniform \"padding\""
                },
                ["textStyle"] = EnumOf(FactoryConstants("TextStyle")),
                ["align"] = Lits("left", "center", "right"),
                ["controls"] = Typed("string", "tab: id of the sibling panel it shows/hides"),
                ["group"] = Typed("string", "tab: shared toggle-group name — standalone tabs sharing it get one-on exclusivity"),
                ["catalog"] = Typed("string", "settings/cheats: id of the menu catalog this element presents"),
                ["options"] = new Dictionary<string, object>
                {
                    ["type"] = "array", ["items"] = Typed("string"),
                    ["description"] = "dropdown: option labels in order (value = selected index)"
                },
                ["bind"] = Typed("string", "list/grid: UIData source id feeding rows at runtime"),
                ["signal"] = new Dictionary<string, object>
                {
                    ["description"] = "toggle/slider/dropdown: domain signal (\"Cat/Name\" or { category, name }) the widget " +
                                       "publishes its typed value to, IN ADDITION to its standard \"…/Behaviour\" stream"
                }
            };

        /// <summary>
        /// The generic schema for an <see cref="ElementSpec"/> field with no <see cref="BuildElementFieldOverrides"/>
        /// entry — keeps the schema honest (present, generically typed) for any field a future change
        /// adds before someone teaches it a precise sub-schema.
        /// </summary>
        private static Dictionary<string, object> DefaultSchemaForField(FieldInfo field)
        {
            Type t = field.FieldType;
            if (t == typeof(string)) return Typed("string");
            if (t == typeof(bool)) return Typed("boolean");
            if (t == typeof(float) || t == typeof(float?)) return Typed("number");
            if (t == typeof(int) || t == typeof(int?)) return Typed("integer");
            if (t == typeof(float[])) return NumberArray();
            if (t == typeof(List<string>)) return new Dictionary<string, object> { ["type"] = "array", ["items"] = Typed("string") };
            return new Dictionary<string, object> { ["description"] = $"({FriendlyType(t)}) — no schema override yet; see ElementSpec.{field.Name}" };
        }

        private static Dictionary<string, object> Typed(string type, string description = null)
        {
            var d = new Dictionary<string, object> { ["type"] = type };
            if (!string.IsNullOrEmpty(description)) d["description"] = description;
            return d;
        }

        private static Dictionary<string, object> Lits(params string[] values) => EnumOf(values);

        private static Dictionary<string, object> EnumOf(IEnumerable<string> values) =>
            new Dictionary<string, object> { ["enum"] = values.Select(v => (object)v).ToList() };

        private static Dictionary<string, object> NumberArray(string description = null)
        {
            var d = new Dictionary<string, object> { ["type"] = "array", ["items"] = new Dictionary<string, object> { ["type"] = "number" } };
            if (!string.IsNullOrEmpty(description)) d["description"] = description;
            return d;
        }

        private static Dictionary<string, object> ArrayOf(string defName) =>
            new Dictionary<string, object> { ["type"] = "array", ["items"] = Ref(defName) };

        private static Dictionary<string, object> Ref(string defName) =>
            new Dictionary<string, object> { ["$ref"] = $"#/definitions/{defName}" };

        /// <summary> The reference as markdown (no I/O). </summary>
        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Neo UI — Spec Reference");
            sb.AppendLine();
            sb.AppendLine("> Generated from code by `Tools → Neo UI → Generate Spec Reference` " +
                          "(or bridge `{\"action\":\"specReference\"}`). Do not edit by hand — regenerate.");
            sb.AppendLine();
            sb.AppendLine("A spec is JSON with optional top-level sections: `theme`, `presets`, `views`, `popups`, " +
                          "`settings`, `cheats`, `breakpoints`, `flow`. Run one through " +
                          "`{\"action\":\"generate\",\"spec\":\"path.json\"}`, round-trip with `{\"action\":\"export\"}`, " +
                          "lint with `{\"action\":\"validate\"}`.");
            sb.AppendLine();

            Section(sb, "Element kinds", ElementSpec.Kinds.OrderBy(k => k, StringComparer.Ordinal));
            sb.AppendLine("`scroll` is a forgiving alias for `list` — accepted on parse, but always " +
                          "normalized to `list` immediately, so an authored `scroll` element always " +
                          "exports back as `list` (byte-stable; there is no separate `scroll` kind to " +
                          "round-trip to).");
            sb.AppendLine();

            sb.AppendLine("## Element fields");
            sb.AppendLine();
            sb.AppendLine("Per-element JSON keys (an element is `{ \"<kind>\": { ...fields } }`). " +
                          "Fields apply only to the kinds that use them.");
            sb.AppendLine();
            sb.AppendLine("| Field | Type |");
            sb.AppendLine("|---|---|");
            foreach (FieldInfo field in typeof(ElementSpec)
                         .GetFields(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (field.Name == "kind") continue; // the wrapping key, not a body field
                sb.AppendLine($"| `{field.Name}` | {FriendlyType(field.FieldType)} |");
            }
            sb.AppendLine();

            Section(sb, "Anchor presets", UIWidgetFactory.AnchorPresetNames.OrderBy(a => a, StringComparer.Ordinal));
            Section(sb, "Shapes (kind `shape`)", Enum.GetNames(typeof(ShapeType)));
            Section(sb, "Button variants", FactoryConstants("Variant"));
            Section(sb, "Button sizes", FactoryConstants("Size"));
            Section(sb, "Theme color tokens (factory)", FactoryConstants("Token"));
            Section(sb, "Shape styles", FactoryConstants("Style"));
            sb.AppendLine("`progress` also accepts `\"style\": \"radial\"` — not a `ShapeStyle` asset, it switches the " +
                          "widget to an arc dial (`ShapeProgressTarget`).");
            sb.AppendLine();
            Section(sb, "Text styles", FactoryConstants("TextStyle"));
            Section(sb, "Theme bundles", ThemeBundles.Names);

            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.theme != null)
            {
                IEnumerable<string> live = settings.theme.GetTokenNames();
                if (live.Any())
                    Section(sb, "Theme color tokens (current project theme)", live.OrderBy(t => t, StringComparer.Ordinal));
            }

            sb.AppendLine("## Responsive layout");
            sb.AppendLine();
            sb.AppendLine("Top-level `breakpoints` is an ordered list of `{ \"name\", \"when\": { ... } }` — the FIRST " +
                          "whose condition matches the viewport wins at runtime. `when` sets exactly one of " +
                          $"{string.Join(", ", ConditionKinds().Select(k => $"`{k}`"))} (a project can register more via " +
                          "`BreakpointConditions`). An element's per-breakpoint `overrides` dict keys by breakpoint `name` " +
                          "and cascades a delta `layout` object over the element's base `layout`.");
            sb.AppendLine();
            sb.AppendLine("An element's `layout` object takes `h` " +
                          $"({string.Join("/", LayoutConstraints.All.Where(c => c.Axis == LayoutAxis.Horizontal).Select(c => c.Id).Distinct())}), " +
                          "`v` " +
                          $"({string.Join("/", LayoutConstraints.All.Where(c => c.Axis == LayoutAxis.Vertical).Select(c => c.Id).Distinct())}), " +
                          "`offset` (per-constraint, keyed by constraint name), `size` (`{w,h}`, ignored on a stretched axis) " +
                          "and `sizing` (`{w,h}` each " +
                          $"{string.Join("/", LayoutSizingModes.All.Select(m => m.Id))} in a layout-group parent). `padding4` " +
                          "`[l,t,r,b]` wins over uniform `padding` on a container.");
            sb.AppendLine();

            sb.AppendLine("## Shape effects & particles");
            sb.AppendLine();
            sb.AppendLine("An element's `effect` object: `{ \"id\", \"params\": { ... } }`. Built-in ids: " +
                          $"{string.Join(", ", ShapeEffectRegistry.All.Select(d => $"`{d.Id}`"))} (ShapeEffectRegistry — a project " +
                          "registers its own). Every descriptor's `params` shares the timeline keys `duration`/`loop`/`pingPong`/" +
                          "`ease`/`restingPhase`; an optional pointer gate `trigger` (`hover`/`press`/`always`) + `triggerMode` " +
                          "(`hold`/`playOnce`); and an optional live `bindings` array " +
                          "`[{ \"signal\":\"Cat/Name\", \"param\":..., \"min\":..., \"max\":... }]` (param `\"enabled\"` toggles " +
                          "the whole effect). The Tier-2 `variant` id additionally takes `definition` (a ShapeEffectDefinition " +
                          "id) and optionally `animate`/`from`/`to` (a material float driven over the same timeline).");
            sb.AppendLine();
            sb.AppendLine("An element's `particles` object is a UI particle emitter (scalars + an open `modules` array — " +
                          $"built-in module ids: {string.Join(", ", ParticleEffectRegistry.All.Select(m => $"`{m.Id}`"))}). " +
                          "`atPointer: true` bursts at the click point; an optional `signal` adds a burst-on-signal trigger.");
            sb.AppendLine();
            sb.AppendLine("An element's `pointerGlow` object (`{ \"color\", \"size\", \"softness\" }`) follows the cursor while " +
                          "the element is hovered.");
            sb.AppendLine();

            sb.AppendLine("## Popups");
            sb.AppendLine();
            sb.AppendLine("`{ \"name\": ..., \"title\": ..., \"message\": ... }` builds the canonical card " +
                          "(title/message/OK). Add `\"elements\": [...]` (same vocabulary as views, stacked in " +
                          "the card) for custom content, `\"size\": [w,h]` for the card size and `\"close\": true` " +
                          "for an X dismiss button. A button element with `\"onClick\": { \"close\": true }` hides " +
                          "the popup. Open one from any button via `\"onClick\": { \"popup\": \"Name\" }`.");
            sb.AppendLine();

            sb.AppendLine("## Flow edges & transitions");
            sb.AppendLine();
            sb.AppendLine("A flow node's `next` array holds edges: `{ \"to\": \"NodeName\", \"allowsBack\": true, " +
                          "\"transition\": \"Category/Name\", \"on\": { ... } }`. `to` and `on` are covered below; " +
                          "`allowsBack` (default `true`) controls whether the Back trigger can return along this edge. " +
                          "`transition` names a `ViewTransitionAsset` by its full name (e.g. `\"Push/SlideLeft\"`) choreographing " +
                          "this navigation cut — resolved through `ViewTransitionRegistry`/`ViewTransitionAsset` full names; " +
                          "empty/absent falls back to the project default (`NeoUISettings.defaultViewTransition`). Omitted " +
                          "entirely when not set, so existing flows round-trip unchanged.");
            sb.AppendLine();
            sb.AppendLine("An element's `sharedElement` field (a plain string key) marks it for hero/shared-element matching: " +
                          "when a transition supports shared elements and an element with the SAME key exists in both the " +
                          "outgoing and incoming view, it flies its own frame across the cut instead of being cut/crossfaded " +
                          "with the rest of its view (backed by the `NeoSharedElement` component).");
            sb.AppendLine();

            sb.AppendLine("## Flow triggers (`on` in a flow edge)");
            sb.AppendLine();
            sb.AppendLine("| Trigger | Form |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Button click | `{ \"button\": \"Cat/Name\" }` |");
            sb.AppendLine("| Signal | `{ \"signal\": \"Cat/Name\" }` or `{ \"signal\": { \"category\":..., \"name\":... } }` |");
            sb.AppendLine("| Toggle on/off | `{ \"toggleOn\": \"Cat/Name\" }` / `{ \"toggleOff\": \"Cat/Name\" }` |");
            sb.AppendLine("| View shown/hidden | `{ \"viewShown\": \"Cat/Name\" }` / `{ \"viewHidden\": \"Cat/Name\" }` |");
            sb.AppendLine("| Back button | `{ \"back\": true }` |");
            sb.AppendLine("| Timer | `{ \"timer\": seconds }` |");
            sb.AppendLine();

            IEnumerable<string> icons = IconMap.FeaturedNames.OrderBy(n => n, StringComparer.Ordinal);
            sb.AppendLine($"## Icons ({IconMap.Count} featured Lucide names)");
            sb.AppendLine();
            sb.AppendLine("Use on `icon` elements (`\"name\"`) or button/tab `\"icon\"` slots. Beyond this " +
                          "featured list, EVERY Lucide 1.17.0 name (~1960) resolves — use any name from " +
                          "lucide.dev — plus project-defined `IconMapOverlay` entries (custom glyphs and " +
                          "sprite-backed PNG icons).");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", icons.Select(n => $"`{n}`")));
            sb.AppendLine();

            return sb.ToString();
        }

        private static IEnumerable<string> ConditionKinds() =>
            BreakpointConditions.All.Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal);

        private static void Section(StringBuilder sb, string title, IEnumerable<string> items)
        {
            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", items.Select(i => $"`{i}`")));
            sb.AppendLine();
        }

        private static IEnumerable<string> FactoryConstants(string namePrefix) =>
            typeof(UIWidgetFactory)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.StartsWith(namePrefix, StringComparison.Ordinal))
                .Select(f => (string)f.GetRawConstantValue())
                .OrderBy(v => v, StringComparer.Ordinal);

        private static string FriendlyType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(float?) || type == typeof(float)) return "number";
            if (type == typeof(int?) || type == typeof(int)) return "int";
            if (type == typeof(float[])) return "number[]";
            if (type == typeof(List<string>)) return "string[]";
            if (type == typeof(GradientSpec)) return "{ from, to, angle }";
            if (type == typeof(SignalRefSpec)) return "{ category, name }";
            if (type == typeof(EffectSpec)) return "{ id, params }";
            if (type == typeof(ParticleSpec)) return "{ ...scalars, modules[], signal, atPointer }";
            if (type == typeof(PointerGlowSpec)) return "{ color, size, softness }";
            if (type == typeof(ElementAnimationsSpec)) return "{ hover, press, selected, disabled, loop }";
            if (type == typeof(LayoutSpec)) return "{ h, v, offset, size, sizing }";
            if (type == typeof(Dictionary<string, LayoutSpec>)) return "{ [breakpointName]: layout delta }";
            if (type == typeof(ElementSpec)) return "element";
            if (type == typeof(List<ElementSpec>)) return "element[]";
            return type.Name;
        }
    }
}
