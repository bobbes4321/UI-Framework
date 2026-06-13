using UnityEngine;
using UnityEngine.EventSystems;

namespace AlterEyes.UI
{
    /// <summary> Hides its popup when the attached graphic is clicked (overlay / container hide-on options). </summary>
    [AddComponentMenu("")]
    public class PopupClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public UIPopup popup;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (popup != null) popup.Hide();
        }
    }
}
