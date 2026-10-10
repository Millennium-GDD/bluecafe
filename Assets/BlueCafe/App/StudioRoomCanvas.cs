using System;
using System.Collections.Generic;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    internal sealed class StudioRoomCanvas : VisualElement
    {
        public Action ResizeStarted;
        public Action<float, float, bool> ResizeChanged;
        public Action<int> WallSelected;
        public Action ZoomChanged;
        public Func<bool> CanInteract;
        public float Zoom { get; private set; } = 1;
        Layout cafeLayout, preview;
        readonly Dictionary<string, Item> items = new Dictionary<string, Item>();
        Catalog catalog;
        readonly Label widthLabel, depthLabel, cafeLabel;
        Rect roomRect;
        float pixelsPerMetre, gestureScale, initialWidth, initialDepth;
        Vector2 pointerStart;
        int corner = -1, capturedPointer, selectedWall;
        bool walls, validPreview = true;
        static readonly Color Blue = new Color(.09f, .47f, .94f);
        static readonly Color Ink = new Color(.20f, .31f, .44f);

        public StudioRoomCanvas()
        {
            AddToClassList("room-plan-canvas"); focusable = true;
            widthLabel = MakeLabel("room-plan-label"); depthLabel = MakeLabel("room-plan-label"); cafeLabel = MakeLabel("room-plan-title");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => LayoutLabels());
            RegisterCallback<DetachFromPanelEvent>(_ => CancelResize());
            this.AddManipulator(new BoundsManipulator(this));
            RegisterCallback<WheelEvent>(e => { SetZoom(Zoom * (e.delta.y > 0 ? 1 / 1.08f : 1.08f)); e.StopPropagation(); });
        }

        Label MakeLabel(string cls)
        {
            var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList(cls); Add(label); return label;
        }

        public void SetLayout(Layout next, Catalog source)
        {
            cafeLayout = Copies.Copy(next); catalog = source; items.Clear();
            foreach (var item in source.items) items[item.id] = item;
            if (corner < 0) preview = null;
            LayoutLabels(); MarkDirtyRepaint();
        }
        public void SetPreview(Layout next, bool valid) { preview = Copies.Copy(next); validPreview = valid; LayoutLabels(); MarkDirtyRepaint(); }
        public void SetWallMode(bool value, int selected) { walls = value; selectedWall = selected; MarkDirtyRepaint(); }
        public void SetZoom(float zoom) { CancelResize(); Zoom = Mathf.Clamp(zoom, .5f, 1.6f); LayoutLabels(); MarkDirtyRepaint(); ZoomChanged?.Invoke(); }
        public void CancelResize()
        {
            if (corner < 0 && preview == null) return;
            corner = -1; preview = null; validPreview = true;
            if (this.HasPointerCapture(capturedPointer)) this.ReleasePointer(capturedPointer);
            LayoutLabels(); MarkDirtyRepaint();
        }
        void Measure()
        {
            if (cafeLayout == null) return;
            // Preview dimensions use the gesture's fixed scale so the dragged handle follows the pointer.
            var current = preview ?? cafeLayout;
            var available = new Vector2(Mathf.Max(20, contentRect.width - 150), Mathf.Max(20, contentRect.height - 100));
            pixelsPerMetre = corner >= 0 ? gestureScale : Mathf.Min(available.x / cafeLayout.room.width, available.y / cafeLayout.room.depth) * Zoom;
            float w = current.room.width * pixelsPerMetre, d = current.room.depth * pixelsPerMetre;
            roomRect = new Rect((contentRect.width - w) / 2 + 18, (contentRect.height - d) / 2 - 8, w, d);
        }
        void LayoutLabels()
        {
            if (cafeLayout == null) return; Measure(); var current = preview ?? cafeLayout;
            widthLabel.text = current.room.width.ToString("0.#") + " m";
            depthLabel.text = current.room.depth.ToString("0.#") + " m";
            cafeLabel.text = "Cafe bounds\n" + (current.room.width * current.room.depth).ToString("0.#") + " m²";
            Place(widthLabel, roomRect.center.x - 42, roomRect.yMax + 29, 84, 28);
            Place(depthLabel, roomRect.xMin - 73, roomRect.center.y - 14, 65, 28);
            // Keep the heading clear of the real footprint plan, rather than hiding placed furniture.
            Place(cafeLabel, roomRect.center.x - Mathf.Min(100, roomRect.width / 2), roomRect.yMin + 15, Mathf.Min(200, roomRect.width), 62);
        }
        static void Place(VisualElement element, float x, float y, float width, float height)
        {
            element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height;
        }
        static void Path(Painter2D p, Rect r)
        {
            p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath();
        }
        static void Segment(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }
        void Draw(MeshGenerationContext context)
        {
            if (cafeLayout == null || contentRect.width < 1 || contentRect.height < 1) return;
            var current = preview ?? cafeLayout; var p = context.painter2D; Measure();
            p.fillColor = new Color(.77f, .85f, .92f, .35f); Path(p, new Rect(roomRect.x + 5, roomRect.y + 6, roomRect.width, roomRect.height)); p.Fill();
            p.fillColor = new Color(.83f, .92f, 1, .75f); Path(p, roomRect); p.Fill();
            float gridPixels = current.room.grid * pixelsPerMetre;
            while (gridPixels < 15) gridPixels *= 2;
            p.lineWidth = 1; p.strokeColor = new Color(.62f, .78f, .91f, .25f);
            for (float x = roomRect.center.x % gridPixels; x < contentRect.width; x += gridPixels) Segment(p, new Vector2(x, 0), new Vector2(x, contentRect.height));
            for (float y = roomRect.center.y % gridPixels; y < contentRect.height; y += gridPixels) Segment(p, new Vector2(0, y), new Vector2(contentRect.width, y));
            p.lineWidth = 2; p.strokeColor = Ink; Path(p, roomRect); p.Stroke();
            // Only the native v1 rear walls are solid walls; the remaining two edges are cafe bounds.
            p.lineWidth = 7; p.strokeColor = new Color(.36f, .42f, .48f);
            Segment(p, roomRect.min, new Vector2(roomRect.xMax, roomRect.yMin)); Segment(p, roomRect.min, new Vector2(roomRect.xMin, roomRect.yMax));
            if (walls)
            {
                p.strokeColor = Blue; p.lineWidth = 4;
                Segment(p, roomRect.min, selectedWall == 0 ? new Vector2(roomRect.xMin, roomRect.yMax) : new Vector2(roomRect.xMax, roomRect.yMin));
            }
            foreach (var instance in current.instances)
            {
                if (!items.TryGetValue(instance.furnitureId, out var item) || Placement.IsRoomStyle(item)) continue;
                float width = item.width * catalog.gridUnit, depth = item.depth * catalog.gridUnit;
                if ((instance.quarterTurns & 1) != 0) { float swap = width; width = depth; depth = swap; }
                Vector2 center = new Vector2(roomRect.center.x + instance.position.x * pixelsPerMetre, roomRect.center.y - instance.position.z * pixelsPerMetre);
                Rect footprint = new Rect(center.x - width * pixelsPerMetre / 2, center.y - depth * pixelsPerMetre / 2, width * pixelsPerMetre, depth * pixelsPerMetre);
                p.fillColor = item.placement == "wall" ? new Color(1, .70f, .26f, .80f) : new Color(.32f, .55f, .77f, .38f);
                if (!item.available) p.fillColor = new Color(.90f, .28f, .26f, .4f);
                Path(p, footprint); p.Fill(); p.strokeColor = item.placement == "wall" ? new Color(.65f, .38f, .09f) : new Color(.30f, .49f, .69f); p.lineWidth = 1; p.Stroke();
            }
            Color selection = validPreview ? Blue : new Color(.86f, .22f, .24f);
            if (!walls)
            {
                p.strokeColor = selection; p.lineWidth = 2.5f; Path(p, roomRect); p.Stroke();
                foreach (var point in Corners(roomRect))
                {
                    p.fillColor = Color.white; p.strokeColor = selection; p.lineWidth = 2.5f; Path(p, new Rect(point.x - 5, point.y - 5, 10, 10)); p.Fill(); p.Stroke();
                }
            }
            p.strokeColor = new Color(.23f, .39f, .69f); p.lineWidth = 1.2f;
            float bottom = roomRect.yMax + 25, left = roomRect.xMin - 26;
            Segment(p, new Vector2(roomRect.xMin, bottom), new Vector2(roomRect.xMax, bottom));
            Segment(p, new Vector2(left, roomRect.yMin), new Vector2(left, roomRect.yMax));
            foreach (float x in new[] { roomRect.xMin, roomRect.xMax }) Segment(p, new Vector2(x, bottom - 7), new Vector2(x, bottom + 7));
            foreach (float y in new[] { roomRect.yMin, roomRect.yMax }) Segment(p, new Vector2(left - 7, y), new Vector2(left + 7, y));
        }
        static Vector2[] Corners(Rect r) => new[] { r.min, new Vector2(r.xMax, r.yMin), r.max, new Vector2(r.xMin, r.yMax) };

        sealed class BoundsManipulator : PointerManipulator
        {
            readonly StudioRoomCanvas canvas;
            public BoundsManipulator(StudioRoomCanvas canvas) { this.canvas = canvas; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move); target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move); target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            void Down(PointerDownEvent e)
            {
                if (e.button != 0 || canvas.cafeLayout == null || canvas.corner >= 0 || canvas.CanInteract?.Invoke() == false) return;
                var point = (Vector2)e.localPosition; canvas.Focus(); canvas.Measure();
                if (canvas.walls)
                {
                    if (Mathf.Abs(point.x - canvas.roomRect.xMin) <= 12 && point.y >= canvas.roomRect.yMin - 12 && point.y <= canvas.roomRect.yMax + 12) canvas.WallSelected?.Invoke(0);
                    else if (Mathf.Abs(point.y - canvas.roomRect.yMin) <= 12 && point.x >= canvas.roomRect.xMin - 12 && point.x <= canvas.roomRect.xMax + 12) canvas.WallSelected?.Invoke(1);
                    e.StopPropagation(); return;
                }
                var corners = Corners(canvas.roomRect);
                for (int i = 0; i < corners.Length; i++) if (Vector2.Distance(point, corners[i]) <= 14) { canvas.corner = i; break; }
                if (canvas.corner < 0) return;
                canvas.pointerStart = point; canvas.gestureScale = canvas.pixelsPerMetre;
                canvas.initialWidth = canvas.cafeLayout.room.width; canvas.initialDepth = canvas.cafeLayout.room.depth; canvas.capturedPointer = e.pointerId;
                canvas.ResizeStarted?.Invoke(); target.CapturePointer(e.pointerId); e.StopPropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (canvas.corner < 0 || e.pointerId != canvas.capturedPointer) return;
                var delta = (Vector2)e.localPosition - canvas.pointerStart;
                bool right = canvas.corner == 1 || canvas.corner == 2, bottom = canvas.corner == 2 || canvas.corner == 3;
                float width = canvas.initialWidth + delta.x * (right ? 2 : -2) / canvas.gestureScale;
                float depth = canvas.initialDepth + delta.y * (bottom ? 2 : -2) / canvas.gestureScale;
                float grid = canvas.cafeLayout.room.grid;
                width = Mathf.Max(grid, Placement.Snap(width, grid)); depth = Mathf.Max(grid, Placement.Snap(depth, grid));
                canvas.ResizeChanged?.Invoke(width, depth, false); e.StopPropagation();
            }
            void Up(PointerUpEvent e)
            {
                if (canvas.corner < 0 || e.pointerId != canvas.capturedPointer) return;
                var candidate = canvas.preview; bool commit = candidate != null && canvas.validPreview && target.worldBound.Contains(e.position);
                canvas.CancelResize(); if (commit) canvas.ResizeChanged?.Invoke(candidate.room.width, candidate.room.depth, true);
                e.StopPropagation();
            }
            void Lost(PointerCaptureOutEvent e) { if (e.pointerId == canvas.capturedPointer) canvas.CancelResize(); }
        }
    }
}
