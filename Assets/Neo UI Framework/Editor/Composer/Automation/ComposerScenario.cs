using System.Collections.Generic;
using System.IO;

namespace Neo.UI.Editor.Composer.Automation
{
    /// <summary>
    /// One scripted user action in a Composer probe scenario, kept as its raw parsed-JSON object plus
    /// its <see cref="action"/> name. The action name is resolved against <see cref="ComposerProbeActions"/>
    /// (the extension-seam registry) at run time, so a step is just an intent — the driver decides how
    /// to realize it (inject events / call a code path). Typed reads go through the <c>Get*</c> helpers
    /// so a step kind a project registered can read its own fields the same way the built-ins do.
    /// </summary>
    public sealed class ScenarioStep
    {
        public string action;
        public Dictionary<string, object> raw;

        public string GetString(string key, string fallback = null) => JsonReader.GetString(raw, key, fallback);
        public float GetFloat(string key, float fallback = 0f) => JsonReader.GetFloat(raw, key, fallback);
        public int GetInt(string key, int fallback = 0) => (int)JsonReader.GetFloat(raw, key, fallback);
        public bool GetBool(string key, bool fallback = false) => JsonReader.GetBool(raw, key, fallback);
    }

    /// <summary>
    /// A scripted Composer session: which document to open, an optional initial device size, then an
    /// ordered list of <see cref="ScenarioStep"/>s a human would perform (select, drag, resize, nudge,
    /// add a widget, switch breakpoint, rotate the device…). The probe replays it against the real
    /// window and records a filmstrip + telemetry per step.
    ///
    /// <para>Authored as JSON so scenarios are agent-writable and double as regression assets. Parsed
    /// with the same <see cref="MiniJson"/>/<see cref="JsonReader"/> the agent bridge uses, so the
    /// vocabulary stays consistent with the rest of the Neo agent tooling.</para>
    /// </summary>
    public sealed class ComposerScenario
    {
        public string name;

        /// <summary> Spec JSON file to open (relative to project or absolute). When set it wins over
        /// <see cref="open"/>. </summary>
        public string specPath;

        /// <summary> Document origin when no <see cref="specPath"/>: "new" (empty doc) or "project"
        /// (export the live committed UI). Null/empty leaves whatever the window already has. </summary>
        public string open;

        /// <summary> Optional initial device size in device px (0 = leave the default preset). </summary>
        public int width;
        public int height;

        public readonly List<ScenarioStep> steps = new List<ScenarioStep>();

        public static ComposerScenario FromJson(string json) => Parse(JsonReader.AsObject(MiniJson.Parse(json), "scenario"));

        public static ComposerScenario FromFile(string path) => FromJson(File.ReadAllText(path));

        public static ComposerScenario Parse(Dictionary<string, object> obj)
        {
            var scenario = new ComposerScenario
            {
                name = JsonReader.GetString(obj, "name", "scenario"),
                specPath = JsonReader.GetString(obj, "spec") ?? JsonReader.GetString(obj, "specPath"),
                open = JsonReader.GetString(obj, "open"),
                width = (int)JsonReader.GetFloat(obj, "width", 0f),
                height = (int)JsonReader.GetFloat(obj, "height", 0f),
            };

            List<object> steps = JsonReader.GetArray(obj, "steps");
            if (steps != null)
                foreach (object raw in steps)
                {
                    if (!(raw is Dictionary<string, object> stepObj)) continue;
                    string action = JsonReader.GetString(stepObj, "action");
                    if (string.IsNullOrEmpty(action)) continue;
                    scenario.steps.Add(new ScenarioStep { action = action, raw = stepObj });
                }
            return scenario;
        }
    }
}
