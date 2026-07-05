using UnityEngine;

namespace Neo.UI.Menus
{
    /// <summary>
    /// A catalog of debug cheats. Button cheats fire on the <see cref="UserSettingsService.CheatCategory"/>
    /// stream (<c>Signals.On("Cheat", "Player/GiveGold", ...)</c>); toggle/numeric cheats carry their
    /// value as the signal payload. Adds favourites support on top of <see cref="MenuCatalog"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Menus/Cheat Catalog", fileName = "CheatCatalog")]
    public class CheatCatalog : MenuCatalog
    {
        [Tooltip("When true the cheat menu shows a favourites pin per cheat and a favourites group.")]
        public bool favouritesEnabled = true;

        public override string ChangeSignalCategory => UserSettingsService.CheatCategory;
    }
}
