using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Drives a GridLayoutGroup's FixedColumnCount from the grid's actual width so one layout
    /// works across aspect ratios: columns = how many cells fit the current width, never below
    /// one. Spec grids without an explicit "columns" get this instead of GridLayoutGroup's
    /// Flexible constraint, whose row math never feeds ContentSizeFitter stacks a usable
    /// preferred height (the grid collapses or wraps on a stale width).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(GridLayoutGroup))]
    [AddComponentMenu("Neo/UI/Containers/UI Responsive Grid Columns")]
    public class UIResponsiveGridColumns : UIBehaviour
    {
        private GridLayoutGroup _grid;

        private GridLayoutGroup grid => _grid != null ? _grid : _grid = GetComponent<GridLayoutGroup>();

        protected override void OnEnable()
        {
            base.OnEnable();
            Apply();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Apply();
        }

        private void Apply()
        {
            GridLayoutGroup layout = grid;
            if (layout == null) return;
            float width = ((RectTransform)transform).rect.width;
            float cell = layout.cellSize.x + layout.spacing.x;
            if (width <= 0f || cell <= 0f) return;

            float usable = width - layout.padding.horizontal + layout.spacing.x;
            int columns = Mathf.Max(1, Mathf.FloorToInt(usable / cell));
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            if (layout.constraintCount == columns) return;
            layout.constraintCount = columns;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
