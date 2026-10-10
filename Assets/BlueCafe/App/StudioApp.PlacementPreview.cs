using System;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Layout = BlueCafe.Core.Layout;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        void FocusBuildViewport()
        {
            var destination = viewport;
            if (screen != "Build" || destination?.panel == null || modal != null) return;
            destination.Focus();
            // A menu may restore its now-detached origin after its command callback.
            destination.schedule.Execute(() =>
            {
                if (screen != "Build" || destination != viewport || destination.panel == null || modal != null) return;
                var focused = root.panel?.focusController.focusedElement as VisualElement;
                if (focused?.panel == null) destination.Focus();
            });
        }
        void CompletePlacement(CommandResult result)
        {
            armed = null;
            Apply(result);
            if (result.success) FocusBuildViewport();
        }
        void RotateSelection(int steps = 1)
        {
            var result = Session.Rotate(selection, steps);
            Apply(result);
            if (result.success) FocusBuildViewport();
        }
        sealed class CatalogGesture : PointerManipulator
        {
            readonly StudioApp app;
            readonly Item item;
            Vector2 start;
            bool held, moved, candidateValid;
            int capturedId;
            long revision;
            Layout baseline;
            Session previewSession;
            Instance candidate;

            public CatalogGesture(StudioApp app, Item item) { this.app = app; this.item = item; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            void Down(PointerDownEvent e)
            {
                if (e.button != 0 || !item.available || app.busy || app.modal != null || app.View == null || app.Session == null) return;
                app.CancelGesture();
                try
                {
                    baseline = app.Session.Layout;
                    previewSession = new Session(app.Pack.Catalog, baseline);
                    revision = app.Session.Revision;
                    app.gestureRevision = revision;
                    app.catalogGesture = this;
                    start = e.position; moved = false; held = true; capturedId = e.pointerId;
                    target.Focus(); target.CapturePointer(capturedId);
                    app.Message("Drag " + item.name + " into the cafe.");
                    e.StopPropagation();
                }
                catch (Exception exception) { Cancel(); app.catalogGesture = null; app.Message("Placement preview: " + exception.Message); }
            }
            public void ValidateRevision()
            {
                if (held && (app.Session == null || app.Session.Revision != revision))
                {
                    app.CancelGesture(); app.Message("Cafe changed during the drag. Drag the item again.");
                }
                else if (held && (app.busy || app.modal != null || app.screen != "Build"))
                {
                    app.CancelGesture(); app.Message("Placement cancelled.");
                }
            }
            void Move(PointerMoveEvent e)
            {
                if (!held || e.pointerId != capturedId) return;
                moved |= Vector2.Distance(start, e.position) >= 8;
                UpdatePreview(e.position); e.StopPropagation();
            }
            void UpdatePreview(Vector2 pointer)
            {
                ValidateRevision(); if (!held) return;
                candidate = null; candidateValid = false;
                if (app.screen != "Build" || app.busy || app.modal != null || app.viewport == null || !app.viewport.worldBound.Contains(pointer))
                {
                    app.View.ClearPlacementPreview(); return;
                }
                var point = app.Floor(pointer);
                if (!point.HasValue) { app.View.ClearPlacementPreview(); return; }
                try
                {
                    // Reset only the detached session. Committed revision and history stay unchanged.
                    var reset = previewSession.OpenDocument(baseline);
                    if (!reset.success) throw new Exception(reset.code + ": " + reset.message);
                    var result = previewSession.Place(item.id, point.Value.x, point.Value.z);
                    candidateValid = result.success;
                    if (candidateValid) candidate = previewSession.Layout.instances.First(i => result.instanceIds.Contains(i.instanceId));
                    else candidate = RawCandidate(point.Value);
                    candidate.instanceId = "__placement_preview";
                    app.View.ShowPlacementPreview(candidate, candidateValid);
                    app.Message(candidateValid ? "Release to place " + item.name + "." : result.code + ": " + result.message);
                }
                catch (Exception exception)
                {
                    candidate = null; candidateValid = false; app.View.ClearPlacementPreview(); app.Message("Placement preview: " + exception.Message);
                }
            }
            Instance RawCandidate(Vector3 point)
            {
                // Only a red visual diagnostic uses the unconstrained point. Valid placement
                // always comes from Session.Place; this candidate can never be committed.
                var room = baseline.room;
                var snapStep = Placement.SnapStep(item, room);
                var position = new BlueCafe.Core.Position(Placement.Snap(point.x, snapStep), 0, Placement.Snap(point.z, snapStep));
                int turns = 0;
                if (item.placement == "wall")
                {
                    if (Math.Abs(position.x + room.width / 2) < Math.Abs(position.z - room.depth / 2)) position.x = -room.width / 2;
                    else position.z = room.depth / 2;
                    position.y = Placement.Snap(room.wallHeight / 2, room.grid);
                    turns = Math.Abs(position.x + room.width / 2) < .001f ? 0 : 1;
                }
                if (Placement.IsRoomStyle(item)) position = new BlueCafe.Core.Position();
                return new Instance { instanceId = "__placement_preview", furnitureId = item.id, position = position, quarterTurns = turns };
            }
            void Up(PointerUpEvent e)
            {
                if (!held || e.pointerId != capturedId || e.button != 0) return;
                UpdatePreview(e.position); if (!held) return;
                bool drop = app.viewport != null && app.viewport.worldBound.Contains(e.position);
                var placed = candidateValid ? Copies.Copy(candidate) : null;
                bool arm = !drop && !moved && Vector2.Distance(start, e.position) < 8;
                var expected = revision;
                held = false; app.catalogGesture = null; Cancel();
                e.StopPropagation();
                if (drop && placed != null)
                {
                    app.CompletePlacement(app.Session.Place(item.id, placed.position.x, placed.position.z, placed.quarterTurns, placed.position.y, expected));
                }
                else if (arm)
                {
                    app.armed = item.id; app.RefreshCards(); app.viewport?.Focus(); app.Message("Click in the cafe to place " + item.name + ".");
                }
                else if (!drop) app.Message("Placement cancelled.");
            }
            public void Cancel()
            {
                held = false; candidate = null; candidateValid = false; baseline = null; previewSession = null;
                app.View?.ClearPlacementPreview();
                if (target != null && target.HasPointerCapture(capturedId)) target.ReleasePointer(capturedId);
            }
            void Lost(PointerCaptureOutEvent e)
            {
                if (!held || e.pointerId != capturedId) return;
                app.CancelGesture(); app.Message("Placement cancelled.");
            }
        }
    }
}
