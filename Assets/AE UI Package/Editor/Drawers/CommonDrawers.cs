using System;
using System.Collections.Generic;
using AlterEyes.EditorUI;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// ThemeColorRef: one line — a "T" toggle switches between a plain color field and a theme
    /// token dropdown (tokens from the active theme) with a live swatch of the resolved color.
    /// </summary>
    [CustomPropertyDrawer(typeof(ThemeColorRef))]
    public class ThemeColorRefDrawer : PropertyDrawer
    {
        private const float ToggleWidth = 24f;
        private const float SwatchWidth = 36f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty useTokenProperty = property.FindPropertyRelative("useToken");
            SerializedProperty tokenProperty = property.FindPropertyRelative("token");
            SerializedProperty colorProperty = property.FindPropertyRelative("color");

            EditorGUI.BeginProperty(position, label, property);
            Rect content = EditorGUI.PrefixLabel(position, label);

            var toggleRect = new Rect(content.x, content.y, ToggleWidth, content.height);
            var fieldRect = new Rect(toggleRect.xMax + 2f, content.y,
                content.width - ToggleWidth - 2f, content.height);

            EditorGUI.BeginChangeCheck();
            bool useToken = GUI.Toggle(toggleRect, useTokenProperty.boolValue,
                new GUIContent("T", "Use a theme token instead of a hardcoded color"), EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck()) useTokenProperty.boolValue = useToken;

            if (useTokenProperty.boolValue)
            {
                var dropdownRect = new Rect(fieldRect.x, fieldRect.y, fieldRect.width - SwatchWidth - 2f, fieldRect.height);
                var swatchRect = new Rect(dropdownRect.xMax + 2f, fieldRect.y + 1f, SwatchWidth, fieldRect.height - 2f);

                AEDropdown.StringPopup(dropdownRect, tokenProperty, TokenOptions, emptyLabel: "(no token)");

                Theme theme = AEUISettings.instance != null ? AEUISettings.instance.theme : null;
                Color resolved = theme != null && theme.TryGetColor(tokenProperty.stringValue, out Color themed)
                    ? themed
                    : colorProperty.colorValue;
                EditorGUI.DrawRect(swatchRect, resolved);
            }
            else
            {
                EditorGUI.PropertyField(fieldRect, colorProperty, GUIContent.none);
            }
            EditorGUI.EndProperty();
        }

        private static List<string> TokenOptions()
        {
            var options = new List<string>();
            Theme theme = AEUISettings.instance != null ? AEUISettings.instance.theme : null;
            if (theme != null) options.AddRange(theme.GetTokenNames());
            return options;
        }
    }

    /// <summary>
    /// UIActionBehaviour: collapses to "Click — 2 listeners", expands to trigger, event, optional
    /// signal (stream picker only shown when sending) and cooldown.
    /// </summary>
    [CustomPropertyDrawer(typeof(UIActionBehaviour))]
    public class UIActionBehaviourDrawer : PropertyDrawer
    {
        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;

            height += Line; // trigger
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Event")) +
                      EditorGUIUtility.standardVerticalSpacing;
            height += Line; // sendSignal
            if (property.FindPropertyRelative("sendSignal").boolValue) height += Line; // signalStream
            height += Line; // cooldown
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty triggerProperty = property.FindPropertyRelative("trigger");
            SerializedProperty eventProperty = property.FindPropertyRelative("Event");
            SerializedProperty sendSignalProperty = property.FindPropertyRelative("sendSignal");

            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(AEGUI.NextLine(ref line), property.isExpanded,
                Summarize(triggerProperty, eventProperty), toggleOnLabelClick: true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(AEGUI.NextLine(ref line), triggerProperty);

                float eventHeight = EditorGUI.GetPropertyHeight(eventProperty);
                EditorGUI.PropertyField(new Rect(line.x, line.y, line.width, eventHeight), eventProperty);
                line.y += eventHeight + EditorGUIUtility.standardVerticalSpacing;

                EditorGUI.PropertyField(AEGUI.NextLine(ref line), sendSignalProperty);
                if (sendSignalProperty.boolValue)
                    EditorGUI.PropertyField(AEGUI.NextLine(ref line), property.FindPropertyRelative("signalStream"));

                EditorGUI.PropertyField(AEGUI.NextLine(ref line), property.FindPropertyRelative("cooldown"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        private static readonly GUIContent SummaryContent = new GUIContent();

        private static GUIContent Summarize(SerializedProperty trigger, SerializedProperty eventProperty)
        {
            string triggerName = trigger.enumValueIndex < 0
                ? "—" // mixed values across multi-selected objects
                : ((BehaviourTrigger)trigger.enumValueIndex).ToString();
            int listeners = eventProperty.FindPropertyRelative("m_PersistentCalls.m_Calls").arraySize;
            SummaryContent.text = listeners == 0
                ? triggerName
                : $"{triggerName} — {listeners} listener{(listeners == 1 ? "" : "s")}";
            return SummaryContent;
        }
    }

    /// <summary>
    /// TweenSettings: collapses to "0.30s OutQuad" style summary; expands to play mode, ease (enum
    /// or curve depending on ease mode), and duration/delay/loops rows where the "~" toggle swaps a
    /// single value for a random min/max range. Spring/Shake fields only appear for those modes.
    /// </summary>
    [CustomPropertyDrawer(typeof(TweenSettings))]
    public class TweenSettingsDrawer : PropertyDrawer
    {
        private const float RandomToggleWidth = 24f;

        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;

            height += Line * 5f; // playMode, ease, duration, startDelay, loops
            if (HasLoops(property)) height += Line; // loopDelay
            if (IsSpringOrShake(property)) height += Line * 4f; // strength, vibration, elasticity, fadeOutShake
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(AEGUI.NextLine(ref line), property.isExpanded,
                Summarize(property, label), toggleOnLabelClick: true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(AEGUI.NextLine(ref line), property.FindPropertyRelative("playMode"));
                DrawEaseRow(AEGUI.NextLine(ref line), property);
                DrawValueOrRangeRow(AEGUI.NextLine(ref line), property, "Duration", "duration", "useRandomDuration", "randomDuration");
                DrawValueOrRangeRow(AEGUI.NextLine(ref line), property, "Start Delay", "startDelay", "useRandomStartDelay", "randomStartDelay");
                DrawValueOrRangeRow(AEGUI.NextLine(ref line), property, "Loops", "loops", "useRandomLoops", "randomLoops");
                if (HasLoops(property))
                    DrawValueOrRangeRow(AEGUI.NextLine(ref line), property, "Loop Delay", "loopDelay", "useRandomLoopDelay", "randomLoopDelay");

                if (IsSpringOrShake(property))
                {
                    EditorGUI.PropertyField(AEGUI.NextLine(ref line), property.FindPropertyRelative("strength"));
                    EditorGUI.PropertyField(AEGUI.NextLine(ref line), property.FindPropertyRelative("vibration"));
                    EditorGUI.PropertyField(AEGUI.NextLine(ref line), property.FindPropertyRelative("elasticity"));
                    EditorGUI.PropertyField(AEGUI.NextLine(ref line), property.FindPropertyRelative("fadeOutShake"));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        private static void DrawEaseRow(Rect rect, SerializedProperty property)
        {
            SerializedProperty easeModeProperty = property.FindPropertyRelative("easeMode");
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("Ease"));

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            var modeRect = new Rect(rect.x, rect.y, 70f, rect.height);
            var valueRect = new Rect(modeRect.xMax + 2f, rect.y, rect.width - modeRect.width - 2f, rect.height);

            EditorGUI.PropertyField(modeRect, easeModeProperty, GUIContent.none);
            bool useCurve = easeModeProperty.enumValueIndex == (int)EaseMode.AnimationCurve;
            EditorGUI.PropertyField(valueRect,
                property.FindPropertyRelative(useCurve ? "curve" : "ease"), GUIContent.none);
            EditorGUI.indentLevel = indent;
        }

        private static void DrawValueOrRangeRow(Rect rect, SerializedProperty property,
            string label, string valueName, string toggleName, string rangeName)
        {
            SerializedProperty toggleProperty = property.FindPropertyRelative(toggleName);
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            var valueRect = new Rect(rect.x, rect.y, rect.width - RandomToggleWidth - 2f, rect.height);
            var toggleRect = new Rect(valueRect.xMax + 2f, rect.y, RandomToggleWidth, rect.height);

            if (toggleProperty.boolValue)
            {
                // draw min/max as two plain fields — a Vector2 PropertyField wraps to a second
                // line in narrow inspectors, breaking the height budget
                SerializedProperty rangeProperty = property.FindPropertyRelative(rangeName);
                AEGUI.SplitHorizontal(valueRect, out Rect minRect, out Rect maxRect);
                EditorGUI.PropertyField(minRect, rangeProperty.FindPropertyRelative("x"), GUIContent.none);
                EditorGUI.PropertyField(maxRect, rangeProperty.FindPropertyRelative("y"), GUIContent.none);
            }
            else
            {
                EditorGUI.PropertyField(valueRect, property.FindPropertyRelative(valueName), GUIContent.none);
            }

            EditorGUI.BeginChangeCheck();
            bool useRandom = GUI.Toggle(toggleRect, toggleProperty.boolValue,
                new GUIContent("~", "Use a random range, re-rolled every play"), EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck()) toggleProperty.boolValue = useRandom;
            EditorGUI.indentLevel = indent;
        }

        private static bool HasLoops(SerializedProperty property) =>
            property.FindPropertyRelative("loops").intValue != 0 ||
            property.FindPropertyRelative("useRandomLoops").boolValue;

        private static bool IsSpringOrShake(SerializedProperty property)
        {
            int index = property.FindPropertyRelative("playMode").enumValueIndex;
            if (index < 0) return false; // mixed values
            var mode = (TweenPlayMode)index;
            return mode == TweenPlayMode.Spring || mode == TweenPlayMode.Shake;
        }

        private static readonly GUIContent SummaryContent = new GUIContent();

        private static GUIContent Summarize(SerializedProperty property, GUIContent label)
        {
            SerializedProperty durationProperty = property.FindPropertyRelative("duration");
            bool randomDuration = property.FindPropertyRelative("useRandomDuration").boolValue;
            int modeIndex = property.FindPropertyRelative("playMode").enumValueIndex;
            int easeIndex = property.FindPropertyRelative("ease").enumValueIndex;
            bool useCurve = property.FindPropertyRelative("easeMode").enumValueIndex == (int)EaseMode.AnimationCurve;

            string duration = randomDuration
                ? $"{property.FindPropertyRelative("randomDuration").vector2Value.x:0.##}–{property.FindPropertyRelative("randomDuration").vector2Value.y:0.##}s"
                : $"{durationProperty.floatValue:0.##}s";
            string ease = useCurve ? "Curve" : easeIndex < 0 ? "—" : ((Ease)easeIndex).ToString();
            string modeSuffix = modeIndex <= 0 ? "" : $" · {(TweenPlayMode)modeIndex}";
            SummaryContent.text = $"{label.text}   {duration} {ease}{modeSuffix}";
            return SummaryContent;
        }
    }
}
