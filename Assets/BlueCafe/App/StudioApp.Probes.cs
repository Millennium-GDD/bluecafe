#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Collections.Generic;
using BlueCafe.Agent;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        Response ProbeUi(Arguments a)
        {
            // Development-only evidence harness: actual control events, no model bypass.
            if (a.action == "state") return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new UiState { screen = screen, selected = selection.Count, selectionIds = selection.ToArray(), armed = armed, focusedName = (root.panel?.focusController.focusedElement as VisualElement)?.name, focusedType = root.panel?.focusController.focusedElement?.GetType().Name, focusedAttached = (root.panel?.focusController.focusedElement as VisualElement)?.panel != null, recoveryPending = recoveryPending, modal = modal != null, placementPreview = View.HasPlacementPreview, placementPreviewValid = View.PlacementPreviewValid, previewInstance = View.PlacementPreviewInstance, renderedObjects = View.ObjectCount, cachedThumbnails = thumbnails.Count, pinnedThumbnails = thumbnailPins.Count, visibleImages = root.Query<Image>().ToList().Count(i => i.image), blankImages = root.Query<Image>().ToList().Count(i => !i.image), labels = root.Query<Label>().ToList().Select(l => l.text).ToArray(), buttons = root.Query<Button>().ToList().Select(b => b.text).ToArray() }) };
            if (a.action == "screenshot") { ScreenCapture.CaptureScreenshot(a.path); return ProbeReply(); }
            if (a.action == "placement-state")
            {
                return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new PlacementState {
                    gridVisible = BuildGridVisible, manualGrid = buildGrid, dragging = dragging,
                    preview = View.PlacementPreviewInstance, previewMesh = View.PlacementPreviewBounds ?? new Bounds(),
                    selected = Session.Layout.instances.Where(i => selection.Contains(i.instanceId)).Select(i => new FootprintState {
                        instance = i, footprint = View.FootprintBoundsFor(i.instanceId) ?? new Bounds(), mesh = View.BoundsFor(i.instanceId) ?? new Bounds()
                    }).ToArray()
                }) };
            }
            if (a.action == "interior-state")
            {
                var scroll = root.Q<ScrollView>("interiorScroll");
                return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new InteriorState {
                    kind = root.Q<PopupField<string>>("interiorKind")?.value,
                    offset = scroll?.scrollOffset ?? Vector2.zero, viewport = scroll?.contentViewport.worldBound ?? Rect.zero,
                    cards = scroll == null ? new InteriorCard[0] : scroll.Query<VisualElement>(className: "card").ToList().Select(c => new InteriorCard {
                        name = c.name, selected = c.ClassListContains("selected"), bounds = c.worldBound,
                        background = ColorUtility.ToHtmlStringRGBA(c.resolvedStyle.backgroundColor)
                    }).ToArray()
                }) };
            }
            if (a.action == "escape-cycle")
            {
                void SendKey() { using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape })) (root.panel.focusController.focusedElement as VisualElement ?? root).SendEvent(e); }
                void SendCancel() { using (var e = NavigationCancelEvent.GetPooled()) (root.panel.focusController.focusedElement as VisualElement ?? root).SendEvent(e); }
                if (a.query == "navigation-first") { SendCancel(); SendKey(); }
                else { SendKey(); SendCancel(); }
                return ProbeReply();
            }
            if (a.action == "request-close") { bool closeAllowed = WantsToQuit(); return new Response { success = true, revision = Session.Revision, json = "{\"closeAllowed\":" + (closeAllowed ? "true" : "false") + "}" }; }
            if (a.action == "toggle-state")
            {
                var track = inCafe.Q<VisualElement>(className: "unity-toggle__checkmark");
                return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new ToggleState {
                    value = inCafe.value, onClass = inCafe.ClassListContains("build-switch-on"),
                    background = ColorUtility.ToHtmlStringRGBA(track.resolvedStyle.backgroundColor), trackBounds = track.worldBound,
                    knobBounds = track.Q<VisualElement>("switchKnob").worldBound, count = catalogCount.text,
                    cards = cards.Query<VisualElement>(className: "card").ToList().Select(c => c.tooltip.Split(' ')[0]).ToArray()
                }) };
            }
            if (a.action == "dropdown-state")
            {
                var panelRoot = root.panel.visualTree;
                var menu = panelRoot.Query<VisualElement>().ToList().FirstOrDefault(e => e.ClassListContains("unity-base-dropdown__container-outer"));
                var scroll = menu?.Q<ScrollView>();
                return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new DropdownState {
                    open = menu != null, horizontalVisible = scroll != null && scroll.horizontalScroller.resolvedStyle.display != DisplayStyle.None,
                    verticalVisible = scroll != null && scroll.verticalScroller.resolvedStyle.display != DisplayStyle.None,
                    visibleCheckmarks = menu == null ? 0 : menu.Query<VisualElement>(className: GenericDropdownMenu.checkmarkUssClassName).ToList().Count(e => e.resolvedStyle.display != DisplayStyle.None),
                    rows = menu == null ? new DropdownRow[0] : menu.Query<VisualElement>(className: GenericDropdownMenu.itemUssClassName).ToList().Select(e => new DropdownRow { text = e.Q<Label>()?.text, background = ColorUtility.ToHtmlStringRGBA(e.resolvedStyle.backgroundColor), bounds = e.worldBound }).ToArray(),
                    panelBounds = panelRoot.worldBound, menuBounds = menu?.worldBound ?? Rect.zero,
                    labels = menu == null ? new DropdownLabel[0] : menu.Query<Label>().ToList().Select(l => new DropdownLabel { text = l.text, bounds = l.worldBound,
                        textWidth = l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x }).ToArray(),
                    elements = menu == null ? new string[0] : menu.Query<VisualElement>().ToList().Select(e => e.GetType().Name + " " + string.Join(" ", e.GetClasses())).ToArray()
                }) };
            }
            if (a.action == "navigation")
            {
                var targetNavigation = root.panel.focusController.focusedElement as VisualElement ?? root;
                if (a.query == "Cancel") { using (var e = NavigationCancelEvent.GetPooled()) targetNavigation.SendEvent(e); }
                else if (a.query == "Submit") { using (var e = NavigationSubmitEvent.GetPooled()) targetNavigation.SendEvent(e); }
                else { using (var e = NavigationMoveEvent.GetPooled((NavigationMoveEvent.Direction)Enum.Parse(typeof(NavigationMoveEvent.Direction), a.query))) targetNavigation.SendEvent(e); }
                return ProbeReply();
            }
            if (a.action == "focus") { root.Q<VisualElement>(a.name).Focus(); return ProbeReply(); }
            if (a.action == "resolution") { Screen.SetResolution(a.width, a.height, FullScreenMode.Windowed); return ProbeReply(); }
            if (a.action == "set")
            {
                var element = root.Q<VisualElement>(a.name);
                if (element is TextField text) text.value = a.query;
                else if (element is FloatField number) number.value = a.x;
                else if (element is Toggle toggle) toggle.value = a.shift;
                else if (element is PopupField<string> popup) popup.value = a.query;
                else if (element is PopupField<int> integer) integer.value = a.width;
                else throw new Exception("Unknown field " + a.name);
                return ProbeReply();
            }
            if (a.action == "workspace") { ShowScreen(a.workspace); return ProbeReply(); }
            if (a.action == "search") { search.value = a.query; return ProbeReply(); }
            if (a.action == "shortcut")
            {
                var focused = root.panel.focusController.focusedElement as VisualElement;
                if (focused == null || focused.panel == null) throw new Exception("No attached keyboard focus for shortcut");
                using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = (KeyCode)Enum.Parse(typeof(KeyCode), a.query) })) focused.SendEvent(e);
                return ProbeReply();
            }
            if (a.action == "key") { var targetKey = a.target == "focused" ? (root.panel.focusController.focusedElement as VisualElement ?? root) : string.IsNullOrEmpty(a.name) ? root : root.Q<VisualElement>(a.name); using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = (KeyCode)Enum.Parse(typeof(KeyCode), a.query), modifiers = a.target == "ctrl" ? EventModifiers.Control : EventModifiers.None })) targetKey.SendEvent(e); return ProbeReply(); }
            if (a.action == "escape") { using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape })) (root.panel.focusController.focusedElement as VisualElement ?? root).SendEvent(e); return ProbeReply(); }
            VisualElement target;
            Vector2 point;
            if (a.target == "instance")
            {
                target = viewport; var bounds = View.BoundsFor(a.name) ?? throw new Exception("Unknown rendered instance " + a.name);
                var projected = View.Camera.WorldToViewportPoint(bounds.center); var rect = viewport.worldBound;
                point = new Vector2(rect.x + projected.x * rect.width, rect.y + (1 - projected.y) * rect.height);
            }
            else if (a.target == "menu") { target = root.panel.visualTree.Query<Label>().ToList().Single(l => l.text == a.query && l.GetFirstAncestorOfType<ScrollView>() != null); point = target.worldBound.center; }
            else if (a.target == "name") { target = root.Q<VisualElement>(a.name) ?? throw new Exception("Unknown element " + a.name); point = target.worldBound.center; }
            else if (a.target == "class") { target = root.Query<VisualElement>(className: a.name).ToList().First(); point = target.worldBound.center + new Vector2(a.dx, a.dz); }
            else if (a.target == "button")
            {
                target = root.Query<Button>().ToList().Single(b => b.text == a.query || b.Q<Label>(className: "tool-label")?.text == a.query); point = target.worldBound.center;
            }
            else if (a.target == "card")
            {
                target = root.Query<VisualElement>(className: "card").ToList().Single(c => c.tooltip.StartsWith(a.furnitureId + " •", StringComparison.Ordinal));
                point = target.worldBound.center;
                if (a.action != "down" && !string.IsNullOrEmpty(a.path)) point = FloorToPanel(a.x, a.z);
            }
            else { target = viewport; point = FloorToPanel(a.x, a.z); }
            if (a.action == "pointer-position") return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new PointerPosition { x = (point.x - root.worldBound.x) * Screen.width / root.worldBound.width, y = (point.y - root.worldBound.y) * Screen.height / root.worldBound.height, width = Screen.width, height = Screen.height, floorX = Floor(point)?.x ?? float.NaN, floorZ = Floor(point)?.z ?? float.NaN }) };
            var raw = new Event { type = a.action == "down" ? EventType.MouseDown : a.action == "move" ? EventType.MouseDrag : EventType.MouseUp, button = 0, mousePosition = point, modifiers = a.shift ? EventModifiers.Shift : EventModifiers.None };
            if (a.action == "down") { using (var e = PointerDownEvent.GetPooled(raw)) target.SendEvent(e); }
            else if (a.action == "move") { using (var e = PointerMoveEvent.GetPooled(raw)) target.SendEvent(e); }
            else { using (var e = PointerUpEvent.GetPooled(raw)) target.SendEvent(e); }
            return ProbeReply();
        }
        [Serializable] sealed class FootprintState { public Instance instance; public Bounds footprint, mesh; }
        [Serializable] sealed class PlacementState { public bool gridVisible, manualGrid, dragging; public Instance preview; public Bounds previewMesh; public FootprintState[] selected; }
        [Serializable] sealed class InteriorCard { public string name, background; public bool selected; public Rect bounds; }
        [Serializable] sealed class InteriorState { public string kind; public Vector2 offset; public Rect viewport; public InteriorCard[] cards; }
        [Serializable] sealed class ToggleState { public bool value, onClass; public string background, count; public Rect trackBounds, knobBounds; public string[] cards; }
        [Serializable] sealed class DropdownRow { public string text, background; public Rect bounds; }
        [Serializable] sealed class DropdownLabel { public string text; public Rect bounds; public float textWidth; }
        [Serializable] sealed class DropdownState { public bool open, horizontalVisible, verticalVisible; public int visibleCheckmarks; public DropdownRow[] rows; public Rect panelBounds, menuBounds; public DropdownLabel[] labels; public string[] elements; }
        [Serializable] sealed class UiState { public string screen, armed, focusedName, focusedType; public string[] selectionIds; public bool focusedAttached; public int selected, cachedThumbnails, pinnedThumbnails, visibleImages, blankImages, renderedObjects; public bool recoveryPending, modal, placementPreview, placementPreviewValid; public Instance previewInstance; public string[] labels, buttons; }
        [Serializable] sealed class PointerPosition { public float x, y, floorX, floorZ; public int width, height; }
        Vector2 FloorToPanel(float x, float z)
        {
            var point = View.Camera.WorldToViewportPoint(new Vector3(x, 0, z)); var rect = viewport.worldBound;
            return new Vector2(rect.x + point.x * rect.width, rect.y + (1 - point.y) * rect.height);
        }
        Response ProbeReply() => new Response { success = true, revision = Session.Revision, json = UnityEngine.JsonUtility.ToJson(Session.Layout), message = footer.text };
    }
}
#endif
