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

        /// <summary>
        /// Deep-copies this preset's channels into the given animation (target binding untouched) and
        /// stamps <see cref="UIAnimation.sourcePreset"/> with this preset's full name so the inspector
        /// can show where a slot's data came from.
        /// </summary>
        public void CopyTo(UIAnimation targetAnimation)
        {
            if (targetAnimation == null) return;
            targetAnimation.sourcePreset = fullName;
            targetAnimation.purpose = animation.purpose;
            CopyMove(animation.move, targetAnimation.move);
            CopyRotate(animation.rotate, targetAnimation.rotate);
            CopyScale(animation.scale, targetAnimation.scale);
            CopyFade(animation.fade, targetAnimation.fade);
            CopyColor(animation.color, targetAnimation.color);
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

        private static void CopyColor(ColorAnimation source, ColorAnimation target)
        {
            target.enabled = source.enabled;
            target.settings.CopyFrom(source.settings);
            CopyColorEndpoint(source.from, target.from);
            CopyColorEndpoint(source.to, target.to);
        }

        private static void CopyColorEndpoint(ColorAnimationEndpoint source, ColorAnimationEndpoint target)
        {
            target.reference = source.reference;
            target.customColor = source.customColor;
            target.themeToken = source.themeToken;
        }
    }
}
