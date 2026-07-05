using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// The ScriptableObject form of a <see cref="TemplateEntry"/> — the no-C# extensibility seam for
    /// insertable layout scaffolds, mirroring <see cref="ShowcaseDefinition"/> /
    /// <see cref="ThemeBundleDefinition"/>. A consuming project (or designer) drops one of these assets
    /// anywhere under <c>Assets/</c> and <see cref="NeoLayoutTemplates"/> folds it into the
    /// <c>GameObject → Neo UI → Insert Template…</c> menu via the shared
    /// <see cref="NeoAssetRegistry{TAsset,TEntry}"/> discovery — no fork, no
    /// <see cref="NeoLayoutTemplates.Register"/> call. Before Phase 3.1 templates were the ONLY
    /// extension point in the package that required C#; this closes that gap.
    /// <para>
    /// Editor-only (it references the editor-side template pipeline), like the sibling definition assets.
    /// The spec JSON is held exactly the way <see cref="ShowcaseDefinition"/> holds its spec — a wired
    /// <see cref="TextAsset"/> (a <c>.json</c> imports as one) plus a <see cref="specPathOverride"/>
    /// escape hatch — and loaded lazily so a definition touches disk only when actually inserted. Parse
    /// validation is deliberately deferred to <see cref="NeoLayoutTemplates.Insert"/> (the same path the
    /// built-in <c>Templates~/*.json</c> scaffolds take), which warns cleanly on malformed JSON.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Layout Template Definition", fileName = "NeoLayoutTemplateDefinition")]
    public class NeoLayoutTemplateDefinition : ScriptableObject
    {
        [Tooltip("Stable id (also the picker key). Falls back to the asset name if blank. A discovered id " +
                 "that collides with a built-in or code-registered template is ignored (the earlier one wins).")]
        public string id;

        [Tooltip("Human label shown in the 'Insert Template…' picker. Falls back to the id if blank.")]
        public string displayName;

        [Tooltip("One-line description (tooltip in the picker).")]
        [TextArea]
        public string description;

        [Tooltip("The JSON spec fragment this template inserts (one or more views/popups, authored with the " +
                 "Figma-style layout model). A .json file imports as a TextAsset and drops straight into this slot.")]
        public TextAsset specJson;

        [Tooltip("Explicit spec path, used only when 'specJson' is unset — a project- or absolute path to a " +
                 "JSON file. Loaded lazily, only when the template is inserted.")]
        public string specPathOverride;

        /// <summary>
        /// Projects this definition into the <see cref="TemplateEntry"/> the registry stores. The loader is
        /// lazy (captured, not read here) so discovery is cheap and the JSON is read only on insertion, and
        /// falls back to the asset name for a blank id and to the id for a blank label.
        /// </summary>
        public TemplateEntry ToEntry()
        {
            string resolvedId = string.IsNullOrWhiteSpace(id) ? name : id.Trim();
            string label = string.IsNullOrWhiteSpace(displayName) ? resolvedId : displayName;

            // Capture only what the loader needs so the closure doesn't pin the whole SO.
            TextAsset json = specJson;
            string path = specPathOverride;
            string who = name;
            return new TemplateEntry(resolvedId, label, () => LoadJson(json, path, who), description);
        }

        /// <summary>
        /// Resolves the raw spec JSON: the wired <see cref="TextAsset"/> first, else a TextAsset (or raw
        /// file) at <see cref="specPathOverride"/>, else null with a single warning (the "no silent
        /// failures" rule) — <see cref="NeoLayoutTemplates.Insert"/> already treats null/empty JSON as a
        /// clean no-op.
        /// </summary>
        internal static string LoadJson(TextAsset specJson, string specPathOverride, string who)
        {
            if (specJson != null) return specJson.text;
            if (!string.IsNullOrEmpty(specPathOverride))
            {
                TextAsset ta = AssetDatabase.LoadAssetAtPath<TextAsset>(specPathOverride);
                if (ta != null) return ta.text;
                if (File.Exists(specPathOverride)) return File.ReadAllText(specPathOverride);
            }
            Debug.LogWarning($"[NeoLayoutTemplateDefinition] '{who}' declares no spec JSON " +
                             "(assign 'specJson' or a valid 'specPathOverride') — nothing to insert.");
            return null;
        }
    }
}
