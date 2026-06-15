using System.Collections.Generic;

namespace Neo.UI.Editor.Composer.Automation
{
    /// <summary>
    /// Interaction telemetry for an agent-driven Composer probe session — the numbers that turn
    /// "the Composer feels clunky" into something measurable: per-pane <c>OnGUI</c> cost, how many
    /// repaints a single interaction triggered, and how long the (debounced) preview rebuild took.
    ///
    /// <para><b>Gated, zero-cost when idle.</b> <see cref="Active"/> is false in normal Composer use,
    /// so the window's instrumented draws and the preview's rebuild hook short-circuit before doing
    /// any work — honoring the editor-performance rule (no per-frame allocation / no idle tick cost).
    /// <see cref="ComposerProbe"/> flips it on only for the duration of a session and off again in a
    /// <c>finally</c>.</para>
    ///
    /// <para>The collector is a static singleton because IMGUI draw code is reached through Unity's
    /// repaint loop, not through a reference we could thread a context object into. The probe
    /// <see cref="Reset"/>s it before each step and reads the accumulators after.</para>
    /// </summary>
    internal static class ComposerProbeMetrics
    {
        /// <summary> When false (the default), every collector call is a no-op. </summary>
        public static bool Active;

        /// <summary> Repaints the window performed during the current step (a drag that thrashes the
        /// renderer shows up here). </summary>
        public static int RepaintCount;

        /// <summary> Total time the preview pane spent in <c>Rebuild</c> during the current step (ms) —
        /// the 150 ms debounce stall an interaction can hit. </summary>
        public static double PreviewRebuildMs;

        /// <summary> Accumulated <c>OnGUI</c> time per pane during the current step (ms), keyed by a
        /// short pane name ("preview", "tree", "inspector"). </summary>
        public static readonly Dictionary<string, double> PaneDrawMs = new Dictionary<string, double>();

        /// <summary> Clears the per-step accumulators. Called by the probe before each scenario step. </summary>
        public static void Reset()
        {
            RepaintCount = 0;
            PreviewRebuildMs = 0;
            PaneDrawMs.Clear();
        }

        public static void CountRepaint()
        {
            if (Active) RepaintCount++;
        }

        public static void AddPaneDraw(string pane, double ms)
        {
            if (!Active || string.IsNullOrEmpty(pane)) return;
            PaneDrawMs.TryGetValue(pane, out double v);
            PaneDrawMs[pane] = v + ms;
        }

        public static void AddRebuildMs(double ms)
        {
            if (Active) PreviewRebuildMs += ms;
        }
    }
}