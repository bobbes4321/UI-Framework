using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Round-trip marker carrying the authored spec <c>id</c> of an element that has no interactive
    /// widget component to hold it (shape, text, image, icon, counter, spacer, container, progress,
    /// list, …). Interactive widgets (button/toggle/tab/slider/dropdown/panel/tabbar/stepper) already
    /// round-trip their id through their own <c>NeoId</c>; this is the seam for everything else, so ANY
    /// element addressable in the spec keeps its id across generate ↔ export (the spec is the source of
    /// truth — an authored id must survive the round trip, not be silently dropped).
    /// <para>
    /// Stamped by the generator only when an explicit id was authored AND the element carries no
    /// id-bearing widget, so widget prefabs stay lean; absence ⇒ an unnamed/auto-named element.
    /// Force-text and trivially diffable.
    /// </para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Containers/Neo Element Id")]
    public class NeoElementId : MonoBehaviour
    {
        /// <summary> The authored "Category/Name" element id, verbatim. </summary>
        public string id;
    }
}
