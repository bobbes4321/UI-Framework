using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Opens a popup from the popup database when the UIButton on this GameObject is clicked. </summary>
    [RequireComponent(typeof(UIButton))]
    [AddComponentMenu("AlterEyes/UI/Containers/Show Popup On Click")]
    public class ShowPopupOnClick : MonoBehaviour
    {
        [Tooltip("Popup name as registered in the popup database")]
        public string popupName;

        [Tooltip("Texts written into the popup's labels before showing")]
        public string[] texts = new string[0];

        private void Awake()
        {
            GetComponent<UIButton>().onClickEvent.AddListener(Show);
        }

        public void Show()
        {
            UIPopup popup = UIPopup.Get(popupName);
            if (popup == null) return;
            if (texts != null && texts.Length > 0) popup.SetTexts(texts);
            popup.Show();
        }
    }
}
