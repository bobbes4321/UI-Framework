using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Rainbow hue cycling: rotates the host <see cref="NeoShape"/>'s fill color HUE over the
    /// timeline (and, optionally, the gradient stop B color too) — a full-spectrum sweep with no
    /// custom shader. Tier-1 — it only writes the shape's fill <see cref="NeoShape.color"/> /
    /// <see cref="NeoShape.colorB"/> that already ride the shared SDF material's vertex channels, so
    /// the hue-shifting shape keeps batching with every other NeoShape; no new material, no fragment
    /// shader.
    ///
    /// <para>The effect is <b>idempotent</b>: it captures the host's BASE color once (in
    /// <see cref="OnEnable"/>, before the base class samples its resting frame) and rebuilds the
    /// shifted color from that snapshot every frame, so repeated <see cref="NeoShapeEffect.Sample"/>
    /// frames never compound. Saturation, value and alpha are preserved from the base color; only the
    /// hue is offset.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Hue Shift")]
    public class NeoHueShift : NeoShapeEffect
    {
        [Header("Hue offset (turns, 0..1)")]
        [Tooltip("Hue offset (in 0..1 turns) added to the base hue at phase 0.")]
        [SerializeField] private float hueFrom;

        [Tooltip("Hue offset (in 0..1 turns) added to the base hue at phase 1. 1 = a full rainbow sweep.")]
        [SerializeField] private float hueTo = 1f;

        [Tooltip("Also rotate the gradient stop B color (colorB) by the same hue offset so a " +
                 "gradient fill rainbows both stops.")]
        [SerializeField] private bool cycleColorB;

        // Base-color snapshot — the un-shifted color(s) the host had when the effect enabled. We
        // rebuild the shifted color from these every frame so repeated ApplyAt calls don't compound.
        [System.NonSerialized] private Color _baseColor = Color.white;
        [System.NonSerialized] private Color _baseColorB = Color.white;
        [System.NonSerialized] private bool _captured;

        /// <summary> Hue offset (turns) at phase 0. </summary>
        public float HueFrom { get => hueFrom; set => hueFrom = value; }
        /// <summary> Hue offset (turns) at phase 1 (1 = a full spectrum sweep). </summary>
        public float HueTo { get => hueTo; set => hueTo = value; }
        /// <summary> Whether the gradient stop B color is hue-shifted too. </summary>
        public bool CycleColorB { get => cycleColorB; set => cycleColorB = value; }

        /// <inheritdoc/>
        protected override void OnEnable()
        {
            // Capture the BASE color BEFORE base.OnEnable() — its final act is EvaluateRest(), which
            // calls ApplyAt() and overwrites the host color. If we captured after, we'd snapshot the
            // already-shifted color and the hue would drift on every enable.
            var shape = GetComponent<Graphic>() as NeoShape;
            if (shape != null)
            {
                _baseColor = shape.color;
                _baseColorB = shape.colorB;
                _captured = true;
            }

            base.OnEnable();
        }

        /// <inheritdoc/>
        protected override void OnDisable()
        {
            base.OnDisable();
            // Allow a fresh capture next time the effect enables (the host may have been recolored).
            _captured = false;
        }

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            NeoShape shape = hostShape;
            if (shape == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoHueShift)} on '{name}' needs an NeoShape host " +
                                 "to shift the fill hue — effect is inert.", this);
                return;
            }

            // Lazy capture: Sample() can run before OnEnable (e.g. a test calls it directly). Snapshot
            // the host's CURRENT color the first time so we still have an un-shifted base to rebuild from.
            if (!_captured)
            {
                _baseColor = shape.color;
                _baseColorB = shape.colorB;
                _captured = true;
            }

            float offset = Mathf.LerpUnclamped(hueFrom, hueTo, easedPhase01);

            shape.color = ShiftHue(_baseColor, offset);
            if (cycleColorB)
                shape.colorB = ShiftHue(_baseColorB, offset);
        }

        /// <summary>
        /// Returns <paramref name="baseColor"/> with its hue rotated by <paramref name="hueOffset"/>
        /// turns (wrapping 0..1), preserving the base saturation, value and alpha.
        /// </summary>
        private static Color ShiftHue(Color baseColor, float hueOffset)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            float shiftedH = Mathf.Repeat(h + hueOffset, 1f);
            Color shifted = Color.HSVToRGB(shiftedH, s, v);
            shifted.a = baseColor.a;
            return shifted;
        }

        /// <inheritdoc/>
        public override bool TrySetLiveParam(string param, float value)
        {
            switch (param)
            {
                case "hueFrom": HueFrom = value; return true;
                case "hueTo": HueTo = value; return true;
                // Convenience: pin both ends so a slider drives a static (non-cycling) hue offset.
                case "hue": HueFrom = value; HueTo = value; return true;
            }
            return base.TrySetLiveParam(param, value);
        }
    }
}
