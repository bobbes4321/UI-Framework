using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A named float material parameter default for a <see cref="ShapeEffectDefinition"/>.
    /// </summary>
    [Serializable]
    public struct ShapeEffectFloatParam
    {
        /// <summary> Shader property name (e.g. "_DissolveAmount"). </summary>
        public string name;
        /// <summary> Default value applied to the variant's material. </summary>
        public float value;
    }

    /// <summary>
    /// A named color material parameter default for a <see cref="ShapeEffectDefinition"/>.
    /// </summary>
    [Serializable]
    public struct ShapeEffectColorParam
    {
        /// <summary> Shader property name (e.g. "_EdgeColor"). </summary>
        public string name;
        /// <summary> Default value applied to the variant's material. </summary>
        public Color value;
    }

    /// <summary>
    /// Describes a <b>Tier-2</b> shape effect — a heavy fragment effect (dissolve, scanline,
    /// displacement, …) that genuinely needs its own shader and therefore breaks the single shared
    /// NeoShape batch. Tier-2 is the explicit, opt-in counterpart to the Tier-1 driver
    /// (<see cref="NeoShapeEffect"/>): batching is sacrificed deliberately and the cost is bounded —
    /// the material is shared <b>per variant</b>, never per instance, so N shapes wearing the same
    /// effect still batch <em>with each other</em> (one extra draw-call group, not N).
    ///
    /// <para>This ScriptableObject is the consuming-project <b>extension seam</b>: a project ships a
    /// <c>ShapeEffectDefinition</c> asset (its own shader + that shader's shared material + param
    /// defaults) and an <see cref="NeoShapeVariant"/> referencing it to add a brand-new effect with
    /// no fork of the package. The set of effects stays open — there is no enum or switch to edit.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Shape Effect Definition", fileName = "NeoShapeEffect")]
    public class ShapeEffectDefinition : ScriptableObject
    {
        [Tooltip("Stable, agent-addressable id (never a GUID). The variant stores this for round-trip " +
                 "read-back and the editor registry keys on it.")]
        [SerializeField] private string id;

        [Tooltip("Human-readable name shown in editor dropdowns.")]
        [SerializeField] private string displayName;

        [Tooltip("The ONE shared material for this variant. Every NeoShapeVariant pointing at this " +
                 "definition assigns THIS exact material — so they all batch together. Never a " +
                 "per-instance copy.")]
        [SerializeField] private Material sharedMaterial;

        [Tooltip("False for Tier-2 effects: they break the single shared NeoShape batch (they batch " +
                 "per variant instead). Surfaced so tooling/lint can flag the batch split honestly.")]
        [SerializeField] private bool batchSafe;

        [Tooltip("Default float shader params applied to a shape when the variant is attached.")]
        [SerializeField] private List<ShapeEffectFloatParam> floatParams = new List<ShapeEffectFloatParam>();

        [Tooltip("Default color shader params applied to a shape when the variant is attached.")]
        [SerializeField] private List<ShapeEffectColorParam> colorParams = new List<ShapeEffectColorParam>();

        /// <summary> Stable, agent-addressable id (never a GUID); falls back to the asset name when blank. </summary>
        public string Id => string.IsNullOrEmpty(id) ? name : id;

        /// <summary> Human-readable name; falls back to <see cref="Id"/> when blank. </summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? Id : displayName;

        /// <summary>
        /// The ONE shared material for this variant — assigned verbatim to every wearing shape so
        /// they batch together. Never copy it per instance.
        /// </summary>
        public Material SharedMaterial => sharedMaterial;

        /// <summary> False for Tier-2 effects (they break the single shared NeoShape batch). </summary>
        public bool BatchSafe => batchSafe;

        /// <summary> Default float shader params. </summary>
        public IReadOnlyList<ShapeEffectFloatParam> FloatParams => floatParams;

        /// <summary> Default color shader params. </summary>
        public IReadOnlyList<ShapeEffectColorParam> ColorParams => colorParams;

        /// <summary>
        /// Pushes this definition's default params onto a material. The material is expected to be
        /// the shared one (so the defaults are the variant's resting look); per-instance overrides
        /// are intentionally NOT supported here — that would defeat the per-variant batching.
        /// </summary>
        public void ApplyDefaults(Material target)
        {
            if (target == null) return;
            for (int i = 0; i < floatParams.Count; i++)
            {
                ShapeEffectFloatParam p = floatParams[i];
                if (!string.IsNullOrEmpty(p.name) && target.HasProperty(p.name))
                    target.SetFloat(p.name, p.value);
            }
            for (int i = 0; i < colorParams.Count; i++)
            {
                ShapeEffectColorParam p = colorParams[i];
                if (!string.IsNullOrEmpty(p.name) && target.HasProperty(p.name))
                    target.SetColor(p.name, p.value);
            }
        }
    }
}
