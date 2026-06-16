using System.Collections.Generic;
using Neo.UI;
using Neo.UI.Menus;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The hand-written half of the GameUI binding contract — the partner to the generated
    /// <c>GameUIBindings.g.cs</c>. This is the file a developer writes and owns; the generator never
    /// touches it, so regenerating the UI (or re-running the binding stub) never wipes this logic.
    /// It is the worked example referenced by <c>Assets/docs/developer-binding-guide.md</c>.
    ///
    /// It demonstrates the three pillars of the guide:
    ///   1. Call <c>Wire()</c> once in Start — subscribes every domain signal and binds every
    ///      setting/cheat to a hook (most stay empty no-ops; you implement only what you need).
    ///   2. React to a domain signal — <see cref="OnShopBuy"/> (the Shop/Buy button onClick.signal).
    ///   3. Feed a bound list from your OWN type — <see cref="SeedShopDeals"/> via the generated
    ///      <c>PopulateShopDeals</c> helper and a one-time projection to row tokens.
    /// …plus the two boundaries the binding contract intentionally does NOT cover, done the honest way:
    ///   • Cheat/settings <b>buttons</b> carry no value, so <c>Wire()</c> can't auto-wire them — react
    ///     to the raw cheat stream instead (Economy/Give1k, Economy/Give10k below).
    ///   • Scalar <b>output</b> widgets (the coin counter) aren't string-addressable — fetch them by a
    ///     generated view-id const and drive them directly.
    /// </summary>
    [AddComponentMenu("Neo/UI/Demo/Game UI Bindings")]
    public partial class GameUIBindings : MonoBehaviour
    {
        /// <summary> A shop deal in the developer's own model — projected to list tokens once. </summary>
        private readonly struct Deal
        {
            public readonly string Name, Tag, Price;
            public Deal(string name, string tag, string price) { Name = name; Tag = tag; Price = price; }
        }

        [Tooltip("Coins shown in the Garage header; Shop/Buy deducts, the +coins cheats add")]
        public float coins = 2450f;
        [Tooltip("Coins deducted per Shop/Buy signal")]
        public float purchaseCost = 980f;

        private UICounter _coinCounter;

        private void Start()
        {
            Wire();                  // pillar 1 — generated subscriptions + setting/cheat bindings
            SeedShopDeals();         // pillar 3 — typed list data
            SubscribeCheatButtons(); // boundary — cheat buttons aren't auto-wired
            RefreshCoins();
        }

        private void OnDestroy()
        {
            Signals.Off(UserSettingsService.CheatCategory, "Economy/Give1k", OnGive1k);
            Signals.Off(UserSettingsService.CheatCategory, "Economy/Give10k", OnGive10k);
        }

        // ------------------------------------------------------------- pillar 3: typed list data

        private void SeedShopDeals()
        {
            var deals = new[]
            {
                new Deal("Nitro Cell MkII",   "consumable · +18% boost", "1,250"),
                new Deal("Slipstream Tyres",  "equipment · wet grip",    "3,400"),
                new Deal("Chrome Wrap",       "cosmetic · limited",      "980"),
                new Deal("Pit Crew Contract", "service · 7 days",        "5,000"),
            };

            // The generated helper wraps UIData.Set<T> — supply the row→token projection once;
            // later you can patch single rows with UIData.Update/Add/RemoveAt (no full rebuild).
            PopulateShopDeals(deals, d => new Dictionary<string, string>
            {
                ["name"] = d.Name,
                ["tag"] = d.Tag,
                ["price"] = d.Price,
            });
        }

        // ------------------------------------------------------- pillar 2: react to a domain signal

        // Declared in GameUIBindings.g.cs as `partial void OnShopBuy();` and called from Wire()'s
        // Signals.On(...) subscription. Both Shop/BuyFeatured and the per-row BUY buttons publish it.
        partial void OnShopBuy()
        {
            coins = Mathf.Max(0f, coins - purchaseCost);
            RefreshCoins();
        }

        // A valued setting, auto-bound by the generated Wire() via UserSettingsService.Bind<float>.
        // (One implemented hook for show; the rest of the generated On…Changed hooks stay no-ops.)
        partial void OnAudioMasterChanged(float value) => AudioListener.volume = value;

        // ------------------------------------------------ boundary: cheat *buttons* → raw cheat stream

        private void SubscribeCheatButtons()
        {
            Signals.On(UserSettingsService.CheatCategory, "Economy/Give1k", OnGive1k);
            Signals.On(UserSettingsService.CheatCategory, "Economy/Give10k", OnGive10k);
        }

        private void OnGive1k() { coins += 1000f; RefreshCoins(); }
        private void OnGive10k() { coins += 10000f; RefreshCoins(); }

        // --------------------------------------------- boundary: scalar output → fetch by view-id const

        private void RefreshCoins() 
        {
            if (_coinCounter == null)
            {
                // ViewShopStore is a generated const ("Shop/Store"). The binding contract covers
                // inputs + lists, not scalar output, so we reach the counter by view id and drive it.
                CategoryNameId.Parse(GameUIBindings.ViewShopStore, out string category, out string name);
                UIView shop = UIView.GetFirstView(category, name);
                if (shop != null) _coinCounter = shop.GetComponentInChildren<UICounter>(true);
            }
            if (_coinCounter != null) _coinCounter.SetValue(coins);
        }
    }
}
