using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The IMGUI helpers shared across more than one Design System tab (moved verbatim out of the old
    /// monolithic <see cref="NeoDesignSystemWindow"/> when the tabs were split into their own files):
    /// the <see cref="ThemeColorRef"/> editor, the searchable theme-token dropdown, token→color
    /// resolution, a swatch, and the folder-ensure used when creating preset assets. Single
    /// implementation so every tab draws color refs / token pickers identically.
    /// </summary>
    internal static class DesignSystemGUI
    {
        // ThemeColorRef editor: "T" toggles token-vs-raw; dirtyTarget is the asset that owns the ref.
        internal static void ColorRef(Theme theme, Object dirtyTarget, string label, ThemeColorRef cref)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                bool useTok = GUILayout.Toggle(cref.useToken, "T", EditorStyles.miniButton, GUILayout.Width(24f));
                if (useTok != cref.useToken)
                { Undo.RecordObject(dirtyTarget, "toggle token"); cref.useToken = useTok; EditorUtility.SetDirty(dirtyTarget); }

                if (cref.useToken)
                {
                    TokenPicker(theme, null, cref.token, chosen =>
                    {
                        Undo.RecordObject(dirtyTarget, "edit token ref");
                        cref.token = chosen;
                        EditorUtility.SetDirty(dirtyTarget);
                    });
                    EditorGUILayout.ColorField(GUIContent.none, ResolveToken(theme, cref.token), false, true, false,
                        GUILayout.Width(44f));
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    Color c = EditorGUILayout.ColorField(cref.color);
                    if (EditorGUI.EndChangeCheck())
                    { Undo.RecordObject(dirtyTarget, "edit color"); cref.color = c; EditorUtility.SetDirty(dirtyTarget); }
                }
            }
        }

        // Searchable theme-token dropdown with a "(none)" sentinel (null token). The option list is only
        // built when the dropdown is opened; onPick fires with the chosen token, or null for "(none)".
        internal static void TokenPicker(Theme theme, string label, string current, System.Action<string> onPick)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            if (label != null) rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            NeoDropdown.ValuePopup(rect, current ?? "(none)",
                () =>
                {
                    List<string> tokens = theme.GetTokenNames().ToList();
                    tokens.Insert(0, "(none)");
                    return tokens;
                },
                chosen => onPick(chosen == "(none)" ? null : chosen));
        }

        internal static Color ResolveToken(Theme theme, string token) =>
            !string.IsNullOrEmpty(token) && theme.TryGetColor(token, out Color c) ? c : Color.gray;

        internal static void Swatch(string label, Color c)
        {
            Rect r = GUILayoutUtility.GetRect(34f, 18f, GUILayout.Width(34f));
            EditorGUI.DrawRect(r, c);
            GUI.Label(r, label, EditorStyles.miniLabel);
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
