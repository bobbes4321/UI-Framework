using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// UIAnimation: a horizontal channel strip (Move / Rotate / Scale / Fade) where each chip carries
    /// its own enable checkbox AND acts as a tab — enabled channels are tinted with the Animation
    /// accent so you see at a glance which channels a state/show/hide animation actually drives, and
    /// only the selected channel's settings expand below. Replaces the default nested foldout wall
    /// that stacked all four channels (×5 for a Selectable) on top of each other.
    ///
    /// The enable toggle and the tab-select are separate clicks (checkbox toggles `enabled`, the chip
    /// body selects for editing) so a disabled channel's settings can still be inspected.
    /// </summary>
    [CustomPropertyDrawer(typeof(UIAnimation))]
    public class UIAnimationDrawer : PropertyDrawer
    {
        private static readonly string[] ChannelFields = { "move", "rotate", "scale", "fade" };
        private static readonly string[] ChannelLabels = { "Move", "Rotate", "Scale", "Fade" };

        private const float StripHeight = 22f;
        private const float CheckboxWidth = 16f;
        private const float ChipGap = 2f;

        private static GUIStyle s_chipLabel;
        private static GUIStyle ChipLabel => s_chipLabel ?? (s_chipLabel = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(2, 2, 0, 0)
        });

        private static float Line => EditorGUIUtility.singleLineHeight;
        private static float Spacing => EditorGUIUtility.standardVerticalSpacing;

        // ------------------------------------------------------------------ selection state

        private static string SelectionKey(SerializedProperty property) =>
            "Neo.UIAnim.sel:" + property.propertyPath;

        private static int GetSelected(SerializedProperty property) =>
            Mathf.Clamp(SessionState.GetInt(SelectionKey(property), 0), 0, ChannelFields.Length - 1);

        private static void SetSelected(SerializedProperty property, int index) =>
            SessionState.SetInt(SelectionKey(property), index);

        // ------------------------------------------------------------------ height

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = Line + Spacing;          // purpose row
            height += StripHeight + Spacing;         // channel strip
            height += ChannelPanelHeight(property);  // selected channel fields
            return height;
        }

        private static float ChannelPanelHeight(SerializedProperty property)
        {
            int selected = GetSelected(property);
            SerializedProperty channel = property.FindPropertyRelative(ChannelFields[selected]);
            float height = Spacing;
            CollectPanelItems(channel, selected, s_items);
            foreach (PanelItem item in s_items)
                height += (item.Header != null ? Line : EditorGUI.GetPropertyHeight(item.Prop, true)) + Spacing;
            return height;
        }

        // ------------------------------------------------------------------ draw

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float y = position.y;

            // Purpose tag — informational; drives ApplyPurposeDefaults() in code, not edited often.
            var purposeRect = new Rect(position.x, y, position.width, Line);
            EditorGUI.PropertyField(purposeRect, property.FindPropertyRelative("purpose"));
            y += Line + Spacing;

            int selected = GetSelected(property);
            DrawChannelStrip(new Rect(position.x, y, position.width, StripHeight), property, ref selected);
            y += StripHeight + Spacing;

            DrawChannelPanel(new Rect(position.x, y, position.width, position.yMax - y), property, selected);

            EditorGUI.EndProperty();
        }

        private static void DrawChannelStrip(Rect rect, SerializedProperty property, ref int selected)
        {
            int count = ChannelFields.Length;
            float chipWidth = (rect.width - ChipGap * (count - 1)) / count;
            Color accent = NeoColors.Animation;

            for (int i = 0; i < count; i++)
            {
                var chipRect = new Rect(rect.x + i * (chipWidth + ChipGap), rect.y, chipWidth, rect.height);
                SerializedProperty channel = property.FindPropertyRelative(ChannelFields[i]);
                SerializedProperty enabledProp = channel.FindPropertyRelative("enabled");
                bool isSelected = i == selected;
                bool isEnabled = !enabledProp.hasMultipleDifferentValues && enabledProp.boolValue;

                if (Event.current.type == EventType.Repaint)
                {
                    Color bg = isEnabled ? accent.WithAlpha(isSelected ? 0.30f : 0.16f)
                                         : (isSelected ? NeoColors.RowSelected : NeoColors.SectionBackground);
                    EditorGUI.DrawRect(chipRect, bg);
                    if (isSelected)
                        EditorGUI.DrawRect(new Rect(chipRect.x, chipRect.yMax - 2f, chipRect.width, 2f), accent);
                }

                // Body click anywhere on the chip (except the checkbox) selects it for editing.
                var bodyRect = new Rect(chipRect.x + CheckboxWidth, chipRect.y, chipRect.width - CheckboxWidth, chipRect.height);
                if (GUI.Button(bodyRect, GUIContent.none, GUIStyle.none))
                {
                    selected = i;
                    SetSelected(property, i);
                }

                // Enable checkbox — its own hit target, wrapped so multi-edit mixed values don't stomp.
                var checkRect = new Rect(chipRect.x + 2f, chipRect.y + (chipRect.height - 16f) * 0.5f, CheckboxWidth - 2f, 16f);
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = enabledProp.hasMultipleDifferentValues;
                bool newEnabled = EditorGUI.Toggle(checkRect, enabledProp.boolValue);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck()) enabledProp.boolValue = newEnabled;

                var labelRect = new Rect(bodyRect.x + 2f, bodyRect.y, bodyRect.width - 4f, bodyRect.height);
                Color prev = GUI.contentColor;
                GUI.contentColor = isEnabled ? accent : NeoColors.TextSubtle;
                GUI.Label(labelRect, ChannelLabels[i], ChipLabel);
                GUI.contentColor = prev;
            }
        }

        private static void DrawChannelPanel(Rect rect, SerializedProperty property, int selected)
        {
            SerializedProperty channel = property.FindPropertyRelative(ChannelFields[selected]);
            float y = rect.y + Spacing;

            CollectPanelItems(channel, selected, s_items);
            foreach (PanelItem item in s_items)
            {
                if (item.Header != null)
                {
                    GUI.Label(new Rect(rect.x, y, rect.width, Line), item.Header, HeaderStyle);
                    y += Line + Spacing;
                    continue;
                }
                float h = EditorGUI.GetPropertyHeight(item.Prop, true);
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, h), item.Prop, true);
                EditorGUI.indentLevel--;
                y += h + Spacing;
            }
        }

        // ------------------------------------------------------------------ relevant-field selection

        private struct PanelItem
        {
            public string Header;            // non-null → a section header line ("From" / "To")
            public SerializedProperty Prop;  // non-null → a field to draw
        }

        // Reused across the height and draw passes within a single frame — never held across calls.
        private static readonly List<PanelItem> s_items = new List<PanelItem>(12);

        private static GUIStyle s_header;
        private static GUIStyle HeaderStyle => s_header ?? (s_header = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = NeoColors.TextSubtle }
        });

        /// <summary>
        /// Builds the field list for the selected channel, showing ONLY what each mode actually uses:
        /// a move preset (direction != CustomPosition) computes its endpoint, so reference/custom/
        /// offset are hidden; a custom value field appears only when its ReferenceValue is CustomValue.
        /// Mixed multi-edit values fall back to showing the dependent fields so nothing gets hidden
        /// out from under an edit.
        /// </summary>
        private static void CollectPanelItems(SerializedProperty channel, int channelIndex, List<PanelItem> buffer)
        {
            buffer.Clear();
            buffer.Add(new PanelItem { Prop = channel.FindPropertyRelative("settings") });
            AddEndpoint(buffer, channel, channelIndex == 0, isFrom: true);
            AddEndpoint(buffer, channel, channelIndex == 0, isFrom: false);
        }

        private static void AddEndpoint(List<PanelItem> buffer, SerializedProperty channel, bool isMove, bool isFrom)
        {
            string prefix = isFrom ? "from" : "to";
            buffer.Add(new PanelItem { Header = isFrom ? "From" : "To" });

            if (isMove)
            {
                SerializedProperty direction = channel.FindPropertyRelative(prefix + "Direction");
                buffer.Add(new PanelItem { Prop = direction });
                // A preset direction resolves the endpoint itself — reference/custom/offset are ignored.
                if (!IsCustomPosition(direction)) return;
            }

            SerializedProperty reference = channel.FindPropertyRelative(prefix + "Reference");
            buffer.Add(new PanelItem { Prop = reference });
            if (IsCustomValue(reference))
                buffer.Add(new PanelItem { Prop = channel.FindPropertyRelative(prefix + "CustomValue") });
            buffer.Add(new PanelItem { Prop = channel.FindPropertyRelative(prefix + "Offset") });
        }

        private static bool IsCustomPosition(SerializedProperty direction) =>
            direction.hasMultipleDifferentValues || direction.enumValueIndex == (int)UIMoveDirection.CustomPosition;

        private static bool IsCustomValue(SerializedProperty reference) =>
            reference.hasMultipleDifferentValues || reference.enumValueIndex == (int)ReferenceValue.CustomValue;
    }
}
