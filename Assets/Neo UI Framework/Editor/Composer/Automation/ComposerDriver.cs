using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer.Automation
{
    /// <summary>
    /// Drives a live <see cref="NeoComposerWindow"/> the way a human would, so an agent can experience
    /// the Composer instead of only mutating the spec behind it. Each method realizes one scenario
    /// intent and is honest about HOW:
    ///
    /// <list type="bullet">
    /// <item><b>Injected</b> — <see cref="Select"/>/<see cref="DragElement"/>/<see cref="ResizeElement"/>/
    /// <see cref="Nudge"/> synthesize real <see cref="Event"/>s (<see cref="EditorWindow.SendEvent"/>)
    /// that flow through the genuine <c>ComposerCanvas</c> handlers — the gesture surface whose feel
    /// we're measuring.</item>
    /// <item><b>Driven</b> — device size, breakpoint scope, undo/redo and palette-add call the exact code
    /// path the corresponding control invokes. Unity's <c>DragAndDrop</c> session and per-frame toolbar
    /// layout can't be faithfully synthesized, so these are invoked directly rather than faked.</item>
    /// </list>
    ///
    /// <para>Between injected events the driver pumps a SYNCHRONOUS repaint
    /// (<c>EditorWindow.RepaintImmediately</c>, reflected) so the next event hit-tests against fresh
    /// geometry and a capture sees the result — letting a whole scenario run inside one bridge call.
    /// <see cref="Settle"/> force-rebuilds the preview so element rects are current before a gesture.</para>
    /// </summary>
    public sealed class ComposerDriver
    {
        private readonly NeoComposerWindow _window;
        private static MethodInfo s_repaintImmediately;

        /// <summary> Total input events injected so far — the probe diffs this around a step to record
        /// how many discrete events the action cost ("economy"). </summary>
        public int EventCount { get; private set; }

        /// <summary> A short note set by the last action (e.g. an API fallback) for the step record. </summary>
        public string LastNote { get; private set; }

        public ComposerDriver(NeoComposerWindow window)
        {
            _window = window;
        }

        // ------------------------------------------------------------------ settle / pump

        /// <summary> Force the preview to rebuild and redraw so element geometry is current. Order
        /// matters: sync the target from the selection, rebuild (captures boxes + texture), then repaint
        /// so the on-screen device rect is recomputed against the fresh texture. </summary>
        public void Settle()
        {
            LastNote = null;
            Pump();                              // a draw pass syncs the preview target from the selection
            _window.Probe_SyncPreviewTarget();
            _window.Preview.ForceRebuild();      // build the element boxes + texture synchronously
            Pump();                              // recompute the on-screen draw rect against the new texture
        }

        // Synchronous repaint so injected events and captures see an up-to-date window in one call.
        // Best-effort: a failed repaint (e.g. -nographics batch) must never abort the session.
        private void Pump()
        {
            MethodInfo m = s_repaintImmediately ??= typeof(EditorWindow).GetMethod("RepaintImmediately",
                BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                if (m != null) m.Invoke(_window, null);
                else _window.Repaint(); // async fallback — less deterministic, but never crashes the session
            }
            catch
            {
                _window.Repaint();
            }
        }

        // ------------------------------------------------------------------ injected gestures

        /// <summary> Select a node. Prefers a real canvas click (exercises hit-test + selection sync);
        /// falls back to the tree-select code path for non-canvas nodes or off-screen elements. </summary>
        public void Select(string path)
        {
            Settle();
            if (_window.Probe_TryGetElementWindowRect(path, out Rect r))
            {
                Click(r.center);
                if (_window.SelectedPath == path) return;
            }
            _window.Probe_SelectPath(path);
            LastNote = "selected via tree API (not visible on canvas)";
            Pump();
        }

        /// <summary> Drag an element by a device-space delta (dx right, dy up — the spec's layout space).
        /// Synthesizes a press, several drag moves (past the canvas drag threshold) and a release through
        /// the real <c>ComposerCanvas</c> move/reorder/reparent path. </summary>
        public void DragElement(string path, float dxDevice, float dyDevice)
        {
            Settle();
            if (!_window.Probe_TryGetElementWindowRect(path, out Rect r))
            {
                LastNote = $"drag skipped — '{path}' not visible on canvas";
                Debug.LogWarning($"[ComposerProbe] {LastNote}");
                return;
            }
            // device px → on-screen px (screen y is down, device y is up)
            float scale = _window.Preview.LastScale;
            var screenDelta = new Vector2(dxDevice * scale, -dyDevice * scale);
            DragGesture(r.center, screenDelta);
        }

        /// <summary> Resize an element from a named handle (corner: tl/tr/bl/br, edge: l/r/t/b) by a
        /// device-space delta. The element must be selectable as a free element for handles to show. </summary>
        public void ResizeElement(string path, string handle, float dxDevice, float dyDevice)
        {
            Select(path);
            Settle();
            if (!_window.Probe_TryGetElementWindowRect(path, out Rect r))
            {
                LastNote = $"resize skipped — '{path}' not visible on canvas";
                Debug.LogWarning($"[ComposerProbe] {LastNote}");
                return;
            }
            Vector2 grip = HandlePoint(r, handle);
            float scale = _window.Preview.LastScale;
            var screenDelta = new Vector2(dxDevice * scale, -dyDevice * scale);
            DragGesture(grip, screenDelta);
        }

        /// <summary> Keyboard-nudge the selection (arrow keys, optionally shift for the ×10 step) through
        /// the real canvas <c>HandleKeyboard</c> path. </summary>
        public void Nudge(string path, string dir, int count, bool shift)
        {
            Select(path);
            _window.Probe_FocusPreview();
            Pump();
            KeyCode key = dir switch
            {
                "left" => KeyCode.LeftArrow,
                "up" => KeyCode.UpArrow,
                "down" => KeyCode.DownArrow,
                _ => KeyCode.RightArrow,
            };
            EventModifiers mods = shift ? EventModifiers.Shift : EventModifiers.None;
            for (int i = 0; i < count; i++) { SendKey(key, mods); Pump(); }
        }

        // ------------------------------------------------------------------ driven (same code path the control hits)

        public void AddWidget(string kind, string targetPath)
        {
            if (!string.IsNullOrEmpty(targetPath)) { _window.Probe_SelectPath(targetPath); Pump(); }
            _window.Probe_AddWidget(kind); // identical to the palette click-to-add path
            LastNote = "added via click-to-add code path (DragAndDrop can't be synthesized)";
            Settle();
        }

        public void SetDevicePreset(string presetId)
        {
            _window.Preview.ApplyPresetById(presetId);
            Settle();
        }

        public void SetDeviceSize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            _window.Preview.SetDeviceSize(width, height);
            Settle();
        }

        public void ResizeDevice(int dw, int dh)
        {
            (int w, int h) = _window.Preview.DeviceSizePx;
            _window.Preview.SetDeviceSize(w + dw, h + dh);
            Settle();
        }

        public void SetBreakpoint(string name)
        {
            _window.Probe_SetBreakpoint(name);
            Settle();
        }

        public void Undo() { _window.Probe_Undo(); Settle(); }
        public void Redo() { _window.Probe_Redo(); Settle(); }

        // ------------------------------------------------------------------ event synthesis

        private void DragGesture(Vector2 start, Vector2 delta)
        {
            const int moves = 4;
            SendMouse(EventType.MouseDown, start);
            Pump();
            for (int i = 1; i <= moves; i++)
            {
                SendMouse(EventType.MouseDrag, start + delta * (i / (float)moves));
                Pump();
            }
            SendMouse(EventType.MouseUp, start + delta);
            Pump();
        }

        private void Click(Vector2 windowPos)
        {
            SendMouse(EventType.MouseDown, windowPos);
            Pump();
            SendMouse(EventType.MouseUp, windowPos);
            Pump();
        }

        private void SendMouse(EventType type, Vector2 windowPos, int button = 0)
        {
            var e = new Event { type = type, mousePosition = windowPos, button = button };
            _window.SendEvent(e);
            EventCount++;
        }

        private void SendKey(KeyCode key, EventModifiers mods)
        {
            _window.SendEvent(new Event { type = EventType.KeyDown, keyCode = key, modifiers = mods });
            _window.SendEvent(new Event { type = EventType.KeyUp, keyCode = key, modifiers = mods });
            EventCount += 2;
        }

        // The grip point for a resize handle, mapped onto the element's window rect. Unknown names fall
        // back to the bottom-right corner (the most common resize grip).
        private static Vector2 HandlePoint(Rect r, string handle)
        {
            switch (handle)
            {
                case "tl": return new Vector2(r.xMin, r.yMin);
                case "tr": return new Vector2(r.xMax, r.yMin);
                case "bl": return new Vector2(r.xMin, r.yMax);
                case "l": return new Vector2(r.xMin, r.center.y);
                case "r": return new Vector2(r.xMax, r.center.y);
                case "t": return new Vector2(r.center.x, r.yMin);
                case "b": return new Vector2(r.center.x, r.yMax);
                default: return new Vector2(r.xMax, r.yMax); // "br"
            }
        }
    }
}
