using Neo.UI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Seeds a curated, ~40-preset library of default <see cref="UIAnimationPreset"/> assets so a fresh
    /// project HAS ready-to-use motion to reference by name — the package previously shipped none. The
    /// library spans utilitarian → wacky across seven categories — <c>Show</c> / <c>Hide</c> (entry/exit
    /// fades, four-way slides, zoom, spin, drop-bounce), <c>Hover</c> / <c>Press</c> / <c>Click</c>
    /// (game-feel scales, tints, tilts, flips, jello, rubber-band, punch), <c>Toggle</c> (on/off pops +
    /// knob nudge + tints) and <c>Loop</c> (pulse, heartbeat, breathe, bob, spin, shimmer) — so the
    /// per-state inspector picker (which groups by these exact category strings) has a meaningful menu in
    /// every slot. Entry/exit presets build on the runtime's own <see cref="UIAnimation.ApplyPurposeDefaults"/>
    /// (known-good durations/eases); the rest hand-author the Move/Rotate/Scale/Fade/Color channels with
    /// tasteful, game-feel-correct timing. They land under <see cref="LibraryRoot"/> and auto-discover via
    /// <see cref="AnimationPresetRegistry"/> (no database wiring). Create-missing-only: a re-run fills gaps
    /// and never clobbers a preset you've tweaked.
    /// </summary>
    public static class AnimationLibraryBootstrap
    {
        public const string LibraryRoot = "Assets/Neo UI Framework/Animations";

        [MenuItem("Tools/Neo UI/Setup/Create or Repair Animation Library", priority = 104)]
        public static void CreateOrRepairMenu()
        {
            int created = CreateOrRepair();
            Debug.Log($"[Neo.UI] Animation library (~40 curated presets): {created} preset(s) created (others already present).");
        }

        /// <summary> Creates any missing default presets. Returns how many were created. </summary>
        public static int CreateOrRepair()
        {
            EnsureFolder(LibraryRoot);
            int n = 0;

            // ---- Show: entry transitions ------------------------------------------------------------
            n += Ensure("Show", "FadeIn", Show(move: false));
            n += Ensure("Show", "SlideInLeft", Show(UIMoveDirection.Left));
            n += Ensure("Show", "SlideInRight", Show(UIMoveDirection.Right));
            n += Ensure("Show", "SlideInUp", Show(UIMoveDirection.Bottom));
            n += Ensure("Show", "SlideInDown", Show(UIMoveDirection.Top));
            n += Ensure("Show", "ScalePopIn", ScalePopIn());
            n += Ensure("Show", "ZoomIn", ZoomIn());
            n += Ensure("Show", "SpinIn", SpinIn());
            n += Ensure("Show", "DropInBounce", DropInBounce());

            // ---- Hide: exit transitions -------------------------------------------------------------
            n += Ensure("Hide", "FadeOut", Hide(move: false));
            n += Ensure("Hide", "SlideOutLeft", Hide(UIMoveDirection.Left));
            n += Ensure("Hide", "SlideOutRight", Hide(UIMoveDirection.Right));
            n += Ensure("Hide", "SlideOutUp", Hide(UIMoveDirection.Top));
            n += Ensure("Hide", "SlideOutDown", Hide(UIMoveDirection.Bottom));
            n += Ensure("Hide", "ZoomOut", ZoomOut());
            n += Ensure("Hide", "SpinOut", SpinOut());

            // ---- Hover: rest → hovered (un-hover restores cleanly) ----------------------------------
            n += Ensure("Hover", "ScaleUp", ScaleTo(new Vector3(1.06f, 1.06f, 1f), 0.12f, Ease.OutQuad));
            n += Ensure("Hover", "ScaleUpBig", ScaleTo(new Vector3(1.12f, 1.12f, 1f), 0.16f, Ease.OutBack));
            n += Ensure("Hover", "ScaleDown", ScaleTo(new Vector3(0.94f, 0.94f, 1f), 0.12f, Ease.OutQuad));
            n += Ensure("Hover", "LiftUp", MoveOffset(new Vector3(0f, 6f, 0f), 0.14f, Ease.OutQuad));
            n += Ensure("Hover", "TiltLeft", RotateTo(new Vector3(0f, 0f, 4f), 0.14f, Ease.OutQuad));
            n += Ensure("Hover", "TiltRight", RotateTo(new Vector3(0f, 0f, -4f), 0.14f, Ease.OutQuad));
            n += Ensure("Hover", "TintPrimary", TintTo("Primary", 0.15f));
            n += Ensure("Hover", "GlowPulse", GlowPulse());

            // ---- Press: pointer-down feedback -------------------------------------------------------
            n += Ensure("Press", "PressDip", ScaleTo(new Vector3(0.95f, 0.95f, 1f), 0.08f, Ease.OutQuad));
            n += Ensure("Press", "PressPop", ScaleTo(new Vector3(0.92f, 0.92f, 1f), 0.08f, Ease.OutQuad));
            n += Ensure("Press", "SquashStretch", ScaleTo(new Vector3(1.1f, 0.9f, 1f), 0.1f, Ease.OutQuad));
            n += Ensure("Press", "FlashTint", TintTo("PrimaryHover", 0.08f));
            n += Ensure("Press", "PressLift", MoveOffset(new Vector3(0f, -2f, 0f), 0.08f, Ease.OutQuad));

            // ---- Click: one-shot wacky --------------------------------------------------------------
            n += Ensure("Click", "Spin360", RotateTo(new Vector3(0f, 0f, 360f), 0.5f, Ease.OutCubic));
            n += Ensure("Click", "Backflip", RotateTo(new Vector3(360f, 0f, 0f), 0.6f, Ease.OutCubic));
            n += Ensure("Click", "BarrelRoll", RotateTo(new Vector3(0f, 360f, 0f), 0.6f, Ease.OutCubic));
            n += Ensure("Click", "Jello", Jello());
            n += Ensure("Click", "RubberBand", ScaleFromTo(new Vector3(0.8f, 0.8f, 1f), Vector3.one, 0.6f, Ease.OutElastic));
            n += Ensure("Click", "Wobble", Wobble());
            n += Ensure("Click", "PunchScale", ScaleTo(new Vector3(1.25f, 1.25f, 1f), 0.3f, Ease.OutBack));
            n += Ensure("Click", "ColorCycle", ColorCycle());

            // ---- Toggle: on/off state changes -------------------------------------------------------
            n += Ensure("Toggle", "ToggleOnPop", ToggleOnPop());
            n += Ensure("Toggle", "ToggleOffFade", ToggleOffFade());
            n += Ensure("Toggle", "KnobNudge", MoveOffset(new Vector3(8f, 0f, 0f), 0.15f, Ease.OutBack));
            n += Ensure("Toggle", "TintOn", TintTo("Primary", 0.15f));

            // ---- Loop: ambient, infinite ------------------------------------------------------------
            n += Ensure("Loop", "Pulse", Purpose(AnimationPurpose.Loop));
            n += Ensure("Loop", "Heartbeat", Heartbeat());
            n += Ensure("Loop", "Breathe", Breathe());
            n += Ensure("Loop", "Bob", Bob());
            n += Ensure("Loop", "SpinLoop", SpinLoop());
            n += Ensure("Loop", "Shimmer", Shimmer());

            if (n > 0) AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
            return n;
        }

        // ---------------------------------------------------------------- builders (reuse runtime defaults)

        private static UIAnimation Purpose(AnimationPurpose purpose)
        {
            var a = new UIAnimation();
            a.ApplyPurposeDefaults(purpose);
            return a;
        }

        // Show: the runtime's Show default (slide-from-Left + fade-in). Override the entry direction, or
        // drop the move entirely for a pure fade.
        private static UIAnimation Show(UIMoveDirection from)
        {
            var a = Purpose(AnimationPurpose.Show);
            a.move.fromDirection = from;
            return a;
        }

        private static UIAnimation Show(bool move)
        {
            var a = Purpose(AnimationPurpose.Show);
            a.move.enabled = move;
            return a;
        }

        private static UIAnimation Hide(UIMoveDirection to)
        {
            var a = Purpose(AnimationPurpose.Hide);
            a.move.toDirection = to;
            return a;
        }

        private static UIAnimation Hide(bool move)
        {
            var a = Purpose(AnimationPurpose.Hide);
            a.move.enabled = move;
            return a;
        }

        private static UIAnimation ScalePopIn()
        {
            var a = Purpose(AnimationPurpose.Show);
            a.move.enabled = false; // pop, not slide
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = 0.3f, ease = Ease.OutBack };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = new Vector3(0.85f, 0.85f, 1f);
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = Vector3.one;
            return a; // keeps the fade-in from the Show default
        }

        // ---------------------------------------------------------------- Show / Hide (extra)

        // Zoom in: scale from small + fade in (no slide).
        private static UIAnimation ZoomIn()
        {
            var a = Purpose(AnimationPurpose.Show);
            a.move.enabled = false;
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = 0.35f, ease = Ease.OutBack };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = new Vector3(0.6f, 0.6f, 1f);
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = Vector3.one;
            return a;
        }

        // Spin in: rotate from -180° + fade + scale up.
        private static UIAnimation SpinIn()
        {
            var a = Purpose(AnimationPurpose.Show);
            a.move.enabled = false;
            a.rotate.enabled = true;
            a.rotate.settings = new TweenSettings { duration = 0.5f, ease = Ease.OutCubic };
            a.rotate.fromReference = ReferenceValue.CustomValue;
            a.rotate.fromCustomValue = new Vector3(0f, 0f, -180f);
            a.rotate.toReference = ReferenceValue.CustomValue;
            a.rotate.toCustomValue = Vector3.zero;
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = 0.5f, ease = Ease.OutBack };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = new Vector3(0.5f, 0.5f, 1f);
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = Vector3.one;
            return a;
        }

        // Drop in from the top with a bounce on landing.
        private static UIAnimation DropInBounce()
        {
            var a = Show(UIMoveDirection.Top);
            a.move.settings = new TweenSettings { duration = 0.7f, ease = Ease.OutBounce };
            return a;
        }

        // Zoom out: scale down + fade out.
        private static UIAnimation ZoomOut()
        {
            var a = Purpose(AnimationPurpose.Hide);
            a.move.enabled = false;
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = 0.3f, ease = Ease.InBack };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = Vector3.one;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = new Vector3(0.6f, 0.6f, 1f);
            return a;
        }

        // Spin out: rotate to 180° + fade + scale down.
        private static UIAnimation SpinOut()
        {
            var a = Purpose(AnimationPurpose.Hide);
            a.move.enabled = false;
            a.rotate.enabled = true;
            a.rotate.settings = new TweenSettings { duration = 0.5f, ease = Ease.InCubic };
            a.rotate.fromReference = ReferenceValue.CustomValue;
            a.rotate.fromCustomValue = Vector3.zero;
            a.rotate.toReference = ReferenceValue.CustomValue;
            a.rotate.toCustomValue = new Vector3(0f, 0f, 180f);
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = 0.5f, ease = Ease.InBack };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = Vector3.one;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = new Vector3(0.5f, 0.5f, 1f);
            return a;
        }

        // ---------------------------------------------------------------- generic single-channel builders

        // Scale from the captured rest state to an explicit target. Un-hover/release restores cleanly
        // because the rest endpoint is StartValue.
        private static UIAnimation ScaleTo(Vector3 to, float duration, Ease ease)
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = duration, ease = ease };
            a.scale.fromReference = ReferenceValue.StartValue;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = to;
            return a;
        }

        // Scale between two explicit endpoints (for elastic/overshoot one-shots).
        private static UIAnimation ScaleFromTo(Vector3 from, Vector3 to, float duration, Ease ease)
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = duration, ease = ease };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = from;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = to;
            return a;
        }

        // Rotate from rest to an explicit Euler target.
        private static UIAnimation RotateTo(Vector3 to, float duration, Ease ease)
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.rotate.enabled = true;
            a.rotate.settings = new TweenSettings { duration = duration, ease = ease };
            a.rotate.fromReference = ReferenceValue.StartValue;
            a.rotate.toReference = ReferenceValue.CustomValue;
            a.rotate.toCustomValue = to;
            return a;
        }

        // Move by an offset from the captured rest position (StartValue + toOffset), so it restores cleanly.
        private static UIAnimation MoveOffset(Vector3 offset, float duration, Ease ease)
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.move.enabled = true;
            a.move.settings = new TweenSettings { duration = duration, ease = ease };
            a.move.fromDirection = UIMoveDirection.CustomPosition;
            a.move.toDirection = UIMoveDirection.CustomPosition;
            a.move.fromReference = ReferenceValue.StartValue;
            a.move.toReference = ReferenceValue.StartValue;
            a.move.toOffset = offset;
            return a;
        }

        // Tint from the rest color to a theme token.
        private static UIAnimation TintTo(string themeToken, float duration)
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.color.enabled = true;
            a.color.settings = new TweenSettings { duration = duration, ease = Ease.OutQuad };
            a.color.from = new ColorAnimationEndpoint { reference = ColorReference.StartColor };
            a.color.to = new ColorAnimationEndpoint { reference = ColorReference.ThemeToken, themeToken = themeToken };
            return a;
        }

        // ---------------------------------------------------------------- Hover / Press (composite)

        // Attention hover: a small pulse paired with a tint toward PrimaryHover, looping forever.
        private static UIAnimation GlowPulse()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings
            {
                duration = 0.6f,
                ease = Ease.InOutSine,
                playMode = TweenPlayMode.PingPong,
                loops = TweenSettings.InfiniteLoops
            };
            a.scale.fromReference = ReferenceValue.StartValue;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = new Vector3(1.04f, 1.04f, 1f);
            a.color.enabled = true;
            a.color.settings = new TweenSettings
            {
                duration = 0.6f,
                ease = Ease.InOutSine,
                playMode = TweenPlayMode.PingPong,
                loops = TweenSettings.InfiniteLoops
            };
            a.color.from = new ColorAnimationEndpoint { reference = ColorReference.StartColor };
            a.color.to = new ColorAnimationEndpoint { reference = ColorReference.ThemeToken, themeToken = "PrimaryHover" };
            return a;
        }

        // ---------------------------------------------------------------- Click (wacky one-shots)

        // Jello: a Shake on scale for a gelatinous wobble that settles back to rest.
        private static UIAnimation Jello()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings
            {
                duration = 0.6f,
                ease = Ease.OutQuad,
                playMode = TweenPlayMode.Shake,
                strength = 0.3f,
                vibration = 10,
                fadeOutShake = true
            };
            a.scale.fromReference = ReferenceValue.StartValue;
            a.scale.toReference = ReferenceValue.StartValue;
            return a;
        }

        // Wobble: a Shake on Z rotation that settles back to rest.
        private static UIAnimation Wobble()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.rotate.enabled = true;
            a.rotate.settings = new TweenSettings
            {
                duration = 0.6f,
                ease = Ease.OutQuad,
                playMode = TweenPlayMode.Shake,
                strength = 15f,
                vibration = 10,
                fadeOutShake = true
            };
            a.rotate.fromReference = ReferenceValue.StartValue;
            a.rotate.toReference = ReferenceValue.CustomValue;
            a.rotate.toCustomValue = new Vector3(0f, 0f, 1f); // shake axis (Z)
            return a;
        }

        // Color cycle: ping-pong tint toward Success and back, a couple of times.
        private static UIAnimation ColorCycle()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Custom };
            a.color.enabled = true;
            a.color.settings = new TweenSettings
            {
                duration = 0.4f,
                ease = Ease.InOutSine,
                playMode = TweenPlayMode.PingPong,
                loops = 2
            };
            a.color.from = new ColorAnimationEndpoint { reference = ColorReference.StartColor };
            a.color.to = new ColorAnimationEndpoint { reference = ColorReference.ThemeToken, themeToken = "Success" };
            return a;
        }

        // ---------------------------------------------------------------- Toggle

        // Toggle on: scale-pop in + fade in.
        private static UIAnimation ToggleOnPop()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.State };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = 0.25f, ease = Ease.OutBack };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = new Vector3(0.7f, 0.7f, 1f);
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = Vector3.one;
            a.fade.enabled = true;
            a.fade.settings = new TweenSettings { duration = 0.2f, ease = Ease.OutQuad };
            a.fade.fromReference = ReferenceValue.CustomValue;
            a.fade.toReference = ReferenceValue.CustomValue;
            a.fade.fromCustomValue = 0f;
            a.fade.toCustomValue = 1f;
            return a;
        }

        // Toggle off: scale + fade out.
        private static UIAnimation ToggleOffFade()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.State };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings { duration = 0.2f, ease = Ease.InQuad };
            a.scale.fromReference = ReferenceValue.CustomValue;
            a.scale.fromCustomValue = Vector3.one;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = new Vector3(0.7f, 0.7f, 1f);
            a.fade.enabled = true;
            a.fade.settings = new TweenSettings { duration = 0.2f, ease = Ease.InQuad };
            a.fade.fromReference = ReferenceValue.CustomValue;
            a.fade.toReference = ReferenceValue.CustomValue;
            a.fade.fromCustomValue = 1f;
            a.fade.toCustomValue = 0f;
            return a;
        }

        // ---------------------------------------------------------------- Loop (ambient, infinite)

        // Heartbeat: a quick two-beat-feeling scale ping-pong (short, snappy OutQuad).
        private static UIAnimation Heartbeat()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Loop };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings
            {
                duration = 0.22f,
                ease = Ease.OutQuad,
                playMode = TweenPlayMode.PingPong,
                loops = TweenSettings.InfiniteLoops,
                loopDelay = 0.5f
            };
            a.scale.fromReference = ReferenceValue.StartValue;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = new Vector3(1.12f, 1.12f, 1f);
            return a;
        }

        // Breathe: a slow, calm scale ping-pong.
        private static UIAnimation Breathe()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Loop };
            a.scale.enabled = true;
            a.scale.settings = new TweenSettings
            {
                duration = 1.8f,
                ease = Ease.InOutSine,
                playMode = TweenPlayMode.PingPong,
                loops = TweenSettings.InfiniteLoops
            };
            a.scale.fromReference = ReferenceValue.StartValue;
            a.scale.toReference = ReferenceValue.CustomValue;
            a.scale.toCustomValue = new Vector3(1.04f, 1.04f, 1f);
            return a;
        }

        // Bob: gentle vertical float, up and down forever.
        private static UIAnimation Bob()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Loop };
            a.move.enabled = true;
            a.move.settings = new TweenSettings
            {
                duration = 1.2f,
                ease = Ease.InOutSine,
                playMode = TweenPlayMode.PingPong,
                loops = TweenSettings.InfiniteLoops
            };
            a.move.fromDirection = UIMoveDirection.CustomPosition;
            a.move.toDirection = UIMoveDirection.CustomPosition;
            a.move.fromReference = ReferenceValue.StartValue;
            a.move.toReference = ReferenceValue.StartValue;
            a.move.toOffset = new Vector3(0f, 10f, 0f);
            return a;
        }

        // Spin loop: continuous Z rotation, Normal (not ping-pong) so it keeps spinning one way.
        private static UIAnimation SpinLoop()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Loop };
            a.rotate.enabled = true;
            a.rotate.settings = new TweenSettings
            {
                duration = 1.5f,
                ease = Ease.Linear,
                playMode = TweenPlayMode.Normal,
                loops = TweenSettings.InfiniteLoops
            };
            a.rotate.fromReference = ReferenceValue.CustomValue;
            a.rotate.fromCustomValue = Vector3.zero;
            a.rotate.toReference = ReferenceValue.CustomValue;
            a.rotate.toCustomValue = new Vector3(0f, 0f, 360f);
            return a;
        }

        // Shimmer: ping-pong tint toward PrimaryHover forever.
        private static UIAnimation Shimmer()
        {
            var a = new UIAnimation { purpose = AnimationPurpose.Loop };
            a.color.enabled = true;
            a.color.settings = new TweenSettings
            {
                duration = 0.9f,
                ease = Ease.InOutSine,
                playMode = TweenPlayMode.PingPong,
                loops = TweenSettings.InfiniteLoops
            };
            a.color.from = new ColorAnimationEndpoint { reference = ColorReference.StartColor };
            a.color.to = new ColorAnimationEndpoint { reference = ColorReference.ThemeToken, themeToken = "PrimaryHover" };
            return a;
        }

        // ---------------------------------------------------------------- asset io

        private static int Ensure(string category, string presetName, UIAnimation animation)
        {
            string path = $"{LibraryRoot}/{category}_{presetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<UIAnimationPreset>(path) != null) return 0; // don't clobber
            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = category;
            preset.presetName = presetName;
            preset.animation = animation;
            AssetDatabase.CreateAsset(preset, path);
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
