using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Describes one kind of component that DRIVES the color of the color target (Graphic or
    /// SpriteRenderer) on its own GameObject at runtime — a toggle/selectable color animator, a theme
    /// color target, an enabled color channel on a UIAnimation slot, and so on. <see cref="probe"/>
    /// inspects a candidate component and returns a short human-readable "who drives it" description
    /// (e.g. <c>"UI Toggle Color Animator (on/off colors)"</c>), or null when the component doesn't
    /// drive color. All built-in drivers bind to the color target on their OWN GameObject
    /// (<c>ColorTargetUtils.FindTarget(gameObject)</c>), so a probe never needs to chase references.
    /// </summary>
    public sealed class ColorDriverDescriptor
    {
        public string id;
        public Func<Component, string> probe;
    }

    /// <summary>
    /// Registry of color-driver detectors, consulted by the inspector notices that warn "this color is
    /// overwritten at runtime — don't hand-edit it" (the TMP-LocalizedString-style hint). A consuming
    /// project whose own component drives Graphic colors registers a descriptor via
    /// <see cref="Register"/> and every notice surface picks it up — no package fork.
    /// </summary>
    public static class NeoColorDrivers
    {
        private static readonly NeoKeyedRegistry<ColorDriverDescriptor> s_registry =
            new NeoKeyedRegistry<ColorDriverDescriptor>(
                key: d => d.id,
                builtins: Builtins,
                validate: d => d.probe != null,
                registryName: "NeoColorDrivers");

        public static IReadOnlyList<ColorDriverDescriptor> All => s_registry.All;

        public static void Register(ColorDriverDescriptor descriptor) => s_registry.Register(descriptor);

        internal static bool Remove(string id) => s_registry.Remove(id);

        // shared scratch buffer — the scan runs on selection, never allocates per call
        private static readonly List<Component> s_componentBuffer = new List<Component>(16);
        private static readonly StringBuilder s_describeBuffer = new StringBuilder(96);

        /// <summary>
        /// True when something on <paramref name="gameObject"/> drives the color of its own color
        /// target at runtime. <paramref name="drivers"/> is the comma-joined description of every
        /// driver found; <paramref name="driven"/> is the driven Graphic/SpriteRenderer.
        /// </summary>
        public static bool TryDescribe(GameObject gameObject, out string drivers, out Component driven)
        {
            drivers = null;
            driven = null;
            if (gameObject == null) return false;

            IColorTarget target = ColorTargetUtils.FindTarget(gameObject);
            driven = target?.targetObject as Component;
            if (driven == null) return false;

            s_describeBuffer.Clear();
            gameObject.GetComponents(s_componentBuffer);
            IReadOnlyList<ColorDriverDescriptor> descriptors = s_registry.All;
            foreach (Component component in s_componentBuffer)
            {
                if (component == null) continue;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    string description = descriptors[i].probe(component);
                    if (string.IsNullOrEmpty(description)) continue;
                    if (s_describeBuffer.Length > 0) s_describeBuffer.Append(", ");
                    s_describeBuffer.Append(description);
                    break; // one description per component
                }
            }
            s_componentBuffer.Clear();

            if (s_describeBuffer.Length == 0) return false;
            drivers = s_describeBuffer.ToString();
            return true;
        }

        private static IEnumerable<ColorDriverDescriptor> Builtins()
        {
            // The three ColorTween state animators drive unconditionally: they SetColor the target
            // on every enable / state change, so the base color is pure resting-state bake.
            yield return new ColorDriverDescriptor
            {
                id = "toggleColorAnimator",
                probe = c => c is UIToggleColorAnimator ? "UI Toggle Color Animator (on/off colors)" : null
            };
            yield return new ColorDriverDescriptor
            {
                id = "selectableColorAnimator",
                probe = c => c is UISelectableColorAnimator ? "UI Selectable Color Animator (per-state colors)" : null
            };
            yield return new ColorDriverDescriptor
            {
                id = "containerColorAnimator",
                probe = c => c is UIContainerColorAnimator a &&
                             (a.showAnimation.enabled || a.hideAnimation.enabled)
                    ? "UI Container Color Animator (show/hide)"
                    : null
            };

            // Theme targets re-apply on enable and on every theme change.
            yield return new ColorDriverDescriptor
            {
                id = "themeColorTarget",
                probe = c => c is ThemeColorTarget t && !string.IsNullOrEmpty(t.token)
                    ? $"Theme Color Target (token '{t.token}')"
                    : null
            };
            yield return new ColorDriverDescriptor
            {
                id = "themeShapeStyleTarget",
                probe = c => c is ThemeShapeStyleTarget t && t.applyFillColor && !string.IsNullOrEmpty(t.style)
                    ? $"Theme Shape Style Target (style '{t.style}')"
                    : null
            };
            yield return new ColorDriverDescriptor
            {
                id = "themeTextStyleTarget",
                probe = c => c is ThemeTextStyleTarget t && t.applyColor && !string.IsNullOrEmpty(t.style)
                    ? $"Theme Text Style Target (style '{t.style}')"
                    : null
            };

            // UIAnimation-based animators drive only when a slot's color channel is enabled.
            yield return new ColorDriverDescriptor
            {
                id = "uiAnimator",
                probe = c => c is UIAnimator a && a.animation.color.enabled
                    ? "UI Animator (color channel)"
                    : null
            };
            yield return new ColorDriverDescriptor
            {
                id = "selectableUIAnimator",
                probe = c => c is UISelectableUIAnimator a &&
                             (a.normalAnimation.color.enabled || a.highlightedAnimation.color.enabled ||
                              a.pressedAnimation.color.enabled || a.selectedAnimation.color.enabled ||
                              a.disabledAnimation.color.enabled)
                    ? "UI Selectable UI Animator (color channel)"
                    : null
            };
            yield return new ColorDriverDescriptor
            {
                id = "toggleUIAnimator",
                probe = c => c is UIToggleUIAnimator a &&
                             (a.onAnimation.color.enabled || a.offAnimation.color.enabled)
                    ? "UI Toggle UI Animator (color channel)"
                    : null
            };
            yield return new ColorDriverDescriptor
            {
                id = "containerUIAnimator",
                probe = c => c is UIContainerUIAnimator a &&
                             (a.showAnimation.color.enabled || a.hideAnimation.color.enabled)
                    ? "UI Container UI Animator (color channel)"
                    : null
            };
        }
    }

    /// <summary>
    /// The TMP-LocalizedString-style "this value is driven, don't hand-edit it" notice for colors.
    /// Two surfaces share the one <see cref="NeoColorDrivers"/> scan:
    /// <list type="bullet">
    /// <item><see cref="DrawInline"/> — called by inspectors the package OWNS (NeoShape) right above
    /// their color field.</item>
    /// <item>A <see cref="UnityEditor.Editor.finishedDefaultHeaderGUI"/> hook that draws the same
    /// notice under the GameObject header for graphics whose inspectors are Unity-made (Image,
    /// RawImage, TMP text, SpriteRenderer) and can't be edited inline without forking them. It skips
    /// NeoShape (its own inspector already shows the inline notice).</item>
    /// </list>
    /// Scan results are cached per GameObject and invalidated on any object change / undo / selection
    /// change — never recomputed per repaint (editor-performance rule).
    /// </summary>
    [InitializeOnLoad]
    public static class ColorDriverNotice
    {
        private struct CacheEntry
        {
            public bool driven;
            public string drivers;
            public Component drivenTarget;
        }

        private static readonly Dictionary<int, CacheEntry> s_cache = new Dictionary<int, CacheEntry>();

        static ColorDriverNotice()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => s_cache.Clear();
            Selection.selectionChanged += s_cache.Clear;
            Undo.undoRedoPerformed += s_cache.Clear;
        }

        /// <summary>
        /// Draws the notice for <paramref name="drivenCandidate"/>'s GameObject when its color is
        /// runtime-driven AND the driven target is that component. For inspectors the package owns —
        /// call it just above the color field.
        /// </summary>
        public static void DrawInline(Component drivenCandidate)
        {
            if (drivenCandidate == null) return;
            CacheEntry entry = Scan(drivenCandidate.gameObject);
            if (!entry.driven || entry.drivenTarget != drivenCandidate) return;
            EditorGUILayout.HelpBox(
                $"Color is driven at runtime by {entry.drivers}. " +
                "The color below is only the resting/baked state — runtime overwrites it.",
                MessageType.Info);
        }

        private static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            // GameObject header only, single selection only (multi-edit scans would multiply cost).
            if (editor.targets.Length != 1 || !(editor.target is GameObject gameObject)) return;

            CacheEntry entry = Scan(gameObject);
            if (!entry.driven) return;
            // NeoShape shows the inline notice next to its color field — don't double up.
            if (entry.drivenTarget is NeoShape) return;

            EditorGUILayout.HelpBox(
                $"{entry.drivenTarget.GetType().Name} color is driven at runtime by {entry.drivers}. " +
                "Manual color edits will be overwritten.",
                MessageType.Info);
        }

        private static CacheEntry Scan(GameObject gameObject)
        {
            int id = gameObject.GetInstanceID();
            if (s_cache.TryGetValue(id, out CacheEntry cached)) return cached;

            var entry = new CacheEntry();
            entry.driven = NeoColorDrivers.TryDescribe(gameObject, out entry.drivers, out entry.drivenTarget);
            s_cache[id] = entry;
            return entry;
        }
    }
}
