using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Slide-in / slide-out direction presets: where an element animates from/to,
    /// relative to its parent rect, without hand-typing vectors.
    /// </summary>
    public enum UIMoveDirection
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
        TopLeft = 4,
        TopCenter = 5,
        TopRight = 6,
        MiddleLeft = 7,
        MiddleCenter = 8,
        MiddleRight = 9,
        BottomLeft = 10,
        BottomCenter = 11,
        BottomRight = 12,
        /// <summary> No preset — endpoint resolved via ReferenceValue + custom/offset. </summary>
        CustomPosition = 13
    }

    /// <summary> Computes the off-parent anchored positions for the direction presets (Doozy-compatible math). </summary>
    public static class MoveMath
    {
        /// <summary>
        /// Returns the anchoredPosition3D that places the target fully outside its parent rect
        /// in the given direction. Pure horizontal (Left/Right) keeps the start Y; pure vertical
        /// (Top/Bottom) keeps the start X.
        /// </summary>
        public static Vector3 GetTargetPosition(RectTransform target, UIMoveDirection direction, Vector3 startPosition)
        {
            if (target == null || direction == UIMoveDirection.CustomPosition) return startPosition;

            var parent = target.parent as RectTransform;
            Rect targetRect = target.rect;
            Rect parentRect = parent != null ? parent.rect : targetRect;
            Vector2 pivot = target.pivot;
            Vector2 anchorMin = target.anchorMin;
            Vector2 anchorMax = target.anchorMax;
            Vector3 scale = target.localScale;

            float xOffsetLeft =
                targetRect.width * scale.x * (1f - pivot.x)
                + parentRect.width * (1f - pivot.x) * anchorMin.x
                + parentRect.width * pivot.x * anchorMax.x;

            float xOffsetRight =
                parentRect.width
                + targetRect.width * scale.x * pivot.x
                - parentRect.width * (1f - pivot.x) * anchorMin.x
                - parentRect.width * pivot.x * anchorMax.x;

            float yOffsetTop =
                parentRect.height
                + targetRect.height * scale.y * pivot.y
                - parentRect.height * (1f - pivot.y) * anchorMin.y
                - parentRect.height * pivot.y * anchorMax.y;

            float yOffsetBottom =
                targetRect.height * scale.y * (1f - pivot.y)
                + parentRect.height * (1f - pivot.y) * anchorMin.y
                + parentRect.height * pivot.y * anchorMax.y;

            float z = startPosition.z;
            Vector3 position;
            switch (direction)
            {
                case UIMoveDirection.Left: position = new Vector3(-xOffsetLeft, startPosition.y, z); break;
                case UIMoveDirection.Right: position = new Vector3(xOffsetRight, startPosition.y, z); break;
                case UIMoveDirection.Top: position = new Vector3(startPosition.x, yOffsetTop, z); break;
                case UIMoveDirection.Bottom: position = new Vector3(startPosition.x, -yOffsetBottom, z); break;
                case UIMoveDirection.TopLeft: position = new Vector3(-xOffsetLeft, yOffsetTop, z); break;
                case UIMoveDirection.TopCenter: position = new Vector3(0f, yOffsetTop, z); break;
                case UIMoveDirection.TopRight: position = new Vector3(xOffsetRight, yOffsetTop, z); break;
                case UIMoveDirection.MiddleLeft: position = new Vector3(-xOffsetLeft, 0f, z); break;
                case UIMoveDirection.MiddleCenter: position = new Vector3(0f, 0f, z); break;
                case UIMoveDirection.MiddleRight: position = new Vector3(xOffsetRight, 0f, z); break;
                case UIMoveDirection.BottomLeft: position = new Vector3(-xOffsetLeft, -yOffsetBottom, z); break;
                case UIMoveDirection.BottomCenter: position = new Vector3(0f, -yOffsetBottom, z); break;
                case UIMoveDirection.BottomRight: position = new Vector3(xOffsetRight, -yOffsetBottom, z); break;
                default: position = startPosition; break;
            }

            return ApplyRotationCompensation(target, position, direction);
        }

        /// <summary> Widens the clearance for rotated elements so they still end up fully off-screen. </summary>
        private static Vector3 ApplyRotationCompensation(RectTransform target, Vector3 position, UIMoveDirection direction)
        {
            float zRotation = target.localEulerAngles.z;
            float angle = Mathf.Abs(zRotation % 180f);
            if (Mathf.Approximately(angle, 0f) || Mathf.Approximately(angle, 90f)) return position;

            float width = target.rect.width;
            float height = target.rect.height;
            float newWidth, newHeight;
            if (angle < 90f)
            {
                float theta = angle * Mathf.Deg2Rad;
                newWidth = width * Mathf.Cos(theta) + height * Mathf.Sin(theta);
                newHeight = width * Mathf.Sin(theta) + height * Mathf.Cos(theta);
            }
            else
            {
                float theta = (angle - 90f) * Mathf.Deg2Rad;
                newWidth = height * Mathf.Cos(theta) + width * Mathf.Sin(theta);
                newHeight = height * Mathf.Sin(theta) + width * Mathf.Cos(theta);
            }

            float offsetX = (newWidth - width) * 0.5f;
            float offsetY = (newHeight - height) * 0.5f;
            int xDir = DirectionX(direction);
            int yDir = DirectionY(direction);
            return position + new Vector3(offsetX * xDir, offsetY * yDir, 0f);
        }

        private static int DirectionX(UIMoveDirection d)
        {
            switch (d)
            {
                case UIMoveDirection.Left:
                case UIMoveDirection.TopLeft:
                case UIMoveDirection.MiddleLeft:
                case UIMoveDirection.BottomLeft:
                    return -1;
                case UIMoveDirection.Right:
                case UIMoveDirection.TopRight:
                case UIMoveDirection.MiddleRight:
                case UIMoveDirection.BottomRight:
                    return 1;
                default:
                    return 0;
            }
        }

        private static int DirectionY(UIMoveDirection d)
        {
            switch (d)
            {
                case UIMoveDirection.Top:
                case UIMoveDirection.TopLeft:
                case UIMoveDirection.TopCenter:
                case UIMoveDirection.TopRight:
                    return 1;
                case UIMoveDirection.Bottom:
                case UIMoveDirection.BottomLeft:
                case UIMoveDirection.BottomCenter:
                case UIMoveDirection.BottomRight:
                    return -1;
                default:
                    return 0;
            }
        }
    }
}
