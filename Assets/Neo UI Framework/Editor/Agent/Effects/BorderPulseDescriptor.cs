using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Tier-1 border pulse descriptor: attaches a <see cref="NeoBorderPulse"/> from a flat param bag
    /// and reads it back deterministically. It carries <c>borderMin</c>/<c>borderMax</c> (scalars, the
    /// pulsing outline width), an optional <c>pulseAlpha</c> bool with <c>alphaMin</c>/<c>alphaMax</c>
    /// (the ring's fade), and an OPTIONAL <c>outlineColor</c> — a theme-token-or-hex color ref (the same
    /// color-ref model as <see cref="ParticleEffectRegistry.ParseColorRef"/> /
    /// <c>GradientCycleDescriptor</c>). The color ref is emitted on export ONLY when it was explicitly
    /// supplied, so a bare borderPulse (just breathing the already-baked outline) round-trips minimally.
    ///
    /// <para>BatchSafe — it only animates shape params that ride the shared SDF material's vertex
    /// channels (border width + outline color). Lives in its own file (the per-descriptor seam a
    /// consuming project mirrors) and is registered as a built-in from
    /// <c>ShapeEffectRegistry.RegisterBuiltins</c>.</para>
    /// </summary>
    public sealed class BorderPulseDescriptor : IShapeEffectDescriptor
    {
        /// <summary> Stable, agent-addressable effect id. </summary>
        public string Id => BorderPulse;

        /// <summary> Built-in Tier-1 focus/selection-ring effect id. </summary>
        public const string BorderPulse = "borderPulse";

        /// <summary> Tier-1: writes only vertex-channel shape params, so the batch is preserved. </summary>
        public bool BatchSafe => true;

        /// <inheritdoc/>
        public void Apply(GameObject host, IDictionary<string, object> parameters)
        {
            var e = host.GetComponent<NeoBorderPulse>();
            if (e == null) e = host.AddComponent<NeoBorderPulse>();
            ShapeEffectRegistry.ApplyBase(e, parameters);

            e.BorderMin = ShapeEffectRegistry.GetFloat(parameters, "borderMin", e.BorderMin);
            e.BorderMax = ShapeEffectRegistry.GetFloat(parameters, "borderMax", e.BorderMax);
            e.PulseAlpha = ShapeEffectRegistry.GetBool(parameters, "pulseAlpha", e.PulseAlpha);
            e.AlphaMin = ShapeEffectRegistry.GetFloat(parameters, "alphaMin", e.AlphaMin);
            e.AlphaMax = ShapeEffectRegistry.GetFloat(parameters, "alphaMax", e.AlphaMax);

            // Optional theme-token-or-hex outline color (same model as colorOverLife / gradientCycle).
            // Omitted → keep the host's existing baked outline color RGB (only the alpha pulses), so a
            // bare borderPulse just breathes the already-baked ring. Reuses the shared ParseColorRef.
            string outlineColor = ShapeEffectRegistry.GetString(parameters, "outlineColor", null);
            if (!string.IsNullOrEmpty(outlineColor))
                e.OutlineColorRef = ParticleEffectRegistry.ParseColorRef(outlineColor);

            e.EvaluateRest();
        }

        /// <inheritdoc/>
        public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
        {
            parameters = null;
            var e = host.GetComponent<NeoBorderPulse>();
            if (e == null) return false;

            var p = new Dictionary<string, object>();
            ShapeEffectRegistry.WriteBase(e, p);

            // Deterministic key order: timeline (WriteBase) → border → optional alpha → optional color.
            p["borderMin"] = (double)e.BorderMin;
            p["borderMax"] = (double)e.BorderMax;
            if (e.PulseAlpha) p["pulseAlpha"] = true;
            p["alphaMin"] = (double)e.AlphaMin;
            p["alphaMax"] = (double)e.AlphaMax;

            // Emit the outline color ONLY when one was explicitly supplied (token OR hex), so the default
            // case (pulse the baked outline) round-trips to exactly the bare params it generated from.
            if (e.HasOutlineColor)
                p["outlineColor"] = ParticleEffectRegistry.ColorRefToString(e.OutlineColorRef);

            parameters = p;
            return true;
        }
    }
}
