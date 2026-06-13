using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    public enum SpecNodeKind
    {
        Theme, ViewsHeader, View, Element,
        PopupsHeader, Popup, MenusHeader, Catalog, MenuItem, Flow
    }

    /// <summary> One visible row in the spec tree. Carries everything the inspector and the
    /// context-menu mutations need — the owning view/popup, the element and the list it lives in. </summary>
    public sealed class SpecNode
    {
        public SpecNodeKind kind;
        public string label;
        public string path;        // stable selection / expand key (SpecPath-aligned)
        public int depth;
        public bool expandable;
        public Color accent;

        public ViewSpec view;                 // owning view (View nodes + elements inside a view)
        public PopupSpec popup;               // owning popup (Popup nodes + elements inside a popup)
        public ElementSpec element;
        public List<ElementSpec> siblings;    // the list element lives in (for delete/move/duplicate)
        public int index;                     // element index within siblings
        public MenuCatalogSpec catalog;
        public MenuItemSpec menuItem;
    }

    /// <summary>
    /// Left pane: an IMGUI tree over the live <see cref="UISpec"/> — theme, views (each expanding to
    /// its element tree), popups, settings/cheats catalogs and a flow leaf. Selection drives the
    /// inspector and the preview highlight. Context-menu mutations all route through
    /// <see cref="SpecDocument.ApplyEdit"/> so they snapshot for undo and trigger a preview rebuild.
    ///
    /// <para>The visible-row list is rebuilt only when the document changes or a row is expanded
    /// (not per frame) per the IMGUI caching rules.</para>
    /// </summary>
    public class SpecTreeView
    {
        private const float RowHeight = 18f;
        private const float Indent = 14f;

        private readonly SpecDocument _document;
        private readonly HashSet<string> _expanded = new HashSet<string>
        {
            "theme", "views", "popups", "menus"
        };
        private readonly List<SpecNode> _rows = new List<SpecNode>();
        private bool _dirty = true;
        private Vector2 _scroll;
        private string _filter = "";   // search box: when set, the tree shows matches + their ancestors
        private bool _filtering;

        public SpecNode Selected { get; private set; }
        public string SelectedPath { get; private set; }
        public event Action SelectionChanged;

        public SpecTreeView(SpecDocument document)
        {
            _document = document;
        }

        public void MarkDirty() => _dirty = true;

        public void Select(string path)
        {
            SelectedPath = path;
            ExpandAncestors(path);  // so a path selected from the canvas/inspector resolves to a row
            _dirty = true;
        }

        /// <summary> Expands every ancestor node of <paramref name="path"/> (the section, the owning
        /// view/popup/catalog, and each enclosing element) so a selection driven from the preview
        /// canvas isn't swallowed when its tree branch happens to be collapsed. </summary>
        private void ExpandAncestors(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            int firstSlash = path.IndexOf('/');
            _expanded.Add(firstSlash < 0 ? path : path.Substring(0, firstSlash)); // section header
            foreach (string marker in new[] { "/elements[", "/children[", "/items[" })
            {
                int at = 0;
                while ((at = path.IndexOf(marker, at, StringComparison.Ordinal)) >= 0)
                {
                    _expanded.Add(path.Substring(0, at)); // owner / enclosing element node
                    at += marker.Length;
                }
            }
        }

        // ------------------------------------------------------------------ build

        private void RebuildIfNeeded()
        {
            if (!_dirty) return;
            _dirty = false;
            _rows.Clear();
            _filtering = !string.IsNullOrEmpty(_filter);
            UISpec spec = _document.Spec;

            AddRow(new SpecNode { kind = SpecNodeKind.Theme, label = "Theme", path = "theme", depth = 0, accent = NeoColors.Theming });

            var viewsHeader = new SpecNode { kind = SpecNodeKind.ViewsHeader, label = $"Views ({spec.views.Count})", path = "views", depth = 0, expandable = true, accent = NeoColors.Containers };
            AddRow(viewsHeader);
            if (IsExpanded("views"))
                foreach (ViewSpec view in spec.views)
                {
                    string viewPath = SpecPath.View(view.id);
                    AddRow(new SpecNode { kind = SpecNodeKind.View, label = view.id, path = viewPath, depth = 1, expandable = view.elements.Count > 0, accent = NeoColors.Containers, view = view });
                    if (IsExpanded(viewPath))
                        AddElements(view.elements, viewPath, true, 2, view, null);
                }

            var popupsHeader = new SpecNode { kind = SpecNodeKind.PopupsHeader, label = $"Popups ({spec.popups.Count})", path = "popups", depth = 0, expandable = true, accent = NeoColors.Containers };
            AddRow(popupsHeader);
            if (IsExpanded("popups"))
                foreach (PopupSpec popup in spec.popups)
                {
                    string popupPath = SpecPath.Popup(popup.name);
                    AddRow(new SpecNode { kind = SpecNodeKind.Popup, label = popup.name, path = popupPath, depth = 1, expandable = popup.elements.Count > 0, accent = NeoColors.Containers, popup = popup });
                    if (IsExpanded(popupPath))
                        AddElements(popup.elements, popupPath, true, 2, null, popup);
                }

            AddMenusSection(spec);

            AddRow(new SpecNode { kind = SpecNodeKind.Flow, label = spec.flow != null ? $"Flow: {spec.flow.name}" : "Flow (none)", path = "flow", depth = 0, accent = NeoColors.Flow });

            ApplyFilter();

            // resolve selection against the new rows
            Selected = null;
            foreach (SpecNode node in _rows)
                if (node.path == SelectedPath) { Selected = node; break; }
        }

        /// <summary> Keeps only rows that match the search box plus the ancestors needed to reach them,
        /// so a search over a big project collapses straight to the screens/settings you mean. Matching
        /// is a case-insensitive substring of the row label (which already folds in the kind + id/name).
        /// Ancestors are recovered from the flat list by walking back over decreasing depth. </summary>
        private void ApplyFilter()
        {
            if (!_filtering) return;
            string needle = _filter.ToLowerInvariant();
            int n = _rows.Count;
            var keep = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (_rows[i].label == null || !_rows[i].label.ToLowerInvariant().Contains(needle)) continue;
                keep[i] = true;
                int needDepth = _rows[i].depth;
                for (int j = i - 1; j >= 0 && needDepth > 0; j--)
                    if (_rows[j].depth < needDepth) { keep[j] = true; needDepth = _rows[j].depth; }
            }
            var filtered = new List<SpecNode>(n);
            for (int i = 0; i < n; i++) if (keep[i]) filtered.Add(_rows[i]);
            _rows.Clear();
            _rows.AddRange(filtered);
        }

        /// <summary> One neutral <c>Menus (N)</c> section over every registered catalog kind
        /// (<see cref="ComposerCatalogKinds.All"/>). The header is cosmetic — each catalog row keeps
        /// its real <see cref="SpecPath.Catalog"/> path (section = the kind id) so selection, the
        /// inspector and baseline addressing are unchanged — and carries a kind tag so one section
        /// stays legible. An empty <c>Menus (0)</c> asserts nothing about any particular kind. </summary>
        private void AddMenusSection(UISpec spec)
        {
            int total = 0;
            foreach (CatalogKind kind in ComposerCatalogKinds.All)
                total += kind.list(spec).Count;

            AddRow(new SpecNode { kind = SpecNodeKind.MenusHeader, label = $"Menus ({total})", path = "menus", depth = 0, expandable = true, accent = NeoColors.Data });
            if (!IsExpanded("menus")) return;

            foreach (CatalogKind kind in ComposerCatalogKinds.All)
            {
                foreach (MenuCatalogSpec catalog in kind.list(spec))
                {
                    string catalogPath = SpecPath.Catalog(kind.id, catalog.id);
                    AddRow(new SpecNode { kind = SpecNodeKind.Catalog, label = $"{kind.label} · {catalog.id}", path = catalogPath, depth = 1, expandable = catalog.items.Count > 0, accent = NeoColors.Data, catalog = catalog });
                    if (!IsExpanded(catalogPath)) continue;
                    for (int i = 0; i < catalog.items.Count; i++)
                    {
                        MenuItemSpec item = catalog.items[i];
                        AddRow(new SpecNode
                        {
                            kind = SpecNodeKind.MenuItem,
                            label = $"{item.kind}: {item.id}",
                            path = $"{catalogPath}/items[{i}]",
                            depth = 2,
                            accent = NeoColors.Data,
                            catalog = catalog,
                            menuItem = item
                        });
                    }
                }
            }
        }

        /// <summary> Test seam: forces a rebuild and returns the resulting flat row list (the Composer
        /// keeps it private + rebuilt-on-demand per the IMGUI caching rules). </summary>
        internal IReadOnlyList<SpecNode> RebuildForTest()
        {
            _dirty = true;
            RebuildIfNeeded();
            return _rows;
        }

        private void AddElements(List<ElementSpec> elements, string ownerPath, bool topLevel, int depth,
            ViewSpec view, PopupSpec popup)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                ElementSpec element = elements[i];
                string path = ownerPath + (topLevel ? "/elements[" : "/children[") + i + "]";
                bool hasChildren = element.children != null && element.children.Count > 0;
                AddRow(new SpecNode
                {
                    kind = SpecNodeKind.Element,
                    label = DescribeElement(element),
                    path = path,
                    depth = depth,
                    expandable = hasChildren,
                    accent = AccentFor(element.kind),
                    view = view,
                    popup = popup,
                    element = element,
                    siblings = elements,
                    index = i
                });
                if (hasChildren && IsExpanded(path))
                    AddElements(element.children, path, false, depth + 1, view, popup);
            }
        }

        private static string DescribeElement(ElementSpec element)
        {
            string detail = !string.IsNullOrEmpty(element.label) ? element.label
                : !string.IsNullOrEmpty(element.id) ? element.id : null;
            return detail != null ? $"{element.kind}  {detail}" : element.kind;
        }

        private static Color AccentFor(string kind)
        {
            // project-registered kinds carry their own accent; built-ins fall to the category default
            if (NeoElementKinds.TryGet(kind, out INeoElementKind ext)) return ext.Accent;
            switch (kind)
            {
                case "button": case "toggle": case "switch": case "tab": case "slider":
                case "stepper": case "input": case "dropdown": case "tabbar":
                    return NeoColors.Interactive;
                case "vstack": case "hstack": case "grid": case "scroll": case "panel":
                case "overlay": case "safearea": case "list":
                    return NeoColors.Containers;
                case "shape": case "image": case "icon": case "progress": case "counter":
                    return NeoColors.Rendering;
                case "settings": case "cheats":
                    return NeoColors.Data;
                default:
                    return NeoColors.TextSubtle;
            }
        }

        private void AddRow(SpecNode node) => _rows.Add(node);
        // while a search is active every node is treated as expanded so matches deep in the tree surface
        private bool IsExpanded(string path) => _filtering || _expanded.Contains(path);

        // ------------------------------------------------------------------ draw

        private const float SearchHeight = 20f;

        public void OnGUI(Rect rect)
        {
            DrawSearch(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, SearchHeight - 4f));
            RebuildIfNeeded();

            var listRect = new Rect(rect.x, rect.y + SearchHeight, rect.width, rect.height - SearchHeight);
            var viewRect = new Rect(0, 0, listRect.width - 16f, _rows.Count * RowHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, viewRect);
            for (int i = 0; i < _rows.Count; i++)
                DrawRow(new Rect(0, i * RowHeight, viewRect.width, RowHeight), _rows[i]);
            GUI.EndScrollView();
            if (_filtering && _rows.Count == 0)
                GUI.Label(new Rect(listRect.x + 6f, listRect.y + 4f, listRect.width - 8f, 18f),
                    $"No match for “{_filter}”", EditorStyles.miniLabel);
        }

        private void DrawSearch(Rect rect)
        {
            EditorGUI.BeginChangeCheck();
            string next = EditorGUI.TextField(rect, _filter, GetSearchStyle());
            if (EditorGUI.EndChangeCheck()) { _filter = next ?? ""; _dirty = true; }
        }

        private static GUIStyle s_search;
        private static GUIStyle GetSearchStyle() =>
            s_search ??= new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);

        private void DrawRow(Rect rect, SpecNode node)
        {
            Event e = Event.current;
            bool selected = node.path == SelectedPath;

            if (e.type == EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(rect, NeoColors.RowSelected);
                else if (rect.Contains(e.mousePosition)) EditorGUI.DrawRect(rect, NeoColors.RowHover);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), node.accent.WithAlpha(0.7f));
            }

            float x = 6f + node.depth * Indent;
            var foldoutRect = new Rect(x, rect.y, 12f, rect.height);
            if (node.expandable)
            {
                bool open = IsExpanded(node.path);
                bool newOpen = EditorGUI.Foldout(foldoutRect, open, GUIContent.none);
                if (newOpen != open)
                {
                    if (newOpen) _expanded.Add(node.path); else _expanded.Remove(node.path);
                    _dirty = true;
                }
            }

            var labelRect = new Rect(x + 14f, rect.y, rect.width - x - 16f, rect.height);
            GUI.Label(labelRect, node.label, selected ? EditorStyles.whiteLabel : EditorStyles.label);

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && !foldoutRect.Contains(e.mousePosition))
            {
                if (e.button == 1) // right click → context menu
                {
                    ShowContextMenu(node);
                    e.Use();
                }
                else
                {
                    SelectedPath = node.path;
                    Selected = node;
                    SelectionChanged?.Invoke();
                    e.Use();
                }
            }
        }

        // ------------------------------------------------------------------ context menu

        private void ShowContextMenu(SpecNode node)
        {
            var menu = new GenericMenu();
            switch (node.kind)
            {
                case SpecNodeKind.ViewsHeader:
                    menu.AddItem(new GUIContent("Add View"), false, AddView);
                    break;
                case SpecNodeKind.PopupsHeader:
                    menu.AddItem(new GUIContent("Add Popup"), false, AddPopup);
                    break;
                case SpecNodeKind.MenusHeader:
                    foreach (CatalogKind kind in ComposerCatalogKinds.All)
                    {
                        string kindId = kind.id;
                        menu.AddItem(new GUIContent($"Add Menu/{kind.label}"), false, () => AddCatalog(kindId));
                    }
                    break;
                case SpecNodeKind.View:
                    AddElementCreateItems(menu, "Add Element/", k => AddElementTo(node.view.elements, node, k));
                    menu.AddItem(new GUIContent("Add Tab + Panel"), false, () => AddTabWithPanel(node.view, node));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Duplicate View"), false, () => DuplicateView(node.view));
                    menu.AddItem(new GUIContent("Delete View"), false, () => DeleteView(node.view));
                    break;
                case SpecNodeKind.Popup:
                    AddElementCreateItems(menu, "Add Element/", k => AddElementTo(node.popup.elements, node, k));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Duplicate Popup"), false, () => DuplicatePopup(node.popup));
                    menu.AddItem(new GUIContent("Delete Popup"), false, () => DeletePopup(node.popup));
                    break;
                case SpecNodeKind.Element:
                    AddElementCreateItems(menu, "Add Child/", k => AddElementTo(node.element.children, node, k));
                    AddElementCreateItems(menu, "Add Sibling/", k => AddSibling(node, k));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Move Up"), false, () => MoveElement(node, -1));
                    menu.AddItem(new GUIContent("Move Down"), false, () => MoveElement(node, 1));
                    menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicateElement(node));
                    menu.AddItem(new GUIContent("Delete"), false, () => DeleteElement(node));
                    break;
                case SpecNodeKind.Catalog:
                    foreach (string itemKind in MenuItemSpec.Kinds)
                    {
                        string captured = itemKind;
                        menu.AddItem(new GUIContent($"Add Item/{itemKind}"), false, () => AddMenuItem(node.catalog, captured));
                    }
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Duplicate Catalog"), false, () => DuplicateCatalog(node.catalog));
                    menu.AddItem(new GUIContent("Delete Catalog"), false, () => DeleteCatalog(node.catalog));
                    break;
                case SpecNodeKind.MenuItem:
                    menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicateMenuItem(node));
                    menu.AddItem(new GUIContent("Delete"), false, () => DeleteMenuItem(node));
                    break;
            }
            if (menu.GetItemCount() > 0) menu.ShowAsContext();
        }

        private static void AddElementCreateItems(GenericMenu menu, string prefix, Action<string> create)
        {
            foreach (string kind in ElementSpec.KnownKinds)
            {
                string captured = kind;
                menu.AddItem(new GUIContent(prefix + kind), false, () => create(captured));
            }
        }

        // ------------------------------------------------------------------ mutations (all via ApplyEdit)

        private void AddView()
        {
            string id = UniqueViewId();
            _document.ApplyEdit(() => _document.Spec.views.Add(ComposerFactory.NewView("Menu", id)), "Add View");
            _expanded.Add("views");
            SelectAfter(SpecPath.View($"Menu/{id}"));
        }

        private string UniqueViewId()
        {
            int n = _document.Spec.views.Count + 1;
            string name = $"View{n}";
            while (_document.Spec.views.Exists(v => v.viewName == name)) name = $"View{++n}";
            return name;
        }

        private void DuplicateView(ViewSpec view)
        {
            ViewSpec clone = ComposerFactory.Clone(view);
            clone.viewName += "Copy";
            _document.ApplyEdit(() => _document.Spec.views.Add(clone), "Duplicate View");
            SelectAfter(SpecPath.View(clone.id));
        }

        private void DeleteView(ViewSpec view)
        {
            _document.ApplyEdit(() => _document.Spec.views.Remove(view), "Delete View");
            SelectAfter("views");
        }

        private void AddPopup()
        {
            int n = _document.Spec.popups.Count + 1;
            string name = $"Popup{n}";
            while (_document.Spec.popups.Exists(p => p.name == name)) name = $"Popup{++n}";
            _document.ApplyEdit(() => _document.Spec.popups.Add(ComposerFactory.NewPopup(name)), "Add Popup");
            _expanded.Add("popups");
            SelectAfter(SpecPath.Popup(name));
        }

        private void DuplicatePopup(PopupSpec popup)
        {
            PopupSpec clone = ComposerFactory.Clone(popup);
            int n = 2;
            string baseName = popup.name;
            clone.name = $"{baseName}Copy";
            while (_document.Spec.popups.Exists(p => p.name == clone.name)) clone.name = $"{baseName}Copy{n++}";
            _document.ApplyEdit(() => _document.Spec.popups.Add(clone), "Duplicate Popup");
            SelectAfter(SpecPath.Popup(clone.name));
        }

        private void DeletePopup(PopupSpec popup)
        {
            _document.ApplyEdit(() => _document.Spec.popups.Remove(popup), "Delete Popup");
            SelectAfter("popups");
        }

        private void DuplicateCatalog(MenuCatalogSpec catalog)
        {
            if (!ComposerCatalogKinds.TryGet(catalog.kind, out CatalogKind kind))
            {
                Debug.LogWarning($"[Composer] No catalog kind registered for '{catalog.kind}'; cannot duplicate.");
                return;
            }
            MenuCatalogSpec clone = ComposerFactory.Clone(catalog);
            List<MenuCatalogSpec> list = kind.list(_document.Spec);
            int n = 2;
            string baseName = catalog.menuName;
            clone.menuName = $"{baseName}Copy";
            while (list.Exists(c => c.menuName == clone.menuName && c.category == clone.category))
                clone.menuName = $"{baseName}Copy{n++}";
            _document.ApplyEdit(() => list.Add(clone), "Duplicate Catalog");
            _expanded.Add("menus");
            SelectAfter(SpecPath.Catalog(kind.id, clone.id));
        }

        private void AddCatalog(string kindId)
        {
            if (!ComposerCatalogKinds.TryGet(kindId, out CatalogKind kind))
            {
                Debug.LogWarning($"[Composer] No catalog kind registered for '{kindId}'; cannot add.");
                return;
            }
            List<MenuCatalogSpec> list = kind.list(_document.Spec);
            int n = list.Count + 1;
            string menuName = $"Catalog{n}";
            string category = kind.defaultCategory;
            _document.ApplyEdit(() => list.Add(ComposerFactory.NewCatalog(kind.id, category, menuName)), "Add Catalog");
            _expanded.Add("menus");
            SelectAfter(SpecPath.Catalog(kind.id, $"{category}/{menuName}"));
        }

        private void DeleteCatalog(MenuCatalogSpec catalog)
        {
            _document.ApplyEdit(() =>
            {
                foreach (CatalogKind kind in ComposerCatalogKinds.All)
                    if (kind.list(_document.Spec).Remove(catalog)) return;
            }, "Delete Catalog");
            SelectAfter(null);
        }

        private void AddMenuItem(MenuCatalogSpec catalog, string kind)
        {
            _document.ApplyEdit(() => catalog.items.Add(ComposerFactory.NewMenuItem(kind)), "Add Menu Item");
            _expanded.Add(CatalogPath(catalog));
        }

        private void DuplicateMenuItem(SpecNode node)
        {
            int index = node.catalog.items.IndexOf(node.menuItem);
            _document.ApplyEdit(() => node.catalog.items.Insert(index + 1, ComposerFactory.Clone(node.menuItem)), "Duplicate Menu Item");
        }

        private void DeleteMenuItem(SpecNode node)
        {
            _document.ApplyEdit(() => node.catalog.items.Remove(node.menuItem), "Delete Menu Item");
            SelectAfter(CatalogPath(node.catalog));
        }

        private void AddElementTo(List<ElementSpec> list, SpecNode parentNode, string kind)
        {
            _document.ApplyEdit(() => list.Add(ComposerFactory.NewElement(kind)), $"Add {kind}");
            if (parentNode != null) _expanded.Add(parentNode.path);
        }

        private void AddSibling(SpecNode node, string kind)
        {
            _document.ApplyEdit(() => node.siblings.Insert(node.index + 1, ComposerFactory.NewElement(kind)), $"Add {kind}");
        }

        private void AddTabWithPanel(ViewSpec view, SpecNode node)
        {
            _document.ApplyEdit(() =>
            {
                int n = 1;
                string panelId;
                do { panelId = $"Panel/Tab{n++}"; } while (view.elements.Exists(el => el.id == panelId));
                ElementSpec tab = ComposerFactory.NewElement("tab");
                tab.controls = panelId;
                tab.id = $"Tab/Tab{n - 1}";
                view.elements.Add(tab);
                view.elements.Add(ComposerFactory.NewPanel(panelId));
            }, "Add Tab + Panel");
            if (node != null) _expanded.Add(node.path);
        }

        private void MoveElement(SpecNode node, int delta)
        {
            int target = node.index + delta;
            if (target < 0 || target >= node.siblings.Count) return;
            _document.ApplyEdit(() =>
            {
                ElementSpec moved = node.siblings[node.index];
                node.siblings.RemoveAt(node.index);
                node.siblings.Insert(target, moved);
            }, "Move Element");
        }

        private void DuplicateElement(SpecNode node)
        {
            _document.ApplyEdit(() => node.siblings.Insert(node.index + 1, ComposerFactory.Clone(node.element)), "Duplicate Element");
        }

        private void DeleteElement(SpecNode node)
        {
            _document.ApplyEdit(() => node.siblings.Remove(node.element), "Delete Element");
            SelectAfter(null);
        }

        private static string CatalogPath(MenuCatalogSpec catalog) =>
            SpecPath.Catalog(catalog.kind, catalog.id);

        private void SelectAfter(string path)
        {
            SelectedPath = path;
            _dirty = true;
            SelectionChanged?.Invoke();
        }

        // toolbar entry points (used by the window's tree-pane toolbar)
        public void AddViewFromToolbar() => AddView();
        public void AddPopupFromToolbar() => AddPopup();

        /// <summary> Adds a catalog of the given registered kind id — the single <c>+ Menu ▾</c>
        /// picker routes here, one entry per <see cref="ComposerCatalogKinds.All"/>. </summary>
        public void AddCatalogFromToolbar(string kindId) => AddCatalog(kindId);

        /// <summary> The catalog-kind option labels shown in the <c>+ Menu ▾</c> picker (= every
        /// registered kind), and a parallel id lookup. Built fresh on demand (dropdown-open only). </summary>
        public static List<string> CatalogKindLabels()
        {
            var labels = new List<string>(ComposerCatalogKinds.All.Count);
            foreach (CatalogKind kind in ComposerCatalogKinds.All) labels.Add(kind.label);
            return labels;
        }

        /// <summary> Maps a picker label back to its kind id (label is unique per registered kind). </summary>
        public static string CatalogKindIdForLabel(string label)
        {
            foreach (CatalogKind kind in ComposerCatalogKinds.All)
                if (kind.label == label) return kind.id;
            return null;
        }
    }
}
