using System;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        StudioRoomCanvas roomCanvas;
        VisualElement roomInspector;
        Button areasMode, wallsMode;
        Label roomZoom;
        bool editingWalls;
        int selectedRearWall;
        long roomGestureRevision;

        void RoomsPage()
        {
            var row = Box(workspace, "screen-row");
            row.styleSheets.Add(Resources.Load<StyleSheet>("Rooms"));
            var stage = Box(row, "plan-stage");
            var bar = Box(stage, "rooms-heading-row");
            Label(bar, "Rooms", "heading");
            var modes = Box(bar, "rooms-mode-switch");
            areasMode = Button(modes, "Areas", () => SetRoomMode(false), "rooms-mode");
            wallsMode = Button(modes, "Walls", () => SetRoomMode(true), "rooms-mode");
            var tools = Box(stage, "rooms-tools");
            Tool(tools, "Select", "select", () => roomCanvas.Focus(), "active");
            var boundary = Tool(tools, "Edit bounds", "boundary", () => SetRoomMode(false));
            boundary.tooltip = "Resize the rectangular cafe using its corners or the inspector.";
            foreach (var entry in new[] { ("Draw area", "rectangle"), ("Free draw", "pen"), ("Split", "split"), ("Merge", "merge"), ("Draw wall", "wall"), ("Opening", "opening") })
            {
                var button = Tool(tools, entry.Item1, entry.Item2, () => { });
                button.SetEnabled(false); button.tooltip = "Polygon areas and wall drawing are not available yet.";
            }
            roomCanvas = new StudioRoomCanvas(); stage.Add(roomCanvas);
            roomCanvas.CanInteract = () => !busy && modal == null;
            roomCanvas.ResizeStarted = () => roomGestureRevision = Session.Revision;
            roomCanvas.ResizeChanged = ResizeRoom;
            roomCanvas.ZoomChanged = UpdateRoomZoom;
            roomCanvas.WallSelected = side => { selectedRearWall = side; SetRoomMode(true); };
            var controls = Box(stage, "rooms-plan-controls");
            Label(controls, "Floor footprints · gold marks show attached decorations", "rooms-plan-key");
            Box(controls, "spacer");
            IconButton(controls, "frame", () => { roomCanvas.SetZoom(1); UpdateRoomZoom(); }, "Fit cafe bounds");
            IconButton(controls, "zoom-out", () => { roomCanvas.SetZoom(roomCanvas.Zoom / 1.15f); UpdateRoomZoom(); }, "Zoom out");
            roomZoom = Label(controls, "100%", "rooms-zoom-label");
            IconButton(controls, "zoom-in", () => { roomCanvas.SetZoom(roomCanvas.Zoom * 1.15f); UpdateRoomZoom(); }, "Zoom in");
            var scroll = new ScrollView(); scroll.AddToClassList("rooms-inspector-scroll"); row.Add(scroll);
            roomInspector = Box(scroll, "rooms-inspector");
            RefreshRooms(); SetRoomMode(editingWalls);
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
            roomCanvas.SetLayout(Session.Layout, Pack.Catalog);
            roomCanvas.SetWallMode(editingWalls, selectedRearWall);
            RefreshRoomInspector();
        }

        void CancelRoomsGesture() { roomCanvas?.CancelResize(); }
        void UpdateRoomZoom() { if (roomZoom != null) roomZoom.text = Mathf.RoundToInt(roomCanvas.Zoom * 100) + "%"; }

        void ResizeRoom(float width, float depth, bool commit)
        {
            var candidate = Session.Layout; candidate.room.width = width; candidate.room.depth = depth;
            if (commit)
            {
                Apply(Session.Replace(candidate, roomGestureRevision), false);
                roomCanvas.SetLayout(Session.Layout, Pack.Catalog); return;
            }
            try
            {
                Placement.ValidateLayout(Pack.Catalog, candidate, true);
                roomCanvas.SetPreview(candidate, true);
                Message("Release to resize to " + width.ToString("0.#") + " × " + depth.ToString("0.#") + " m. Furniture stays in place.");
            }
            catch (ValidationException e)
            {
                roomCanvas.SetPreview(candidate, false);
                Message("Bounds blocked: " + e.Message + " Release keeps the current cafe.");
            }
        }

        void RefreshRoomInspector()
        {
            if (screen != "Rooms" || roomInspector == null || Session == null) return;
            roomInspector.Clear(); var layout = Session.Layout; var room = layout.room;
            Section(roomInspector, editingWalls ? "Walls" : "Areas");
            if (!editingWalls)
            {
                var item = Box(roomInspector, "room-list-item");
                Box(item, "room-color-dot"); Label(item, "Cafe bounds", "room-list-name"); Label(item, (room.width * room.depth).ToString("0.#") + " m²", "room-list-area");
                Label(roomInspector, "This cafe uses one rectangular area. Polygon areas are not available yet.", "rooms-note");
                var details = Section(roomInspector, "Cafe bounds");
                Label(details, "Dimensions (m)", "rooms-field-caption");
                var dimensions = Box(details, "rooms-dimensions");
                var width = new FloatField("Width") { name = "roomsWidth", value = room.width }; width.AddToClassList("rooms-dimension-field"); dimensions.Add(width);
                var depth = new FloatField("Depth") { name = "roomsDepth", value = room.depth }; depth.AddToClassList("rooms-dimension-field"); dimensions.Add(depth);
                var area = Box(details, "rooms-area-line"); Label(area, "Area", "rooms-field-caption"); Label(area, (room.width * room.depth).ToString("0.#") + " m²", "rooms-area-value");
                Button(details, "Apply dimensions", () => { var candidate = Session.Layout; candidate.room.width = width.value; candidate.room.depth = depth.value; Apply(Session.Replace(candidate), false); }, "rooms-apply");
                Label(details, "Drag a corner on the plan to resize. Bounds cannot cut through furniture or detach a wall decoration.", "rooms-note");
            }
            else
            {
                for (var side = 0; side < 2; side++)
                {
                    int current = side;
                    var button = Button(roomInspector, (side == 0 ? "Left rear wall" : "Back rear wall") + "  ·  " + (side == 0 ? room.depth : room.width).ToString("0.#") + " m", () => { selectedRearWall = current; roomCanvas.SetWallMode(true, current); RefreshRoomInspector(); }, "room-wall-row");
                    button.EnableInClassList("active", side == selectedRearWall);
                }
                var wall = Section(roomInspector, "Selected wall");
                Label(wall, selectedRearWall == 0 ? "Left rear wall" : "Back rear wall", "subheading");
                Label(wall, "Length  " + (selectedRearWall == 0 ? room.depth : room.width).ToString("0.#") + " m\nHeight  " + room.wallHeight.ToString("0.#") + " m", "rooms-wall-details");
                var attached = layout.instances.Count(i => Pack.Catalog.items.First(p => p.id == i.furnitureId).placement == "wall" && i.quarterTurns == selectedRearWall);
                Label(wall, attached + (attached == 1 ? " attached decoration" : " attached decorations"), "rooms-note");
                Label(wall, "This cafe uses two fixed rear walls. Partitions, openings and independent wall editing are not available yet.", "rooms-note");
            }
            DrawInterior(roomInspector, layout, true);
        }
    }
}
