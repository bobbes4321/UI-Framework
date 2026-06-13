using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Marks a RectTransform as a named parent/positioning target (for popups and tooltips),
    /// addressed by category/name through a static registry.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("AlterEyes/UI/UI Tag")]
    public class UITag : MonoBehaviour
    {
        public TagId id = new TagId();

        private static readonly HashSet<UITag> Registry = new HashSet<UITag>();

        public RectTransform rectTransform => (RectTransform)transform;

        public static IEnumerable<UITag> allTags => Registry;

        public static UITag GetFirstTag(string category, string name) =>
            Registry.FirstOrDefault(t => t.id.Matches(category, name));

        public static IEnumerable<UITag> GetTags(string category, string name) =>
            Registry.Where(t => t.id.Matches(category, name));

        public static IEnumerable<UITag> GetAllTagsInCategory(string category) =>
            Registry.Where(t => t.id.Category == category);

        private void OnEnable() => Registry.Add(this);
        private void OnDisable() => Registry.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
