using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The curated recipe library (Assets/docs/recipes) is agent-facing few-shot material AND a
    /// regression fixture: every recipe must generate cleanly (no collisions/issues) and pass the
    /// dead-interaction lint, so a recipe can never silently rot into something that doesn't build
    /// or has dead buttons. Also sanity-checks the generated JSON schema.
    /// </summary>
    public class RecipeLibraryTests
    {
        private static readonly string[] Recipes =
        {
            "settings-screen.json", "shop.json", "game-hud.json", "confirm-dialog.json"
        };

        private static string RecipeDir =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Assets", "docs", "recipes");

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            // recipes apply theme bundles — leave the project on the canonical starter system
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.theme != null)
                StarterKitBootstrap.ExpandTheme(settings.theme, new GenerateReport());
            AssetDatabase.SaveAssets();
        }

        [Test]
        [TestCase("settings-screen.json")]
        [TestCase("shop.json")]
        [TestCase("game-hud.json")]
        [TestCase("confirm-dialog.json")]
        public void Recipe_GeneratesCleanly(string recipe)
        {
            string path = Path.Combine(RecipeDir, recipe);
            Assert.IsTrue(File.Exists(path), $"recipe missing: {path}");

            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(File.ReadAllText(path)));
            Assert.IsEmpty(report.collisions, $"{recipe} collisions:\n{report}");
            Assert.IsEmpty(report.issues, $"{recipe} issues:\n{report}");
        }

        [Test]
        public void AllRecipes_HaveNoDeadInteractions()
        {
            foreach (string recipe in Recipes)
                UISpecGenerator.Generate(UISpec.FromJson(File.ReadAllText(Path.Combine(RecipeDir, recipe))));

            List<string> issues = AgentValidation.ValidateAll();
            var dead = issues.Where(i => i.Contains("does nothing") || i.Contains("controls nothing")).ToList();
            Assert.IsEmpty(dead, "recipes must have no dead buttons/tabs:\n" + string.Join("\n", dead));
        }

        [Test]
        public void JsonSchema_GeneratesAndDescribesElements()
        {
            string schema = SpecReference.WriteSchema("Temp/neo-test-schema.json");
            Assert.IsTrue(File.Exists(schema));

            var root = JsonReader.AsObject(MiniJson.Parse(File.ReadAllText(schema)), "schema");
            var defs = JsonReader.GetObject(root, "definitions");
            Assert.IsNotNull(defs, "schema must declare definitions");
            Assert.IsTrue(defs.ContainsKey("element") && defs.ContainsKey("elementBody") && defs.ContainsKey("view"),
                "schema must define element/elementBody/view");

            // the element definition enumerates every spec kind as a property
            var element = JsonReader.GetObject(defs, "element");
            var kindProps = JsonReader.GetObject(element, "properties");
            foreach (string kind in ElementSpec.Kinds)
                Assert.IsTrue(kindProps.ContainsKey(kind), $"schema element is missing kind '{kind}'");
        }
    }
}
