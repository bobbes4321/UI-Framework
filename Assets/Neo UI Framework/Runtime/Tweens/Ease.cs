// Neo UI Package — tween core
namespace Neo.UI
{
    /// <summary> Easing functions available to tweens. Mirrors the standard Penner set plus Spring. </summary>
    public enum Ease
    {
        Linear = 0,

        InSine, OutSine, InOutSine,
        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc,
        InBack, OutBack, InOutBack,
        InElastic, OutElastic, InOutElastic,
        InBounce, OutBounce, InOutBounce,

        Spring
    }

    /// <summary> How a tween evaluates progress: a named <see cref="Ease"/> or a custom AnimationCurve. </summary>
    public enum EaseMode
    {
        Ease = 0,
        AnimationCurve = 1
    }
}
