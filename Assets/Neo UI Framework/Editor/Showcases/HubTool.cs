using System;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A single launchable entry in the Hub's Tools tab — a window, a wizard or a menu action the
    /// Hub surfaces in one click. Deliberately a plain data record carrying an <see cref="invoke"/>
    /// delegate rather than an enum/switch, so the launcher set is open: a consuming project registers
    /// its own tools through <see cref="HubToolRegistry.Register"/> without forking the package.
    /// </summary>
    public sealed class HubTool
    {
        /// <summary> Stable, unique id (ordinal). Re-registering the same id replaces the entry. </summary>
        public string id;

        /// <summary> Button label shown in the launcher. </summary>
        public string label;

        /// <summary> Hover tooltip — say what the tool does, so the grid isn't a wall of bare names. </summary>
        public string tooltip;

        /// <summary>
        /// Grouping bucket (e.g. "Author", "Setup", "Advanced", "Data"). Drives the collapsible
        /// section the tool lands in and its accent color. Free-form — a new category just appears.
        /// </summary>
        public string category;

        /// <summary> Invoked when the launcher button is clicked. Never null for a usable tool. </summary>
        public Action invoke;

        /// <summary>
        /// Optional accent override. When null the Hub maps <see cref="category"/> to a NeoColors
        /// family accent (Author→Containers, Setup→Data, Advanced→Flow, Data→Data, …).
        /// </summary>
        public Color? accent;
    }
}
