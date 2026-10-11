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
        string wallMoveId, wallMoveWall, wallPreviewWall;
        float wallMoveAlong, wallMoveHeight;
        Vector2 wallPointerStart, wallPanelStart;
        Instance wallMoveCandidate;
        bool wallMoveValid;
        Label wallPositionReadout;
        Session wallPreviewSession, wallDragSession;
        long wallPreviewRevision = -1;

        static string AttachmentWall(Instance instance) => string.IsNullOrEmpty(instance.wallId) ? (instance.quarterTurns == 0 ? "left" : "back") : instance.wallId;
        string PlacementHint(Item item) => item.placement == "wall"
            ? "Click a wall to place " + item.name + " at that height. Select it to drag vertically or edit Height."
            : "Click in the cafe to place " + item.name + ".";
        bool DrawWallInspector(VisualElement parent, Instance instance)
        {
            if (BuildItem(instance.furnitureId)?.placement != "wall") return false;
            var layout = Session.Layout; string wallId = AttachmentWall(instance);
            Label(parent, "Wall attachment", "build-form-label");
            Button(parent, "View this wall", () => { View.FocusWall(wallId); buildOverlay?.MarkDirtyRepaint(); }, "rooms-apply").name = "buildViewWall";
            var walls = StudioDropdown.Create<string>(Geometry.WallIds.ToList(), Array.IndexOf(Geometry.WallIds, wallId)); walls.name = "buildWall"; parent.Add(walls);
            var fields = Box(parent, "rooms-dimensions");
            var along = RoomNumber(fields, "Along (m)", "buildWallAlong", Geometry.Along(layout.room, wallId, instance.position));
            var height = RoomNumber(fields, "Height (m)", "buildWallHeight", instance.position.y);
            height.tooltip = "Mounting center above the floor. Type a height and press Enter, or use Lower / Raise.";
            var error = Label(parent, "", "rooms-note"); error.name = "wallAttachmentFeedback";
            void ApplyCoordinates(string wall, float offset, float elevation)
            {
                var result = Session.SetWallAttachment(instance.instanceId, wall, offset, elevation);
                if (result.success) Apply(result); else { invalidTransformField = true; error.text = result.message; error.EnableInClassList("rooms-error", true); Message(result.code + ": " + result.message);
                    var current = Session.Layout.instances.First(i => i.instanceId == instance.instanceId);
                    walls.SetValueWithoutNotify(AttachmentWall(current));
                    along.SetValueWithoutNotify(Geometry.Along(Session.Layout.room, AttachmentWall(current), current.position));
                    height.SetValueWithoutNotify(current.position.y);
                }
            }
            walls.RegisterValueChangedCallback(e => ApplyCoordinates(e.newValue, along.value, height.value));
            along.RegisterValueChangedCallback(e => ApplyCoordinates(walls.value, e.newValue, height.value));
            height.RegisterValueChangedCallback(e => ApplyCoordinates(walls.value, along.value, e.newValue));
            var elevationButtons = Box(parent, "build-rotation-row");
            Button(elevationButtons, "Lower", () => ApplyCoordinates(walls.value, along.value, height.value - layout.room.grid)).name = "buildWallLower";
            Button(elevationButtons, "Raise", () => ApplyCoordinates(walls.value, along.value, height.value + layout.room.grid)).name = "buildWallRaise";
            wallPositionReadout = Label(parent, wallId + " · along " + along.value.ToString("0.##") + " m · height " + height.value.ToString("0.##") + " m", "rooms-note");
            Label(parent, "Height is the mounting center above the floor. Drag the decoration up or down on the wall, or type Height and press Enter. The wall grid snaps both axes to " + layout.room.grid.ToString("0.##") + " m.", "rooms-note");
            return true;
        }
        bool PickVisibleWall(Vector2 pointer, Room room, out string wall, out Vector2 coordinates)
        {
            wall = null; coordinates = default; float nearest = float.PositiveInfinity;
            foreach (var id in Geometry.WallIds)
            {
                if (!WallFacesCamera(id) || !WallPointer(pointer, id, room, out var local, out var distance)) continue;
                if (local.x < 0 || local.x > Geometry.WallLength(room, id) || local.y < 0 || local.y > room.wallHeight || distance >= nearest) continue;
                nearest = distance; wall = id; coordinates = local;
            }
            return wall != null;
        }
        bool WallFacesCamera(string wallId)
        {
            int side = Geometry.WallTurns(wallId);
            var inward = side == 0 ? Vector3.right : side == 1 ? Vector3.back : side == 2 ? Vector3.left : Vector3.forward;
            return View != null && Vector3.Dot(inward, View.Camera.transform.forward) < -.0001f;
        }
        bool WallPointer(Vector2 panelPointer, string wallId, Room room, out Vector2 coordinates, out float distance)
        {
            coordinates = default; distance = 0;
            if (View == null || viewport == null) return false;
            var ray = View.ScreenRay(viewport.WorldToLocal(panelPointer), new Rect(Vector2.zero, viewport.contentRect.size));
            var point = Geometry.WallPosition(room, wallId, 0, 0);
            Vector3 normal = (wallId == "left" || wallId == "right") ? Vector3.right : Vector3.forward;
            if (!new Plane(normal, new Vector3(point.x, 0, point.z)).Raycast(ray, out distance)) return false;
            var hit = ray.GetPoint(distance);
            coordinates = new Vector2(Geometry.Along(room, wallId, new Position(hit.x, hit.y, hit.z)), hit.y); return true;
        }
        bool TryWallCandidate(string furnitureId, Vector2 panelPointer, out Instance candidate, out CommandResult validation)
        {
            candidate = null; validation = null; var room = Session.Layout.room;
            if (BuildItem(furnitureId)?.placement != "wall") return false;
            string picked = null; Vector2 coordinates = default; float nearest = float.PositiveInfinity;
            foreach (var id in Geometry.WallIds)
            {
                // Match the rendered cutaway: hidden front walls cannot intercept a
                // pointer aimed at the visible wall behind them.
                if (!WallFacesCamera(id)) continue;
                if (!WallPointer(panelPointer, id, room, out var local, out var distance)) continue;
                if (local.x < 0 || local.x > Geometry.WallLength(room, id) || local.y < 0 || local.y > room.wallHeight || distance >= nearest) continue;
                nearest = distance; coordinates = local; picked = id;
            }
            // Keep a red diagnostic on the previous wall when moving beyond its edge.
            if (picked == null && wallPreviewWall != null && WallFacesCamera(wallPreviewWall) && WallPointer(panelPointer, wallPreviewWall, room, out coordinates, out _)) picked = wallPreviewWall;
            if (picked == null) return false;
            wallPreviewWall = picked;
            float along = Placement.Snap(coordinates.x, room.grid), elevation = Placement.Snap(coordinates.y, room.grid);
            candidate = new Instance { instanceId = "__placement_preview", furnitureId = furnitureId, wallId = picked, wallAlong = along, position = Geometry.WallPosition(room, picked, along, elevation), quarterTurns = Geometry.WallTurns(picked) };
            if (wallPreviewSession == null || wallPreviewRevision != Session.Revision)
            { wallPreviewSession = new Session(Pack.Catalog, Session.Layout); wallPreviewRevision = Session.Revision; }
            else wallPreviewSession.OpenDocument(Session.Layout);
            validation = wallPreviewSession.PlaceOnWall(furnitureId, picked, along, elevation);
            return true;
        }
        CommandResult CommitWallPlacement(Instance candidate, long? expectedRevision = null) => Session.PlaceOnWall(candidate.furnitureId, candidate.wallId, candidate.wallAlong, candidate.position.y, expectedRevision);
        bool BeginWallMove(Vector2 panelPointer)
        {
            if (selection.Count != 1) return false;
            var instance = Session.Layout.instances.FirstOrDefault(i => selection.Contains(i.instanceId));
            if (instance == null || BuildItem(instance.furnitureId)?.placement != "wall") return false;
            var wall = AttachmentWall(instance); var layout = Session.Layout;
            if (!WallPointer(panelPointer, wall, layout.room, out wallPointerStart, out _)) return false;
            wallPanelStart = panelPointer; wallMoveId = instance.instanceId; wallMoveWall = wall; wallMoveAlong = Geometry.Along(layout.room, wall, instance.position); wallMoveHeight = instance.position.y;
            wallMoveCandidate = null; wallMoveValid = false; gestureLayout = layout; wallDragSession = new Session(Pack.Catalog, layout); gestureRevision = Session.Revision; dragging = true; return true;
        }
        bool UpdateWallMove(Vector2 panelPointer)
        {
            if (wallMoveId == null) return false;
            if (Session.Revision != gestureRevision) { CancelGesture(); Message("The cafe changed during the wall move. Try again."); return true; }
            if (wallMoveCandidate == null && (panelPointer - wallPanelStart).sqrMagnitude < 9) return true;
            var room = gestureLayout.room;
            string targetWall = wallMoveWall;
            if (!PickVisibleWall(panelPointer, room, out var pickedWall, out var coordinates))
            {
                if (!WallPointer(panelPointer, targetWall, room, out coordinates, out _)) return true;
            }
            else targetWall = pickedWall;
            // Keep the original grab offset on its wall; a transferred attachment follows
            // the pointer along the new wall while retaining the vertical grab offset.
            float along = Placement.Snap(targetWall == AttachmentWall(gestureLayout.instances.First(i => i.instanceId == wallMoveId))
                ? wallMoveAlong + coordinates.x - wallPointerStart.x : coordinates.x, room.grid);
            float elevation = Placement.Snap(wallMoveHeight + coordinates.y - wallPointerStart.y, room.grid);
            wallMoveWall = targetWall;
            wallDragSession.OpenDocument(gestureLayout); var result = wallDragSession.SetWallAttachment(wallMoveId, wallMoveWall, along, elevation);
            wallMoveValid = result.success; wallMoveCandidate = Copies.Copy(gestureLayout.instances.First(i => i.instanceId == wallMoveId));
            wallMoveCandidate.wallId = wallMoveWall; wallMoveCandidate.wallAlong = along; wallMoveCandidate.position = Geometry.WallPosition(room, wallMoveWall, along, elevation); wallMoveCandidate.quarterTurns = Geometry.WallTurns(wallMoveWall);
            if (result.success) { View.Sync(wallDragSession.Layout, gestureRevision); previewing = true; }
            else { View.Sync(Session.Layout, Session.Revision); View.ShowPlacementPreview(wallMoveCandidate, false); previewing = true; }
            string text = wallMoveWall + " · along " + along.ToString("0.##") + " m · height " + elevation.ToString("0.##") + " m";
            if (wallPositionReadout != null) wallPositionReadout.text = text;
            Message(result.success ? text + ". Release to move; Esc cancels." : result.code + ": " + result.message); return true;
        }
        bool CompleteWallMove(Vector2 panelPointer, bool insideCanvas)
        {
            if (wallMoveId == null) return false;
            if (insideCanvas) UpdateWallMove(panelPointer);
            if (wallMoveId == null) return true;
            var candidate = wallMoveCandidate; bool commit = insideCanvas && wallMoveValid && candidate != null; long revision = gestureRevision;
            CancelGesture();
            if (commit) Apply(Session.SetWallAttachment(candidate.instanceId, candidate.wallId, candidate.wallAlong, candidate.position.y, revision));
            else RefreshInspector();
            return true;
        }
        void CancelWallMove()
        {
            if (wallMoveId != null && wallPositionReadout != null && Session != null)
            {
                var instance = Session.Layout.instances.FirstOrDefault(i => i.instanceId == wallMoveId);
                if (instance != null) wallPositionReadout.text = AttachmentWall(instance) + " · along " + Geometry.Along(Session.Layout.room, AttachmentWall(instance), instance.position).ToString("0.##") + " m · height " + instance.position.y.ToString("0.##") + " m";
            }
            wallMoveId = null; wallMoveWall = null; wallPreviewWall = null; wallMoveCandidate = null; wallMoveValid = false; wallDragSession = null; wallPreviewSession = null; wallPreviewRevision = -1;
        }
        void DrawWallOutline(Painter2D painter, Func<Vector3, Vector2> project, Instance instance, Color color)
        {
            var room = Session.Layout.room; var item = BuildItem(instance.furnitureId); string wall = AttachmentWall(instance);
            float along = Geometry.Along(room, wall, instance.position), halfWidth = Placement.AttachmentWidth(Pack.Catalog, item) / 2, halfHeight = Placement.AttachmentHeight(Pack.Catalog, item) / 2;
            Vector2 Point(float offset, float height) { var p = Geometry.WallPosition(room, wall, offset, height); return project(new Vector3(p.x, p.y, p.z)); }
            var points = new[] { Point(along - halfWidth, instance.position.y - halfHeight), Point(along + halfWidth, instance.position.y - halfHeight), Point(along + halfWidth, instance.position.y + halfHeight), Point(along - halfWidth, instance.position.y + halfHeight) };
            painter.strokeColor = color; painter.lineWidth = 2.2f;
            var clip = new Rect(3, 3, Mathf.Max(0, viewport.contentRect.width - 6), Mathf.Max(0, viewport.contentRect.height - 6));
            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i]; var b = points[(i + 1) % points.Length]; if (!CanvasClip.Line(clip, ref a, ref b)) continue;
                painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke();
            }
        }
        void DrawWallGrid(Painter2D painter, Func<Vector3, Vector2> project)
        {
            string wall = wallMoveWall ?? wallPreviewWall;
            var layout = Session.Layout;
            foreach (var selected in layout.instances.Where(i => selection.Contains(i.instanceId) && BuildItem(i.furnitureId)?.placement == "wall" && !(i.instanceId == wallMoveId && wallMoveValid)))
                DrawWallOutline(painter, project, selected, new Color(.02f, .49f, 1));
            var preview = View.PlacementPreviewInstance;
            if (preview != null && BuildItem(preview.furnitureId)?.placement == "wall")
                DrawWallOutline(painter, project, preview, View.PlacementPreviewValid ? new Color(.02f, .49f, 1) : new Color(.91f, .22f, .24f));
            if (wallMoveCandidate != null && wallMoveValid) DrawWallOutline(painter, project, wallMoveCandidate, new Color(.02f, .49f, 1));
            bool wallGesture = wallMoveCandidate != null || BuildItem(armed ?? "")?.placement == "wall";
            var grids = (buildGrid || wallGesture)
                ? Geometry.WallIds.Where(id => id == wall || WallFacesCamera(id)).ToArray()
                : Array.Empty<string>();
            foreach (var gridWall in grids) DrawWallGridLines(painter, project, layout, gridWall);
        }
        void DrawWallGridLines(Painter2D painter, Func<Vector3, Vector2> project, Layout layout, string wall)
        {
            var room = layout.room; float length = Geometry.WallLength(room, wall), step = room.grid;
            // Bound grid complexity for very large imported enclosures.
            step *= Mathf.Max(1, Mathf.Ceil(Mathf.Max(length, room.wallHeight) / step / 160));
            Vector2 Point(float along, float height) { var p = Geometry.WallPosition(room, wall, along, height); return project(new Vector3(p.x, p.y, p.z)); }
            void Line(Vector2 a, Vector2 b)
            {
                var clip = new Rect(2, 2, Mathf.Max(0, viewport.contentRect.width - 4), Mathf.Max(0, viewport.contentRect.height - 4));
                if (!CanvasClip.Line(clip, ref a, ref b)) return;
                painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke();
            }
            painter.strokeColor = new Color(.18f, .52f, .84f, .65f); painter.lineWidth = 1;
            for (float along = 0; along <= length + .001f; along += step) Line(Point(along, 0), Point(along, room.wallHeight));
            for (float height = 0; height <= room.wallHeight + .001f; height += step) Line(Point(0, height), Point(length, height));
            painter.lineWidth = 2; Line(Point(length, 0), Point(length, room.wallHeight)); Line(Point(0, room.wallHeight), Point(length, room.wallHeight));
        }
    }
}
