namespace Neo.UI
{
    /// <summary>
    /// A widget component that carries its element's own category/name id — the exporter reads the
    /// id back off the component itself, so the generator skips the <c>NeoElementId</c> marker on
    /// its GameObject and derives the element's hierarchy name prefix from the component's type
    /// ("Button - Nav_Quit"). The extension seam for id-bearing widgets: a project's custom widget
    /// implements this (with a matching export path via its registered element kind) instead of the
    /// generator keeping a hardcoded component-type list.
    /// </summary>
    public interface INeoIdOwner
    {
        /// <summary> The widget's own id (may be default when unassigned). Usually the component's
        /// serialized id field, but may be a derived value instead (e.g. a stepper derives its id
        /// from its minus button's id) — never write through this either way. </summary>
        CategoryNameId OwnId { get; }
    }
}
