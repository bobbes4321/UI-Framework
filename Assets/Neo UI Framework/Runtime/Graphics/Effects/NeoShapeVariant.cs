using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Attaches a <b>Tier-2</b> shape effect (<see cref="ShapeEffectDefinition"/>) to a host
    /// <see cref="NeoShape"/> by overriding the shape's material with the definition's ONE shared
    /// material. This is the deliberate, <b>named batch-split seam</b>: assigning a non-shared
    /// material here breaks the project-wide single-material NeoShape batch on purpose — but because
    /// every shape wearing the SAME definition gets the SAME material instance, they all batch with
    /// each other (one extra draw-call group, not one per shape).
    ///
    /// <para><b>This is NOT a per-instance material.</b> We assign
    /// <see cref="ShapeEffectDefinition.SharedMaterial"/> verbatim to <see cref="UnityEngine.UI.Graphic.material"/>;
    /// we never <c>new Material(...)</c> or read <c>Renderer.material</c> (which would instantiate a
    /// copy). Per-variant sharing is the whole point of the Tier-2 seam.</para>
    ///
    /// <para>The effect <see cref="ShapeEffectDefinition.Id"/> is stored so the editor registry can
    /// read the attached effect back for spec round-trip — see the round-trip note in the class
    /// remarks of <see cref="ShapeEffectDefinition"/>.</para>
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Neo/UI/Effects/Shape Variant (Tier-2)")]
    [RequireComponent(typeof(NeoShape))]
    public class NeoShapeVariant : MonoBehaviour
    {
        [Tooltip("The Tier-2 effect definition (its own shader + ONE shared material + param defaults). " +
                 "Ship one of these from a consuming project to add an effect with no fork.")]
        [SerializeField] private ShapeEffectDefinition definition;

        [Tooltip("Cached effect id of the attached definition — what the editor/spec round-trip reads " +
                 "back to re-resolve the definition. Updated automatically from the definition.")]
        [SerializeField] private string effectId;

        [System.NonSerialized] private NeoShape _shape;

        /// <summary> The host shape (cached). </summary>
        public NeoShape Shape => _shape != null ? _shape : (_shape = GetComponent<NeoShape>());

        /// <summary> The attached effect definition; re-applies the material when reassigned. </summary>
        public ShapeEffectDefinition Definition
        {
            get => definition;
            set
            {
                definition = value;
                Apply();
            }
        }

        /// <summary> The attached effect's stable id (for editor/spec round-trip read-back). </summary>
        public string EffectId => definition != null ? definition.Id : effectId;

        private void OnEnable() => Apply();

        private void OnDisable() => Restore();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (isActiveAndEnabled)
                Apply();
        }
#endif

        /// <summary>
        /// Assigns the definition's shared material to the host shape and pushes its default params.
        /// Warns (never fails silently) on misconfiguration.
        /// </summary>
        public void Apply()
        {
            NeoShape shape = Shape;
            if (shape == null) return; // RequireComponent guarantees one; guard for editor edge cases.

            if (definition == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoShapeVariant)} on '{name}' has no " +
                                 "ShapeEffectDefinition assigned — nothing to apply.", this);
                Restore();
                return;
            }

            effectId = definition.Id;

            Material shared = definition.SharedMaterial;
            if (shared == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoShapeVariant)} on '{name}': effect " +
                                 $"'{definition.Id}' has no shared material — falling back to the " +
                                 "default NeoShape material.", this);
                shape.material = null; // Graphic restores defaultMaterial (the shared NeoShape material).
                return;
            }

            // Deliberate, named batch split: assign the variant's SHARED material verbatim — NOT a
            // per-instance copy (no new Material, no Renderer.material). All shapes on this same
            // definition batch together.
            shape.material = shared;

            // Defaults live on the shared material itself (one resting look per variant).
            definition.ApplyDefaults(shared);

            shape.SetMaterialDirty();
        }

        /// <summary> Restores the host to the default shared NeoShape material (rejoins the main batch). </summary>
        public void Restore()
        {
            NeoShape shape = Shape;
            if (shape == null) return;
            shape.material = null; // Graphic.material == null ⇒ falls back to defaultMaterial.
            shape.SetMaterialDirty();
        }
    }
}
