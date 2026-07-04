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
    /// tokens/styles/bundles, flow triggers, the Lucide icon set). Because it reflects the live
    /// types/databases it can't drift from what the generator actually accepts. Emitted via the
    /// menu, or the Agent Bridge <c>{"action":"specReference"}</c>.
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

        private static Dictionary<string, object> BuildSchema()
        {
            // an element is { "<kind>": { ...fields } } — one property, the kind
            var kindProps = new Dictionary<string, object>();
            foreach (string kind in ElementSpec.Kinds)
                kindProps[kind] = Ref("elementBody");

            var elementBody = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = true,
                ["properties"] = new Dictionary<string, object>
                {
                    ["id"] = Typed("string", "\"Category/Name\""),
                    ["label"] = Typed("string"),
                    ["color"] = Typed("string", "theme token or #hex"),
                    ["labelColor"] = Typed("string", "theme token"),
                    ["background"] = Typed("string", "theme token or #hex"),
                    ["controls"] = Typed("string", "tab: id of the sibling panel it shows/hides"),
                    ["src"] = Typed("string", "image: sprite asset path (\"Assets/...\"); radius rounds the corners"),
                    ["bind"] = Typed("string", "list/grid: UIData source id feeding rows at runtime"),
                    ["item"] = Ref("element"),
                    ["align"] = Lits("left", "center", "right"),
                    ["anchor"] = EnumOf(UIWidgetFactory.AnchorPresetNames),
                    ["shape"] = EnumOf(Enum.GetNames(typeof(ShapeType))),
                    ["variant"] = EnumOf(FactoryConstants("Variant")),
                    ["style"] = EnumOf(FactoryConstants("Style")),
                    ["textStyle"] = EnumOf(FactoryConstants("TextStyle")),
                    ["preset"] = Typed("string", "name of a reusable NeoWidgetPreset; resolved at generate as the base, element fields override"),
                    ["icon"] = EnumOf(IconMap.Names),
                    ["name"] = EnumOf(IconMap.Names),
                    ["radius"] = Typed("number"),
                    ["padding"] = Typed("number"),
                    ["spacing"] = Typed("number"),
                    ["min"] = Typed("number"),
                    ["max"] = Typed("number"),
                    ["value"] = Typed("number"),
                    ["step"] = Typed("number"),
                    ["fontSize"] = Typed("number"),
                    ["badge"] = Typed("number"),
                    ["thickness"] = Typed("number"),
                    ["arcStart"] = Typed("number"),
                    ["arcSweep"] = Typed("number"),
                    ["columns"] = Typed("integer"),
                    ["size"] = new Dictionary<string, object> { ["description"] = "button size variant (sm/md/lg) or [w,h]" },
                    ["position"] = NumberArray(),
                    ["cellSize"] = NumberArray(),
                    ["cascade"] = Typed("boolean"),
                    ["gradient"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["from"] = Typed("string"), ["to"] = Typed("string"), ["angle"] = Typed("number")
                        }
                    },
                    ["onClick"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["signal"] = new Dictionary<string, object> { ["description"] = "\"Cat/Name\" or { category, name }" },
                            ["showView"] = Typed("string"), ["hideView"] = Typed("string"), ["popup"] = Typed("string"),
                            ["close"] = Typed("boolean", "hides the enclosing popup/view")
                        }
                    },
                    ["animations"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["description"] = "per-element animation presets (preset names): hover/press/selected/" +
                                          "disabled drive a selectable animator, loop adds a play-on-start animator",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["hover"] = Typed("string"), ["press"] = Typed("string"),
                            ["selected"] = Typed("string"), ["disabled"] = Typed("string"),
                            ["loop"] = Typed("string")
                        }
                    },
                    ["children"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Ref("element") }
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
                    ["views"] = ArrayOf("view"),
                    ["popups"] = ArrayOf("popup"),
                    ["presets"] = new Dictionary<string, object> { ["type"] = "array" },
                    ["flow"] = new Dictionary<string, object> { ["type"] = "object" }
                },
                ["definitions"] = new Dictionary<string, object>
                {
                    ["view"] = new Dictionary<string, object>
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
                    },
                    ["popup"] = new Dictionary<string, object>
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
                    },
                    ["element"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["minProperties"] = 1d,
                        ["maxProperties"] = 1d,
                        ["additionalProperties"] = false,
                        ["properties"] = kindProps
                    },
                    ["elementBody"] = elementBody
                }
            };
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

        private static Dictionary<string, object> NumberArray() =>
            new Dictionary<string, object> { ["type"] = "array", ["items"] = new Dictionary<string, object> { ["type"] = "number" } };

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
            sb.AppendLine("A spec is JSON with optional top-level sections: `theme`, `presets`, `views`, " +
                          "`popups`, `flow`. Run one through `{\"action\":\"generate\",\"spec\":\"path.json\"}`, " +
                          "round-trip with `{\"action\":\"export\"}`, lint with `{\"action\":\"validate\"}`.");
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
            Section(sb, "Text styles", FactoryConstants("TextStyle"));
            Section(sb, "Theme bundles", ThemeBundles.Names);

            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.theme != null)
            {
                IEnumerable<string> live = settings.theme.GetTokenNames();
                if (live.Any())
                    Section(sb, "Theme color tokens (current project theme)", live.OrderBy(t => t, StringComparer.Ordinal));
            }

            sb.AppendLine("## Popups");
            sb.AppendLine();
            sb.AppendLine("`{ \"name\": ..., \"title\": ..., \"message\": ... }` builds the canonical card " +
                          "(title/message/OK). Add `\"elements\": [...]` (same vocabulary as views, stacked in " +
                          "the card) for custom content, `\"size\": [w,h]` for the card size and `\"close\": true` " +
                          "for an X dismiss button. A button element with `\"onClick\": { \"close\": true }` hides " +
                          "the popup. Open one from any button via `\"onClick\": { \"popup\": \"Name\" }`.");
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

            IEnumerable<string> icons = IconMap.Names.OrderBy(n => n, StringComparer.Ordinal);
            sb.AppendLine($"## Icons ({IconMap.Count} Lucide names)");
            sb.AppendLine();
            sb.AppendLine("Use on `icon` elements (`\"name\"`) or button/tab `\"icon\"` slots.");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", icons.Select(n => $"`{n}`")));
            sb.AppendLine();

            return sb.ToString();
        }

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
            if (type == typeof(GradientSpec)) return "{ from, to, angle }";
            if (type == typeof(SignalRefSpec)) return "{ category, name }";
            if (type == typeof(List<ElementSpec>)) return "element[]";
            return type.Name;
        }
    }
}
