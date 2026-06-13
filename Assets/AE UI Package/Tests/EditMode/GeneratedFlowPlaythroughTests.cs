using System.IO;
using System.Linq;
using AlterEyes.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// The "renders fine, does nothing" net at the PIPELINE level: generate the canonical demo spec,
    /// instantiate its views + flow graph, then drive the whole flow by clicking the REAL generated
    /// buttons and assert the active node advances to the right place and is wired to show the right
    /// view. Catches empty graphs, dead buttons, dead tabs and mis-wired view navigation that
    /// screenshots can't see.
    ///
    /// EditMode on purpose: button click → flow advance is synchronous static signal dispatch
    /// (UIButton.ExecuteTrigger → Signals.Send → the active node's FlowTrigger receiver → Advance),
    /// which needs no play-mode ticking. View *visibility* (UIView's registry) does need play mode
    /// — non-ExecuteAlways OnEnable doesn't run in edit mode — so we assert navigation at the flow
    /// graph level (the active node's showViews), which is exactly what dead/mis-wired navigation
    /// gets wrong. Animated visibility itself is covered by the play-mode FullStackEndToEndTest, and
    /// the baked tab→panel start state by TabPanelTests.
    /// </summary>
    public class GeneratedFlowPlaythroughTests
    {
        private GameObject _root;
        private FlowController _controller;

        [OneTimeSetUp]
        public void GenerateDemo()
        {
            string specPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".", "aeui-demo-game-ui.json");
            Assert.IsTrue(File.Exists(specPath), $"demo spec missing at {specPath}");
            GenerateReport report = UISpecGenerator.GenerateFromSpecFile(specPath);
            Assert.IsEmpty(report.collisions, report.ToString());
            Assert.IsEmpty(report.issues, report.ToString());
        }

        [OneTimeTearDown]
        public void DeleteGenerated()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AEUISettings settings = AEUISettings.instance;
            if (settings != null && settings.popupDatabase != null)
            {
                bool removed = settings.popupDatabase.Remove("QuitConfirm");
                removed |= settings.popupDatabase.Remove("PurchaseConfirm");
                if (removed) EditorUtility.SetDirty(settings.popupDatabase);
            }
            AssetDatabase.SaveAssets();
        }

        [SetUp]
        public void BuildInMemoryScene()
        {
            // instantiate every generated view so its real buttons/tabs exist to click (component
            // presence doesn't depend on OnEnable; clicking publishes the flow signal directly)
            _root = new GameObject("PlaythroughRoot", typeof(RectTransform), typeof(Canvas));
            var canvas = (RectTransform)_root.transform;
            string viewFolder = $"{UISpecGenerator.GeneratedRoot}/Views";
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { viewFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<UIView>() == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(canvas, false);
            }

            FlowGraph graph = LoadGeneratedFlow();
            Assert.IsNotNull(graph, "generated flow graph missing — the demo would ship an empty controller");

            var controllerGo = new GameObject("FlowController");
            controllerGo.transform.SetParent(_root.transform, false);
            _controller = controllerGo.AddComponent<FlowController>();
            _controller.flow = graph;
            _controller.StartFlow();
        }

        [TearDown]
        public void Teardown()
        {
            if (_controller != null) _controller.StopFlow();
            if (_root != null) Object.DestroyImmediate(_root);
            _controller = null;
            _root = null;
        }

        [Test]
        public void Flow_StartsOnMainMenu_ShowingItsView()
        {
            AssertNode("MainMenu");
            AssertShows("Menu/Main");
        }

        [Test]
        public void EveryButtonEdge_AdvancesToTheRightNodeAndView()
        {
            AssertNode("MainMenu");

            ClickButton("Menu/Play");
            AssertNode("Playing");
            AssertShows("Game/HUD");

            ClickButton("HUD/Inventory");
            AssertNode("Inventory");
            AssertShows("Game/HUD");      // inventory node layers over the HUD
            AssertShows("Game/Inventory");

            ClickButton("Inventory/Close");
            AssertNode("Playing");

            ClickButton("HUD/Pause");
            AssertNode("MainMenu");

            ClickButton("Menu/Settings");
            AssertNode("Settings");
            AssertShows("Menu/Settings");

            ClickButton("Settings/Back");
            AssertNode("MainMenu");
        }

        [Test]
        public void ShowcaseEdges_AdvanceAcrossTheNewNodes()
        {
            AssertNode("MainMenu");

            ClickButton("Menu/Garage");
            AssertNode("Shop");
            AssertShows("Shop/Store");
            ClickButton("Shop/Back");
            AssertNode("MainMenu");

            ClickButton("Menu/Options");
            AssertNode("Options");
            AssertShows("Menu/Options");
            ClickButton("Options/Back");
            AssertNode("MainMenu");

            ClickButton("Menu/Cheats");
            AssertNode("Cheats");
            AssertShows("Menu/Main");     // the cheat sheet layers over the main menu
            AssertShows("Menu/Cheats");
            ClickButton("Cheats/Close");
            AssertNode("MainMenu");

            ClickButton("Menu/Play");
            AssertNode("Playing");
            ClickButton("HUD/Finish");
            AssertNode("Victory");
            AssertShows("Race/Victory");
            ClickButton("Victory/Rematch");
            AssertNode("Playing");
            ClickButton("HUD/Finish");
            ClickButton("Victory/Continue");
            AssertNode("MainMenu");
        }

        [Test]
        public void SettingsTabs_AreWiredToPanels()
        {
            ClickButton("Menu/Settings");
            AssertNode("Settings");

            foreach (string id in new[] { "SettingsTabs/Audio", "SettingsTabs/Video", "SettingsTabs/Game" })
            {
                UITab tab = Tab(id);
                Assert.IsNotNull(tab.targetContainer, $"tab '{id}' must control a panel (dead-tab regression)");
                Assert.IsInstanceOf<UIPanel>(tab.targetContainer, $"tab '{id}' should control a UIPanel");
            }
        }

        [Test]
        public void Demo_PassesInteractivityLint()
        {
            var issues = AgentValidation.ValidateAll();
            Assert.IsFalse(issues.Any(i => i.Contains("does nothing") || i.Contains("controls nothing")),
                "the canonical demo must have no dead interactions:\n" + string.Join("\n", issues));
        }

        // ------------------------------------------------------------------ helpers

        private static FlowGraph LoadGeneratedFlow() =>
            AssetDatabase.FindAssets("t:FlowGraph", new[] { $"{UISpecGenerator.GeneratedRoot}/Flow" })
                .Select(g => AssetDatabase.LoadAssetAtPath<FlowGraph>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(g => g != null);

        private static UITab Tab(string id)
        {
            CategoryNameId.Parse(id, out string c, out string n);
            UITab tab = Object.FindObjectsByType<UITab>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.id.Matches(c, n));
            Assert.IsNotNull(tab, $"tab '{id}' not found in any generated view");
            return tab;
        }

        private void ClickButton(string id)
        {
            CategoryNameId.Parse(id, out string c, out string n);
            UIButton button = Object.FindObjectsByType<UIButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(b => b.id.Matches(c, n));
            Assert.IsNotNull(button, $"button '{id}' not found in any generated view");
            button.Click();
        }

        private void AssertNode(string expected) =>
            Assert.AreEqual(expected, _controller.activeNode?.name, "flow active node");

        private void AssertShows(string viewId)
        {
            CategoryNameId.Parse(viewId, out string c, out string n);
            var node = _controller.activeNode as UINode;
            Assert.IsNotNull(node, $"active node '{_controller.activeNode?.name}' is not a view node");
            Assert.IsTrue(node.showViews.Any(v => v.category == c && v.viewName == n),
                $"node '{node.name}' must be wired to show '{viewId}' (shows: " +
                $"{string.Join(", ", node.showViews.Select(v => $"{v.category}/{v.viewName}"))})");
        }
    }
}
