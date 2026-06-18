using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Tier-1 hue-shift descriptor: attaches/reads a <see cref="NeoHueShift"/> that rainbow-cycles the
    /// host NeoShape's fill color hue. Lives in its OWN file (the documented extensibility seam) and
    /// reuses the shared <see cref="ShapeEffectRegistry.ApplyBase"/>/<see cref="ShapeEffectRegistry.WriteBase"/>
    /// timeline round-trip, so the spec pipeline carries no per-effect switch.
    ///
    /// <para>BatchSafe — it only writes the fill <c>color</c>/<c>colorB</c> that ride the shared SDF
    /// material's vertex channels, so the effect never splits the batch.</para>
    /// </summary>
    public sealed class HueShiftDescriptor : IShapeEffectDescriptor
    {
        /// <inheritdoc/>
        public string Id => "hueShift";

        /// <inheritdoc/>
        public bool BatchSafe => true;

        /// <inheritdoc/>
        public void Apply(GameObject host, IDictionary<string, object> parameters)
        {
            var e = host.GetComponent<NeoHueShift>();
            if (e == null) e = host.AddComponent<NeoHueShift>();
            ShapeEffectRegistry.ApplyBase(e, parameters);
            e.HueFrom = ShapeEffectRegistry.GetFloat(parameters, "hueFrom", e.HueFrom);
            e.HueTo = ShapeEffectRegistry.GetFloat(parameters, "hueTo", e.HueTo);
            e.CycleColorB = ShapeEffectRegistry.GetBool(parameters, "cycleColorB", e.CycleColorB);
            e.EvaluateRest();
        }

        /// <inheritdoc/>
        public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
        {
            parameters = null;
            var e = host.GetComponent<NeoHueShift>();
            if (e == null) return false;
            var p = new Dictionary<string, object>();
            ShapeEffectRegistry.WriteBase(e, p);
            p["hueFrom"] = (double)e.HueFrom;
            p["hueTo"] = (double)e.HueTo;
            if (e.CycleColorB) p["cycleColorB"] = true;
            parameters = p;
            return true;
        }
    }
}
