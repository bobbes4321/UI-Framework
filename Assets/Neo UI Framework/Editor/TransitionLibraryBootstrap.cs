using Neo.UI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Seeds a curated, 8-transition library of default <see cref="ViewTransitionAsset"/>s so a fresh
    /// project has ready-to-use navigation choreography to reference by full name from a
    /// <see cref="FlowEdge.transition"/> — the view-transition sibling of
    /// <see cref="AnimationLibraryBootstrap"/> (same conventions: one flat output folder, create-missing-
    /// only idempotence, ALL fields configured before <see cref="AssetDatabase.CreateAsset"/> so a
    /// seed-then-query flow never sees a half-initialized instance). Spans three categories —
    /// <c>Fade</c> (cross-fade, through-black), <c>Push</c> (four-way slide, iOS/Android-style) and
    /// <c>Modal</c> (zoom-in, sheet-up) — landing under <see cref="LibraryRoot"/> and auto-discovered via
    /// <see cref="ViewTransitionRegistry"/> (no database wiring).
    /// </summary>
    public static class TransitionLibraryBootstrap
    {
        public const string LibraryRoot = "Assets/Neo UI Framework/Transitions";

        [MenuItem("Tools/Neo UI/Setup/Create or Repair Transition Library", priority = 105)]
        public static void CreateOrRepairMenu()
        {
            int created = CreateOrRepair();
            Debug.Log($"[Neo.UI] Transition library (8 curated transitions): {created} transition(s) created (others already present).");
        }

        /// <summary> Creates any missing default transitions. Returns how many were created. </summary>
        public static int CreateOrRepair()
        {
            EnsureFolder(LibraryRoot);
            int n = 0;

            // ---- Fade -------------------------------------------------------------------------------
            n += Ensure("Fade", "Cross", t =>
            {
                ConfigureFadeOut(t.outgoing, 0.20f, Ease.OutQuad);
                ConfigureFadeIn(t.incoming, 0.25f, Ease.OutQuad);
                t.incomingOffset = 0f;
            });
            n += Ensure("Fade", "ThroughBlack", t =>
            {
                ConfigureFadeOut(t.outgoing, 0.15f, Ease.InQuad);
                ConfigureFadeIn(t.incoming, 0.25f, Ease.OutQuad);
                t.incomingOffset = 0.15f; // sequential: outgoing fully finishes before incoming starts
            });

            // ---- Push (iOS/Android-style directional slide, old view slides out as new slides in) ----
            n += Ensure("Push", "SlideLeft", t =>
            {
                ConfigureMoveOut(t.outgoing, UIMoveDirection.Left, 0.28f, Ease.InOutCubic);
                ConfigureFadeOut(t.outgoing, 0.28f, Ease.InOutCubic);
                ConfigureMoveIn(t.incoming, UIMoveDirection.Right, 0.30f, Ease.OutCubic);
                ConfigureFadeIn(t.incoming, 0.30f, Ease.OutCubic);
                t.incomingOffset = 0.05f;
            });
            n += Ensure("Push", "SlideRight", t =>
            {
                ConfigureMoveOut(t.outgoing, UIMoveDirection.Right, 0.28f, Ease.InOutCubic);
                ConfigureFadeOut(t.outgoing, 0.28f, Ease.InOutCubic);
                ConfigureMoveIn(t.incoming, UIMoveDirection.Left, 0.30f, Ease.OutCubic);
                ConfigureFadeIn(t.incoming, 0.30f, Ease.OutCubic);
                t.incomingOffset = 0.05f;
            });
            n += Ensure("Push", "SlideUp", t =>
            {
                ConfigureMoveOut(t.outgoing, UIMoveDirection.Top, 0.28f, Ease.InOutCubic);
                ConfigureFadeOut(t.outgoing, 0.28f, Ease.InOutCubic);
                ConfigureMoveIn(t.incoming, UIMoveDirection.Bottom, 0.30f, Ease.OutCubic);
                ConfigureFadeIn(t.incoming, 0.30f, Ease.OutCubic);
                t.incomingOffset = 0.05f;
                t.incomingCascade.enabled = true;
                t.incomingCascade.stagger = 0.04f;
                t.incomingCascade.itemDuration = 0.22f;
                t.incomingCascade.ease = Ease.OutQuad;
            });
            n += Ensure("Push", "SlideDown", t =>
            {
                ConfigureMoveOut(t.outgoing, UIMoveDirection.Bottom, 0.28f, Ease.InOutCubic);
                ConfigureFadeOut(t.outgoing, 0.28f, Ease.InOutCubic);
                ConfigureMoveIn(t.incoming, UIMoveDirection.Top, 0.30f, Ease.OutCubic);
                ConfigureFadeIn(t.incoming, 0.30f, Ease.OutCubic);
                t.incomingOffset = 0.05f;
            });

            // ---- Modal ------------------------------------------------------------------------------
            n += Ensure("Modal", "ZoomIn", t =>
            {
                // outgoing stays fully disabled — the underlying view plays its own hide animation
                // (or none), this transition only choreographs the incoming modal.
                ConfigureScaleIn(t.incoming, new Vector3(0.85f, 0.85f, 1f), 0.25f, Ease.OutBack);
                ConfigureFadeIn(t.incoming, 0.25f, Ease.OutBack);
                t.incomingOffset = 0f;
            });
            n += Ensure("Modal", "SheetUp", t =>
            {
                ConfigureFadeOut(t.outgoing, 0.15f, Ease.InQuad);
                ConfigureMoveIn(t.incoming, UIMoveDirection.Bottom, 0.30f, Ease.OutCubic);
                ConfigureFadeIn(t.incoming, 0.30f, Ease.OutCubic);
                t.incomingOffset = 0f;
            });

            if (n > 0) AssetDatabase.SaveAssets();
            ViewTransitionRegistry.InvalidateDiscovery();
            return n;
        }

        // ---------------------------------------------------------------- channel builders (mirror AnimationLibraryBootstrap)

        private static void ConfigureFadeOut(UIAnimation a, float duration, Ease ease)
        {
            a.fade.enabled = true;
            a.fade.settings = new TweenSettings { duration = duration, ease = ease };
            a.fade.fromReference = ReferenceValue.CustomValue;
            a.fade.toReference = ReferenceValue.CustomValue;
            a.fade.fromCustomValue = 1f;
            a.fade.toCustomValue = 0f;
        }

        private static void ConfigureFadeIn(UIAnimation a, float duration, Ease ease)
        {
            a.fade.enabled = true;
            a.fade.settings = new TweenSettings { duration = duration, ease = ease };
            a.fade.fromReference = ReferenceValue.CustomValue;
            a.fade.toReference = ReferenceValue.CustomValue;
            a.fade.fromCustomValue = 0f;
            a.fade.toCustomValue = 1f;
        }

        // Move the OUTGOING view off-screen toward `to`, starting from wherever it currently sits.
        private static void ConfigureMoveOut(UIAnimation a, UIMoveDirection to, float duration, Ease ease)
        {
            a.move.enabled = true;
            a.move.settings = new TweenSettings { duration = duration, ease = ease };
            a.move.fromDirection = UIMoveDirection.CustomPosition;
            a.move.fromReference = ReferenceValue.StartValue;
            a.move.toDirection = to;
        }

        // Move the INCOMING view on-screen from `from`, settling at its own rest position.
        private static void ConfigureMoveIn(UIAnimation a, UIMoveDirection from, float duration, Ease ease)
        {
            a.move.enabled = true;
            a.move.settings = new TweenSettings { duration = duration, ease = ease };
            a.move.fromDirection = from;
            a.move.toDirection = UIMoveDirection.CustomPosition;
            a.move.toReference = ReferenceValue.StartValue;
        }

        private static void ConfigureScaleIn(UIAnimation a, Vector3 from, float duration, Ease ease)
        {
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = duration, ease = ease };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = from;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = Vector3.one;
        }

        // ---------------------------------------------------------------- asset io

        private static int Ensure(string category, string transitionName, System.Action<ViewTransitionAsset> configure)
        {
            string path = $"{LibraryRoot}/{category}_{transitionName}.asset";
            if (AssetDatabase.LoadAssetAtPath<ViewTransitionAsset>(path) != null) return 0; // don't clobber

            var transition = ScriptableObject.CreateInstance<ViewTransitionAsset>();
            // Configure every field BEFORE CreateAsset — seed-then-query flows (this method's own
            // Register call, tests) must never observe a half-initialized instance.
            transition.category = category;
            transition.transitionName = transitionName;
            transition.sharedElements = true;
            configure(transition);

            AssetDatabase.CreateAsset(transition, path);
            ViewTransitionRegistry.Register(transition);
            return 1;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
