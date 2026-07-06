using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A container addressed by category/name — the content surface a <see cref="UITab"/> shows and
    /// hides through its <c>containerReference</c>. Panels live as siblings beside their tab bar
    /// inside a view; the tab↔panel link is a direct in-prefab reference, while the registry keeps
    /// panels addressable by name for agents and tooling (mirrors <see cref="UIView"/>/<see cref="UITag"/>).
    /// </summary>
    [AddComponentMenu("Neo/UI/Containers/UI Panel")]
    public class UIPanel : UIContainer
    {
        public PanelId id = new PanelId();

        [Tooltip("Fade the panel's content in when it activates — tab switches read as a transition " +
                 "instead of an instant swap")]
        public bool fadeInOnEnable = true;
        [Min(0f)] public float fadeInDuration = 0.2f;

        private static readonly HashSet<UIPanel> Registry = new HashSet<UIPanel>();
        private FloatTween _fadeTween;

        /// <summary> All enabled panels. </summary>
        public static IEnumerable<UIPanel> allPanels => Registry;

        public static IEnumerable<UIPanel> GetPanels(string category, string name) =>
            Registry.Where(p => p.id.Matches(category, name));

        public static UIPanel GetFirstPanel(string category, string name) =>
            Registry.FirstOrDefault(p => p.id.Matches(category, name));

        protected virtual void OnEnable()
        {
            Registry.Add(this);

            // panels show/hide via SetActive (no container animator), so the show juice lives here
            if (fadeInOnEnable && fadeInDuration > 0f && Application.isPlaying)
            {
                if (_fadeTween == null)
                {
                    _fadeTween = new FloatTween();
                    _fadeTween.SetTarget(this, () => canvasGroup.alpha, a => canvasGroup.alpha = a);
                }
                _fadeTween.Stop(silent: true);
                _fadeTween.SetFrom(0f);
                _fadeTween.SetTo(1f);
                _fadeTween.settings.duration = fadeInDuration;
                _fadeTween.settings.ease = Ease.OutQuad;
                canvasGroup.alpha = 0f;
                _fadeTween.Play();
            }
        }

        /// <summary>
        /// Kills a running enable-fade and restores full alpha. Synchronous capture paths
        /// (thumbnails, screenshots) call this after activating a panel so the snapshot is WYSIWYG
        /// instead of catching frame one of the play-mode fade (alpha 0 — a blank render).
        /// </summary>
        public void CancelEnableFade()
        {
            _fadeTween?.Stop(silent: true);
            canvasGroup.alpha = 1f;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Registry.Remove(this);
            if (_fadeTween != null)
            {
                _fadeTween.Stop(silent: true);
                canvasGroup.alpha = 1f; // never trap a panel half-faded for its next activation
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
