using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Hides the parent container (popup close buttons, view dismiss buttons). </summary>
    [RequireComponent(typeof(UIButton))]
    [AddComponentMenu("AlterEyes/UI/Containers/Hide Container On Click")]
    public class HideContainerOnClick : MonoBehaviour
    {
        [Tooltip("Container to hide; found in parents when empty")]
        public UIContainer container;

        private void Awake()
        {
            if (container == null) container = GetComponentInParent<UIContainer>();
            GetComponent<UIButton>().onClickEvent.AddListener(Hide);
        }

        public void Hide()
        {
            if (container != null) container.Hide();
        }
    }
}
