using static UnityEngine.Mathf;

namespace Neo.UI
{
    /// <summary>
    /// Pure easing math. All functions map progress t in [0,1] to an eased value
    /// (eased(0) == 0 and eased(1) == 1 for every ease; Back/Elastic overshoot in between).
    /// </summary>
    public static class Easing
    {
        public static float Evaluate(Ease ease, float t)
        {
            t = Clamp01(t);
            switch (ease)
            {
                case Ease.Linear: return t;

                case Ease.InSine: return 1f - Cos(t * PI * 0.5f);
                case Ease.OutSine: return Sin(t * PI * 0.5f);
                case Ease.InOutSine: return 0.5f * (1f - Cos(t * PI));

                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return t * (2f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

                case Ease.InCubic: return t * t * t;
                case Ease.OutCubic: { float p = t - 1f; return p * p * p + 1f; }
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : (t - 1f) * (2f * t - 2f) * (2f * t - 2f) + 1f;

                case Ease.InQuart: return t * t * t * t;
                case Ease.OutQuart: { float p = t - 1f; return 1f - p * p * p * p; }
                case Ease.InOutQuart:
                {
                    if (t < 0.5f) return 8f * t * t * t * t;
                    float p = t - 1f;
                    return 1f - 8f * p * p * p * p;
                }

                case Ease.InQuint: return t * t * t * t * t;
                case Ease.OutQuint: { float p = t - 1f; return p * p * p * p * p + 1f; }
                case Ease.InOutQuint:
                {
                    if (t < 0.5f) return 16f * t * t * t * t * t;
                    float p = t - 1f;
                    return 16f * p * p * p * p * p + 1f;
                }

                case Ease.InExpo: return Approximately(t, 0f) ? 0f : Pow(2f, 10f * (t - 1f));
                case Ease.OutExpo: return Approximately(t, 1f) ? 1f : 1f - Pow(2f, -10f * t);
                case Ease.InOutExpo:
                {
                    if (Approximately(t, 0f)) return 0f;
                    if (Approximately(t, 1f)) return 1f;
                    return t < 0.5f
                        ? 0.5f * Pow(2f, 20f * t - 10f)
                        : 1f - 0.5f * Pow(2f, -20f * t + 10f);
                }

                case Ease.InCirc: return 1f - Sqrt(1f - t * t);
                case Ease.OutCirc: return Sqrt((2f - t) * t);
                case Ease.InOutCirc:
                {
                    return t < 0.5f
                        ? 0.5f * (1f - Sqrt(1f - 4f * t * t))
                        : 0.5f * (Sqrt(-(2f * t - 3f) * (2f * t - 1f)) + 1f);
                }

                // Back: overshoot cubic, y = x^3 - x * sin(x * pi)  (Doozy-compatible)
                case Ease.InBack: return BackIn(t);
                case Ease.OutBack: return 1f - BackIn(1f - t);
                case Ease.InOutBack:
                {
                    if (t < 0.5f) return 0.5f * BackIn(2f * t);
                    return 0.5f * (1f - BackIn(1f - (2f * t - 1f))) + 0.5f;
                }

                // Elastic: exponentially damped sine (Doozy-compatible)
                case Ease.InElastic: return ElasticIn(t);
                case Ease.OutElastic: return Sin(-13f * PI * 0.5f * (t + 1f)) * Pow(2f, -10f * t) + 1f;
                case Ease.InOutElastic:
                {
                    if (t < 0.5f) return 0.5f * ElasticIn(2f * t);
                    float p = 2f * t - 1f;
                    return 0.5f * (Sin(-13f * PI * 0.5f * (p + 1f)) * Pow(2f, -10f * p) + 2f);
                }

                case Ease.InBounce: return 1f - BounceOut(1f - t);
                case Ease.OutBounce: return BounceOut(t);
                case Ease.InOutBounce:
                {
                    return t < 0.5f
                        ? 0.5f * (1f - BounceOut(1f - 2f * t))
                        : 0.5f * BounceOut(2f * t - 1f) + 0.5f;
                }

                // Spring: oscillation with damping, settles at 1 (Doozy-compatible)
                case Ease.Spring:
                {
                    float p = Sin(t * PI * (0.2f + 2.5f * t * t * t)) * Pow(1f - t, 2.2f) + t;
                    return p * (1f + 1.2f * (1f - t));
                }

                default: return t;
            }
        }

        private static float BackIn(float t) => t * t * t - t * Sin(t * PI);

        private static float ElasticIn(float t) => Sin(13f * PI * 0.5f * t) * Pow(2f, 10f * (t - 1f));

        private static float BounceOut(float t)
        {
            if (t < 4f / 11f) return 121f * t * t / 16f;
            if (t < 8f / 11f) return 363f / 40f * t * t - 99f / 10f * t + 17f / 5f;
            if (t < 9f / 10f) return 4356f / 361f * t * t - 35442f / 1805f * t + 16061f / 1805f;
            return 54f / 5f * t * t - 513f / 25f * t + 268f / 25f;
        }
    }
}
