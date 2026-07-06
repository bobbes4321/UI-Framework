using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Deep-copies <see cref="UIAnimation"/> channel data between instances (target binding and
    /// <see cref="UIAnimation.sourcePreset"/> untouched). The one shared copy routine behind
    /// <see cref="UIAnimationPreset.CopyTo"/>, the view-transition runner's per-view scratch
    /// animations and the editor's transition previews — a shared asset's UIAnimation instance is
    /// never PLAYED directly (its tweens/snapshots are live state; two views playing one instance
    /// would fight), it is always copied into a scratch first.
    /// </summary>
    public static class UIAnimationChannels
    {
        /// <summary> Copies purpose + all five channels from source into target. </summary>
        public static void Copy(UIAnimation source, UIAnimation target)
        {
            if (source == null || target == null) return;
            target.purpose = source.purpose;
            CopyMove(source.move, target.move);
            CopyRotate(source.rotate, target.rotate);
            CopyScale(source.scale, target.scale);
            CopyFade(source.fade, target.fade);
            CopyColor(source.color, target.color);
        }

        public static void CopyMove(MoveAnimation source, MoveAnimation target)
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

        public static void CopyRotate(RotateAnimation source, RotateAnimation target)
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

        public static void CopyScale(ScaleAnimation source, ScaleAnimation target)
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

        public static void CopyFade(FadeAnimation source, FadeAnimation target)
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

        public static void CopyColor(ColorAnimation source, ColorAnimation target)
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
