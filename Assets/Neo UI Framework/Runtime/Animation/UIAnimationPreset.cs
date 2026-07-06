using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A named, reusable animation configuration ("Show: SlideInLeft") — the unit agents reference
    /// in UI specs. Copying a preset onto an animator copies the data; the asset is not linked.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Animation Preset", fileName = "AnimationPreset")]
    public class UIAnimationPreset : ScriptableObject
    {
        [Tooltip("Preset category, e.g. Show / Hide / Button / Loop")]
        public string category = "Custom";

        [Tooltip("Preset name referenced by specs, e.g. SlideInLeft")]
        public string presetName;

        public UIAnimation animation = new UIAnimation();

        public string fullName => $"{category}/{presetName}";

        /// <summary>
        /// Deep-copies this preset's channels into the given animation (target binding untouched) and
        /// stamps <see cref="UIAnimation.sourcePreset"/> with this preset's full name so the inspector
        /// can show where a slot's data came from.
        /// </summary>
        public void CopyTo(UIAnimation targetAnimation)
        {
            if (targetAnimation == null) return;
            targetAnimation.sourcePreset = fullName;
            UIAnimationChannels.Copy(animation, targetAnimation);
        }
    }
}
