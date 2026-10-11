using System;
using System.Collections.Generic;
using System.Linq;
using BlueCafe.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Layout = BlueCafe.Core.Layout;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        Button buildMore;
        readonly List<Button> buildCategoryButtons = new List<Button>();
        BuildSelectionOverlay buildOverlay;
        bool buildGrid;
        bool buildMoveMode;
        bool buildAreaMode, areaSelecting, areaAdditive;
        // Area selection follows the floor axes; Vector2.y stores world Z.
        Vector2 areaStart, areaEnd;
        Button buildSelectTool, buildMoveTool, buildGridTool, buildAreaTool, buildTopDownTool;
        Rect SelectionRectangle => Rect.MinMaxRect(Mathf.Min(areaStart.x, areaEnd.x), Mathf.Min(areaStart.y, areaEnd.y), Mathf.Max(areaStart.x, areaEnd.x), Mathf.Max(areaStart.y, areaEnd.y));
        Vector3[] SelectionWorldCorners
        {
            get
            {
                var rect = SelectionRectangle;
                return new[] { new Vector3(rect.xMin, .01f, rect.yMin), new Vector3(rect.xMax, .01f, rect.yMin), new Vector3(rect.xMax, .01f, rect.yMax), new Vector3(rect.xMin, .01f, rect.yMax) };
            }
        }
        Vector3? AreaFloorPoint(Vector2 panelPointer)
        {
            var local = viewport.WorldToLocal(panelPointer);
            local = new Vector2(Mathf.Clamp(local.x, 0, viewport.contentRect.width), Mathf.Clamp(local.y, 0, viewport.contentRect.height));
            return Floor(viewport.LocalToWorld(local));
        }
        bool BeginAreaSelection(Vector2 panelPointer, bool additive)
        {
            var point = AreaFloorPoint(panelPointer);
            if (!point.HasValue) return false;
            areaSelecting = true; areaAdditive = additive; areaStart = areaEnd = new Vector2(point.Value.x, point.Value.z);
            buildOverlay?.MarkDirtyRepaint(); return true;
        }
        void UpdateAreaSelection(Vector2 panelPointer)
        {
            var point = AreaFloorPoint(panelPointer);
            if (point.HasValue) areaEnd = new Vector2(point.Value.x, point.Value.z);
            buildOverlay?.MarkDirtyRepaint();
        }
        void SetAreaMode(bool enabled)
        {
            CancelGesture(); buildAreaMode = enabled; viewport?.Focus(); RefreshBuildTools();
            if (enabled) Message("Drag a floor rectangle to select furniture centers. Release to return to Select. Shift adds; Escape cancels.");
        }
        void CompleteAreaSelection()
        {
            var rectangle = SelectionRectangle;
            if (!areaAdditive) selection.Clear();
            foreach (var instance in Session.Layout.instances)
            {
                var item = BuildItem(instance.furnitureId);
                if (item == null || Placement.IsRoomStyle(item)) continue;
                var position = instance.position;
                if (position.x >= rectangle.xMin && position.x <= rectangle.xMax && position.z >= rectangle.yMin && position.z <= rectangle.yMax) selection.Add(instance.instanceId);
            }
            areaSelecting = buildAreaMode = false; RefreshInspector(); RefreshBuildTools(); buildOverlay?.MarkDirtyRepaint();
            Message(selection.Count + " furniture selected.");
        }
        public bool BuildGridVisible => screen == "Build" && (buildGrid || buildMoveMode || armed != null || catalogGesture != null || dragging);
        void RefreshBuildTools()
        {
            buildSelectTool?.EnableInClassList("active", !buildMoveMode && !buildAreaMode);
            buildAreaTool?.EnableInClassList("active", buildAreaMode);
            buildTopDownTool?.EnableInClassList("active", View != null && View.IsTopDown);
            if (buildTopDownTool != null) { buildTopDownTool.Q<Label>(className: "tool-label").text = View.IsTopDown ? "Oblique view" : "God’s-eye view"; buildTopDownTool.tooltip = View.IsTopDown ? "Return to the previous camera view" : "Look straight down at the cafe"; }
            buildMoveTool?.EnableInClassList("active", buildMoveMode);
            RefreshCommandStates();
            buildGridTool?.EnableInClassList("active", buildGrid);
        }
        Catalog buildCatalog;
        Dictionary<string, Item> buildItems;
        Item BuildItem(string id)
        {
            if (buildCatalog != Pack.Catalog) { buildCatalog = Pack.Catalog; buildItems = buildCatalog.items.ToDictionary(i => i.id, StringComparer.Ordinal); }
            return buildItems.TryGetValue(id, out var item) ? item : null;
        }
        void BuildPage()
        {
            if (topCategory == "Interior") topCategory = "Furniture";
            var row = Box(workspace, "body"); row.AddToClassList("build-body");
            var catalog = Box(row, "catalog"); catalog.name = "catalogPanel"; catalog.AddToClassList("build-catalog"); Label(catalog, "Catalog", "heading");
            buildCategoryButtons.Clear(); var tabs = Box(catalog, "build-segments");
            foreach (var cat in new[] { "Furniture", "Decoration" })
            {
                var c = cat; var button = Button(tabs, c, () => { topCategory = c; limit = 60; UpdateBuildCategories(); RefreshCards(); });
                button.EnableInClassList("active", c == topCategory); buildCategoryButtons.Add(button);
            }
            var cafeToggle = Box(catalog, "build-cafe-toggle"); Label(cafeToggle, "In this cafe");
            inCafe = new Toggle { name = "buildInCafe", tooltip = "Show only items used in this cafe" }; cafeToggle.Add(inCafe);
            var check = inCafe.Q<VisualElement>(className: "unity-toggle__checkmark"); if (check != null) check.Add(new VisualElement { name = "switchKnob", pickingMode = PickingMode.Ignore });
            check?.Q<VisualElement>("switchKnob")?.AddToClassList("build-switch-knob");
            inCafe.EnableInClassList("build-switch-on", inCafe.value);
            inCafe.RegisterValueChangedCallback(e => { inCafe.EnableInClassList("build-switch-on", e.newValue); limit = 60; RefreshCards(); });
            search = SearchField(catalog, "Search items", _ => { limit = 60; RefreshCards(); }); search.name = "buildSearch"; search.tooltip = "Search by furniture name or game ID";
            var filters = Box(catalog, "build-filter-row"); var first = Box(filters, "build-filter"); first.AddToClassList("build-filter-first"); Label(first, "Subcategory");
            category = StudioDropdown.Create<string>(BuildCategories(), 0); category.name = "buildCategory"; first.Add(category);
            var second = Box(filters, "build-filter"); Label(second, "Theme");
            theme = StudioDropdown.Create<string>(Pack.Catalog.items.Select(i => i.theme).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).Prepend("All").ToList(), 0); theme.name = "buildTheme"; second.Add(theme);
            category.RegisterValueChangedCallback(_ => { limit = 60; RefreshCards(); }); theme.RegisterValueChangedCallback(_ => { limit = 60; RefreshCards(); });
            Button(catalog, "Reset filters", ResetBuildFilters, "build-reset").name = "buildReset";
            catalogCount = Label(catalog, "", "build-catalog-count");
            var scroll = new ScrollView { name = "buildCatalogScroll", horizontalScrollerVisibility = ScrollerVisibility.Hidden }; scroll.AddToClassList("build-card-scroll"); catalog.Add(scroll); cards = Box(scroll, "cards");
            buildMore = Button(catalog, "Show more", () => { limit += 60; RefreshCards(); }, "build-more");
            var middle = Box(row, "canvas-column"); middle.name = "buildCanvas"; buildCanvas = middle; middle.style.overflow = Overflow.Hidden; middle.AddToClassList("build-canvas");
            viewport = new Image { name = "buildViewport", scaleMode = ScaleMode.StretchToFill, focusable = true }; viewport.AddToClassList("viewport"); middle.Add(viewport);
            viewport.AddManipulator(new CanvasGesture(this)); viewport.RegisterCallback<WheelEvent>(e => { if (areaSelecting || dragging || panning) CancelGesture(); View.Zoom(e.delta.y); e.StopPropagation(); });
            buildOverlay = new BuildSelectionOverlay(this); middle.Add(buildOverlay);
            var floating = Box(middle, "build-floating-tools"); floating.pickingMode = PickingMode.Ignore; var tools = Box(floating, "build-tools");
            buildSelectTool = Tool(tools, "0 selected", "select", () => { CancelGesture(); viewport.Focus(); RefreshCards(); }, "active"); buildSelectTool.name = "buildSelect"; buildSelectTool.tooltip = "Select and drag furniture. Shift adds to selection."; selectionLabel = buildSelectTool.Q<Label>(className: "tool-label"); selectionLabel.name = "buildSelectionCount";
            buildAreaTool = IconButton(tools, "area-select", () => SetAreaMode(!buildAreaMode), "Area select: drag a floor rectangle around furniture centers, then release to return to Select. Shift adds to selection."); buildAreaTool.name = "buildAreaSelect";
            IconButton(tools, "rotate", () => RotateSelection(), "Rotate selection clockwise 90° (R)").name = "buildRotate";
            Box(tools, "build-tool-separator");
            buildGridTool = IconButton(tools, "grid", () => { buildGrid = !buildGrid; RefreshBuildTools(); buildOverlay.MarkDirtyRepaint(); }, "Keep grid visible"); buildGridTool.name = "buildGrid"; buildGridTool.tooltip = "Keep the grid visible after moving or placing furniture. Floor moves snap to half a tile; wall moves snap to one tile."; RefreshBuildTools();
            IconButton(tools, "duplicate", () => Apply(Session.Duplicate(selection)), "Duplicate selection (Ctrl+D)").name = "buildDuplicate";
            IconButton(tools, "delete", () => Apply(Session.Delete(selection)), "Delete selection").name = "buildDelete";
            var viewTools = Box(middle, "build-view-tools"); Tool(viewTools, "Fit room", "frame", () => { CancelGesture(); View.FrameRoom(); }).name = "buildFitRoom";
            buildTopDownTool = Tool(viewTools, "God’s-eye view", "top-down", () => { CancelGesture(); View.ToggleTopDown(); RefreshBuildTools(); }); buildTopDownTool.name = "buildTopDown";
            IconButton(viewTools, "zoom-out", () => View.Zoom(2), "Zoom out"); IconButton(viewTools, "zoom-in", () => View.Zoom(-2), "Zoom in");
            inspector = Box(row, "inspector"); inspector.name = "inspectorPanel"; inspector.AddToClassList("build-inspector"); ConfigureBuildPanels(row, catalog, middle, inspector); ConfigureFloatingToolbar(floating, tools); RefreshCards(); RefreshInspector();
        }
        static bool BuildDecoration(Item item) => item.placement == "wall" || item.category == "FloorDecoration" || item.category == "Prop" || item.category == "Trophy" || item.category == "Decoration";
        bool InBuildCategory(Item item) => !Placement.IsRoomStyle(item) && (topCategory == "Decoration" ? BuildDecoration(item) : !BuildDecoration(item));
        static string BuildCategoryName(string category) => category == "WallDecoration" ? "Wall decoration" : category == "FloorDecoration" ? "Floor decoration" : category == "HomeAppliance" ? "Appliances" : category == "FurnitureEtc" ? "Other furniture" : category;
        List<string> BuildCategories() => Pack.Catalog.items.Where(InBuildCategory).Select(i => BuildCategoryName(i.category)).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).Prepend("All").ToList();
        void UpdateBuildCategories()
        {
            foreach (var button in buildCategoryButtons) button.EnableInClassList("active", button.text == topCategory);
            category.choices = BuildCategories(); category.SetValueWithoutNotify("All");
        }
        IEnumerable<Item> Matches(bool all = false)
        {
            string q = search?.value ?? "", c = category?.index > 0 ? category.value : "", t = theme?.index > 0 ? theme.value : "";
            var ids = Session.Layout.instances.Select(i => i.furnitureId).ToHashSet();
            return Pack.Catalog.items.Where(i => (q.Length == 0 || ((i.name ?? "") + " " + i.id).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (c.Length == 0 || BuildCategoryName(i.category) == c) && (t.Length == 0 || i.theme == t) && (all || !inCafe.value || ids.Contains(i.id)) && (all || InBuildCategory(i)));
        }
        IEnumerable<string> CurrentInteriorIds(Layout layout) => layout.instances.Where(i => BuildItem(i.furnitureId) != null && Placement.IsRoomStyle(BuildItem(i.furnitureId))).Select(i => i.furnitureId);
        void RefreshCards()
        {
            if (cards == null || screen != "Build") return;
            cards.Clear(); var items = Matches().ToArray(); var visible = items.Take(limit).ToArray(); BeginThumbnailScope(visible.Select(i => i.id).Concat(CurrentInteriorIds(Session.Layout)));
            catalogCount.text = items.Length + (inCafe.value ? " items in this cafe" : " items"); buildMore?.EnableInClassList("build-more-hidden", visible.Length >= items.Length);
            if (items.Length == 0) { Label(cards, "No items match. Try another search or clear the filters.", "build-empty-message"); return; }
            foreach (var item in visible)
            {
                var card = Box(cards, "card"); card.name = "build-card-" + item.id; CopyAssetDetails(card, item); card.EnableInClassList("selected", item.id == armed);
                var texture = Thumbnail(item); var img = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit }; img.AddToClassList("thumb"); card.Add(img);
                Label(card, item.name, "card-name"); card.tooltip = item.id + " • " + item.name + "\n" + (item.available ? "Click to place; drag into cafe" : Pack.Availability(item.id).Reason);
                if (!item.available) Label(card, "Unavailable", "error"); else if (texture == null) Label(card, "Image unavailable", "muted"); card.AddManipulator(new CatalogGesture(this, item)); card.focusable = item.available; card.RegisterCallback<KeyDownEvent>(e => { if (item.available && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space)) { armed = item.id; RefreshCards(); viewport?.Focus(); Message(PlacementHint(item)); e.StopPropagation(); } });
            }
        }
        void RefreshInspector()
        {
            if (inspector == null || screen != "Build") return; inspector.Clear(); Label(inspector, selection.Count > 0 ? "Transform" : "No selection", "heading"); if (selectionLabel != null) selectionLabel.text = selection.Count + " selected";
            var scroll = new ScrollView { horizontalScrollerVisibility = ScrollerVisibility.Hidden }; scroll.AddToClassList("build-inspector-scroll"); inspector.Add(scroll);
            var selected = Session.Layout.instances.Where(i => selection.Contains(i.instanceId)).ToArray();
            if (selected.Length > 0)
            {
                Label(scroll, selected.Length == 1 ? (BuildItem(selected[0].furnitureId)?.name ?? "Unavailable item") : selected.Length + " items selected", "build-item-name");
                if (!(selected.Length == 1 && DrawWallInspector(scroll, selected[0])))
                {
                if (selected.All(i => BuildItem(i.furnitureId)?.placement == "floor")) Label(scroll, "Floor furniture · rests on the floor. Wall decorations have separate height controls.", "rooms-note");
                var center = BuildSelectionCenter(selected); Label(scroll, "Position (m)", "build-form-label"); var position = Box(scroll, "build-position-row");
                var x = new FloatField("X") { name = "buildX", value = center.x, isDelayed = true }; x.AddToClassList("build-position-first"); position.Add(x);
                var z = new FloatField("Z") { name = "buildZ", value = center.y, isDelayed = true }; position.Add(z);
                x.RegisterValueChangedCallback(e => Safe(() => ApplyTransform(Session.Move(selection, e.newValue - center.x, 0)))); z.RegisterValueChangedCallback(e => Safe(() => ApplyTransform(Session.Move(selection, 0, e.newValue - center.y))));
                Label(scroll, "", "error").name = "buildTransformError";
                Label(scroll, "Rotation", "build-form-label"); var rotationRow = Box(scroll, "build-rotation-row");
                var turns = selected.Select(i => i.quarterTurns).Distinct().ToArray(); var directions = new List<string> { "0° (North)", "90° (East)", "180° (South)", "270° (West)" };
                if (turns.Length > 1) directions.Insert(0, "Mixed"); var rotation = StudioDropdown.Create<string>(directions, turns.Length == 1 ? turns[0] : 0); rotation.name = "buildRotation"; rotationRow.Add(rotation); rotation.SetEnabled(turns.Length == 1); rotation.tooltip = turns.Length == 1 ? "Set the selection orientation" : "Mixed orientations. Use the rotate buttons to rotate the rigid group.";
                rotation.RegisterValueChangedCallback(e => { var desired = directions.IndexOf(e.newValue) - (turns.Length > 1 ? 1 : 0); if (desired >= 0) Safe(() => RotateSelection(desired - selected[0].quarterTurns)); });
                IconButton(rotationRow, "rotate", () => RotateSelection(1), "Rotate clockwise 90°").name = "buildRotateClockwise"; IconButton(rotationRow, "undo", () => RotateSelection(-1), "Rotate counterclockwise 90°").name = "buildRotateCounterclockwise";
                }
            }
            else Label(scroll, "Select furniture to edit its position. Shift adds items to a group.", "build-empty-message");
            var room = InspectorFoldout(scroll, "Cafe · " + Session.Layout.room.width.ToString("0.##") + " × " + Session.Layout.room.depth.ToString("0.##") + " m", false); Label(room, Session.Layout.name, "build-cafe-name").name = "buildCafeName";
            Tool(room, "Rename cafe", "pen", RenameCafe, "build-rename").name = "buildRenameCafe";
            var values = Box(room, "build-room-values");
            var width = Box(values, "build-room-value"); Label(width, "Width (m)"); Label(width, Session.Layout.room.width.ToString("0.##"), "build-room-number");
            var depth = Box(values, "build-room-value"); Label(depth, "Depth (m)"); Label(depth, Session.Layout.room.depth.ToString("0.##"), "build-room-number");
            Tool(room, "Resize room", "resize", () => ShowScreen("Rooms"), "build-resize");
            var choices = CurrentInteriorIds(Session.Layout).Select(id => BuildItem(id)?.name).Where(n => n != null); var interior = InspectorFoldout(scroll, "Interior · " + string.Join(" / ", choices), true); DrawInterior(interior, Session.Layout, true); RefreshCommandStates(); buildOverlay?.MarkDirtyRepaint();
        }
        void RenameCafe()
        {
            if (Session == null) return;
            var revision = Session.Revision;
            var candidate = Session.Layout;
            InputModal("Rename cafe", "Cafe name", candidate.name, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) throw new Exception("Choose a cafe name.");
                candidate.name = name.Trim();
                var result = Session.Replace(candidate, revision);
                if (!result.success) throw new Exception(result.message);
                Apply(result, false);
                Message("Cafe renamed. Save to keep the new name.");
            });
        }
        void ApplyTransform(CommandResult result)
        {
            Apply(result); if (!result.success) { invalidTransformField = true; var label = inspector?.Q<Label>("buildTransformError"); if (label != null) label.text = result.message; }
        }
        Vector2 BuildSelectionCenter(Instance[] selected)
        {
            if (selected.Length == 1) return new Vector2(selected[0].position.x, selected[0].position.z);
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var instance in selected)
            {
                var item = BuildItem(instance.furnitureId); if (item == null) continue; float w = item.width * Pack.Catalog.gridUnit, d = item.depth * Pack.Catalog.gridUnit; if (instance.quarterTurns % 2 == 1) (w, d) = (d, w);
                minX = Mathf.Min(minX, instance.position.x - w / 2); maxX = Mathf.Max(maxX, instance.position.x + w / 2); minZ = Mathf.Min(minZ, instance.position.z - d / 2); maxZ = Mathf.Max(maxZ, instance.position.z + d / 2);
            }
            return new Vector2((minX + maxX) / 2, (minZ + maxZ) / 2);
        }
        void DrawInterior(VisualElement parent, Layout layout, bool editable)
        {
            RetainThumbnailIds(CurrentInteriorIds(layout));
            foreach (var kind in new[] { "flooring", "wallpaper", "background" })
            {
                var instance = layout.instances.FirstOrDefault(i => BuildItem(i.furnitureId)?.placement == kind); var item = instance == null ? null : BuildItem(instance.furnitureId);
                var row = Box(parent, "build-interior-choice"); row.name = "interior-" + kind; row.tooltip = item?.name ?? "None";
                var image = new Image { image = item == null ? null : Thumbnail(item), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("build-interior-thumb"); row.Add(image);
                var copy = Box(row, "build-interior-copy"); Label(copy, char.ToUpper(kind[0]) + kind.Substring(1), "build-interior-kind"); Label(copy, item?.name ?? "None", "build-interior-name");
                if (editable) { string chosenKind = kind; row.AddManipulator(new Clickable(() => OpenInteriorPicker(chosenKind))); row.focusable = true; row.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space) { OpenInteriorPicker(chosenKind); e.StopPropagation(); } }); }
            }
            if (editable) Button(parent, "Browse Interior…", () => OpenInteriorPicker("flooring"), "build-interior-browse");
        }
        void OpenInteriorPicker(string selectedKind)
        {
            var panel = Modal("Choose Interior"); panel.AddToClassList("interior-picker");
            var controls = Box(panel, "interior-picker-controls"); TextField query = null;
            var kindLabels = new List<string> { "Flooring", "Wallpaper", "Background" }; var kind = StudioDropdown.Create<string>(kindLabels, kindLabels.FindIndex(k => k.ToLowerInvariant() == selectedKind)); kind.name = "interiorKind";
            var scroll = new ScrollView { name = "interiorScroll", horizontalScrollerVisibility = ScrollerVisibility.Hidden }; scroll.AddToClassList("interior-picker-scroll"); var content = new VisualElement(); content.AddToClassList("cards"); scroll.Add(content);
            var count = new Label(); int pickerLimit = 60, fillGeneration = 0, browseLimit = 60; Vector2 browseOffset = Vector2.zero; string previousQuery = ""; Button more = null;
            void Fill(bool revealApplied, Vector2? restoreOffset = null)
            {
                int generation = ++fillGeneration;
                content.Clear(); string q = query?.value ?? "", placement = kind.value.ToLowerInvariant();
                string appliedId = Session.Layout.instances.FirstOrDefault(i => BuildItem(i.furnitureId)?.placement == placement)?.furnitureId;
                var options = Pack.Catalog.items.Where(i => i.placement == placement && ((i.name ?? "") + " " + i.id).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                int appliedIndex = Array.FindIndex(options, i => i.id == appliedId);
                if (options.Length == 0 || revealApplied && appliedIndex < 0) scroll.scrollOffset = Vector2.zero;
                if (revealApplied && appliedIndex >= pickerLimit) pickerLimit = ((appliedIndex / 60) + 1) * 60;
                var shown = options.Take(pickerLimit).ToArray(); RetainThumbnailIds(shown.Select(i => i.id)); count.text = options.Length + " choices"; more?.EnableInClassList("build-more-hidden", shown.Length >= options.Length);
                foreach (var item in shown)
                {
                    bool applied = item.id == appliedId;
                    var card = Box(content, "card"); card.name = "interior-item-" + item.id; card.EnableInClassList("selected", applied);
                    var image = new Image { image = Thumbnail(item), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("thumb"); card.Add(image); Label(card, item.name, "card-name");
                    card.tooltip = (item.available ? item.name : Pack.Availability(item.id).Reason) + (applied ? " · Applied" : "");
                    if (applied) Label(card, "Applied", "interior-applied-label");
                    if (item.available) { card.focusable = true; Action choose = () => { CloseModal(); Apply(Session.Place(item.id, 0, 0), false); }; card.AddManipulator(new Clickable(() => Safe(choose))); card.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space) { Safe(choose); e.StopPropagation(); } }); }
                    else Label(card, "Unavailable", "error");
                    bool restoreHere = restoreOffset.HasValue && item == shown[0];
                    if (revealApplied && applied || restoreHere)
                    {
                        EventCallback<GeometryChangedEvent> ready = null;
                        ready = _ =>
                        {
                            if (card.layout.width <= 0 || card.layout.height <= 0) return;
                            card.UnregisterCallback(ready);
                            scroll.schedule.Execute(() => { if (generation != fillGeneration || card.panel == null || scroll.panel == null) return; if (restoreHere) scroll.scrollOffset = restoreOffset.Value; else scroll.ScrollTo(card); });
                        };
                        card.RegisterCallback(ready);
                    }
                }
                if (options.Length == 0) Label(content, "No Interior choices match your search.", "build-empty-message");
            }
            query = SearchField(controls, "Search Interior", value =>
            {
                if (previousQuery.Length == 0 && value.Length > 0) { browseOffset = scroll.scrollOffset; browseLimit = pickerLimit; }
                bool clear = previousQuery.Length > 0 && value.Length == 0; previousQuery = value;
                pickerLimit = clear ? browseLimit : 60; Fill(false, clear ? browseOffset : (Vector2?)null);
            }); query.name = "interiorSearch";
            controls.Add(kind); kind.RegisterValueChangedCallback(_ => { pickerLimit = 60; browseLimit = 60; browseOffset = Vector2.zero; Fill(true); });
            panel.Add(scroll); var bottom = Box(panel, "interior-picker-footer"); bottom.Add(count); more = Button(bottom, "Show more", () => { pickerLimit += 60; Fill(false); }); Button(bottom, "Cancel", CloseModal); Fill(true); query.Focus();
        }
        sealed class BuildSelectionOverlay : VisualElement
        {
            readonly StudioApp app;
            public BuildSelectionOverlay(StudioApp app) { this.app = app; pickingMode = PickingMode.Ignore; AddToClassList("build-overlay"); generateVisualContent += Draw; schedule.Execute(() => { app.RefreshBuildTools(); MarkDirtyRepaint(); }).Every(33); }
            Vector2 Project(Vector3 world) { var p = app.View.Camera.WorldToViewportPoint(world); return new Vector2(p.x * contentRect.width, (1 - p.y) * contentRect.height); }
            void DashedLine(Painter2D p, Vector2 a, Vector2 b)
            {
                if (!CanvasClip.Line(new Rect(2, 2, Mathf.Max(0, contentRect.width - 4), Mathf.Max(0, contentRect.height - 4)), ref a, ref b)) return;
                var delta = b - a; float length = delta.magnitude; if (length < .1f) return;
                var direction = delta / length; p.BeginPath();
                for (float distance = 0; distance < length; distance += 10)
                {
                    p.MoveTo(a + direction * distance); p.LineTo(a + direction * Mathf.Min(distance + 4, length));
                }
                p.Stroke();
            }
            Vector2[] FootprintPoints(Bounds bounds) => new[] {
                Project(new Vector3(bounds.min.x, bounds.center.y, bounds.min.z)), Project(new Vector3(bounds.max.x, bounds.center.y, bounds.min.z)),
                Project(new Vector3(bounds.max.x, bounds.center.y, bounds.max.z)), Project(new Vector3(bounds.min.x, bounds.center.y, bounds.max.z)) };
            void Draw(MeshGenerationContext context)
            {
                if (app.View == null || app.Session == null || contentRect.width < 1 || contentRect.height < 1) return;
                var p = context.painter2D; app.DrawWallGrid(p, Project);
                if (app.areaSelecting)
                {
                    var corners = app.SelectionWorldCorners.Select(Project).ToArray();
                    var polygon = CanvasClip.Polygon(new Rect(3, 3, Mathf.Max(0, contentRect.width - 6), Mathf.Max(0, contentRect.height - 6)), corners);
                    p.strokeColor = new Color(.02f, .49f, 1); p.fillColor = new Color(.02f, .49f, 1, .12f); p.lineWidth = 2.2f;
                    if (polygon.Length > 0)
                    {
                        p.BeginPath(); p.MoveTo(polygon[0]); for (int i = 1; i < polygon.Length; i++) p.LineTo(polygon[i]); p.ClosePath(); p.Fill(); p.Stroke();
                    }
                    p.fillColor = new Color(.02f, .49f, 1); p.strokeColor = Color.white; p.lineWidth = 1.5f;
                    foreach (var corner in corners)
                    {
                        if (!new Rect(7, 7, Mathf.Max(0, contentRect.width - 14), Mathf.Max(0, contentRect.height - 14)).Contains(corner)) continue;
                        p.BeginPath(); p.Arc(corner, 4.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); p.Stroke();
                    }
                }
                if (app.BuildGridVisible)
                {
                    var room = app.Session.Layout.room; float step = room.grid * Mathf.Max(1, Mathf.Ceil(Mathf.Max(room.width, room.depth) / room.grid / 100));
                    p.strokeColor = new Color(.25f, .36f, .49f, .48f); p.lineWidth = 1;
                    for (float x = Mathf.Ceil((room.centerX - room.width / 2) / step) * step; x <= room.centerX + room.width / 2 + .001f; x += step) DashedLine(p, Project(new Vector3(x, .01f, room.centerZ - room.depth / 2)), Project(new Vector3(x, .01f, room.centerZ + room.depth / 2)));
                    for (float z = Mathf.Ceil((room.centerZ - room.depth / 2) / step) * step; z <= room.centerZ + room.depth / 2 + .001f; z += step) DashedLine(p, Project(new Vector3(room.centerX - room.width / 2, .01f, z)), Project(new Vector3(room.centerX + room.width / 2, .01f, z)));
                }
                var preview = app.View.PlacementPreviewInstance;
                if (preview != null && app.BuildItem(preview.furnitureId)?.placement != "wall")
                {
                    var previewBounds = app.View.PlacementFootprintFor(preview);
                    if (previewBounds.HasValue)
                    {
                        var footprint = CanvasClip.Polygon(new Rect(3, 3, contentRect.width - 6, contentRect.height - 6), FootprintPoints(previewBounds.Value)); if (footprint.Length == 0) return;
                        var color = app.View.PlacementPreviewValid ? new Color(.02f, .49f, 1) : new Color(.91f, .22f, .24f);
                        p.strokeColor = color; p.fillColor = new Color(color.r, color.g, color.b, .12f); p.lineWidth = 2.2f;
                        p.BeginPath(); p.MoveTo(footprint[0]); for (int i = 1; i < footprint.Length; i++) p.LineTo(footprint[i]); p.ClosePath(); p.Fill(); p.Stroke();
                    }
                }
                Bounds? bounds = null;
                foreach (var id in app.selection) { var instance = app.Session.Layout.instances.FirstOrDefault(i => i.instanceId == id); if (instance != null && app.BuildItem(instance.furnitureId)?.placement == "wall") continue; var next = app.View.FootprintBoundsFor(id); if (next.HasValue) { if (bounds.HasValue) { var combined = bounds.Value; combined.Encapsulate(next.Value); bounds = combined; } else bounds = next; } }
                if (!bounds.HasValue) return;
                var originalPoints = FootprintPoints(bounds.Value); var points = CanvasClip.Polygon(new Rect(3, 3, contentRect.width - 6, contentRect.height - 6), originalPoints); if (points.Length == 0) return;
                p.strokeColor = new Color(.02f, .49f, 1); p.lineWidth = 2.2f; p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.ClosePath(); p.Stroke();
                p.fillColor = new Color(.02f, .49f, 1); p.strokeColor = Color.white; p.lineWidth = 1.5f;
                // Corner dots indicate the selected footprint; furniture moves as a rigid group.
                foreach (var point in originalPoints) { if (!new Rect(7, 7, contentRect.width - 14, contentRect.height - 14).Contains(point)) continue; p.BeginPath(); p.Arc(point, 4.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); p.Stroke(); }
            }
        }
    }
}
