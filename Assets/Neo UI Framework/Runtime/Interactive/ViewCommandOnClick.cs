using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Serializable "onClick: show/hide view" wiring (used by the spec generator so view commands
    /// stay data, not UnityEvent references). Hooks the UIButton on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(UIButton))]
    [AddComponentMenu("Neo/UI/Interactive/View Command On Click")]
    public class ViewCommandOnClick : MonoBehaviour
    {
        public enum Command
        {
            Show = 0,
            Hide = 1,
            Toggle = 2
        }

        public Command command = Command.Show;
        public ViewId view = new ViewId();

        private void Awake()
        {
            GetComponent<UIButton>().onClickEvent.AddListener(Execute);
        }

        public void Execute()
        {
            switch (command)
            {
                case Command.Show:
                    UIView.Show(view.Category, view.Name);
                    break;
                case Command.Hide:
                    UIView.Hide(view.Category, view.Name);
                    break;
                case Command.Toggle:
                    UIView first = UIView.GetFirstView(view.Category, view.Name);
                    if (first != null) first.Toggle();
                    else UIView.Show(view.Category, view.Name);
                    break;
            }
        }
    }
}
