using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The validation-rule registry seam: a project can register a custom rule into the right
    /// bucket (Hard fires in ValidateAll, Design only in ValidateDesign), declare custom wiring
    /// live so the dead-interaction lint stops flagging it, and override the blessed spacing scale
    /// once on settings so the lint and the authoring tools agree. Built-ins always run regardless.
    /// </summary>
    public class NeoValidationRulesTests
    {
        /// <summary> A rule that always reports one finding, tagged with its bucket. </summary>
        private sealed class TaggingRule : INeoValidationRule
        {
            private readonly string _tag;
            public ValidationBucket Bucket { get; }
            public TaggingRule(ValidationBucket bucket, string tag) { Bucket = bucket; _tag = tag; }
            public void Validate(ValidationContext ctx) => ctx.Add(_tag);
        }

        private readonly List<INeoValidationRule> _registered = new();
        private readonly List<System.Func<GameObject, bool>> _providers = new();

        private void Reg(INeoValidationRule rule) { NeoValidationRules.Register(rule); _registered.Add(rule); }
        private void Reg(System.Func<GameObject, bool> p) { NeoInteractivityProviders.Register(p); _providers.Add(p); }

        [TearDown]
        public void TearDown()
        {
            foreach (INeoValidationRule r in _registered) NeoValidationRules.Unregister(r);
            foreach (System.Func<GameObject, bool> p in _providers) NeoInteractivityProviders.Unregister(p);
            _registered.Clear();
            _providers.Clear();
        }

        [Test]
        public void Register_AddsToAll_AndUnregisterRemoves()
        {
            var rule = new TaggingRule(ValidationBucket.Design, "x");
            Assert.IsFalse(NeoValidationRules.All.Contains(rule));
            Reg(rule);
            Assert.IsTrue(NeoValidationRules.All.Contains(rule));
            Reg(rule); // idempotent per-instance
            Assert.AreEqual(1, NeoValidationRules.All.Count(r => r == rule));
        }

        [Test]
        public void DesignRule_FiresInDesign_NotInValidateAll()
        {
            const string tag = "DESIGN_RULE_MARKER_42";
            Reg(new TaggingRule(ValidationBucket.Design, tag));

            List<string> warnings = AgentValidation.ValidateDesign();
            List<string> issues = AgentValidation.ValidateAll();

            Assert.IsTrue(warnings.Contains(tag), "a Design rule must fire in ValidateDesign");
            Assert.IsFalse(issues.Contains(tag), "a Design rule must NOT fire in ValidateAll");
        }

        [Test]
        public void HardRule_FiresInValidateAll()
        {
            const string tag = "HARD_RULE_MARKER_7";
            Reg(new TaggingRule(ValidationBucket.Hard, tag));

            List<string> issues = AgentValidation.ValidateAll();
            List<string> warnings = AgentValidation.ValidateDesign();

            Assert.IsTrue(issues.Contains(tag), "a Hard rule must fire in ValidateAll");
            Assert.IsFalse(warnings.Contains(tag), "a Hard rule must NOT fire in ValidateDesign");
        }

        [Test]
        public void SpacingScale_ReadsSettings_SingleSourceOfTruth()
        {
            NeoUISettings settings = NeoUISettings.instance;
            Assume.That(settings, Is.Not.Null, "needs a NeoUISettings asset");

            float[] original = settings.spacingScale;
            try
            {
                // A value off the default scale but present on a project's custom scale.
                settings.spacingScale = new[] { 0f, 10f, 20f, 30f };
                CollectionAssert.Contains(NeoWidgetOptions.SpacingScale, 30f,
                    "NeoWidgetOptions.SpacingScale must read the settings field (one source of truth)");
                CollectionAssert.DoesNotContain(NeoWidgetOptions.SpacingScale, 16f,
                    "the default 16 should be gone once the project overrides the scale");
            }
            finally
            {
                settings.spacingScale = original;
            }
        }
    }

    /// <summary>
    /// Interactivity provider seam: a button wired only by a project's custom component is flagged
    /// dead until the project registers a provider that claims it live. Generates a minimal view
    /// with an unwired button so the built-in dead-interaction lint has something to flag.
    /// </summary>
    public class NeoInteractivityProviderTests
    {
        // a deliberately unwired button (no onClick) so the built-in lint flags it dead
        private const string Spec = @"{
          ""views"": [ { ""id"": ""IProv/View"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 16, ""children"": [
              { ""button"": { ""id"": ""IProv/Custom"", ""label"": ""Go"" } }
            ] } }
          ] } ]
        }";

        private System.Func<GameObject, bool> _provider;

        [OneTimeSetUp]
        public void Generate()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(Spec));
            Assert.IsEmpty(report.collisions, report.ToString());
        }

        [TearDown]
        public void TearDown()
        {
            if (_provider != null) { NeoInteractivityProviders.Unregister(_provider); _provider = null; }
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset($"{UISpecGenerator.GeneratedRoot}/Views/IProv_View.prefab");
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void UnwiredButton_FlaggedDead_UntilProviderClaimsIt()
        {
            bool DeadButton(List<string> issues) =>
                issues.Any(i => i.Contains("IProv/Custom") && i.Contains("does nothing"));

            Assert.IsTrue(DeadButton(AgentValidation.ValidateAll()),
                "an unwired button must be flagged dead by the built-in lint");

            // The project declares the button live through the lightweight provider seam.
            _provider = go =>
            {
                var b = go.GetComponent<UIButton>();
                return b != null && b.id.ToString() == "IProv/Custom";
            };
            NeoInteractivityProviders.Register(_provider);

            Assert.IsFalse(DeadButton(AgentValidation.ValidateAll()),
                "once a provider claims it wired, the button must no longer be flagged dead");
        }
    }
}
