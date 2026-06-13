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
/// </summary>
[SetUpFixture]
public class NeoTestScratchRoot
{
    /// <summary> Throwaway root tests generate into. Deliberately NOT a prefix of the real root. </summary>
    public const string ScratchRoot = "Assets/NeoUITestScratch";

    [OneTimeSetUp]
    public void RedirectGeneratedRoot()
    {
        UISpecGenerator.GeneratedRoot = ScratchRoot;
    }

    [OneTimeTearDown]
    public void RestoreGeneratedRoot()
    {
        AssetDatabase.DeleteAsset(ScratchRoot);
        UISpecGenerator.GeneratedRoot = UISpecGenerator.DefaultGeneratedRoot;
        AssetDatabase.SaveAssets();
    }
}
