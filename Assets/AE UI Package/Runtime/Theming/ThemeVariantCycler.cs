using System.Collections.Generic;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Cycles the active theme through its variants (Dark → Light → …) on button click. </summary>
    [RequireComponent(typeof(UIButton))]
    [AddComponentMenu("AlterEyes/UI/Theming/Theme Variant Cycler")]
    public class ThemeVariantCycler : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<UIButton>().onClickEvent.AddListener(Cycle);
        }

        public void Cycle()
        {
            Theme theme = ThemeService.activeTheme;
            if (theme == null || theme.Variants.Count < 2) return;

            IReadOnlyList<Theme.ThemeVariant> variants = theme.Variants;
            int index = 0;
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i].name != theme.ActiveVariantName) continue;
                index = i;
                break;
            }
            theme.ActiveVariantName = variants[(index + 1) % variants.Count].name;
        }
    }
}
