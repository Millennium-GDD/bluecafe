using System;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Position = BlueCafe.Core.Position;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        StudioRoomCanvas roomCanvas;
        VisualElement roomInspector;
        Button areasMode, wallsMode;
        Label roomZoom, roomFeedback;
        bool editingWalls;
        int selectedRearWall;
        string selectedRoomArea;
        long roomGestureRevision;
        Layout roomGestureLayout;
        Session roomPreviewSession;
        static readonly string[] RoomWallIds = { "left", "back", "right", "front" };

        void RoomsPage()
        {
            var row = Box(workspace, "screen-row"); row.styleSheets.Add(Resources.Load<StyleSheet>("Rooms"));
            var stage = Box(row, "plan-stage"); var bar = Box(stage, "rooms-heading-row"); Label(bar, "Rooms", "heading");
            var modes = Box(bar, "rooms-mode-switch");
            areasMode = Button(modes, "Areas", () => SetRoomMode(false), "rooms-mode");
            wallsMode = Button(modes, "Walls", () => SetRoomMode(true), "rooms-mode");
            var tools = Box(stage, "rooms-tools");
            Tool(tools, "Select / move", "select", () => roomCanvas.Focus(), "active");
            Tool(tools, "Cafe extent", "boundary", () => { selectedRoomArea = null; SetRoomMode(false); roomCanvas.SetTarget(null); }).tooltip = "Move or resize the enclosing cafe and its walls. Furniture stays fixed.";
            roomCanvas = new StudioRoomCanvas { name = "roomsPlan" }; stage.Add(roomCanvas);
            roomCanvas.CanInteract = () => modal == null;
            roomCanvas.EditStarted = () => { roomGestureRevision = Session.Revision; roomGestureLayout = Session.Layout; roomPreviewSession = new Session(Pack.Catalog, roomGestureLayout); };
            roomCanvas.EditChanged = EditRoomPlan;
            roomCanvas.AreaSelected = id => { selectedRoomArea = id; RefreshRoomInspector(); };
            roomCanvas.ZoomChanged = UpdateRoomZoom;
            roomCanvas.WallSelected = side => { selectedRearWall = side; SetRoomMode(true); };
            var controls = Box(stage, "rooms-plan-controls");
            Label(controls, "Furniture previews · hover for name · drag area/handles · Esc cancels", "rooms-plan-key"); Box(controls, "spacer");
            IconButton(controls, "frame", () => { roomCanvas.SetZoom(1); UpdateRoomZoom(); }, "Fit cafe bounds");
            IconButton(controls, "zoom-out", () => { roomCanvas.SetZoom(roomCanvas.Zoom / 1.15f); UpdateRoomZoom(); }, "Zoom out");
            roomZoom = Label(controls, "100%", "rooms-zoom-label");
            IconButton(controls, "zoom-in", () => { roomCanvas.SetZoom(roomCanvas.Zoom * 1.15f); UpdateRoomZoom(); }, "Zoom in");
            var scroll = new ScrollView(); scroll.AddToClassList("rooms-inspector-scroll"); row.Add(scroll);
            roomInspector = Box(scroll, "rooms-inspector"); RefreshRooms(); SetRoomMode(editingWalls);
        }
        void SetRoomMode(bool walls)
        {
            editingWalls = walls; roomCanvas?.CancelResize();
            areasMode?.EnableInClassList("active", !walls); wallsMode?.EnableInClassList("active", walls);
            roomCanvas?.SetWallMode(walls, selectedRearWall); RefreshRoomInspector();
        }
        void RefreshRooms()
        {
            if (screen != "Rooms" || roomCanvas == null || Session == null) return;
            if (selectedRoomArea != null && !Session.Layout.areas.Any(a => a.id == selectedRoomArea)) selectedRoomArea = null;
            var layout = Session.Layout;
            var thumbnailIds = layout.instances.Select(i => i.furnitureId).Distinct().Take(180).ToArray();
            BeginThumbnailScope(thumbnailIds);
            roomCanvas.SetThumbnails(thumbnailIds.Select(BuildItem).Where(i => i != null).ToDictionary(i => i.id, i => Thumbnail(i)));
            roomCanvas.SetLayout(layout, Pack.Catalog); roomCanvas.SetTarget(selectedRoomArea);
            roomCanvas.SetWallMode(editingWalls, selectedRearWall); RefreshRoomInspector();
        }
        void CancelRoomsGesture() { roomCanvas?.CancelResize(); }
        void UpdateRoomZoom() { if (roomZoom != null) roomZoom.text = Mathf.RoundToInt(roomCanvas.Zoom * 100) + "%"; }
        CommandResult ExecuteRoomEdit(Session session, RoomPlanEdit edit, long? revision = null)
        {
            if (edit.areaId == null) return session.SetExtent(edit.x, edit.z, edit.width, edit.depth, revision);
            if (edit.kind == "move") return session.TranslateArea(edit.areaId, edit.x, edit.z, revision);
            if (edit.kind == "corner") return session.ResizeAreaCorner(edit.areaId, edit.corner, edit.x, edit.z, revision);
            return session.SetAreaVertex(edit.areaId, edit.corner, edit.x, edit.z, revision);
        }
        void RoomFeedback(string text, bool invalid = false)
        {
            if (roomFeedback != null) { roomFeedback.text = text; roomFeedback.EnableInClassList("rooms-error", invalid); }
            Message(text);
        }
        void EditRoomPlan(RoomPlanEdit edit, bool commit)
        {
            if (Session.Revision != roomGestureRevision) { roomCanvas.CancelResize(); RoomFeedback("The cafe changed during this gesture. Try again.", true); return; }
            if (commit)
            {
                var result = ExecuteRoomEdit(Session, edit, roomGestureRevision); Apply(result, false);
                roomCanvas.SetLayout(Session.Layout, Pack.Catalog); RoomFeedback(result.success ? "Room updated. Furniture stays in place." : result.message, !result.success); return;
            }
            roomPreviewSession.OpenDocument(roomGestureLayout); var check = ExecuteRoomEdit(roomPreviewSession, edit);
            roomCanvas.SetPreview(check.success ? roomPreviewSession.Layout : RoomRawPreview(roomGestureLayout, edit), check.success);
            RoomFeedback(check.success ? "Release to apply. Furniture stays in place; Esc cancels." : check.message + " Release keeps the current cafe.", !check.success);
        }
        // Rejected geometry remains visible in red without ever entering the live Session.
        static Layout RoomRawPreview(Layout original, RoomPlanEdit edit)
        {
            var result = Copies.Copy(original);
            if (edit.areaId == null) { result.room.centerX = edit.x; result.room.centerZ = edit.z; result.room.width = edit.width; result.room.depth = edit.depth; return result; }
            var area = result.areas.First(a => a.id == edit.areaId);
            if (edit.kind == "move") { for (int i = 0; i < area.vertices.Length; i++) { var p = area.vertices[i]; p.x += edit.x; p.z += edit.z; area.vertices[i] = p; } }
            else if (edit.kind == "corner")
            {
                var old = area.vertices[edit.corner];
                for (int i = 0; i < area.vertices.Length; i++) { var p = area.vertices[i]; if (Mathf.Abs(p.x - old.x) < .0001f) p.x = edit.x; if (Mathf.Abs(p.z - old.z) < .0001f) p.z = edit.z; area.vertices[i] = p; }
            }
            else area.vertices[edit.corner] = new Position(edit.x, 0, edit.z);
            return result;
        }
        void ApplyRoomPrecision(Func<CommandResult> edit)
        {
            CancelRoomsGesture(); var result = edit(); Apply(result, false); RoomFeedback(result.success ? "Room updated. Furniture stays in place." : result.message, !result.success);
        }
        FloatField RoomNumber(VisualElement parent, string label, string name, float value)
        {
            var field = new FloatField(label) { name = name, value = value, isDelayed = true }; field.AddToClassList("rooms-dimension-field"); parent.Add(field); return field;
        }
        void RefreshRoomInspector()
        {
            if (screen != "Rooms" || roomInspector == null || Session == null) return;
            roomInspector.Clear(); var layout = Session.Layout; var room = layout.room;
            Section(roomInspector, editingWalls ? "Enclosing walls" : "Edit boundary");
            if (!editingWalls)
            {
                var extent = Button(roomInspector, "Cafe extent & walls", () => { selectedRoomArea = null; roomCanvas.SetTarget(null); RefreshRoomInspector(); }, "room-wall-row"); extent.name = "roomsExtent"; extent.EnableInClassList("active", selectedRoomArea == null);
                foreach (var area in layout.areas)
                {
                    var id = area.id; var button = Button(roomInspector, area.name + "  ·  named area", () => { selectedRoomArea = id; roomCanvas.SetTarget(id); RefreshRoomInspector(); }, "room-wall-row"); button.name = "roomArea_" + id; button.EnableInClassList("active", selectedRoomArea == id);
                }
                if (layout.areas.Count == 0)
                {
                    var shapes = StudioDropdown.Create<string>(new System.Collections.Generic.List<string> { "Rectangle", "L-shaped", "Diagonal" }, 0); shapes.name = "roomsNewAreaShape"; roomInspector.Add(shapes);
                    Button(roomInspector, "Add named area", () => AddRoomArea(shapes.value), "rooms-apply");
                }
                var selected = layout.areas.FirstOrDefault(a => a.id == selectedRoomArea);
                if (selected == null) DrawExtentInspector(roomInspector, layout);
                else DrawAreaInspector(roomInspector, selected, room.grid / 2);
            }
            else
            {
                for (var side = 0; side < RoomWallIds.Length; side++)
                {
                    int current = side; var id = RoomWallIds[side];
                    var button = Button(roomInspector, id + " wall  ·  " + Geometry.WallLength(room, id).ToString("0.##") + " m", () => { selectedRearWall = current; roomCanvas.SetWallMode(true, current); RefreshRoomInspector(); }, "room-wall-row"); button.EnableInClassList("active", side == selectedRearWall);
                }
                var wall = Section(roomInspector, "Selected wall"); string wallId = RoomWallIds[selectedRearWall];
                Label(wall, "Length " + Geometry.WallLength(room, wallId).ToString("0.##") + " m · Height " + room.wallHeight.ToString("0.##") + " m", "rooms-wall-details");
                int attached = layout.instances.Count(i => i.wallId == wallId);
                Label(wall, attached + " attached decorations", "rooms-note");
                Button(wall, "Edit enclosing cafe extent", () => { selectedRoomArea = null; roomCanvas.SetTarget(null); SetRoomMode(false); }, "rooms-apply");
                Label(wall, "Extent edits move these walls. An attached decoration blocks a change that would detach it. Move decorations in Build.", "rooms-note");
            }
            roomFeedback = Label(roomInspector, "", "rooms-note"); roomFeedback.name = "roomsFeedback";
            Section(roomInspector, "Whole cafe Interior"); DrawInterior(roomInspector, layout, true);
        }
        void AddRoomArea(string shape)
        {
            var layout = Session.Layout; var room = layout.room; float grid = room.grid / 2;
            float minX = Placement.Snap(room.centerX - room.width / 4, grid), maxX = Placement.Snap(room.centerX + room.width / 4, grid);
            float minZ = Placement.Snap(room.centerZ - room.depth / 4, grid), maxZ = Placement.Snap(room.centerZ + room.depth / 4, grid);
            float middleX = Placement.Snap((minX + maxX) / 2, grid), middleZ = Placement.Snap((minZ + maxZ) / 2, grid);
            var vertices = Geometry.Rectangle(minX, minZ, maxX, maxZ);
            if (shape == "L-shaped") vertices = new[] { new Position(minX, 0, minZ), new Position(maxX, 0, minZ), new Position(maxX, 0, middleZ), new Position(middleX, 0, middleZ), new Position(middleX, 0, maxZ), new Position(minX, 0, maxZ) };
            else if (shape == "Diagonal") vertices = new[] { new Position(minX, 0, minZ), new Position(maxX, 0, minZ), new Position(maxX, 0, middleZ), new Position(middleX, 0, maxZ), new Position(minX, 0, maxZ) };
            var area = new Area { id = Guid.NewGuid().ToString("N"), name = shape + " area", color = "#80baff", vertices = vertices };
            var result = Session.SetAreas(layout.areas.Concat(new[] { area }).ToArray());
            if (result.success) selectedRoomArea = area.id;
            Apply(result, false); RoomFeedback(result.success ? "Named area added. Drag it or edit its handles." : result.message, !result.success);
        }
        void DrawExtentInspector(VisualElement parent, Layout layout)
        {
            var room = layout.room; var details = Section(parent, "Cafe extent");
            Label(details, "Moves or resizes the enclosure and its walls. A named area matching the enclosure follows it. Furniture remains fixed.", "rooms-note");
            var coordinates = Box(details, "rooms-dimensions");
            var x = RoomNumber(coordinates, "Center X", "roomsCenterX", room.centerX); var z = RoomNumber(coordinates, "Center Z", "roomsCenterZ", room.centerZ);
            var dimensions = Box(details, "rooms-dimensions");
            var width = RoomNumber(dimensions, "Width", "roomsWidth", room.width); var depth = RoomNumber(dimensions, "Depth", "roomsDepth", room.depth);
            Button(details, "Apply extent", () => ApplyRoomPrecision(() => Session.SetExtent(x.value, z.value, width.value, depth.value)), "rooms-apply");
            var fit = Button(details, "Fit extent to furniture", () => FitRoomToFurniture(), "rooms-apply");
            fit.SetEnabled(layout.instances.Any(i => Pack.Catalog.items.Any(item => item.id == i.furnitureId && item.placement == "floor")));
            Label(details, "Corner handles keep the opposite corner fixed. Bounds snap to " + (room.grid / 2).ToString("0.##") + " m. Fit adds no margin when footprint edges lie on this grid.", "rooms-note");
        }
        void FitRoomToFurniture()
        {
            var layout = Session.Layout; float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            foreach (var instance in layout.instances)
            {
                var item = Pack.Catalog.items.First(p => p.id == instance.furnitureId); if (item.placement != "floor") continue;
                float width = item.width * Pack.Catalog.gridUnit, depth = item.depth * Pack.Catalog.gridUnit; if ((instance.quarterTurns & 1) != 0) { float swap = width; width = depth; depth = swap; }
                minX = Mathf.Min(minX, instance.position.x - width / 2); maxX = Mathf.Max(maxX, instance.position.x + width / 2);
                minZ = Mathf.Min(minZ, instance.position.z - depth / 2); maxZ = Mathf.Max(maxZ, instance.position.z + depth / 2);
            }
            if (float.IsInfinity(minX)) { RoomFeedback("Place floor furniture before fitting the cafe.", true); return; }
            float grid = layout.room.grid / 2;
            minX = Mathf.Floor((minX + .00001f) / grid) * grid; minZ = Mathf.Floor((minZ + .00001f) / grid) * grid;
            maxX = Mathf.Ceil((maxX - .00001f) / grid) * grid; maxZ = Mathf.Ceil((maxZ - .00001f) / grid) * grid;
            ApplyRoomPrecision(() => Session.SetExtent((minX + maxX) / 2, (minZ + maxZ) / 2, maxX - minX, maxZ - minZ));
        }
        PopupField<string> AreaStyleChoice(VisualElement parent, string label, string kind, string selected)
        {
            Label(parent, label, "rooms-field-caption");
            var choices = Pack.Catalog.items.Where(i => i.placement == kind).OrderBy(i => i.name).Select(i => i.id).ToList(); choices.Insert(0, "");
            selected = selected ?? ""; if (!choices.Contains(selected)) choices.Add(selected);
            var field = StudioDropdown.Create<string>(choices, choices.IndexOf(selected)); field.name = "roomArea" + label;
            string Display(string id) { if (string.IsNullOrEmpty(id)) return "Use whole cafe Interior"; var item = BuildItem(id); return item == null ? id + " · Unavailable" : item.name + (item.available ? "" : " · Unavailable"); }
            field.formatListItemCallback = Display; field.formatSelectedValueCallback = Display; parent.Add(field); return field;
        }
        void DrawAreaInspector(VisualElement parent, Area area, float grid)
        {
            var details = Section(parent, area.name); Label(details, "Named area only. Furniture and physical walls stay fixed. Snap " + grid.ToString("0.##") + " m.", "rooms-note");
            var name = new TextField("Name") { name = "roomAreaName", value = area.name, isDelayed = true }; name.AddToClassList("rooms-property-field"); details.Add(name);
            Button(details, "Rename area", () => ApplyRoomPrecision(() =>
            {
                var next = Session.Layout.areas.ToArray(); next.First(a => a.id == area.id).name = name.value.Trim();
                return Session.SetAreas(next);
            }), "rooms-apply").name = "roomAreaRename";
            Button(details, "Delete area", () =>
            {
                CancelRoomsGesture(); var result = Session.SetAreas(Session.Layout.areas.Where(a => a.id != area.id).ToArray());
                if (result.success) selectedRoomArea = null;
                Apply(result, false); RoomFeedback(result.success ? "Named area deleted. Furniture and enclosing walls stay in place. Undo restores the area." : result.message, !result.success);
            }, "rooms-apply").name = "roomAreaDelete";
            var color = new TextField("Plan color") { name = "roomAreaColor", value = string.IsNullOrEmpty(area.color) ? "#80baff" : area.color, isDelayed = true }; color.AddToClassList("rooms-property-field"); details.Add(color);
            Label(details, "Area Interior", "rooms-field-caption");
            var flooring = AreaStyleChoice(details, "Flooring", "flooring", area.flooringId);
            var wallpaper = AreaStyleChoice(details, "Wallpaper", "wallpaper", area.wallpaperId);
            Button(details, "Apply area properties", () =>
            {
                if (!ColorUtility.TryParseHtmlString(color.value, out _)) { RoomFeedback("Use a valid HTML color, for example #80baff.", true); return; }
                foreach (var id in new[] { flooring.value, wallpaper.value })
                    if (!string.IsNullOrEmpty(id) && id != area.flooringId && id != area.wallpaperId && !BuildItem(id).available) { RoomFeedback("This original Interior asset is unavailable. Relink its source pack first.", true); return; }
                ApplyRoomPrecision(() =>
                {
                    var next = Session.Layout.areas.ToArray(); var current = next.First(a => a.id == area.id);
                    current.name = name.value; current.color = color.value; current.flooringId = flooring.value; current.wallpaperId = wallpaper.value;
                    return Session.SetAreas(next);
                });
            }, "rooms-apply");
            var origin = Box(details, "rooms-dimensions"); float minX = area.vertices.Min(p => p.x), minZ = area.vertices.Min(p => p.z);
            var x = RoomNumber(origin, "Minimum X", "areaMinX", minX); var z = RoomNumber(origin, "Minimum Z", "areaMinZ", minZ);
            Button(details, "Move area", () => ApplyRoomPrecision(() => Session.TranslateArea(area.id, x.value - minX, z.value - minZ)), "rooms-apply");
            bool rectangle = Geometry.IsRectangle(area.vertices);
            Label(details, rectangle ? "Drag any corner to resize both dimensions; its opposite corner stays fixed." : "Polygon handles move one vertex. Other vertices stay fixed; crossed edges and overlaps are blocked.", "rooms-note");
            for (int i = 0; i < area.vertices.Length; i++)
            {
                int index = i; var vertex = area.vertices[i]; var row = Box(details, "rooms-dimensions");
                var vx = RoomNumber(row, "Point " + (i + 1) + " X", "areaVertexX" + i, vertex.x); var vz = RoomNumber(row, "Z", "areaVertexZ" + i, vertex.z);
                Button(details, rectangle ? "Resize corner " + (i + 1) : "Apply point " + (i + 1), () => ApplyRoomPrecision(() => rectangle ? Session.ResizeAreaCorner(area.id, index, vx.value, vz.value) : Session.SetAreaVertex(area.id, index, vx.value, vz.value)), "rooms-apply");
            }
        }
    }
}
