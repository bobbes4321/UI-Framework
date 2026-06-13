using UnityEngine;

namespace Neo.UI.Menus
{
    /// <summary>
    /// A <see cref="MenuPresenter"/> specialised for a <see cref="CheatCatalog"/>. Toggle and numeric
    /// cheats work through the shared binder; this adds store-backed favourites.
    /// </summary>
    [AddComponentMenu("Neo/UI/Menus/Cheat Menu")]
    public class CheatMenu : MenuPresenter
    {
        public bool IsFavourite(MenuItemDefinition item) => CheatFavourites.IsFavourite(item);
        public void ToggleFavourite(MenuItemDefinition item) => CheatFavourites.Toggle(item);
    }

    /// <summary>
    /// Store-backed cheat favourites (mirrors common's FavouritesHolder, but runtime + injectable store).
    /// A favourite is just a persisted bool under the "CheatFavourite" category, so it round-trips through
    /// any <see cref="IUserSettingsStore"/> without extra machinery.
    /// </summary>
    public static class CheatFavourites
    {
        public const string Category = "CheatFavourite";

        public static bool IsFavourite(MenuItemDefinition item) =>
            item != null && UserSettingsService.Get<bool>(Category, item.Id);

        public static void Set(MenuItemDefinition item, bool favourite)
        {
            if (item == null) return;
            UserSettingsService.Set(Category, item.Id, favourite);
        }

        public static void Toggle(MenuItemDefinition item)
        {
            if (item == null) return;
            Set(item, !IsFavourite(item));
        }
    }
}
