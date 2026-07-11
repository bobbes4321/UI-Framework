using Neo.UI;
using NUnit.Framework;

// No namespace on purpose: a [SetUpFixture] in the global namespace wraps the ENTIRE test assembly
// (same reasoning as the EditMode NeoTestScratchRoot).

/// <summary>
/// Points <see cref="NeoUISettings.instance"/> at an in-memory scratch copy
/// (<see cref="NeoUISettings.CreateScratchCopy"/>) for the whole PlayMode run, so fixtures that
/// run the generator (SettingsCheatsDemoPlayTest and friends) register ids/popups/presets into
/// throwaway database clones instead of dirtying the committed assets under Resources — before
/// this, a test run left dozens of scratch ids in the ID Database Manager. Runtime behavior is
/// unchanged: ids are raw category/name strings on components, the databases are editor-picker
/// autocomplete only. EditMode sibling: the same redirect inside NeoTestScratchRoot.
/// </summary>
[SetUpFixture]
public class NeoPlayModeScratchSettings
{
    private NeoUISettings _realSettings;
    private NeoUISettings _scratchSettings;

    [OneTimeSetUp]
    public void RedirectSettings()
    {
        _realSettings = NeoUISettings.instance;
        if (_realSettings == null) return;
        _scratchSettings = _realSettings.CreateScratchCopy();
        NeoUISettings.instance = _scratchSettings;
    }

    [OneTimeTearDown]
    public void RestoreSettings()
    {
        NeoUISettings.instance = _realSettings;
        NeoUISettings.DestroyScratchCopy(_scratchSettings);
        _scratchSettings = null;
    }
}
