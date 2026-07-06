using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Staggered entrance for a container's children: when the owning UIContainer/UIView shows,
    /// each direct child fades in with an incremental start delay (default 40 ms) — the "cascade"
    /// that makes lists and menus feel alive. Spec: <c>"cascade": true</c> on vstack/hstack/grid.
    /// Runtime-only juice: baked prefab state stays fully visible (WYSIWYG).
    /// </summary>
    [AddComponentMenu("Neo/UI/Containers/Cascade Children")]
    public class UICascadeChildren : MonoBehaviour
    {
        [Tooltip("Delay added per child (seconds)")]
        public float stagger = 0.04f;
        [Tooltip("Fade duration per child (seconds)")]
        public float itemDuration = 0.25f;
        public Ease ease = Ease.OutQuad;

        private UIContainer _container;
        private readonly List<FloatTween> _tweens = new List<FloatTween>();

        private void OnEnable()
        {
            _container = GetComponentInParent<UIContainer>();
            if (_container != null) _container.OnShow += HandleShow;
        }

        private void OnDisable()
        {
            if (_container != null) _container.OnShow -= HandleShow;
            StopAll(restore: true);
        }

        private void HandleShow()
        {
            if (!Application.isPlaying) return;
            StopAll(restore: true);

            int index = 0;
            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeSelf) continue;
                CanvasGroup group = child.GetComponent<CanvasGroup>();
                if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();

                group.alpha = 0f;
                var tween = new FloatTween();
                tween.SetTarget(group, () => group.alpha, value => group.alpha = value);
                tween.SetFrom(0f);
                tween.SetTo(1f);
                tween.settings.duration = itemDuration;
                tween.settings.ease = ease;
                tween.settings.startDelay = index * stagger;
                tween.Play();
                _tweens.Add(tween);
                index++;
            }
        }

        private void StopAll(bool restore)
        {
            foreach (FloatTween tween in _tweens)
            {
                if (tween == null) continue;
                tween.Stop(silent: true);
                if (restore && tween.setter != null) tween.setter(1f);
            }
            _tweens.Clear();
        }
    }
}
