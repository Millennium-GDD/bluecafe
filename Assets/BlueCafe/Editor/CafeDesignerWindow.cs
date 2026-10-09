using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueCafe
{
    public class CafeDesignerWindow : EditorWindow
    {
        CafePreview preview;
        IMGUIContainer viewport;
        VisualElement cards, selection, status, count;
        TextField search;
        PopupField<string> category, theme;
        FloatField roomWidth, roomDepth, roomHeight, roomGrid;
        [NonSerialized] string selected, armed;
        Vector3 moveStart, moveCandidate;
        bool moving, showGrid;
        Vector2 lastMouse;
        int limit = 120;
        bool needsRender = true;
        RenderTexture currentTexture;
        GUIStyle viewportCaption;
        CatalogDrag catalogDrag;
        Label dragHint;
        [MenuItem("Window/BlueCafe/Designer")]
        public static void Open() { var w = GetWindow<CafeDesignerWindow>(); w.titleContent = new GUIContent("BlueCafe"); w.minSize = new Vector2(880, 560); }
        void OnEnable() { selected = null; armed = null; moving = false; needsRender = true; CafeApi.Changed += Changed; }
        void OnDisable() { CafeApi.Changed -= Changed; CancelGesture(true, false); preview?.Dispose(); preview = null; currentTexture = null; }
        void OnLostFocus()
        {
            // Click-to-place is a pending choice, not a held-pointer gesture.
            if (!moving && catalogDrag == null) return;
            CancelGesture(false, true); RefreshSelection(); Dirty();
        }
        void CancelGesture(bool clearSelection = false, bool restorePreview = true)
        {
            catalogDrag?.Cancel();
            bool restore = moving && restorePreview;
            moving = false; armed = null; moveStart = default; moveCandidate = default;
            if (clearSelection) selected = null;
            if (restore && preview != null) preview.Rebuild(CafeApi.Layout);
        }
        public void CreateGUI()
        {
            CancelGesture(true, false);
            rootVisualElement.Clear();
            rootVisualElement.UnregisterCallback<KeyDownEvent>(CatalogEscape, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<KeyDownEvent>(CatalogEscape, TrickleDown.TrickleDown);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/BlueCafe/Editor/CafeDesigner.uss"); if (sheet) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.AddToClassList("designer");
            var header = Row("header", rootVisualElement); AddLabel(header, "BLUE CAFE", "brand"); AddLabel(header, "A room to make your own", "subtitle");
            Button(header, "New", NewCanvas);
            var examples = new PopupField<string>(new System.Collections.Generic.List<string> { "Load an example", "Momo Friends Cafe", "Abydos Classroom" }, 0);
            examples.AddToClassList("examples"); examples.RegisterValueChangedCallback(e => {
                if (examples.index == 0) return;
                var file = examples.index == 1 ? "momo-friends" : "abydos-classroom";
                Run(() => { var path = "evidence/references/layouts/" + file + ".layout.json"; if (!File.Exists(path)) throw new CafeValidationException("EXAMPLE_UNAVAILABLE", path); CancelGesture(true, false); CafeApi.Load(path); preview.FrameRoom(); RefreshSelection(); Message("Loaded " + e.newValue + ". Arrangement inferred from the source preview."); });
                examples.SetValueWithoutNotify("Load an example");
            }); header.Add(examples);
            Button(header, "Open", () => { var p = EditorUtility.OpenFilePanel("Open cafe layout", "", "json"); if (p.Length > 0) CafeApi.Load(p); });
            Button(header, "Save", () => { var p = EditorUtility.SaveFilePanel("Save cafe layout", "", CafeApi.Layout.name, "json"); if (p.Length > 0) { CafeApi.Save(p); Message("Saved " + p); } });
            Button(header, "Capture", () => { var p = EditorUtility.SaveFilePanel("Capture cafe", "", "cafe", "png"); if (p.Length > 0) { CafeApi.Capture(p); Message("Captured " + p); } });
            Button(header, "Export scene", () => { var p = EditorUtility.SaveFilePanelInProject("Export Unity scene", "Cafe", "unity", "Choose a scene path"); if (p.Length > 0) { CafeApi.ExportScene(p); Message("Exported " + p); } });
            var body = Row("body", rootVisualElement); var workspace = Row("workspace", body);
            var toolbar = Row("toolbar", workspace);
            Button(toolbar, "Undo", CafeApi.UndoLast); Button(toolbar, "Redo", CafeApi.RedoLast); Button(toolbar, "Frame room", () => { preview.FrameRoom(); Dirty(); });
            var grid = new Toggle("Grid"); grid.RegisterValueChangedCallback(e => { showGrid = e.newValue; Dirty(); }); toolbar.Add(grid);
            AddLabel(toolbar, "Scroll to zoom · Right-drag to pan", "hint");
            viewport = new IMGUIContainer(DrawViewport); viewport.AddToClassList("viewport"); viewport.focusable = true; workspace.Add(viewport);
            // A release over a sibling control may never reach the IMGUI container.
            // Cancel its provisional move without consuming the sibling's click.
            workspace.parent.RegisterCallback<PointerUpEvent>(e => {
                if (e.button == 0 && moving && !viewport.worldBound.Contains(e.position)) { CancelGesture(); Dirty(); }
            }, TrickleDown.TrickleDown);
            selection = Row("selection", workspace);
            var room = new Foldout { text = "Room dimensions & placement grid", value = false }; room.AddToClassList("room-settings"); workspace.Add(room);
            var fields = Row("room-fields", room); var r = CafeApi.Layout.room;
            roomWidth = Number(fields, "Width", r.width, "room-field"); roomDepth = Number(fields, "Depth", r.depth, "room-field"); roomHeight = Number(fields, "Height", r.wallHeight, "room-field"); roomGrid = Number(fields, "Grid", r.grid, "room-field");
            Button(fields, "Apply", () => CafeApi.ResizeRoom(roomWidth.value, roomDepth.value, roomHeight.value, roomGrid.value));
            var catalogPanel = Row("catalog", body); AddLabel(catalogPanel, "FURNITURE LIBRARY", "catalog-title");
            search = new TextField("Search"); search.AddToClassList("search"); search.tooltip = "Search furniture by name or game id"; search.RegisterValueChangedCallback(_ => { limit = 120; RefreshCards(); }); catalogPanel.Add(search);
            var categories = CafeApi.Catalog.items.Select(i => i.category ?? "").Distinct().Where(s => s.Length > 0).OrderBy(s => s).Prepend("All categories").ToList();
            category = new PopupField<string>(categories, 0); category.AddToClassList("filters"); category.RegisterValueChangedCallback(_ => { limit = 120; RefreshCards(); }); catalogPanel.Add(category);
            var themes = CafeApi.Catalog.items.Select(i => i.theme ?? "").Distinct().Where(s => s.Length > 0).OrderBy(s => s).Prepend("All themes").ToList();
            theme = new PopupField<string>(themes, 0); theme.AddToClassList("filters"); theme.RegisterValueChangedCallback(_ => { limit = 120; RefreshCards(); }); catalogPanel.Add(theme);
            count = new Label(); count.AddToClassList("catalog-count"); catalogPanel.Add(count);
            var scroll = new ScrollView(ScrollViewMode.Vertical); catalogPanel.Add(scroll); scroll.AddToClassList("viewport"); cards = Row("cards", scroll);
            Button(catalogPanel, "Reload asset catalog", () => { CafeApi.ReloadCatalog(); CreateGUI(); });
            status = new Label("Drag furniture into the room, or click a card then click the floor."); status.AddToClassList("status"); rootVisualElement.Add(status);
            dragHint = new Label { pickingMode = PickingMode.Ignore };
            dragHint.style.position = Position.Absolute; dragHint.style.display = DisplayStyle.None;
            dragHint.style.backgroundColor = new Color(.12f, .25f, .30f, .94f); dragHint.style.color = Color.white;
            dragHint.style.paddingLeft = 10; dragHint.style.paddingRight = 10; dragHint.style.paddingTop = 6; dragHint.style.paddingBottom = 6;
            rootVisualElement.Add(dragHint);
            preview?.Dispose(); preview = new CafePreview(); preview.Rebuild(CafeApi.Layout); preview.FrameRoom();
            RefreshCards(); RefreshSelection(); Dirty();
        }
        void NewCanvas()
        {
            var floor = CafeApi.Find("1"); var wall = CafeApi.Find("2");
            if (!floor.available || !wall.available) throw new CafeValidationException("ASSET_UNAVAILABLE", "The original default floor and wall models are required for a new canvas.");
            CancelGesture(true, false);
            int group = Undo.GetCurrentGroup() + 1;
            CafeApi.NewLayout(width: 12, depth: 12); CafeApi.Place("1", 0, 0); CafeApi.Place("2", 0, 0);
            Undo.CollapseUndoOperations(group);
            selected = null; armed = null; preview.FrameRoom(); RefreshSelection(); Dirty(); Message("New cafe with original wood floor and walls. Drag furniture into the room.");
        }
        static VisualElement Row(string cls, VisualElement parent) { var e = new VisualElement(); e.AddToClassList(cls); parent.Add(e); return e; }
        static void AddLabel(VisualElement parent, string text, string cls) { var label = new Label(text); label.AddToClassList(cls); parent.Add(label); }
        void Button(VisualElement parent, string text, Action action) { var button = new Button(() => Run(action)) { text = text }; parent.Add(button); }
        static FloatField Number(VisualElement parent, string label, float value, string cls) { var f = new FloatField(label) { value = value, isDelayed = true }; f.AddToClassList(cls); parent.Add(f); return f; }
        void Run(Action action) { try { action(); } catch (Exception e) { Message(e.Message, true); } }
        void Message(string message, bool error = false) { if (status is Label l) { l.text = message; l.EnableInClassList("error", error); } }
        void Dirty() { needsRender = true; viewport?.MarkDirtyRepaint(); Repaint(); }
        void Changed()
        {
            if (preview == null) return;
            CancelGesture(false, false);
            var room = CafeApi.Layout.room;
            roomWidth?.SetValueWithoutNotify(room.width); roomDepth?.SetValueWithoutNotify(room.depth); roomHeight?.SetValueWithoutNotify(room.wallHeight); roomGrid?.SetValueWithoutNotify(room.grid);
            preview.Rebuild(CafeApi.Layout); if (!CafeApi.Layout.instances.Any(i => i.instanceId == selected)) selected = null;
            RefreshSelection(); Dirty();
        }
        void RefreshCards()
        {
            if (cards == null || theme == null || category == null) return;
            catalogDrag?.Cancel();
            cards.Clear(); var items = CafeApi.Search(search.value, category.index == 0 ? "" : category.value, theme.index == 0 ? "" : theme.value).OrderByDescending(i => i.available).ThenBy(i => i.name).ToArray();
            ((Label)count).text = items.Length + " items · " + items.Count(i => i.available) + " models available";
            foreach (var item in items.Take(limit))
            {
                var card = Row("card", cards); card.EnableInClassList("unavailable", !item.available); card.tooltip = item.name + " · " + item.id + "\n" + (item.available ? "Drag to place · " + item.placement : "Model unavailable; retained in catalog");
                var image = new Image { image = AssetDatabase.LoadAssetAtPath<Texture2D>(item.thumbnailPath ?? ""), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("thumb"); card.Add(image);
                AddLabel(card, item.name ?? item.id, "item-name"); AddLabel(card, item.available ? (item.placement ?? "floor").ToUpperInvariant() : "MODEL UNAVAILABLE", "item-kind");
                card.AddManipulator(new CatalogDrag(this, item, () => { if (item.available) { CancelGesture(); armed = item.id; Dirty(); Message("Placing " + item.name + ". Click the room. Escape cancels."); } else Message("Original model unavailable for " + item.name, true); }));
            }
            if (items.Length > limit) Button(cards, "Show more", () => { limit += 120; RefreshCards(); });
        }
        void RefreshSelection()
        {
            if (selection == null) return; selection.Clear();
            var instance = CafeApi.Layout.instances.FirstOrDefault(i => i.instanceId == selected);
            if (instance == null) { AddLabel(selection, CafeApi.Layout.name + "  ·  " + CafeApi.Layout.instances.Count + " pieces", "selection-title"); AddLabel(selection, "Select a piece to move, rotate, duplicate or remove it.", "hint"); return; }
            var item = CafeApi.Find(instance.furnitureId); AddLabel(selection, item.name + "  ·  " + instance.quarterTurns * 90 + "°", "selection-title"); var actions = Row("selection-actions", selection);
            var x = Number(actions, "X", instance.position.x, "coordinate"); var z = Number(actions, "Z", instance.position.z, "coordinate"); var y = Number(actions, "Y", instance.position.y, "coordinate"); y.SetEnabled(item.placement == "wall");
            Button(actions, "Move", () => CafeApi.Move(selected, x.value, z.value, y.value));
            Button(actions, "Rotate 90°  R", () => CafeApi.Rotate(selected)); Button(actions, "Duplicate", () => { selected = CafeApi.Duplicate(selected); RefreshSelection(); }); Button(actions, "Remove", () => CafeApi.Delete(selected));
        }
        void DrawViewport()
        {
            if (preview == null) return;
            var rect = GUILayoutUtility.GetRect(100, 10000, 100, 10000); var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                int pixelWidth = Mathf.Clamp(Mathf.RoundToInt(rect.width * EditorGUIUtility.pixelsPerPoint), 32, 4096);
                int pixelHeight = Mathf.Clamp(Mathf.RoundToInt(rect.height * EditorGUIUtility.pixelsPerPoint), 32, 4096);
                if (needsRender || !currentTexture || currentTexture.width != pixelWidth || currentTexture.height != pixelHeight) { currentTexture = preview.Render(pixelWidth, pixelHeight); needsRender = false; }
                if (viewportCaption == null) { viewportCaption = new GUIStyle(EditorStyles.boldLabel); viewportCaption.normal.textColor = new Color(.18f,.32f,.39f); }
                GUI.DrawTexture(rect, currentTexture, ScaleMode.StretchToFill, false);
                if (showGrid || CafeApi.Layout.instances.Count == 0) DrawGrid(rect);
                DrawSelection(rect);
                GUI.Label(new Rect(rect.x + 18, rect.y + 16, 460, 24), CafeApi.Layout.name.ToUpperInvariant() + "  /  " + CafeApi.Layout.instances.Count + " PIECES", viewportCaption);
                if (!string.IsNullOrEmpty(armed)) GUI.Label(new Rect(e.mousePosition.x + 18, e.mousePosition.y + 12, 300, 25), "Place " + CafeApi.Find(armed).name, EditorStyles.helpBox);
            }
            var local = e.mousePosition - rect.position;
            if (!rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseUp && e.button == 0 && moving) { CancelGesture(); Dirty(); }
                return;
            }
            if (e.type == EventType.ScrollWheel) { preview.Zoom(e.delta.y); Dirty(); e.Use(); }
            if (e.type == EventType.MouseDown)
            {
                viewport.Focus(); lastMouse = e.mousePosition;
                if (e.button == 0)
                {
                    if (!string.IsNullOrEmpty(armed)) { var p = preview.FloorPoint(local, rect); Run(() => { selected = CafeApi.Place(armed, p.x, p.z); armed = null; RefreshSelection(); }); }
                    else { selected = preview.Pick(local, rect); RefreshSelection(); var instance = CafeApi.Layout.instances.FirstOrDefault(i => i.instanceId == selected); if (instance != null) { moveStart = preview.FloorPoint(local, rect); moveCandidate = instance.position; moving = true; Undo.IncrementCurrentGroup(); } Dirty(); }
                }
                e.Use();
            }
            if (e.type == EventType.MouseDrag)
            {
                if (e.button == 1 || e.button == 2) preview.Pan(e.mousePosition - lastMouse);
                else if (moving && !string.IsNullOrEmpty(selected))
                {
                    var instance = CafeApi.Layout.instances.First(i => i.instanceId == selected); var p = instance.position + preview.FloorPoint(local, rect) - moveStart;
                    try { moveCandidate = CafeApi.Constrain(CafeApi.Find(instance.furnitureId), p, instance.quarterTurns, CafeApi.Layout.room); moveCandidate.y = instance.position.y; preview.Objects[selected].transform.localPosition = moveCandidate; } catch (CafeValidationException) { }
                }
                lastMouse = e.mousePosition; Dirty(); e.Use();
            }
            if (e.type == EventType.MouseUp && moving) { moving = false; Run(() => CafeApi.Move(selected, moveCandidate.x, moveCandidate.z, moveCandidate.y)); e.Use(); }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape) { CancelGesture(true); RefreshSelection(); Dirty(); e.Use(); }
                else if (e.keyCode == KeyCode.R && !string.IsNullOrEmpty(selected)) { Run(() => CafeApi.Rotate(selected)); e.Use(); }
                else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && !string.IsNullOrEmpty(selected)) { Run(() => CafeApi.Delete(selected)); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.D && !string.IsNullOrEmpty(selected)) { Run(() => { selected = CafeApi.Duplicate(selected); RefreshSelection(); }); e.Use(); }
                else if (e.control && e.keyCode == KeyCode.Z) { if (e.shift) CafeApi.RedoLast(); else CafeApi.UndoLast(); e.Use(); }
            }
        }
        Vector3 Screen(Vector3 point, Rect rect) { var v = preview.camera.WorldToViewportPoint(point); return new Vector3(rect.x + v.x * rect.width, rect.y + (1 - v.y) * rect.height, 0); }
        void DrawGrid(Rect rect)
        {
            var r = CafeApi.Layout.room; var spacing = Mathf.Max(r.grid, Mathf.Max(r.width, r.depth) / 40); Handles.BeginGUI(); Handles.color = new Color(.23f, .40f, .48f, .22f);
            for (float x = -r.width / 2; x <= r.width / 2 + .01f; x += spacing) Handles.DrawLine(Screen(new Vector3(x, 0, -r.depth / 2), rect), Screen(new Vector3(x, 0, r.depth / 2), rect));
            for (float z = -r.depth / 2; z <= r.depth / 2 + .01f; z += spacing) Handles.DrawLine(Screen(new Vector3(-r.width / 2, 0, z), rect), Screen(new Vector3(r.width / 2, 0, z), rect));
            Handles.EndGUI();
        }
        void DrawSelection(Rect rect)
        {
            var bounds = preview.BoundsFor(selected); if (!bounds.HasValue) return; var b = bounds.Value;
            var points = new[] { new Vector3(b.min.x,b.min.y,b.min.z), new Vector3(b.max.x,b.min.y,b.min.z), new Vector3(b.max.x,b.min.y,b.max.z), new Vector3(b.min.x,b.min.y,b.max.z), new Vector3(b.min.x,b.min.y,b.min.z) };
            Handles.BeginGUI(); Handles.color = new Color(.12f,.72f,.94f); Handles.DrawAAPolyLine(3, points.Select(p => Screen(p, rect)).ToArray()); Handles.EndGUI();
        }
        void CatalogEscape(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape || catalogDrag == null) return;
            CancelGesture(); Message("Placement cancelled."); Dirty(); e.StopPropagation();
        }
        void DropCatalogItem(CafeItem item, Vector2 panelPosition)
        {
            var bounds = viewport.worldBound;
            if (!bounds.Contains(panelPosition)) { Message("Placement cancelled. Release inside the room to place furniture."); return; }
            var point = preview.FloorPoint(panelPosition - bounds.position, new Rect(Vector2.zero, bounds.size));
            Run(() => { selected = CafeApi.Place(item.id, point.x, point.z); RefreshSelection(); Message("Placed " + item.name + "."); });
        }
        class CatalogDrag : PointerManipulator
        {
            readonly CafeDesignerWindow owner; readonly CafeItem item; readonly Action click;
            Vector2 start; bool held, dragging; int pointerId = -1;
            public CatalogDrag(CafeDesignerWindow owner, CafeItem item, Action click) { this.owner = owner; this.item = item; this.click = click; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move); target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(CaptureLost); target.RegisterCallback<PointerCancelEvent>(PointerCancelled); target.RegisterCallback<DetachFromPanelEvent>(Detached);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move); target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(CaptureLost); target.UnregisterCallback<PointerCancelEvent>(PointerCancelled); target.UnregisterCallback<DetachFromPanelEvent>(Detached);
            }
            public void Cancel()
            {
                int captured = pointerId; held = false; dragging = false; pointerId = -1;
                if (owner.catalogDrag == this) owner.catalogDrag = null;
                if (owner.dragHint != null) owner.dragHint.style.display = DisplayStyle.None;
                // Clear state before releasing: our own CaptureOut is not another drop.
                if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
            }
            void Down(PointerDownEvent e)
            {
                if (e.button != 0) return;
                owner.CancelGesture(); owner.catalogDrag = this;
                start = e.position; held = true; dragging = false; pointerId = e.pointerId;
                target.CapturePointer(pointerId); e.StopPropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (!held || e.pointerId != pointerId) return;
                if ((e.pressedButtons & 1) == 0) { Cancel(); return; }
                if (!dragging && Vector2.Distance(start, e.position) >= 5) dragging = true;
                if (dragging && owner.dragHint != null)
                {
                    bool inside = owner.viewport.worldBound.Contains(e.position);
                    owner.dragHint.text = item.available ? (inside ? "Release to place " : "Drag into the room: ") + item.name : "Original model unavailable";
                    owner.dragHint.style.left = e.position.x + 14; owner.dragHint.style.top = e.position.y + 12;
                    owner.dragHint.style.display = DisplayStyle.Flex;
                }
                e.StopPropagation();
            }
            void Up(PointerUpEvent e)
            {
                if (!held || e.pointerId != pointerId || e.button != 0) return;
                bool drop = dragging; var position = (Vector2)e.position;
                Cancel();
                if (drop) { if (item.available) owner.DropCatalogItem(item, position); }
                else if (target.worldBound.Contains(position)) click();
                e.StopPropagation();
            }
            void CaptureLost(PointerCaptureOutEvent e) { if (held && e.pointerId == pointerId) Cancel(); }
            void PointerCancelled(PointerCancelEvent e) { if (e.pointerId == pointerId) Cancel(); }
            void Detached(DetachFromPanelEvent e) { Cancel(); }
        }
    }
}
