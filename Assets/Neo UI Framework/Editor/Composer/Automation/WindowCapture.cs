using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Neo.UI.Editor.Composer.Automation
{
    /// <summary>
    /// Captures the pixels of a live <see cref="EditorWindow"/> — the missing primitive that lets an
    /// agent SEE the Composer (its real chrome, real IMGUI, real on-screen state), not just the spec
    /// behind it. This is what makes a "try it and watch it" loop possible.
    ///
    /// <para>Two mechanisms, tried in order, both capturing the genuine on-screen window (not an
    /// offscreen re-render):</para>
    /// <list type="number">
    /// <item><b>GrabPixels</b> — reflect <c>EditorWindow.m_Parent</c> (a <c>GUIView</c>/<c>HostView</c>)
    /// and call its internal <c>GrabPixels(RenderTexture, Rect)</c>. Occlusion- and DPI-robust: it asks
    /// the view to render its own contents, so it works even if the window is partially covered.</item>
    /// <item><b>ReadScreenPixel</b> — <see cref="InternalEditorUtility.ReadScreenPixel"/> over the
    /// window's screen rect. Simpler, but reads the actual framebuffer so the window must be visible
    /// and unobscured.</item>
    /// </list>
    ///
    /// <para>Capture is best-effort: any failure returns null (and the session records "no screenshot"
    /// for that step) rather than throwing — a probe must survive a capture hiccup. Both mechanisms are
    /// Unity-internal and version-sensitive; this is the file Milestone 0 validates against the live
    /// editor before the rest of the harness is trusted.</para>
    /// </summary>
    internal static class WindowCapture
    {
        // GUIView.GrabPixels(RenderTexture, Rect) — cached MethodInfo (null = not resolved / unavailable)
        private static MethodInfo s_grabPixels;
        private static bool s_grabResolved;
        private static FieldInfo s_parentField;

        /// <summary> Captures <paramref name="window"/> to a fresh <see cref="Texture2D"/> (caller owns
        /// it — destroy or encode then destroy). Returns null if no mechanism succeeded. </summary>
        public static Texture2D Capture(EditorWindow window)
        {
            if (window == null) return null;
            Texture2D tex = TryGrabPixels(window);
            if (tex != null) return tex;
            return TryReadScreenPixel(window);
        }

        /// <summary> Captures <paramref name="window"/> to a PNG at <paramref name="path"/>. Returns the
        /// absolute path written, or null on failure. </summary>
        public static string CaptureToPng(EditorWindow window, string path)
        {
            Texture2D tex = Capture(window);
            if (tex == null) return null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return Path.GetFullPath(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ComposerProbe] WindowCapture failed to write '{path}': {e.Message}");
                return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ------------------------------------------------------------------ mechanism 1: GrabPixels

        private static Texture2D TryGrabPixels(EditorWindow window)
        {
            try
            {
                object hostView = HostView(window);
                MethodInfo grab = GrabPixelsMethod(hostView);
                if (hostView == null || grab == null) return null;

                Rect pos = window.position;
                float ppp = EditorGUIUtility.pixelsPerPoint;
                int pw = Mathf.Max(1, Mathf.RoundToInt(pos.width * ppp));
                int ph = Mathf.Max(1, Mathf.RoundToInt(pos.height * ppp));

                var rt = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                try
                {
                    // GrabPixels takes the view-local rect in points; the RT is in physical pixels.
                    grab.Invoke(hostView, new object[] { rt, new Rect(0f, 0f, pos.width, pos.height) });
                    return ReadBack(rt, pw, ph);
                }
                finally
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ComposerProbe] GrabPixels capture unavailable ({e.GetType().Name}: {e.Message}); falling back to ReadScreenPixel.");
                return null;
            }
        }

        private static object HostView(EditorWindow window)
        {
            s_parentField ??= typeof(EditorWindow).GetField("m_Parent",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return s_parentField?.GetValue(window);
        }

        private static MethodInfo GrabPixelsMethod(object hostView)
        {
            if (s_grabResolved) return s_grabPixels;
            s_grabResolved = true;
            if (hostView == null) return null;
            foreach (MethodInfo m in hostView.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.Name != "GrabPixels") continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length == 2 && p[0].ParameterType == typeof(RenderTexture) && p[1].ParameterType == typeof(Rect))
                {
                    s_grabPixels = m;
                    break;
                }
            }
            return s_grabPixels;
        }

        // ------------------------------------------------------------------ mechanism 2: ReadScreenPixel

        private static Texture2D TryReadScreenPixel(EditorWindow window)
        {
            try
            {
                Rect pos = window.position;
                float ppp = EditorGUIUtility.pixelsPerPoint;
                int pw = Mathf.Max(1, Mathf.RoundToInt(pos.width * ppp));
                int ph = Mathf.Max(1, Mathf.RoundToInt(pos.height * ppp));

                // ReadScreenPixel reads from the framebuffer with a bottom-left origin; window.position
                // is top-left in points on the main display. Flip y against the main display height.
                float screenH = Screen.currentResolution.height;
                var origin = new Vector2(pos.x * ppp, screenH - (pos.y + pos.height) * ppp);
                Color[] pixels = InternalEditorUtility.ReadScreenPixel(origin, pw, ph);
                if (pixels == null || pixels.Length < pw * ph) return null;

                var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, mipChain: false);
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ComposerProbe] ReadScreenPixel capture failed ({e.GetType().Name}: {e.Message}).");
                return null;
            }
        }

        // ------------------------------------------------------------------ shared

        private static Texture2D ReadBack(RenderTexture rt, int w, int h)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            try
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
