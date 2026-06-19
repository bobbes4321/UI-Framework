using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The ScriptableObject form of a <see cref="Showcase"/> — the no-C# extensibility seam. A consuming
    /// project (or this package) drops one of these assets anywhere under <c>Assets/</c>, and
    /// <see cref="ShowcaseRegistry.EnsureDiscovered"/> folds it into the registry on next access (a
    /// discovered definition overrides a built-in of the same id), so a new showcase ships without
    /// forking the package or writing any code.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Showcase Definition", fileName = "ShowcaseDefinition")]
    public class ShowcaseDefinition : ScriptableObject
    {
        [Tooltip("Stable, file-safe id (also the folder + scene name). Falls back to the asset name if blank.")]
        public string id;

        [Tooltip("Human-facing title shown in the Hub gallery. Falls back to the id if blank.")]
        public string title;

        [Tooltip("One-line description of what this showcase demonstrates.")]
        [TextArea]
        public string description;

        [Tooltip("Grouping bucket for the Hub gallery (e.g. Interactive, Layout, Theming).")]
        public string category;

        [Tooltip("The JSON spec this showcase generates from.")]
        public DefaultAsset specJson;

        [Tooltip("Explicit spec path, for when the spec is a .json (which imports as a TextAsset and so " +
                 "can't sit in the DefaultAsset slot above). Takes effect only when specJson is unset. The " +
                 "native 'new showcase' flow sets this to the conventional Specs/{id}.json path.")]
        public string specPathOverride;

        [Tooltip("Name of the flow graph to build the scene around.")]
        public string flowName;

        [Tooltip("Optional committed thumbnail for the gallery.")]
        public Texture2D thumbnail;

        /// <summary>
        /// Projects this definition into the plain <see cref="Showcase"/> the registry stores. Resolves
        /// the spec/thumbnail asset paths, and falls back to the asset name for a blank id and to the id
        /// for a blank title so a half-filled definition still produces a usable showcase.
        /// </summary>
        public Showcase ToShowcase()
        {
            string resolvedId = string.IsNullOrWhiteSpace(id) ? name : id.Trim();
            string resolvedTitle = string.IsNullOrWhiteSpace(title) ? resolvedId : title;
            // Prefer the wired asset; else the explicit override (the way a .json TextAsset spec is named);
            // else null — never fabricate a path for a definition that genuinely declares no spec.
            string specPath = specJson != null ? AssetDatabase.GetAssetPath(specJson)
                : !string.IsNullOrEmpty(specPathOverride) ? specPathOverride
                : null;
            return new Showcase
            {
                id = resolvedId,
                title = resolvedTitle,
                description = description,
                category = category,
                specPath = specPath,
                flowName = flowName,
                thumbnail = thumbnail != null ? AssetDatabase.GetAssetPath(thumbnail) : null
            };
        }
    }
}
