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
    /// Tier 3 (Plan 2 Phase 4) + Pillar D: the WYSIWYG canvas. Turns the center preview into a Figma-grade
    /// direct-manipulation surface — click to select, drag to move, handles to resize, marquee to
    /// multi-select, drag-onto-a-container to reparent, drag-to-reorder inside a layout group, smart
    /// alignment guides + equal-spacing snapping, a multi-select align/distribute toolbar, and keyboard
    /// nudge/duplicate/delete.
    ///
    /// <para><b>Constraint-aware writeback (Pillar D correctness keystone).</b> A move/resize no longer
    /// writes an absolute <c>position</c>/<c>size</c>; it writes the element's new device-space rect into
    /// its <c>layout</c> offsets, stored RELATIVE TO ITS CURRENT CONSTRAINT against the live parent rect
    /// (<see cref="ConstraintWriteback"/>). So a right-glued element stays glued, a stretched element
    /// keeps its insets, and a scaled element keeps its fractions across a viewport aspect change.</para>
    ///
    /// <para>Moves/resizes are committed ONCE on mouse-up (a single undo step); during the drag a ghost
    /// outline tracks the cursor so it feels live without rebuilding the texture on every mouse move.
    /// Selection is held by <see cref="ElementSpec"/> reference so it survives a viewport resize/rebuild
    /// (handles/guides recompute from the fresh <c>_boxes</c>, never stale device px).</para>
    /// </summary>
    public class ComposerCanvas
    {
        private const float HandleSize = 8f;
        private const float SnapPx = 6f;
        private const float GridCanvas = 8f;    // the on-scale spacing grid the design lint blesses
        private const float DragThreshold = 3f;
        private const float DuplicateOffset = 16f; // canvas px a Ctrl+D clone is nudged by

        private readonly SpecDocument _document;
        private readonly Action<string> _selectPath;
        private readonly Action _repaint;
        private readonly AlignmentGuides _smartGuides = new AlignmentGuides();

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
        private int _reorderIndex = -1;          // insertion index when reordering within a layout group
        private ElementSpec _reorderParent;      // the layout-group parent the reorder targets
        private Vector2 _snapGuideV = new Vector2(float.NaN, 0f); // x of a vertical snap guide (screen)
        private Vector2 _snapGuideH = new Vector2(0f, float.NaN); // y of a horizontal snap guide (screen)

        // cached toolbar style (built once — never allocate GUIStyles per OnGUI pass)
        private GUIStyle _toolbarButton;

        private static readonly HashSet<string> FreeParents = new HashSet<string> { "overlay", "safearea" };
        private static readonly HashSet<string> ContainerKinds =
            new HashSet<string> { "vstack", "hstack", "grid", "scroll", "list", "panel", "overlay", "safearea" };

        /// <summary> Whether a spec kind is a layout container that nests child elements. The canvas
        /// treats these as drop parents; the palette click-to-add (<see cref="NeoComposerWindow"/>) nests
        /// the new widget INTO a selected one rather than appending it to the view root. Single source so
        /// the two stay in lockstep. Built-ins live in <see cref="ContainerKinds"/>; a project-registered
        /// kind opts in by implementing <see cref="IElementKindContainer"/> (the extension seam — a custom
        /// container becomes first-class on the canvas and in click-to-add with zero package edits). </summary>
        public static bool IsContainerKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            if (ContainerKinds.Contains(kind)) return true;
            return NeoElementKinds.TryGet(kind, out INeoElementKind ek)
                   && ek is IElementKindContainer container && container.AcceptsChildren;
        }
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
            HandlePaletteDrop();
            HandleKeyboard();
            HandleEvents();
            DrawOverlay();
            DrawAlignToolbar();
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

        // The device-space size of the whole preview (canvas px). _drawRect = deviceSize * _scale.
        private Vector2 DeviceSize => _scale > 0f
            ? new Vector2(_drawRect.width / _scale, _drawRect.height / _scale)
            : new Vector2(_drawRect.width, _drawRect.height);

        // An element's DEVICE-space rect (y grows UP, bottom-origin) — the space ConstraintWriteback works
        // in. box.norm is normalized y-from-bottom, so device = norm * deviceSize with no flip.
        private bool TryDeviceRect(ElementSpec element, out Rect device)
        {
            device = default;
            if (element == null || _boxes == null || !_boxes.TryGetValue(element, out ElementBox box)) return false;
            device = NormToDevice(box.norm);
            return true;
        }

        private Rect NormToDevice(Rect n)
        {
            Vector2 d = DeviceSize;
            return new Rect(n.x * d.x, n.y * d.y, n.width * d.x, n.height * d.y);
        }

        // The device-space rect of an element's effective PARENT — the free container it lives in, or the
        // whole canvas for a top-level element.
        private Rect ParentDeviceRect(ElementSpec element)
        {
            if (_index.TryGetValue(element, out Node node) && node.parent != null
                && TryDeviceRect(node.parent, out Rect parentRect))
                return parentRect;
            Vector2 d = DeviceSize;
            return new Rect(0f, 0f, d.x, d.y);
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
            _dropTarget = null; _reorderIndex = -1; _reorderParent = null;
            _smartGuides.Clear();
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
                    ComputeReorder(e.mousePosition);
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
            _dropTarget = null; _reorderIndex = -1; _reorderParent = null;
            _snapGuideV.x = float.NaN; _snapGuideH.y = float.NaN;
            _smartGuides.Clear();
            _dragStart.Clear();
            if (GUIUtility.hotControl == _controlId) GUIUtility.hotControl = 0;
            e.Use();
        }

        // ------------------------------------------------------------------ keyboard affordances

        private void HandleKeyboard()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || _primary == null) return;
            if (_mode == Mode.Move || _mode == Mode.Resize || _mode == Mode.Marquee) return;

            switch (e.keyCode)
            {
                case KeyCode.LeftArrow: NudgeSelection(new Vector2(-(e.shift ? 10f : 1f), 0f)); e.Use(); break;
                case KeyCode.RightArrow: NudgeSelection(new Vector2(e.shift ? 10f : 1f, 0f)); e.Use(); break;
                case KeyCode.UpArrow: NudgeSelection(new Vector2(0f, e.shift ? 10f : 1f)); e.Use(); break;
                case KeyCode.DownArrow: NudgeSelection(new Vector2(0f, -(e.shift ? 10f : 1f))); e.Use(); break;
                case KeyCode.Delete:
                case KeyCode.Backspace: DeleteSelection(); e.Use(); break;
                case KeyCode.D when e.control || e.command: DuplicateSelection(); e.Use(); break;
            }
        }

        // arrow nudge: shift the free elements' device rects by a canvas-px delta (y up), constraint-aware
        private void NudgeSelection(Vector2 canvasDelta)
        {
            var targets = new List<ElementSpec>();
            foreach (ElementSpec el in _selection) if (IsFree(el)) targets.Add(el);
            if (targets.Count == 0) return;

            _document.ApplyEdit(() =>
            {
                foreach (ElementSpec el in targets)
                {
                    if (!TryDeviceRect(el, out Rect device)) continue;
                    var moved = new Rect(device.x + canvasDelta.x, device.y + canvasDelta.y, device.width, device.height);
                    ConstraintWriteback.Write(el, moved, ParentDeviceRect(el));
                }
            }, targets.Count > 1 ? "Nudge Elements" : "Nudge Element");
            SelectInTree(_primary);
        }

        private void DeleteSelection()
        {
            var targets = new List<ElementSpec>(_selection);
            if (targets.Count == 0) return;
            _document.ApplyEdit(() =>
            {
                foreach (ElementSpec el in targets)
                    if (_index.TryGetValue(el, out Node node)) node.siblings.Remove(el);
            }, targets.Count > 1 ? "Delete Elements" : "Delete Element");
            _selection.Clear();
            _primary = null;
            _lastExternalPrimary = null;
            _selectPath?.Invoke(null);
        }

        private void DuplicateSelection()
        {
            var sources = new List<ElementSpec>(_selection);
            if (sources.Count == 0) return;
            ElementSpec lastClone = null;
            string lastClonePath = null;

            _document.ApplyEdit(() =>
            {
                foreach (ElementSpec src in sources)
                {
                    if (!_index.TryGetValue(src, out Node node)) continue;
                    ElementSpec clone = CloneElement(src);
                    if (IsFree(clone) && TryDeviceRect(src, out Rect device))
                    {
                        // offset the clone slightly so it doesn't sit exactly on the original
                        var moved = new Rect(device.x + DuplicateOffset, device.y - DuplicateOffset,
                            device.width, device.height);
                        ConstraintWriteback.Write(clone, moved, ParentDeviceRect(src));
                    }
                    int insertAt = node.index + 1;
                    node.siblings.Insert(insertAt, clone);
                    lastClone = clone;
                    lastClonePath = ParentChildPath(node) + insertAt + "]";
                }
            }, sources.Count > 1 ? "Duplicate Elements" : "Duplicate Element");

            if (lastClone != null)
            {
                _selection.Clear();
                _selection.Add(lastClone);
                _primary = lastClone;
                _lastExternalPrimary = lastClone;
                _selectPath?.Invoke(lastClonePath);
            }
        }

        // path prefix up to (but not including) the index for a sibling list, e.g. ".../children["
        private string ParentChildPath(Node node)
        {
            if (node.parent != null && _index.TryGetValue(node.parent, out Node pn))
                return pn.path + "/children[";
            string ownerPath = _view != null ? SpecPath.View(_view.id)
                : _popup != null ? SpecPath.Popup(_popup.name) : null;
            return ownerPath + "/elements[";
        }

        // round-trip-safe deep clone via the spec's own JSON serialization
        private static ElementSpec CloneElement(ElementSpec src) =>
            ElementSpec.Parse(src.ToJsonObject());

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

        // ------------------------------------------------------------------ commit: move / reparent / reorder

        private void CommitMove(Vector2 mouse)
        {
            // reorder wins inside the element's own layout-group parent (an in-container drag)
            if (_reorderParent != null && _reorderIndex >= 0
                && _index.TryGetValue(_primary, out Node moveNode)
                && ReferenceEquals(_reorderParent, moveNode.parent))
            {
                ReorderWithinParent(moveNode);
                return;
            }

            // reparent wins when the drop lands on a different container than the element's current parent
            if (_dropTarget != null && _index.TryGetValue(_primary, out Node primaryNode)
                && !ReferenceEquals(_dropTarget, primaryNode.parent))
            {
                ReparentSelection(_dropTarget);
                return;
            }

            if (_primary == null || !IsFree(_primary)) return; // layout children only reparent/reorder

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
                    if (!TryDeviceRect(el, out Rect device)) continue;
                    // the captured device rect is the START rect; add the canvas-px delta (y up)
                    var moved = new Rect(device.x + canvasDelta.x, device.y + canvasDelta.y,
                        device.width, device.height);
                    ConstraintWriteback.Write(el, moved, ParentDeviceRect(el));
                }
            }, targets.Count > 1 ? "Move Elements" : "Move Element");
            SelectInTree(_primary);
        }

        // Drag-to-reorder: compute the insertion index from the cursor vs sibling box midpoints. vstack =
        // vertical (screen y), hstack/grid = horizontal (screen x), row-major for grid.
        private void ComputeReorder(Vector2 mouse)
        {
            _reorderIndex = -1; _reorderParent = null;
            if (_primary == null || !_index.TryGetValue(_primary, out Node node)) return;
            if (node.parent == null || !LayoutKinds.Contains(node.parent.kind)) return;
            // only reorder when the cursor stays over the same parent (else it's a reparent gesture)
            if (_dropTarget != null && !ReferenceEquals(_dropTarget, node.parent)) return;

            bool horizontal = node.parent.kind == "hstack" || node.parent.kind == "grid";
            int index = 0;
            for (int i = 0; i < node.siblings.Count; i++)
            {
                ElementSpec sib = node.siblings[i];
                if (ReferenceEquals(sib, _primary)) continue;
                if (!TryScreenRect(sib, out Rect r)) continue;
                float mid = horizontal ? r.center.x : r.center.y;
                float cursor = horizontal ? mouse.x : mouse.y;
                if (cursor > mid) index = i + 1;
            }
            _reorderParent = node.parent;
            _reorderIndex = index;
        }

        private void ReorderWithinParent(Node moveNode)
        {
            List<ElementSpec> siblings = moveNode.siblings;
            int from = moveNode.index;
            int to = _reorderIndex;
            if (to == from || to == from + 1) { SelectInTree(_primary); return; } // no-op move

            ElementSpec moving = _primary;
            _document.ApplyEdit(() =>
            {
                siblings.RemoveAt(from);
                int insertAt = to > from ? to - 1 : to;     // account for the removed slot
                insertAt = Mathf.Clamp(insertAt, 0, siblings.Count);
                siblings.Insert(insertAt, moving);
            }, "Reorder Element");

            // re-address: find the moving element's new index in its (unchanged) sibling list
            int newIndex = siblings.IndexOf(moving);
            _selectPath?.Invoke(ParentChildPath(moveNode) + newIndex + "]");
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
                    // a layout group owns child placement — drop the now-meaningless free position/layout
                    if (layout) { el.position = null; el.layout = null; }
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
            if (_primary == null || !IsFree(_primary) || !TryScreenRect(_primary, out Rect startScreen)) return;

            // The ghost rect IS the result the user is dragging toward — convert it to a device rect and
            // write it constraint-aware, so the committed element matches the outline AND survives a
            // viewport aspect change (it stores offsets against its constraint, not absolute pixels).
            Rect ghost = ResizeGhost(startScreen, mouse - _mouseDownScreen);
            Rect device = ScreenRectToDevice(ghost);
            if (device.width < 1f) device.width = 1f;
            if (device.height < 1f) device.height = 1f;

            _document.ApplyEdit(() => ConstraintWriteback.Write(_primary, device, ParentDeviceRect(_primary)),
                "Resize Element");
            SelectInTree(_primary);
        }

        // a screen-px rect (y down) → device-px rect (y up), inverse of ToScreen∘NormToDevice
        private Rect ScreenRectToDevice(Rect screen)
        {
            Vector2 d = DeviceSize;
            float nx = (screen.x - _drawRect.x) / _drawRect.width;
            float nyTop = (screen.y - _drawRect.y) / _drawRect.height;        // 0 at top
            float nw = screen.width / _drawRect.width;
            float nh = screen.height / _drawRect.height;
            float nyBottom = 1f - nyTop - nh;                                 // flip to y-from-bottom
            return new Rect(nx * d.x, nyBottom * d.y, nw * d.x, nh * d.y);
        }

        // ------------------------------------------------------------------ snapping

        // snap the moved rect's edges to sibling edges via smart guides, else to the spacing grid
        private Vector2 SnapMove(Vector2 screenDelta)
        {
            _snapGuideV.x = float.NaN; _snapGuideH.y = float.NaN;
            _smartGuides.Clear();
            if (Event.current.alt || _primary == null || !TryScreenRect(_primary, out Rect start)) return screenDelta;

            Rect moved = new Rect(start.position + screenDelta, start.size);
            List<Rect> siblings = SiblingScreenRects();
            Vector2 guideSnap = _smartGuides.Compute(moved, siblings);

            // grid fallback on any axis the smart guides didn't already pin
            float sx = guideSnap.x;
            float sy = guideSnap.y;
            bool hitX = Mathf.Abs(guideSnap.x) > 0.0001f || HasVerticalGuide();
            bool hitY = Mathf.Abs(guideSnap.y) > 0.0001f || HasHorizontalGuide();
            if (!hitX) sx = SnapGrid(moved.xMin, true);
            if (!hitY) sy = SnapGrid(moved.yMin, false);
            return new Vector2(screenDelta.x + sx, screenDelta.y + sy);
        }

        private bool HasVerticalGuide()
        {
            foreach (AlignmentGuides.Guide g in _smartGuides.Guides) if (g.vertical) return true;
            return false;
        }

        private bool HasHorizontalGuide()
        {
            foreach (AlignmentGuides.Guide g in _smartGuides.Guides) if (!g.vertical) return true;
            return false;
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

        private List<Rect> SiblingScreenRects()
        {
            var rects = new List<Rect>();
            foreach (ElementSpec sib in Siblings())
                if (TryScreenRect(sib, out Rect r)) rects.Add(r);
            return rects;
        }

        private IEnumerable<ElementSpec> Siblings()
        {
            if (_primary == null || !_index.TryGetValue(_primary, out Node node)) yield break;
            foreach (ElementSpec sib in node.siblings)
                if (!ReferenceEquals(sib, _primary) && !_selection.Contains(sib)) yield return sib;
        }

        // ------------------------------------------------------------------ multi-select align / distribute

        private void DrawAlignToolbar()
        {
            if (_selection.Count < 2) return;
            EnsureToolbarStyles();

            // overlay toolbar pinned top-left of the canvas
            const float h = 22f, pad = 4f;
            float x = _drawRect.x + 6f, y = _drawRect.y + 6f;
            string[] labels = { "L", "C", "R", "T", "M", "B", "↔", "↕" };
            AlignDistribute.Op[] ops =
            {
                AlignDistribute.Op.Left, AlignDistribute.Op.CenterX, AlignDistribute.Op.Right,
                AlignDistribute.Op.Top, AlignDistribute.Op.CenterY, AlignDistribute.Op.Bottom,
                AlignDistribute.Op.DistributeH, AlignDistribute.Op.DistributeV
            };

            float w = 22f;
            float total = labels.Length * w + pad * 2f;
            var bg = new Rect(x, y, total, h);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bg, new Color(0f, 0f, 0f, 0.6f));

            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(x + pad + i * w, y + 2f, w, h - 4f);
                if (GUI.Button(r, new GUIContent(labels[i], AlignDistribute.Label(ops[i])), _toolbarButton))
                    ApplyAlign(ops[i]);
            }
        }

        private void EnsureToolbarStyles()
        {
            _toolbarButton ??= new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };
        }

        // Align/distribute snapshots every selected free element's device rect, runs the pure
        // AlignDistribute math, then writes each result back through the constraint model — one
        // ApplyEdit = one undo. Only free elements participate.
        private void ApplyAlign(AlignDistribute.Op op)
        {
            var rects = new Dictionary<ElementSpec, Rect>();
            foreach (ElementSpec el in _selection)
                if (IsFree(el) && TryDeviceRect(el, out Rect d)) rects[el] = d;
            if (rects.Count < 2) return;

            Dictionary<ElementSpec, Rect> result = AlignDistribute.Apply(op, rects);

            _document.ApplyEdit(() =>
            {
                foreach (var kv in result)
                    ConstraintWriteback.Write(kv.Key, kv.Value, ParentDeviceRect(kv.Key));
            }, AlignDistribute.Label(op));
            SelectInTree(_primary);
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
                _smartGuides.Draw(_drawRect);
                DrawReorderLine();
            }

            // ghost while resizing
            if (_mode == Mode.Resize && _primary != null && TryScreenRect(_primary, out Rect rstart))
                DrawBox(ResizeGhost(rstart, Event.current.mousePosition - _mouseDownScreen),
                    NeoColors.Interactive, 1f, NeoColors.Interactive.WithAlpha(0.06f));

            // resize handles on the primary (free elements only)
            if (_primary != null && IsFree(_primary) && _mode != Mode.Move && _mode != Mode.Marquee
                && TryScreenRect(_primary, out Rect pr))
                foreach (Vector2 hp in HandlePositions(pr))
                    EditorGUI.DrawRect(new Rect(hp.x - 3f, hp.y - 3f, 6f, 6f), NeoColors.Interactive);

            // legacy single-line snap guides (grid fallback retains these)
            if (!float.IsNaN(_snapGuideV.x))
                EditorGUI.DrawRect(new Rect(_snapGuideV.x, _drawRect.y, 1f, _drawRect.height), NeoColors.Warning);
            if (!float.IsNaN(_snapGuideH.y))
                EditorGUI.DrawRect(new Rect(_drawRect.x, _snapGuideH.y, _drawRect.width, 1f), NeoColors.Warning);

            // marquee
            if (_mode == Mode.Marquee)
                DrawBox(_marquee, NeoColors.Interactive, 1f, NeoColors.Interactive.WithAlpha(0.08f));
        }

        // a 2px blue insertion line between two siblings of the layout-group parent being reordered
        private void DrawReorderLine()
        {
            if (_reorderParent == null || _reorderIndex < 0
                || !_index.TryGetValue(_primary, out Node node)
                || !ReferenceEquals(_reorderParent, node.parent)) return;

            bool horizontal = node.parent.kind == "hstack" || node.parent.kind == "grid";
            List<Rect> ordered = SiblingRectsInOrder(node);
            if (ordered.Count == 0)
            {
                // empty target — draw inside the parent's rect
                if (TryScreenRect(node.parent, out Rect pr))
                    EditorGUI.DrawRect(new Rect(pr.x + 4f, pr.center.y - 1f, pr.width - 8f, 2f), NeoColors.Interactive);
                return;
            }

            // clamp the insertion index against the (primary-excluded) ordered list
            int idx = Mathf.Clamp(_reorderIndex, 0, ordered.Count);
            if (horizontal)
            {
                float lineX = idx == 0 ? ordered[0].xMin - 2f
                    : idx >= ordered.Count ? ordered[ordered.Count - 1].xMax + 1f
                    : (ordered[idx - 1].xMax + ordered[idx].xMin) * 0.5f;
                float top = float.MaxValue, bot = float.MinValue;
                foreach (Rect r in ordered) { top = Mathf.Min(top, r.yMin); bot = Mathf.Max(bot, r.yMax); }
                EditorGUI.DrawRect(new Rect(lineX, top, 2f, bot - top), NeoColors.Interactive);
            }
            else
            {
                float lineY = idx == 0 ? ordered[0].yMin - 2f
                    : idx >= ordered.Count ? ordered[ordered.Count - 1].yMax + 1f
                    : (ordered[idx - 1].yMax + ordered[idx].yMin) * 0.5f;
                float left = float.MaxValue, right = float.MinValue;
                foreach (Rect r in ordered) { left = Mathf.Min(left, r.xMin); right = Mathf.Max(right, r.xMax); }
                EditorGUI.DrawRect(new Rect(left, lineY, right - left, 2f), NeoColors.Interactive);
            }
        }

        // sibling screen rects (excluding the dragged primary) in their list order
        private List<Rect> SiblingRectsInOrder(Node node)
        {
            var rects = new List<Rect>();
            foreach (ElementSpec sib in node.siblings)
            {
                if (ReferenceEquals(sib, _primary)) continue;
                if (TryScreenRect(sib, out Rect r)) rects.Add(r);
            }
            return rects;
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

        private static Rect RectFromPoints(Vector2 a, Vector2 b) => new Rect(
            Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        // ------------------------------------------------------------------ palette drag-to-create (Pillar E)

        private const float PaletteDropW = 160f;   // device-px default footprint for a free-dropped widget
        private const float PaletteDropH = 48f;

        /// <summary>
        /// Pillar E drag-to-create: accepts a <see cref="ComposerPalette"/> tile dragged onto the canvas.
        /// On drop it finds the hovered container (the existing <see cref="FindDropTarget"/>), creates a
        /// <see cref="ComposerFactory.NewElement"/> for the carried kind, and places it at the cursor —
        /// for a free parent (overlay/safearea/top-level) it computes a constraint-correct <c>layout</c>
        /// centered on the cursor via <see cref="ConstraintWriteback"/>; for a layout-group parent it just
        /// inserts (the group owns placement). One <see cref="SpecDocument.ApplyEdit"/> = one undo step.
        /// This is the ONLY Pillar-E edit to this file (an appended method + its call in OnGUI).
        /// </summary>
        private void HandlePaletteDrop()
        {
            Event e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!_drawRect.Contains(e.mousePosition)) return;

            string kind = DragAndDrop.GetGenericData(ComposerPalette.DragKey) as string;
            if (string.IsNullOrEmpty(kind)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragUpdated)
            {
                // surface the container the drop would land in
                _dropTarget = FindDropTargetForKind(e.mousePosition);
                _repaint?.Invoke();
                e.Use();
                return;
            }

            // DragPerform — create + insert
            DragAndDrop.AcceptDrag();
            ElementSpec target = FindDropTargetForKind(e.mousePosition);
            List<ElementSpec> destination = target != null && target.children != null ? target.children
                : target != null ? (target.children = new List<ElementSpec>())
                : (_view != null ? _view.elements : _popup?.elements);
            if (destination == null) { e.Use(); return; }

            bool intoLayoutGroup = target != null && LayoutKinds.Contains(target.kind);
            Vector2 dropMouse = e.mousePosition;
            int insertAt = intoLayoutGroup ? InsertIndexAt(target, destination, dropMouse) : destination.Count;

            ElementSpec created = ComposerFactory.NewElement(kind);
            string parentPath = target != null && _index.TryGetValue(target, out Node tnode)
                ? tnode.path : (_view != null ? SpecPath.View(_view.id)
                    : _popup != null ? SpecPath.Popup(_popup.name) : null);

            if (!intoLayoutGroup)
            {
                // free placement: a default footprint centered on the cursor, stored constraint-aware
                Rect parentDevice = target != null && TryDeviceRect(target, out Rect pd)
                    ? pd : new Rect(0f, 0f, DeviceSize.x, DeviceSize.y);
                Vector2 centerDevice = ScreenPointToDevice(dropMouse);
                var rect = new Rect(centerDevice.x - PaletteDropW * 0.5f, centerDevice.y - PaletteDropH * 0.5f,
                    PaletteDropW, PaletteDropH);
                _document.ApplyEdit(() =>
                {
                    destination.Insert(Mathf.Clamp(insertAt, 0, destination.Count), created);
                    ConstraintWriteback.Write(created, rect, parentDevice);
                }, $"Add {kind}");
            }
            else
            {
                _document.ApplyEdit(() => destination.Insert(Mathf.Clamp(insertAt, 0, destination.Count), created),
                    $"Add {kind}");
            }

            if (parentPath != null)
            {
                int finalIndex = destination.IndexOf(created);
                string marker = (target == null) ? "/elements[" : "/children[";
                _selectPath?.Invoke($"{parentPath}{marker}{finalIndex}]");
            }
            _dropTarget = null;
            _repaint?.Invoke();
            e.Use();
        }

        // a screen-px point → device-space point (y up), reusing the rect converter on a zero-size rect
        private Vector2 ScreenPointToDevice(Vector2 screen)
        {
            Rect d = ScreenRectToDevice(new Rect(screen.x, screen.y, 0f, 0f));
            return new Vector2(d.x, d.y);
        }

        // drop target that does NOT exclude the (empty) selection — a fresh element has no selection guard
        private ElementSpec FindDropTargetForKind(Vector2 point)
        {
            ElementSpec best = null;
            int bestDepth = -1;
            foreach (KeyValuePair<ElementSpec, Node> entry in _index)
            {
                Node node = entry.Value;
                if (!node.container) continue;
                if (!TryScreenRect(entry.Key, out Rect screen) || !screen.Contains(point)) continue;
                if (node.depth > bestDepth) { best = entry.Key; bestDepth = node.depth; }
            }
            return best;
        }

        // insertion index within a layout-group destination from the cursor vs sibling box midpoints
        private int InsertIndexAt(ElementSpec parent, List<ElementSpec> destination, Vector2 mouse)
        {
            bool horizontal = parent.kind == "hstack" || parent.kind == "grid";
            int index = destination.Count;
            for (int i = 0; i < destination.Count; i++)
            {
                if (!TryScreenRect(destination[i], out Rect r)) continue;
                float mid = horizontal ? r.center.x : r.center.y;
                float cursor = horizontal ? mouse.x : mouse.y;
                if (cursor < mid) { index = i; break; }
            }
            return index;
        }
    }
}
