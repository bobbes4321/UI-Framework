using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Neo.UI
{
    /// <summary>
    /// Tooltip container: parenting modes (tooltips canvas / trigger / tag target), tracking modes
    /// (follow pointer / trigger / target), show/hide hover delays and size constraints.
    /// Shown/hidden by a <see cref="UITooltipTrigger"/> or from code.
    /// </summary>
    [AddComponentMenu("Neo/UI/Containers/UI Tooltip")]
    public class UITooltip : UIContainer
    {
        public enum Parenting
        {
            TooltipsCanvas = 0,
            TooltipTrigger = 1,
            UITag = 2
        }

        public enum Tracking
        {
            Disabled = 0,
            FollowPointer = 1,
            FollowTrigger = 2,
            FollowTarget = 3
        }

        [Header("Tooltip")]
        public Parenting parenting = Parenting.TooltipsCanvas;
        public Tracking tracking = Tracking.Disabled;
        public TagId targetTag = new TagId();

        [Tooltip("Seconds of hover before the tooltip shows")]
        [Min(0f)] public float showDelay = 0.5f;
        [Tooltip("Seconds after hover ends before the tooltip hides")]
        [Min(0f)] public float hideDelay = 0.1f;

        [Tooltip("Offset applied to the tracked position")]
        public Vector2 followOffset = new Vector2(0f, 24f);

        [Header("Size Constraints (0 = unconstrained)")]
        [Min(0f)] public float maxWidth;
        [Min(0f)] public float maxHeight;

        [Tooltip("Labels SetTexts writes into; discovered in children when empty")]
        public List<TMP_Text> labels = new List<TMP_Text>();

        /// <summary> The trigger currently driving this tooltip (set by UITooltipTrigger). </summary>
        public RectTransform trigger { get; set; }

        private Canvas _rootCanvas;

        public UITooltip SetTexts(params string[] texts)
        {
            if (labels.Count == 0) labels.AddRange(GetComponentsInChildren<TMP_Text>(includeInactive: true));
            for (int i = 0; i < texts.Length && i < labels.Count; i++)
            {
                if (labels[i] != null) labels[i].text = texts[i];
            }
            return this;
        }

        protected override void Awake()
        {
            base.Awake();
            ApplySizeConstraints();
        }

        protected virtual void OnEnable()
        {
            OnVisibilityChanged += HandleVisibilityChanged;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            OnVisibilityChanged -= HandleVisibilityChanged;
            TooltipTicker.Unregister(this);
        }

        private void HandleVisibilityChanged(VisibilityState state)
        {
            if (state == VisibilityState.IsShowing || state == VisibilityState.Visible)
            {
                if (tracking != Tracking.Disabled) TooltipTicker.Register(this);
                UpdateTrackedPosition();
            }
            else
            {
                TooltipTicker.Unregister(this);
            }
        }

        private void ApplySizeConstraints()
        {
            if (maxWidth > 0f)
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(rectTransform.rect.width, maxWidth));
            if (maxHeight > 0f)
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(rectTransform.rect.height, maxHeight));
        }

        internal void UpdateTrackedPosition()
        {
            switch (tracking)
            {
                case Tracking.FollowPointer:
                {
                    if (!TryGetPointerPosition(out Vector2 screenPosition)) return;
                    SetScreenPosition(screenPosition + followOffset);
                    break;
                }
                case Tracking.FollowTrigger:
                {
                    if (trigger == null) return;
                    SetWorldPosition(trigger.position, followOffset);
                    break;
                }
                case Tracking.FollowTarget:
                {
                    UITag uiTag = UITag.GetFirstTag(targetTag.Category, targetTag.Name);
                    if (uiTag == null) return;
                    SetWorldPosition(uiTag.rectTransform.position, followOffset);
                    break;
                }
            }
        }

        private void SetScreenPosition(Vector2 screenPosition)
        {
            Canvas canvas = GetRootCanvas();
            if (canvas == null) return;
            Camera worldCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)canvas.transform, screenPosition, worldCamera, out Vector2 localPoint);
            rectTransform.position = canvas.transform.TransformPoint(localPoint);
        }

        private void SetWorldPosition(Vector3 worldPosition, Vector2 offset)
        {
            rectTransform.position = worldPosition + (Vector3)offset;
        }

        private Canvas GetRootCanvas()
        {
            if (_rootCanvas == null) _rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
            return _rootCanvas;
        }

        private static bool TryGetPointerPosition(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            if (Pointer.current != null)
            {
                position = Pointer.current.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            position = UnityEngine.Input.mousePosition;
            return true;
#endif
            position = default;
            return false;
        }

        /// <summary> Shared tick driver for tooltips that track a moving anchor while visible. </summary>
        private static class TooltipTicker
        {
            private static readonly List<UITooltip> Tracked = new List<UITooltip>();
            private static readonly Driver TickDriver = new Driver();

            private sealed class Driver : ITickable
            {
                public void Tick(float deltaTime)
                {
                    for (int i = Tracked.Count - 1; i >= 0; i--)
                    {
                        UITooltip tooltip = Tracked[i];
                        if (tooltip == null) Tracked.RemoveAt(i);
                        else tooltip.UpdateTrackedPosition();
                    }
                }
            }

            public static void Register(UITooltip tooltip)
            {
                if (Tracked.Contains(tooltip)) return;
                Tracked.Add(tooltip);
                if (Tracked.Count == 1) UITick.Register(TickDriver);
            }

            public static void Unregister(UITooltip tooltip)
            {
                Tracked.Remove(tooltip);
                if (Tracked.Count == 0) UITick.Unregister(TickDriver);
            }
        }
    }
}
