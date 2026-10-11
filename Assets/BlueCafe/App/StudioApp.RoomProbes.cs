#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using BlueCafe.Agent;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Position = BlueCafe.Core.Position;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        // Synthetic evidence uses the same public pointer event path as the UI.
        // These probes do not mutate the Session or call gesture methods directly.
        Response ProbeRooms(Arguments a)
        {
            if (a.action == "rooms-state") return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new RoomProbeState {
                layout = Session.Layout, bounds = roomCanvas?.worldBound ?? Rect.zero, gesture = roomCanvas?.GestureActive ?? false,
                selectedArea = selectedRoomArea, walls = editingWalls, selectedWall = RoomWallIds[selectedRearWall],
                preview = roomCanvas?.ProbePreview, previewValid = roomCanvas?.ProbePreviewValid ?? false,
                feedback = roomFeedback?.text, wallMoveId = wallMoveId, wallMoveWall = wallMoveWall, wallMoveValid = wallMoveValid,
                wallMoveCandidate = wallMoveCandidate, wallPreviewWall = wallPreviewWall
            }) };
            bool room = a.action.StartsWith("room-", StringComparison.Ordinal), wall = a.action.StartsWith("wall-", StringComparison.Ordinal);
            if (!room && !wall) return null;
            string action = a.action.Substring(5);
            if (action != "point" && action != "down" && action != "move" && action != "up") return null;
            VisualElement target; Vector2 point;
            if (room)
            {
                if (screen != "Rooms" || roomCanvas == null) throw new Exception("Open Rooms before using room pointer probes.");
                target = roomCanvas; point = roomCanvas.ProbePanelPoint(new Position(a.x, 0, a.z));
            }
            else
            {
                if (screen != "Build" || viewport == null) throw new Exception("Open Build before using wall pointer probes.");
                target = viewport; var world = Geometry.WallPosition(Session.Layout.room, a.wallId, a.along, a.elevation);
                var projected = View.Camera.WorldToViewportPoint(new Vector3(world.x, world.y, world.z)); var rect = viewport.worldBound;
                point = new Vector2(rect.x + projected.x * rect.width, rect.y + (1 - projected.y) * rect.height);
            }
            if (action == "point") return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new RoomProbePoint {
                panel = point, screen = new Vector2((point.x - root.worldBound.x) * Screen.width / root.worldBound.width, (point.y - root.worldBound.y) * Screen.height / root.worldBound.height),
                canvas = target.worldBound, inside = target.worldBound.Contains(point)
            }) };
            var raw = new Event { type = action == "down" ? EventType.MouseDown : action == "move" ? EventType.MouseDrag : EventType.MouseUp, mousePosition = point, button = 0, modifiers = a.shift ? EventModifiers.Shift : EventModifiers.None };
            if (action == "down") { using (var e = PointerDownEvent.GetPooled(raw)) target.SendEvent(e); }
            else if (action == "move") { using (var e = PointerMoveEvent.GetPooled(raw)) target.SendEvent(e); }
            else { using (var e = PointerUpEvent.GetPooled(raw)) target.SendEvent(e); }
            return ProbeReply();
        }
        [Serializable] sealed class RoomProbeState
        {
            public Layout layout, preview;
            public Rect bounds;
            public bool gesture, walls, previewValid, wallMoveValid;
            public string selectedArea, selectedWall, feedback, wallMoveId, wallMoveWall, wallPreviewWall;
            public Instance wallMoveCandidate;
        }
        [Serializable] sealed class RoomProbePoint { public Vector2 panel, screen; public Rect canvas; public bool inside; }
    }
}
#endif
