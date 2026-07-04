using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Wave 5 Task 5.1 (audit A1): four hard round-trip breaks in the spec pipeline's legacy core,
    /// all previously untested. Each was "export → generate → export is NOT a fixed point" for a
    /// different reason:
    /// 1. a button with BOTH onClick.showView and onClick.hideView lost one of the two
    ///    <see cref="ViewCommandOnClick"/> components (single GetComponent + if/else).
    /// 2. the "scroll" kind alias always exported as "list", never byte-stable for an authored
    ///    "scroll" element (now normalized to "list" at parse time instead).
    /// 3. pointerGlow's color and a text element's SDF outline color, when authored from a THEME
    ///    TOKEN, were resolved to a concrete color at bake and exported back as hex — the token
    ///    link silently died.
    /// 4. toggle/tab never applied or exported the generic `labelColor` field the parser already
    ///    accepted for every kind — authored data was silently swallowed.
    /// </summary>
    public class RoundTripBreakRegressionTests
    {
        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""RT/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""button"": { ""id"": ""RT/DualNav"", ""label"": ""Nav"",
                ""onClick"": { ""showView"": ""RT/ShowTarget"", ""hideView"": ""RT/HideTarget"" } } },
              { ""scroll"": { ""id"": ""RT/List"", ""background"": ""none"", ""spacing"": 12, ""children"": [
                { ""text"": { ""label"": ""Row"" } }
              ] } },
              { ""shape"": { ""id"": ""RT/Glow"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""SurfaceElevated"",
                ""pointerGlow"": { ""color"": ""Primary"", ""size"": 100, ""softness"": 20 } } },
              { ""text"": { ""id"": ""RT/Outlined"", ""label"": ""Outlined"", ""outlineColor"": ""Primary"", ""outlineWidth"": 0.3 } },
              { ""toggle"": { ""id"": ""RT/Agree"", ""label"": ""Agree"", ""labelColor"": ""Warning"" } },
              { ""tab"": { ""id"": ""RT/TabOne"", ""label"": ""One"", ""labelColor"": ""Warning"" } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject Generate()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/RT_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        /// <summary> Finds an element by id anywhere in the exported project (recursing into children). </summary>
        private static ElementSpec Exported(string id)
        {
            UISpec exported = UISpecExporter.ExportProject();
            foreach (ViewSpec view in exported.views)
            {
                ElementSpec found = FindById(view.elements, id);
                if (found != null) return found;
            }
            Assert.Fail($"element '{id}' not found in exported project");
            return null;
        }

        private static ElementSpec FindById(System.Collections.Generic.List<ElementSpec> elements, string id)
        {
            foreach (ElementSpec element in elements)
            {
                if (element.id == id) return element;
                ElementSpec inChildren = FindById(element.children, id);
                if (inChildren != null) return inChildren;
            }
            return null;
        }

        private static bool InLayout(GameObject widget) =>
            widget.transform.parent != null
            && ((RectTransform)widget.transform.parent).GetComponent<LayoutGroup>() != null;

        // ---- 1. dual view commands ----------------------------------------------------------

        [Test]
        public void DualViewCommands_BothRoundTrip()
        {
            Generate();
            ElementSpec button = Exported("RT/DualNav");
            Assert.AreEqual("RT/ShowTarget", button.onClickShowView, "the Show command must not be lost");
            Assert.AreEqual("RT/HideTarget", button.onClickHideView, "the Hide command must not be lost");
        }

        [Test]
        public void DualViewCommands_DuplicateSameCommand_KeepsFirstAndWarns()
        {
            GameObject prefab = Generate();
            UIButton button = prefab.GetComponentsInChildren<UIButton>(true)
                .First(b => b.id.Matches("RT", "DualNav"));

            // simulate an off-spec hand edit: a second Show command alongside the authored one
            var duplicate = button.gameObject.AddComponent<ViewCommandOnClick>();
            duplicate.command = ViewCommandOnClick.Command.Show;
            duplicate.view = new ViewId("RT", "DuplicateShow");

            LogAssert.Expect(LogType.Warning, new Regex("RT/DualNav.*Show view command"));
            ElementSpec exported = UISpecExporter.ExportElement(button.gameObject, InLayout(button.gameObject));
            Assert.AreEqual("RT/ShowTarget", exported.onClickShowView,
                "a duplicate Show command must keep the first, not silently overwrite it");
        }

        // ---- 2. "scroll" alias ----------------------------------------------------------------

        [Test]
        public void ScrollAlias_NormalizesToList()
        {
            Generate();
            ElementSpec list = Exported("RT/List");
            Assert.AreEqual("list", list.kind, "an authored 'scroll' element must export as 'list'");
        }

        // ---- 3. token loss: pointerGlow color + text outline color -----------------------------

        [Test]
        public void PointerGlowToken_RoundTrips()
        {
            GameObject prefab = Generate();
            NeoPointerReactor reactor = prefab.GetComponentsInChildren<NeoPointerReactor>(true).FirstOrDefault();
            Assert.IsNotNull(reactor, "pointerGlow must attach a NeoPointerReactor");
            Assert.AreEqual("Primary", reactor.ColorToken, "the authored token must be stamped on the reactor");

            PointerGlowSpec glow = Exported("RT/Glow").pointerGlow;
            Assert.IsNotNull(glow, "pointerGlow must export");
            Assert.AreEqual("Primary", glow.color, "a theme-token pointerGlow color must export as the token, not a resolved hex");
        }

        [Test]
        public void TextOutlineToken_RoundTrips()
        {
            GameObject prefab = Generate();
            TMP_Text outlined = prefab.GetComponentsInChildren<TMP_Text>(true)
                .First(t => t.gameObject.GetComponent<NeoElementId>()?.id == "RT/Outlined");
            ThemeColorTarget colorTarget = outlined.GetComponent<ThemeColorTarget>();
            Assert.IsNotNull(colorTarget, "an authored outline token needs a ThemeColorTarget to carry it");
            Assert.AreEqual("Primary", colorTarget.outlineToken);

            ElementSpec text = Exported("RT/Outlined");
            Assert.AreEqual("Primary", text.outlineColor, "a theme-token outline color must export as the token, not a resolved hex");
            Assert.AreEqual(0.3f, text.outlineWidth.Value, 0.01f);
        }

        // ---- 4. toggle/tab labelColor -----------------------------------------------------------

        [Test]
        public void ToggleLabelColor_RoundTrips()
        {
            Generate();
            Assert.AreEqual("Warning", Exported("RT/Agree").labelColor);
        }

        [Test]
        public void TabLabelColor_RoundTrips()
        {
            Generate();
            Assert.AreEqual("Warning", Exported("RT/TabOne").labelColor);
        }

        // ---- overall fixed point ----------------------------------------------------------------

        [Test]
        public void Export_Generate_Export_IsFixedPoint()
        {
            Generate();
            string first = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(first));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second,
                "dual view commands / scroll alias / token colors / toggle+tab labelColor must all " +
                "round-trip byte-identically");
        }

        // ---- Wave 5 Task 5.2 (audit A2): deterministic export ordering --------------------------
        //
        // Views, popups and flow graphs used to export in raw AssetDatabase.FindAssets order (unlike
        // catalogs, already sorted with an explanatory comment) — "which one exports first" was
        // scan-order dependent, a latent source of phantom baseline drift. Views/popups now sort by
        // id/name (StringComparer.Ordinal) before export, mirroring the catalog sort exactly.
        //
        // Flow graphs are the partial fix: UISpecExporter.cs used to `break` on the first FlowGraph
        // found in the shared GeneratedRoot/Flow folder, silently dropping the rest in the documented
        // multi-flow scenario (see SceneBuilderFlowScopingTests). This task verified UISpec.cs before
        // touching it, per the task brief's explicit instruction not to assume the plan's claim — and
        // found `UISpec.flow` is a single FlowSpec, NOT a list, so the model cannot hold more than one
        // flow today. Exporting every flow graph therefore needs an additive schema change
        // (spec.flow -> a list) that is out of this task's scope (UISpecExporter.cs only); that part
        // was reported back to the orchestrator rather than improvised here. What IS fixed and tested
        // below: which single graph gets exported is now deterministic (alphabetically first by name),
        // not whichever the asset scan happened to enumerate first.

        [Test]
        public void Views_ExportInSortedOrder_NotScanOrder()
        {
            const string json = @"{
              ""views"": [
                { ""id"": ""Sort/Zebra"", ""elements"": [ { ""text"": { ""label"": ""Z"" } } ] },
                { ""id"": ""Sort/Alpha"", ""elements"": [ { ""text"": { ""label"": ""A"" } } ] },
                { ""id"": ""Sort/Mike"",  ""elements"": [ { ""text"": { ""label"": ""M"" } } ] }
              ]
            }";
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.collisions, report.ToString());

            UISpec exported = UISpecExporter.ExportProject();
            var sortIds = exported.views.Select(v => v.id)
                .Where(id => id.StartsWith("Sort/", System.StringComparison.Ordinal)).ToList();
            CollectionAssert.AreEqual(
                new[] { "Sort/Alpha", "Sort/Mike", "Sort/Zebra" }, sortIds,
                "views must export sorted by id (StringComparer.Ordinal), not asset-scan order");
        }

        [Test]
        public void Popups_ExportInSortedOrder_NotScanOrder()
        {
            const string json = @"{
              ""popups"": [
                { ""name"": ""Sort/ZPopup"", ""title"": ""Z"", ""message"": ""z"" },
                { ""name"": ""Sort/APopup"", ""title"": ""A"", ""message"": ""a"" },
                { ""name"": ""Sort/MPopup"", ""title"": ""M"", ""message"": ""m"" }
              ]
            }";
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.collisions, report.ToString());

            UISpec exported = UISpecExporter.ExportProject();
            var sortNames = exported.popups.Select(p => p.name)
                .Where(name => name.StartsWith("Sort/", System.StringComparison.Ordinal)).ToList();
            CollectionAssert.AreEqual(
                new[] { "Sort/APopup", "Sort/MPopup", "Sort/ZPopup" }, sortNames,
                "popups must export sorted by name (StringComparer.Ordinal), not asset-scan order");
        }

        [Test]
        public void MultipleFlows_SelectionIsDeterministic_AndExportIsByteIdenticalAcrossCalls()
        {
            // Two flow graphs land in the shared GeneratedRoot/Flow bucket, named so creation order
            // (Second generated first) disagrees with alphabetical order (First < Second) — exactly
            // the scan-order-dependence audit A2 flagged.
            GenerateReport reportSecond = UISpecGenerator.Generate(UISpec.FromJson(@"{
              ""flow"": { ""name"": ""SortSecond"", ""start"": ""Screen"", ""nodes"": [ { ""name"": ""Screen"" } ] }
            }"));
            Assert.IsEmpty(reportSecond.collisions, reportSecond.ToString());
            GenerateReport reportFirst = UISpecGenerator.Generate(UISpec.FromJson(@"{
              ""flow"": { ""name"": ""SortFirst"", ""start"": ""Screen"", ""nodes"": [ { ""name"": ""Screen"" } ] }
            }"));
            Assert.IsEmpty(reportFirst.collisions, reportFirst.ToString());

            string first = UISpecExporter.ExportProject().ToJson();
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second, "export must be byte-identical across repeated calls");

            UISpec exported = UISpecExporter.ExportProject();
            Assert.AreEqual("SortFirst", exported.flow.name,
                "the alphabetically-first flow graph must always be selected, regardless of " +
                "generation/scan order (the model can only hold one flow — see class comment above)");
        }
    }
}
