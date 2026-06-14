using System.Collections.Generic;

namespace Neo.UI
{
    /// <summary>
    /// The preview-only hook Pillar C (free viewport / Composer preview) drives to force a
    /// <see cref="UIResponsiveRoot"/> to a chosen breakpoint, decoupling the preview's selected
    /// viewport from the live canvas size. Shipped in B-core so Pillar C compiles against it without a
    /// dependency on B-core's internals. Implemented by <see cref="UIResponsiveRoot"/>.
    /// </summary>
    public interface IActiveBreakpoint
    {
        /// <summary> Force the active breakpoint by name. Empty string forces the base layout; null
        /// releases the override so the root follows the live viewport again. </summary>
        void SetActiveBreakpoint(string breakpoint);

        /// <summary> The breakpoint currently applied (empty string = base). </summary>
        string ActiveBreakpoint { get; }

        /// <summary> The breakpoint names this root knows about, in order (base excluded). </summary>
        IReadOnlyList<string> BreakpointNames { get; }
    }
}
