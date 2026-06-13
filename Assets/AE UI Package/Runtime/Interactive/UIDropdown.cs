using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Payload published on "UIDropdown/Behaviour" when a dropdown selection changes. </summary>
    [Serializable]
    public struct DropdownSignalData
    {
        public string category;
        public string dropdownName;
        public int index;
        public string option;
    }

    /// <summary>
    /// Single-choice dropdown with category/name id and a behaviour signal. Extends TMP_Dropdown so the
    /// popup list, scrolling and keyboard navigation come from TextMeshPro; the value is the selected
    /// option index. Populate at author time or runtime via <see cref="SetStringOptions"/>.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Interactive/UI Dropdown")]
    public class UIDropdown : TMP_Dropdown
    {
        public const string StreamCategory = "UIDropdown";
        public const string StreamName = "Behaviour";

        public DropdownId id = new DropdownId();

        private static readonly HashSet<UIDropdown> Registry = new HashSet<UIDropdown>();

        public static IEnumerable<UIDropdown> allDropdowns => Registry;

        public static UIDropdown GetFirstDropdown(string category, string name) =>
            Registry.FirstOrDefault(d => d.id.Matches(category, name));

        protected override void OnEnable()
        {
            base.OnEnable();
            Registry.Add(this);
            onValueChanged.AddListener(Publish);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Registry.Remove(this);
            onValueChanged.RemoveListener(Publish);
        }

        /// <summary> Replaces the option list with plain strings, preserving the selected index when valid. </summary>
        public void SetStringOptions(IList<string> labels)
        {
            int previous = value;
            ClearOptions();
            if (labels != null && labels.Count > 0)
                AddOptions(labels.ToList());
            SetValueWithoutNotify(Mathf.Clamp(previous, 0, Mathf.Max(0, options.Count - 1)));
            RefreshShownValue();
        }

        private void Publish(int index)
        {
            string option = index >= 0 && index < options.Count ? options[index].text : null;
            Signals.Send(StreamCategory, StreamName,
                new DropdownSignalData { category = id.Category, dropdownName = id.Name, index = index, option = option }, this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
