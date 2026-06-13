using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Marks which factory variant/size built a widget so the spec exporter can read it back
    /// verbatim — inferring the variant from baked token references would be fragile.
    /// Stamped by UIWidgetFactory; carries no behavior.
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/Widget Style Tag")]
    public class WidgetStyleTag : MonoBehaviour
    {
        [Tooltip("Factory variant, e.g. primary / secondary / ghost / danger")]
        public string variant;

        [Tooltip("Factory size, e.g. sm / md / lg")]
        public string size;
    }
}
