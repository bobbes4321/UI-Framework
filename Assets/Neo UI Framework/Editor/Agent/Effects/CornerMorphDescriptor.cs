using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Tier-1 corner morph descriptor: attaches <see cref="NeoCornerMorph"/> from a flat param bag and
    /// reads it back deterministically. In the default uniform mode it carries <c>radiusMin</c>/
    /// <c>radiusMax</c> (scalars); with <c>perCorner:true</c> it adds <c>radiiFrom</c>/<c>radiiTo</c> as
    /// 4-number JSON arrays (Vector4, component order x=TR y=BR z=TL w=BL).
    ///
    /// <para>BatchSafe — it only animates shape params that ride the shared SDF material's vertex
    /// channels. Registered as a built-in via <c>ShapeEffectRegistry.Register(new CornerMorphDescriptor())</c>;
    /// a consuming project could equally register it from its own code, no core edit required.</para>
    /// </summary>
    public sealed class CornerMorphDescriptor : IShapeEffectDescriptor
    {
        /// <summary> Stable, agent-addressable effect id. </summary>
        public string Id => "cornerMorph";

        /// <summary> Tier-1: writes only vertex-channel shape params, so the batch is preserved. </summary>
        public bool BatchSafe => true;

        /// <inheritdoc/>
        public void Apply(GameObject host, IDictionary<string, object> parameters)
        {
            var e = host.GetComponent<NeoCornerMorph>();
            if (e == null) e = host.AddComponent<NeoCornerMorph>();
            ShapeEffectRegistry.ApplyBase(e, parameters);

            e.RadiusMin = ShapeEffectRegistry.GetFloat(parameters, "radiusMin", e.RadiusMin);
            e.RadiusMax = ShapeEffectRegistry.GetFloat(parameters, "radiusMax", e.RadiusMax);
            e.PerCorner = ShapeEffectRegistry.GetBool(parameters, "perCorner", e.PerCorner);

            // Per-corner endpoints arrive as 4-number JSON arrays (List<object> of double). Default to a
            // uniform Vector4 built from radiusMin/radiusMax so a bare `perCorner:true` still animates.
            e.RadiiFrom = GetVector4(parameters, "radiiFrom",
                new Vector4(e.RadiusMin, e.RadiusMin, e.RadiusMin, e.RadiusMin));
            e.RadiiTo = GetVector4(parameters, "radiiTo",
                new Vector4(e.RadiusMax, e.RadiusMax, e.RadiusMax, e.RadiusMax));

            e.EvaluateRest();
        }

        /// <inheritdoc/>
        public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
        {
            parameters = null;
            var e = host.GetComponent<NeoCornerMorph>();
            if (e == null) return false;

            var p = new Dictionary<string, object>();
            ShapeEffectRegistry.WriteBase(e, p);

            // Deterministic key order: timeline (WriteBase) → radii → perCorner → optional endpoints.
            p["radiusMin"] = (double)e.RadiusMin;
            p["radiusMax"] = (double)e.RadiusMax;
            if (e.PerCorner)
            {
                p["perCorner"] = true;
                p["radiiFrom"] = ToList(e.RadiiFrom);
                p["radiiTo"] = ToList(e.RadiiTo);
            }

            parameters = p;
            return true;
        }

        /// <summary> Reads a 4-number JSON array (List&lt;object&gt; of double) as a Vector4; falls back when absent/malformed. </summary>
        private static Vector4 GetVector4(IDictionary<string, object> p, string key, Vector4 fallback)
        {
            if (p != null && p.TryGetValue(key, out object raw) && raw is List<object> list && list.Count == 4)
                return new Vector4(ToFloat(list[0]), ToFloat(list[1]), ToFloat(list[2]), ToFloat(list[3]));
            return fallback;
        }

        private static float ToFloat(object o) => o is double d ? (float)d : 0f;

        /// <summary> Writes a Vector4 as the project JSON array model (List&lt;object&gt; of double). </summary>
        private static List<object> ToList(Vector4 v) =>
            new List<object> { (double)v.x, (double)v.y, (double)v.z, (double)v.w };
    }
}
