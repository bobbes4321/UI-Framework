using System.Collections.Generic;
using UnityEngine;
using Neo.UI.Menus;

namespace Neo.UI.Demo
{
    /// <summary>
    /// Makes the generated showcase scene feel like a live game: streams shop rows through
    /// <see cref="UIData"/>, simulates the HUD (score ticks, health/boost, speed dial, cooldown
    /// loops) and reacts to shop/cheat signals. Deliberately a pure consumer of the package's
    /// public APIs — UIData.Set, Signals.On, UICounter.SetValue, Progressor.SetValueAt — i.e.
    /// exactly the wiring a real game would write against generated UI.
    ///
    /// Widget binding leans on hierarchy order inside the generated views (first bar = health,
    /// largest radial = speed dial, first counter = score…), so it tolerates renames but tracks
    /// the demo spec's element order.
    /// </summary>
    [AddComponentMenu("Neo/UI/Demo/Showcase Director")]
    public class ShowcaseDirector : MonoBehaviour
    {
        [Tooltip("Coins shown in the HUD and the Garage header")]
        public float startCoins = 2450f;
        [Tooltip("Featured car price deducted per Shop/Buy signal")]
        public float purchaseCost = 980f;

        private Progressor _healthBar;
        private Progressor _boostBar;
        private Progressor _speedDial;
        private readonly List<Progressor> _cooldowns = new List<Progressor>();
        private readonly List<float> _cooldownValues = new List<float>();
        private UICounter _scoreCounter;
        private UICounter _speedCounter;
        private readonly List<UICounter> _coinCounters = new List<UICounter>();

        private float _coins;
        private float _score = 124500f;
        private float _clock;
        private float _scoreTimer;
        private bool _godMode;
        private bool _infiniteBoost;
        private float _simSpeed = 1f;

        private void Start()
        {
            _coins = startCoins;
            SeedShopData();
            BindHudWidgets();
            BindShopWidgets();
            SubscribeSignals();
        }

        private void OnDestroy() => UnsubscribeSignals();

        // ------------------------------------------------------------------ data binding showcase

        private void SeedShopData()
        {
            UIData.Set("Shop", "Deals", new[]
            {
                new Dictionary<string, string> { ["name"] = "Nitro Cell MkII", ["tag"] = "consumable · +18% boost", ["price"] = "1,250" },
                new Dictionary<string, string> { ["name"] = "Slipstream Tyres", ["tag"] = "equipment · wet grip", ["price"] = "3,400" },
                new Dictionary<string, string> { ["name"] = "Chrome Wrap", ["tag"] = "cosmetic · limited", ["price"] = "980" },
                new Dictionary<string, string> { ["name"] = "Pit Crew Contract", ["tag"] = "service · 7 days", ["price"] = "5,000" },
            });
        }

        // ------------------------------------------------------------------ widget lookup

        private void BindHudWidgets()
        {
            UIView hud = UIView.GetFirstView("Game", "HUD");
            if (hud == null)
            {
                Debug.LogWarning("[Neo.UI] ShowcaseDirector: no Game/HUD view in the scene — HUD simulation off", this);
                return;
            }

            foreach (Progressor progressor in hud.GetComponentsInChildren<Progressor>(true))
            {
                bool radial = progressor.GetComponentInChildren<ShapeProgressTarget>(true) != null;
                if (!radial)
                {
                    if (_healthBar == null) _healthBar = progressor;
                    else if (_boostBar == null) _boostBar = progressor;
                    continue;
                }
                var rect = (RectTransform)progressor.transform;
                if (rect.sizeDelta.x > 150f && _speedDial == null)
                {
                    _speedDial = progressor;
                }
                else
                {
                    _cooldowns.Add(progressor);
                    _cooldownValues.Add(Random.Range(0f, 100f));
                }
            }

            UICounter[] counters = hud.GetComponentsInChildren<UICounter>(true);
            if (counters.Length > 0) _scoreCounter = counters[0];
            if (counters.Length > 1) _coinCounters.Add(counters[1]);
            if (counters.Length > 2) _speedCounter = counters[2];
        }

