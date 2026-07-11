using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// Drop-target side of the palette's drag-and-drop: makes <see cref="NeoPaletteWindow"/> tiles
    /// droppable onto the Hierarchy and the Scene view. Reads the reserved
    /// <see cref="NeoWidgetPalette.DragKey"/> / <see cref="NeoWidgetPalette.PresetDragKey"/> /
    /// <see cref="NeoWidgetPalette.TemplateDragKey"/> generic-data payloads (documented on the palette
    /// since the Composer era, wired up here) and routes them through the ONE shared
    /// <see cref="NeoPaletteWindow.SpawnPayload"/> — so a drop behaves exactly like a tile click
    /// (Canvas/EventSystem bootstrap, undo, Linked/Detached preset mode, Recent strip), just with the
    /// drop target as the parent. Handlers that see no palette payload return
    /// <see cref="DragAndDropVisualMode.None"/> immediately, leaving every other drag untouched.
    /// A scene drop outside any layout group additionally lands the widget at the cursor.
    /// </summary>
    [InitializeOnLoad]
    internal static class NeoPaletteDropHandlers
    {
        static NeoPaletteDropHandlers()
        {
            // Once per domain load — the handler lists reset with the domain, so this never stacks.
            DragAndDrop.AddDropHandler((DragAndDrop.HierarchyDropHandler)OnHierarchyDrop);
            DragAndDrop.AddDropHandlerV2((DragAndDrop.SceneDropHandler)OnSceneDrop);
        }

        private readonly struct Payload
        {
            public readonly string kind, preset, template;

            public Payload(string kind, string preset, string template)
            {
                this.kind = kind;
                this.preset = preset;
                this.template = template;
            }

            public bool IsEmpty => string.IsNullOrEmpty(kind) && string.IsNullOrEmpty(template);
        }

        private static Payload Read() => new Payload(
            DragAndDrop.GetGenericData(NeoWidgetPalette.DragKey) as string,
            DragAndDrop.GetGenericData(NeoWidgetPalette.PresetDragKey) as string,
            DragAndDrop.GetGenericData(NeoWidgetPalette.TemplateDragKey) as string);

        private static DragAndDropVisualMode OnHierarchyDrop(int dropTargetInstanceID,
            HierarchyDropFlags dropMode, Transform parentForDraggedObjects, bool perform)
        {
            Payload payload = Read();
            if (payload.IsEmpty) return DragAndDropVisualMode.None;
            if (perform)
            {
                DragAndDrop.AcceptDrag();
                var target = EditorUtility.InstanceIDToObject(dropTargetInstanceID) as GameObject;
                NeoPaletteWindow.SpawnPayload(payload.kind, payload.preset, payload.template, target);
            }
            return DragAndDropVisualMode.Copy;
        }

        private static DragAndDropVisualMode OnSceneDrop(Object dropUpon, Vector3 worldPosition,
            Vector2 viewportPosition, Transform parentForDraggedObjects, bool perform)
        {
            Payload payload = Read();
            if (payload.IsEmpty) return DragAndDropVisualMode.None;
            if (perform)
            {
                DragAndDrop.AcceptDrag();
                GameObject target = dropUpon as GameObject
                    ?? (dropUpon is Component component ? component.gameObject : null);
                GameObject created = NeoPaletteWindow.SpawnPayload(
                    payload.kind, payload.preset, payload.template, target);
                PlaceAtDropPoint(created, worldPosition);
            }
            return DragAndDropVisualMode.Copy;
        }

        // A free-anchored drop lands where the cursor was (the Doozy "position visible in scene view"
        // nicety, but at the actual drop point); a layout-managed child keeps its layout slot, and a
        // stretch-anchored root (a dropped View tile, a safearea) keeps its anchored rest position —
        // offsetting a stretched rect's localPosition would shove it half off its parent.
        private static void PlaceAtDropPoint(GameObject created, Vector3 worldPosition)
        {
            if (created == null) return;
            var rect = created.transform as RectTransform;
            var parent = created.transform.parent as RectTransform;
            if (rect == null || parent == null || parent.GetComponent<LayoutGroup>() != null) return;
            if (rect.anchorMin != rect.anchorMax) return; // stretched on some axis — leave it be
            Vector3 local = parent.InverseTransformPoint(worldPosition);
            rect.localPosition = new Vector3(local.x, local.y, 0f);
        }
    }
}
