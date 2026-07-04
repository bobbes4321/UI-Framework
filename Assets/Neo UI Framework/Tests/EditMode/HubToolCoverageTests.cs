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
    /// Coverage net for the Hub's Tools tab: every editor menu item under
    /// <c>Tools/Neo UI/Setup/</c> and <c>Tools/Neo UI/Advanced/</c> must be surfaced as a
    /// <see cref="HubTool"/> in <see cref="HubToolRegistry.All"/> — OR be on the explicit
    /// <see cref="Allowlist"/> of deliberately-unsurfaced items below. This is exactly the bug that
    /// shipped: <c>Create or Repair Effect Assets</c> existed as a menu item but no Hub tool surfaced
    /// it, so it was invisible from the Hub. This test would have caught that.
    ///
    /// <para><b>Matching strategy — leaf == label.</b> <see cref="HubTool"/> does NOT store the menu
    /// path it fires (it carries an opaque <c>invoke</c> delegate). The Hub's <c>Menu(...)</c> helper
    /// always uses the menu LEAF (the substring after the last '/', including any trailing ellipsis)
    /// verbatim as the tool's <see cref="HubTool.label"/>. So we match a menu item to a Hub tool by
    /// comparing the menu leaf against <see cref="HubTool.label"/> (ordinal). This holds for every
    /// current Setup/Advanced tool. If a future tool diverges leaf-from-label, this test would flag it
    /// as "uncovered" — the fix then is to either align the label or store the path on HubTool.</para>
    /// </summary>
    public class HubToolCoverageTests
    {
        private const string SetupRoot = "Tools/Neo UI/Setup/";
        private const string AdvancedRoot = "Tools/Neo UI/Advanced/";

        /// <summary>
        /// Menu items under the Setup/Advanced roots that are deliberately NOT surfaced in the Hub.
        /// Each entry is the EXACT menu path and must carry a reason. Keep this list as short as
        /// possible — an item belongs here only when it is intentionally Hub-invisible, never as a way
        /// to silence the test for a tool that should have a launcher.
        /// </summary>
        private static readonly HashSet<string> Allowlist = new HashSet<string>
        {
            // Legacy per-widget preset library bootstrap. Superseded by the Starter Kit / theme-bundle
            // path in the Hub; left as a menu item for manual repair but intentionally not a Hub tool.
            "Tools/Neo UI/Setup/Create or Repair Widget Presets",
        };

        [SetUp]
        public void ResetRegistry() => HubToolRegistry.ResetForTests();

        [Test]
        public void EverySetupAndAdvancedMenuItem_IsSurfacedInTheHub_OrAllowlisted()
        {
            // The set of menu leaves the Hub surfaces (leaf == label by the Hub's Menu(...) contract).
            var hubLabels = new HashSet<string>(
                HubToolRegistry.All.Select(t => t.label).Where(l => !string.IsNullOrEmpty(l)));

            var uncovered = new List<string>();

            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (var attr in method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                {
                    string path = attr.menuItem;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!path.StartsWith(SetupRoot) && !path.StartsWith(AdvancedRoot)) continue;

                    // Validator entries gate another item's enabled state — they are not their own
                    // command, so they are never Hub tools.
                    if (attr.validate) continue;

                    // Separator / parent-only entries (trailing '/') aren't invocable commands.
                    string leaf = path.Substring(path.LastIndexOf('/') + 1);
                    if (string.IsNullOrEmpty(leaf)) continue;

                    if (Allowlist.Contains(path)) continue;

                    if (!hubLabels.Contains(leaf))
                        uncovered.Add(path);
                }
            }

            // Distinct + ordered so a failure message is stable regardless of TypeCache iteration order.
            var distinct = uncovered.Distinct().OrderBy(p => p).ToList();

            Assert.IsEmpty(distinct,
                "These Tools/Neo UI/Setup or /Advanced menu items have no Hub tool surfacing them " +
                "(leaf must equal a HubTool.label) and are not on the documented allowlist:\n  " +
                string.Join("\n  ", distinct) +
                "\nFix: add a Menu(...) entry in HubToolRegistryDefaults.Builtins() so the tool is " +
                "reachable from the Hub (or add it to the test allowlist with a reason if it is " +
                "intentionally Hub-invisible).");
        }

        [Test]
        public void EffectAssetsMenu_IsSurfacedInTheHub()
        {
            // Direct pin of the specific regression: the effect-assets Setup item must be a Hub tool.
            const string path = "Tools/Neo UI/Setup/Create or Repair Effect Assets";
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            Assert.IsTrue(HubToolRegistry.All.Any(t => t.label == leaf),
                $"No HubTool surfaces '{path}'. Expected a tool whose label is '{leaf}'.");
        }

        // ------------------------------------------------------------------ Wave 4 Task 4.4:
        // NeoKeyedRegistry<HubTool> contract mirror (replace-on-duplicate, invalid-register-warns).

        [Test]
        public void Register_SameId_ReplacesInPlace_NeverDuplicates()
        {
            int before = HubToolRegistry.All.Count;
            var first = new HubTool { id = "probe-tool", label = "Probe First", category = "Author", invoke = () => { } };
            var second = new HubTool { id = "probe-tool", label = "Probe Second", category = "Author", invoke = () => { } };

            HubToolRegistry.Register(first);
            Assert.AreEqual(before + 1, HubToolRegistry.All.Count);

            HubToolRegistry.Register(second);
            Assert.AreEqual(before + 1, HubToolRegistry.All.Count, "a same-id registration replaces, never duplicates");
            Assert.IsTrue(HubToolRegistry.TryGet("probe-tool", out HubTool got));
            Assert.AreEqual("Probe Second", got.label, "the later registration wins");
        }

        [Test]
        public void Register_NullOrInvokeLessTool_WarnsAndIgnores_NeverThrows()
        {
            int before = HubToolRegistry.All.Count;

            LogAssert.Expect(LogType.Warning, new Regex("HubToolRegistry: ignored a null/invalid entry"));
            Assert.DoesNotThrow(() => HubToolRegistry.Register(null));

            LogAssert.Expect(LogType.Warning, new Regex("HubToolRegistry: ignored a null/invalid entry"));
            Assert.DoesNotThrow(() => HubToolRegistry.Register(new HubTool { id = "no-invoke", label = "No Invoke", invoke = null }));

            Assert.AreEqual(before, HubToolRegistry.All.Count, "nothing invalid was actually registered");
            Assert.IsFalse(HubToolRegistry.TryGet("no-invoke", out _));
        }
    }
}
