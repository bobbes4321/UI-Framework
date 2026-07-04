using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Shared flat-param-bag helpers for the effect descriptor registries
    /// (<see cref="ShapeEffectRegistry"/>, <see cref="ParticleEffectRegistry"/>). Both registries used
    /// to carry their own copy of <c>GetFloat</c>/<c>GetString</c>, and <see cref="ShapeEffectRegistry"/>
    /// additionally reached into <see cref="ParticleEffectRegistry"/>'s internals for
    /// <c>ParseColorRef</c>/<c>ColorRefToString</c> (audit D9) — this is the one shared home both
    /// registries call into instead. <see cref="ShapeEffectRegistry"/>/<see cref="ParticleEffectRegistry"/>
    /// keep their own same-named methods as thin forwarders so the per-descriptor files under
    /// <c>Editor/Agent/Effects/</c> (and other existing callers) never had to change.
    /// <para/>
    /// This is also the ONE "#hex-or-token → <see cref="ThemeColorRef"/>" decode in the package (audit
    /// D6) — <see cref="UISpecGenerator"/>'s gradient/outline parsing forwards here too, supplying its
    /// own issue-reporter callback instead of hand-rolling the hex check a second time.
    /// </summary>
    internal static class EffectParams
    {
        internal static float GetFloat(IDictionary<string, object> p, string key, float fallback) =>
            p != null && p.TryGetValue(key, out object v) && v is double d ? (float)d : fallback;

        internal static string GetString(IDictionary<string, object> p, string key, string fallback) =>
            p != null && p.TryGetValue(key, out object v) && v != null ? v.ToString() : fallback;

        /// <summary>
        /// "#hex" / "Category/Token" string → a theme-aware <see cref="ThemeColorRef"/>. An invalid
        /// "#hex" value always reports — never silently — falling back to white: pass
        /// <paramref name="reportInvalid"/> to route the message into a caller-owned report (e.g. the
        /// generator's <c>GenerateReport.issues</c>); omit it and the effect/particle callers get a
        /// <see cref="Debug.LogWarning(object)"/> instead, so no call site can go quiet by accident.
        /// </summary>
        internal static ThemeColorRef ParseColorRef(string value, Action<string> reportInvalid = null)
        {
            if (string.IsNullOrEmpty(value)) return new ThemeColorRef(Color.white);
            if (value.StartsWith("#"))
            {
                if (ColorUtils.TryParseHex(value, out Color c)) return new ThemeColorRef(c);
                if (reportInvalid != null) reportInvalid(value);
                else Debug.LogWarning($"Neo UI: invalid color '{value}' (expected #hex or a theme token) — falling back to white.");
                return new ThemeColorRef(Color.white);
            }
            return new ThemeColorRef(value); // a theme token ("Category/Name")
        }

        /// <summary> A <see cref="ThemeColorRef"/> → its token string (or "#hex" when raw). </summary>
        internal static string ColorRefToString(ThemeColorRef reference) =>
            reference == null ? "#FFFFFFFF"
            : reference.useToken ? reference.token
            : ColorUtils.ToHex(reference.color);
    }
}
