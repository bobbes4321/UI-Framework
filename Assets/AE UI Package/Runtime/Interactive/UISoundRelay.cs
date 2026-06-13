using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Zero-wiring UI audio: one relay in the scene listens to the package-wide UIButton/UIToggle
    /// signal streams (which every widget already publishes) and plays the clips referenced from
    /// <see cref="AEUISettings"/>. No per-widget setup, no serialized events.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Interactive/Sound Relay")]
    public class UISoundRelay : MonoBehaviour
    {
        [Tooltip("Output source; one is added here when empty")]
        public AudioSource source;

        private System.Action<ButtonSignalData> _onButton;
        private System.Action<ToggleSignalData> _onToggle;

        private void Awake()
        {
            if (source == null)
            {
                source = GetComponent<AudioSource>();
                if (source == null)
                {
                    source = gameObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                }
            }
        }

        private void OnEnable()
        {
            _onButton = HandleButton;
            _onToggle = HandleToggle;
            Signals.On(UIButton.StreamCategory, UIButton.StreamName, _onButton);
            Signals.On(UIToggle.StreamCategory, UIToggle.StreamName, _onToggle);
        }

        private void OnDisable()
        {
            Signals.Off(UIButton.StreamCategory, UIButton.StreamName, _onButton);
            Signals.Off(UIToggle.StreamCategory, UIToggle.StreamName, _onToggle);
        }

        private void HandleButton(ButtonSignalData data)
        {
            if (data.trigger != BehaviourTrigger.Click && data.trigger != BehaviourTrigger.Submit) return;
            AEUISettings settings = AEUISettings.instance;
            Play(settings != null ? settings.clickSound : null);
        }

        private void HandleToggle(ToggleSignalData data)
        {
            AEUISettings settings = AEUISettings.instance;
            if (settings == null) return;
            Play(data.isOn ? settings.toggleOnSound : settings.toggleOffSound);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null || source == null) return;
            source.PlayOneShot(clip);
        }
    }
}
