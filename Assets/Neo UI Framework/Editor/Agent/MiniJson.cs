using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Small dependency-free JSON parser/writer for the UI spec format.
    /// Parses into Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null;
    /// writes the same shapes back with stable, human-diffable indentation.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new FormatException("Empty JSON input");
            int index = 0;
            object value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index < json.Length) throw new FormatException($"Unexpected trailing content at position {index}");
            return value;
        }

        public static string Serialize(object value, bool pretty = true)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, pretty, 0);
            return builder.ToString();
        }

        // ------------------------------------------------------------------ parsing

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) throw new FormatException("Unexpected end of JSON");

            char c = json[index];
            switch (c)
            {
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case '"': return ParseString(json, ref index);
                case 't': Expect(json, ref index, "true"); return true;
                case 'f': Expect(json, ref index, "false"); return false;
                case 'n': Expect(json, ref index, "null"); return null;
                default: return ParseNumber(json, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var result = new Dictionary<string, object>();
            index++; // {
            SkipWhitespace(json, ref index);
            if (Peek(json, index) == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (Peek(json, index) != '"') throw new FormatException($"Expected object key at position {index}");
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (Peek(json, index) != ':') throw new FormatException($"Expected ':' at position {index}");
                index++;
                result[key] = ParseValue(json, ref index);
                SkipWhitespace(json, ref index);
                char next = Peek(json, index);
                index++;
                if (next == '}') return result;
                if (next != ',') throw new FormatException($"Expected ',' or '}}' at position {index - 1}");
            }
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var result = new List<object>();
            index++; // [
            SkipWhitespace(json, ref index);
            if (Peek(json, index) == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);
                char next = Peek(json, index);
                index++;
                if (next == ']') return result;
                if (next != ',') throw new FormatException($"Expected ',' or ']' at position {index - 1}");
            }
        }

        private static string ParseString(string json, ref int index)
        {
            var builder = new StringBuilder();
            index++; // opening quote
            while (true)
            {
                if (index >= json.Length) throw new FormatException("Unterminated string");
                char c = json[index++];
                if (c == '"') return builder.ToString();
                if (c == '\\')
                {
                    if (index >= json.Length) throw new FormatException("Unterminated escape sequence");
                    char escape = json[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > json.Length) throw new FormatException("Invalid \\u escape");
                            builder.Append((char)Convert.ToInt32(json.Substring(index, 4), 16));
                            index += 4;
                            break;
                        default: throw new FormatException($"Invalid escape '\\{escape}'");
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }
        }

        private static double ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && "+-0123456789.eE".IndexOf(json[index]) >= 0) index++;
            string token = json.Substring(start, index - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"Invalid number '{token}' at position {start}");
            return value;
        }

        private static void Expect(string json, ref int index, string literal)
        {
            if (index + literal.Length > json.Length || json.Substring(index, literal.Length) != literal)
                throw new FormatException($"Invalid literal at position {index}");
            index += literal.Length;
        }

        private static char Peek(string json, int index)
        {
            if (index >= json.Length) throw new FormatException("Unexpected end of JSON");
            return json[index];
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }

        // ------------------------------------------------------------------ writing

        private static void WriteValue(StringBuilder builder, object value, bool pretty, int depth)
        {
            switch (value)
            {
                case null: builder.Append("null"); break;
                case bool b: builder.Append(b ? "true" : "false"); break;
                case string s: WriteString(builder, s); break;
                case float f: builder.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: builder.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case int i: builder.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: builder.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary dictionary: WriteObject(builder, dictionary, pretty, depth); break;
                case IEnumerable enumerable: WriteArray(builder, enumerable, pretty, depth); break;
                default: WriteString(builder, value.ToString()); break;
            }
        }

        private static void WriteObject(StringBuilder builder, IDictionary dictionary, bool pretty, int depth)
        {
            if (dictionary.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first) builder.Append(',');
                first = false;
                NewLine(builder, pretty, depth + 1);
                WriteString(builder, entry.Key.ToString());
                builder.Append(pretty ? ": " : ":");
                WriteValue(builder, entry.Value, pretty, depth + 1);
            }
            NewLine(builder, pretty, depth);
            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IEnumerable enumerable, bool pretty, int depth)
        {
            var items = new List<object>();
            foreach (object item in enumerable) items.Add(item);
            if (items.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) builder.Append(',');
                NewLine(builder, pretty, depth + 1);
                WriteValue(builder, items[i], pretty, depth + 1);
            }
            NewLine(builder, pretty, depth);
            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }

        private static void NewLine(StringBuilder builder, bool pretty, int depth)
        {
            if (!pretty) return;
            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }
    }

    /// <summary> Helpers for reading parsed JSON dictionaries with friendly errors. </summary>
    public static class JsonReader
    {
        public static Dictionary<string, object> AsObject(object value, string context)
        {
            if (value is Dictionary<string, object> dict) return dict;
            throw new FormatException($"Expected an object for '{context}' but got {Describe(value)}");
        }

        public static List<object> AsArray(object value, string context)
        {
            if (value is List<object> list) return list;
            throw new FormatException($"Expected an array for '{context}' but got {Describe(value)}");
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null) =>
            obj.TryGetValue(key, out object value) && value != null ? value.ToString() : fallback;

        public static float GetFloat(Dictionary<string, object> obj, string key, float fallback = 0f) =>
            obj.TryGetValue(key, out object value) && value is double d ? (float)d : fallback;

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false) =>
            obj.TryGetValue(key, out object value) && value is bool b ? b : fallback;

        public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out object value) && value is Dictionary<string, object> dict ? dict : null;

        public static List<object> GetArray(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out object value) && value is List<object> list ? list : null;

        private static string Describe(object value) => value == null ? "null" : value.GetType().Name;
    }
}
