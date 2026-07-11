using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;

// No namespace on purpose: a [SetUpFixture] in the global namespace wraps the ENTIRE test assembly,
// so the redirect is active regardless of which namespace individual test fixtures declare.

/// <summary>
/// Redirects <see cref="UISpecGenerator.GeneratedRoot"/> to a throwaway scratch folder for the whole
/// EditMode test run, then deletes that folder when the run finishes.
///
/// This is what keeps the committed demo under <see cref="UISpecGenerator.DefaultGeneratedRoot"/>
/// ("Assets/Neo UI Generated") intact. Most EditMode fixtures call
/// <c>AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot)</c> in their teardown; with this
/// redirect active those deletes only ever hit the scratch root. Before the redirect existed,
/// running the EditMode suite wiped the committed, GUID-referenced demo assets — a footgun the
/// docs could only mitigate ("regenerate after testing"), never prevent.
///
/// It ALSO points <see cref="NeoUISettings.instance"/> at an in-memory scratch copy
/// (<see cref="NeoUISettings.CreateScratchCopy"/>) for the run, so every id/popup/preset the
/// generator registers lands on throwaway database clones instead of dirtying the committed
/// assets under Resources — before this, a test run left dozens of scratch ids in the ID
/// Database Manager. Ids are raw strings on components; the databases are editor autocomplete
/// only, so generation and runtime behavior are unchanged. PlayMode sibling:
/// NeoPlayModeScratchSettings.
/// </summary>
[SetUpFixture]
public class NeoTestScratchRoot
{
    /// <summary> Throwaway root tests generate into. Deliberately NOT a prefix of the real root. </summary>
    public const string ScratchRoot = "Assets/NeoUITestScratch";

    private NeoUISettings _realSettings;
    private NeoUISettings _scratchSettings;

    [OneTimeSetUp]
    public void RedirectGeneratedRoot()
    {
        UISpecGenerator.GeneratedRoot = ScratchRoot;

        _realSettings = NeoUISettings.instance;
        if (_realSettings != null)
        {
            _scratchSettings = _realSettings.CreateScratchCopy();
            NeoUISettings.instance = _scratchSettings;
        }
    }

    [OneTimeTearDown]
    public void RestoreGeneratedRoot()
    {
        AssetDatabase.DeleteAsset(ScratchRoot);
        UISpecGenerator.GeneratedRoot = UISpecGenerator.DefaultGeneratedRoot;

        NeoUISettings.instance = _realSettings;
        NeoUISettings.DestroyScratchCopy(_scratchSettings);
        _scratchSettings = null;

        AssetDatabase.SaveAssets();
    }
}
