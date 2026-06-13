using System.Linq;
using AlterEyes.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// The soft design lint as a "design reviewer": a button/tab with neither label nor icon is a
    /// mystery click target and must be flagged, while properly labelled or icon-only ones must not.
    /// </summary>
    public class DesignLintTests
    {
        private const string Spec = @"{
          ""views"": [ { ""id"": ""Lint/View"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 16, ""children"": [
              { ""button"": { ""id"": ""Lint/Labeled"", ""label"": ""Play"", ""onClick"": { ""signal"": ""X/Y"" } } },
              { ""button"": { ""id"": ""Lint/IconOnly"", ""icon"": ""play"", ""onClick"": { ""signal"": ""X/Y"" } } },
              { ""button"": { ""id"": ""Lint/Empty"", ""onClick"": { ""signal"": ""X/Y"" } } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset($"{UISpecGenerator.GeneratedRoot}/Views/Lint_View.prefab");
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void NoLabelOrIcon_IsFlagged_Otherwise_Not()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(Spec));
            Assert.IsEmpty(report.collisions, report.ToString());

            var warnings = AgentValidation.ValidateDesign();
            bool NoContentFor(string name) =>
                warnings.Any(w => w.Contains(name) && w.Contains("no label or icon"));

            Assert.IsTrue(NoContentFor("Lint_Empty"), "an empty button must be flagged:\n" + string.Join("\n", warnings));
            Assert.IsFalse(NoContentFor("Lint_Labeled"), "a labelled button must not be flagged");
            Assert.IsFalse(NoContentFor("Lint_IconOnly"), "an icon-only button must not be flagged");
        }
    }
}
