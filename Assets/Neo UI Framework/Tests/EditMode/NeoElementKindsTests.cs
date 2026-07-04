using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The keystone extensibility seam: <see cref="NeoElementKinds"/>. Phase 1 is seam-first — built-ins
    /// stay on their proven generator/exporter path (so the registry is empty by default and the existing
    /// round-trip/golden suites can't regress), and only a project's NOVEL kind flows through the registry.
    /// These tests register a throwaway "probe" kind and prove it reaches every site: generation +
    /// export round-trip, the binding manifest (signal + data source), a host inspector's accent, and
    /// that an unknown kind still WARNS (no silent failure).
    /// </summary>
    public class NeoElementKindsTests
    {
        /// <summary> A throwaway project-defined kind, registered only for the duration of a test. </summary>
        private sealed class ProbeKind : INeoElementKind
        {
            private readonly string _payload;
            public ProbeKind(string payload = "none") { _payload = payload; }

            public string Kind => "probe";
            public Color Accent => new Color(0.12f, 0.34f, 0.56f, 1f);
            public string SignalPayload => _payload;

            public IEnumerable<SpecField> Fields => new[]
            {
                new SpecField("label", "Label", FieldKind.Text,
                    e => e.label, (e, v) => e.label = (string)v, new[] { "probe" })
            };

            // The test asmdef is Editor-only, so it can't define a runtime-addable MonoBehaviour marker.
            // Instead the probe stamps a sentinel CHILD GameObject whose name encodes the round-tripped
            // data ("__probe__|label|signalCat|signalName|bind") — that survives SaveAsPrefabAsset + reload
            // with no component, and a real project would simply key TryExport off its own marker component.
            private const string Sentinel = "__probe__";

            public GameObject Build(ElementBuildContext ctx)
            {
                var go = new GameObject("Probe", typeof(RectTransform));
                go.transform.SetParent(ctx.parent, false);
                string sigCat = ctx.element.signal?.category ?? "";
                string sigName = ctx.element.signal?.name ?? "";
                var tag = new GameObject($"{Sentinel}|{ctx.element.label}|{sigCat}|{sigName}|{ctx.element.bind}",
                    typeof(RectTransform));
                tag.transform.SetParent(go.transform, false);
                return go;
            }

            public bool TryExport(GameObject go, out ElementSpec spec)
            {
                spec = null;
                Transform tag = null;
                foreach (Transform child in go.transform)
                    if (child.name.StartsWith(Sentinel + "|")) { tag = child; break; }
                if (tag == null) return false;

                string[] parts = tag.name.Split('|');
                spec = new ElementSpec { kind = "probe", label = parts.Length > 1 ? parts[1] : null,
                    bind = parts.Length > 4 ? parts[4] : null };
                if (parts.Length > 3 && !string.IsNullOrEmpty(parts[2]))
                    spec.signal = new SignalRefSpec { category = parts[2], name = parts[3] };
                return true;
            }
        }

        /// <summary> A throwaway project-defined CONTAINER kind (Task 7.3 / audit E4) — proves
        /// <c>UISpecGenerator.IsPlainContainer</c> consults the registry's <see cref="IElementKindContainer"/>
        /// fact instead of only its sealed built-in string chain, so a registered container kind's
        /// "background" field gets the same card-decor <see cref="NeoShape"/> treatment as vstack/hstack/etc. </summary>
        private sealed class ProbeContainerKind : INeoElementKind, IElementKindContainer
        {
            public string Kind => "probeContainer";
            public Color Accent => new Color(0.56f, 0.34f, 0.12f, 1f);
            public string SignalPayload => "none";
            public bool AcceptsChildren => true;

            public IEnumerable<SpecField> Fields => System.Array.Empty<SpecField>();

            public GameObject Build(ElementBuildContext ctx)
            {
                var go = new GameObject("ProbeContainer", typeof(RectTransform));
                go.transform.SetParent(ctx.parent, false);
                return go;
            }

            public bool TryExport(GameObject go, out ElementSpec spec)
            {
                spec = null;
                return false;
            }
        }

        [TearDown]
        public void ClearRegistry()
        {
            NeoElementKinds.ResetForTests();
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
        }

        // ------------------------------------------------------------------ container decor seam (E4 / Task 7.3)

        [Test]
        public void RegisteredContainerKind_WithBackground_ReceivesCardDecor()
        {
            NeoElementKinds.Register(new ProbeContainerKind());
            const string json = @"{ ""views"": [ { ""id"": ""Probe/View"", ""elements"": [
                { ""probeContainer"": { ""id"": ""Probe/Card"", ""background"": ""Surface"" } }
            ] } ] }";

            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            CollectionAssert.IsEmpty(report.issues, report.ToString());

            var view = AssetDatabase.LoadAssetAtPath<GameObject>(UISpecGenerator.ViewPrefabPath("Probe", "View"));
            Assert.IsNotNull(view, "generated view prefab must exist");
            Transform card = view.transform.Find("Probe_Card");
            Assert.IsNotNull(card, "the registered container element must be built under the view");
            Assert.IsNotNull(card.GetComponent<NeoShape>(),
                "a registered container kind with 'background' must receive card decor (an NeoShape), same as a built-in container");
        }

        // ------------------------------------------------------------------ registry semantics

        [Test]
        public void All_IsEmptyByDefault()
        {
            Assert.IsEmpty(NeoElementKinds.All, "built-ins stay in the switch — the registry is empty until a project registers");
        }

        [Test]
        public void Register_AddsThenReplacesByKind()
        {
            var first = new ProbeKind("none");
            NeoElementKinds.Register(first);
            Assert.AreEqual(1, NeoElementKinds.All.Count);
            Assert.IsTrue(NeoElementKinds.TryGet("probe", out INeoElementKind got1));
            Assert.AreSame(first, got1);

            var second = new ProbeKind("bool");
            NeoElementKinds.Register(second);
            Assert.AreEqual(1, NeoElementKinds.All.Count, "same Kind replaces, never duplicates");
            Assert.IsTrue(NeoElementKinds.TryGet("probe", out INeoElementKind got2));
            Assert.AreSame(second, got2);
        }

        [Test]
        public void TryGet_MissingKind_ReturnsFalse()
        {
            Assert.IsFalse(NeoElementKinds.TryGet("nope", out INeoElementKind k));
            Assert.IsNull(k);
        }

        [Test]
        public void Register_NullOrEmptyKind_WarnsAndIgnores_NeverThrows()
        {
            LogAssert.Expect(LogType.Warning, new Regex("NeoElementKinds: ignored a null/invalid entry"));
            Assert.DoesNotThrow(() => NeoElementKinds.Register(null));

            Assert.AreEqual(0, NeoElementKinds.All.Count, "nothing was actually registered");
        }

        [Test]
        public void KnownKinds_UnionsRegistry_WithoutMutatingBuiltins()
        {
            CollectionAssert.DoesNotContain(ElementSpec.Kinds, "probe");
            CollectionAssert.DoesNotContain(ElementSpec.KnownKinds.ToArray(), "probe");

            NeoElementKinds.Register(new ProbeKind());
            CollectionAssert.Contains(ElementSpec.KnownKinds.ToArray(), "probe");
            CollectionAssert.DoesNotContain(ElementSpec.Kinds, "probe",
                "the built-in Kinds array must stay the pure built-in list");
        }

        // ------------------------------------------------------------------ parse + generate + export round-trip

        private const string ProbeSpecJson = @"{
          ""views"": [ { ""id"": ""Probe/View"", ""elements"": [
            { ""probe"": { ""id"": ""Probe/One"", ""label"": ""Hello Probe"" } }
          ] } ]
        }";

        [Test]
        public void RegisteredKind_Parses_Generates_AndExportsThroughProvider()
        {
            NeoElementKinds.Register(new ProbeKind());

            // parses (the parser iterates KnownKinds, so a novel kind is recognized in JSON)
            UISpec spec = UISpec.FromJson(ProbeSpecJson);
            ElementSpec parsed = spec.views[0].elements[0];
            Assert.AreEqual("probe", parsed.kind);
            Assert.AreEqual("Hello Probe", parsed.label);

            // generates through the provider (no "Unknown kind" issue)
            GenerateReport report = UISpecGenerator.Generate(spec);
            CollectionAssert.IsEmpty(report.issues, report.ToString());

            // exports back through the provider's TryExport
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Probe/View");
            Assert.IsNotNull(view, "probe view must export");
            ElementSpec probe = view.elements.FirstOrDefault(e => e.kind == "probe");
            Assert.IsNotNull(probe, "the registered kind must round-trip through its own provider");
            Assert.AreEqual("Hello Probe", probe.label);
        }

        // ------------------------------------------------------------------ binding manifest invariant

        [Test]
        public void RegisteredKind_WithSignalPayload_SurfacesDomainSignal()
        {
            NeoElementKinds.Register(new ProbeKind("bool"));
            const string json = @"{ ""views"": [ { ""id"": ""Probe/View"", ""elements"": [
                { ""probe"": { ""id"": ""Probe/Sig"", ""signal"": { ""category"": ""Game"", ""name"": ""Probed"" } } }
            ] } ] }";

            BindingManifest manifest = BindingManifest.Derive(UISpec.FromJson(json));
            BindingManifest.SignalBinding sig = manifest.signals.FirstOrDefault(s => s.domain && s.name == "Probed");
            Assert.IsNotNull(sig, "a registered kind that publishes a signal MUST appear in the manifest (binding-guide invariant)");
            Assert.AreEqual("Game", sig.category);
            Assert.AreEqual("bool", sig.payload, "payload comes from INeoElementKind.SignalPayload");
        }

        [Test]
        public void RegisteredKind_WithBind_SurfacesDataSource()
        {
            NeoElementKinds.Register(new ProbeKind());
            const string json = @"{ ""views"": [ { ""id"": ""Probe/View"", ""elements"": [
                { ""probe"": { ""id"": ""Probe/Bound"", ""bind"": ""Shop/Deals"" } }
            ] } ] }";

            BindingManifest manifest = BindingManifest.Derive(UISpec.FromJson(json));
            BindingManifest.DataSourceBinding ds = manifest.dataSources.FirstOrDefault(d => d.id == "Shop/Deals");
            Assert.IsNotNull(ds, "a registered kind that binds data MUST appear in dataSources");
        }

        // ------------------------------------------------------------------ no silent failure

        [Test]
        public void UnknownKind_StillWarns()
        {
            // a kind that is neither built-in nor registered must surface a report issue, never no-op
            var view = new ViewSpec { category = "Probe", viewName = "Unknown" };
            view.elements.Add(new ElementSpec { kind = "definitelyNotARealKind" });
            var spec = new UISpec();
            spec.views.Add(view);

            GenerateReport report = UISpecGenerator.Generate(spec);
            Assert.IsTrue(report.issues.Any(i => i.Contains("definitelyNotARealKind")),
                "unknown element kind must report an issue (no silent failure)");
        }
    }
}
