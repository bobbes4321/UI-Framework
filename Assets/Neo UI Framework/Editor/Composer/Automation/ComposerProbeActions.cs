using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor.Composer.Automation
{
    /// <summary>
    /// The registry of scenario step kinds — the extension seam for the probe vocabulary, mirroring
    /// <see cref="NeoWidgetPalette"/> / <see cref="ComposerDevicePresets"/> (Pattern R). Built-ins are
    /// seeded through <see cref="Register"/> in the static ctor; a consuming project adds its own step
    /// kind (e.g. to drive a custom inspector field or a project-specific gesture) with one
    /// <see cref="Register"/> call and no package fork. The probe dispatches each
    /// <see cref="ScenarioStep"/> by looking its <see cref="ScenarioStep.action"/> up here.
    ///
    /// <para>A handler receives the live <see cref="ComposerDriver"/> (its window facade + injection
    /// primitives) and the step. Built-ins split into two honest categories, recorded per step:</para>
    /// <list type="bullet">
    /// <item><b>Injected</b> (the "feel" surface we're measuring): canvas select/drag/resize/nudge feed
    /// synthesized input events through the real <c>ComposerCanvas</c> code paths.</item>
    /// <item><b>Driven</b> (setup/navigation): device size, breakpoint scope, undo/redo and palette
    /// add invoke the same code path the corresponding UI control invokes — Unity's drag-and-drop and
    /// per-frame toolbar layout can't be faithfully synthesized, so these are called directly rather
    /// than pretending to be a synthesized gesture.</item>
    /// </list>
    /// </summary>
    public static class ComposerProbeActions
    {
        private static readonly Dictionary<string, Action<ComposerDriver, ScenarioStep>> _handlers =
            new Dictionary<string, Action<ComposerDriver, ScenarioStep>>(StringComparer.Ordinal);

        static ComposerProbeActions()
        {
            // --- injected (real event paths through ComposerCanvas) ---
            Register("select", (d, s) => d.Select(s.GetString("path")));
            Register("drag", (d, s) => d.DragElement(s.GetString("path"), s.GetFloat("dx"), s.GetFloat("dy")));
            Register("resize", (d, s) => d.ResizeElement(s.GetString("path"), s.GetString("handle", "br"),
                s.GetFloat("dx"), s.GetFloat("dy")));
            Register("nudge", (d, s) => d.Nudge(s.GetString("path"), s.GetString("dir", "right"),
                Math.Max(1, s.GetInt("count", 1)), s.GetBool("shift")));

            // --- driven (same code path the control invokes) ---
            Register("addWidget", (d, s) => d.AddWidget(s.GetString("kind"), s.GetString("target")));
            Register("setDevice", (d, s) =>
            {
                string preset = s.GetString("preset");
                if (!string.IsNullOrEmpty(preset)) d.SetDevicePreset(preset);
                else d.SetDeviceSize(s.GetInt("width"), s.GetInt("height"));
            });
            Register("resizeDevice", (d, s) => d.ResizeDevice(s.GetInt("dw"), s.GetInt("dh")));
            Register("setBreakpoint", (d, s) => d.SetBreakpoint(s.GetString("name")));
            Register("undo", (d, s) => d.Undo());
            Register("redo", (d, s) => d.Redo());

            // --- harness control ---
            Register("settle", (d, s) => d.Settle());
            Register("capture", (d, s) => { /* the probe captures after every step; this is just a labelled beat */ });
        }

        /// <summary> Registers (or replaces, by name) a step kind — the extension seam. </summary>
        public static void Register(string action, Action<ComposerDriver, ScenarioStep> handler)
        {
            if (string.IsNullOrEmpty(action) || handler == null) return;
            _handlers[action] = handler;
        }

        public static bool TryGet(string action, out Action<ComposerDriver, ScenarioStep> handler) =>
            _handlers.TryGetValue(action ?? "", out handler);

        public static IReadOnlyCollection<string> Names => _handlers.Keys;

        /// <summary> Runs one step, surfacing an unknown action loudly (no silent failure). </summary>
        public static void Run(ComposerDriver driver, ScenarioStep step)
        {
            if (step == null) return;
            if (TryGet(step.action, out Action<ComposerDriver, ScenarioStep> handler))
                handler(driver, step);
            else
                Debug.LogWarning($"[ComposerProbe] Unknown scenario action '{step.action}'. " +
                                 $"Registered: {string.Join(", ", Names)}");
        }
    }
}
