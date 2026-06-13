using System;
using System.Collections.Generic;

namespace AlterEyes.UI
{
    /// <summary>
    /// Anything exposing selection states to per-state animators (UISelectable and the
    /// Slider/Scrollbar wrappers that can't inherit from it).
    /// </summary>
    public interface ISelectionStateHost
    {
        UISelectionState selectionState { get; }
        void RegisterStateAnimator(ISelectionStateAnimator animator);
        void UnregisterStateAnimator(ISelectionStateAnimator animator);
    }

    /// <summary> Reusable register/relay plumbing for <see cref="ISelectionStateHost"/> implementers. </summary>
    public class SelectionStateRelay
    {
        private readonly List<ISelectionStateAnimator> _animators = new List<ISelectionStateAnimator>();

        public UISelectionState selectionState { get; private set; } = UISelectionState.Normal;

        public event Action<UISelectionState, bool> OnChanged;

        public void Register(ISelectionStateAnimator animator)
        {
            if (animator == null || _animators.Contains(animator)) return;
            _animators.Add(animator);
            animator.OnSelectionStateChanged(selectionState, instant: true);
        }

        public void Unregister(ISelectionStateAnimator animator) => _animators.Remove(animator);

        /// <summary> Unity's Selectable.SelectionState shares ordering with UISelectionState — cast and relay. </summary>
        public void Relay(int unitySelectionState, bool instant)
        {
            var mapped = (UISelectionState)unitySelectionState;
            bool changed = mapped != selectionState;
            selectionState = mapped;
            if (!changed && !instant) return;
            for (int i = 0; i < _animators.Count; i++)
                _animators[i].OnSelectionStateChanged(mapped, instant);
            OnChanged?.Invoke(mapped, instant);
        }
    }
}
