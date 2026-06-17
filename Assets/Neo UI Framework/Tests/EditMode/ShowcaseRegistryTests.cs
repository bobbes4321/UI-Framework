using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pattern R contract for <see cref="ShowcaseRegistry"/> — mirrors <see cref="ThemeBundleRegistryTests"/>:
    /// <c>Register</c> appends a novel id and replaces by id without growing, <c>TryGet</c> hits/misses,
    /// the derived <see cref="Showcase.GeneratedRoot"/>/<see cref="Showcase.ScenePath"/> follow the id,
    /// and <see cref="ShowcaseDefinition.ToShowcase"/> maps fields + applies id/title fallbacks. The
    /// registry is reset around each test (<see cref="ShowcaseRegistry.ResetForTests"/>) so it never
    /// pollutes sibling suites in the same domain.
    /// </summary>
    public class ShowcaseRegistryTests
    {
        [SetUp]
        public void Reset() => ShowcaseRegistry.ResetForTests();

        [TearDown]
        public void Cleanup() => ShowcaseRegistry.ResetForTests();

        private static Showcase Probe(string id, string title = "Probe") =>
            new Showcase { id = id, title = title, category = "Test" };

        [Test]
        public void Register_AppendsNovelId_ThenResolvableViaTryGet()
        {
            // An id that is NOT a code-seeded built-in, so registering it genuinely appends (rather than
            // replacing in place). Guarded so a future built-in of this id can't silently weaken the test.
            const string novelId = "novel-probe-id";
            Assert.IsFalse(ShowcaseRegistry.Ids.Contains(novelId), "precondition: the probe id must be novel");
            int before = ShowcaseRegistry.All.Count;

            ShowcaseRegistry.Register(Probe(novelId, "Buttons"));

            Assert.AreEqual(before + 1, ShowcaseRegistry.All.Count, "a novel id appends");
            Assert.Contains(novelId, ShowcaseRegistry.Ids.ToList());
            Assert.IsTrue(ShowcaseRegistry.TryGet(novelId, out Showcase got));
            Assert.AreEqual("Buttons", got.title);
        }

        [Test]
        public void Register_SameId_ReplacesInPlace_WithoutDuplicating()
        {
            ShowcaseRegistry.Register(Probe("toggles", "First"));
            int afterFirst = ShowcaseRegistry.All.Count;

            ShowcaseRegistry.Register(Probe("toggles", "Second"));

            Assert.AreEqual(afterFirst, ShowcaseRegistry.All.Count, "same id replaces, never duplicates");
            Assert.IsTrue(ShowcaseRegistry.TryGet("toggles", out Showcase got));
            Assert.AreEqual("Second", got.title);
        }

        [Test]
        public void TryGet_Miss_ReturnsFalseAndNull()
        {
            Assert.IsFalse(ShowcaseRegistry.TryGet("does-not-exist", out Showcase missing));
            Assert.IsNull(missing);
            Assert.IsFalse(ShowcaseRegistry.TryGet(null, out _));
        }

        [Test]
        public void Register_IgnoresNullOrIdlessShowcases()
        {
            int before = ShowcaseRegistry.All.Count;
            ShowcaseRegistry.Register(null);
            ShowcaseRegistry.Register(new Showcase { id = null });
            ShowcaseRegistry.Register(new Showcase { id = "" });
            Assert.AreEqual(before, ShowcaseRegistry.All.Count);
        }

        [Test]
        public void Showcase_GeneratedRootAndScenePath_DeriveFromId()
        {
            var s = new Showcase { id = "buttons" };
            Assert.AreEqual($"{ShowcaseRegistry.ShowcasesRoot}/buttons/Generated", s.GeneratedRoot);
            Assert.AreEqual($"{ShowcaseRegistry.ShowcasesRoot}/buttons/buttons.unity", s.ScenePath);
        }

        [Test]
        public void DistinctIds_ProduceDisjointGeneratedRoots()
        {
            var a = new Showcase { id = "buttons" };
            var b = new Showcase { id = "toggles" };
            Assert.AreNotEqual(a.GeneratedRoot, b.GeneratedRoot);
            Assert.AreNotEqual(a.ScenePath, b.ScenePath);
        }

        [Test]
        public void ShowcaseDefinition_ToShowcase_MapsFields()
        {
            var def = UnityEngine.ScriptableObject.CreateInstance<ShowcaseDefinition>();
            try
            {
                def.id = "popups";
                def.title = "Popups";
                def.description = "plain + rich popups";
                def.category = "Overlays";
                def.flowName = "Popups";

                Showcase s = def.ToShowcase();
                Assert.AreEqual("popups", s.id);
                Assert.AreEqual("Popups", s.title);
                Assert.AreEqual("plain + rich popups", s.description);
                Assert.AreEqual("Overlays", s.category);
                Assert.AreEqual("Popups", s.flowName);
                // no specJson/thumbnail assigned → null paths (no scan/fallback to a bogus path)
                Assert.IsNull(s.specPath);
                Assert.IsNull(s.thumbnail);
            }
            finally { UnityEngine.Object.DestroyImmediate(def); }
        }

        [Test]
        public void ShowcaseDefinition_ToShowcase_FallsBackIdFromName_AndTitleFromId()
        {
            var def = UnityEngine.ScriptableObject.CreateInstance<ShowcaseDefinition>();
            try
            {
                def.name = "fallback-showcase"; // id blank → falls back to the asset name
                def.id = "";
                def.title = ""; // title blank → falls back to the (resolved) id

                Showcase s = def.ToShowcase();
                Assert.AreEqual("fallback-showcase", s.id);
                Assert.AreEqual("fallback-showcase", s.title);
            }
            finally { UnityEngine.Object.DestroyImmediate(def); }
        }
    }
}
