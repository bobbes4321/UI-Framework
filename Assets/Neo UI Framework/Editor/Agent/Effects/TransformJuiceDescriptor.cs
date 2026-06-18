using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Tier-1 transform-juice descriptor: attaches a <see cref="NeoTransformJuice"/> from a flat param
    /// bag and reads it back deterministically. It carries up to five amplitude scalars — <c>bob</c>
    /// (vertical px), <c>sway</c> (horizontal px), <c>rotate</c> (Z degrees), <c>scale</c> (uniform pulse
    /// fraction) and <c>squash</c> (non-uniform jelly fraction) — layered over the shared
    /// <see cref="NeoShapeEffect"/> timeline (duration/loop/pingPong/restingPhase/ease, via
    /// <see cref="ShapeEffectRegistry.ApplyBase"/> / <see cref="ShapeEffectRegistry.WriteBase"/>).
    ///
    /// <para>BatchSafe — it drives only the host's <see cref="RectTransform"/> (position / Z rotation /
    /// scale) and never touches the material or mesh, so the shared NeoShape batch is preserved. On
    /// export each amplitude is written ONLY when non-zero (fixed deterministic order), so a bob-only
    /// effect round-trips to a minimal <c>{ …timeline…, "bob": N }</c> bag.</para>
    /// </summary>
    public sealed class TransformJuiceDescriptor : IShapeEffectDescriptor
    {
        /// <summary> Built-in Tier-1 idle/attention RectTransform-motion effect id. </summary>
        public const string TransformJuice = "transformJuice";

        /// <summary> Stable, agent-addressable effect id. </summary>
        public string Id => TransformJuice;

        /// <summary> Tier-1: drives only the RectTransform — no material touched — so the batch is preserved. </summary>
        public bool BatchSafe => true;

        /// <inheritdoc/>
        public void Apply(GameObject host, IDictionary<string, object> parameters)
        {
            var e = host.GetComponent<NeoTransformJuice>();
            if (e == null) e = host.AddComponent<NeoTransformJuice>();
            ShapeEffectRegistry.ApplyBase(e, parameters);

            e.Bob = ShapeEffectRegistry.GetFloat(parameters, "bob", e.Bob);
            e.Sway = ShapeEffectRegistry.GetFloat(parameters, "sway", e.Sway);
            e.Rotate = ShapeEffectRegistry.GetFloat(parameters, "rotate", e.Rotate);
            e.Scale = ShapeEffectRegistry.GetFloat(parameters, "scale", e.Scale);
            e.Squash = ShapeEffectRegistry.GetFloat(parameters, "squash", e.Squash);

            e.EvaluateRest();
        }

        /// <inheritdoc/>
        public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
        {
            parameters = null;
            var e = host.GetComponent<NeoTransformJuice>();
            if (e == null) return false;

            var p = new Dictionary<string, object>();
            ShapeEffectRegistry.WriteBase(e, p);

            // Deterministic key order: timeline (WriteBase) → bob → sway → rotate → scale → squash.
            // Each amplitude is emitted ONLY when non-zero so an effect using one channel round-trips
            // to a minimal bag (a zero-amplitude channel contributes nothing at runtime anyway).
            if (e.Bob != 0f) p["bob"] = (double)e.Bob;
            if (e.Sway != 0f) p["sway"] = (double)e.Sway;
            if (e.Rotate != 0f) p["rotate"] = (double)e.Rotate;
            if (e.Scale != 0f) p["scale"] = (double)e.Scale;
            if (e.Squash != 0f) p["squash"] = (double)e.Squash;

            parameters = p;
            return true;
        }
    }
}
