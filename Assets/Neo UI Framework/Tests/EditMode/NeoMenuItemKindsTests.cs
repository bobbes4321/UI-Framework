using System;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI.Editor;
using Neo.UI.Menus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Wave 7 Task 7.1: <see cref="NeoMenuItemKinds"/> is the registry that replaced the generator's
    /// <c>MapKind</c>/<c>BuildMenuRow</c> switches, the exporter's <c>UnmapKind</c> switch, and the
    /// sanctioned Phase-2 TODO at the old <c>BindingManifest.TypeForKind</c> (audit E3 — the largest
    /// fully-sealed subsystem the architecture audit found).
    /// <para>
    /// Covers the 8 built-ins staying behaviorally identical to the pre-refactor switches (the full
    /// generate/export pipeline against them is exercised by <c>SettingsAndCheatsSpecTests</c> and
    /// <c>BindingManifestTests</c>, which this task must keep green) plus the extension seam itself. The
    /// seam has a documented runtime boundary: a project-registered kind with no <see cref="MenuControlKind"/>
    /// slot parses, exports, round-trips at the SPEC level, and shows up in <see cref="MenuItemSpec.Kinds"/>
    /// (so <c>MenuCatalogInspector</c>'s kind popup picks it up) — but its generated row degrades to a
    /// non-interactive Label because <see cref="MenuItemDefinition.kind"/> is a closed runtime enum with
    /// nowhere else to carry a novel kind's identity. See <see cref="NeoMenuItemKinds.MapKind"/>'s doc and
    /// the CLAUDE.md "Settings/cheats menu items" bullet for the Runtime/ change that would close this gap.
    /// </para>
    /// </summary>
    public class NeoMenuItemKindsTests
    {
        private const string FakeKind = "radialDial";

        [TearDown]
        public void Reset()
        {
            NeoMenuItemKinds.ResetForTests();
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
        }

        [Test]
        public void All_ContainsTheEightBuiltins_InMenuControlKindDeclarationOrder()
        {
            var expected = new[] { "label", "button", "toggle", "switch", "slider", "stepper", "dropdown", "rebind" };
            CollectionAssert.AreEqual(expected, NeoMenuItemKinds.All.Select(d => d.id).ToArray());
            Assert.AreEqual(expected.Length, Enum.GetValues(typeof(MenuControlKind)).Length,
                "the built-in registration order must still line up with MenuControlKind 1:1 " +
                "(MenuCatalogInspector writes the enum by positional index into MenuItemSpec.Kinds)");
        }

        [Test]
        public void MenuItemSpecKinds_IsRegistryDerived_MatchesAllByDefault()
        {
            CollectionAssert.AreEqual(NeoMenuItemKinds.All.Select(d => d.id).ToArray(), MenuItemSpec.Kinds);
        }

        [TestCase("toggle", MenuControlKind.Toggle)]
        [TestCase("switch", MenuControlKind.Switch)]
        [TestCase("slider", MenuControlKind.Slider)]
        [TestCase("stepper", MenuControlKind.Stepper)]
        [TestCase("dropdown", MenuControlKind.Dropdown)]
        [TestCase("button", MenuControlKind.Button)]
        [TestCase("label", MenuControlKind.Label)]
        [TestCase("rebind", MenuControlKind.KeyRebind)]
        public void MapKind_And_UnmapKind_AreInverses_ForEveryBuiltin(string kind, MenuControlKind expected)
        {
            Assert.AreEqual(expected, NeoMenuItemKinds.MapKind(kind), "MapKind must match the old switch");
            Assert.AreEqual(kind, NeoMenuItemKinds.UnmapKind(expected), "UnmapKind must match the old switch");
        }

        [TestCase("toggle", "bool")]
        [TestCase("switch", "bool")]
        [TestCase("slider", "float")]
        [TestCase("stepper", "float")]
        [TestCase("dropdown", "int")]
        [TestCase("button", "none")]
        [TestCase("label", "none")]
        [TestCase("rebind", "none")]
        public void TypeForKind_MatchesBindingManifestContract(string kind, string expectedType)
        {
            Assert.AreEqual(expectedType, NeoMenuItemKinds.TypeForKind(kind));
            Assert.AreEqual(expectedType, BindingManifest.TypeForKind(kind),
                "BindingManifest.TypeForKind must ride the same registry (the Phase-2 TODO this task resolves)");
        }

        [Test]
        public void ValueToTyped_ConvertsPerKind_MatchingTheOldSwitch()
        {
            Assert.AreEqual(true, NeoMenuItemKinds.ValueToTyped("toggle", "True"));
            Assert.AreEqual(false, NeoMenuItemKinds.ValueToTyped("switch", "False"));
            Assert.AreEqual(0.8, NeoMenuItemKinds.ValueToTyped("slider", "0.8"));
            Assert.AreEqual(2.0, NeoMenuItemKinds.ValueToTyped("dropdown", "2"));
            Assert.IsNull(NeoMenuItemKinds.ValueToTyped("button", "whatever"), "value-less kinds carry no typed value");
            Assert.IsNull(NeoMenuItemKinds.ValueToTyped("toggle", null));
            Assert.IsNull(NeoMenuItemKinds.ValueToTyped("toggle", ""));
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(NeoMenuItemKinds.TryGet("not-a-real-kind", out _));
        }

        [Test]
        public void MapKind_UnknownKind_WarnsAndFallsBackToLabel_NeverSilent()
        {
            LogAssert.Expect(LogType.Warning, new Regex("NeoMenuItemKinds.*no runtime MenuControlKind mapping"));
            Assert.AreEqual(MenuControlKind.Label, NeoMenuItemKinds.MapKind("not-a-real-kind"));
        }

        [Test]
        public void Register_NovelKind_AppendsThenReplacesByIdInPlace()
        {
            int before = NeoMenuItemKinds.All.Count;

            NeoMenuItemKinds.Register(new MenuItemKindDescriptor(FakeKind, null, "bool", ToBool, NoOpBuildRow));
            Assert.AreEqual(before + 1, NeoMenuItemKinds.All.Count, "a novel id appends");
            Assert.IsTrue(NeoMenuItemKinds.TryGet(FakeKind, out MenuItemKindDescriptor got));
            Assert.AreEqual("bool", got.valueType);

            NeoMenuItemKinds.Register(new MenuItemKindDescriptor(FakeKind, null, "float", null, NoOpBuildRow));
            Assert.AreEqual(before + 1, NeoMenuItemKinds.All.Count, "same id replaces, never duplicates");
            Assert.IsTrue(NeoMenuItemKinds.TryGet(FakeKind, out MenuItemKindDescriptor got2));
            Assert.AreEqual("float", got2.valueType);
        }

        [Test]
        public void ResetForTests_ClearsRegistrations_AndRestoresExactlyTheEightBuiltins()
        {
            NeoMenuItemKinds.Register(new MenuItemKindDescriptor(FakeKind, null, "bool", null, NoOpBuildRow));
            Assert.IsTrue(NeoMenuItemKinds.TryGet(FakeKind, out _));

            NeoMenuItemKinds.ResetForTests();

            Assert.IsFalse(NeoMenuItemKinds.TryGet(FakeKind, out _), "reset drops project registrations");
            Assert.AreEqual(8, NeoMenuItemKinds.All.Count, "reset re-seeds exactly the 8 built-ins");
        }

        [Test]
        public void Register_FakeKind_ParsesInACatalogSpec_AppearsInKindsList_AndRoundTripsAtTheSpecLevel()
        {
            NeoMenuItemKinds.Register(new MenuItemKindDescriptor(FakeKind, null, "bool", ToBool, NoOpBuildRow));

            string json = $@"{{
              ""settings"": [
                {{ ""id"": ""Debug/Panel"", ""items"": [
                  {{ ""{FakeKind}"": {{ ""id"":""Debug/Thing"", ""label"":""Thing"", ""value"":true }} }}
                ] }}
              ]
            }}";

            UISpec spec = UISpec.FromJson(json);
            Assert.AreEqual(1, spec.settings.Count);
            Assert.AreEqual(1, spec.settings[0].items.Count);
            Assert.AreEqual(FakeKind, spec.settings[0].items[0].kind, "the fake kind must parse");
            Assert.AreEqual("True", spec.settings[0].items[0].value);
            CollectionAssert.Contains(MenuItemSpec.Kinds, FakeKind, "the fake kind must appear in the kind list");

            // Spec-level round trip: the layer that's actually seam-complete for a kind with no
            // MenuControlKind slot (see the runtime-boundary note on NeoMenuItemKinds.MapKind) — the
            // fake kind's identity survives parse -> ToJson -> FromJson unchanged.
            UISpec reparsed = UISpec.FromJson(spec.ToJson());
            Assert.AreEqual(FakeKind, reparsed.settings[0].items[0].kind);
            Assert.AreEqual(spec.settings[0].items[0].category, reparsed.settings[0].items[0].category);
            Assert.AreEqual(spec.settings[0].items[0].name, reparsed.settings[0].items[0].name);
            Assert.AreEqual(spec.settings[0].items[0].value, reparsed.settings[0].items[0].value);
        }

        [Test]
        public void Register_FakeKind_GeneratesWithoutIssues_ButDegradesToLabel_TheDocumentedRuntimeBoundary()
        {
            NeoMenuItemKinds.Register(new MenuItemKindDescriptor(FakeKind, null, "bool", ToBool, NoOpBuildRow));

            string json = $@"{{
              ""settings"": [
                {{ ""id"": ""Debug/Panel"", ""items"": [
                  {{ ""{FakeKind}"": {{ ""id"":""Debug/Thing"", ""label"":""Thing"", ""value"":true }} }}
                ] }}
              ]
            }}";
            UISpec spec = UISpec.FromJson(json);

            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            // MapKind warns once while ToDefinition bakes the item — documented, not silent.
            LogAssert.Expect(LogType.Warning, new Regex("NeoMenuItemKinds.*no runtime MenuControlKind mapping"));
            GenerateReport report = UISpecGenerator.Generate(spec);
            Assert.IsEmpty(report.issues, string.Join("\n", report.issues));
            Assert.IsEmpty(report.collisions, string.Join("\n", report.collisions));

            var catalog = AssetDatabase.LoadAssetAtPath<SettingsCatalog>(
                $"{UISpecGenerator.GeneratedRoot}/Menus/Debug_Panel.asset");
            Assert.IsNotNull(catalog, "generate must still produce the catalog asset (no crash)");
            Assert.AreEqual(1, catalog.items.Count);
            Assert.AreEqual(MenuControlKind.Label, catalog.items[0].kind,
                "a kind with no MenuControlKind slot bakes as Label — the runtime-boundary degradation");

            // Full asset-level round trip is therefore intentionally LOSSY for a kind with no runtime
            // slot: export reads the baked Label back as "label", not the original fake kind. This is
            // the exact, concrete shape of the Task 7.1 "stop and report" finding — not a bug in this
            // test. Closing it needs a Runtime/ change (out of this task's scope).
            UISpec exported = UISpec.FromJson(UISpecExporter.ExportProject().ToJson());
            MenuCatalogSpec exportedCatalog = exported.settings.First(s => s.id == "Debug/Panel");
            Assert.AreEqual("label", exportedCatalog.items[0].kind,
                "asset round trip degrades a kind with no MenuControlKind slot to 'label'");
        }

        private static object ToBool(string value) =>
            string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || value == "1";

        private static void NoOpBuildRow(MenuCatalog catalog, MenuItemDefinition def, RectTransform parent,
            UnityEngine.InputSystem.InputActionAsset rebindAsset, NeoUISettings settings, GenerateReport report)
        {
            // Intentionally does nothing — this fake kind exists only to exercise the parse/kind-list/
            // round-trip seam, not to prove a real widget can be built for it.
        }
    }
}
