using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neo.UI
{
    public enum VisibilityState
    {
        Visible = 0,
        Hidden = 1,
        IsShowing = 2,
        IsHiding = 3
    }

    public enum ContainerStartBehaviour
    {
        Disabled = 0,
        InstantHide = 1,
        InstantShow = 2,
        Hide = 3,
        Show = 4
    }

    /// <summary>
    /// An animator participating in a container's show/hide transition. The container's transition
    /// is complete only when every registered animator reports it is no longer animating
    /// (registration model — the container never polls component lists).
    /// Animators must handle interruption internally: OnShow while their hide animation runs
    /// reverses it from the current progress instead of snapping.
    /// </summary>
    public interface IContainerAnimator
    {
        void OnShow(bool instant);
        void OnHide(bool instant);
        bool isAnimating { get; }
        float showDuration { get; }
        float hideDuration { get; }
    }

    /// <summary>
    /// Base visibility container shared by UIView, UIPopup and UITooltip:
    /// Show/Hide/Toggle with animated transitions, interruption handling (a Hide during Show
    /// reverses the running animations), completion via registered animators + progressors,
    /// and the usual chrome (auto-hide, when-hidden disables, EventSystem selection).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Neo/UI/Containers/UI Container")]
    public class UIContainer : MonoBehaviour, ITickable
    {
        [Header("Behaviour")]
        public ContainerStartBehaviour onStartBehaviour = ContainerStartBehaviour.Disabled;

        [Tooltip("Automatically hide again after the show transition + delay")]
        public bool autoHideAfterShow;
        public float autoHideAfterShowDelay = 3f;

        [Tooltip("Move to the end of the parent on Show so this container renders above its siblings " +
                 "(views layered by a flow node show in list order, last on top)")]
        public bool bringToFrontOnShow;

        [Header("When Hidden")]
        public bool disableGameObjectWhenHidden;
        public bool disableCanvasWhenHidden = true;
        public bool disableGraphicRaycasterWhenHidden = true;

        [Header("Canvas Group")]
        [Tooltip("Manage CanvasGroup interactable/blocksRaycasts during transitions and when hidden")]
        public bool controlCanvasGroup = true;

        [Header("Custom Start Position")]
        public bool useCustomStartPosition;
        public Vector3 customStartPosition;

        [Header("EventSystem Selection")]
        public bool clearSelectedOnShow;
        public bool clearSelectedOnHide;
        public bool autoSelectAfterShow;
        public GameObject autoSelectTarget;

        [Header("Callbacks")]
        public UnityEvent OnShowCallback = new UnityEvent();
        public UnityEvent OnVisibleCallback = new UnityEvent();
        public UnityEvent OnHideCallback = new UnityEvent();
        public UnityEvent OnHiddenCallback = new UnityEvent();

        /// <summary> C# events mirroring the UnityEvents. </summary>
        public event Action OnShow;
        public event Action OnVisible;
        public event Action OnHide;
        public event Action OnHidden;
        public event Action<VisibilityState> OnVisibilityChanged;

        private readonly List<IContainerAnimator> _animators = new List<IContainerAnimator>();
        private readonly List<Progressor> _progressors = new List<Progressor>();

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Canvas _canvas;
        private GraphicRaycaster _graphicRaycaster;
        private float _autoHideCountdown = -1f;
        private bool _startBehaviourExecuted;
        private bool _customRestPoseEstablished;

        public VisibilityState visibilityState { get; private set; } = VisibilityState.Visible;

        public bool isVisible => visibilityState == VisibilityState.Visible;
        public bool isHidden => visibilityState == VisibilityState.Hidden;
        public bool inTransition => visibilityState == VisibilityState.IsShowing || visibilityState == VisibilityState.IsHiding;

        public RectTransform rectTransform => _rectTransform != null ? _rectTransform : _rectTransform = GetComponent<RectTransform>();

        public CanvasGroup canvasGroup
        {
            get
            {
                if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                return _canvasGroup;
            }
        }

        public IReadOnlyList<IContainerAnimator> animators => _animators;

        /// <summary> Worst-case show transition duration across registered animators and progressors. </summary>
        public float totalShowDuration
        {
            get
            {
                float total = 0f;
                foreach (IContainerAnimator a in _animators) total = Mathf.Max(total, a.showDuration);
                foreach (Progressor p in _progressors) if (p != null) total = Mathf.Max(total, p.duration);
                return total;
            }
        }

        public float totalHideDuration
        {
            get
            {
                float total = 0f;
                foreach (IContainerAnimator a in _animators) total = Mathf.Max(total, a.hideDuration);
                foreach (Progressor p in _progressors) if (p != null) total = Mathf.Max(total, p.duration);
                return total;
            }
        }

        // ------------------------------------------------------------------ lifecycle

        protected virtual void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvas = GetComponent<Canvas>();
            _graphicRaycaster = GetComponent<GraphicRaycaster>();

            if (useCustomStartPosition && Application.isPlaying)
                rectTransform.anchoredPosition3D = customStartPosition;
        }

        protected virtual void Start()
        {
            if (!Application.isPlaying || _startBehaviourExecuted) return;
            _startBehaviourExecuted = true;
            switch (onStartBehaviour)
            {
                case ContainerStartBehaviour.InstantHide: InstantHide(); break;
                case ContainerStartBehaviour.InstantShow: InstantShow(); break;
                case ContainerStartBehaviour.Hide: Hide(); break;
                case ContainerStartBehaviour.Show: InstantHide(); Show(); break;
            }
        }

        protected virtual void OnDisable()
        {
            UITick.Unregister(this);
        }

        // ------------------------------------------------------------------ animator registration

        public void RegisterAnimator(IContainerAnimator animator)
        {
            if (animator == null || _animators.Contains(animator)) return;
            _animators.Add(animator);
        }

        public void UnregisterAnimator(IContainerAnimator animator) => _animators.Remove(animator);

        public void RegisterProgressor(Progressor progressor)
        {
            if (progressor == null || _progressors.Contains(progressor)) return;
            _progressors.Add(progressor);
        }

        public void UnregisterProgressor(Progressor progressor) => _progressors.Remove(progressor);

        /// <summary>
        /// Establishes the runtime rest pose for containers laid out at an editor-only offset (a scene
        /// builder spreads views side-by-side, then relies on customStartPosition to snap them back at
        /// runtime). Awake already snapped the rect to customStartPosition, but a position animator's
        /// own Awake may have captured its StartValue endpoints from the pre-snap offset (component
        /// Awake order is undefined), which would animate shown views off-screen. Re-assert the pose
        /// and have position animators recapture — once, synchronously before the first transition runs
        /// them, so this also covers views the flow activates lazily. Scoped to useCustomStartPosition:
        /// containers without it are completely untouched.
        /// </summary>
        private void EnsureCustomRestPose()
        {
            if (!useCustomStartPosition || _customRestPoseEstablished || !Application.isPlaying) return;
            _customRestPoseEstablished = true;
            rectTransform.anchoredPosition3D = customStartPosition;
            foreach (IContainerAnimator animator in _animators)
                (animator as UIContainerUIAnimator)?.RecaptureStartValues();
        }

        private bool anyAnimatorActive
        {
            get
            {
                foreach (IContainerAnimator a in _animators)
                    if (a.isAnimating) return true;
                foreach (Progressor p in _progressors)
                    if (p != null && p.isActive) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ commands

        public virtual void Show() => Show(instant: false);
        public virtual void Hide() => Hide(instant: false);
        public virtual void InstantShow() => Show(instant: true);
        public virtual void InstantHide() => Hide(instant: true);

        public void Toggle()
        {
            NormalizeBakedHiddenState();
            if (isVisible || visibilityState == VisibilityState.IsShowing) Hide();
            else Show();
        }

        /// <summary>
        /// A container the generator baked hidden (disableGameObjectWhenHidden → SetActive(false)
        /// at bake time) wakes with the in-memory DEFAULT state Visible while its GameObject is
        /// inactive — it never went through Hide(), so the state lies. Trust the GameObject:
        /// without this, Show()'s redundancy early-out swallows the first show and a tab can
        /// highlight while its panel never appears.
        /// </summary>
        private void NormalizeBakedHiddenState()
        {
            if (visibilityState == VisibilityState.Visible && disableGameObjectWhenHidden && !gameObject.activeSelf)
                visibilityState = VisibilityState.Hidden; // quiet correction — no visibility event
        }

        protected virtual void Show(bool instant)
        {
            // consume the start behaviour BEFORE the redundancy early-out: an explicit command
            // outranks the default start state. A flow can Show() a default-Visible view before
            // its Start() ran — the Show is a no-op, but Start() must not InstantHide over it.
            _startBehaviourExecuted = true;
            NormalizeBakedHiddenState();
            if (visibilityState == VisibilityState.Visible || visibilityState == VisibilityState.IsShowing) return;
            _autoHideCountdown = -1f;

            if (bringToFrontOnShow) transform.SetAsLastSibling();
            EnableForTransition();

            bool interrupting = visibilityState == VisibilityState.IsHiding;
            SetVisibility(VisibilityState.IsShowing);

            if (clearSelectedOnShow) ClearSelected();
            OnShowCallback?.Invoke();
            OnShow?.Invoke();

            EnsureCustomRestPose();
            foreach (IContainerAnimator animator in _animators) animator.OnShow(instant);
            foreach (Progressor progressor in _progressors)
            {
                if (progressor == null) continue;
                if (instant) progressor.SetProgressAt(1f);
                else if (interrupting && progressor.isActive) progressor.Reverse();
                else progressor.Play(PlayDirection.Forward);
            }

            if (instant || !anyAnimatorActive)
            {
                CompleteShow();
            }
            else
            {
                if (controlCanvasGroup) canvasGroup.interactable = false;
                UITick.Register(this);
            }
        }

        protected virtual void Hide(bool instant)
        {
            _startBehaviourExecuted = true; // see Show(): explicit commands consume the start behaviour
            if (visibilityState == VisibilityState.Hidden || visibilityState == VisibilityState.IsHiding) return;
            _autoHideCountdown = -1f;

            bool interrupting = visibilityState == VisibilityState.IsShowing;
            SetVisibility(VisibilityState.IsHiding);

            if (clearSelectedOnHide) ClearSelected();
            OnHideCallback?.Invoke();
            OnHide?.Invoke();

            EnsureCustomRestPose();
            foreach (IContainerAnimator animator in _animators) animator.OnHide(instant);
            foreach (Progressor progressor in _progressors)
            {
                if (progressor == null) continue;
                if (instant) progressor.SetProgressAt(0f);
                else if (interrupting && progressor.isActive) progressor.Reverse();
                else progressor.Play(PlayDirection.Reverse);
            }

            if (instant || !anyAnimatorActive)
            {
                CompleteHide();
            }
            else
            {
                if (controlCanvasGroup) canvasGroup.interactable = false;
                UITick.Register(this);
            }
        }

        // ------------------------------------------------------------------ transition completion (tick-driven)

        public void Tick(float deltaTime)
        {
            switch (visibilityState)
            {
                case VisibilityState.IsShowing:
                    if (!anyAnimatorActive) CompleteShow();
                    break;
                case VisibilityState.IsHiding:
                    if (!anyAnimatorActive) CompleteHide();
                    break;
                case VisibilityState.Visible:
                    if (_autoHideCountdown >= 0f)
                    {
                        _autoHideCountdown -= deltaTime;
                        if (_autoHideCountdown < 0f)
                        {
                            UITick.Unregister(this);
                            Hide();
                        }
                    }
                    else
                    {
                        UITick.Unregister(this);
                    }
                    break;
                default:
                    UITick.Unregister(this);
                    break;
            }
        }

        private void CompleteShow()
        {
            SetVisibility(VisibilityState.Visible);
            if (controlCanvasGroup)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            OnVisibleCallback?.Invoke();
            OnVisible?.Invoke();

            if (autoSelectAfterShow && autoSelectTarget != null) SetSelected(autoSelectTarget);

            if (autoHideAfterShow && Application.isPlaying)
            {
                _autoHideCountdown = autoHideAfterShowDelay;
                UITick.Register(this);
            }
            else
            {
                UITick.Unregister(this);
            }
        }

        private void CompleteHide()
        {
            SetVisibility(VisibilityState.Hidden);
            UITick.Unregister(this);

            if (controlCanvasGroup)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            if (disableCanvasWhenHidden && _canvas != null) _canvas.enabled = false;
            if (disableGraphicRaycasterWhenHidden && _graphicRaycaster != null) _graphicRaycaster.enabled = false;

            OnHiddenCallback?.Invoke();
            OnHidden?.Invoke();

            if (disableGameObjectWhenHidden) gameObject.SetActive(false);
        }

        private void EnableForTransition()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (_canvas != null) _canvas.enabled = true;
            if (_graphicRaycaster != null) _graphicRaycaster.enabled = true;
        }

        private void SetVisibility(VisibilityState newState)
        {
            if (visibilityState == newState) return;
            visibilityState = newState;
            OnVisibilityChanged?.Invoke(newState);
        }

        // ------------------------------------------------------------------ selection helpers

        private static void ClearSelected()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private static void SetSelected(GameObject target)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
