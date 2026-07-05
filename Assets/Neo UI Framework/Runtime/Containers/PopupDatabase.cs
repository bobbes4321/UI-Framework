using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Popup prefab lookup by name. One entry per popup prefab (the link-asset model collapsed
    /// into a single flat, agent-readable database).
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Databases/Popup Database", fileName = "PopupDatabase")]
    public class PopupDatabase : ScriptableObject
    {
        [Serializable]
        public class PopupLink
        {
            public string popupName;
            public GameObject prefab;
        }

        [SerializeField] private List<PopupLink> popups = new List<PopupLink>();

        public IReadOnlyList<PopupLink> Popups => popups;

        public IEnumerable<string> GetPopupNames() => popups.Select(p => p.popupName);

        public bool Contains(string popupName) =>
            popups.Any(p => string.Equals(p.popupName, popupName, StringComparison.Ordinal));

        public GameObject GetPrefab(string popupName) =>
            popups.FirstOrDefault(p => string.Equals(p.popupName, popupName, StringComparison.Ordinal))?.prefab;

        public void AddOrUpdate(string popupName, GameObject prefab)
        {
            if (string.IsNullOrWhiteSpace(popupName)) return;
            PopupLink link = popups.FirstOrDefault(p => p.popupName == popupName);
            if (link == null)
            {
                link = new PopupLink { popupName = popupName };
                popups.Add(link);
                popups.Sort((a, b) => string.CompareOrdinal(a.popupName, b.popupName));
            }
            link.prefab = prefab;
        }

        public bool Remove(string popupName) => popups.RemoveAll(p => p.popupName == popupName) > 0;

        /// <summary>
        /// Drops links whose prefab reference is gone (generated popups whose assets were
        /// deleted). Returns the removed names so callers can report them loudly.
        /// </summary>
        public List<string> PruneDanglingLinks()
        {
            var removed = new List<string>();
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                if (popups[i].prefab != null) continue;
                removed.Add(popups[i].popupName);
                popups.RemoveAt(i);
            }
            return removed;
        }
    }
}
