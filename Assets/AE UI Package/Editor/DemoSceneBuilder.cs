using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Builds a self-contained showcase scene at Assets/Scenes/AlterEyesUIDemo.unity:
    /// themed, animated views navigated by a flow graph, buttons with press feel, a toggle group,
    /// a slider, an animated progress bar, queued popups, a loop spinner, live theme-variant
    /// switching and Esc-key back navigation.
    /// </summary>
    public static class DemoSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/AlterEyesUIDemo.unity";
        private const string DemoFolder = "Assets/AE UI Package/Demo";

        private static Sprite UISprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        private static Sprite Knob => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        [MenuItem("Tools/AlterEyes UI/Create Demo Scene", priority = 50)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildDemoScene();
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[AlterEyes.UI] Demo scene created at {ScenePath} — press Play.");
        }

        /// <summary> Batch entry point: -executeMethod AlterEyes.UI.Editor.DemoSceneBuilder.BuildDemoScene </summary>
        public static void BuildDemoScene()
        {
            AEUISettings settings = AEUISettingsBootstrap.GetOrCreateSettings();
            SetUpTheme(settings.theme);

            if (!AssetDatabase.IsValidFolder(DemoFolder))
                AssetDatabase.CreateFolder("Assets/AE UI Package", "Demo");

            GameObject popupPrefab = BuildPopupPrefab();
            settings.popupDatabase.AddOrUpdate("Popup_Demo", popupPrefab);
            EditorUtility.SetDirty(settings.popupDatabase);

            FlowGraph graph = BuildFlowGraph();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // camera (overlay canvas doesn't need one, but an empty game view is confusing)
            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            _ = eventSystemGo;

            var inputGo = new GameObject("Back Button Input (Esc)", typeof(BackButtonInput));
            _ = inputGo;

            var loggerGo = new GameObject("Signal Logger (Gameplay/StartPainting)", typeof(SignalLogger));
            loggerGo.GetComponent<SignalLogger>().streamId = new StreamId("Gameplay", "StartPainting");

            // canvas
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var canvasRect = (RectTransform)canvasGo.transform;

            GameObject background = CreateImage(canvasRect, "Background", "Background");
            Stretch((RectTransform)background.transform);

            BuildMainView(canvasRect);
            BuildSettingsView(canvasRect);

            var controllerGo = new GameObject("Flow Controller");
            var controller = controllerGo.AddComponent<FlowController>();
            controller.flow = graph;
            controller.goBackOnBackButton = false; // back is an explicit edge in the demo graph

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ theme

        private static void SetUpTheme(Theme theme)
        {
            theme.SetToken("Background", Hex("#14213D"));
            theme.SetToken("Panel", Hex("#1D2D50"));
            theme.SetToken("Primary", Hex("#3A86FF"));
            theme.SetToken("Accent", Hex("#FCA311"));
            theme.SetToken("TextDefault", Hex("#FFFFFF"));

            theme.AddVariant("Light");
            theme.SetToken("Background", Hex("#D8DEE9"), "Light");
            theme.SetToken("Panel", Hex("#FFFFFF"), "Light");
            theme.SetToken("Primary", Hex("#2667CC"), "Light");
            theme.SetToken("Accent", Hex("#FB8500"), "Light");
            theme.SetToken("TextDefault", Hex("#14213D"), "Light");
            EditorUtility.SetDirty(theme);
        }

        private static Color Hex(string hex)
        {
            ColorUtils.TryParseHex(hex, out Color color);
            return color;
        }

        // ------------------------------------------------------------------ views

        private static void BuildMainView(RectTransform canvas)
        {
            (GameObject viewGo, RectTransform panel) = CreateView(canvas, "Menu", "Main", UIMoveDirection.Left);

            CreateTitle(panel, "AlterEyes UI Demo", 340f);
            CreateLabel(panel, "Flow graph + signals + theming + tweens", 24f, 280f, "Accent");

            GameObject play = CreateButton(panel, "Action", "Play", "Play  (sends Gameplay/StartPainting)", 180f);
            UIActionBehaviour click = play.GetComponent<UIButton>().GetOrAddBehaviour(BehaviourTrigger.Click);
            click.sendSignal = true;
            click.signalStream = new StreamId("Gameplay", "StartPainting");

            CreateButton(panel, "Action", "Settings", "Settings  ▶", 80f);

            GameObject popupButton = CreateButton(panel, "Action", "Popup", "Show Popup  (click twice → queue)", -20f);
            var showPopup = popupButton.AddComponent<ShowPopupOnClick>();
            showPopup.popupName = "Popup_Demo";
            showPopup.texts = new[]
            {
                "Demo Popup",
                "Popups queue FIFO — if you clicked twice,\nclosing this one reveals the next."
            };

            GameObject themeButton = CreateButton(panel, "Action", "Theme", "Switch Theme Variant", -120f);
            themeButton.AddComponent<ThemeVariantCycler>();

            // loop spinner (UIAnimator, infinite rotate)
            GameObject spinner = CreateImage(panel, "Spinner", "Accent", Knob);
            var spinnerRect = (RectTransform)spinner.transform;
            spinnerRect.sizeDelta = new Vector2(56f, 56f);
            spinnerRect.anchoredPosition = new Vector2(0f, -250f);
            var spinnerAnimator = spinner.AddComponent<UIAnimator>();
            spinnerAnimator.onStartBehaviour = AnimatorStartBehaviour.PlayForward;
            spinnerAnimator.animation.purpose = AnimationPurpose.Loop;
            spinnerAnimator.animation.scale.enabled = true;
            spinnerAnimator.animation.scale.settings.duration = 0.7f;
            spinnerAnimator.animation.scale.settings.ease = Ease.InOutSine;
            spinnerAnimator.animation.scale.settings.playMode = TweenPlayMode.PingPong;
            spinnerAnimator.animation.scale.settings.loops = TweenSettings.InfiniteLoops;
            spinnerAnimator.animation.scale.fromReference = ReferenceValue.CustomValue;
            spinnerAnimator.animation.scale.fromCustomValue = Vector3.one;
            spinnerAnimator.animation.scale.toReference = ReferenceValue.CustomValue;
            spinnerAnimator.animation.scale.toCustomValue = new Vector3(1.35f, 1.35f, 1f);

            CreateLabel(panel, "Esc fires the back button", 20f, -310f, "TextDefault");
            _ = viewGo;
        }

        private static void BuildSettingsView(RectTransform canvas)
        {
            (GameObject viewGo, RectTransform panel) = CreateView(canvas, "Menu", "Settings", UIMoveDirection.Right);

            CreateTitle(panel, "Settings", 340f);

            // toggle group: exactly one quality level on
            CreateLabel(panel, "Quality (toggle group, one-on enforced)", 24f, 250f, "Accent");
            var groupGo = new GameObject("QualityGroup", typeof(RectTransform));
            var groupRect = (RectTransform)groupGo.transform;
            groupRect.SetParent(panel, false);
            groupRect.anchoredPosition = new Vector2(0f, 180f);
            var group = groupGo.AddComponent<UIToggleGroup>();
            group.controlMode = UIToggleGroup.ControlMode.OneToggleOnEnforced;
            string[] levels = { "Low", "Medium", "High" };
            for (int i = 0; i < levels.Length; i++)
                CreateGroupedToggle(groupRect, group, levels[i], (i - 1) * 180f);

            // slider
            CreateLabel(panel, "Volume (UISlider)", 24f, 90f, "Accent");
            CreateSlider(panel, 30f);

            // animated progress bar
            CreateLabel(panel, "Progressor (infinite ping-pong)", 24f, -50f, "Accent");
            CreateProgressBar(panel, -110f);

            CreateLabel(panel, "Esc / back returns to the main menu", 22f, -260f, "TextDefault");
            _ = viewGo;
        }

        private static (GameObject view, RectTransform panel) CreateView(
            RectTransform canvas, string category, string name, UIMoveDirection slideFrom)
        {
            var viewGo = new GameObject($"View_{category}_{name}", typeof(RectTransform), typeof(CanvasGroup));
            var viewRect = (RectTransform)viewGo.transform;
            viewRect.SetParent(canvas, false);
            Stretch(viewRect);

            var view = viewGo.AddComponent<UIView>();
            view.id = new ViewId(category, name);
            view.onStartBehaviour = ContainerStartBehaviour.InstantHide;

            var animator = viewGo.AddComponent<UIContainerUIAnimator>();
            animator.controller = view;
            animator.showAnimation.move.enabled = true;
            animator.showAnimation.move.settings = new TweenSettings { duration = 0.45f, ease = Ease.OutCubic };
            animator.showAnimation.move.fromDirection = slideFrom;
            animator.showAnimation.move.toDirection = UIMoveDirection.CustomPosition;
            animator.showAnimation.move.toReference = ReferenceValue.StartValue;
            animator.showAnimation.fade.enabled = true;
            animator.showAnimation.fade.settings = new TweenSettings { duration = 0.45f, ease = Ease.OutCubic };
            animator.showAnimation.fade.fromCustomValue = 0f;
            animator.showAnimation.fade.toCustomValue = 1f;
            animator.hideAnimation.move.enabled = true;
            animator.hideAnimation.move.settings = new TweenSettings { duration = 0.3f, ease = Ease.InCubic };
            animator.hideAnimation.move.fromDirection = UIMoveDirection.CustomPosition;
            animator.hideAnimation.move.fromReference = ReferenceValue.StartValue;
            animator.hideAnimation.move.toDirection = slideFrom;
            animator.hideAnimation.fade.enabled = true;
            animator.hideAnimation.fade.settings = new TweenSettings { duration = 0.3f, ease = Ease.InCubic };
            animator.hideAnimation.fade.fromCustomValue = 1f;
            animator.hideAnimation.fade.toCustomValue = 0f;

            GameObject panelGo = CreateImage(viewRect, "Panel", "Panel", UISprite, Image.Type.Sliced);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.sizeDelta = new Vector2(780f, 820f);

            return (viewGo, panelRect);
        }

        // ------------------------------------------------------------------ widgets

        private static GameObject CreateButton(RectTransform parent, string category, string name, string label, float y)
        {
            GameObject go = CreateImage(parent, $"Btn_{name}", "Primary", UISprite, Image.Type.Sliced);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(560f, 80f);
            rect.anchoredPosition = new Vector2(0f, y);

            var button = go.AddComponent<UIButton>();
            button.id = new ButtonId(category, name);

            // press feel: scale dip on press, settle back on normal
            var stateAnimator = go.AddComponent<UISelectableUIAnimator>();
            stateAnimator.controller = button;
            ConfigureScaleState(stateAnimator.pressedAnimation, 0.93f, 0.08f);
            ConfigureScaleState(stateAnimator.highlightedAnimation, 1.04f, 0.12f);
            ConfigureScaleState(stateAnimator.normalAnimation, 1f, 0.15f);

            CreateLabel((RectTransform)go.transform, label, 28f, 0f, "TextDefault", stretch: true);
            return go;
        }

        private static void ConfigureScaleState(UIAnimation animation, float scale, float duration)
        {
            animation.scale.enabled = true;
            animation.scale.settings = new TweenSettings { duration = duration, ease = Ease.OutQuad };
            animation.scale.fromReference = ReferenceValue.CurrentValue;
            animation.scale.toReference = ReferenceValue.CustomValue;
            animation.scale.toCustomValue = new Vector3(scale, scale, 1f);
        }

        private static void CreateGroupedToggle(RectTransform parent, UIToggleGroup group, string label, float x)
        {
            GameObject go = CreateImage(parent, $"Toggle_{label}", null, UISprite, Image.Type.Sliced);
            go.GetComponent<Image>().color = new Color(0.35f, 0.38f, 0.45f);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(160f, 64f);
            rect.anchoredPosition = new Vector2(x, 0f);

            var toggle = go.AddComponent<UIToggle>();
            toggle.id = new ToggleId("Quality", label);
            // serialized group reference (the property is for runtime assignment)
            var serialized = new SerializedObject(toggle);
            serialized.FindProperty("groupReference").objectReferenceValue = group;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var colorAnimator = go.AddComponent<UIToggleColorAnimator>();
            colorAnimator.controller = toggle;
            colorAnimator.onColor = new ThemeColorRef("Accent");
            colorAnimator.offColor = new ThemeColorRef(new Color(0.35f, 0.38f, 0.45f));

            CreateLabel(rect, label, 24f, 0f, "TextDefault", stretch: true);
        }

        private static void CreateSlider(RectTransform parent, float y)
        {
            var go = new GameObject("Slider_Volume", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(560f, 40f);
            rect.anchoredPosition = new Vector2(0f, y);

            GameObject backgroundGo = CreateImage(rect, "Background", null, UISprite, Image.Type.Sliced);
            backgroundGo.GetComponent<Image>().color = new Color(0.2f, 0.22f, 0.3f);
            var backgroundRect = (RectTransform)backgroundGo.transform;
            Stretch(backgroundRect);
            backgroundRect.offsetMin = new Vector2(0f, 12f);
            backgroundRect.offsetMax = new Vector2(0f, -12f);

            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            var fillArea = (RectTransform)fillAreaGo.transform;
            fillArea.SetParent(rect, false);
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(12f, 12f);
            fillArea.offsetMax = new Vector2(-12f, -12f);

            GameObject fillGo = CreateImage(fillArea, "Fill", "Primary", UISprite, Image.Type.Sliced);
            var fillRect = (RectTransform)fillGo.transform;
            Stretch(fillRect);

            var handleAreaGo = new GameObject("Handle Area", typeof(RectTransform));
            var handleArea = (RectTransform)handleAreaGo.transform;
            handleArea.SetParent(rect, false);
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(16f, 0f);
            handleArea.offsetMax = new Vector2(-16f, 0f);

            GameObject handleGo = CreateImage(handleArea, "Handle", "Accent", Knob);
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.sizeDelta = new Vector2(36f, 36f);

            var slider = go.AddComponent<UISlider>();
            slider.id = new SliderId("Settings", "Volume");
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleGo.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.7f;
        }

        private static void CreateProgressBar(RectTransform parent, float y)
        {
            GameObject barGo = CreateImage(parent, "ProgressBar", null, UISprite, Image.Type.Sliced);
            barGo.GetComponent<Image>().color = new Color(0.2f, 0.22f, 0.3f);
            var barRect = (RectTransform)barGo.transform;
            barRect.sizeDelta = new Vector2(560f, 36f);
            barRect.anchoredPosition = new Vector2(0f, y);

            GameObject fillGo = CreateImage(barRect, "Fill", "Primary", UISprite, Image.Type.Filled);
            Image fillImage = fillGo.GetComponent<Image>();
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            var fillRect = (RectTransform)fillGo.transform;
            Stretch(fillRect);
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(-4f, -4f);

            GameObject labelGo = CreateLabel(barRect, "0%", 22f, 0f, "TextDefault", stretch: true);

            var progressor = barGo.AddComponent<Progressor>();
            progressor.fromValue = 0f;
            progressor.toValue = 100f;
            progressor.settings = new TweenSettings
            {
                duration = 2f,
                ease = Ease.InOutSine,
                playMode = TweenPlayMode.PingPong,
                loops = TweenSettings.InfiniteLoops
            };
            progressor.onStartBehaviour = Progressor.StartBehaviour.PlayForward;

            var imageTarget = fillGo.AddComponent<ImageProgressTarget>();
            imageTarget.image = fillImage;
            imageTarget.targetMode = ProgressTarget.Mode.Progress;
            progressor.progressTargets.Add(imageTarget);

            var textTarget = labelGo.AddComponent<TextProgressTarget>();
            textTarget.text = labelGo.GetComponent<TMP_Text>();
            textTarget.targetMode = ProgressTarget.Mode.Progress;
            textTarget.format = "{0:0}%";
            progressor.progressTargets.Add(textTarget);
        }

        // ------------------------------------------------------------------ popup prefab

        private static GameObject BuildPopupPrefab()
        {
            var root = new GameObject("Popup_Demo", typeof(RectTransform), typeof(CanvasGroup));
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);
            rootRect.sizeDelta = new Vector2(1920f, 1080f); // sensible size when previewed unparented; stretches once parented

            Image overlay = root.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.55f);

            GameObject panelGo = CreateImage(rootRect, "Panel", "Panel", UISprite, Image.Type.Sliced);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.sizeDelta = new Vector2(620f, 380f);

            GameObject title = CreateLabel(panelRect, "Title", 32f, 130f, "TextDefault");
            GameObject body = CreateLabel(panelRect, "Body", 24f, 20f, "TextDefault");
            ((RectTransform)body.transform).sizeDelta = new Vector2(540f, 140f);

            GameObject closeButton = CreateButton(panelRect, "Generic", "ClosePopup", "Close", -130f);
            ((RectTransform)closeButton.transform).sizeDelta = new Vector2(240f, 70f);
            closeButton.AddComponent<HideContainerOnClick>();

            var popup = root.AddComponent<UIPopup>();
            popup.popupName = "Popup_Demo";
            popup.content = panelRect;
            popup.hideOnClickOverlay = true;
            popup.hideOnBackButton = true;
            popup.labels.Add(title.GetComponent<TMP_Text>());
            popup.labels.Add(body.GetComponent<TMP_Text>());

            var animator = root.AddComponent<UIContainerUIAnimator>();
            animator.controller = popup;
            animator.showAnimation.scale.enabled = true;
            animator.showAnimation.scale.settings = new TweenSettings { duration = 0.25f, ease = Ease.OutBack };
            animator.showAnimation.scale.fromReference = ReferenceValue.CustomValue;
            animator.showAnimation.scale.fromCustomValue = new Vector3(0.7f, 0.7f, 1f);
            animator.showAnimation.scale.toReference = ReferenceValue.CustomValue;
            animator.showAnimation.scale.toCustomValue = Vector3.one;
            animator.showAnimation.fade.enabled = true;
            animator.showAnimation.fade.settings = new TweenSettings { duration = 0.2f, ease = Ease.OutQuad };
            animator.showAnimation.fade.fromCustomValue = 0f;
            animator.showAnimation.fade.toCustomValue = 1f;
            animator.hideAnimation.fade.enabled = true;
            animator.hideAnimation.fade.settings = new TweenSettings { duration = 0.15f, ease = Ease.InQuad };
            animator.hideAnimation.fade.fromCustomValue = 1f;
            animator.hideAnimation.fade.toCustomValue = 0f;

            string path = $"{DemoFolder}/Popup_Demo.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ flow graph

        private static FlowGraph BuildFlowGraph()
        {
            string path = $"{DemoFolder}/DemoFlow.asset";
            var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(path);
            if (graph == null)
            {
                graph = ScriptableObject.CreateInstance<FlowGraph>();
                AssetDatabase.CreateAsset(graph, path);
            }

            graph.graphName = "Demo UI";
            graph.graphDescription = "Demo navigation: MainMenu ⇄ Settings (button forward, back-button back)";
            graph.nodes.Clear();

            var start = graph.AddNode<StartNode>("Start", new Vector2(0f, 100f));
            start.outputs.Add(new FlowEdge { portName = "Start", toNode = "MainMenu", allowsBack = false });

            var mainMenu = graph.AddNode<UINode>("MainMenu", new Vector2(260f, 100f));
            mainMenu.showViews.Add(new UINode.ViewRef("Menu", "Main"));
            mainMenu.hideShownViewsOnExit = true;
            mainMenu.outputs.Add(new FlowEdge
            {
                toNode = "Settings",
                trigger = new FlowTrigger { type = FlowTrigger.TriggerType.ButtonClick, category = "Action", name = "Settings" }
            });

            var settingsNode = graph.AddNode<UINode>("Settings", new Vector2(560f, 100f));
            settingsNode.showViews.Add(new UINode.ViewRef("Menu", "Settings"));
            settingsNode.hideShownViewsOnExit = true;
            settingsNode.outputs.Add(new FlowEdge
            {
                toNode = "MainMenu",
                allowsBack = false,
                trigger = new FlowTrigger { type = FlowTrigger.TriggerType.Back }
            });

            graph.startNode = "Start";
            EditorUtility.SetDirty(graph);
            return graph;
        }

        // ------------------------------------------------------------------ primitives

        private static GameObject CreateImage(RectTransform parent, string name, string themeToken,
            Sprite sprite = null, Image.Type imageType = Image.Type.Simple)
        {
            var go = new GameObject(name, typeof(RectTransform));
            ((RectTransform)go.transform).SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = imageType;
            if (!string.IsNullOrEmpty(themeToken))
                go.AddComponent<ThemeColorTarget>().token = themeToken;
            return go;
        }

        private static void CreateTitle(RectTransform parent, string text, float y)
        {
            GameObject go = CreateLabel(parent, text, 46f, y, "TextDefault");
            go.GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;
        }

        private static GameObject CreateLabel(RectTransform parent, string text, float size, float y,
            string themeToken, bool stretch = false)
        {
            var go = new GameObject($"Label_{text}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            if (stretch)
            {
                Stretch(rect);
            }
            else
            {
                rect.sizeDelta = new Vector2(700f, size * 2f);
                rect.anchoredPosition = new Vector2(0f, y);
            }

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            if (!string.IsNullOrEmpty(themeToken))
                go.AddComponent<ThemeColorTarget>().token = themeToken;
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
