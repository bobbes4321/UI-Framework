using System;
using System.Collections.Generic;
using System.IO;

namespace Neo.UI.Editor.Composer.Automation
{
    /// <summary>
    /// What one scenario step cost — the per-interaction record an agent reads to spot clunkiness. The
    /// fields are the concrete, measurable face of "feel": how many input events the action took
    /// (economy), how long it blocked (responsiveness), how many repaints + how much rebuild stall it
    /// triggered, how much it allocated (cost), and whether the spec still round-trips and the
    /// selection survived (stability / correctness). Plus the screenshot path for the visual read.
    /// </summary>
    public sealed class StepRecord
    {
        public int index;
        public string action;
        public string note;            // optional human note (e.g. "fell back to API select")
        public int events;            // input events injected to perform the action ("economy")
        public double latencyMs;      // wall-clock for the whole step ("responsiveness")
        public int repaints;          // window repaints during the step
        public double rebuildMs;      // preview-pane Rebuild time during the step (debounce stall)
        public long allocBytes;       // managed bytes allocated during the step ("cost")
        public Dictionary<string, double> paneDrawMs = new Dictionary<string, double>();
        public string selection;      // tree selection path after the step
        public int specHash;          // stable spec content hash (did the doc actually change?)
        public string screenshot;     // relative path to this step's window PNG (null if capture failed)

        public Dictionary<string, object> ToJsonObject()
        {
            var panes = new Dictionary<string, object>();
            foreach (KeyValuePair<string, double> p in paneDrawMs) panes[p.Key] = Math.Round(p.Value, 2);
            return new Dictionary<string, object>
            {
                ["index"] = (double)index,
                ["action"] = action,
                ["note"] = note,
                ["events"] = (double)events,
                ["latencyMs"] = Math.Round(latencyMs, 2),
                ["repaints"] = (double)repaints,
                ["rebuildMs"] = Math.Round(rebuildMs, 2),
                ["allocBytes"] = (double)allocBytes,
                ["paneDrawMs"] = panes,
                ["selection"] = selection,
                ["specHash"] = (double)specHash,
                ["screenshot"] = screenshot,
            };
        }
    }

    /// <summary>
    /// The result of a probe session: the scenario name, the device it ran at, a record per step, the
    /// filmstrip directory, and the session-level invariants (did the final spec round-trip?). Serialized
    /// to <c>session.json</c> next to the PNGs so an agent can read the numbers and look at the frames.
    /// <see cref="Diff"/> compares two sessions of the same scenario so a fix can be proven by
    /// before/after deltas — the closing move of the self-recursive improvement loop.
    /// </summary>
    public sealed class SessionReport
    {
        public string name;
        public int width;
        public int height;
        public string outputDir;
        public bool roundTrips = true;     // the final document spec serializes identically after a parse round-trip
        public readonly List<StepRecord> steps = new List<StepRecord>();

        public Dictionary<string, object> ToJsonObject()
        {
            var stepObjs = new List<object>(steps.Count);
            foreach (StepRecord s in steps) stepObjs.Add(s.ToJsonObject());

            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["width"] = (double)width,
                ["height"] = (double)height,
                ["outputDir"] = outputDir,
                ["roundTrips"] = roundTrips,
                ["stepCount"] = (double)steps.Count,
                ["totalEvents"] = (double)Sum(s => s.events),
                ["totalLatencyMs"] = Math.Round(SumD(s => s.latencyMs), 2),
                ["totalRebuildMs"] = Math.Round(SumD(s => s.rebuildMs), 2),
                ["steps"] = stepObjs,
            };
        }

        public string ToJson() => MiniJson.Serialize(ToJsonObject());

        public string WriteJson(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson());
            return Path.GetFullPath(path);
        }

        private int Sum(Func<StepRecord, int> sel)
        {
            int t = 0;
            foreach (StepRecord s in steps) t += sel(s);
            return t;
        }

        private double SumD(Func<StepRecord, double> sel)
        {
            double t = 0;
            foreach (StepRecord s in steps) t += sel(s);
            return t;
        }

        /// <summary>
        /// Before/after comparison of two runs of the SAME scenario — the per-step deltas that show
        /// whether a Composer change actually improved the feel (fewer events, less latency, fewer
        /// repaints, smaller rebuild stall) or regressed it. Steps are matched by index.
        /// </summary>
        public static Dictionary<string, object> Diff(SessionReport before, SessionReport after)
        {
            var stepDeltas = new List<object>();
            int n = Math.Min(before?.steps.Count ?? 0, after?.steps.Count ?? 0);
            for (int i = 0; i < n; i++)
            {
                StepRecord b = before.steps[i], a = after.steps[i];
                stepDeltas.Add(new Dictionary<string, object>
                {
                    ["index"] = (double)i,
                    ["action"] = a.action,
                    ["dEvents"] = (double)(a.events - b.events),
                    ["dLatencyMs"] = Math.Round(a.latencyMs - b.latencyMs, 2),
                    ["dRepaints"] = (double)(a.repaints - b.repaints),
                    ["dRebuildMs"] = Math.Round(a.rebuildMs - b.rebuildMs, 2),
                    ["dAllocBytes"] = (double)(a.allocBytes - b.allocBytes),
                });
            }
            return new Dictionary<string, object>
            {
                ["name"] = after?.name ?? before?.name,
                ["dTotalEvents"] = (double)((after?.Sum(s => s.events) ?? 0) - (before?.Sum(s => s.events) ?? 0)),
                ["dTotalLatencyMs"] = Math.Round((after?.SumD(s => s.latencyMs) ?? 0) - (before?.SumD(s => s.latencyMs) ?? 0), 2),
                ["dTotalRebuildMs"] = Math.Round((after?.SumD(s => s.rebuildMs) ?? 0) - (before?.SumD(s => s.rebuildMs) ?? 0), 2),
                ["stepCountChanged"] = (before?.steps.Count ?? 0) != (after?.steps.Count ?? 0),
                ["steps"] = stepDeltas,
            };
        }
    }
}
