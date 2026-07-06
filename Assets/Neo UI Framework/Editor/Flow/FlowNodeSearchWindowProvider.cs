using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Blueprint-style "search as you type" node creation for the flow graph. One instance is owned
    /// by <see cref="FlowGraphView"/> and reused across opens (<c>SearchWindow.Open&lt;T&gt;</c>
    /// requires a live <see cref="ScriptableObject"/>). Backs two entry points:
    /// <list type="bullet">
    /// <item><see cref="GraphView.nodeCreationRequest"/> — the canvas's built-in "Create Node" entry
    /// point (right-click empty space / the stock node-creation shortcut).</item>
    /// <item><see cref="FlowPortConnectorListener.OnDropOutsidePort"/> — dragging a connection off an
    /// output port and releasing over empty space (<see cref="PrepareForPortDrag"/> remembers the
    /// source port so the picked node's inbound edge gets wired automatically).</item>
    /// </list>
    /// The tree also lists every executable node under "Go To Node" (general create only — jumping
    /// makes no sense mid port-drag) so the same picker doubles as a fast node-name finder.
    /// </summary>
    internal class FlowNodeSearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        private FlowGraphView _view;
        private EditorWindow _window;

        // Set just before opening for a drag-off-port create; cleared for a plain canvas create.
        private FlowNodeView _sourceNodeView;
        private int _sourcePortIndex = -1;

        public void Initialize(FlowGraphView view, EditorWindow window)
        {
            _view = view;
            _window = window;
        }

        /// <summary> Call before <see cref="SearchWindow.Open{T}"/> for a plain canvas create. </summary>
        public void PrepareGeneralCreate()
        {
            _sourceNodeView = null;
            _sourcePortIndex = -1;
        }

        /// <summary> Call before <see cref="SearchWindow.Open{T}"/> when opened by dragging a
        /// connection off an output port into empty space. </summary>
        public void PrepareForPortDrag(FlowNodeView sourceNodeView, int sourcePortIndex)
        {
            _sourceNodeView = sourceNodeView;
            _sourcePortIndex = sourcePortIndex;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Create Flow Node")) };

            tree.Add(new SearchTreeGroupEntry(new GUIContent("Create Node"), 1));
            foreach (FlowNodeDescriptor descriptor in FlowNodeKinds.All)
                tree.Add(new SearchTreeEntry(new GUIContent(descriptor.menuLabel)) { level = 2, userData = descriptor });

            // Jumping to an existing node only makes sense outside a port-drag — there is nothing
            // to wire a jump target's inbound edge to.
            if (_sourceNodeView == null && _view != null && _view.Graph != null)
            {
                List<string> executables = _view.Graph.nodes
                    .Where(n => n != null && n.isExecutable && !string.IsNullOrEmpty(n.name))
                    .Select(n => n.name)
                    .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (executables.Count > 0)
                {
                    tree.Add(new SearchTreeGroupEntry(new GUIContent("Go To Node"), 1));
                    foreach (string name in executables)
                        tree.Add(new SearchTreeEntry(new GUIContent(name)) { level = 2, userData = name });
                }
            }
            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is FlowNodeDescriptor descriptor)
            {
                Vector2 graphPosition = ToGraphPosition(context.screenMousePosition);
                if (_sourceNodeView != null)
                    _view.CreateNodeFromPortDrag(descriptor, graphPosition, _sourceNodeView, _sourcePortIndex);
                else
                    _view.CreateNodeFromSearch(descriptor, graphPosition);
                return true;
            }
            if (entry.userData is string nodeName)
            {
                _view.JumpToNodeByName(nodeName);
                return true;
            }
            return false;
        }

        /// <summary> Standard SearchWindow screen-to-graph conversion: the window's root visual
        /// element resolves window-local coordinates, which the content view container then maps
        /// into graph space. </summary>
        private Vector2 ToGraphPosition(Vector2 screenMousePosition)
        {
            VisualElement root = _window.rootVisualElement;
            Vector2 windowMousePosition = root.ChangeCoordinatesTo(root.parent, screenMousePosition - _window.position.position);
            return _view.contentViewContainer.WorldToLocal(windowMousePosition);
        }
    }
}
