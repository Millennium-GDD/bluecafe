using System;
using System.Collections.Generic;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Position = BlueCafe.Core.Position;

namespace BlueCafe.App
{
    internal sealed class RoomPlanEdit
    {
        public string areaId, kind;
        public int corner;
        public float x, z, width, depth;
    }

    // The transform remains fixed throughout a gesture: furniture and the opposite
    // rectangle corner cannot drift as the edited boundary changes size.
    internal sealed class StudioRoomCanvas : VisualElement
    {
        public Action EditStarted;
        public Action<RoomPlanEdit, bool> EditChanged;
        public Action<string> AreaSelected;
        public Action<int> WallSelected;
        public Action ZoomChanged;
        public Func<bool> CanInteract;
        public float Zoom { get; private set; } = 1;
        public bool GestureActive => gesture;
        Layout cafeLayout, preview;
        readonly Dictionary<string, Item> items = new Dictionary<string, Item>();
        Catalog catalog;
        readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, VisualElement> furniturePreviews = new Dictionary<string, VisualElement>();
        readonly Label dimensionsLabel, titleLabel;
        float scale, gestureScale;
        Vector2 origin, gestureOrigin, pointerStart;
        Position[] startVertices;
        int corner = -1, capturedPointer, selectedWall;
        bool walls, validPreview = true, gesture, viewEstablished;
        string selectedArea;
        RoomPlanEdit lastEdit;
        static readonly Color Blue = new Color(.09f, .47f, .94f);
        static readonly Color Ink = new Color(.20f, .31f, .44f);
        static readonly string[] WallIds = { "left", "back", "right", "front" };

