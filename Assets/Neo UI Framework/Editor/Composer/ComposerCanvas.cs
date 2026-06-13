using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// One element's geometry captured from the live preview build: its normalized screen rect (for
    /// hit-testing / drawing) plus the exact <c>anchoredPosition</c> and rect <c>size</c> read off the
    /// built RectTransform before it was destroyed. The captured pos/size are the BASE values a drag
    /// adds its delta to — so a drag never "jumps" even when the element had no explicit
    /// <c>position</c>/<c>size</c> in the spec (it inherited one from an anchor or the auto-stack).
    /// </summary>
    public struct ElementBox
    {
        public Rect norm;            // normalized 0..1 canvas rect, y from bottom
        public Vector2 anchoredPos;  // current anchoredPosition (canvas px)
        public Vector2 size;         // current rect size (canvas px)
        public Vector2 pivot;        // rect pivot — resize keeps the opposite edge fixed about it
    }

    /// <summary>
    /// Tier 3 (Plan 2 Phase 4): the WYSIWYG canvas. Turns the center preview from a passive image into
    /// a direct-manipulation surface — click to select, drag to move, handles to resize, marquee to
    /// multi-select, drag-onto-a-container to reparent — with snapping to sibling edges and the spacing
    /// grid. It is purely additive: every gesture is just another <see cref="SpecDocument.ApplyEdit"/>
    /// mutating <c>position</c>/<c>size</c>/parent lists, so everything it does round-trips losslessly,
    /// exactly like the inspector. The preview owns the render + the element→<see cref="ElementBox"/>
    /// map; this owns the input and the overlay it draws on top.
    ///
    /// <para>Moves/resizes are committed ONCE on mouse-up (a single undo step); during the drag a ghost
    /// outline tracks the cursor so it feels live without rebuilding the texture on every mouse move.
    /// Only free-anchored elements (top-level view/popup elements, and <c>overlay</c>/<c>safearea</c>
    /// children) carry position/size — layout-group children are placed by their parent, so for those
    /// a drag is reparent-only.</para>
    /// </summary>
    public class ComposerCanvas
    {
        private const float HandleSize = 8f;
        private const float SnapPx = 6f;
        private const float GridCanvas = 8f;    // the on-scale spacing grid the design lint blesses
        private const float DragThreshold = 3f;

        private readonly SpecDocument _document;
        private readonly Action<string> _selectPath;
        private readonly Action _repaint;

        // --- per-frame context handed in by the preview ---
        private Rect _drawRect;
        private float _scale = 1f;
        private ViewSpec _view;
        private PopupSpec _popup;
        private IReadOnlyDictionary<ElementSpec, ElementBox> _boxes;

        // --- structural index rebuilt each frame from the owner's element tree ---
        private sealed class Node
        {
            public List<ElementSpec> siblings;
            public int index;
            public ElementSpec parent;     // null = top-level
            public string path;
            public int depth;
            public bool free;              // position/size are honored (not layout-managed)
            public bool container;         // can host children
        }
        private readonly Dictionary<ElementSpec, Node> _index = new Dictionary<ElementSpec, Node>();

        // --- selection + drag state ---
        private readonly HashSet<ElementSpec> _selection = new HashSet<ElementSpec>();
        private ElementSpec _primary;
        private ElementSpec _lastExternalPrimary;

        private enum Mode { None, Maybe, Move, Resize, Marquee }
        private Mode _mode;
        private int _controlId;          // claims mouse capture so drag/up reach us across the window
        private Vector2 _mouseDownScreen;
        private int _resizeHandle = -1;
        private readonly Dictionary<ElementSpec, (Vector2 pos, Vector2 size)> _dragStart =
            new Dictionary<ElementSpec, (Vector2, Vector2)>();
        private Rect _marquee;
        private ElementSpec _dropTarget;
        private Vector2 _snapGuideV = new Vector2(float.NaN, 0f); // x of a vertical snap guide (screen)
        private Vector2 _snapGuideH = new Vector2(0f, float.NaN); // y of a horizontal snap guide (screen)

        private static readonly HashSet<string> FreeParents = new HashSet<string> { "overlay", "safearea" };
        private static readonly HashSet<string> ContainerKinds =
            new HashSet<string> { "vstack", "hstack", "grid", "scroll", "list", "panel", "overlay", "safearea" };
        private static readonly HashSet<string> LayoutKinds =
            new HashSet<string> { "vstack", "hstack", "grid", "scroll", "list", "panel" };

        public ComposerCanvas(SpecDocument document, Action<string> selectPath, Action repaint)
        {
            _document = document;
            _selectPath = selectPath;
            _repaint = repaint;
        }

        /// <summary>
        /// Drawn + driven by the preview pane after it has blitted the texture. <paramref name="drawRect"/>
        /// is where the texture sits on screen, <paramref name="scale"/> maps canvas px → screen px,
        /// and <paramref name="primary"/> is the tree's currently-selected element (so the canvas and
        /// tree agree on selection).
        /// </summary>
        public void OnGUI(Rect drawRect, float scale,
            ViewSpec view, PopupSpec popup, IReadOnlyDictionary<ElementSpec, ElementBox> boxes,
            ElementSpec primary)
        {
            _drawRect = drawRect; _scale = scale;
            _view = view; _popup = popup; _boxes = boxes;
            _controlId = GUIUtility.GetControlID(FocusType.Passive);

            SyncExternalSelection(primary);
            BuildIndex();
            HandleEvents();
            DrawOverlay();
        }

        // ------------------------------------------------------------------ selection sync

        private void SyncExternalSelection(ElementSpec primary)
        {
            // when the tree (or inspector) moves the selection, follow it — unless we're mid-drag
            if (_mode == Mode.Move || _mode == Mode.Resize || _mode == Mode.Marquee) return;
            if (!ReferenceEquals(primary, _lastExternalPrimary))
            {
                _lastExternalPrimary = primary;
                _primary = primary;
                _selection.Clear();
                if (primary != null) _selection.Add(primary);
            }
        }

        // ------------------------------------------------------------------ structural index

        private void BuildIndex()
        {
            _index.Clear();
            List<ElementSpec> top = _view != null ? _view.elements : _popup?.elements;
            if (top == null) return;
            string ownerPath = _view != null ? SpecPath.View(_view.id)
                : _popup != null ? SpecPath.Popup(_popup.name) : null;
            IndexList(top, ownerPath, true, null, 0);
        }

        private void IndexList(List<ElementSpec> list, string ownerPath, bool topLevel, ElementSpec parent, int depth)
        {
            for (int i = 0; i < list.Count; i++)
            {
                ElementSpec element = list[i];
                string path = ownerPath + (topLevel ? "/elements[" : "/children[") + i + "]";
                bool free = parent == null || FreeParents.Contains(parent.kind);
                _index[element] = new Node
                {
                    siblings = list, index = i, parent = parent, path = path, depth = depth,
                    free = free, container = ContainerKinds.Contains(element.kind)
                };
                if (element.children != null && element.children.Count > 0)
                    IndexList(element.children, path, false, element, depth + 1);
            }
        }

        // ------------------------------------------------------------------ geometry

        private Rect ToScreen(Rect n) => new Rect(
            _drawRect.x + n.x * _drawRect.width,
            _drawRect.y + (1f - n.y - n.height) * _drawRect.height,
            n.width * _drawRect.width,
            n.height * _drawRect.height);

        private bool TryScreenRect(ElementSpec element, out Rect screen)
        {
            screen = default;
            if (element == null || _boxes == null || !_boxes.TryGetValue(element, out ElementBox box)) return false;
            screen = ToScreen(box.norm);
            return true;
        }

        // screen px delta → canvas px delta (screen y is down, anchoredPosition y is up)
        private Vector2 ScreenToCanvasDelta(Vector2 screenDelta) =>
            new Vector2(screenDelta.x / _scale, -screenDelta.y / _scale);

        // ------------------------------------------------------------------ event handling

        private void HandleEvents()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && _drawRect.Contains(e.mousePosition):
                    OnMouseDown(e);
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == _controlId && _mode != Mode.None:
                    OnMouseDrag(e);
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == _controlId && _mode != Mode.None:
                    OnMouseUp(e);
                    break;
            }
        }

        private void OnMouseDown(Event e)
        {
            _mouseDownScreen = e.mousePosition;
            _snapGuideV.x = float.NaN; _snapGuideH.y = float.NaN;
            _dropTarget = null;
            GUIUtility.hotControl = _controlId; // capture the mouse for the whole gesture

            // a resize handle on the primary selection takes priority
            int handle = HitHandle(e.mousePosition);
            if (handle >= 0 && _primary != null && IsFree(_primary))
            {
                _resizeHandle = handle;
                _mode = Mode.Resize;
                CaptureDragStarts();
                e.Use();
                return;
            }

            ElementSpec hit = HitTest(e.mousePosition);
            if (hit == null)
            {
                // empty canvas → begin a marquee selection
                _mode = Mode.Marquee;
                _marquee = new Rect(e.mousePosition, Vector2.zero);
                if (!e.shift) { _selection.Clear(); _primary = null; }
                e.Use();
                return;
            }

            bool additive = e.shift || EditorGUI.actionKey; // shift / cmd-ctrl
            if (additive)
            {
                if (!_selection.Add(hit)) _selection.Remove(hit);
                _primary = _selection.Contains(hit) ? hit : FirstSelected();
            }
            else if (!_selection.Contains(hit))
            {
                _selection.Clear();
                _selection.Add(hit);
                _primary = hit;
            }
            else
            {
                _primary = hit;
            }

            SelectInTree(_primary);
            _mode = Mode.Maybe; // promotes to Move once the cursor passes the drag threshold
            CaptureDragStarts();
            e.Use();
        }

        private void OnMouseDrag(Event e)
        {
            if (_mode == Mode.Maybe)
            {
                if ((e.mousePosition - _mouseDownScreen).magnitude < DragThreshold) { e.Use(); return; }
                _mode = Mode.Move;
            }

            switch (_mode)
            {
                case Mode.Marquee:
                    _marquee = RectFromPoints(_mouseDownScreen, e.mousePosition);
                    break;
                case Mode.Move:
                    _dropTarget = FindDropTarget(e.mousePosition);
                    break;
                case Mode.Resize:
                    break;
            }
            _repaint?.Invoke();
            e.Use();
        }

        private void OnMouseUp(Event e)
        {
            switch (_mode)
            {
                case Mode.Marquee:
                    CommitMarquee();
                    break;
                case Mode.Move:
                    CommitMove(e.mousePosition);
                    break;
                case Mode.Resize:
                    CommitResize(e.mousePosition);
                    break;
                case Mode.Maybe:
                    break; // a click with no drag — selection already applied
            }
            _mode = Mode.None;
            _resizeHandle = -1;
            _dropTarget = null;
            _snapGuideV.x = float.NaN; _snapGuideH.y = float.NaN;
            _dragStart.Clear();
            if (GUIUtility.hotControl == _controlId) GUIUtility.hotControl = 0;
            e.Use();
        }

        // ------------------------------------------------------------------ hit testing

        // deepest element under the point (most nested wins; ties broken by smaller area)
        private ElementSpec HitTest(Vector2 point)
        {
            ElementSpec best = null;
            int bestDepth = -1;
            float bestArea = float.MaxValue;
            foreach (KeyValuePair<ElementSpec, Node> entry in _index)
            {
                if (!TryScreenRect(entry.Key, out Rect screen) || !screen.Contains(point)) continue;
                float area = screen.width * screen.height;
                int depth = entry.Value.depth;
                if (depth > bestDepth || (depth == bestDepth && area < bestArea))
                {
                    best = entry.Key; bestDepth = depth; bestArea = area;
                }
            }
            return best;
        }

        // the container the drag is hovering over (shallowest sensible drop is the deepest container
        // that isn't the dragged element or one of its descendants)
        private ElementSpec FindDropTarget(Vector2 point)
        {
            ElementSpec best = null;
            int bestDepth = -1;
            foreach (KeyValuePair<ElementSpec, Node> entry in _index)
            {
                Node node = entry.Value;
                if (!node.container) continue;
                if (_selection.Contains(entry.Key) || IsDescendantOfSelection(entry.Key)) continue;
                if (!TryScreenRect(entry.Key, out Rect screen) || !screen.Contains(point)) continue;
                if (node.depth > bestDepth) { best = entry.Key; bestDepth = node.depth; }
            }
            return best;
        }

        private bool IsDescendantOfSelection(ElementSpec element)
        {
            for (Node n = _index.TryGetValue(element, out Node node) ? node : null; n?.parent != null;
                 n = _index.TryGetValue(n.parent, out Node pn) ? pn : null)
                if (_selection.Contains(n.parent)) return true;
            return false;
        }

        // returns 0..7 (TL,T,TR,R,BR,B,BL,L) when the point is on a handle of the primary's rect
        private int HitHandle(Vector2 point)
        {
            if (_primary == null || !TryScreenRect(_primary, out Rect r)) return -1;
            Vector2[] handles = HandlePositions(r);
            for (int i = 0; i < handles.Length; i++)
            {
                var hr = new Rect(handles[i].x - HandleSize, handles[i].y - HandleSize,
                    HandleSize * 2f, HandleSize * 2f);
                if (hr.Contains(point)) return i;
            }
            return -1;
        }

        private static Vector2[] HandlePositions(Rect r) => new[]
        {
            new Vector2(r.xMin, r.yMin), new Vector2(r.center.x, r.yMin), new Vector2(r.xMax, r.yMin),
            new Vector2(r.xMax, r.center.y), new Vector2(r.xMax, r.yMax), new Vector2(r.center.x, r.yMax),
            new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.center.y),
        };

        // ------------------------------------------------------------------ commit: marquee

        private void CommitMarquee()
        {
            foreach (KeyValuePair<ElementSpec, Node> entry in _index)
                if (TryScreenRect(entry.Key, out Rect screen) && _marquee.Overlaps(screen, true))
                    _selection.Add(entry.Key);
            _primary = FirstSelected();
            SelectInTree(_primary);
        }

        // ------------------------------------------------------------------ commit: move / reparent

        private void CommitMove(Vector2 mouse)
        {
            // reparent wins when the drop lands on a different container than the element's current parent
            if (_dropTarget != null && _index.TryGetValue(_primary, out Node primaryNode)
                && !ReferenceEquals(_dropTarget, primaryNode.parent))
            {
                ReparentSelection(_dropTarget);
                return;
            }

            if (_primary == null || !IsFree(_primary)) return; // layout children only reparent

            Vector2 screenDelta = SnapMove(mouse - _mouseDownScreen);
            Vector2 canvasDelta = ScreenToCanvasDelta(screenDelta);
            if (canvasDelta.sqrMagnitude < 0.01f) return;

            var targets = new List<ElementSpec>();
            foreach (ElementSpec el in _selection) if (IsFree(el)) targets.Add(el);
            if (targets.Count == 0) return;

            _document.ApplyEdit(() =>
            {
                foreach (ElementSpec el in targets)
                {
                    if (!_dragStart.TryGetValue(el, out var start)) continue;
                    Vector2 pos = start.pos + canvasDelta;
                    el.position = new[] { Round(pos.x), Round(pos.y) };
                }
            }, targets.Count > 1 ? "Move Elements" : "Move Element");
            SelectInTree(_primary);
        }

        private void ReparentSelection(ElementSpec target)
        {
            if (!_index.TryGetValue(target, out Node targetNode)) return;
            bool layout = LayoutKinds.Contains(target.kind);
            var moving = new List<ElementSpec>();
            foreach (ElementSpec el in _selection)
                if (_index.ContainsKey(el) && !ReferenceEquals(el, target)) moving.Add(el);
            if (moving.Count == 0) return;

            _document.ApplyEdit(() =>
            {
                foreach (ElementSpec el in moving)
                {
                    if (!_index.TryGetValue(el, out Node node)) continue;
                    node.siblings.Remove(el);
                    if (target.children == null) target.children = new List<ElementSpec>();
                    target.children.Add(el);
                    // a layout group owns child placement — drop the now-meaningless free position
                    if (layout) { el.position = null; }
                }
            }, moving.Count > 1 ? "Reparent Elements" : "Reparent Element");

            // re-address: the primary is now the last child of the target
            string targetPath = targetNode.path;
            int newIndex = (target.children?.Count ?? 1) - 1;
            _selectPath?.Invoke($"{targetPath}/children[{newIndex}]");
        }

        // ------------------------------------------------------------------ commit: resize

        private void CommitResize(Vector2 mouse)
        {
            if (_primary == null || !IsFree(_primary) || !_dragStart.TryGetValue(_primary, out var start)
                || !TryScreenRect(_primary, out Rect startScreen)
                || _boxes == null || !_boxes.TryGetValue(_primary, out ElementBox box)) return;

            // The ghost rect IS the result the user is dragging toward — commit to exactly that, so the
            // committed element matches the outline pixel-for-pixel regardless of the element's pivot
            // (the bug was assuming a centred pivot, which translated off-pivot elements as they grew).
            Rect ghost = ResizeGhost(startScreen, mouse - _mouseDownScreen);

            Vector2 newSize = new Vector2(Mathf.Max(1f, ghost.width / _scale), Mathf.Max(1f, ghost.height / _scale));
            Vector2 dSize = newSize - start.size;
            // anchoredPosition places the PIVOT; the rect centre sits (0.5 - pivot)*size from it. Keep the
            // centre tracking the ghost centre, then back out the pivot offset for the new size.
            Vector2 dCenter = ScreenToCanvasDelta(ghost.center - startScreen.center);
            Vector2 newPos = start.pos + dCenter
                - new Vector2((0.5f - box.pivot.x) * dSize.x, (0.5f - box.pivot.y) * dSize.y);

            _document.ApplyEdit(() =>
            {
                _primary.size = new[] { Round(newSize.x), Round(newSize.y) };
                _primary.position = new[] { Round(newPos.x), Round(newPos.y) };
            }, "Resize Element");
            SelectInTree(_primary);
        }

        // ------------------------------------------------------------------ snapping

        // snap the moved rect's edges to sibling edges, else to the spacing grid; record guide lines
        private Vector2 SnapMove(Vector2 screenDelta)
        {
            _snapGuideV.x = float.NaN; _snapGuideH.y = float.NaN;
            if (Event.current.alt || _primary == null || !TryScreenRect(_primary, out Rect start)) return screenDelta;

            Rect moved = new Rect(start.position + screenDelta, start.size);
            float sx = SnapAxis(new[] { moved.xMin, moved.center.x, moved.xMax }, SiblingEdgesX(), out bool hitX, out float guideX);
            float sy = SnapAxis(new[] { moved.yMin, moved.center.y, moved.yMax }, SiblingEdgesY(), out bool hitY, out float guideY);
            if (hitX) _snapGuideV.x = guideX; else sx = SnapGrid(moved.xMin, true);
            if (hitY) _snapGuideH.y = guideY; else sy = SnapGrid(moved.yMin, false);
            return new Vector2(screenDelta.x + sx, screenDelta.y + sy);
        }

        // returns the offset to add to bring one of the candidate positions onto the nearest target edge
        private float SnapAxis(float[] candidates, List<float> targets, out bool hit, out float guide)
        {
            hit = false; guide = 0f;
            float best = SnapPx;
            float offset = 0f;
            foreach (float c in candidates)
                foreach (float t in targets)
                {
                    float d = t - c;
                    if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); offset = d; guide = t; hit = true; }
                }
            return offset;
        }

        private float SnapGrid(float screenEdge, bool horizontal)
        {
            float gridScreen = GridCanvas * _scale;
            if (gridScreen < 2f) return 0f;
            float origin = horizontal ? _drawRect.x : _drawRect.y;
            float rel = screenEdge - origin;
            float snapped = Mathf.Round(rel / gridScreen) * gridScreen;
            float offset = (origin + snapped) - screenEdge;
            return Mathf.Abs(offset) <= SnapPx ? offset : 0f;
        }

        private List<float> SiblingEdgesX()
        {
            var edges = new List<float>();
            foreach (ElementSpec sib in Siblings())
                if (TryScreenRect(sib, out Rect r)) { edges.Add(r.xMin); edges.Add(r.center.x); edges.Add(r.xMax); }
            return edges;
        }

        private List<float> SiblingEdgesY()
        {
            var edges = new List<float>();
            foreach (ElementSpec sib in Siblings())
                if (TryScreenRect(sib, out Rect r)) { edges.Add(r.yMin); edges.Add(r.center.y); edges.Add(r.yMax); }
            return edges;
        }

        private IEnumerable<ElementSpec> Siblings()
        {
            if (_primary == null || !_index.TryGetValue(_primary, out Node node)) yield break;
            foreach (ElementSpec sib in node.siblings)
                if (!ReferenceEquals(sib, _primary) && !_selection.Contains(sib)) yield return sib;
        }

        // ------------------------------------------------------------------ overlay drawing

        private void DrawOverlay()
        {
            if (Event.current.type != EventType.Repaint) return;

            // drop-target container
            if (_dropTarget != null && TryScreenRect(_dropTarget, out Rect dropRect))
                DrawBox(dropRect, NeoColors.Add, 2f, NeoColors.Add.WithAlpha(0.08f));

            // selection outlines
            foreach (ElementSpec el in _selection)
                if (TryScreenRect(el, out Rect r))
                    DrawBox(r, ReferenceEquals(el, _primary) ? NeoColors.Interactive : NeoColors.Interactive.WithAlpha(0.5f), 1f, default);

            // ghost while moving
            if (_mode == Mode.Move && _primary != null && TryScreenRect(_primary, out Rect start))
            {
                Vector2 delta = SnapMove(Event.current.mousePosition - _mouseDownScreen);
                DrawBox(new Rect(start.position + delta, start.size), NeoColors.Interactive, 1f,
                    NeoColors.Interactive.WithAlpha(0.06f));
            }

            // ghost while resizing
            if (_mode == Mode.Resize && _primary != null && TryScreenRect(_primary, out Rect rstart))
                DrawBox(ResizeGhost(rstart, Event.current.mousePosition - _mouseDownScreen),
                    NeoColors.Interactive, 1f, NeoColors.Interactive.WithAlpha(0.06f));

            // resize handles on the primary (free elements only)
            if (_primary != null && IsFree(_primary) && _mode != Mode.Move && _mode != Mode.Marquee
                && TryScreenRect(_primary, out Rect pr))
                foreach (Vector2 h in HandlePositions(pr))
                    EditorGUI.DrawRect(new Rect(h.x - 3f, h.y - 3f, 6f, 6f), NeoColors.Interactive);

            // snap guides
            if (!float.IsNaN(_snapGuideV.x))
                EditorGUI.DrawRect(new Rect(_snapGuideV.x, _drawRect.y, 1f, _drawRect.height), NeoColors.Warning);
            if (!float.IsNaN(_snapGuideH.y))
                EditorGUI.DrawRect(new Rect(_drawRect.x, _snapGuideH.y, _drawRect.width, 1f), NeoColors.Warning);

            // marquee
            if (_mode == Mode.Marquee)
                DrawBox(_marquee, NeoColors.Interactive, 1f, NeoColors.Interactive.WithAlpha(0.08f));
        }

        // the live screen rect a resize gesture is producing — start rect with the handle's edges
        // pushed by the screen-space drag (mirrors CommitResize, kept in screen px for drawing)
        private Rect ResizeGhost(Rect start, Vector2 d)
        {
            float xMin = start.xMin, xMax = start.xMax, yMin = start.yMin, yMax = start.yMax;
            switch (_resizeHandle)
            {
                case 0: xMin += d.x; yMin += d.y; break;
                case 1: yMin += d.y; break;
                case 2: xMax += d.x; yMin += d.y; break;
                case 3: xMax += d.x; break;
                case 4: xMax += d.x; yMax += d.y; break;
                case 5: yMax += d.y; break;
                case 6: xMin += d.x; yMax += d.y; break;
                case 7: xMin += d.x; break;
            }
            return new Rect(Mathf.Min(xMin, xMax), Mathf.Min(yMin, yMax),
                Mathf.Abs(xMax - xMin), Mathf.Abs(yMax - yMin));
        }

        private static void DrawBox(Rect rect, Color edge, float thickness, Color fill)
        {
            if (fill.a > 0f) EditorGUI.DrawRect(rect, fill);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), edge);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), edge);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), edge);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), edge);
        }

        // ------------------------------------------------------------------ helpers

        private void CaptureDragStarts()
        {
            _dragStart.Clear();
            foreach (ElementSpec el in _selection)
                if (_boxes != null && _boxes.TryGetValue(el, out ElementBox box))
                    _dragStart[el] = (box.anchoredPos, box.size);
        }

        private bool IsFree(ElementSpec element) =>
            _index.TryGetValue(element, out Node node) && node.free;

        private ElementSpec FirstSelected()
        {
            foreach (ElementSpec el in _selection) return el;
            return null;
        }

        private void SelectInTree(ElementSpec element)
        {
            if (element != null && _index.TryGetValue(element, out Node node))
            {
                _lastExternalPrimary = element; // we initiated this — don't let SyncExternalSelection reset us
                _selectPath?.Invoke(node.path);
            }
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        private static Rect RectFromPoints(Vector2 a, Vector2 b) => new Rect(
            Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
}
