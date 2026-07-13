using Neo.EditorUI;
using Neo.UI.Editor.Authoring;
using Neo.UI.Menus;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Native-first inspector for settings/cheats menu presenters (<see cref="SettingsMenu"/>,
    /// <see cref="CheatMenu"/>, and the base <see cref="MenuPresenter"/>). The catalog SO is the source of
    /// truth: this surfaces a one-click <b>Rebuild From Catalog</b> so a developer edits the asset and
    /// re-materialises the rows WYSIWYG with no spec/generate round-trip, plus a jump straight to the
    /// catalog. The wiring fields (catalog / library / roots / buildOnStart) stay editable underneath.
    /// </summary>
    [CustomEditor(typeof(MenuPresenter), true)]
    public class MenuPresenterInspector : NeoUIEditor
    {
        protected override string HeaderTitle => target is CheatMenu ? "Cheat Menu" : "Settings Menu";
        protected override string HeaderSubtitle =>
            ((MenuPresenter)target).catalog != null ? ((MenuPresenter)target).catalog.Id : "(no catalog)";
        protected override Color Accent => NeoColors.Data;

        protected override void DrawBody()
        {
            var presenter = (MenuPresenter)target;

            EditorGUILayout.HelpBox(
                "This menu is built from its catalog asset. Edit the catalog's items, then Rebuild — no spec " +
                "or generate step. Rows are baked into the scene (WYSIWYG) and are not rebuilt on play.",
                MessageType.Info);

            if (NeoGUI.BeginFoldoutSection("NeoUI.MenuPresenter.Wiring", "Wiring", defaultOpen: true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("catalog"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("library"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("contentRoot"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("categoryNavRoot"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("buildOnStart"));
            }
            NeoGUI.EndFoldoutSection();

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(presenter.catalog == null))
            {
                if (GUILayout.Button("Rebuild From Catalog", GUILayout.Height(24f)))
                {
                    NeoSceneAuthoring.RebuildMenu(presenter);
                    // RebuildMenu replaces this menu root (and thus this inspector's target); abort the
                    // current IMGUI pass so we don't draw a destroyed object. Selection already moved to
                    // the rebuilt root, so the inspector redraws it next repaint.
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Edit Catalog", GUILayout.Height(24f)))
                {
                    Selection.activeObject = presenter.catalog;
                    EditorGUIUtility.PingObject(presenter.catalog);
                }
            }

            if (presenter.catalog != null)
                EditorGUILayout.LabelField("Items in catalog", presenter.catalog.items.Count.ToString());
        }
    }
}
