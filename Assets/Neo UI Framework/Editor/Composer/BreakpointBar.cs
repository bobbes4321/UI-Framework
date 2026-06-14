using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// The Composer's breakpoint authoring bar (Pillar B "B-ui"): a segmented selector that sets the
    /// document's <see cref="SpecDocument.ActiveBreakpoint"/> edit scope (Base + one chip per
    /// <see cref="UISpec.breakpoints"/> entry), plus add / rename / delete and a per-breakpoint
    /// condition picker populated from the <see cref="BreakpointConditions"/> registry seam. Breakpoints
    /// are global (top-level on the spec) so every view shares the same named conditions.
    ///
    /// <para>IMGUI through the EditorUI kit, allocation-light: option lists are gathered only when a
    /// dropdown opens, styles are the kit's cached ones. Every spec mutation routes through
    /// <see cref="SpecDocument.ApplyEdit"/>; selecting a chip is editor state, not a spec edit.</para>
    /// </summary>
    public class BreakpointBar
    {
        private readonly SpecDocument _document;
        private bool _expanded;

        public BreakpointBar(SpecDocument document)
        {
            _document = document;
        }

        private UISpec Spec => _document.Spec;

        /// <summary> Draws the bar's chrome inline (call inside an IMGUI region / toolbar host). </summary>
        public void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Breakpoint:", EditorStyles.miniLabel, GUILayout.Width(72f));
                DrawChip("Base", string.IsNullOrEmpty(_document.ActiveBreakpoint), () => _document.SetActiveBreakpoint(""));

                List<BreakpointSpec> bps = Spec?.breakpoints;
                if (bps != null)
                    for (int i = 0; i < bps.Count; i++)
                    {
                        BreakpointSpec bp = bps[i];
                        if (bp == null || string.IsNullOrEmpty(bp.name)) continue;
                        bool active = string.Equals(_document.ActiveBreakpoint, bp.name, System.StringComparison.Ordinal);
                        string name = bp.name;
                        DrawChip(name, active, () => _document.SetActiveBreakpoint(name));
                    }

                if (GUILayout.Button("+ Breakpoint", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                    AddBreakpoint();

                GUILayout.FlexibleSpace();
                _expanded = GUILayout.Toggle(_expanded, "Manage ▾", EditorStyles.toolbarButton, GUILayout.Width(78f));
            }

            if (_expanded) DrawManager();
        }

        private static void DrawChip(string label, bool active, System.Action onClick)
        {
            Color prev = GUI.backgroundColor;
            if (active) GUI.backgroundColor = NeoColors.Interactive;
            if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.MinWidth(40f)) && !active)
                onClick();
            GUI.backgroundColor = prev;
        }

        // the expanded manager: rename / set condition / delete each breakpoint
        private void DrawManager()
        {
            List<BreakpointSpec> bps = Spec?.breakpoints;
            if (bps == null || bps.Count == 0)
            {
                EditorGUILayout.HelpBox("No breakpoints yet. Add one to author responsive overrides.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < bps.Count; i++)
                {
                    BreakpointSpec bp = bps[i];
                    if (bp == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUILayout.DelayedTextField(bp.name ?? "", GUILayout.Width(120f));
                        if (EditorGUI.EndChangeCheck()) RenameBreakpoint(bp, newName);

                        DrawConditionPicker(bp);

                        Color prev = GUI.backgroundColor;
                        GUI.backgroundColor = NeoColors.Remove;
                        if (GUILayout.Button("Delete", GUILayout.Width(56f))) DeleteBreakpoint(bp);
                        GUI.backgroundColor = prev;
                    }
                }
            }
        }

        // condition kind picker (from the registry seam) + the active kind's value field
        private void DrawConditionPicker(BreakpointSpec bp)
        {
            string activeKind = ActiveConditionId(bp.when);
            Rect kindRect = GUILayoutUtility.GetRect(96f, 18f, GUILayout.Width(96f));
            NeoDropdown.ValuePopup(kindRect, activeKind, ConditionKindOptions,
                id => Apply(() => SetConditionKind(bp, id), "Edit Condition"), "condition");

            if (string.IsNullOrEmpty(activeKind)) return;

            // the built-in kinds map to a value field; orientation is an enum, the rest are floats.
            if (activeKind == "orientation")
            {
                Rect r = GUILayoutUtility.GetRect(96f, 18f, GUILayout.Width(96f));
                NeoDropdown.ValuePopup(r, bp.when.orientation, () => new List<string> { "portrait", "landscape" },
                    v => Apply(() => bp.when.orientation = v, "Edit Orientation"), "—");
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                float current = ConditionValue(bp.when, activeKind);
                float v = EditorGUILayout.DelayedFloatField(current, GUILayout.Width(70f));
                if (EditorGUI.EndChangeCheck()) Apply(() => SetConditionValue(bp.when, activeKind, v), "Edit Condition");
            }
        }

        // option list built only when the dropdown opens, from the registry (so a project's kind shows)
        private static List<string> ConditionKindOptions()
        {
            var list = new List<string>();
            foreach (IBreakpointCondition c in BreakpointConditions.All)
                if (c != null && !string.IsNullOrEmpty(c.Id)) list.Add(c.Id);
            return list;
        }

        private static string ActiveConditionId(BreakpointCondition c)
        {
            if (c == null) return null;
            foreach (IBreakpointCondition kind in BreakpointConditions.All)
                if (kind != null && kind.IsActive(c)) return kind.Id;
            return null;
        }

        // setting a kind clears the others (exactly one kind set per the model's emit convention)
        private static void SetConditionKind(BreakpointSpec bp, string id)
        {
            var c = new BreakpointCondition();
            switch (id)
            {
                case "orientation": c.orientation = "portrait"; break;
                case "minAspect": c.minAspect = 1.7777f; break;
                case "maxAspect": c.maxAspect = 1f; break;
                case "minWidth": c.minWidth = 1024f; break;
                case "maxWidth": c.maxWidth = 768f; break;
                // unknown/project kind: leave empty; the project's evaluator reads its own param off
                // the condition, which it would have extended — no built-in seed to set here.
            }
            bp.when = c;
        }

        private static float ConditionValue(BreakpointCondition c, string id)
        {
            switch (id)
            {
                case "minAspect": return c.minAspect ?? 0f;
                case "maxAspect": return c.maxAspect ?? 0f;
                case "minWidth": return c.minWidth ?? 0f;
                case "maxWidth": return c.maxWidth ?? 0f;
                default: return 0f;
            }
        }

        private static void SetConditionValue(BreakpointCondition c, string id, float v)
        {
            switch (id)
            {
                case "minAspect": c.minAspect = v; break;
                case "maxAspect": c.maxAspect = v; break;
                case "minWidth": c.minWidth = v; break;
                case "maxWidth": c.maxWidth = v; break;
            }
        }

        // ------------------------------------------------------------------ mutations

        private void AddBreakpoint()
        {
            string name = UniqueName("breakpoint");
            Apply(() =>
            {
                if (Spec.breakpoints == null) Spec.breakpoints = new List<BreakpointSpec>();
                Spec.breakpoints.Add(new BreakpointSpec { name = name, when = new BreakpointCondition() });
            }, "Add Breakpoint");
            _expanded = true;
            _document.SetActiveBreakpoint(name);
        }

        private void RenameBreakpoint(BreakpointSpec bp, string raw)
        {
            string newName = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(newName) || newName == bp.name) return;
            if (NameTaken(newName, bp))
            {
                Debug.LogWarning($"BreakpointBar: a breakpoint named '{newName}' already exists; rename ignored.");
                return;
            }
            string old = bp.name;
            bool wasActive = string.Equals(_document.ActiveBreakpoint, old, System.StringComparison.Ordinal);
            Apply(() =>
            {
                bp.name = newName;
                // carry every element's override key from the old name to the new one
                RenameOverrideKey(old, newName);
            }, "Rename Breakpoint");
            if (wasActive) _document.SetActiveBreakpoint(newName);
        }

        private void DeleteBreakpoint(BreakpointSpec bp)
        {
            string name = bp.name;
            bool wasActive = string.Equals(_document.ActiveBreakpoint, name, System.StringComparison.Ordinal);
            if (wasActive) _document.SetActiveBreakpoint(""); // leave the scope before the name disappears
            Apply(() =>
            {
                Spec.breakpoints.Remove(bp);
                RemoveOverrideKey(name);
            }, "Delete Breakpoint");
        }

        // propagate a rename / delete across every element's overrides dict (else stale keys orphan)
        private void RenameOverrideKey(string oldName, string newName)
        {
            foreach (ViewSpec view in Spec.views) RenameInElements(view.elements, oldName, newName);
            foreach (PopupSpec popup in Spec.popups) RenameInElements(popup.elements, oldName, newName);
        }

        private static void RenameInElements(List<ElementSpec> elements, string oldName, string newName)
        {
            if (elements == null) return;
            foreach (ElementSpec e in elements)
            {
                if (e.overrides != null && e.overrides.TryGetValue(oldName, out LayoutSpec d))
                {
                    e.overrides.Remove(oldName);
                    e.overrides[newName] = d;
                }
                RenameInElements(e.children, oldName, newName);
            }
        }

        private void RemoveOverrideKey(string name)
        {
            foreach (ViewSpec view in Spec.views) RemoveInElements(view.elements, name);
            foreach (PopupSpec popup in Spec.popups) RemoveInElements(popup.elements, name);
        }

        private static void RemoveInElements(List<ElementSpec> elements, string name)
        {
            if (elements == null) return;
            foreach (ElementSpec e in elements)
            {
                if (e.overrides != null && e.overrides.Remove(name) && e.overrides.Count == 0) e.overrides = null;
                RemoveInElements(e.children, name);
            }
        }

        private bool NameTaken(string name, BreakpointSpec except)
        {
            if (Spec.breakpoints == null) return false;
            foreach (BreakpointSpec b in Spec.breakpoints)
                if (b != null && b != except && string.Equals(b.name, name, System.StringComparison.Ordinal)) return true;
            return false;
        }

        private string UniqueName(string baseName)
        {
            string name = baseName;
            int n = 1;
            while (NameTaken(name, null)) name = baseName + (++n);
            return name;
        }

        private void Apply(System.Action mutate, string label) => _document.ApplyEdit(mutate, label);
    }
}
