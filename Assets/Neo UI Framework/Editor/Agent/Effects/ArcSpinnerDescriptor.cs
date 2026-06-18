using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Tier-1 arc spinner descriptor: attaches an <see cref="NeoArcSpinner"/> from a flat param bag
    /// and reads it back deterministically. Lives in its own file (the extensibility seam) and reuses
    /// the shared <see cref="ShapeEffectRegistry"/> helpers (<c>GetFloat</c>/<c>GetBool</c>/
    /// <c>ApplyBase</c>/<c>WriteBase</c>) so its round-trip matches the built-ins exactly. Register it
    /// in <see cref="ShapeEffectRegistry"/> — no core edits beyond the one Register line.
    /// </summary>
    public sealed class ArcSpinnerDescriptor : IShapeEffectDescriptor
    {
        public string Id => "arcSpinner";
        public bool BatchSafe => true;

        public void Apply(GameObject host, IDictionary<string, object> parameters)
        {
            var e = host.GetComponent<NeoArcSpinner>();
            if (e == null) e = host.AddComponent<NeoArcSpinner>();
            ShapeEffectRegistry.ApplyBase(e, parameters);
            e.SpinFrom = ShapeEffectRegistry.GetFloat(parameters, "spinFrom", e.SpinFrom);
            e.SpinTo = ShapeEffectRegistry.GetFloat(parameters, "spinTo", e.SpinTo);
            e.AnimateSweep = ShapeEffectRegistry.GetBool(parameters, "animateSweep", e.AnimateSweep);
            e.SweepFrom = ShapeEffectRegistry.GetFloat(parameters, "sweepFrom", e.SweepFrom);
            e.SweepTo = ShapeEffectRegistry.GetFloat(parameters, "sweepTo", e.SweepTo);
            e.AnimateThickness = ShapeEffectRegistry.GetBool(parameters, "animateThickness", e.AnimateThickness);
            e.ThicknessMin = ShapeEffectRegistry.GetFloat(parameters, "thicknessMin", e.ThicknessMin);
            e.ThicknessMax = ShapeEffectRegistry.GetFloat(parameters, "thicknessMax", e.ThicknessMax);
            e.EvaluateRest();
        }

        public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
        {
            parameters = null;
            var e = host.GetComponent<NeoArcSpinner>();
            if (e == null) return false;
            var p = new Dictionary<string, object>();
            ShapeEffectRegistry.WriteBase(e, p);
            p["spinFrom"] = (double)e.SpinFrom;
            p["spinTo"] = (double)e.SpinTo;
            if (e.AnimateSweep) p["animateSweep"] = true;
            p["sweepFrom"] = (double)e.SweepFrom;
            p["sweepTo"] = (double)e.SweepTo;
            if (e.AnimateThickness) p["animateThickness"] = true;
            p["thicknessMin"] = (double)e.ThicknessMin;
            p["thicknessMax"] = (double)e.ThicknessMax;
            parameters = p;
            return true;
        }
    }
}
