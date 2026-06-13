#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AlterEyes.UI;
using AlterEyes.UI.Menus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// Plays the settings-cheats demo the way a user does: real generated prefabs instanced under a
    /// canvas, the generated flow graph driving navigation, real clicks on buttons and tabs. This is
    /// the coverage class the menu bugs shipped through — the EditMode playthrough asserts at the flow
    /// level and the synthetic tab tests build panels ACTIVE in code, so nothing ever exercised a
    /// baked-INACTIVE panel (whose in-memory visibilityState lies as Visible) being shown at runtime.
    ///
    /// PlayMode test assemblies must NOT be editor-only (the platform decides Edit vs Play
    /// classification), so the generator — editor assembly — is reached via reflection under
    /// UNITY_EDITOR instead of an assembly reference.
    /// </summary>
    public class SettingsCheatsDemoPlayTest : PlayModeTestBase
    {
        private const string GeneratedRoot = "Assets/AEUI Generated"; // UISpecGenerator.GeneratedRoot

        private FlowController _controller;

        private static object InvokeGenerator(string methodName, params object[] args)
        {
            System.Type type = System.Type.GetType("AlterEyes.UI.Editor.UISpecGenerator, AlterEyes.UI.Editor");
            Assert.IsNotNull(type, "AlterEyes.UI.Editor.UISpecGenerator not found via reflection");
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, $"UISpecGenerator.{methodName} not found");
            return method.Invoke(null, args);
        }

        [OneTimeSetUp]
        public void GenerateDemo()
        {
            string specPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".", "settings-cheats-demo.json");
            Assert.IsTrue(File.Exists(specPath), $"demo spec missing at {specPath}");

            object report = InvokeGenerator("GenerateFromSpecFile", specPath);
            var issues = (List<string>)report.GetType().GetField("issues").GetValue(report);
            var collisions = (List<string>)report.GetType().GetField("collisions").GetValue(report);
            Assert.IsEmpty(collisions, string.Join("\n", collisions));
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [OneTimeTearDown]
        public void DeleteGenerated()
        {
            AssetDatabase.DeleteAsset(GeneratedRoot);
            AEUISettings settings = AEUISettings.instance;
            if (settings != null && settings.animationPresets != null)
            {
                foreach (UIAnimationPreset preset in settings.animationPresets.Presets.Where(p => p == null).ToList())
                    settings.animationPresets.Remove(preset);
                EditorUtility.SetDirty(settings.animationPresets);
            }
            AssetDatabase.SaveAssets();
        }

        public override void SetUp()
        {
            base.SetUp();
            Signals.ClearAll();
            UserSettingsService.ClearAll();
            UserSettingsService.Store = new InMemoryUserSettingsStore();

            // mirror GeneratedSceneBuilder: every generated view instanced (hidden — the flow shows them)
            string viewFolder = $"{GeneratedRoot}/Views";
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { viewFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<UIView>() == null) continue;
                GameObject instance = Object.Instantiate(prefab, canvas.transform);
                instance.name = prefab.name;
            }

            FlowGraph graph = AssetDatabase.FindAssets("t:FlowGraph", new[] { $"{GeneratedRoot}/Flow" })
                .Select(g => AssetDatabase.LoadAssetAtPath<FlowGraph>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(g => g != null);
            Assert.IsNotNull(graph, "generated flow graph missing");

            GameObject controllerGo = Track(new GameObject("FlowController"));
            _controller = controllerGo.AddComponent<FlowController>();
            _controller.flow = graph;
        }

        public override void TearDown()
        {
            if (_controller != null) _controller.StopFlow();
            base.TearDown();
            UserSettingsService.ClearAll();
            Signals.ClearAll();
        }

        private static UIView View(string category, string name)
        {
            UIView view = UIView.allViews.FirstOrDefault(v => v.id.Matches(category, name));
            Assert.IsNotNull(view, $"view '{category}/{name}' not found in the live registry");
            return view;
        }

        private static UIPanel Panel(UIView view, string category, string name)
        {
            UIPanel panel = view.GetComponentsInChildren<UIPanel>(true)
                .FirstOrDefault(p => p.id.Matches(category, name));
            Assert.IsNotNull(panel, $"panel '{category}/{name}' not found under view '{view.id}'");
            return panel;
        }

        private static void ClickTab(string category, string name)
        {
            UIToggle tab = UIToggle.allToggles.FirstOrDefault(t => t.id.Matches(category, name));
            Assert.IsNotNull(tab, $"tab '{category}/{name}' not found in the live registry");
            tab.Toggle(); // the pointer-click path
        }

        [UnityTest]
        public IEnumerator SettingsMenu_TabClicks_ShowTheirPanels()
        {
            yield return null; // Start(): views run their start behaviour, the flow shows Menu/Main

            UIView main = View("Menu", "Main");
            UIView settings = View("Menu", "Settings");
            UIView cheats = View("Menu", "Cheats");
            yield return WaitUntil(() => main.isVisible, 5f, "main menu to show at start");

            Assert.IsFalse(settings.isVisible, "settings view must start hidden");

            // visual truth, not just state: a "hidden" view at alpha 1 still covers the screen
            // (the start-on-settings bug — animation-less views never drove their alpha down)
            Assert.That(main.canvasGroup.alpha, Is.EqualTo(1f).Within(0.01f), "main view must RENDER at start");
            Assert.That(settings.canvasGroup.alpha, Is.EqualTo(0f).Within(0.01f),
                "a hidden settings view must be invisible, not just flagged Hidden");
            Assert.That(cheats.canvasGroup.alpha, Is.EqualTo(0f).Within(0.01f),
                "a hidden cheats view must be invisible, not just flagged Hidden");

            // ---- navigate to settings like a user
            UIButton open = UIButton.GetFirstButton("Menu", "OpenSettings");
            Assert.IsNotNull(open, "Menu/OpenSettings button missing");
            open.Click();
            yield return WaitUntil(() => settings.isVisible, 5f, "settings view to show");
            Assert.That(settings.canvasGroup.alpha, Is.EqualTo(1f).Within(0.01f),
                "the shown settings view must render at full alpha");

            UIPanel audio = Panel(settings, "Settings", "Audio");
            UIPanel video = Panel(settings, "Settings", "Video");
            UIPanel controls = Panel(settings, "Settings", "Controls");

            Assert.IsTrue(audio.gameObject.activeSelf, "start group (Audio) panel must be visible on arrival");
            Assert.IsTrue(audio.GetComponentsInChildren<UISlider>(true).Length >= 2,
                "audio panel should contain its slider rows");

            // ---- the user's exact repro: click another tab
            ClickTab("Settings", "Video");
            yield return null;
            Assert.IsTrue(video.gameObject.activeSelf,
                "clicking the Video tab must ACTIVATE the baked-inactive Video panel");
            Assert.IsFalse(audio.gameObject.activeSelf, "and hide the Audio panel");
            Assert.IsNotNull(video.GetComponentInChildren<UIDropdown>(true),
                "video panel should contain the quality dropdown row");

            ClickTab("Settings", "Controls");
            yield return null;
            Assert.IsTrue(controls.gameObject.activeSelf,
                "clicking the Controls tab must activate the Controls panel");
            Assert.IsFalse(video.gameObject.activeSelf);
            Assert.IsTrue(controls.GetComponentsInChildren<UIRebindControl>(true).Length >= 2,
                "controls panel should contain the rebind rows");

            // ---- and back to the first tab
            ClickTab("Settings", "Audio");
            yield return null;
            Assert.IsTrue(audio.gameObject.activeSelf, "re-selecting Audio must bring its panel back");
            Assert.IsFalse(controls.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator CheatsMenu_TabClicksAndControls_Work()
        {
            yield return null;
            UIView main = View("Menu", "Main");
            yield return WaitUntil(() => main.isVisible, 5f, "main menu to show at start");

            UIButton open = UIButton.GetFirstButton("Menu", "OpenCheats");
            Assert.IsNotNull(open, "Menu/OpenCheats button missing");
            open.Click();

            UIView cheats = View("Menu", "Cheats");
            yield return WaitUntil(() => cheats.isVisible, 5f, "cheats view to show");

            UIPanel player = Panel(cheats, "Cheats", "Player");
            UIPanel world = Panel(cheats, "Cheats", "World");
            Assert.IsTrue(player.gameObject.activeSelf, "start group (Player) panel must be visible");

            // a cheat button click fires the Cheat signal
            bool fired = false;
            Signals.On(UserSettingsService.CheatCategory, "Player/GiveGold", () => fired = true);
            UIButton give = UIButton.GetFirstButton("Player", "GiveGold");
            Assert.IsNotNull(give, "Player/GiveGold cheat button missing");
            give.Click();
            Assert.IsTrue(fired, "cheat button must fire its Cheat signal");

            ClickTab("Cheats", "World");
            yield return null;
            Assert.IsTrue(world.gameObject.activeSelf, "clicking the World tab must activate its panel");
            Assert.IsFalse(player.gameObject.activeSelf);
        }
    }
}
#endif