        public StudioRoomCanvas()
        {
            AddToClassList("room-plan-canvas"); focusable = true;
            dimensionsLabel = MakeLabel("room-plan-label"); titleLabel = MakeLabel("room-plan-title");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => { viewEstablished = false; CancelResize(); LayoutLabels(); });
            RegisterCallback<DetachFromPanelEvent>(_ => CancelResize());
            RegisterCallback<FocusOutEvent>(_ => CancelResize());
            RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape && GestureActive) { CancelResize(); e.StopPropagation(); } });
            this.AddManipulator(new PlanManipulator(this));
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
            if (selectedArea != null && !cafeLayout.areas.Any(a => a.id == selectedArea)) selectedArea = null;
            if (!gesture) preview = null;
            RebuildFurniturePreviews(); LayoutLabels(); MarkDirtyRepaint();
        }
        public void SetThumbnails(Dictionary<string, Texture2D> source)
        {
            thumbnails.Clear(); foreach (var entry in source) thumbnails[entry.Key] = entry.Value;
        }
        void RebuildFurniturePreviews()
        {
            foreach (var element in furniturePreviews.Values) element.RemoveFromHierarchy(); furniturePreviews.Clear();
            foreach (var instance in cafeLayout.instances.Take(512))
            {
                if (!items.TryGetValue(instance.furnitureId, out var item) || Placement.IsRoomStyle(item)) continue;
                thumbnails.TryGetValue(item.id, out var texture);
                VisualElement element = texture ? (VisualElement)new Image { image = texture, scaleMode = ScaleMode.ScaleToFit } : new Label("No preview");
                element.name = "roomFurniture_" + instance.instanceId; element.AddToClassList("room-furniture-preview");
                if (!texture) element.AddToClassList("room-furniture-missing");
                element.tooltip = item.name + " · " + item.id + (item.available ? "" : " · Unavailable") + (texture ? "" : thumbnails.ContainsKey(item.id) ? " · Original thumbnail unavailable" : " · Preview cache limit");
                Insert(0, element); furniturePreviews[instance.instanceId] = element;
            }
            LayoutFurniturePreviews();
        }
        void LayoutFurniturePreviews()
        {
            if (cafeLayout == null || contentRect.width < 1 || contentRect.height < 1) return;
            Measure(); var current = preview ?? cafeLayout;
            foreach (var instance in current.instances)
            {
                if (!furniturePreviews.TryGetValue(instance.instanceId, out var element) || !items.TryGetValue(instance.furnitureId, out var item)) continue;
                float w = ((instance.quarterTurns & 1) == 0 ? item.width : item.depth) * catalog.gridUnit * scale;
                float d = ((instance.quarterTurns & 1) == 0 ? item.depth : item.width) * catalog.gridUnit * scale;
                var center = Screen(instance.position);
                // Preserve footprint information below the image, including wall placement and unavailable status.
                float width = Mathf.Max(0, w - 4), height = Mathf.Max(0, d - 4);
                element.style.left = center.x - width / 2; element.style.top = center.y - height / 2;
                element.style.width = width; element.style.height = height;
            }
        }
        public void SetTarget(string id) { CancelResize(); selectedArea = id; LayoutLabels(); MarkDirtyRepaint(); }
        public void SetPreview(Layout next, bool valid) { preview = Copies.Copy(next); validPreview = valid; LayoutLabels(); MarkDirtyRepaint(); }
        public void SetWallMode(bool value, int selected) { walls = value; selectedWall = selected; MarkDirtyRepaint(); }
        public void SetZoom(float zoom) { CancelResize(); viewEstablished = false; Zoom = Mathf.Clamp(zoom, .35f, 3); LayoutLabels(); MarkDirtyRepaint(); ZoomChanged?.Invoke(); }
        public void CancelResize()
        {
            gesture = false; corner = -1; preview = null; lastEdit = null; validPreview = true;
            if (this.HasPointerCapture(capturedPointer)) this.ReleasePointer(capturedPointer);
            LayoutLabels(); MarkDirtyRepaint();
        }
        public static Position[] Extent(Room room) => Geometry.Rectangle(room.centerX - room.width / 2, room.centerZ - room.depth / 2, room.centerX + room.width / 2, room.centerZ + room.depth / 2);
        Position[] Vertices(Layout layout) => selectedArea == null ? Extent(layout.room) : layout.areas.FirstOrDefault(a => a.id == selectedArea)?.vertices ?? Extent(layout.room);
        void Measure()
        {
            if (cafeLayout == null) return;
            if (gesture) { scale = gestureScale; origin = gestureOrigin; return; }
            if (viewEstablished) return;
            float minX = cafeLayout.room.centerX - cafeLayout.room.width / 2, maxX = minX + cafeLayout.room.width;
            float minZ = cafeLayout.room.centerZ - cafeLayout.room.depth / 2, maxZ = minZ + cafeLayout.room.depth;
            // Include existing footprints so moving the extent never hides stranded furniture.
            foreach (var instance in cafeLayout.instances)
            {
                if (!items.TryGetValue(instance.furnitureId, out var item) || Placement.IsRoomStyle(item)) continue;
                float w = ((instance.quarterTurns & 1) == 0 ? item.width : item.depth) * catalog.gridUnit;
                float d = ((instance.quarterTurns & 1) == 0 ? item.depth : item.width) * catalog.gridUnit;
                minX = Mathf.Min(minX, instance.position.x - w / 2); maxX = Mathf.Max(maxX, instance.position.x + w / 2);
                minZ = Mathf.Min(minZ, instance.position.z - d / 2); maxZ = Mathf.Max(maxZ, instance.position.z + d / 2);
            }
            scale = Mathf.Min(Mathf.Max(20, contentRect.width - 140) / (maxX - minX), Mathf.Max(20, contentRect.height - 110) / (maxZ - minZ)) * Zoom;
            origin = new Vector2(contentRect.width / 2 - (minX + maxX) / 2 * scale, contentRect.height / 2 + (minZ + maxZ) / 2 * scale);
            viewEstablished = true;
        }
        Vector2 Screen(Position point) => origin + new Vector2(point.x, -point.z) * scale;
        Position World(Vector2 point) => new Position((point.x - origin.x) / scale, 0, -(point.y - origin.y) / scale);
        void LayoutLabels()
        {
            if (cafeLayout == null) return; Measure(); var current = preview ?? cafeLayout; var vertices = Vertices(current);
            float width = vertices.Max(v => v.x) - vertices.Min(v => v.x), depth = vertices.Max(v => v.z) - vertices.Min(v => v.z);
            dimensionsLabel.text = width.ToString("0.##") + " × " + depth.ToString("0.##") + " m  ·  snap " + (current.room.grid / 2).ToString("0.##") + " m";
            titleLabel.text = selectedArea == null ? "Cafe extent & enclosing walls" : current.areas.FirstOrDefault(a => a.id == selectedArea)?.name ?? "Named area";
            LayoutFurniturePreviews();
        }
        void Path(Painter2D p, Vector2[] points)
        {
            points = CanvasClip.Polygon(new Rect(3, 3, Mathf.Max(0, contentRect.width - 6), Mathf.Max(0, contentRect.height - 6)), points);
            p.BeginPath(); if (points.Length == 0) return; p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.ClosePath();
        }
        void Segment(Painter2D p, Vector2 a, Vector2 b)
        {
            if (!CanvasClip.Line(new Rect(3, 3, Mathf.Max(0, contentRect.width - 6), Mathf.Max(0, contentRect.height - 6)), ref a, ref b)) return;
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public Vector2 ProbePanelPoint(Position position) { Measure(); return this.LocalToWorld(Screen(position)); }
        public Layout ProbePreview => Copies.Copy(preview);
        public bool ProbePreviewValid => validPreview;
#endif
        void Draw(MeshGenerationContext context)
        {
            if (cafeLayout == null || contentRect.width < 1 || contentRect.height < 1) return;
            var current = preview ?? cafeLayout; var p = context.painter2D; Measure();
            float gridPixels = current.room.grid / 2 * scale; while (gridPixels < 12) gridPixels *= 2;
            p.lineWidth = 1; p.strokeColor = new Color(.62f, .78f, .91f, .25f);
            for (float x = origin.x % gridPixels; x < contentRect.width; x += gridPixels) Segment(p, new Vector2(x, 0), new Vector2(x, contentRect.height));
            for (float y = origin.y % gridPixels; y < contentRect.height; y += gridPixels) Segment(p, new Vector2(0, y), new Vector2(contentRect.width, y));
            p.fillColor = new Color(.83f, .92f, 1, .6f); p.strokeColor = Ink; p.lineWidth = 2; Path(p, Extent(current.room).Select(Screen).ToArray()); p.Fill(); p.Stroke();
            foreach (var area in current.areas)
            {
                Color color; if (!ColorUtility.TryParseHtmlString(area.color, out color)) color = Blue;
                color.a = .16f; p.fillColor = color; color.a = .65f; p.strokeColor = color; p.lineWidth = 2;
                Path(p, area.vertices.Select(Screen).ToArray()); p.Fill(FillRule.OddEven); p.Stroke();
            }
            for (int i = 0; i < WallIds.Length; i++)
            {
                p.strokeColor = walls && selectedWall == i ? Blue : new Color(.36f, .42f, .48f); p.lineWidth = walls && selectedWall == i ? 6 : 4;
                Segment(p, Screen(Geometry.WallPosition(current.room, WallIds[i], 0, 0)), Screen(Geometry.WallPosition(current.room, WallIds[i], Geometry.WallLength(current.room, WallIds[i]), 0)));
            }
            foreach (var instance in current.instances)
            {
                if (!items.TryGetValue(instance.furnitureId, out var item) || Placement.IsRoomStyle(item)) continue;
                float w = item.width * catalog.gridUnit, d = item.depth * catalog.gridUnit;
                if ((instance.quarterTurns & 1) != 0) { float swap = w; w = d; d = swap; }
                p.fillColor = item.placement == "wall" ? new Color(1, .70f, .26f, .8f) : new Color(.32f, .55f, .77f, .38f);
                if (!item.available) p.fillColor = new Color(.90f, .28f, .26f, .4f);
                Path(p, Geometry.Rectangle(instance.position.x - w / 2, instance.position.z - d / 2, instance.position.x + w / 2, instance.position.z + d / 2).Select(Screen).ToArray());
                p.Fill(); p.strokeColor = item.placement == "wall" ? new Color(.65f, .38f, .09f) : new Color(.30f, .49f, .69f); p.lineWidth = 1; p.Stroke();
            }
            if (!walls)
            {
                var points = Vertices(current).Select(Screen).ToArray(); var color = validPreview ? Blue : new Color(.86f, .22f, .24f);
                p.strokeColor = color; p.lineWidth = 2.5f; Path(p, points); p.Stroke();
                foreach (var point in points)
                {
                    p.fillColor = Color.white; p.strokeColor = color; p.lineWidth = 2.5f;
                    Path(p, new[] { point + new Vector2(-5, -5), point + new Vector2(5, -5), point + new Vector2(5, 5), point + new Vector2(-5, 5) }); p.Fill(); p.Stroke();
                }
            }
        }
        static bool Contains(Position[] vertices, Position point)
        {
            bool inside = false;
            for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
                if ((vertices[i].z > point.z) != (vertices[j].z > point.z) && point.x < (vertices[j].x - vertices[i].x) * (point.z - vertices[i].z) / (vertices[j].z - vertices[i].z) + vertices[i].x) inside = !inside;
            return inside;
        }
        static float Distance(Vector2 point, Vector2 a, Vector2 b) => Vector2.Distance(point, a + (b - a) * Mathf.Clamp01(Vector2.Dot(point - a, b - a) / (b - a).sqrMagnitude));
        sealed class PlanManipulator : PointerManipulator
        {
            readonly StudioRoomCanvas canvas;
            public PlanManipulator(StudioRoomCanvas canvas) { this.canvas = canvas; }
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
                if (e.button != 0 || canvas.cafeLayout == null || canvas.gesture || canvas.CanInteract?.Invoke() == false) return;
                var point = (Vector2)e.localPosition; canvas.Focus(); canvas.Measure();
                if (canvas.walls)
                {
                    for (int i = 0; i < WallIds.Length; i++) if (Distance(point, canvas.Screen(Geometry.WallPosition(canvas.cafeLayout.room, WallIds[i], 0, 0)), canvas.Screen(Geometry.WallPosition(canvas.cafeLayout.room, WallIds[i], Geometry.WallLength(canvas.cafeLayout.room, WallIds[i]), 0))) <= 12) { canvas.WallSelected?.Invoke(i); break; }
                    e.StopPropagation(); return;
                }
                canvas.startVertices = canvas.Vertices(canvas.cafeLayout).ToArray(); canvas.corner = -1;
                for (int i = 0; i < canvas.startVertices.Length; i++) if (Vector2.Distance(point, canvas.Screen(canvas.startVertices[i])) <= 14) { canvas.corner = i; break; }
                if (canvas.corner < 0 && !Contains(canvas.startVertices, canvas.World(point)))
                {
                    var area = canvas.cafeLayout.areas.LastOrDefault(a => Contains(a.vertices, canvas.World(point)));
                    if (area == null) return; canvas.selectedArea = area.id; canvas.AreaSelected?.Invoke(area.id); canvas.startVertices = area.vertices.ToArray();
                }
                canvas.pointerStart = point; canvas.gestureScale = canvas.scale; canvas.gestureOrigin = canvas.origin; canvas.capturedPointer = e.pointerId;
                canvas.gesture = true; canvas.lastEdit = null; canvas.EditStarted?.Invoke(); target.CapturePointer(e.pointerId); e.StopPropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (!canvas.gesture || e.pointerId != canvas.capturedPointer) return;
                var delta = (Vector2)e.localPosition - canvas.pointerStart;
                if (delta.sqrMagnitude < 9 && canvas.lastEdit == null) return;
                float grid = canvas.cafeLayout.room.grid / 2;
                float dx = Placement.Snap(delta.x / canvas.gestureScale, grid), dz = Placement.Snap(-delta.y / canvas.gestureScale, grid);
                var edit = new RoomPlanEdit { areaId = canvas.selectedArea, corner = canvas.corner };
                if (canvas.corner < 0)
                {
                    edit.kind = "move"; edit.x = dx; edit.z = dz;
                    if (edit.areaId == null) { edit.x += canvas.cafeLayout.room.centerX; edit.z += canvas.cafeLayout.room.centerZ; edit.width = canvas.cafeLayout.room.width; edit.depth = canvas.cafeLayout.room.depth; }
                }
                else
                {
                    var start = canvas.startVertices[canvas.corner]; edit.x = Placement.Snap(start.x + dx, grid); edit.z = Placement.Snap(start.z + dz, grid);
                    edit.kind = Geometry.IsRectangle(canvas.startVertices) ? "corner" : "vertex";
                    if (edit.areaId == null)
                    {
                        var opposite = canvas.startVertices[(canvas.corner + 2) % 4];
                        // Crossing the anchor is invalid, rather than silently flipping the rectangle.
                        edit.width = (edit.x - opposite.x) * Mathf.Sign(start.x - opposite.x); edit.depth = (edit.z - opposite.z) * Mathf.Sign(start.z - opposite.z);
                        edit.x = (edit.x + opposite.x) / 2; edit.z = (edit.z + opposite.z) / 2;
                    }
                }
                canvas.lastEdit = edit; canvas.EditChanged?.Invoke(edit, false); e.StopPropagation();
            }
            void Up(PointerUpEvent e)
            {
                if (!canvas.gesture || e.pointerId != canvas.capturedPointer) return;
                var edit = canvas.lastEdit; bool commit = edit != null && canvas.validPreview && target.worldBound.Contains(e.position);
                canvas.CancelResize(); if (commit) canvas.EditChanged?.Invoke(edit, true); e.StopPropagation();
            }
            void Lost(PointerCaptureOutEvent e) { if (e.pointerId == canvas.capturedPointer) canvas.CancelResize(); }
        }
    }
}
