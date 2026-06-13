namespace AlterEyes.UI.Menus
{
    /// <summary>
    /// The kind of control a <see cref="MenuItemDefinition"/> renders as. Shared by settings and
    /// cheats menus — a cheat is just a Button/Toggle/Slider/Stepper with a command binding.
    /// </summary>
    public enum MenuControlKind
    {
        /// <summary> A non-interactive label / section header row. </summary>
        Label = 0,
        /// <summary> A clickable button (settings: action; cheats: fire). </summary>
        Button = 1,
        /// <summary> An on/off toggle (checkbox style). </summary>
        Toggle = 2,
        /// <summary> An on/off switch (sliding knob style). </summary>
        Switch = 3,
        /// <summary> A continuous (or whole-number) value slider. </summary>
        Slider = 4,
        /// <summary> A discrete +/- stepper. </summary>
        Stepper = 5,
        /// <summary> A single-choice dropdown populated from <see cref="MenuItemDefinition.options"/>. </summary>
        Dropdown = 6,
        /// <summary> An input-action binding rebind row. </summary>
        KeyRebind = 7,
    }

    /// <summary>
    /// The runtime value type a control reads/writes through <see cref="UserSettingsService"/>.
    /// Derived from the control kind so catalogs stay flat (no generics in serialized data).
    /// </summary>
    public enum MenuValueKind
    {
        /// <summary> No persisted value (Button, Label). </summary>
        None = 0,
        Bool = 1,
        Float = 2,
        Int = 3,
        String = 4,
    }
}
