using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Coverage for the clean-slate reset (<see cref="NeoProjectReset"/> / <see cref="NeoResetComponents"/>):
    /// the built-in component catalog mirrors every create-or-repair bootstrap, every registered path
    /// passes the delete-safety guard (a mis-registered descriptor must never be able to take out package
    /// code, the NeoShape shader or the showcase specs), plan building is existence-filtered and loud
    /// about unknown ids / unsafe paths, and Execute really deletes — proven end-to-end against a
    /// registered probe component in the test scratch root, never against real project assets.
    /// </summary>
    public class ProjectResetTests
    {
        private const string ScratchFolder = "Assets/NeoUITestScratchReset";

        [TearDown]
        public void Cleanup()
        {
            NeoResetComponents.ResetForTests();
            AssetDatabase.DeleteAsset(ScratchFolder);
        }

        // ------------------------------------------------------------------ catalog

        [Test]
        public void BuiltIns_CoverEverySetupBootstrap()
        {
            // one reset component per Setup-wizard installable + the generated-content roots — the
            // uninstall side must never lag the install side
            string[] required =
            {
                "settings", "starter-kit", "fonts", "widget-presets", "animations", "transitions",
                "effects", "generated-ui", "showcases", "custom-themes", "binding-stubs",
            };
            var ids = new HashSet<string>(NeoResetComponents.All.Select(c => c.id));
            foreach (string id in required)
                Assert.IsTrue(ids.Contains(id), $"missing built-in reset component '{id}'");
        }

        [Test]
        public void BuiltIns_KeepCuratedLibrariesAndUserContentByDefault()
        {
            // nobody plausibly wants these empty (or they're user-authored) — unchecked by default
            string[] kept = { "fonts", "animations", "transitions", "showcases", "custom-themes", "binding-stubs" };
            // the bootstrap-owned rest is exactly what a fresh project wouldn't have — checked by default
            string[] removed = { "settings", "starter-kit", "widget-presets", "effects", "generated-ui" };

            foreach (string id in kept)
            {
                Assert.IsTrue(NeoResetComponents.TryGet(id, out ResetComponentDescriptor c), id);
                Assert.IsTrue(c.keepByDefault, $"'{id}' should be kept by default");
            }
            foreach (string id in removed)
            {
                Assert.IsTrue(NeoResetComponents.TryGet(id, out ResetComponentDescriptor c), id);
                Assert.IsFalse(c.keepByDefault, $"'{id}' should be selected for deletion by default");
            }
        }

        [Test]
        public void EveryBuiltInCleanPath_PassesTheSafetyGuard()
        {
            foreach (ResetComponentDescriptor component in NeoResetComponents.All)
            {
                foreach (string path in component.cleanPaths())
                {
                    Assert.IsTrue(NeoProjectReset.IsSafeToDelete(path),
                        $"built-in component '{component.id}' registered an unsafe path '{path}'");
                }
            }
        }

        // ------------------------------------------------------------------ safety guard

        [Test]
        public void SafetyGuard_RefusesPackageCodeSpecsAndAncestors()
        {
            string[] refused =
            {
                null, "", "Assets",
                "Assets/Neo UI Framework",                          // package root
                "Assets/Neo UI Framework/Runtime",                  // package code
                "Assets/Neo UI Framework/Editor",
                "Assets/Neo UI Framework/Tests",
                "Assets/Neo UI Framework/Resources",                // holds the shared shader + settings
                "Assets/Neo UI Framework/Resources/NeoShape.shader",
                "Assets/Showcases",                                 // ancestor of the committed specs
                "Assets/Showcases/Specs",
                "Packages/com.some.package/Runtime",                // outside Assets entirely
                "Assets/../ProjectSettings",                        // parent traversal
            };
            foreach (string path in refused)
                Assert.IsFalse(NeoProjectReset.IsSafeToDelete(path), $"guard should refuse '{path}'");
        }

        [Test]
        public void SafetyGuard_AllowsBootstrapOutputAndScratch()
        {
            string[] allowed =
            {
                "Assets/Neo UI Framework/Starter",
                "Assets/Neo UI Framework/Resources/Effects",        // descendant of a protected root is fine
                "Assets/Neo UI Framework/Resources/NeoUISettings.asset",
                "Assets/Showcases/buttons/Generated",
                "Assets/Neo UI Generated",
                ScratchFolder,
            };
            foreach (string path in allowed)
                Assert.IsTrue(NeoProjectReset.IsSafeToDelete(path), $"guard should allow '{path}'");
        }

        // ------------------------------------------------------------------ plan

        [Test]
        public void BuildPlan_UnknownId_WarnsAndSkips()
        {
            LogAssert.Expect(LogType.Warning, new Regex("unknown component 'nope'"));
            ResetPlan plan = NeoProjectReset.BuildPlan(new[] { "nope" });
            Assert.AreEqual(0, plan.TotalPathCount);
        }

        [Test]
        public void BuildPlan_MissingPaths_AreDropped()
        {
            NeoResetComponents.Register(new ResetComponentDescriptor
            {
                id = "test-missing", label = "Test Missing",
                cleanPaths = () => new[] { ScratchFolder + "/DoesNotExist" },
            });
            ResetPlan plan = NeoProjectReset.BuildPlan(new[] { "test-missing" });
            Assert.AreEqual(0, plan.TotalPathCount, "a path that doesn't exist plans nothing");
        }

        [Test]
        public void BuildPlan_UnsafeProjectRegisteredPath_IsRefusedNotPlanned()
        {
            NeoResetComponents.Register(new ResetComponentDescriptor
            {
                id = "test-unsafe", label = "Test Unsafe",
                cleanPaths = () => new[] { "Assets/Neo UI Framework/Editor" },
            });
            LogAssert.Expect(LogType.Warning, new Regex("refusing unsafe path"));
            ResetPlan plan = NeoProjectReset.BuildPlan(new[] { "test-unsafe" });
            Assert.AreEqual(0, plan.TotalPathCount, "an unsafe path must never make it into a plan");
        }

        // ------------------------------------------------------------------ execute (scratch only)

        [Test]
        public void Execute_DeletesARegisteredComponentsPaths_EndToEnd()
        {
            if (!AssetDatabase.IsValidFolder(ScratchFolder))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratchReset");
            var probe = ScriptableObject.CreateInstance<Theme>();
            AssetDatabase.CreateAsset(probe, ScratchFolder + "/ResetProbe.asset");

            NeoResetComponents.Register(new ResetComponentDescriptor
            {
                id = "test-probe", label = "Test Probe",
                cleanPaths = () => new[] { ScratchFolder },
            });

            ResetPlan plan = NeoProjectReset.BuildPlan(new[] { "test-probe" });
            Assert.AreEqual(1, plan.TotalPathCount, "the existing scratch folder should be planned");

            ResetReport report = NeoProjectReset.Execute(plan);

            Assert.AreEqual(1, report.deletedPaths);
            Assert.IsEmpty(report.failedPaths);
            Assert.IsFalse(AssetDatabase.IsValidFolder(ScratchFolder), "the planned folder was deleted");
        }

        [Test]
        public void Execute_EmptyPlan_IsANoOp()
        {
            ResetReport report = NeoProjectReset.Execute(new ResetPlan());
            Assert.AreEqual(0, report.deletedPaths);
            Assert.AreEqual("Nothing to delete.", report.Summary);
        }
    }
}
