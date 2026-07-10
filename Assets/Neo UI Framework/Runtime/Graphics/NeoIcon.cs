using TMPro;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Marks a TMP text as an icon glyph and carries its name-addressed identity ("play",
    /// "volume-x") — the icon analog of <see cref="WidgetStyleTag"/>: the name is the source of
    /// truth (the spec exporter reads it back verbatim instead of reverse-sniffing the glyph),
    /// the baked TMP character is its materialization (WYSIWYG). Also the runtime seam for
    /// swapping icons by name: <see cref="SetIcon"/> resolves through <see cref="IconLibrary"/>
    /// (project <see cref="IconMapOverlay"/> first) and rewrites the glyph. Stamped by
    /// UIWidgetFactory.CreateIcon; its inspector shows a searchable glyph-grid picker.
    /// </summary>
    [AddComponentMenu("Neo/UI/Graphics/Neo Icon")]
    [RequireComponent(typeof(TMP_Text)), DisallowMultipleComponent]
    public class NeoIcon : MonoBehaviour
    {
        [Tooltip("Icon name from the packaged Lucide set or the project's IconMapOverlay, e.g. \"play\"")]
        public string icon;

        /// <summary>
        /// Swaps the glyph by name at runtime (e.g. a mute button flipping "volume-2" → "volume-x").
        /// Aliases canonicalize ("home" → "house"); unknown names warn and keep the current glyph
        /// (no silent failures).
        /// </summary>
        public void SetIcon(string name)
        {
            if (!IconLibrary.TryResolveIcon(name, out ResolvedIcon resolved))
            {
                Debug.LogWarning($"[Neo.UI] Unknown icon '{name}' — keeping '{icon}'. " +
                                 "Valid names: see IconLibrary (Lucide set) + the project's IconMapOverlay.", this);
                return;
            }
            icon = resolved.name;
            Bake(resolved);
        }

        private void Bake(in ResolvedIcon resolved)
        {
            var text = GetComponent<TMP_Text>();
            if (text == null) return;
            if (resolved.isSprite)
            {
                if (text.spriteAsset != resolved.spriteAsset) text.spriteAsset = resolved.spriteAsset;
                if (!text.richText) text.richText = true; // sprite tags need rich text
            }
            else
            {
                NeoUISettings settings = NeoUISettings.instance;
                if (settings != null && settings.iconFont != null && text.font != settings.iconFont)
                    text.font = settings.iconFont;
            }
            string baked = resolved.BakedText;
            if (text.text != baked) text.text = baked;
        }

#if UNITY_EDITOR
        // hand-edits to the name field re-bake the glyph/sprite tag; idempotent, so loading a
        // generated prefab (name already matches the baked text) never dirties it
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(icon)) return;
            if (!IconLibrary.TryResolveIcon(icon, out ResolvedIcon resolved)) return;
            Bake(resolved);
        }
#endif
    }
}
