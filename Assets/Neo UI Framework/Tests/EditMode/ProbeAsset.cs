using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Throwaway ScriptableObject used by <see cref="NeoAssetRegistryTests"/> to exercise
    /// <c>NeoAssetRegistry&lt;TAsset,TEntry&gt;</c> discovery. Must live in its own top-level file
    /// (not nested) so Unity can associate a MonoScript when <c>AssetDatabase.CreateAsset</c>
    /// persists an instance to disk under the scratch root.
    /// </summary>
    public sealed class ProbeAsset : ScriptableObject
    {
        public string probeKey;
    }
}
