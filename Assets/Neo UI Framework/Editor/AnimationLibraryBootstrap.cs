using Neo.UI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Seeds a curated library of default <see cref="UIAnimationPreset"/> assets (fades, four-way slides,
    /// scale-pop, button press, loop pulse) so a fresh project HAS ready-to-use motion to reference by
    /// name — the package previously shipped none. Each preset is built on the runtime's own
    /// <see cref="UIAnimation.ApplyPurposeDefaults"/> (known-good durations/eases), overriding only the
    /// direction, so the library matches how the engine animates. They land under <see cref="LibraryRoot"/>
    /// and auto-discover via <see cref="AnimationPresetRegistry"/> (no database wiring). Create-missing-only:
    /// a re-run fills gaps and never clobbers a preset you've tweaked.
    /// </summary>
    public static class AnimationLibraryBootstrap
    {
        public const string LibraryRoot = "Assets/Neo UI Framework/Animations";

        [MenuItem("Tools/Neo UI/Setup/Create or Repair Animation Library", priority = 103)]
        public static void CreateOrRepairMenu()
        {
            int created = CreateOrRepair();
            Debug.Log($"[Neo.UI] Animation library: {created} preset(s) created (others already present).");
        }

        /// <summary> Creates any missing default presets. Returns how many were created. </summary>
        public static int CreateOrRepair()
        {
            EnsureFolder(LibraryRoot);
            int n = 0;

            n += Ensure("Show", "FadeIn", Show(move: false));
            n += Ensure("Show", "SlideInLeft", Show(UIMoveDirection.Left));
            n += Ensure("Show", "SlideInRight", Show(UIMoveDirection.Right));
            n += Ensure("Show", "SlideInUp", Show(UIMoveDirection.Bottom));
            n += Ensure("Show", "SlideInDown", Show(UIMoveDirection.Top));
            n += Ensure("Show", "ScalePopIn", ScalePopIn());

            n += Ensure("Hide", "FadeOut", Hide(move: false));
            n += Ensure("Hide", "SlideOutLeft", Hide(UIMoveDirection.Left));
            n += Ensure("Hide", "SlideOutRight", Hide(UIMoveDirection.Right));
            n += Ensure("Hide", "SlideOutUp", Hide(UIMoveDirection.Top));
            n += Ensure("Hide", "SlideOutDown", Hide(UIMoveDirection.Bottom));

            n += Ensure("Button", "Press", Purpose(AnimationPurpose.Button));
            n += Ensure("Loop", "Pulse", Purpose(AnimationPurpose.Loop));

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
