using System.Collections.Generic;
using UnityEngine;
using Neo.UI.Menus;

namespace Neo.UI.Demo
{
    /// <summary>
    /// Makes the generated showcase scene feel like a live game: simulates the HUD (score ticks,
    /// health/boost, speed dial, cooldown loops) and reacts to player/world cheat signals.
    ///
    /// This is deliberately the package's <b>low-level output-driving</b> consumer: it reaches HUD
    /// widgets by hierarchy order (first bar = health, largest radial = speed dial, first counter =
    /// score…) and drives them imperatively with <see cref="Progressor.SetValueAt"/> /
    /// <see cref="UICounter.SetValue"/>. Scalar output like this is outside the binding contract — a
    /// view has no string-addressed "set this counter" command — so direct widget access is the
    /// honest tool here. For the <i>blessed</i> input/list/settings wiring (domain signals, typed
    /// list data, generated <c>Wire()</c>) see the companion <c>Game.UI.GameUIBindings</c> and
    /// <c>Assets/docs/developer-binding-guide.md</c>; that class owns the shop economy.
    /// </summary>
    [AddComponentMenu("Neo/UI/Demo/Showcase Director")]
    public class ShowcaseDirector : MonoBehaviour
    {
        private Progressor _healthBar;
        private Progressor _boostBar;
        private Progressor _speedDial;
        private readonly List<Progressor> _cooldowns = new List<Progressor>();
        private readonly List<float> _cooldownValues = new List<float>();
        private UICounter _scoreCounter;
        private UICounter _speedCounter;

        private float _score = 124500f;
        private float _clock;
        private float _scoreTimer;
        private bool _godMode;
        private bool _infiniteBoost;
        private float _simSpeed = 1f;

        private void Start()
        {
            BindHudWidgets();
            SubscribeSignals();
        }

        private void OnDestroy() => UnsubscribeSignals();

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
            if (counters.Length > 2) _speedCounter = counters[2];
        }

        // ------------------------------------------------------------------ signal reactions
        // Player/World cheats modulate the HUD simulation below, so they live here with the sim
        // they drive. They are cheat *catalog* entries (not view widgets), so the binding manifest
        // surfaces them differently: the valued ones (GodMode/InfiniteBoost/TimeScale) are bound
        // through GameUIBindings' generated Wire(); HealFull is a cheat *button* (no value) so it is
        // reached by the raw cheat stream, as below. See GameUIBindings for the contrasting path.

        private void SubscribeSignals()
        {
            Signals.On(UserSettingsService.CheatCategory, "Player/HealFull", OnHealFull);
            Signals.On<object>(UserSettingsService.CheatCategory, "Player/GodMode", OnGodMode);
            Signals.On<object>(UserSettingsService.CheatCategory, "Player/InfiniteBoost", OnInfiniteBoost);
            Signals.On<object>(UserSettingsService.CheatCategory, "World/TimeScale", OnTimeScale);
        }

        private void UnsubscribeSignals()
        {
            Signals.Off(UserSettingsService.CheatCategory, "Player/HealFull", OnHealFull);
            Signals.Off<object>(UserSettingsService.CheatCategory, "Player/GodMode", OnGodMode);
            Signals.Off<object>(UserSettingsService.CheatCategory, "Player/InfiniteBoost", OnInfiniteBoost);
            Signals.Off<object>(UserSettingsService.CheatCategory, "World/TimeScale", OnTimeScale);
        }

        private void OnHealFull() => _clock = 0f;
        private void OnGodMode(object value) => _godMode = value is bool b && b;
        private void OnInfiniteBoost(object value) => _infiniteBoost = value is bool b && b;
        private void OnTimeScale(object value)
        {
            if (value is float f) _simSpeed = f;
            else if (value is double d) _simSpeed = (float)d;
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
