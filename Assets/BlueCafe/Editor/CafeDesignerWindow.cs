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
        [MenuItem("Window/BlueCafe/Designer")]
        public static void Open() { var w = GetWindow<CafeDesignerWindow>(); w.titleContent = new GUIContent("BlueCafe"); w.minSize = new Vector2(880, 560); }
        void OnEnable() { selected = null; armed = null; moving = false; needsRender = true; CafeApi.Changed += Changed; }
        void OnDisable() { CafeApi.Changed -= Changed; preview?.Dispose(); preview = null; }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/BlueCafe/Editor/CafeDesigner.uss"); if (sheet) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.AddToClassList("designer");
            var header = Row("header", rootVisualElement); AddLabel(header, "BLUE CAFE", "brand"); AddLabel(header, "A room to make your own", "subtitle");
            Button(header, "New", NewCanvas);
            var examples = new PopupField<string>(new System.Collections.Generic.List<string> { "Load an example", "Momo Friends Cafe", "Abydos Classroom" }, 0);
            examples.AddToClassList("examples"); examples.RegisterValueChangedCallback(e => {
                if (examples.index == 0) return;
                var file = examples.index == 1 ? "momo-friends" : "abydos-classroom";
                Run(() => { var path = "evidence/references/layouts/" + file + ".layout.json"; if (!File.Exists(path)) throw new CafeValidationException("EXAMPLE_UNAVAILABLE", path); CafeApi.Load(path); selected = null; armed = null; preview.FrameRoom(); RefreshSelection(); Message("Loaded " + e.newValue + ". Arrangement inferred from the source preview."); });
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
            preview?.Dispose(); preview = new CafePreview(); preview.Rebuild(CafeApi.Layout); preview.FrameRoom();
            RefreshCards(); RefreshSelection(); Dirty();
        }
        void NewCanvas()
        {
            var floor = CafeApi.Find("1"); var wall = CafeApi.Find("2");
            if (!floor.available || !wall.available) throw new CafeValidationException("ASSET_UNAVAILABLE", "The original default floor and wall models are required for a new canvas.");
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
            var room = CafeApi.Layout.room;
            roomWidth?.SetValueWithoutNotify(room.width); roomDepth?.SetValueWithoutNotify(room.depth); roomHeight?.SetValueWithoutNotify(room.wallHeight); roomGrid?.SetValueWithoutNotify(room.grid);
            preview.Rebuild(CafeApi.Layout); if (!CafeApi.Layout.instances.Any(i => i.instanceId == selected)) selected = null;
            RefreshSelection(); Dirty();
        }
        void RefreshCards()
        {
            if (cards == null || theme == null || category == null) return;
            cards.Clear(); var items = CafeApi.Search(search.value, category.index == 0 ? "" : category.value, theme.index == 0 ? "" : theme.value).OrderByDescending(i => i.available).ThenBy(i => i.name).ToArray();
            ((Label)count).text = items.Length + " items · " + items.Count(i => i.available) + " models available";
            foreach (var item in items.Take(limit))
            {
                var card = Row("card", cards); card.EnableInClassList("unavailable", !item.available); card.tooltip = item.name + " · " + item.id + "\n" + (item.available ? "Drag to place · " + item.placement : "Model unavailable; retained in catalog");
                var image = new Image { image = AssetDatabase.LoadAssetAtPath<Texture2D>(item.thumbnailPath ?? ""), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("thumb"); card.Add(image);
                AddLabel(card, item.name ?? item.id, "item-name"); AddLabel(card, item.available ? (item.placement ?? "floor").ToUpperInvariant() : "MODEL UNAVAILABLE", "item-kind");
                card.AddManipulator(new CatalogDrag(item, () => { if (item.available) { armed = item.id; Message("Placing " + item.name + ". Click the room. Escape cancels."); } else Message("Original model unavailable for " + item.name, true); }));
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
            if (e.type == EventType.DragUpdated && rect.Contains(e.mousePosition) && DragAndDrop.GetGenericData("BlueCafe.Item") is string) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.Use(); }
            if (e.type == EventType.DragPerform && rect.Contains(e.mousePosition) && DragAndDrop.GetGenericData("BlueCafe.Item") is string id)
            {
                DragAndDrop.AcceptDrag(); var p = preview.FloorPoint(local, rect); Run(() => { selected = CafeApi.Place(id, p.x, p.z); armed = null; RefreshSelection(); }); DragAndDrop.SetGenericData("BlueCafe.Item", null); e.Use();
            }
            if (!rect.Contains(e.mousePosition) && !moving) return;
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
                if (e.keyCode == KeyCode.Escape) { armed = null; moving = false; preview.Rebuild(CafeApi.Layout); selected = null; RefreshSelection(); Dirty(); e.Use(); }
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
        class CatalogDrag : PointerManipulator
        {
            readonly CafeItem item; readonly Action click; Vector2 start; bool held;
            public CatalogDrag(CafeItem item, Action click) { this.item = item; this.click = click; }
            protected override void RegisterCallbacksOnTarget() { target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move); target.RegisterCallback<PointerUpEvent>(Up); }
            protected override void UnregisterCallbacksFromTarget() { target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move); target.UnregisterCallback<PointerUpEvent>(Up); }
            void Down(PointerDownEvent e) { if (e.button != 0) return; start = e.position; held = true; target.CapturePointer(e.pointerId); e.StopPropagation(); }
            void Move(PointerMoveEvent e) { if (!held || !item.available || Vector2.Distance(start, e.position) < 5) return; held = false; target.ReleasePointer(e.pointerId); DragAndDrop.PrepareStartDrag(); DragAndDrop.SetGenericData("BlueCafe.Item", item.id); DragAndDrop.StartDrag(item.name); }
            void Up(PointerUpEvent e) { if (!held) return; held = false; target.ReleasePointer(e.pointerId); click(); e.StopPropagation(); }
        }
    }
}
