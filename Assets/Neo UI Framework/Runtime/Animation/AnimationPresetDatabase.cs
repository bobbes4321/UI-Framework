using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Neo.UI
{
    /// <summary> Registry of animation presets addressable by name (and optionally category). </summary>
    [CreateAssetMenu(menuName = "Neo/UI/Databases/Animation Preset Database", fileName = "AnimationPresetDatabase")]
    public class AnimationPresetDatabase : ScriptableObject
    {
        [SerializeField] private List<UIAnimationPreset> presets = new List<UIAnimationPreset>();

        public IReadOnlyList<UIAnimationPreset> Presets => presets;

        public IEnumerable<string> GetPresetNames() => presets.Where(p => p != null).Select(p => p.presetName);

        public UIAnimationPreset Get(string presetName) =>
            presets.FirstOrDefault(p => p != null && string.Equals(p.presetName, presetName, StringComparison.Ordinal));

        public UIAnimationPreset Get(string category, string presetName) =>
            presets.FirstOrDefault(p => p != null && p.category == category && p.presetName == presetName);

        public bool Contains(string presetName) => Get(presetName) != null;

        public void AddOrUpdate(UIAnimationPreset preset)
        {
            if (preset == null) return;
            presets.RemoveAll(p => p == null || (p != preset && p.presetName == preset.presetName && p.category == preset.category));
            if (!presets.Contains(preset)) presets.Add(preset);
        }

        public bool Remove(UIAnimationPreset preset) => presets.Remove(preset);
    }
}
