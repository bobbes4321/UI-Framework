using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Animates a NAMED material <c>float</c> property over the inherited 0..1 timeline — the
    /// <b>general driver for Tier-2</b> shape variants. Where Tier-1 effects animate fields the
    /// shared SDF shader already reads (so batching survives), a Tier-2 variant
    /// (<see cref="NeoShapeVariant"/>) wears its OWN shader/material; the only way to move such an
    /// effect over time is to drive a material parameter. This component is that seam: point it at
    /// any shader float (<c>_DissolveAmount</c>, a distortion strength, a fill, …) and it lerps the
    /// property between <see cref="FromValue"/> and <see cref="ToValue"/> as the timeline runs.
    /// Dissolve is just the first user — nothing here is dissolve-specific.
    ///
    /// <para><b>Material safety (the whole point).</b> A variant's material is the definition's ONE
    /// committed, SHARED asset (see <see cref="NeoShapeVariant"/>). Writing
    /// <see cref="Material.SetFloat(string,float)"/> on that shared instance would dirty the .mat,
    /// bleed across every shape wearing the variant, and — in the editor — corrupt a committed asset.
    /// So at <b>runtime only</b> we lazily <c>new Material(shared)</c> the first time we animate and
    /// assign that per-instance clone to the host graphic, then SetFloat on the clone. A per-instance
    /// material is honest here: Tier-2 already opted out of the project-wide batch
    /// (<see cref="ShapeEffectDefinition.BatchSafe"/> == false), a deliberate, cost-lint-flagged
    /// split. The clone is destroyed in <see cref="OnDisable"/>/<see cref="OnDestroy"/> so it never
    /// leaks.</para>
    ///
    /// <para><b>Edit mode is a no-op on materials.</b> When not playing we never instance or write a
    /// material — the baked static value (whatever <see cref="ShapeEffectDefinition.ApplyDefaults"/>
    /// put on the shared material) is what the prefab shows, keeping WYSIWYG and never dirtying the
    /// committed asset. The resting frame the generator bakes is therefore the variant's static look,
    /// and the animation only comes alive at runtime.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Material Float Cycle (Tier-2)")]
    public class NeoMaterialFloatCycle : NeoShapeEffect
    {
        [Tooltip("Shader float property to animate, e.g. \"_DissolveAmount\". Must exist on the host " +
                 "graphic's (variant) material — a missing property warns once and the effect is inert.")]
        [SerializeField] private string propertyName = "_DissolveAmount";

        [Tooltip("Property value at timeline phase 0.")]
        [SerializeField] private float fromValue;

        [Tooltip("Property value at timeline phase 1.")]
        [SerializeField] private float toValue = 1f;

        [System.NonSerialized] private Material _instance;
        [System.NonSerialized] private bool _missingWarned;

        /// <summary> Shader float property this effect drives (e.g. "_DissolveAmount"). </summary>
        public string PropertyName { get => propertyName; set => propertyName = value; }
        /// <summary> Property value at timeline phase 0. </summary>
        public float FromValue { get => fromValue; set => fromValue = value; }
        /// <summary> Property value at timeline phase 1. </summary>
        public float ToValue { get => toValue; set => toValue = value; }

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            // Edit mode: never instance or write a material — leave the baked static value so the
            // prefab stays WYSIWYG and no committed asset is dirtied (see class remarks).
            if (!Application.isPlaying)
                return;

            if (string.IsNullOrEmpty(propertyName))
                return;

            Graphic g = hostGraphic;
            if (g == null)
                return;

            Material mat = ResolveInstanceMaterial(g);
            if (mat == null)
                return;

            if (!mat.HasProperty(propertyName))
            {
                if (!_missingWarned)
                {
                    Debug.LogWarning($"[Neo.UI] {nameof(NeoMaterialFloatCycle)} on '{name}': material " +
                                     $"'{mat.name}' has no float property '{propertyName}' — effect is inert.", this);
                    _missingWarned = true;
                }
                return;
            }

            mat.SetFloat(propertyName, Mathf.LerpUnclamped(fromValue, toValue, easedPhase01));
        }

        /// <summary>
        /// Lazily clones the host's SHARED material into a per-instance copy (once) and assigns it to
        /// the graphic, so SetFloat never touches the shared/committed asset. Runtime-only; returns
        /// null when the host has no material to animate.
        /// </summary>
        private Material ResolveInstanceMaterial(Graphic g)
        {
            // Already cloned and still wired to the host ⇒ reuse it.
            if (_instance != null && g.material == _instance)
                return _instance;

            Material shared = g.material;
            if (shared == null)
                return null;

            // Clone the shared variant material so animating this instance can't bleed across the
            // others (or dirty the committed .mat). Tier-2 already broke the batch, so a per-instance
            // material is the honest cost here.
            _instance = new Material(shared) { name = shared.name + " (Cycle Instance)" };
            g.material = _instance;
            return _instance;
        }

        /// <summary> Destroys the per-instance clone (if any) so it never leaks. </summary>
        private void DisposeInstance()
        {
            if (_instance == null)
                return;

            // Restore the host to the shared material before destroying the clone so a re-enable
            // re-clones cleanly from the variant's shared asset rather than a dangling instance.
            Graphic g = hostGraphic;
            if (g != null && g.material == _instance)
            {
                var variant = GetComponent<NeoShapeVariant>();
                g.material = variant != null && variant.Definition != null
                    ? variant.Definition.SharedMaterial
                    : null;
            }

            if (Application.isPlaying) Destroy(_instance);
            else DestroyImmediate(_instance);
            _instance = null;
        }

        /// <inheritdoc/>
        protected override void OnDisable()
        {
            base.OnDisable(); // restores the resting frame (a no-op on materials in edit mode)
            DisposeInstance();
        }

        private void OnDestroy() => DisposeInstance();
    }
}
