using Neo.UI.Editor.Composer;
using Neo.UI.Editor.Composer.Automation;
using NUnit.Framework;
using UnityEditor;

namespace Neo.UI.Tests
{
    /// <summary>
    /// End-to-end probe smoke test: opens the live Composer window, replays a scenario, and asserts the
    /// session mutated the in-memory document and recorded one step per intent. Uses driven steps
    /// (addWidget/undo) so it stays meaningful even under <c>-nographics</c>, where the preview can't
    /// render and window capture returns null (the session degrades, it doesn't throw). Plus a pure
    /// <see cref="SessionReport.Diff"/> check that needs no window.
    /// </summary>
    public class ComposerProbeTests
    {
        [TearDown]
        public void CloseWindow()
        {
            if (EditorWindow.HasOpenInstances<NeoComposerWindow>())
                EditorWindow.GetWindow<NeoComposerWindow>().Close();
        }

        [Test]
        public void RunSession_DrivenScenario_MutatesDocument_AndRecordsEveryStep()
        {
            ComposerScenario scenario = ComposerScenario.FromJson(@"{
                ""name"": ""probe-smoke"",
                ""open"": ""new"",
                ""steps"": [
                    { ""action"": ""addWidget"", ""kind"": ""vstack"" },
                    { ""action"": ""addWidget"", ""kind"": ""button"", ""target"": ""views/Menu/Main/elements[0]"" },
                    { ""action"": ""undo"" }
                ]
            }");

            SessionReport report = ComposerProbe.RunSession(scenario, new ProbeOptions { outputDir = "Temp/neo-composer-test" });

            Assert.IsNotNull(report);
            Assert.AreEqual(3, report.steps.Count, "one record per scenario step");
            Assert.IsTrue(report.roundTrips, "the final document spec must round-trip losslessly");

            NeoComposerWindow window = EditorWindow.GetWindow<NeoComposerWindow>();
            // vstack added, button added, then undone → just the vstack survives at the view root
            Assert.AreEqual(1, window.Document.Spec.views[0].elements.Count);
            Assert.AreEqual("vstack", window.Document.Spec.views[0].elements[0].kind);
        }

        [Test]
        public void Diff_ComputesPerStepDeltas()
        {
            var before = new SessionReport { name = "x" };
            before.steps.Add(new StepRecord { index = 0, action = "drag", events = 6, latencyMs = 120 });
            var after = new SessionReport { name = "x" };
            after.steps.Add(new StepRecord { index = 0, action = "drag", events = 6, latencyMs = 80 });

            System.Collections.Generic.Dictionary<string, object> diff = SessionReport.Diff(before, after);

            Assert.AreEqual(-40d, (double)((System.Collections.Generic.Dictionary<string, object>)
                ((System.Collections.Generic.List<object>)diff["steps"])[0])["dLatencyMs"], 0.001,
                "a faster drag should show as a negative latency delta");
        }
    }
}
