using Neo.UI.Editor.Composer.Automation;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The probe scenario DSL and its action registry — pure model, no window. Proves a scenario JSON
    /// parses to the intended steps/typed fields and that the built-in step kinds are registered through
    /// the extension seam (so a project can add its own the same way).
    /// </summary>
    public class ComposerScenarioParseTests
    {
        [Test]
        public void Parse_ReadsHeaderAndSteps()
        {
            const string json = @"{
                ""name"": ""demo"",
                ""open"": ""new"",
                ""width"": 1080, ""height"": 1920,
                ""steps"": [
                    { ""action"": ""addWidget"", ""kind"": ""vstack"" },
                    { ""action"": ""drag"", ""path"": ""views/Menu/Main/elements[0]"", ""dx"": 240, ""dy"": -160 },
                    { ""action"": ""nudge"", ""path"": ""views/Menu/Main/elements[0]"", ""count"": 5, ""shift"": true }
                ]
            }";

            ComposerScenario s = ComposerScenario.FromJson(json);

            Assert.AreEqual("demo", s.name);
            Assert.AreEqual("new", s.open);
            Assert.AreEqual(1080, s.width);
            Assert.AreEqual(1920, s.height);
            Assert.AreEqual(3, s.steps.Count);

            Assert.AreEqual("addWidget", s.steps[0].action);
            Assert.AreEqual("vstack", s.steps[0].GetString("kind"));

            Assert.AreEqual("drag", s.steps[1].action);
            Assert.AreEqual(240f, s.steps[1].GetFloat("dx"));
            Assert.AreEqual(-160f, s.steps[1].GetFloat("dy"));

            Assert.AreEqual(5, s.steps[2].GetInt("count"));
            Assert.IsTrue(s.steps[2].GetBool("shift"));
            Assert.IsFalse(s.steps[2].GetBool("missing"));
        }

        [Test]
        public void Parse_SkipsStepsWithoutAnAction()
        {
            const string json = @"{ ""steps"": [ { ""kind"": ""button"" }, { ""action"": ""undo"" } ] }";
            ComposerScenario s = ComposerScenario.FromJson(json);
            Assert.AreEqual(1, s.steps.Count, "a step with no action is not a runnable intent");
            Assert.AreEqual("undo", s.steps[0].action);
        }

        [Test]
        public void Actions_BuiltInsRegistered_UnknownIsNot()
        {
            foreach (string built in new[] { "select", "drag", "resize", "nudge", "addWidget",
                         "setDevice", "resizeDevice", "setBreakpoint", "undo", "redo", "settle", "capture" })
                Assert.IsTrue(ComposerProbeActions.TryGet(built, out _), $"'{built}' should be registered");

            Assert.IsFalse(ComposerProbeActions.TryGet("teleport", out _));
        }

        [Test]
        public void Actions_RegisterIsAnOpenSeam()
        {
            bool ran = false;
            ComposerProbeActions.Register("test/custom-probe-step", (d, s) => ran = true);
            Assert.IsTrue(ComposerProbeActions.TryGet("test/custom-probe-step", out var h));
            h(null, null);
            Assert.IsTrue(ran, "a project-registered step kind must dispatch like a built-in");
        }
    }
}
