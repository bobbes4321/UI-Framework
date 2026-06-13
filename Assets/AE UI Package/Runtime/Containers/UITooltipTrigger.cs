using UnityEngine;
using UnityEngine.EventSystems;

namespace AlterEyes.UI
{
    /// <summary>
    /// Shows a tooltip after hovering this element for the tooltip's show delay,
    /// hides it (after the hide delay) when the pointer leaves.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Containers/UI Tooltip Trigger")]
    public class UITooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ITickable
    {
        [Tooltip("Tooltip instance to drive (scene object)")]
        public UITooltip tooltip;

        private float _countdown = -1f;
        private bool _showing;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltip == null) return;
            tooltip.trigger = transform as RectTransform;
            if (tooltip.parenting == UITooltip.Parenting.TooltipTrigger)
                tooltip.transform.SetParent(transform, worldPositionStays: false);
            _showing = true;
            _countdown = tooltip.showDelay;
            UITick.Register(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltip == null) return;
            _showing = false;
            _countdown = tooltip.hideDelay;
            UITick.Register(this);
        }

        private void OnDisable()
        {
            UITick.Unregister(this);
            _countdown = -1f;
        }

        public void Tick(float deltaTime)
        {
            if (_countdown < 0f)
            {
                UITick.Unregister(this);
                return;
            }

            _countdown -= deltaTime;
            if (_countdown > 0f) return;

            _countdown = -1f;
            UITick.Unregister(this);
            if (_showing) tooltip.Show();
            else tooltip.Hide();
        }
    }
}
