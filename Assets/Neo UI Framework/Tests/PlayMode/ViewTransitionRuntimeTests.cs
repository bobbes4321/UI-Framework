using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Runtime net for the view-transition feature: FlowController plants a resolved
    /// ViewTransitionAsset on the outgoing/incoming UINode pair when an edge names one, and
    /// ViewTransitionRunner delays the incoming Show by <c>incomingOffset</c> instead of showing it
    /// in the same frame as the outgoing Hide. Also guards that an edge with no transition (and no
    /// project default) keeps the pre-feature immediate show/hide behavior byte-for-byte.
    /// </summary>
    public class ViewTransitionRuntimeTests
    {
        private readonly List<Object> _cleanup = new List<Object>();
        private NeoUISettings _settings;
        private ViewTransitionAsset _transition;
        private string _previousDefaultTransition;

        [SetUp]
        public void SetUp()
        {
            _settings = NeoUISettings.instance;
            Assert.IsNotNull(_settings, "the committed NeoUISettings resource must exist for this test to run");

            // determinism regardless of whatever the committed asset's project default currently is
            _previousDefaultTransition = _settings.defaultViewTransition;
            _settings.defaultViewTransition = string.Empty;

            _transition = ScriptableObject.CreateInstance<ViewTransitionAsset>();
            _transition.category = "Test";
            _transition.transitionName = "Slide";
            _transition.incomingOffset = 0.4f;
            _transition.incoming.fade.enabled = true;
            _transition.incoming.fade.settings.duration = 0.2f;
            _transition.incoming.fade.fromCustomValue = 0f;
            _transition.incoming.fade.toCustomValue = 1f;
            _transition.outgoing.fade.enabled = true;
            _transition.outgoing.fade.settings.duration = 0.1f;
            _transition.outgoing.fade.fromCustomValue = 1f;
            _transition.outgoing.fade.toCustomValue = 0f;
            _settings.viewTransitions.Add(_transition);
        }

        [TearDown]
        public void TearDown()
        {
            // leave the committed settings asset exactly as found
            _settings.viewTransitions.Remove(_transition);
            _settings.defaultViewTransition = _previousDefaultTransition;
            Object.DestroyImmediate(_transition);

            foreach (Object obj in _cleanup)
                if (obj != null) Object.Destroy(obj);
            _cleanup.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _cleanup.Add(go);
            return go;
        }

        private UIView BuildView(string category, string name)
        {
            var go = Track(new GameObject($"View_{category}_{name}", typeof(RectTransform), typeof(CanvasGroup)));
            var view = go.AddComponent<UIView>();
            view.id = new ViewId(category, name);
            // start from a real Hidden baseline — the Show()-consumes-the-start-behaviour guard
            // (UIContainer.Show sets _startBehaviourExecuted before its redundancy early-out) keeps
            // this from racing whichever node's flow-driven Show runs first.
            view.onStartBehaviour = ContainerStartBehaviour.InstantHide;
            go.AddComponent<UIContainerUIAnimator>();
            return view;
        }

        private FlowController BuildController(FlowGraph graph)
        {
            _cleanup.Add(graph);
            var controllerGo = Track(new GameObject("Controller", typeof(FlowController)));
            var controller = controllerGo.GetComponent<FlowController>();
            controller.flow = graph;
            return controller;
        }

        [UnityTest]
        public IEnumerator NamedTransition_DelaysIncomingShow_UntilOffsetElapsed()
        {
            UIView viewA = BuildView("TxCut", "A");
            UIView viewB = BuildView("TxCut", "B");

            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            StartNode start = graph.AddNode<StartNode>("Start");
            UINode nodeA = graph.AddNode<UINode>("A");
            nodeA.showViews.Add(new UINode.ViewRef("TxCut", "A"));
            nodeA.hideShownViewsOnExit = true;
            UINode nodeB = graph.AddNode<UINode>("B");
            nodeB.showViews.Add(new UINode.ViewRef("TxCut", "B"));
            start.outputs.Add(new FlowEdge { toNode = "A" });
            var edgeAB = new FlowEdge { toNode = "B", transition = "Test/Slide" };
            nodeA.outputs.Add(edgeAB);
            graph.startNode = "Start";

            FlowController controller = BuildController(graph);

            yield return null; // Start(): flow starts, Start->A, view A shows (that edge has no transition)
            yield return null;

            Assert.IsTrue(viewA.isVisible || viewA.visibilityState == VisibilityState.IsShowing,
                "precondition: node A's view must be showing before we cut to B");

            controller.Advance(edgeAB);

            Assert.IsTrue(viewB.isHidden,
                "the incoming view must not show in the same frame as the cut — it is offset-delayed");

            yield return new WaitForSeconds(0.15f);
            Assert.IsTrue(viewB.isHidden,
                "the incoming view must still be waiting out incomingOffset (0.4s) at the 0.15s mark");

            yield return new WaitForSeconds(0.6f); // well past offset(0.4) + the incoming fade(0.2)
            Assert.IsTrue(viewB.isVisible,
                "the incoming view must be fully shown once the offset and its fade have elapsed");
        }

        [UnityTest]
        public IEnumerator SlideOutHideOverride_PlainShowLater_RestoresRestPose()
        {
            // regression: a Push-style transition slides the OUTGOING view off-screen as its hide
            // override; navigating Back re-shows that view with its own animation (a fade for every
            // generated view), which never touches position — the view used to fade "in" while still
            // parked off-screen, leaving the player staring at the camera clear color.
            _transition.incomingOffset = 0f;
            _transition.outgoing.move.enabled = true;
            _transition.outgoing.move.settings.duration = 0.1f;
            _transition.outgoing.move.fromDirection = UIMoveDirection.CustomPosition;
            _transition.outgoing.move.fromReference = ReferenceValue.StartValue;
            _transition.outgoing.move.toDirection = UIMoveDirection.CustomPosition;
            _transition.outgoing.move.toReference = ReferenceValue.CustomValue;
            _transition.outgoing.move.toCustomValue = new Vector3(-1500f, 0f, 0f);

            UIView viewA = BuildView("TxPose", "A");
            UIView viewB = BuildView("TxPose", "B");

            // give A the generated-view animator shape — fade-only show/hide, the exact combination
            // that cannot undo a slide-out displacement by itself
            var animatorA = viewA.GetComponent<UIContainerUIAnimator>();
            ConfigureFade(animatorA.showAnimation, from: 0f, to: 1f);
            ConfigureFade(animatorA.hideAnimation, from: 1f, to: 0f);

            var rectA = (RectTransform)viewA.transform;
            Vector3 restPosition = rectA.anchoredPosition3D;

            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            StartNode start = graph.AddNode<StartNode>("Start");
            UINode nodeA = graph.AddNode<UINode>("A");
            nodeA.showViews.Add(new UINode.ViewRef("TxPose", "A"));
            nodeA.hideShownViewsOnExit = true;
            UINode nodeB = graph.AddNode<UINode>("B");
            nodeB.showViews.Add(new UINode.ViewRef("TxPose", "B"));
            nodeB.hideShownViewsOnExit = true;
            start.outputs.Add(new FlowEdge { toNode = "A" });
            var edgeAB = new FlowEdge { toNode = "B", transition = "Test/Slide" };
            nodeA.outputs.Add(edgeAB);
            var edgeBA = new FlowEdge { toNode = "A" }; // Back: no transition, plain show
            nodeB.outputs.Add(edgeBA);
            graph.startNode = "Start";

            FlowController controller = BuildController(graph);

            yield return null;
            yield return null;
            // let A's own 0.05s show fade fully settle before cutting away — advancing while it is
            // still mid-flight would interrupt it (OnHide's own-animation-reversal path), which
            // reverses THAT fade rather than playing the transition's hide override; this test
            // targets a clean, completed cut, not a Show/Hide interruption race.
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(viewA.isVisible, "precondition: A's own show must finish before the cut to B");

            controller.Advance(edgeAB);
            yield return new WaitForSeconds(0.4f); // past the 0.1s slide-out + 0.2s incoming fade

            Assert.IsTrue(viewA.isHidden, "precondition: A must have finished hiding");
            Assert.That(rectA.anchoredPosition3D.x, Is.EqualTo(-1500f).Within(1f),
                "sanity: the slide-out override must have parked A off-screen");

            controller.Advance(edgeBA);
            yield return new WaitForSeconds(0.4f);

            Assert.IsTrue(viewA.isVisible, "A must be visible again after Back");
            Assert.That(Vector3.Distance(rectA.anchoredPosition3D, restPosition), Is.LessThan(1f),
                "a plain show after a slide-out hide override must restore the rest pose — " +
                "not fade the view 'in' while it is still parked off-screen");
        }

        private static void ConfigureFade(UIAnimation animation, float from, float to)
        {
            animation.fade.enabled = true;
            animation.fade.settings.duration = 0.05f;
            animation.fade.fromReference = ReferenceValue.CustomValue;
            animation.fade.fromCustomValue = from;
            animation.fade.toReference = ReferenceValue.CustomValue;
            animation.fade.toCustomValue = to;
        }

        [UnityTest]
        public IEnumerator HeroFlight_ProxyKeepsSourceSize_NotOverlayStretchedSize()
        {
            // regression: the flight proxy is a clone of the source widget, and a STRETCH-anchored
            // source used to keep its anchors on the full-screen overlay canvas — sizeDelta then
            // resolved to screen size + source size, flashing a giant copy across the whole cut.
            _transition.incomingOffset = 0f;
            _transition.sharedElements = true;

            var canvasGo = Track(new GameObject("Canvas", typeof(Canvas)));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            UIView viewA = BuildView("TxHero", "A");
            UIView viewB = BuildView("TxHero", "B");
            viewA.transform.SetParent(canvasGo.transform, false);
            viewB.transform.SetParent(canvasGo.transform, false);

            // source: a 160x160 card holding a stretch-anchored hero (the showcase's exact shape)
            var cardA = new GameObject("CardA", typeof(RectTransform));
            var cardARect = (RectTransform)cardA.transform;
            cardARect.SetParent(viewA.transform, false);
            cardARect.sizeDelta = new Vector2(160f, 160f);
            var heroA = new GameObject("HeroA", typeof(RectTransform), typeof(NeoSharedElement));
            var heroARect = (RectTransform)heroA.transform;
            heroARect.SetParent(cardARect, false);
            heroARect.anchorMin = Vector2.zero;
            heroARect.anchorMax = Vector2.one;
            heroARect.sizeDelta = Vector2.zero;
            heroA.GetComponent<NeoSharedElement>().key = "hero";

            var heroB = new GameObject("HeroB", typeof(RectTransform), typeof(NeoSharedElement));
            var heroBRect = (RectTransform)heroB.transform;
            heroBRect.SetParent(viewB.transform, false);
            heroBRect.sizeDelta = new Vector2(240f, 240f);
            heroB.GetComponent<NeoSharedElement>().key = "hero";

            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            StartNode start = graph.AddNode<StartNode>("Start");
            UINode nodeA = graph.AddNode<UINode>("A");
            nodeA.showViews.Add(new UINode.ViewRef("TxHero", "A"));
            nodeA.hideShownViewsOnExit = true;
            UINode nodeB = graph.AddNode<UINode>("B");
            nodeB.showViews.Add(new UINode.ViewRef("TxHero", "B"));
            start.outputs.Add(new FlowEdge { toNode = "A" });
            var edgeAB = new FlowEdge { toNode = "B", transition = "Test/Slide" };
            nodeA.outputs.Add(edgeAB);
            graph.startNode = "Start";

            FlowController controller = BuildController(graph);

            yield return null;
            yield return null;

            controller.Advance(edgeAB);
            // offset is 0 — the flight starts synchronously inside Advance; assert before yielding
            // so a long test frame can't land the flight (and destroy the proxy) under us

            GameObject overlayGo = GameObject.Find("NeoTransitionOverlay");
            Assert.IsNotNull(overlayGo, "a shared-element cut must spawn the transition overlay canvas");
            Assert.AreEqual(1, overlayGo.transform.childCount, "exactly one hero pair, exactly one proxy");
            var proxy = (RectTransform)overlayGo.transform.GetChild(0);

            Assert.AreEqual(proxy.anchorMin, proxy.anchorMax,
                "the proxy must be point-anchored — cloned stretch anchors resolve against the full-screen overlay");
            Assert.That(proxy.rect.width, Is.EqualTo(160f).Within(1f),
                "the proxy must start at the SOURCE hero's size, not screen size + sizeDelta");
            Assert.That(proxy.rect.height, Is.EqualTo(160f).Within(1f));

            yield return new WaitForSeconds(0.6f); // past the flight duration
            Assert.IsTrue(overlayGo == null || overlayGo.transform.childCount == 0,
                "the proxy (and overlay canvas) must clean themselves up when the flight lands");
        }

        [UnityTest]
        public IEnumerator EdgeWithoutTransition_ShowsIncomingImmediately_LegacyBehavior()
        {
            UIView viewA = BuildView("TxLegacy", "A");
            UIView viewB = BuildView("TxLegacy", "B");

            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            StartNode start = graph.AddNode<StartNode>("Start");
            UINode nodeA = graph.AddNode<UINode>("A");
            nodeA.showViews.Add(new UINode.ViewRef("TxLegacy", "A"));
            UINode nodeB = graph.AddNode<UINode>("B");
            nodeB.showViews.Add(new UINode.ViewRef("TxLegacy", "B"));
            start.outputs.Add(new FlowEdge { toNode = "A" });
            var edgeAB = new FlowEdge { toNode = "B" }; // no transition, no project default configured
            nodeA.outputs.Add(edgeAB);
            graph.startNode = "Start";

            FlowController controller = BuildController(graph);

            yield return null;
            yield return null;

            controller.Advance(edgeAB);
            yield return null; // one frame is enough for an un-delayed, instantly-complete Show

            Assert.IsTrue(viewB.isVisible,
                "an edge without a transition (and no project default) must show its view immediately, " +
                "matching the pre-transition behavior");
        }
    }
}
