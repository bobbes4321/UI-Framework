using UnityEngine;

namespace Neo.UI.Menus
{
    /// <summary>
    /// A catalog of user settings. Controls persist their value and emit change signals on the
    /// <see cref="UserSettingsService.SettingsCategory"/> stream so gameplay can react with
    /// <c>Signals.On&lt;float&gt;("Settings", "Audio/Master", ...)</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo/UI/Menus/Settings Catalog", fileName = "SettingsCatalog")]
    public class SettingsCatalog : MenuCatalog
    {
        public override string ChangeSignalCategory => UserSettingsService.SettingsCategory;
    }
}
