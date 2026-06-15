using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer.Automation
{
    /// <summary> How a probe session is framed: the window rect (fixed for deterministic capture) and
    /// where the filmstrip + <c>session.json</c> are written. </summary>
    public sealed class ProbeOptions
    {
        public int x = 80, y = 80;
        public int width = 1100, height = 720;
        public string outputDir = "Temp/neo-composer-session";
    }

    /// <summary>
    /// Runs a <see cref="ComposerScenario"/> against the live <see cref="NeoComposerWindow"/> and
    /// produces a <see cref="SessionReport"/> — a filmstrip (one window PNG per step) plus per-step
    /// interaction telemetry. This is the heart of the self-recursive loop: an agent runs a scenario,
    /// reads the frames + numbers to find what's clunky, edits the Composer, re-runs the SAME scenario,
    /// and compares with <see cref="SessionReport.Diff"/>.
    ///
    /// <para>The session never saves: it only mutates the in-memory document (the preview builds and
    /// destroys throwaway objects, exactly like normal authoring), so it can't touch committed assets.
    /// Metrics collection is switched on only for the duration of the run.</para>
    /// </summary>
    public static class ComposerProbe
    {
        public static SessionReport RunSession(ComposerScenario scenario, ProbeOptions options)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            options ??= new ProbeOptions();

            var window = EditorWindow.GetWindow<NeoComposerWindow>();
            window.position = new Rect(options.x, options.y, options.width, options.height);
            window.Focus();

            OpenDocument(window, scenario);

            var driver = new ComposerDriver(window);
            var report = new SessionReport
            {
                name = scenario.name,
                outputDir = Path.GetFullPath(options.outputDir),
            };
            string filmstrip = Path.Combine(options.outputDir, "filmstrip");

            ComposerProbeMetrics.Active = true;
            try
            {
                if (scenario.width > 0 && scenario.height > 0) driver.SetDeviceSize(scenario.width, scenario.height);
                else driver.Settle();
                WindowCapture.CaptureToPng(window, Path.Combine(filmstrip, "00-initial.png"));

                for (int i = 0; i < scenario.steps.Count; i++)
                {
                    ScenarioStep step = scenario.steps[i];
                    ComposerProbeMetrics.Reset();
                    long allocBefore = GC.GetTotalMemory(false);
                    int eventsBefore = driver.EventCount;

                    var sw = Stopwatch.StartNew();
                    ComposerProbeActions.Run(driver, step);
                    sw.Stop();

                    string label = $"{step.action}";
                    string shotName = $"{i + 1:D2}-{Sanitize(step.action)}.png";
                    string shot = WindowCapture.CaptureToPng(window, Path.Combine(filmstrip, shotName));

                    report.steps.Add(new StepRecord
                    {
                        index = i,
                        action = label,
                        note = driver.LastNote,
                        events = driver.EventCount - eventsBefore,
                        latencyMs = sw.Elapsed.TotalMilliseconds,
                        repaints = ComposerProbeMetrics.RepaintCount,
                        rebuildMs = ComposerProbeMetrics.PreviewRebuildMs,
                        allocBytes = Math.Max(0, GC.GetTotalMemory(false) - allocBefore),
                        paneDrawMs = new System.Collections.Generic.Dictionary<string, double>(ComposerProbeMetrics.PaneDrawMs),
                        selection = window.SelectedPath,
                        specHash = StableHash(window.Document.Spec),
                        screenshot = shot != null ? Path.Combine(filmstrip, shotName).Replace('\\', '/') : null,
                    });
                }
            }
            finally
            {
                ComposerProbeMetrics.Active = false;
            }

            (int w, int h) = window.Preview.DeviceSizePx;
            report.width = w;
            report.height = h;
            report.roundTrips = RoundTrips(window.Document.Spec);
            return report;
        }

        private static void OpenDocument(NeoComposerWindow window, ComposerScenario scenario)
        {
            SpecDocument doc = window.Document;
            if (!string.IsNullOrEmpty(scenario.specPath))
                doc.LoadFromFile(scenario.specPath);
            else if (scenario.open == "project")
                doc.LoadCurrentProject();
            else if (scenario.open == "new")
                doc.Load(SpecDocument.NewEmptySpec(), null);
            // else: drive whatever document the window already holds
        }

        // Stable content hash so a step's "did the doc change?" survives across processes (string
        // GetHashCode is randomized per-run, which would make cross-session Diff meaningless).
        private static int StableHash(UISpec spec)
        {
            string s = spec.ToJson();
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in s) h = (h ^ c) * 16777619;
                return h;
            }
        }

        private static bool RoundTrips(UISpec spec)
        {
            try
            {
                string a = spec.ToJson();
                string b = UISpec.FromJson(a).ToJson();
                return string.Equals(a, b, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "step";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            return s;
        }
    }
}
