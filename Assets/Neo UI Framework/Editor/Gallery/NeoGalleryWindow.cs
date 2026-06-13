using System;
using System.Collections.Generic;
using System.IO;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A visual gallery of everything the spec generator has produced — every generated view and
    /// popup prefab, rendered with the in-editor screenshotter so you can see at a glance what the
    /// agent built without opening prefabs one at a time.
    ///
    /// Read-only by design: it never writes project assets. Thumbnails are rendered through
    /// <see cref="UIScreenshotter.Capture(GameObject,string,int,int)"/> and cached as throwaway PNGs
    /// under <c>Temp/neo-gallery</c>, so re-opening the window is cheap and the project stays clean.
    /// Click a tile to select + ping its prefab; double-click to open it; right-click for more.
    /// </summary>
    public sealed class NeoGalleryWindow : EditorWindow
    {
        private enum Filter { All, Views, Popups }

        private sealed class Item
        {
            public string assetPath;
            public string guid;
            public GameObject prefab;
            public string displayName;
            public bool isPopup;
            public Texture2D thumbnail;
            public int issues;
            public int warnings;
        }

        private const string ThumbnailCacheDir = "Temp/neo-gallery";
        private const string ResPrefKey = "Neo.UI.Gallery.ResolutionIndex";
        private const string SizePrefKey = "Neo.UI.Gallery.TileSize";
        private const int ThumbnailMaxEdge = 512; // cap rendered thumbnail resolution (keeps device aspect)

        private static readonly string[] FilterLabels = { "All", "Views", "Popups" };

        private readonly List<Item> _items = new List<Item>();
        private string[] _resolutionLabels;

        private Vector2 _gridScroll;
        private Vector2 _issueScroll;
        private string _search = "";
        private Filter _filter = Filter.All;
        private int _resolutionIndex;
        private float _tileSize = 220f;
        private bool _scanned;
        private bool _showIssues;
        private string _status;
        private List<string> _issues = new List<string>();
        private List<string> _warnings = new List<string>();

        private GUIStyle _tileName;
        private GUIStyle _placeholder;
        private GUIStyle _wrap;

        [MenuItem("Tools/Neo UI/Gallery", priority = 5)]
        public static void Open()
        {
            var window = GetWindow<NeoGalleryWindow>("Neo UI Gallery");
            window.minSize = new Vector2(420f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            _resolutionLabels = new string[UISpecPreview.DefaultResolutions.Length];
            for (int i = 0; i < _resolutionLabels.Length; i++)
            {
                (string name, int width, int height) res = UISpecPreview.DefaultResolutions[i];
                _resolutionLabels[i] = $"{res.name}  {res.width}×{res.height}";
            }

            _resolutionIndex = Mathf.Clamp(EditorPrefs.GetInt(ResPrefKey, 0), 0, _resolutionLabels.Length - 1);
            _tileSize = EditorPrefs.GetFloat(SizePrefKey, 220f);

            if (!_scanned) Scan(renderThumbnails: false);
        }

        private void OnDisable()
        {
            EditorPrefs.SetInt(ResPrefKey, _resolutionIndex);
            EditorPrefs.SetFloat(SizePrefKey, _tileSize);
            foreach (Item it in _items) DestroyThumbnail(it);
        }

        // ------------------------------------------------------------------ scanning

        private void Scan(bool renderThumbnails)
        {
            foreach (Item it in _items) DestroyThumbnail(it);
            _items.Clear();
            _scanned = true;

            string root = UISpecGenerator.GeneratedRoot;
            if (!AssetDatabase.IsValidFolder(root))
            {
                _status = $"No generated UI found at '{root}'.\nGenerate a spec first, then Refresh.";
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<GeneratedMarker>() == null) continue;

                bool isView = prefab.GetComponent<UIView>() != null;
                bool isPopup = prefab.GetComponent<UIPopup>() != null;
                if (!isView && !isPopup) continue;

                _items.Add(new Item
                {
                    assetPath = path,
                    guid = guid,
                    prefab = prefab,
                    displayName = prefab.name,
                    isPopup = isPopup && !isView, // a UIView is a view even if it also has popup-ish parts
                });
            }

            _items.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
            RefreshValidation();
            foreach (Item it in _items) LoadCachedThumbnail(it);

            int viewCount = 0, popupCount = 0;
            foreach (Item it in _items) { if (it.isPopup) popupCount++; else viewCount++; }
            _status = $"{viewCount} view(s), {popupCount} popup(s).";

            if (renderThumbnails) RenderAll(force: true);
        }

        private void RefreshValidation()
        {
            try { _issues = AgentValidation.ValidateAll(); }
            catch (Exception e) { _issues = new List<string> { $"validation failed: {e.Message}" }; }
            try { _warnings = AgentValidation.ValidateDesign(); }
            catch { _warnings = new List<string>(); }

            foreach (Item it in _items)
            {
                it.issues = CountMatches(_issues, it.displayName);
                it.warnings = CountMatches(_warnings, it.displayName);
            }
        }

        // Best-effort: validation messages quote the offending prefab as '<name>...'. Used only for
        // the per-tile badge counts; the authoritative full list lives in the issues panel.
        private static int CountMatches(List<string> messages, string name)
        {
            int count = 0;
            string needle = "'" + name;
            foreach (string m in messages)
                if (m.IndexOf(needle, StringComparison.Ordinal) >= 0) count++;
            return count;
        }

        // ------------------------------------------------------------------ thumbnails

        private string ThumbnailPath(Item it) => $"{ThumbnailCacheDir}/{it.guid}_{_resolutionIndex}.png";

        private void LoadCachedThumbnail(Item it)
        {
            string path = ThumbnailPath(it);
            if (!File.Exists(path)) return;
            // stale if the prefab changed after the cache was written
            if (File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(it.assetPath)) return;

            var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            if (tex.LoadImage(File.ReadAllBytes(path))) it.thumbnail = tex;
            else DestroyImmediate(tex);
        }

        private void RenderAll(bool force)
        {
            try
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    Item it = _items[i];
                    if (!force && it.thumbnail != null) continue;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Neo UI Gallery", $"Rendering {it.displayName}…", (float)i / Mathf.Max(1, _items.Count)))
                        break;
                    RenderThumbnail(it);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            Repaint();
        }

        private void RenderThumbnail(Item it)
        {
            (string name, int width, int height) res = UISpecPreview.DefaultResolutions[_resolutionIndex];
            ThumbnailResolution(res.width, res.height, out int tw, out int th);
            try
            {
                string path = ThumbnailPath(it);
                UIScreenshotter.Capture(it.prefab, path, tw, th);
                DestroyThumbnail(it);
                var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (tex.LoadImage(File.ReadAllBytes(path))) it.thumbnail = tex;
                else DestroyImmediate(tex);
            }
            catch (Exception e)
            {
                DestroyThumbnail(it);
                Debug.LogWarning($"[Neo.UI] Gallery couldn't render '{it.displayName}': {e.Message}");
            }
        }

        private static void ThumbnailResolution(int width, int height, out int tw, out int th)
        {
            float scale = Mathf.Min(1f, (float)ThumbnailMaxEdge / Mathf.Max(width, height));
            tw = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            th = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        }

        private static void DestroyThumbnail(Item it)
        {
            if (it.thumbnail != null)
            {
                DestroyImmediate(it.thumbnail);
                it.thumbnail = null;
            }
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();

            if (_items.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_status ?? "Nothing generated yet.", _placeholder);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Refresh", GUILayout.Width(120))) Scan(renderThumbnails: true);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.FlexibleSpace();
                return;
            }

            if (_showIssues) DrawIssuePanel();
            DrawGrid();
        }

        private void EnsureStyles()
        {
            if (_tileName != null) return;
            _tileName = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _placeholder = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 12
            };
            _wrap = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 11 };
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58)))
                    Scan(renderThumbnails: true);
                if (GUILayout.Button("Re-render", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    RenderAll(force: true);

                GUILayout.Space(8f);
                int newRes = EditorGUILayout.Popup(_resolutionIndex, _resolutionLabels,
                    EditorStyles.toolbarPopup, GUILayout.Width(150));
                if (newRes != _resolutionIndex)
                {
                    _resolutionIndex = newRes;
                    foreach (Item it in _items) { DestroyThumbnail(it); LoadCachedThumbnail(it); }
                }

                GUILayout.FlexibleSpace();

                _filter = (Filter)GUILayout.Toolbar((int)_filter, FilterLabels,
                    EditorStyles.toolbarButton, GUILayout.Width(170));

                GUILayout.Space(8f);
                GUILayout.Label("Size", GUILayout.Width(28f));
                _tileSize = GUILayout.HorizontalSlider(_tileSize, 130f, 380f, GUILayout.Width(80f));

                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(150f));

                Color badge = _issues.Count > 0 ? NeoColors.Remove
                    : _warnings.Count > 0 ? NeoColors.Warning : NeoColors.Add;
                Color previous = GUI.color;
                GUI.color = badge;
                if (GUILayout.Button($"⚠ {_issues.Count}/{_warnings.Count}",
                        EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    _showIssues = !_showIssues;
                GUI.color = previous;
            }
        }

        private void DrawIssuePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Validation — {_issues.Count} issue(s), {_warnings.Count} design warning(s)",
                        EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Re-validate", GUILayout.Width(90f))) RefreshValidation();
                    if (GUILayout.Button("✕", GUILayout.Width(22f))) _showIssues = false;
                }

                if (_issues.Count == 0 && _warnings.Count == 0)
                {
                    GUILayout.Label("All clear — no issues or design warnings.", _wrap);
                    return;
                }

                _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.MaxHeight(160f));
                Color previous = GUI.contentColor;
                GUI.contentColor = NeoColors.Remove;
                foreach (string s in _issues) GUILayout.Label("•  " + s, _wrap);
                GUI.contentColor = NeoColors.Warning;
                foreach (string s in _warnings) GUILayout.Label("•  " + s, _wrap);
                GUI.contentColor = previous;
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawGrid()
        {
            List<Item> visible = Filtered();
            const float gap = 8f;
            float tile = _tileSize;
            float available = position.width - 16f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((available + gap) / (tile + gap)));

            _gridScroll = EditorGUILayout.BeginScrollView(_gridScroll);
            int i = 0;
            while (i < visible.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns && i < visible.Count; c++, i++)
                        DrawTile(visible[i], tile);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.Space(gap);
            }
            if (visible.Count == 0)
                GUILayout.Label("No items match the current filter / search.", _placeholder);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTile(Item it, float width)
        {
            (string name, int w, int h) res = UISpecPreview.DefaultResolutions[_resolutionIndex];
            float aspect = (float)res.h / res.w;
            float thumbHeight = Mathf.Clamp(width * aspect, width * 0.5f, width * 1.9f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(width)))
            {
                Rect thumbRect = GUILayoutUtility.GetRect(width - 10f, thumbHeight,
                    GUILayout.ExpandWidth(false));
                bool selected = Selection.activeObject == it.prefab;

                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(thumbRect, new Color(0f, 0f, 0f, 0.22f));
                    if (it.thumbnail != null)
                        GUI.DrawTexture(thumbRect, it.thumbnail, ScaleMode.ScaleToFit);
                    else
                        GUI.Label(thumbRect, "not rendered\n(Re-render)", _placeholder);
                    if (selected) DrawOutline(thumbRect, NeoColors.Interactive);
                }

                HandleTileEvents(thumbRect, it);

                using (new EditorGUILayout.HorizontalScope())
                {
                    NeoGUI.Badge(it.isPopup ? "Popup" : "View",
                        it.isPopup ? NeoColors.Flow : NeoColors.Containers);
                    GUILayout.Label(new GUIContent(it.displayName, it.assetPath), _tileName,
                        GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();
                    if (it.issues > 0) NeoGUI.Badge(it.issues.ToString(), NeoColors.Remove);
                    if (it.warnings > 0) NeoGUI.Badge(it.warnings.ToString(), NeoColors.Warning);
                }
            }
        }

        private void HandleTileEvents(Rect rect, Item it)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.clickCount == 2) AssetDatabase.OpenAsset(it.prefab);
                else { Selection.activeObject = it.prefab; EditorGUIUtility.PingObject(it.prefab); }
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ContextClick)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Open Prefab"), false, () => AssetDatabase.OpenAsset(it.prefab));
                menu.AddItem(new GUIContent("Ping in Project"), false, () => EditorGUIUtility.PingObject(it.prefab));
                menu.AddItem(new GUIContent("Re-render Tile"), false, () => { RenderThumbnail(it); Repaint(); });
                menu.AddItem(new GUIContent("Save Full-Res Screenshot…"), false, () => SaveFullScreenshot(it));
                menu.ShowAsContext();
                e.Use();
            }
        }

        private void SaveFullScreenshot(Item it)
        {
            (string name, int width, int height) res = UISpecPreview.DefaultResolutions[_resolutionIndex];
            string path = EditorUtility.SaveFilePanel("Save Screenshot", "",
                $"{it.displayName}_{res.width}x{res.height}.png", "png");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                UIScreenshotter.Capture(it.prefab, path, res.width, res.height);
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Neo.UI] Gallery screenshot failed: {e.Message}");
            }
        }

        private List<Item> Filtered()
        {
            var list = new List<Item>(_items.Count);
            foreach (Item it in _items)
            {
                if (_filter == Filter.Views && it.isPopup) continue;
                if (_filter == Filter.Popups && !it.isPopup) continue;
                if (!string.IsNullOrEmpty(_search) &&
                    it.displayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(it);
            }
            return list;
        }

        private static void DrawOutline(Rect r, Color color)
        {
            const float t = 2f;
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), color);
        }
    }
}
