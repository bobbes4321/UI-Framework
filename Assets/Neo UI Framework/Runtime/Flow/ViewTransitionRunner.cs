using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Executes a resolved <see cref="ViewTransitionAsset"/>'s incoming-side choreography: the
    /// offset-delayed Show, the incoming view's child cascade, and — when the transition wants it —
    /// shared-element "hero" flights between <see cref="NeoSharedElement"/> pairs on the outgoing and
    /// incoming views. Planted by <c>UINode.OnEnter</c>, one call per shown view.
    /// <para>
    /// Play-mode only: outside <see cref="Application.isPlaying"/> (edit mode, and the synchronous
    /// EditMode flow-playthrough tests) a scheduled show fires immediately with no choreography,
    /// matching the pre-transition behavior exactly (WYSIWYG — a transition never changes what the
    /// editor shows).
    /// </para>
    /// </summary>
    internal static class ViewTransitionRunner
    {
        private sealed class PendingShow
        {
            public string category;
            public string viewName;
            public ViewTransitionAsset asset;
            public List<UINode.ViewRef> outgoingViews;
            public float remaining;
        }

        private sealed class HeroFlight
        {
            public RectTransform proxy;
            public RectTransform source;
            public RectTransform target;
            public CanvasGroup sourceGroup;
            public float sourcePriorAlpha;
            public CanvasGroup targetGroup;
            public float targetPriorAlpha;
            public Vector3 startCenter;
            public Vector3 endCenter;
            public Vector2 startSize;
            public Vector2 endSize;
            public float duration;
            public float elapsed;
        }

        private sealed class Tickable : ITickable
        {
            public void Tick(float deltaTime) => ViewTransitionRunner.Tick(deltaTime);
        }

        private const int OverlaySortingOrder = 32760;
        private const float MinHeroDuration = 0.15f;
        private const float DefaultHeroDuration = 0.3f;

        private static readonly List<PendingShow> Pending = new List<PendingShow>();
        private static readonly List<HeroFlight> Flights = new List<HeroFlight>();
        private static readonly Vector3[] CornersScratch = new Vector3[4];
        private static readonly Tickable RunnerTickable = new Tickable();

        private static Canvas _overlayCanvas;

        /// <summary>
        /// Schedules the incoming side of a resolved transition for one shown view. Cancels any
        /// transition work already in flight first — a second navigation starting before the first
        /// settles must never leave the earlier one's views stuck mid-flight or invisible.
        /// </summary>
        public static void ScheduleShow(string category, string viewName, ViewTransitionAsset asset,
            List<UINode.ViewRef> outgoingViews)
        {
            if (asset == null)
            {
                UIView.Show(category, viewName);
                return;
            }

            CancelAll();

            if (!Application.isPlaying)
            {
                // edit mode / synchronous flow tests: no choreography, exact legacy behavior
                UIView.Show(category, viewName);
                return;
            }

            float offset = Mathf.Max(0f, asset.incomingOffset);
            if (offset <= 0f)
            {
                ExecuteShow(category, viewName, asset, outgoingViews);
                return;
            }

            Pending.Add(new PendingShow
            {
                category = category,
                viewName = viewName,
                asset = asset,
                outgoingViews = outgoingViews,
                remaining = offset
            });
            UITick.Register(RunnerTickable);
        }

        /// <summary>
        /// Flushes every pending/in-flight transition immediately: a scheduled show fires right now
        /// WITHOUT its override (a skipped Show would leave a view permanently invisible — the one
        /// outcome worse than an early one), and hero flights snap to their end position and clean
        /// up. Called before a new batch schedules so an interrupted navigation never strands the
        /// previous one's choreography.
        /// </summary>
        public static void CancelAll()
        {
            if (Pending.Count > 0)
            {
                var due = new List<PendingShow>(Pending);
                Pending.Clear();
                foreach (PendingShow show in due) UIView.Show(show.category, show.viewName);
            }

            if (Flights.Count > 0)
            {
                var flights = new List<HeroFlight>(Flights);
                Flights.Clear();
                foreach (HeroFlight flight in flights) CompleteFlight(flight, snapToEnd: true);
            }

            if (Pending.Count == 0 && Flights.Count == 0) UITick.Unregister(RunnerTickable);
        }

        private static void Tick(float deltaTime)
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingShow show = Pending[i];
                show.remaining -= deltaTime;
                if (show.remaining > 0f) continue;
                Pending.RemoveAt(i);
                ExecuteShow(show.category, show.viewName, show.asset, show.outgoingViews);
            }

            for (int i = Flights.Count - 1; i >= 0; i--)
            {
                HeroFlight flight = Flights[i];
                if (flight.source == null || flight.target == null || flight.proxy == null)
                {
                    // the view or shared element died mid-flight — abort cleanly rather than animate garbage
                    Flights.RemoveAt(i);
                    CompleteFlight(flight, snapToEnd: false);
                    continue;
                }

                flight.elapsed += deltaTime;
                float t = flight.duration > 0f ? Mathf.Clamp01(flight.elapsed / flight.duration) : 1f;
                float eased = Easing.Evaluate(Ease.OutCubic, t);
                flight.proxy.position = Vector3.LerpUnclamped(flight.startCenter, flight.endCenter, eased);
                flight.proxy.sizeDelta = Vector2.LerpUnclamped(flight.startSize, flight.endSize, eased);

                if (t >= 1f)
                {
                    Flights.RemoveAt(i);
                    CompleteFlight(flight, snapToEnd: true);
                }
            }

            if (Pending.Count == 0 && Flights.Count == 0) UITick.Unregister(RunnerTickable);
        }

        // ------------------------------------------------------------------ show + cascade

        private static void ExecuteShow(string category, string viewName, ViewTransitionAsset asset,
            List<UINode.ViewRef> outgoingViews)
        {
            UIView.Show(category, viewName, ViewTransitionAsset.Overrides(asset.incoming) ? asset.incoming : null);

            UIView incoming = UIView.GetFirstView(category, viewName);
            if (incoming == null) return; // no instance in this scene — nothing further to choreograph

            if (asset.incomingCascade.enabled) RunCascade(incoming, asset.incomingCascade);
            if (asset.sharedElements && outgoingViews != null && outgoingViews.Count > 0)
                StartHeroFlights(incoming, outgoingViews, asset);
        }

        /// <summary> The UICascadeChildren feel, scoped to one transition's own stagger/duration/ease. </summary>
        private static void RunCascade(UIView view, ViewTransitionAsset.CascadeSpec spec)
        {
            int index = 0;
            foreach (Transform child in view.transform)
            {
                if (child == view.transform) continue; // guard: never cascade the view root itself
                if (!child.gameObject.activeSelf) continue;

                CanvasGroup group = child.GetComponent<CanvasGroup>();
                if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();

                group.alpha = 0f;
                var tween = new FloatTween();
                tween.SetTarget(group, () => group.alpha, value => group.alpha = value);
                tween.SetFrom(0f);
                tween.SetTo(1f);
                tween.settings.duration = spec.itemDuration;
                tween.settings.ease = spec.ease;
                tween.settings.startDelay = index * spec.stagger;
                tween.Play(); // self-registers with UITick; the runner doesn't need to track it
                index++;
            }
        }

        // ------------------------------------------------------------------ shared-element hero flights

        private static void StartHeroFlights(UIView incoming, List<UINode.ViewRef> outgoingViews, ViewTransitionAsset asset)
        {
            Dictionary<string, NeoSharedElement> outgoingByKey = null;
            foreach (UINode.ViewRef outRef in outgoingViews)
            {
                UIView outgoing = UIView.GetFirstView(outRef.category, outRef.viewName);
                if (outgoing == null || outgoing == incoming) continue;
                CollectSharedElements(outgoing.transform, ref outgoingByKey);
            }
            if (outgoingByKey == null || outgoingByKey.Count == 0) return;

            Dictionary<string, NeoSharedElement> incomingByKey = null;
            CollectSharedElements(incoming.transform, ref incomingByKey);
            if (incomingByKey == null || incomingByKey.Count == 0) return;

            Canvas.ForceUpdateCanvases(); // outgoing/incoming rects must be laid out before we measure them

            float duration = Mathf.Max(MinHeroDuration, asset.totalDuration > 0f ? asset.totalDuration : DefaultHeroDuration);
            foreach (KeyValuePair<string, NeoSharedElement> pair in incomingByKey)
            {
                if (!outgoingByKey.TryGetValue(pair.Key, out NeoSharedElement source)) continue;
                NeoSharedElement target = pair.Value;
                if (source == null || target == null) continue;
                BeginFlight(source, target, duration);
            }
        }

        private static void CollectSharedElements(Transform root, ref Dictionary<string, NeoSharedElement> map)
        {
            // GetComponentsInChildren allocates — accepted here: once per navigation cut, not per-frame
            NeoSharedElement[] found = root.GetComponentsInChildren<NeoSharedElement>(includeInactive: false);
            foreach (NeoSharedElement element in found)
            {
                if (string.IsNullOrEmpty(element.key)) continue;
                map ??= new Dictionary<string, NeoSharedElement>();
                map[element.key] = element; // duplicate keys within one view: last one wins, an authoring issue
            }
        }

        private static void BeginFlight(NeoSharedElement source, NeoSharedElement target, float duration)
        {
            var sourceRect = source.transform as RectTransform;
            var targetRect = target.transform as RectTransform;
            if (sourceRect == null || targetRect == null) return;

            Canvas overlay = EnsureOverlayCanvas();
            GameObject proxyGo = Object.Instantiate(source.gameObject, overlay.transform);
            proxyGo.name = $"{source.name} (Transition Proxy)";

            NeoSharedElement proxyMarker = proxyGo.GetComponent<NeoSharedElement>();
            if (proxyMarker != null) Object.Destroy(proxyMarker);
            foreach (UIContainer container in proxyGo.GetComponentsInChildren<UIContainer>(true))
                Object.Destroy(container);

            var proxyRect = (RectTransform)proxyGo.transform;
            // The clone keeps the source's anchors. A stretch-anchored hero would resolve its
            // sizeDelta RELATIVE to the full-screen overlay canvas (screen size + sizeDelta — a
            // giant flash across the whole cut), so pin the proxy to a point anchor first: from
            // here on sizeDelta IS its absolute pixel size.
            proxyRect.anchorMin = proxyRect.anchorMax = new Vector2(0.5f, 0.5f);
            proxyRect.pivot = new Vector2(0.5f, 0.5f);

            CanvasGroup proxyBlocker = proxyGo.GetComponent<CanvasGroup>();
            if (proxyBlocker == null) proxyBlocker = proxyGo.AddComponent<CanvasGroup>();
            proxyBlocker.blocksRaycasts = false;
            proxyBlocker.interactable = false;

            Vector3 startCenter = RectCenter(sourceRect);
            Vector3 endCenter = RectCenter(targetRect);
            // World-corner sizes, not rect.size: the source/target live under a (possibly
            // CanvasScaler-scaled) scene canvas while the overlay canvas is unscaled — world
            // units ARE overlay pixels, local rect sizes are not.
            Vector2 startSize = WorldSize(sourceRect);
            Vector2 endSize = WorldSize(targetRect);

            proxyRect.position = startCenter;
            proxyRect.sizeDelta = startSize;

            CanvasGroup sourceGroup = EnsureCanvasGroup(sourceRect.gameObject);
            CanvasGroup targetGroup = EnsureCanvasGroup(targetRect.gameObject);
            float sourcePriorAlpha = sourceGroup.alpha;
            float targetPriorAlpha = targetGroup.alpha;
            sourceGroup.alpha = 0f;
            targetGroup.alpha = 0f;

            Flights.Add(new HeroFlight
            {
                proxy = proxyRect,
                source = sourceRect,
                target = targetRect,
                sourceGroup = sourceGroup,
                sourcePriorAlpha = sourcePriorAlpha,
                targetGroup = targetGroup,
                targetPriorAlpha = targetPriorAlpha,
                startCenter = startCenter,
                endCenter = endCenter,
                startSize = startSize,
                endSize = endSize,
                duration = duration,
                elapsed = 0f
            });
            UITick.Register(RunnerTickable);
        }

        private static void CompleteFlight(HeroFlight flight, bool snapToEnd)
        {
            if (snapToEnd && flight.proxy != null)
            {
                flight.proxy.position = flight.endCenter;
                flight.proxy.sizeDelta = flight.endSize;
            }
            if (flight.sourceGroup != null) flight.sourceGroup.alpha = flight.sourcePriorAlpha;
            if (flight.targetGroup != null) flight.targetGroup.alpha = flight.targetPriorAlpha;
            if (flight.proxy != null) Object.Destroy(flight.proxy.gameObject);

            if (Flights.Count == 0 && _overlayCanvas != null)
            {
                Object.Destroy(_overlayCanvas.gameObject);
                _overlayCanvas = null;
            }
        }

        private static Canvas EnsureOverlayCanvas()
        {
            if (_overlayCanvas != null) return _overlayCanvas;
            var go = new GameObject("NeoTransitionOverlay", typeof(RectTransform), typeof(Canvas));
            Object.DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = OverlaySortingOrder;
            _overlayCanvas = canvas;
            return canvas;
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
        }

        private static Vector3 RectCenter(RectTransform rect)
        {
            rect.GetWorldCorners(CornersScratch);
            return (CornersScratch[0] + CornersScratch[2]) * 0.5f;
        }

        private static Vector2 WorldSize(RectTransform rect)
        {
            rect.GetWorldCorners(CornersScratch); // corners: bottom-left, top-left, top-right, bottom-right
            return new Vector2(
                Vector3.Distance(CornersScratch[0], CornersScratch[3]),
                Vector3.Distance(CornersScratch[0], CornersScratch[1]));
        }
    }
}
