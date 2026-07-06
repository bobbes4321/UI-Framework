using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// FlowTrigger picker: trigger type dropdown, then only the fields that type actually uses —
    /// category/name dropdowns backed by the matching ID database (buttons, toggles, views,
    /// streams), or a duration for timers. Back/None need nothing.
    /// </summary>
    [CustomPropertyDrawer(typeof(FlowTrigger))]
    public class FlowTriggerDrawer : PropertyDrawer
    {
        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int typeIndex = property.FindPropertyRelative("type").enumValueIndex;
            var type = (FlowTrigger.TriggerType)Mathf.Max(0, typeIndex); // -1 = mixed values → treat as None
            bool hasSecondLine = type != FlowTrigger.TriggerType.None && type != FlowTrigger.TriggerType.Back;
            return EditorGUIUtility.singleLineHeight + (hasSecondLine ? Line : 0f);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty typeProperty = property.FindPropertyRelative("type");

            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(NeoGUI.NextLine(ref line), typeProperty, label);

            var type = (FlowTrigger.TriggerType)Mathf.Max(0, typeProperty.enumValueIndex);
            switch (type)
            {
                case FlowTrigger.TriggerType.Timer:
                    EditorGUI.PropertyField(EditorGUI.IndentedRect(NeoGUI.NextLine(ref line)),
                        property.FindPropertyRelative("timerDuration"));
                    break;

                case FlowTrigger.TriggerType.None:
                case FlowTrigger.TriggerType.Back:
                    break;

                default:
                    Rect pair = EditorGUI.IndentedRect(NeoGUI.NextLine(ref line));
                    string customKind = type == FlowTrigger.TriggerType.Custom
                        ? property.FindPropertyRelative("customKind").stringValue
                        : null;
                    IdDatabaseOptions.DrawCategoryNamePair(pair,
                        property.FindPropertyRelative("category"),
                        property.FindPropertyRelative("name"),
                        IdDatabaseOptions.ForTrigger(type, customKind));
                    break;
            }
            EditorGUI.EndProperty();
        }
    }

    /// <summary>
    /// UINode view reference: one line, category + view name dropdowns from the view ID database —
    /// the same picker UIView's id field uses, so show/hide view lists are consistent with it.
    /// </summary>
    [CustomPropertyDrawer(typeof(UINode.ViewRef))]
    public class ViewRefDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            // inside lists the per-element "Element N" label is noise — use the full width
            Rect content = label.text.StartsWith("Element", StringComparison.Ordinal)
                ? position
                : EditorGUI.PrefixLabel(position, label);

            NeoUISettings settings = NeoUISettings.instance;
            IdDatabaseOptions.DrawCategoryNamePair(content,
                property.FindPropertyRelative("category"),
                property.FindPropertyRelative("viewName"),
                settings != null ? settings.viewIds : null);
            EditorGUI.EndProperty();
        }
    }

    /// <summary>
    /// Flow edge: collapsed to a "port → target (trigger)" summary line; expanded, the target node
    /// is a dropdown of the graph's executable nodes, the weight only appears on RandomNode outputs
    /// (the only node type that reads it), and a Transition row picks the
    /// <see cref="ViewTransitionAsset"/> (by full name) choreographing this navigation cut. When a
    /// transition is set and resolves, a read-only lanes strip visualizes its channel timeline and a
    /// scrub slider drives it live on the from/to views' scene instances (resolved via
    /// <see cref="FlowEdgeScrubState"/>) — the flow-graph analog of the animation preview controls.
    /// </summary>
    [CustomPropertyDrawer(typeof(FlowEdge))]
    public class FlowEdgeDrawer : PropertyDrawer
    {
        private const string NotConnected = "(not connected)";
        private const float LaneHeight = 10f;
        private const float LaneGap = 2f;
        private const float LaneLabelWidth = 14f;
        private const float LanesTopPad = 4f;

        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;

            height += Line * 3f; // portName, toNode, allowsBack
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("trigger")) +
                      EditorGUIUtility.standardVerticalSpacing;
            if (OwnerIsRandomNode(property)) height += Line;

            height += Line; // transition row

            ViewTransitionAsset transition = ResolveTransition(property);
            if (transition != null)
            {
                int lanes = CountLanes(transition);
                if (lanes > 0) height += LanesTopPad + lanes * (LaneHeight + LaneGap);
                height += Line; // scrub slider
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty portNameProperty = property.FindPropertyRelative("portName");
            SerializedProperty toNodeProperty = property.FindPropertyRelative("toNode");
            SerializedProperty triggerProperty = property.FindPropertyRelative("trigger");

            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(NeoGUI.NextLine(ref line), property.isExpanded,
                Summarize(portNameProperty, toNodeProperty, triggerProperty), toggleOnLabelClick: true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(NeoGUI.NextLine(ref line), portNameProperty);
                DrawTargetNodeDropdown(NeoGUI.NextLine(ref line), toNodeProperty);
                EditorGUI.PropertyField(NeoGUI.NextLine(ref line), property.FindPropertyRelative("allowsBack"));

                float triggerHeight = EditorGUI.GetPropertyHeight(triggerProperty);
                EditorGUI.PropertyField(new Rect(line.x, line.y, line.width, triggerHeight), triggerProperty);
                line.y += triggerHeight + EditorGUIUtility.standardVerticalSpacing;

                if (OwnerIsRandomNode(property))
                    EditorGUI.PropertyField(NeoGUI.NextLine(ref line), property.FindPropertyRelative("weight"));

                DrawTransitionRow(NeoGUI.NextLine(ref line), property);

                ViewTransitionAsset transition = ResolveTransition(property);
                if (transition != null)
                {
                    int lanes = CountLanes(transition);
                    if (lanes > 0)
                    {
                        line.y += LanesTopPad;
                        Rect indented = EditorGUI.IndentedRect(line);
                        var lanesRect = new Rect(indented.x, line.y, indented.width,
                            lanes * (LaneHeight + LaneGap) - LaneGap);
                        DrawLanesStrip(lanesRect, transition);
                        line.y += lanesRect.height + EditorGUIUtility.standardVerticalSpacing;
                    }
                    DrawScrubRow(NeoGUI.NextLine(ref line), property, transition);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        // ------------------------------------------------------------------ target node

        private static void DrawTargetNodeDropdown(Rect rect, SerializedProperty toNodeProperty)
        {
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("To Node", toNodeProperty.tooltip));

            if (!(toNodeProperty.serializedObject.targetObject is FlowGraph graph))
            {
                EditorGUI.PropertyField(rect, toNodeProperty, GUIContent.none);
                return;
            }

            string current = toNodeProperty.stringValue;
            SerializedObject serializedObject = toNodeProperty.serializedObject;
            string path = toNodeProperty.propertyPath;

            NeoDropdown.ValuePopup(rect, current, () => TargetNodeOptions(graph), selected =>
            {
                serializedObject.Update();
                SerializedProperty resolved = serializedObject.FindProperty(path);
                if (resolved == null) return;
                resolved.stringValue = selected == NotConnected ? "" : selected;
                serializedObject.ApplyModifiedProperties();
            }, emptyLabel: NotConnected);
        }

        private static List<string> TargetNodeOptions(FlowGraph graph)
        {
            var options = new List<string> { NotConnected };
            foreach (FlowNode node in graph.nodes)
                if (node != null && node.isExecutable && !string.IsNullOrEmpty(node.name))
                    options.Add(node.name);
            return options;
        }

        // ------------------------------------------------------------------ transition row

        private static void DrawTransitionRow(Rect rect, SerializedProperty edgeProperty)
        {
            SerializedProperty transitionProperty = edgeProperty.FindPropertyRelative("transition");
            Rect content = EditorGUI.PrefixLabel(rect, new GUIContent("Transition", transitionProperty.tooltip));

            const float browseWidth = 22f;
            var dropdownRect = new Rect(content.x, content.y, content.width - browseWidth - 2f, content.height);
            var browseRect = new Rect(dropdownRect.xMax + 2f, content.y, browseWidth, content.height);

            if (transitionProperty.hasMultipleDifferentValues)
                EditorGUI.LabelField(dropdownRect, "—");
            else
                NeoDropdown.StringPopup(dropdownRect, transitionProperty, ViewTransitionRegistry.FullNames,
                    emptyLabel: "(project default)");

            if (GUI.Button(browseRect, new GUIContent("⋯", "Browse view transitions…")))
            {
                string current = transitionProperty.hasMultipleDifferentValues ? null : transitionProperty.stringValue;
                SerializedObject serializedObject = transitionProperty.serializedObject;
                string path = transitionProperty.propertyPath;

                FlowEdgeScrubState.ResolveRoots(edgeProperty, out List<RectTransform> outgoingRoots,
                    out List<RectTransform> incomingRoots);

                PopupWindow.Show(browseRect, new ViewTransitionBrowserPopup(current, selected =>
                {
                    try
                    {
                        if (serializedObject.targetObject == null) return;
                        serializedObject.Update();
                        SerializedProperty resolved = serializedObject.FindProperty(path);
                        if (resolved == null) return;
                        resolved.stringValue = selected ?? "";
                        serializedObject.ApplyModifiedProperties();
                    }
                    catch (Exception)
                    {
                        // the drawer (and its SerializedObject) may have gone away while the popup was open
                    }
                }, outgoingRoots, incomingRoots));
            }
        }

        private static ViewTransitionAsset ResolveTransition(SerializedProperty edgeProperty)
        {
            SerializedProperty transitionProperty = edgeProperty.FindPropertyRelative("transition");
            if (transitionProperty.hasMultipleDifferentValues) return null; // guard multi-edit — never preview mixed values
            string fullName = transitionProperty.stringValue;
            if (string.IsNullOrEmpty(fullName)) return null;
            return ViewTransitionRegistry.TryGet(fullName, out ViewTransitionAsset transition) ? transition : null;
        }

        // ------------------------------------------------------------------ lanes strip (read-only)

        private static int CountLanes(ViewTransitionAsset transition) =>
            CountChannels(transition.outgoing) + CountChannels(transition.incoming);

        private static int CountChannels(UIAnimation animation)
        {
            int n = 0;
            if (animation.move.enabled) n++;
            if (animation.rotate.enabled) n++;
            if (animation.scale.enabled) n++;
            if (animation.fade.enabled) n++;
            if (animation.color.enabled) n++;
            return n;
        }

        private static void DrawLanesStrip(Rect rect, ViewTransitionAsset transition)
        {
            if (Event.current.type != EventType.Repaint) return;
            float total = transition.totalDuration;
            if (total <= 0f) return;

            var barArea = new Rect(rect.x + LaneLabelWidth, rect.y, rect.width - LaneLabelWidth, rect.height);
            float y = rect.y;
            y = DrawSideLanes(rect, barArea, y, transition.outgoing, 0f, total, NeoColors.Remove.WithAlpha(0.6f));
            DrawSideLanes(rect, barArea, y, transition.incoming, transition.incomingOffset, total, NeoColors.Animation.WithAlpha(0.85f));
        }

        private static float DrawSideLanes(Rect rowRect, Rect barArea, float y, UIAnimation animation,
            float offset, float total, Color tint)
        {
            y = DrawLane(rowRect, barArea, y, animation.move.enabled, offset, animation.move.settings, total, tint, "M");
            y = DrawLane(rowRect, barArea, y, animation.rotate.enabled, offset, animation.rotate.settings, total, tint, "R");
            y = DrawLane(rowRect, barArea, y, animation.scale.enabled, offset, animation.scale.settings, total, tint, "S");
            y = DrawLane(rowRect, barArea, y, animation.fade.enabled, offset, animation.fade.settings, total, tint, "F");
            if (animation.color.enabled) y = DrawLane(rowRect, barArea, y, true, offset, animation.color.settings, total, tint, "C");
            return y;
        }

        private static float DrawLane(Rect rowRect, Rect barArea, float y, bool enabled, float offset,
            TweenSettings settings, float total, Color tint, string laneLabel)
        {
            if (!enabled) return y;
            float startT = offset + settings.startDelay;
            float x0 = barArea.x + barArea.width * Mathf.Clamp01(startT / total);
            float x1 = barArea.x + barArea.width * Mathf.Clamp01((startT + settings.duration) / total);
            var barRect = new Rect(x0, y, Mathf.Max(2f, x1 - x0), LaneHeight);
            EditorGUI.DrawRect(barRect, tint);
            GUI.Label(new Rect(rowRect.x, y - 1f, LaneLabelWidth, LaneHeight + 2f), laneLabel, EditorStyles.miniLabel);
            return y + LaneHeight + LaneGap;
        }

        // ------------------------------------------------------------------ scrub slider

        private static void DrawScrubRow(Rect rect, SerializedProperty edgeProperty, ViewTransitionAsset transition)
        {
            string key = ScrubKey(edgeProperty);
            FlowEdgeScrubState.ResolveRoots(edgeProperty, out List<RectTransform> outgoingRoots,
                out List<RectTransform> incomingRoots);
            bool canScrub = outgoingRoots.Count > 0 || incomingRoots.Count > 0;
            bool sessionActive = FlowEdgeScrubState.IsActive(key);
            float total = Mathf.Max(0.01f, transition.totalDuration);
            float current = sessionActive ? FlowEdgeScrubState.CurrentTime : 0f;

            using (new EditorGUI.DisabledScope(!canScrub))
            {
                string tooltip = canScrub
                    ? "Scrub the transition on the live scene views."
                    : "Open a scene containing these views to scrub.";
                EditorGUI.BeginChangeCheck();
                float next = EditorGUI.Slider(rect, new GUIContent("Scrub", tooltip), current, 0f, total);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!sessionActive) FlowEdgeScrubState.Begin(key, edgeProperty, transition);
                    FlowEdgeScrubState.Scrub(next);
                }
            }

            // The slider consumes its own mouse-up, but Event.current.type still reports it this pass.
            if (sessionActive && Event.current.type == EventType.MouseUp) FlowEdgeScrubState.End();
        }

        private static string ScrubKey(SerializedProperty edgeProperty) =>
            $"{(edgeProperty.serializedObject.targetObject != null ? edgeProperty.serializedObject.targetObject.GetInstanceID() : 0)}:{edgeProperty.propertyPath}";

        // ------------------------------------------------------------------ owner node

        /// <summary> The FlowNode that owns this edge (path: nodes.Array.data[i].outputs...), or null. </summary>
        internal static FlowNode ResolveOwnerNode(SerializedProperty edgeProperty, FlowGraph graph)
        {
            if (!TryGetOwnerNodeIndex(edgeProperty, out int index) || graph == null) return null;
            return index >= 0 && index < graph.nodes.Count ? graph.nodes[index] : null;
        }

        private static bool TryGetOwnerNodeIndex(SerializedProperty property, out int index)
        {
            index = -1;
            string path = property.propertyPath;
            const string prefix = "nodes.Array.data[";
            int start = path.IndexOf(prefix, StringComparison.Ordinal);
            if (start != 0) return false;
            int end = path.IndexOf(']', prefix.Length);
            return end >= 0 && int.TryParse(path.Substring(prefix.Length, end - prefix.Length), out index);
        }

        // ------------------------------------------------------------------ summary / random weight

        private static readonly GUIContent SummaryContent = new GUIContent();

        private static GUIContent Summarize(SerializedProperty portName, SerializedProperty toNode,
            SerializedProperty trigger)
        {
            string port = string.IsNullOrEmpty(portName.stringValue) ? "Out" : portName.stringValue;
            string target = string.IsNullOrEmpty(toNode.stringValue) ? NotConnected : toNode.stringValue;
            var type = (FlowTrigger.TriggerType)Mathf.Max(0, trigger.FindPropertyRelative("type").enumValueIndex);
            string suffix = type == FlowTrigger.TriggerType.None ? "" : $"  [{type}]";
            SummaryContent.text = $"{port} → {target}{suffix}";
            return SummaryContent;
        }

        /// <summary> True when this edge sits in a RandomNode's outputs (path: nodes.Array.data[i].outputs...). </summary>
        private static bool OwnerIsRandomNode(SerializedProperty property)
        {
            if (!(property.serializedObject.targetObject is FlowGraph graph)) return false;
            return TryGetOwnerNodeIndex(property, out int index)
                   && index >= 0 && index < graph.nodes.Count && graph.nodes[index] is RandomNode;
        }
    }

    /// <summary>
    /// Caches the live scene <see cref="RectTransform"/>s a flow edge's from/to views resolve to (keyed
    /// by graph instance + edge property path, invalidated on hierarchy changes rather than every
    /// repaint) and drives the one active scrub session on top of them — the flow-edge analog of
    /// <see cref="AnimationPreview"/>, but for a whole <see cref="ViewTransitionAsset"/> (outgoing +
    /// offset incoming) instead of a single <see cref="UIAnimation"/>. Only one session is ever active;
    /// starting a new one or changing the editor selection ends the previous one and restores its
    /// touched roots untouched.
    /// </summary>
    internal static class FlowEdgeScrubState
    {
        private sealed class RootSet
        {
            public readonly List<RectTransform> outgoing = new List<RectTransform>();
            public readonly List<RectTransform> incoming = new List<RectTransform>();
        }

        private sealed class Session
        {
            public string key;
            public ViewTransitionAsset transition;
            public List<RectTransform> outgoing;
            public List<RectTransform> incoming;
            public readonly List<UIAnimation> outScratch = new List<UIAnimation>();
            public readonly List<UIAnimation> inScratch = new List<UIAnimation>();
            public float time;
        }

        private static readonly Dictionary<string, RootSet> RootCache = new Dictionary<string, RootSet>();
        private static Session _session;

        static FlowEdgeScrubState()
        {
            Selection.selectionChanged += End;
            EditorApplication.hierarchyChanged += () => RootCache.Clear();
        }

        public static bool IsActive(string key) => _session != null && string.Equals(_session.key, key, StringComparison.Ordinal);

        public static float CurrentTime => _session?.time ?? 0f;

        /// <summary> Resolved (and cached) from/to view roots for this edge. Never null lists. </summary>
        public static void ResolveRoots(SerializedProperty edgeProperty, out List<RectTransform> outgoing,
            out List<RectTransform> incoming)
        {
            string key = KeyFor(edgeProperty);
            if (!RootCache.TryGetValue(key, out RootSet set))
            {
                set = BuildRootSet(edgeProperty);
                RootCache[key] = set;
            }
            outgoing = set.outgoing;
            incoming = set.incoming;
        }

        private static string KeyFor(SerializedProperty edgeProperty) =>
            $"{(edgeProperty.serializedObject.targetObject != null ? edgeProperty.serializedObject.targetObject.GetInstanceID() : 0)}:{edgeProperty.propertyPath}";

        private static RootSet BuildRootSet(SerializedProperty edgeProperty)
        {
            var set = new RootSet();
            if (!(edgeProperty.serializedObject.targetObject is FlowGraph graph)) return set;

            var fromNode = FlowEdgeDrawer.ResolveOwnerNode(edgeProperty, graph) as UINode;
            string toNodeName = edgeProperty.FindPropertyRelative("toNode")?.stringValue;
            UINode toNode = null;
            if (!string.IsNullOrEmpty(toNodeName))
                foreach (FlowNode node in graph.nodes)
                    if (node is UINode candidate && string.Equals(candidate.name, toNodeName, StringComparison.Ordinal))
                    {
                        toNode = candidate;
                        break;
                    }

            bool needsScan = (fromNode != null && fromNode.showViews.Count > 0) ||
                             (toNode != null && toNode.showViews.Count > 0);
            if (!needsScan) return set;

            UIView[] allViews = UnityEngine.Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (fromNode != null) CollectRoots(fromNode.showViews, allViews, set.outgoing);
            if (toNode != null) CollectRoots(toNode.showViews, allViews, set.incoming);
            return set;
        }

        private static void CollectRoots(List<UINode.ViewRef> refs, UIView[] allViews, List<RectTransform> result)
        {
            foreach (UINode.ViewRef reference in refs)
                foreach (UIView view in allViews)
                    if (view != null && view.id.Matches(reference.category, reference.viewName))
                        result.Add(view.GetComponent<RectTransform>());
        }

        public static void Begin(string key, SerializedProperty edgeProperty, ViewTransitionAsset transition)
        {
            End();
            ResolveRoots(edgeProperty, out List<RectTransform> outgoing, out List<RectTransform> incoming);
            if (outgoing.Count == 0 && incoming.Count == 0) return;

            var session = new Session { key = key, transition = transition, outgoing = outgoing, incoming = incoming };
            for (int i = 0; i < outgoing.Count; i++) session.outScratch.Add(new UIAnimation());
            for (int i = 0; i < incoming.Count; i++) session.inScratch.Add(new UIAnimation());
            foreach (RectTransform r in outgoing) if (r != null) AnimationPreview.BeginPreview(r);
            foreach (RectTransform r in incoming) if (r != null) AnimationPreview.BeginPreview(r);
            _session = session;
        }

        public static void Scrub(float time)
        {
            if (_session == null) return;
            _session.time = time;
            ViewTransitionAsset transition = _session.transition;

            if (ViewTransitionAsset.Overrides(transition.outgoing) && transition.outgoing.totalDuration > 0f)
                ApplyProgress(_session.outgoing, _session.outScratch, transition.outgoing,
                    Mathf.Clamp01(time / transition.outgoing.totalDuration));
            if (ViewTransitionAsset.Overrides(transition.incoming) && transition.incoming.totalDuration > 0f)
                ApplyProgress(_session.incoming, _session.inScratch, transition.incoming,
                    Mathf.Clamp01((time - transition.incomingOffset) / transition.incoming.totalDuration));
        }

        private static void ApplyProgress(List<RectTransform> roots, List<UIAnimation> scratches,
            UIAnimation source, float progress)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                RectTransform root = roots[i];
                if (root == null) continue;
                UIAnimation scratch = scratches[i];
                if (scratch.rectTransform != root)
                {
                    UIAnimationChannels.Copy(source, scratch);
                    CanvasGroup group = root.GetComponent<CanvasGroup>();
                    if (group == null && scratch.fade.enabled) group = root.gameObject.AddComponent<CanvasGroup>();
                    scratch.SetTarget(root, group);
                    scratch.CaptureStartValues(); // target is at rest here — refreshes color/start endpoints
                }
                scratch.SetProgressAt(progress);
            }
        }

        /// <summary> Ends the active scrub session (if any), restoring every touched root untouched. </summary>
        public static void End()
        {
            if (_session == null) return;
            foreach (UIAnimation s in _session.outScratch) { s.Stop(silent: true); s.RestoreStartValues(); }
            foreach (UIAnimation s in _session.inScratch) { s.Stop(silent: true); s.RestoreStartValues(); }
            foreach (RectTransform r in _session.outgoing) if (r != null) AnimationPreview.EndPreview(r);
            foreach (RectTransform r in _session.incoming) if (r != null) AnimationPreview.EndPreview(r);
            _session = null;
            SceneView.RepaintAll();
        }
    }
}
