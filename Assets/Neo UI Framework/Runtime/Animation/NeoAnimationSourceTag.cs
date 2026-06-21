using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Editor-stamped marker recording which animation preset (by name) was copied into each slot of a
    /// widget's interaction animators from a spec element's <c>"animations"</c> block. Because
    /// <see cref="UIAnimationPreset.CopyTo"/> bakes the channels but drops the asset link, the exporter
    /// reads these names back so per-element animations round-trip byte-identically — the animation analog
    /// of <c>WidgetPresetTag</c>. Inert at runtime (pure data the exporter consults).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeoAnimationSourceTag : MonoBehaviour
    {
        public string hover;
        public string press;
        public string selected;
        public string disabled;
        public string loop;

        public bool IsEmpty =>
            string.IsNullOrEmpty(hover) && string.IsNullOrEmpty(press) && string.IsNullOrEmpty(selected)
            && string.IsNullOrEmpty(disabled) && string.IsNullOrEmpty(loop);
    }
}
