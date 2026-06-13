using TMPro;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Notification badge (count pill or plain dot) pinned to a widget corner.
    /// <see cref="SetCount"/> drives both the label and visibility — zero hides the badge.
    /// The serialized <see cref="count"/> is the baked/exported state (WYSIWYG).
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/Badge")]
    public class UIBadge : MonoBehaviour
    {
        [Tooltip("Badge count; 0 hides the badge, counts above 99 render as 99+")]
        public int count = 1;
        [Tooltip("Optional count label; leave empty for a plain dot")]
        public TMP_Text label;

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        public void SetCount(int newCount)
        {
            count = Mathf.Max(0, newCount);
            Apply();
        }

        private void Apply()
        {
            if (label != null) label.text = count > 99 ? "99+" : count.ToString();
            // toggle the visual root, not this component's lifecycle
            foreach (Transform child in transform) child.gameObject.SetActive(count > 0);
            var graphic = GetComponent<UnityEngine.UI.Graphic>();
            if (graphic != null) graphic.enabled = count > 0;
            if (label != null) label.enabled = count > 0;
        }
    }
}
