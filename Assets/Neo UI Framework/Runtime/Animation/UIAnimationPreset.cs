using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A named, reusable animation configuration ("Show: SlideInLeft") — the unit agents reference
    /// in UI specs. Copying a preset onto an animator copies the data; the asset is not linked.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo/UI/Animation Preset", fileName = "AnimationPreset")]
    public class UIAnimationPreset : ScriptableObject
    {
        [Tooltip("Preset category, e.g. Show / Hide / Button / Loop")]
        public string category = "Custom";

        [Tooltip("Preset name referenced by specs, e.g. SlideInLeft")]
        public string presetName;

        public UIAnimation animation = new UIAnimation();

        public string fullName => $"{category}/{presetName}";

        /// <summary> Deep-copies this preset's channels into the given animation (target binding untouched). </summary>
        public void CopyTo(UIAnimation targetAnimation)
        {
            if (targetAnimation == null) return;
            targetAnimation.purpose = animation.purpose;
            CopyMove(animation.move, targetAnimation.move);
            CopyRotate(animation.rotate, targetAnimation.rotate);
            CopyScale(animation.scale, targetAnimation.scale);
            CopyFade(animation.fade, targetAnimation.fade);
        }

        private static void CopyMove(MoveAnimation source, MoveAnimation target)
        {
            target.enabled = source.enabled;
            target.settings.CopyFrom(source.settings);
            target.fromDirection = source.fromDirection;
            target.toDirection = source.toDirection;
            target.fromReference = source.fromReference;
            target.toReference = source.toReference;
            target.fromCustomValue = source.fromCustomValue;
            target.toCustomValue = source.toCustomValue;
            target.fromOffset = source.fromOffset;
            target.toOffset = source.toOffset;
        }

        private static void CopyRotate(RotateAnimation source, RotateAnimation target)
        {
            target.enabled = source.enabled;
            target.settings.CopyFrom(source.settings);
            target.fromReference = source.fromReference;
            target.toReference = source.toReference;
            target.fromCustomValue = source.fromCustomValue;
            target.toCustomValue = source.toCustomValue;
            target.fromOffset = source.fromOffset;
            target.toOffset = source.toOffset;
        }

        private static void CopyScale(ScaleAnimation source, ScaleAnimation target)
        {
            target.enabled = source.enabled;
            target.settings.CopyFrom(source.settings);
            target.fromReference = source.fromReference;
            target.toReference = source.toReference;
            target.fromCustomValue = source.fromCustomValue;
            target.toCustomValue = source.toCustomValue;
            target.fromOffset = source.fromOffset;
            target.toOffset = source.toOffset;
        }

        private static void CopyFade(FadeAnimation source, FadeAnimation target)
        {
            target.enabled = source.enabled;
            target.settings.CopyFrom(source.settings);
            target.fromReference = source.fromReference;
            target.toReference = source.toReference;
            target.fromCustomValue = source.fromCustomValue;
            target.toCustomValue = source.toCustomValue;
            target.fromOffset = source.fromOffset;
            target.toOffset = source.toOffset;
        }
    }
}
