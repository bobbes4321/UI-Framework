using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary> Drives an Image's fillAmount from a progressor. </summary>
    [AddComponentMenu("Neo/UI/Progress Targets/Image Fill Progress Target")]
    public class ImageProgressTarget : ProgressTarget
    {
        public Image image;

        private void Reset() => image = GetComponent<Image>();

        public override void UpdateTarget(Progressor progressor)
        {
            if (image == null) return;
            image.fillAmount = Mathf.Clamp01(Pick(progressor));
        }
    }
}
