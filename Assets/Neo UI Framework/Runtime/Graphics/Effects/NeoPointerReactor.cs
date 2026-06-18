using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// A pointer-position reactor: a soft "glow follows the cursor" highlight that sits exactly
    /// under the mouse while it hovers this element, and is hidden the moment the pointer leaves.
    /// Delivers the mobile-game "spotlight exactly where my finger/mouse is" feel — a follow-glow.
    ///
    /// <para>Batch-safe: like <see cref="NeoGlowPulse"/> it touches nothing the shared SDF material
    /// cares about — it only moves a child <see cref="NeoShape"/> around and sets its color +
    /// edge softness, so the follower stays on the one shared NeoShape material and keeps batching.
    /// No new material, no shader variant.</para>
    ///
    /// <para>Play-mode only: every hook early-outs unless <see cref="Application.isPlaying"/>, and
    /// the follower is spawned lazily on the first hover at runtime. The editor preview is therefore
    /// completely unchanged (WYSIWYG — no follower object appears in edit mode).</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Pointer Reactor")]
    [RequireComponent(typeof(RectTransform))]
    public class NeoPointerReactor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        [Tooltip("Tint of the follow-glow. A soft, semi-transparent white reads as a neutral spotlight.")]
        [SerializeField] private Color glowColor = new Color(1f, 1f, 1f, 0.5f);

        [Tooltip("Diameter of the spawned glow follower, in canvas px.")]
        [SerializeField] private float glowSize = 120f;

        [Tooltip("Edge blur of the glow follower, in canvas px (maps to NeoShape.edgeSoftness).")]
        [SerializeField] private float glowSoftness = 40f;

        [Tooltip("When no follower is assigned, lazily spawn a NeoShape glow child on first hover.")]
        [SerializeField] private bool spawnFollower = true;

        [Tooltip("Optional follower to drive. If null and Spawn Follower is on, one is created at runtime.")]
        [SerializeField] private RectTransform follower;

        private RectTransform _rect;
        private bool _spawnedFollower; // we own (and must destroy) the follower
        private bool _hovering;

        // ------------------------------------------------------------------ properties

        /// <summary> Tint of the follow-glow (alpha included). </summary>
        public Color GlowColor
        {
            get => glowColor;
            set
            {
                glowColor = value;
                if (follower != null && follower.TryGetComponent(out NeoShape shape)) shape.color = value;
            }
        }

        /// <summary> Diameter of the spawned glow follower in canvas px. </summary>
        public float GlowSize
        {
            get => glowSize;
            set
            {
                glowSize = value;
                if (follower != null) follower.sizeDelta = new Vector2(value, value);
            }
        }

        /// <summary> Edge blur of the glow follower in canvas px (drives NeoShape.edgeSoftness). </summary>
        public float GlowSoftness
        {
            get => glowSoftness;
            set
            {
                glowSoftness = value;
                if (follower != null && follower.TryGetComponent(out NeoShape shape)) shape.edgeSoftness = value;
            }
        }

        /// <summary> When true and no follower is assigned, one is created lazily on first hover. </summary>
        public bool SpawnFollower
        {
            get => spawnFollower;
            set => spawnFollower = value;
        }

        /// <summary>
        /// The follower driven under the cursor. Assign an existing <see cref="RectTransform"/> to
        /// reuse it; leave null (with <see cref="SpawnFollower"/>) to let the reactor spawn its own.
        /// </summary>
        public RectTransform Follower
        {
            get => follower;
            set
            {
                follower = value;
                _spawnedFollower = false; // an externally-assigned follower is not ours to destroy
            }
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            _rect = (RectTransform)transform;
            // Pointer events only reach us through a raycastable Graphic; opt the host in.
            if (TryGetComponent(out Graphic hostGraphic)) hostGraphic.raycastTarget = true;
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying) return;
            // Only tear down a follower we spawned ourselves; never destroy a borrowed one.
            if (_spawnedFollower && follower != null) Destroy(follower.gameObject);
        }

        // ------------------------------------------------------------------ pointer

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Application.isPlaying) return;
            _hovering = true;
            EnsureFollower();
            if (follower == null) return;
            follower.gameObject.SetActive(true);
            MoveTo(eventData);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!Application.isPlaying || !_hovering || follower == null) return;
            MoveTo(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!Application.isPlaying) return;
            _hovering = false;
            if (follower != null) follower.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ internals

        /// <summary>
        /// Converts the pointer's screen position into this rect's local space and parks the
        /// follower there. Uses the event's <see cref="PointerEventData.enterEventCamera"/> so it
        /// resolves correctly under Screen Space - Camera / World Space canvases too.
        /// </summary>
        private void MoveTo(PointerEventData eventData)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect, eventData.position, eventData.enterEventCamera, out Vector2 local))
            {
                follower.anchoredPosition = local;
            }
        }

        /// <summary>
        /// Lazily creates the glow follower on first hover (play mode only): a centered, non-
        /// raycastable <see cref="NeoShape"/> circle so it never eats the host's pointer events,
        /// drawn last so it sits above the host content.
        /// </summary>
        private void EnsureFollower()
        {
            if (follower != null || !spawnFollower) return;

            // Clip the glow to the host bounds so it reads as a spotlight ON the element instead of a
            // free-floating blob overflowing its edges. RectMask2D is the cheap (no-stencil) clip and
            // only affects the child follower (the host's own full-rect shape is unaffected).
            if (!TryGetComponent<RectMask2D>(out _)) gameObject.AddComponent<RectMask2D>();

            var go = new GameObject("PointerGlow", typeof(RectTransform));
            var shape = go.AddComponent<NeoShape>();
            shape.shape = ShapeType.Circle;
            shape.color = glowColor;
            shape.edgeSoftness = glowSoftness;
            shape.raycastTarget = false; // must not block the host's pointer events

            follower = (RectTransform)go.transform;
            follower.SetParent(_rect, worldPositionStays: false);
            follower.anchorMin = follower.anchorMax = follower.pivot = new Vector2(0.5f, 0.5f);
            follower.sizeDelta = new Vector2(glowSize, glowSize);
            follower.anchoredPosition = Vector2.zero;
            follower.SetAsLastSibling(); // render above host content

            _spawnedFollower = true;
        }
    }
}
