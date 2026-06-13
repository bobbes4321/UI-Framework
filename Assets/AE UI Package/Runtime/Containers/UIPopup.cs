using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI
{
    /// <summary>
    /// Popup container with database lookup by name, a fluent configure-then-Show API, FIFO queues
    /// (one visible popup per queue), automatic sorting-order management and hide-on options.
    /// <code>
    /// UIPopup.Get("Popup_FontSelection").SetTexts("Title", "Body").Show();
    /// </code>
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Containers/UI Popup")]
    public class UIPopup : UIContainer
    {
        public enum Parenting
        {
            PopupsCanvas = 0,
            UITag = 1
        }

        public const string DefaultQueueName = "Default";
        private const int BaseSortingOrder = 30000;

        [Header("Popup")]
        [Tooltip("Name this popup is addressed by in the popup database")]
        public string popupName;

        public Parenting parenting = Parenting.PopupsCanvas;
        public TagId parentTag = new TagId();

        [Header("Queue")]
        [Tooltip("Only one popup per queue is visible; the rest wait in FIFO order")]
        public bool addToQueue = true;
        public string queueName = DefaultQueueName;

        [Header("Hide Options")]
        public bool hideOnBackButton = true;
        [Tooltip("Hide when the overlay (this popup's root) is clicked")]
        public bool hideOnClickOverlay;
        [Tooltip("Hide when the content container is clicked")]
        public bool hideOnClickContainer;

        [Header("Content (optional)")]
        [Tooltip("Content root; defaults to this RectTransform")]
        public RectTransform content;
        [Tooltip("Labels SetTexts/SetText write into; discovered in children when empty")]
        public List<TMP_Text> labels = new List<TMP_Text>();
        [Tooltip("Image slots SetSprites/SetSprite write into (Image or AEShape), hierarchy order")]
        public List<Graphic> images = new List<Graphic>();
        [Tooltip("Button slots SetEvents wires onClick listeners onto, hierarchy order")]
        public List<UIButton> buttons = new List<UIButton>();

        [Tooltip("Destroy the GameObject once hidden (set automatically for popups created via Get)")]
        public bool destroyOnHidden;

        private static readonly Dictionary<string, List<UIPopup>> Queues = new Dictionary<string, List<UIPopup>>();
        private static int s_sortingOrderCounter;

        private readonly SignalReceiver _backButtonReceiver = new SignalReceiver(BackButton.StreamCategory, BackButton.StreamName);
        private bool _queued;
        private bool _hasShown;

        // ------------------------------------------------------------------ static API

        /// <summary> Instantiates the popup prefab registered under the given name. </summary>
        public static UIPopup Get(string popupName)
        {
            PopupDatabase database = AEUISettings.instance != null ? AEUISettings.instance.popupDatabase : null;
            if (database == null)
            {
                Debug.LogError("[AlterEyes.UI] No popup database configured in AEUISettings.");
                return null;
            }

            GameObject prefab = database.GetPrefab(popupName);
            if (prefab == null)
            {
                Debug.LogError($"[AlterEyes.UI] Popup '{popupName}' not found in the popup database.");
                return null;
            }

            return Get(prefab);
        }

        /// <summary> Instantiates a popup from an explicit prefab. </summary>
        public static UIPopup Get(GameObject prefab)
        {
            if (prefab == null) return null;
            var popup = Object.Instantiate(prefab).GetComponent<UIPopup>();
            if (popup == null)
            {
                Debug.LogError($"[AlterEyes.UI] Prefab '{prefab.name}' has no UIPopup component.");
                return null;
            }

            popup.destroyOnHidden = true;
            popup.transform.SetParent(popup.ResolveParent(), worldPositionStays: false);
            popup.transform.localRotation = Quaternion.identity;
            popup.gameObject.SetActive(true);
            popup.InstantHide();
            return popup;
        }

        /// <summary> Number of popups currently waiting (or showing) in a queue. </summary>
        public static int GetQueueLength(string queue = DefaultQueueName) =>
            Queues.TryGetValue(queue, out List<UIPopup> list) ? list.Count : 0;

        /// <summary> The currently visible popup of a queue (null when the queue is empty). </summary>
        public static UIPopup GetCurrentPopup(string queue = DefaultQueueName) =>
            Queues.TryGetValue(queue, out List<UIPopup> list) && list.Count > 0 ? list[0] : null;

        // ------------------------------------------------------------------ fluent API

        public UIPopup SetTexts(params string[] texts)
        {
            EnsureLabels();
            for (int i = 0; i < texts.Length && i < labels.Count; i++)
            {
                if (labels[i] != null) labels[i].text = texts[i];
            }
            return this;
        }

        public UIPopup SetText(int index, string text)
        {
            EnsureLabels();
            if (index >= 0 && index < labels.Count && labels[index] != null) labels[index].text = text;
            return this;
        }

        /// <summary> Assigns sprites to the indexed image slots (Image.sprite or AEShape.sprite). </summary>
        public UIPopup SetSprites(params Sprite[] sprites)
        {
            for (int i = 0; i < sprites.Length && i < images.Count; i++) SetSprite(i, sprites[i]);
            return this;
        }

        public UIPopup SetSprite(int index, Sprite sprite)
        {
            if (index < 0 || index >= images.Count || images[index] == null) return this;
            switch (images[index])
            {
                case Image image: image.sprite = sprite; break;
                case AEShape shape: shape.sprite = sprite; break;
            }
            return this;
        }

        /// <summary> Shows/hides one indexed image slot (slots keep their layout space). </summary>
        public UIPopup SetImageVisibility(int index, bool visible)
        {
            if (index >= 0 && index < images.Count && images[index] != null)
                images[index].enabled = visible;
            return this;
        }

        /// <summary> Adds one onClick listener per indexed button slot, in order. </summary>
        public UIPopup SetEvents(params UnityEngine.Events.UnityAction[] actions)
        {
            for (int i = 0; i < actions.Length && i < buttons.Count; i++)
            {
                if (buttons[i] != null && actions[i] != null) buttons[i].onClickEvent.AddListener(actions[i]);
            }
            return this;
        }

        public UIPopup SetQueue(string queue)
        {
            queueName = string.IsNullOrEmpty(queue) ? DefaultQueueName : queue;
            return this;
        }

        public UIPopup SkipQueue()
        {
            addToQueue = false;
            return this;
        }

        public UIPopup OnHiddenOnce(UnityEngine.Events.UnityAction callback)
        {
            OnHiddenCallback.AddListener(callback);
            return this;
        }

        private void EnsureLabels()
        {
            if (labels.Count == 0) labels.AddRange(GetComponentsInChildren<TMP_Text>(includeInactive: true));
        }

        // ------------------------------------------------------------------ lifecycle / queueing

        public override void Show()
        {
            _hasShown = true;
            if (addToQueue && Application.isPlaying)
            {
                Enqueue();
                return;
            }
            DoShow();
        }

        public override void InstantShow()
        {
            _hasShown = true;
            ApplySorting();
            base.InstantShow();
            ConnectHideTriggers();
        }

        private void Enqueue()
        {
            if (_queued) return;
            _queued = true;
            if (!Queues.TryGetValue(queueName, out List<UIPopup> queue))
            {
                queue = new List<UIPopup>();
                Queues[queueName] = queue;
            }
            queue.Add(this);
            if (queue[0] == this) DoShow();
        }

        private void DoShow()
        {
            ApplySorting();
            base.Show();
            ConnectHideTriggers();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            DisconnectHideTriggers();
            RemoveFromQueue();
        }

        protected virtual void OnDestroy()
        {
            RemoveFromQueue();
        }

        private void ConnectHideTriggers()
        {
            if (hideOnBackButton)
            {
                _backButtonReceiver.SetOnSignalCallback(_ => Hide());
                _backButtonReceiver.Connect();
            }

            if (hideOnClickOverlay) AddClickCatcher((RectTransform)transform);
            if (hideOnClickContainer && content != null) AddClickCatcher(content);
        }

        private void DisconnectHideTriggers()
        {
            _backButtonReceiver.Disconnect();
        }

        private void AddClickCatcher(RectTransform target)
        {
            if (target == null || target.GetComponent<PopupClickCatcher>() != null) return;
            if (target.GetComponent<Graphic>() == null)
            {
                Image catcherImage = target.gameObject.AddComponent<Image>();
                catcherImage.color = Color.clear;
            }
            target.gameObject.AddComponent<PopupClickCatcher>().popup = this;
        }

        private void RemoveFromQueue()
        {
            if (!_queued) return;
            _queued = false;
            if (!Queues.TryGetValue(queueName, out List<UIPopup> queue)) return;
            bool wasFront = queue.Count > 0 && queue[0] == this;
            queue.Remove(this);
            if (wasFront && queue.Count > 0) queue[0].DoShow();
            if (queue.Count == 0) Queues.Remove(queueName);
        }

        // base.Start runs the container start behaviour; popups created via Get are already hidden.

        protected override void Awake()
        {
            base.Awake();
            if (content == null) content = (RectTransform)transform;
            OnHidden += HandleHidden;
        }

        private void HandleHidden()
        {
            DisconnectHideTriggers();
            RemoveFromQueue();
            if (destroyOnHidden && _hasShown && Application.isPlaying) Destroy(gameObject);
        }

        // ------------------------------------------------------------------ parenting & sorting

        private Transform ResolveParent()
        {
            if (parenting == Parenting.UITag && !parentTag.isDefault)
            {
                UITag uiTag = UITag.GetFirstTag(parentTag.Category, parentTag.Name);
                if (uiTag != null) return uiTag.rectTransform;
            }
            return GetPopupsCanvas().transform;
        }

        private static Canvas GetPopupsCanvas()
        {
            string canvasName = AEUISettings.instance != null ? AEUISettings.instance.popupsCanvasName : "PopupsCanvas";
            GameObject existing = GameObject.Find(canvasName);
            if (existing != null)
            {
                Canvas existingCanvas = existing.GetComponent<Canvas>();
                if (existingCanvas != null) return existingCanvas;
            }

            var go = new GameObject(canvasName, typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BaseSortingOrder - 1;
            return canvas;
        }

        private void ApplySorting()
        {
            Canvas popupCanvas = GetComponent<Canvas>();
            if (popupCanvas == null) popupCanvas = gameObject.AddComponent<Canvas>();
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            popupCanvas.overrideSorting = true;
            popupCanvas.sortingOrder = BaseSortingOrder + (s_sortingOrderCounter++ % 2000);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Queues.Clear();
            s_sortingOrderCounter = 0;
        }
    }
}
