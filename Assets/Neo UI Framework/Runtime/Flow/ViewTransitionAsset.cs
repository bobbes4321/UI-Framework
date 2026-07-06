using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A first-class, named view transition — the choreography of ONE navigation cut, owned by the
    /// edge that navigates (not by either view). Overrides the outgoing views' hide and/or the
    /// incoming views' show with its own <see cref="UIAnimation"/>s, offsets the incoming side on a
    /// shared timeline, optionally cascades the incoming view's children and optionally flies
    /// <see cref="NeoSharedElement"/>-tagged widgets across the cut ("magic move" / hero).
    /// <para>
    /// Referenced from a <see cref="FlowEdge.transition"/> by full name ("Push/SlideLeft" — flows
    /// stay string-addressed and agent-legible); resolved at runtime through
    /// <see cref="NeoUISettings.viewTransitions"/> (the explicit, runtime-loadable list the
    /// generator keeps in sync — the <c>animationPresets</c> precedent), with editor pickers
    /// discovering assets via <c>ViewTransitionRegistry</c>. A channel left disabled means "use the
    /// view's own show/hide animation" — a transition can override one side only, or add pure
    /// choreography (offset/cascade) over the views' own motion.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/View Transition", fileName = "ViewTransition")]
    public class ViewTransitionAsset : ScriptableObject
    {
        [Tooltip("Transition category, e.g. Push / Fade / Modal")]
        public string category = "Custom";

        [Tooltip("Transition name referenced by flow edges, e.g. SlideLeft")]
        public string transitionName;

        [Tooltip("Played on each OUTGOING view instead of its own hide animation. " +
                 "Leave every channel disabled to keep the view's own hide.")]
        public UIAnimation outgoing = new UIAnimation { purpose = AnimationPurpose.Hide };

        [Tooltip("Played on each INCOMING view instead of its own show animation. " +
                 "Leave every channel disabled to keep the view's own show.")]
        public UIAnimation incoming = new UIAnimation { purpose = AnimationPurpose.Show };

        [Tooltip("Seconds after the outgoing side starts before the incoming side starts. " +
                 "0 = parallel; the outgoing duration = fully sequential.")]
        [Min(0f)] public float incomingOffset;

        [Tooltip("Optional stagger over the incoming view's direct children (the UICascadeChildren feel, " +
                 "scoped to this one transition)")]
        public CascadeSpec incomingCascade = new CascadeSpec();

        [Tooltip("Fly widgets tagged with a matching NeoSharedElement key across the cut instead of " +
                 "hiding/showing them with their views")]
        public bool sharedElements = true;

        /// <summary> "Category/Name" — the string flow edges and specs reference. </summary>
        public string fullName => $"{category}/{transitionName}";

        /// <summary> Whether the given animation overrides anything (any channel enabled). </summary>
        public static bool Overrides(UIAnimation animation) =>
            animation != null && (animation.move.enabled || animation.rotate.enabled
                                  || animation.scale.enabled || animation.fade.enabled
                                  || animation.color.enabled);

        /// <summary>
        /// Full choreography length in seconds: the outgoing side plus the offset incoming side,
        /// whichever runs longer. Sides that don't override contribute 0 here (the views' own
        /// animations run on their own clocks).
        /// </summary>
        public float totalDuration =>
            Mathf.Max(Overrides(outgoing) ? outgoing.totalDuration : 0f,
                incomingOffset + (Overrides(incoming) ? incoming.totalDuration : 0f));

        /// <summary> Per-transition child stagger for the incoming view (UICascadeChildren's trio). </summary>
        [Serializable]
        public class CascadeSpec
        {
            public bool enabled;
            [Tooltip("Delay added per child (seconds)")]
            [Min(0f)] public float stagger = 0.04f;
            [Tooltip("Fade duration per child")]
            [Min(0f)] public float itemDuration = 0.25f;
            public Ease ease = Ease.OutQuad;
        }
    }
}