        private void BindShopWidgets()
        {
            UIView shop = UIView.GetFirstView("Shop", "Store");
            if (shop == null) return;
            UICounter[] counters = shop.GetComponentsInChildren<UICounter>(true);
            if (counters.Length > 0) _coinCounters.Add(counters[0]);
        }

        // ------------------------------------------------------------------ signal reactions

        private void SubscribeSignals()
        {
            Signals.On("Shop", "Buy", OnPurchase);
            Signals.On(UserSettingsService.CheatCategory, "Economy/Give1k", OnGive1k);
            Signals.On(UserSettingsService.CheatCategory, "Economy/Give10k", OnGive10k);
            Signals.On(UserSettingsService.CheatCategory, "Player/HealFull", OnHealFull);
            Signals.On<object>(UserSettingsService.CheatCategory, "Player/GodMode", OnGodMode);
            Signals.On<object>(UserSettingsService.CheatCategory, "Player/InfiniteBoost", OnInfiniteBoost);
            Signals.On<object>(UserSettingsService.CheatCategory, "World/TimeScale", OnTimeScale);
        }

        private void UnsubscribeSignals()
        {
            Signals.Off("Shop", "Buy", OnPurchase);
            Signals.Off(UserSettingsService.CheatCategory, "Economy/Give1k", OnGive1k);
            Signals.Off(UserSettingsService.CheatCategory, "Economy/Give10k", OnGive10k);
            Signals.Off(UserSettingsService.CheatCategory, "Player/HealFull", OnHealFull);
            Signals.Off<object>(UserSettingsService.CheatCategory, "Player/GodMode", OnGodMode);
            Signals.Off<object>(UserSettingsService.CheatCategory, "Player/InfiniteBoost", OnInfiniteBoost);
            Signals.Off<object>(UserSettingsService.CheatCategory, "World/TimeScale", OnTimeScale);
        }

        private void OnPurchase() => SetCoins(Mathf.Max(0f, _coins - purchaseCost));
        private void OnGive1k() => SetCoins(_coins + 1000f);
        private void OnGive10k() => SetCoins(_coins + 10000f);
        private void OnHealFull() => _clock = 0f;
        private void OnGodMode(object value) => _godMode = value is bool b && b;
        private void OnInfiniteBoost(object value) => _infiniteBoost = value is bool b && b;
        private void OnTimeScale(object value)
        {
            if (value is float f) _simSpeed = f;
            else if (value is double d) _simSpeed = (float)d;
        }

        private void SetCoins(float coins)
        {
            _coins = coins;
            foreach (UICounter counter in _coinCounters)
                if (counter != null) counter.SetValue(_coins);
        }

        // ------------------------------------------------------------------ HUD simulation

        private void Update()
        {
            float dt = Time.deltaTime * _simSpeed;
            _clock += dt;

            if (_healthBar != null)
                _healthBar.SetValueAt(_godMode ? 100f : 62f + 28f * Mathf.Sin(_clock * 0.6f));
            if (_boostBar != null)
                _boostBar.SetValueAt(_infiniteBoost ? 100f : Mathf.PingPong(_clock * 22f, 100f));

            for (int i = 0; i < _cooldowns.Count; i++)
            {
                if (_cooldowns[i] == null) continue;
                _cooldownValues[i] += (28f + 17f * i) * dt;
                if (_cooldownValues[i] > 100f) _cooldownValues[i] = 0f;
                _cooldowns[i].SetValueAt(_cooldownValues[i]);
            }

            float speed = 246f + 58f * Mathf.Sin(_clock * 0.35f) + 8f * Mathf.Sin(_clock * 2.1f);
            if (_speedDial != null) _speedDial.SetValueAt(speed / 4.2f); // dial is 0–100, top speed ~420
            if (_speedCounter != null) _speedCounter.SetValue(speed, instant: true);

            _scoreTimer += dt;
            if (_scoreTimer >= 1.4f && _scoreCounter != null)
            {
                _scoreTimer = 0f;
                _score += Random.Range(80, 460);
                _scoreCounter.SetValue(_score); // rolls via the counter's own tween
            }
        }
    }
}
