using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// The confirm step of "Connect to…": summarizes the button→view wiring about to be written, lets
    /// the user disambiguate when several flow nodes already show the source/target view, pick a view
    /// transition and the allows-back flag, then calls <see cref="NeoFlowWiring"/>. Opened anchored to
    /// the scene-view overlay's "Connect →" button once a valid target view has been picked (see
    /// <see cref="NeoSceneOverlay"/>'s pick mode).
    /// </summary>
    public sealed class ConnectToPopup : PopupWindowContent
    {
        private readonly UIButton _button;
        private readonly UIView _sourceView;
        private readonly UIView _target;
        private readonly FlowGraph _graph;

        // null unless NodesShowingView found more than one candidate — then the row shows and the
        // selection is passed through as NeoFlowWiring's explicitFromNode/explicitToNode.
        private List<UINode> _fromCandidates;
        private List<UINode> _toCandidates;
        private string _selectedFromNode;
        private string _selectedToNode;

        private const string ProjectDefaultTransition = "(project default)";
        private readonly List<string> _transitionOptions;
        private string _transition = "";
        private bool _allowsBack = true;
        private string _error;

        private GUIStyle _header;

        public ConnectToPopup(UIButton button, UIView target)
        {
            _button = button;
            _target = target;
            _sourceView = button != null ? button.GetComponentInParent<UIView>(true) : null;

            FlowController controller = NeoFlowWiring.FindSceneController();
            _graph = controller != null ? controller.flow : null;

            if (_graph != null && _sourceView != null && target != null)
            {
                List<UINode> fromCandidates = NeoFlowWiring.NodesShowingView(_graph, _sourceView.id.Category, _sourceView.id.Name);
                if (fromCandidates.Count > 1)
                {
                    _fromCandidates = fromCandidates;
                    _selectedFromNode = fromCandidates[0].name;
                }

                List<UINode> toCandidates = NeoFlowWiring.NodesShowingView(_graph, target.id.Category, target.id.Name);
                if (toCandidates.Count > 1)
                {
                    _toCandidates = toCandidates;
                    _selectedToNode = toCandidates[0].name;
                }
            }

            _transitionOptions = new List<string> { ProjectDefaultTransition };
            _transitionOptions.AddRange(ViewTransitionRegistry.FullNames());
        }

        public override Vector2 GetWindowSize()
        {
            float height = 56f; // header block + top/bottom padding
            if (_fromCandidates != null) height += 22f;
            if (_toCandidates != null) height += 22f;
            height += 22f; // transition row
            height += 20f; // allows-back toggle
            if (!string.IsNullOrEmpty(_error)) height += 36f;
            height += 30f; // button row
            return new Vector2(340f, height);
        }

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();

            GUILayout.Space(4f);
            GUILayout.Label(HeaderText(), _header);
            GUILayout.Space(4f);

            if (_fromCandidates != null)
                DrawDropdownRow("Source node", _selectedFromNode,
                    _fromCandidates.Select(n => n.name).ToList(), picked => _selectedFromNode = picked, "(pick a node)");

            if (_toCandidates != null)
                DrawDropdownRow("Target node", _selectedToNode,
                    _toCandidates.Select(n => n.name).ToList(), picked => _selectedToNode = picked, "(pick a node)");

            DrawDropdownRow("Transition", string.IsNullOrEmpty(_transition) ? ProjectDefaultTransition : _transition,
                _transitionOptions,
                picked => _transition = picked == ProjectDefaultTransition ? "" : picked,
                ProjectDefaultTransition);

            _allowsBack = EditorGUILayout.Toggle("Allows Back", _allowsBack);

            if (!string.IsNullOrEmpty(_error))
                EditorGUILayout.HelpBox(_error, MessageType.Error);

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(70f))) editorWindow.Close();

                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = NeoColors.Flow;
                bool clicked = GUILayout.Button("Connect", GUILayout.Width(70f));
                GUI.backgroundColor = previousBackground;
                if (clicked) TryConnect();
            }
        }

        private string HeaderText()
        {
            string buttonLabel = _button != null ? $"{_button.id.Category}/{_button.id.Name}" : "?";
            string targetLabel = _target != null ? $"{_target.id.Category}/{_target.id.Name}" : "?";
            return $"On Click of <b>{buttonLabel}</b> → go to <b>{targetLabel}</b>";
        }

        private void DrawDropdownRow(string label, string current, List<string> options,
            Action<string> onSelect, string emptyLabel)
        {
            Rect row = EditorGUILayout.GetControlRect();
            var labelRect = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height);
            var fieldRect = new Rect(row.x + EditorGUIUtility.labelWidth, row.y,
                row.width - EditorGUIUtility.labelWidth, row.height);
            EditorGUI.LabelField(labelRect, label);
            NeoDropdown.ValuePopup(fieldRect, current, () => options, onSelect, emptyLabel);
        }

        private void TryConnect()
        {
            UINode fromNode = _fromCandidates?.FirstOrDefault(n => n.name == _selectedFromNode);
            UINode toNode = _toCandidates?.FirstOrDefault(n => n.name == _selectedToNode);

            NeoFlowWiring.WiringResult result = _graph != null
                ? NeoFlowWiring.ConnectButtonToView(_graph, _button, _target, _transition, _allowsBack, fromNode, toNode)
                : NeoFlowWiring.ConnectButtonToView(_button, _target, _transition, _allowsBack);

            if (!result.ok)
            {
                _error = result.error;
                return;
            }

            string edgeState = result.alreadyExisted ? "already existed" : "created";
            string buttonLabel = _button != null ? $"{_button.id.Category}/{_button.id.Name}" : "?";
            Debug.Log($"[Neo.UI] Connect To: '{result.graph.name}' — '{result.fromNode.name}' " +
                      $"--[{buttonLabel}]--> '{result.toNode.name}' ({edgeState}).");
            editorWindow.Close();
        }

        private void EnsureStyles()
        {
            _header ??= new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
        }
    }
}
