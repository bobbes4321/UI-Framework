using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Plan 3 A: the binding manifest derives, from the spec alone, every signal / data source /
    /// setting / cheat / view the UI exposes — and the round-trip of the new first-class domain
    /// <c>signal</c> field on toggle/slider/dropdown (Plan 3 B).
    /// </summary>
    public class BindingManifestTests
    {
        // exercises: button onClick.signal, domain signals on toggle/slider/dropdown, a bound list
        // with two tokens, a settings catalog and a cheats catalog, and two views in one flow.
        private const string SpecJson = @"{
          ""views"": [
            { ""id"": ""Shop/Store"", ""elements"": [
              { ""vstack"": { ""children"": [
                { ""button"":   { ""id"": ""Shop/Buy"", ""label"": ""Buy"", ""onClick"": { ""signal"": { ""category"": ""Shop"", ""name"": ""Buy"" } } } },
                { ""toggle"":   { ""id"": ""Audio/Mute"", ""label"": ""Mute"", ""signal"": { ""category"": ""Audio"", ""name"": ""Muted"" } } },
                { ""slider"":   { ""id"": ""Audio/Volume"", ""min"": 0, ""max"": 1, ""value"": 0.8, ""signal"": { ""category"": ""Audio"", ""name"": ""MusicVolume"" } } },
                { ""dropdown"": { ""id"": ""Graphics/Quality"", ""options"": [""Low"",""High""], ""signal"": { ""category"": ""Graphics"", ""name"": ""Quality"" } } },
                { ""list"": { ""bind"": ""Shop/Deals"", ""item"": { ""vstack"": { ""children"": [
                    { ""text"": { ""label"": ""{name}"" } },
                    { ""text"": { ""label"": ""${price}"" } }
                ] } } } }
              ] } }
            ] },
            { ""id"": ""Menu/Settings"", ""elements"": [
              { ""settings"": { ""catalog"": ""Audio/Settings"" } }
            ] }
          ],
          ""settings"": [
            { ""id"": ""Audio/Settings"", ""items"": [
              { ""slider"":   { ""id"": ""Audio/MusicVolume"", ""min"": 0, ""max"": 1, ""value"": 0.8 } },
              { ""toggle"":   { ""id"": ""Audio/Subtitles"", ""value"": true } },
              { ""dropdown"": { ""id"": ""Graphics/Preset"", ""options"": [""Low"",""High""], ""value"": 1 } }
            ] }
          ],
          ""cheats"": [
            { ""id"": ""Cheats/Dev"", ""items"": [
              { ""toggle"": { ""id"": ""Player/GodMode"" } },
              { ""button"": { ""id"": ""World/Reset"" } }
            ] }
          ],
          ""flow"": { ""name"": ""Shop"", ""start"": ""Store"", ""nodes"": [ { ""name"": ""Store"", ""view"": ""Shop/Store"" } ] }
        }";

        private static BindingManifest Derive() => BindingManifest.Derive(UISpec.FromJson(SpecJson));

        [Test]
        public void Manifest_ListsEveryView()
        {
            BindingManifest manifest = Derive();
            Assert.AreEqual("Shop", manifest.flowName);
            CollectionAssert.AreEquivalent(
                new[] { "Shop/Store", "Menu/Settings" },
                manifest.views.Select(v => v.id).ToArray());
        }

        [Test]
        public void Manifest_DomainSignals_HaveTypedPayloads()
        {
            BindingManifest manifest = Derive();
            var domain = manifest.signals.Where(s => s.domain)
                .ToDictionary(s => $"{s.category}/{s.name}", s => s.payload);

            Assert.AreEqual("none", domain["Shop/Buy"], "button onClick.signal carries no payload");
            Assert.AreEqual("bool", domain["Audio/Muted"], "toggle domain signal is bool");
            Assert.AreEqual("float", domain["Audio/MusicVolume"], "slider domain signal is float");
            Assert.AreEqual("int", domain["Graphics/Quality"], "dropdown domain signal is int");
            Assert.AreEqual(4, domain.Count, "exactly the four authored domain signals");
        }

        [Test]
        public void Manifest_ListsStandardStreams_Deduped()
        {
            BindingManifest manifest = Derive();
            var standard = manifest.signals.Where(s => !s.domain)
                .Select(s => $"{s.category}/{s.name}").ToArray();
            // one entry per distinct widget-kind stream that appears in the spec
            CollectionAssert.AreEquivalent(
                new[] { "UIButton/Behaviour", "UIToggle/Behaviour", "UISlider/Behaviour", "UIDropdown/Behaviour" },
                standard);
        }

        [Test]
        public void Manifest_DataSource_CarriesItsTokens()
        {
            BindingManifest manifest = Derive();
            Assert.AreEqual(1, manifest.dataSources.Count);
            BindingManifest.DataSourceBinding deals = manifest.dataSources[0];
            Assert.AreEqual("Shop/Deals", deals.id);
            CollectionAssert.AreEqual(new[] { "name", "price" }, deals.tokens, "distinct {key} tokens, in template order");
        }

        [Test]
        public void Manifest_Settings_And_Cheats_TypePerKind()
        {
            BindingManifest manifest = Derive();

            var settings = manifest.settings.ToDictionary(s => $"{s.category}/{s.name}");
            Assert.AreEqual("float", settings["Audio/MusicVolume"].type);
            Assert.AreEqual("bool", settings["Audio/Subtitles"].type);
            Assert.AreEqual("int", settings["Graphics/Preset"].type);

            var cheats = manifest.cheats.ToDictionary(s => $"{s.category}/{s.name}");
            Assert.AreEqual("bool", cheats["Player/GodMode"].type);
            Assert.AreEqual("none", cheats["World/Reset"].type, "a cheat button carries no plain value");
        }

        // ------------------------------------------------------------------ round-trip of the `signal` field

        private const string DomainSpecJson = @"{
          ""views"": [ { ""id"": ""Audio/Panel"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
              { ""toggle"":   { ""id"": ""Audio/Mute"",   ""label"": ""Mute"",   ""signal"": { ""category"": ""Audio"", ""name"": ""Muted"" } } },
              { ""slider"":   { ""id"": ""Audio/Volume"", ""min"": 0, ""max"": 1, ""value"": 0.5, ""signal"": { ""category"": ""Audio"", ""name"": ""MusicVolume"" } } },
              { ""dropdown"": { ""id"": ""Graphics/Quality"", ""options"": [""Low"",""High""], ""signal"": { ""category"": ""Graphics"", ""name"": ""Quality"" } } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void DomainSignal_RoundTripsThroughExport()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(DomainSpecJson));
            Assert.IsEmpty(report.collisions, report.ToString());

            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Audio/Panel");
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");

            ElementSpec toggle = stack.children.First(e => e.id == "Audio/Mute");
            Assert.IsNotNull(toggle.signal, "toggle domain signal must round-trip");
            Assert.AreEqual("Audio", toggle.signal.category);
            Assert.AreEqual("Muted", toggle.signal.name);

            ElementSpec slider = stack.children.First(e => e.id == "Audio/Volume");
            Assert.AreEqual("MusicVolume", slider.signal?.name);

            ElementSpec dropdown = stack.children.First(e => e.id == "Graphics/Quality");
            Assert.AreEqual("Quality", dropdown.signal?.name);
        }

        [Test]
        public void DomainSignal_IsFixedPoint_ThroughExportGenerateExport()
        {
            UISpecGenerator.Generate(UISpec.FromJson(DomainSpecJson));
            string first = UISpecExporter.ExportProject().ToJson();
            UISpecGenerator.Generate(UISpec.FromJson(first));
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second, "the `signal` field must round-trip byte-identically");
        }
    }
}
