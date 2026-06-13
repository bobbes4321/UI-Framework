using UnityEngine;

namespace AlterEyes.UI
{
    public enum AnimatorStartBehaviour
    {
        Disabled = 0,
        PlayForward = 1,
        PlayReverse = 2,
        SetFromValue = 3,
        SetToValue = 4
    }

    /// <summary>
    /// Standalone animator: plays its UIAnimation on demand from code —
    /// <c>Play()</c> / <c>Play(reverse)</c> — for spinners, hints, expand/collapse and the like.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("AlterEyes/UI/Animators/UI Animator")]
    public class UIAnimator : MonoBehaviour
    {
        public AnimatorStartBehaviour onStartBehaviour = AnimatorStartBehaviour.Disabled;

        public new UIAnimation animation = new UIAnimation();

        private bool _targetBound;

        public bool isPlaying => animation.isActive;

        protected virtual void Awake()
        {
            BindTarget();
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            switch (onStartBehaviour)
            {
                case AnimatorStartBehaviour.PlayForward: Play(); break;
                case AnimatorStartBehaviour.PlayReverse: Play(reverse: true); break;
                case AnimatorStartBehaviour.SetFromValue: SetProgressAtZero(); break;
                case AnimatorStartBehaviour.SetToValue: SetProgressAtOne(); break;
            }
        }

        protected virtual void OnDestroy()
        {
            animation.Stop(silent: true);
            animation.ReleaseTweens();
        }

        /// <summary> Binds the animation to this GameObject's RectTransform (+ CanvasGroup if fading). </summary>
        public void BindTarget()
        {
            if (_targetBound) return;
            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null && animation.fade.enabled) group = gameObject.AddComponent<CanvasGroup>();
            animation.SetTarget(GetComponent<RectTransform>(), group);
            _targetBound = true;
        }

        public void Play() => Play(reverse: false);

        public void Play(bool reverse)
        {
            BindTarget();
            animation.Play(reverse);
        }

        public void Stop() => animation.Stop();
        public void Finish() => animation.Finish();
        public void Reverse() => animation.Reverse();

        public void SetProgressAt(float progress)
        {
            BindTarget();
            animation.SetProgressAt(progress);
        }

        public void SetProgressAtZero() => SetProgressAt(0f);
        public void SetProgressAtOne() => SetProgressAt(1f);
    }
}
