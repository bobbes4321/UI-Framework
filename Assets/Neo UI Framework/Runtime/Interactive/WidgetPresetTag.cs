using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Marks which <see cref="NeoWidgetPreset"/> built a widget so the spec exporter can read the link
    /// back by name and emit only the override delta (the fields that differ from the preset's resolved
    /// values) — preserving the link instead of flattening the preset into inline fields. Stamped by the
    /// generator; carries no behavior. Sibling to <see cref="WidgetStyleTag"/>.
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/Widget Preset Tag")]
    public class WidgetPresetTag : MonoBehaviour
    {
        [Tooltip("Preset name this widget was generated from, e.g. \"Primary Button\".")]
        public string presetName;
    }
}
