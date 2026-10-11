using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BlueCafe.Core;
using BlueCafe.Infrastructure;
using BlueCafe.Agent;
using UnityEngine;
using UnityEngine.UIElements;
using RuntimeView = BlueCafe.Unity.RuntimeView;
using Layout = BlueCafe.Core.Layout;
using Position = BlueCafe.Core.Position;

namespace BlueCafe.App
{
    public sealed partial class StudioApp : MonoBehaviour
    {
        public Shader cafeShader;
        public static StudioApp Active { get; private set; }
        public Session Session { get; private set; }
        public Pack Pack { get; private set; }
        public RuntimeView View { get; private set; }
        DocumentStore store;
        LocalBridge bridge;
        VisualElement root, workspace, modal;
        Image viewport;
        Label title, footer, selectionLabel, catalogCount, footerStats;
        StudioIcon saveState;
        readonly HashSet<string> thumbnailPins = new HashSet<string>();
        readonly Dictionary<string, long> thumbnailUse = new Dictionary<string, long>();
        long thumbnailClock;
        TextField search;
        PopupField<string> category, theme;
        Toggle inCafe;
        VisualElement cards, inspector;
        readonly HashSet<string> selection = new HashSet<string>();
        readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
        string screen = "Build", topCategory = "Furniture", armed, savedCafeId, loadMessage;
        int limit = 60;
        CancellationTokenSource packLoad;
        CatalogGesture catalogGesture;
        long loadGeneration;
        bool busy, panning, dragging, previewing, quitting;
        int pointerId;
        Vector2 lastPointer;
        Vector3 moveStart;
        long gestureRevision;
        Layout gestureLayout;
        float lastEdit, firstDirty, lastRecoveryAttempt;
        long recoveredRevision = -1;
        string documentId = Guid.NewGuid().ToString("N");
        bool recoveryOffered, recoveryPending, invalidTransformField;
        RecoveryRecord pendingRecovery;
        long recoveryBaselineRevision;
        string recoveryBaselineDocumentId;
        VisualElement modalReturnFocus, popupEventRoot;
        Action modalCancelled;
        int handledEscapeFrame = -1;
        bool Dirty => Session != null && (Session.IsDirty || savedCafeId == null && Session.Layout.instances.Count > 0);
        string appData;

        void Awake()
        {
            Active = this; Application.targetFrameRate = 60;
            appData = Application.persistentDataPath;
            var args = Environment.GetCommandLineArgs();
            appData = Arg(args, "--data-path") ?? appData;
            store = new DocumentStore(appData); LoadViewPreferences();
            var panelTemplate = Resources.Load<PanelSettings>("StudioPanel");
            var panel = panelTemplate ? Instantiate(panelTemplate) : ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("StudioTheme");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1600, 1000); panel.match = .5f;
            var font = Resources.Load<Font>("StudioFont");
            var textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            textSettings.defaultFontAsset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font);
            panel.textSettings = textSettings;
            var document = gameObject.AddComponent<UIDocument>(); document.panelSettings = panel;
            root = document.rootVisualElement; root.style.unityFontDefinition = FontDefinition.FromSDFFont(textSettings.defaultFontAsset); root.AddToClassList("studio"); root.styleSheets.Add(Resources.Load<StyleSheet>("Studio"));
            foreach (var stylesheet in new[] { "Build", "Pages", "Rooms", "Workspace", "Themes" }) root.styleSheets.Add(Resources.Load<StyleSheet>(stylesheet));
            root.RegisterCallback<AttachToPanelEvent>(e => { if (e.target == root) BindPopupEvents(); });
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == root) UnbindPopupEvents(); });
            BindPopupEvents();
            BuildShell(); InitializeTheme();
            Application.wantsToQuit += WantsToQuit;
            try { bridge = new LocalBridge(Path.Combine(appData, "agent.json")); } catch (Exception e) { Message("Agent bridge: " + e.Message); }
            var packRoot = Arg(args, "--asset-pack") ?? store.LoadSettings().packRoot;
            if (!string.IsNullOrEmpty(packRoot)) LoadPack(packRoot, Arg(args, "--layout"));
            else ShowScreen("Settings");
        }
        static string Arg(string[] args, string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        static void Classes(VisualElement element, string classes) { if (classes != null) foreach (var cls in classes.Split(' ')) if (cls.Length > 0) element.AddToClassList(cls); }
        VisualElement Box(VisualElement parent, string cls) { var v = new VisualElement(); Classes(v, cls); parent.Add(v); return v; }
        Label Label(VisualElement parent, string text, string cls = null) { var v = new Label(text); Classes(v, cls); parent.Add(v); return v; }
        Button Button(VisualElement parent, string text, Action action, string cls = null, string tooltip = null)
        {
            var b = new Button(() => Safe(action)) { text = text, tooltip = tooltip ?? text }; Classes(b, cls);
            if (b.ClassListContains("icon")) { b.text = ""; b.Add(new StudioIcon(tooltip ?? text)); }
            b.RegisterCallback<FocusInEvent>(_ => { if (footer != null && b.ClassListContains("icon")) footer.text = b.tooltip; }); parent.Add(b); return b;
        }
        Button Tool(VisualElement parent, string text, string icon, Action action, string cls = null)
        {
            var b = Button(parent, "", action, "tool " + cls, text); b.Add(new StudioIcon(icon)); var label = Label(b, text, "tool-label"); label.pickingMode = PickingMode.Ignore; return b;
        }
        Button IconButton(VisualElement parent, string icon, Action action, string tooltip, string cls = null)
        {
            var b = Button(parent, "", action, cls, tooltip); b.AddToClassList("icon"); b.Add(new StudioIcon(icon)); return b;
        }
        VisualElement Section(VisualElement parent, string text) { var section = Box(parent, "section"); Label(section, text, "subheading"); return section; }
        TextField SearchField(VisualElement parent, string placeholder, Action<string> change)
        {
            var field = new TextField(); field.AddToClassList("search-field"); field.textEdition.placeholder = placeholder;
            field.Insert(0, new StudioIcon("search")); field.RegisterValueChangedCallback(e => change(e.newValue)); parent.Add(field); return field;
        }
        void Safe(Action action) { try { action(); } catch (Exception e) { Message(e.Message); } }
        void BuildShell()
        {
            root.Clear(); var header = Box(root, "header row");
            var logo = new StudioIcon("coffee"); logo.AddToClassList("brand-icon"); header.Add(logo); Label(header, "BlueCafe", "brand");
            Menu(header, "File", new[] { ("New cafe", (Action)(() => ReplacePrompt(NewCafe))), ("Import layout…", (Action)Import), ("Save cafe", (Action)Save), ("Export layout…", (Action)ExportLayout), ("Export image…", (Action)ExportImage) });
            Menu(header, "Edit", new[] { ("Undo", (Action)(() => Apply(Session.Undo(), false))), ("Redo", (Action)(() => Apply(Session.Redo(), false))), ("Duplicate selection", (Action)(() => Apply(Session.Duplicate(selection)))), ("Delete selection", (Action)(() => Apply(Session.Delete(selection)))) });
            Menu(header, "View", new[] { ("Fit room", (Action)(() => View?.FrameRoom())), ("View left wall", (Action)(() => View?.FocusWall("left"))), ("View back wall", (Action)(() => View?.FocusWall("back"))), ("View right wall", (Action)(() => View?.FocusWall("right"))), ("View front wall", (Action)(() => View?.FocusWall("front"))), ("Show/hide Catalog", (Action)(() => TogglePanel(true))), ("Show/hide Inspector", (Action)(() => TogglePanel(false))), ("Reset toolbar position", (Action)ResetToolbar), ("Operation status", (Action)ShowOperationStatus), ("Rooms", (Action)(() => ShowScreen("Rooms"))), ("Assets", (Action)(() => ShowScreen("Assets"))) });
            Menu(header, "Help", new[] { ("Controls", (Action)ControlsHelp), ("Commands · Ctrl+K", (Action)CommandPalette) });
            var titleRow = Box(header, "title-row row"); title = Label(titleRow, "Untitled cafe", "title"); title.name = "cafeTitle"; title.tooltip = "Double-click to rename cafe";
            title.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2 && Session != null) RenameCafe(); });
            var rename = IconButton(titleRow, "pen", () => { if (Session != null) RenameCafe(); }, "Rename cafe"); rename.name = "headerRenameCafe";
            saveState = new StudioIcon("check"); titleRow.Add(saveState);
            IconButton(header, "undo", () => Apply(Session.Undo(), false), "Undo (Ctrl+Z)").name = "commandUndo";
            IconButton(header, "redo", () => Apply(Session.Redo(), false), "Redo (Ctrl+Shift+Z)").name = "commandRedo";
            IconButton(header, "save", Save, "Save cafe (Ctrl+S)", "primary");
            IconButton(header, "image", ExportImage, "Export image");
            var body = Box(root, "body"); var ribbon = Box(body, "ribbon");
            foreach (var name in new[] { "Build", "Rooms", "Presets", "Assets" }) Navigation(ribbon, name);
            Box(ribbon, "spacer"); Navigation(ribbon, "Settings");
            workspace = Box(body, "workspace"); var bottom = Box(root, "footer row"); footerStats = Label(bottom, "", "footer-stats"); footer = Label(bottom, "Choose your prepared asset folder in Settings to begin.", "footer-message");
            ShowScreen(screen);
        }
        void Navigation(VisualElement parent, string name)
        {
            var b = Button(parent, "", () => ShowScreen(name), "nav", name); b.name = name; b.Add(new StudioIcon(name)); Label(b, name, "nav-label");
        }
        void Menu(VisualElement parent, string text, (string label, Action action)[] entries)
        {
            Button button = null; button = Button(parent, text, () => { var menu = new GenericDropdownMenu(); foreach (var entry in entries) { var action = entry.action; if (text != "Help" && !CommandEnabled(entry.label)) menu.AddDisabledItem(entry.label, false); else menu.AddItem(entry.label, false, () => { button.Focus(); Safe(action); }); } StudioDropdown.Prepare(menu); menu.DropDown(button.worldBound, button, false, false); }, "menu-button");
        }
        void ShowScreen(string next)
        {
            CaptureBrowsingContext(); CancelGesture(); screen = next; workspace.Clear(); viewport = null; inspector = null; cards = null; search = null; selectionLabel = null;
            BeginThumbnailScope(Array.Empty<string>());
            foreach (var button in root.Query<Button>(className: "nav").ToList()) button.EnableInClassList("active", button.name == next);
            if (next == "Settings") SettingsPage();
            else if (Pack == null) { var p = Box(workspace, "page"); Label(p, next, "heading"); Label(p, "Choose a prepared BlueCafe asset pack to begin.", "message"); Button(p, "Configure asset folder", () => ShowScreen("Settings"), "primary"); }
            else if (next == "Build") { BuildPage(); RestoreBuildBrowsingContext(); }
            else if (next == "Assets") AssetsPage();
            else if (next == "Presets") PresetsPage();
            else RoomsPage();
        }
        async void LoadPack(string path, string layoutPath = null)
        {
            bool creatingSession = Session == null;
            long generation = ++loadGeneration;
            packLoad?.Cancel(); packLoad = new CancellationTokenSource(); busy = true;
            var progress = new Progress<PackProgress>(p => { if (generation == loadGeneration) { loadMessage = "Verifying prepared asset files…"; if (p.Completed % 25 == 0 || p.Completed == p.Total) Message(loadMessage + " " + p.Completed + " / " + p.Total); } });
            Message("Verifying prepared asset files…");
            try
            {
                var candidate = await BlueCafe.Infrastructure.Pack.LoadAsync(path, packLoad.Token, progress);
                if (generation != loadGeneration || !this) return;
                CafeRecord startupCafe = null;
                if (creatingSession && layoutPath == null)
                {
                    var empty = new Layout { name = "Empty room", room = new Room { width = 12, depth = 12 } };
                    foreach (var id in new[] { "1", "2" })
                        empty.instances.Add(new Instance { instanceId = Guid.NewGuid().ToString("N"), furnitureId = id });
                    Placement.ValidateLayout(candidate.Catalog, empty, true);
                    startupCafe = store.EnsureDefaultCafe(empty);
                }
                Layout initial = Session?.Layout ?? startupCafe?.layout ?? DocumentStore.LoadLayout(layoutPath);
                Placement.ValidateLayout(candidate.Catalog, initial, true);
                var previewCatalog = Session?.PreviewCatalog(candidate.Catalog);
                if (previewCatalog != null && !previewCatalog.success) throw new Exception(previewCatalog.code + ": " + previewCatalog.message);
                var nextView = new RuntimeView(candidate);
                try
                {
                    nextView.Sync(initial, Session?.Revision ?? 0);
                    var settings = store.LoadSettings(); settings.packRoot = candidate.Root; store.SaveSettings(settings);
                    if (Session == null) { Session = new Session(candidate.Catalog, initial); Session.OpenDocument(initial, startupCafe != null); savedCafeId = startupCafe?.cafeId; }
                    else { var refreshed = Session.RefreshCatalog(candidate.Catalog); if (!refreshed.success) throw new Exception(refreshed.code + ": " + refreshed.message); }
                    DisposePageResources(); var oldView = View; Pack = candidate; View = nextView; View.Camera.backgroundColor = new Color(.88f, .95f, 1f); nextView = null; oldView?.Dispose(); ClearThumbnails();
                }
                finally { nextView?.Dispose(); }
                busy = false; ShowScreen(screen == "Settings" ? "Build" : screen); Refresh(); View.FrameRoom();
                Message(candidate.Catalog.items.Count(i => i.available) + " of " + candidate.Catalog.items.Length + " assets available. Source files are read-only.");
                OfferRecovery();
            }
            catch (OperationCanceledException) { if (generation == loadGeneration) { busy = false; Message("Asset verification cancelled."); } }
            catch (Exception e) { if (generation == loadGeneration) { busy = false; Message("Asset folder: " + e.Message); } }
        }
        // The cache limit is soft while more than 180 original thumbnails are displayed.
        // Never destroy a texture retained by a visible card, inspector, or picker.
        void BeginThumbnailScope(IEnumerable<string> ids) { thumbnailPins.Clear(); RetainThumbnailIds(ids); TrimThumbnails(); }
        void RetainThumbnailIds(IEnumerable<string> ids) { foreach (var id in ids) if (id != null) thumbnailPins.Add(id); }
        void TrimThumbnails()
        {
            foreach (var id in thumbnails.Keys.Where(id => !thumbnailPins.Contains(id)).OrderBy(id => thumbnailUse[id]).Take(Math.Max(0, thumbnails.Count - 180)).ToArray()) { Destroy(thumbnails[id]); thumbnails.Remove(id); thumbnailUse.Remove(id); }
        }
        Texture2D Thumbnail(Item item)
        {
            thumbnailUse[item.id] = ++thumbnailClock;
            if (thumbnails.TryGetValue(item.id, out var texture) && texture) return texture;
            if (!Pack.Availability(item.id).ThumbnailAvailable) return null;
            try { texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (!texture.LoadImage(File.ReadAllBytes(Pack.Resolve(item.thumbnailPath)))) { Destroy(texture); return null; } thumbnails[item.id] = texture; }
            catch (Exception) { if (texture) Destroy(texture); return null; }
            TrimThumbnails(); return texture;
        }
        void Refresh()
        {
            if (Session == null) return; invalidTransformField = false; CancelGesture(); selection.RemoveWhere(id => !Session.Layout.instances.Any(i => i.instanceId == id));
            View?.Sync(Session.Layout, Session.Revision); title.text = Session.Layout.name; saveState.Kind = Dirty ? "unsaved" : "check"; saveState.tooltip = Dirty ? "Unsaved changes" : "Saved";
            footerStats.text = Session.Layout.instances.Count + " items · " + Session.Layout.room.width + " × " + Session.Layout.room.depth + " m";
            if (screen == "Build") { RefreshCards(); RefreshInspector(); } else if (screen == "Rooms") RefreshRooms(); RefreshCommandStates(); lastEdit = Time.realtimeSinceStartup; if (Dirty && firstDirty == 0) firstDirty = lastEdit;
        }
        void Apply(CommandResult result, bool selectResult = true)
        {
            if (!result.success) { Message(result.code + ": " + result.message); return; }
            if (selectResult && result.instanceIds != null && result.instanceIds.Length > 0) { selection.Clear(); foreach (var id in result.instanceIds) if (Session.Layout.instances.Any(i => i.instanceId == id && !Placement.IsRoomStyle(Session.Find(i.furnitureId)))) selection.Add(id); }
            selection.IntersectWith(Session.Layout.instances.Select(i => i.instanceId));
            Refresh(); Message(showDiagnostics ? Session.Layout.instances.Count + " objects · revision " + Session.Revision : "Cafe updated.");
        }
        void Message(string text) { if (footer != null) footer.text = text; UpdateOperationStatus(text); }
        void NewCafe()
        {
            var candidate = new Layout { room = new Room { width = 12, depth = 12 } }; var result = Session.OpenDocument(candidate); if (!result.success) throw new Exception(result.message);
            FinishReplacement(result); savedCafeId = null; selection.Clear(); Apply(Session.Place("1", 0, 0)); Apply(Session.Place("2", 0, 0)); View.FrameRoom(); ShowScreen("Build");
        }
        void FinishReplacement(CommandResult result, string cafeId = null)
        {
            var oldId = documentId; documentId = Guid.NewGuid().ToString("N"); recoveredRevision = -1; firstDirty = 0;
            savedCafeId = cafeId; CancelGesture(); armed = null; selection.Clear(); Refresh(); Message("Opened " + Session.Layout.name + "."); TryClearRecovery(oldId);
        }
        void Save() { if (invalidTransformField) { Message("Correct the highlighted transform before saving."); return; } SaveThen(null); }
        void SaveThen(Action next)
        {
            if (Session == null) return;
            if (savedCafeId == null) InputModal("Save cafe", "Cafe name", Session.Layout.name, name => { if (string.IsNullOrWhiteSpace(name)) throw new Exception("Choose a cafe name."); var candidate = Session.Layout; candidate.name = name; SaveCandidate(candidate); next?.Invoke(); });
            else { SaveCandidate(Session.Layout); next?.Invoke(); }
        }
        void SaveCandidate(Layout candidate)
        {
            var revision = Session.Revision; var record = store.SaveCafe(candidate, savedCafeId); var result = Session.Replace(candidate, revision); if (!result.success) throw new Exception(result.message);
            savedCafeId = record.cafeId; Session.MarkSaved(); store.ClearRecovery(documentId, Session.Revision); recoveredRevision = Session.Revision; firstDirty = 0; Refresh(); Message("Saved " + candidate.name + ".");
        }
        void OpenCafe(string id, bool copy)
        {
            var layout = store.LoadCafe(id); Placement.ValidateLayout(Pack.Catalog, layout, true); var result = Session.OpenDocument(layout, !copy); if (!result.success) throw new Exception(result.message); FinishReplacement(result, copy ? null : id); Refresh(); View.FrameRoom(); ShowScreen("Build");
        }
        void Import()
        {
            if (Session == null) return; InputModal("Import layout", "Layout JSON path", "", path => { var candidate = DocumentStore.LoadLayout(path); Placement.ValidateLayout(Pack.Catalog, candidate, true); ReplacePrompt(() => { var result = Session.OpenDocument(candidate); if (!result.success) throw new Exception(result.message); FinishReplacement(result); savedCafeId = null; Refresh(); View.FrameRoom(); ShowScreen("Build"); }); });
        }
        void ExportLayout() { InputModal("Export layout", "JSON path", Path.Combine(appData, "Exports", "cafe.layout.json"), path => { DocumentStore.SaveLayout(path, Session.Layout); Message("Exported " + path); }); }
        void ExportImage() { if (View == null) return; CancelGesture(); View.Sync(Session.Layout, Session.Revision); InputModal("Export image", "PNG path", Path.Combine(appData, "Exports", "cafe.png"), path => { View.Capture(path, 1600, 1200); Message("Exported " + path); }); }
        void ReplacePrompt(Action next)
        {
            if (Session == null) return; CancelGesture(); if (!Dirty) { next(); return; }
            var panel = Modal("Save changes to " + Session.Layout.name + "?"); Label(panel, "Save before opening another cafe. Cancel keeps your current work.", "message"); var row = Box(panel, "row");
            Button(row, "Save", () => { CloseModal(); SaveThen(next); }, "primary"); Button(row, "Discard", () => { var oldId = documentId; CloseModal(); next(); TryClearRecovery(oldId); }); Button(row, "Cancel", CloseModal);
        }
        VisualElement Modal(string heading, Action onCancel = null)
        {
            CancelGesture(); CloseModal(); modalReturnFocus = root.panel?.focusController.focusedElement as VisualElement;
            modalCancelled = onCancel; modal = Box(root, "modal-shade"); var shade = modal; var p = Box(modal, "modal"); p.focusable = true; Label(p, heading, "heading"); p.Focus();
            // Native menus restore their invoker's focus after running an action.
            // Reclaim modal focus once that action/dismissal has finished.
            shade.schedule.Execute(() => {
                if (modal != shade) return;
                var focused = root.panel?.focusController.focusedElement as VisualElement;
                if (focused != null && shade.Contains(focused)) return;
                var input = shade.Q<TextField>(); if (input != null) input.Focus(); else p.Focus();
            });
            return p;
        }
        void CloseModal()
        {
            if (modal == null) return;
            var focus = modalReturnFocus; modal?.RemoveFromHierarchy(); modal = null; modalCancelled = null; modalReturnFocus = null;
            if (focus?.panel != null && focus.canGrabFocus) focus.Focus();
            else if (viewport?.panel != null) viewport.Focus();
        }
        void CancelModal() { var cancel = modalCancelled; CloseModal(); cancel?.Invoke(); }
        bool CancelTopPopup()
        {
            if (StudioDropdown.DismissTopMenu(root)) return true;
            if (modal == null) return false;
            CancelModal(); return true;
        }
        void BindPopupEvents()
        {
            var target = root.panel?.visualTree;
            if (target == popupEventRoot) return;
            UnbindPopupEvents(); popupEventRoot = target;
            if (target == null) return;
            target.RegisterCallback<KeyDownEvent>(KeyDown, TrickleDown.TrickleDown);
            target.RegisterCallback<NavigationCancelEvent>(NavigationCancel, TrickleDown.TrickleDown);
        }
        void UnbindPopupEvents()
        {
            if (popupEventRoot == null) return;
            popupEventRoot.UnregisterCallback<KeyDownEvent>(KeyDown, TrickleDown.TrickleDown);
            popupEventRoot.UnregisterCallback<NavigationCancelEvent>(NavigationCancel, TrickleDown.TrickleDown);
            popupEventRoot = null;
        }
        void NavigationCancel(NavigationCancelEvent e)
        {
            if (StudioDropdown.IsMenuTarget(e.target)) { handledEscapeFrame = Time.frameCount; return; }
            // The input system may also emit NavigationCancel for this Escape.
            // Do not let that same key press cancel the newly exposed popup.
            if (handledEscapeFrame == Time.frameCount) { e.StopImmediatePropagation(); return; }
            if (CancelTopPopup()) { handledEscapeFrame = Time.frameCount; e.StopImmediatePropagation(); }
        }
        void InputModal(string heading, string label, string value, Action<string> action)
        {
            var panel = Modal(heading); var owner = modal; var input = new TextField(label) { value = value }; input.name = "dialogInput"; panel.Add(input); var error = Label(panel, "", "error"); error.name = "dialogError";
            if (label.EndsWith("path", StringComparison.Ordinal)) Button(panel, "Browse…", async () => { try { var chosen = await NativeDialogs.Choose(heading, save: heading.StartsWith("Export")); if (!string.IsNullOrEmpty(chosen) && modal != null) input.value = chosen; } catch (Exception e) { error.text = e.Message; } });
            var row = Box(panel, "row");
            string verb = heading.StartsWith("Save") ? "Save" : heading.StartsWith("Import") ? "Import" : heading.StartsWith("Export") ? "Export" : "Apply";
            Button(row, verb, () => { try { string text = input.value; action(text); if (modal == owner) CloseModal(); } catch (Exception e) { error.text = e.Message; Message(e.Message); input.Focus(); } }, "primary").name = "dialogPrimary"; Button(row, "Cancel", CloseModal); input.Focus();
        }
        bool WantsToQuit()
        {
            if (quitting || Session == null) return true;
            if (!Dirty) { TryClearRecovery(documentId); return true; }
            ReplacePrompt(() => { quitting = true; Application.Quit(); }); return false;
        }
        void TryClearRecovery(string id)
        {
            try { store.ClearRecovery(id); }
            catch (Exception e) { Message("Cafe is open, but recovery cleanup failed: " + e.Message); }
        }
        void Update()
        {
            bridge?.Pump(Dispatch); PollSystemTheme();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (measureFrames) measuredFrames.Add(Time.unscaledDeltaTime);
#endif
            catalogGesture?.ValidateRevision();
            if (!recoveryPending && Dirty && recoveredRevision != Session.Revision && Time.realtimeSinceStartup - lastRecoveryAttempt > 2 && (Time.realtimeSinceStartup - lastEdit > 2 || Time.realtimeSinceStartup - firstDirty > 30))
            {
                lastRecoveryAttempt = Time.realtimeSinceStartup;
                try { if (store.WriteRecovery(Session.Layout, documentId, Session.Revision, savedCafeId)) { recoveredRevision = Session.Revision; firstDirty = Time.realtimeSinceStartup; } }
                catch (Exception e) { Message("Automatic recovery could not be written: " + e.Message); }
            }
            if (viewport != null && View != null && viewport.contentRect.width > 16) viewport.image = View.Render(Mathf.Clamp((int)viewport.contentRect.width, 32, 2048), Mathf.Clamp((int)viewport.contentRect.height, 32, 2048));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PreviewRendered();
#endif
        }
        void OfferRecovery()
        {
            if (recoveryOffered) return; recoveryOffered = true;
            pendingRecovery = store.LoadRecovery(); if (pendingRecovery == null) return;
            recoveryPending = true; recoveryBaselineRevision = Session.Revision; recoveryBaselineDocumentId = documentId; ReviewRecovery();
        }
        void ReviewRecovery()
        {
            if (pendingRecovery == null) return;
            var recovery = pendingRecovery;
            var panel = Modal("Restore automatic recovery?", () => Message("Recovery kept. Review it in Settings when you are ready; save current work manually."));
            Label(panel, recovery.layout.name + " has unsaved work from an earlier session.", "message");
            Label(panel, "Later keeps the recovery copy. Automatic recovery pauses until you restore or discard it.", "muted"); var row = Box(panel, "row");
            Button(row, "Restore", () => {
                Placement.ValidateLayout(Pack.Catalog, recovery.layout, true);
                if (Dirty && (Session.Revision != recoveryBaselineRevision || documentId != recoveryBaselineDocumentId)) { CloseModal(); ReplacePrompt(() => RestoreRecovery(recovery)); }
                else RestoreRecovery(recovery);
            }, "primary");
            Button(row, "Discard recovery", () => { store.ClearRecovery(recovery.documentId); pendingRecovery = null; recoveryPending = false; CloseModal(); if (screen == "Settings") ShowScreen("Settings"); Message("Discarded the earlier recovery copy."); });
            Button(row, "Later", CancelModal);
        }
        void RestoreRecovery(RecoveryRecord recovery)
        {
            var result = Session.OpenDocument(recovery.layout); if (!result.success) throw new Exception(result.message);
            savedCafeId = recovery.cafeId; documentId = Guid.NewGuid().ToString("N"); recoveredRevision = -1;
            store.WriteRecovery(Session.Layout, documentId, Session.Revision, savedCafeId);
            store.ClearRecovery(recovery.documentId); pendingRecovery = null; recoveryPending = false; CloseModal(); selection.Clear(); Refresh(); View.FrameRoom(); if (screen == "Settings") ShowScreen("Settings");
        }
        void OnApplicationFocus(bool focused) { if (!focused) CancelGesture(); }
        void OnDestroy() { UnbindPopupEvents(); ++loadGeneration; packLoad?.Cancel(); Application.wantsToQuit -= WantsToQuit; bridge?.Dispose(); DisposePageResources(); View?.Dispose(); ClearThumbnails(); Active = null; }
        void ClearThumbnails() { foreach (var t in thumbnails.Values) if (t) Destroy(t); thumbnails.Clear(); thumbnailUse.Clear(); thumbnailPins.Clear(); }
        void KeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                if (handledEscapeFrame == Time.frameCount) { e.StopImmediatePropagation(); return; }
                bool handled = CancelTopPopup();
                if (!handled) { handled = armed != null || dragging || panning || catalogGesture != null || buildMoveMode || buildAreaMode || areaSelecting; CancelGesture(); }
                if (handled) { handledEscapeFrame = Time.frameCount; e.StopImmediatePropagation(); }
                return;
            }
            // Open menus own navigation keys and do not edit the cafe behind them.
            if (StudioDropdown.IsMenuTarget(e.target)) return;
            if (modal != null) return;
            if (Session != null && GlobalKey(e)) return;
            for (var element = e.target as VisualElement; element != null; element = element.parent) if (element is TextField || element is FloatField || element is IntegerField) return;
            if (Session == null || modal != null) return;
            if (e.ctrlKey || e.commandKey)
            {
                if (e.keyCode == KeyCode.Z) { Apply(e.shiftKey ? Session.Redo() : Session.Undo(), false); e.StopPropagation(); }
                if (e.keyCode == KeyCode.D) { Apply(Session.Duplicate(selection)); e.StopPropagation(); }
                if (e.keyCode == KeyCode.S) { Save(); e.StopPropagation(); }
            }
            else if (e.keyCode == KeyCode.R) { RotateSelection(); e.StopPropagation(); }
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) Apply(Session.Delete(selection));
        }
        Vector3? Floor(Vector2 world) => View?.FloorPoint(viewport.WorldToLocal(world), new Rect(Vector2.zero, viewport.contentRect.size));
        void CancelGesture()
        {
            CancelWallMove(); CancelRoomsGesture();
            View?.ClearPlacementPreview();
            if (previewing && View != null && Session != null) View.Sync(Session.Layout, Session.Revision);
            if (armed != null && cards != null) foreach (var card in cards.Children()) card.EnableInClassList("selected", false);
            previewing = dragging = panning = areaSelecting = false; armed = null; buildMoveMode = buildAreaMode = false; gestureLayout = null; catalogGesture?.Cancel(); catalogGesture = null;
            if (viewport != null && viewport.HasPointerCapture(pointerId)) viewport.ReleasePointer(pointerId);
            RefreshBuildTools();
        }
        sealed class CanvasGesture : PointerManipulator
        {
            readonly StudioApp app;
            Vector2 panStart; bool panMoved; int panButton;
            public CanvasGesture(StudioApp app) { this.app = app; }
            protected override void RegisterCallbacksOnTarget() { target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move); target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost); }
            protected override void UnregisterCallbacksFromTarget() { target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move); target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost); }
            void Down(PointerDownEvent e)
            {
                if (app.modal != null || app.dragging || app.panning || app.areaSelecting) return;
                if (e.button != 0 && e.button != 1 && e.button != 2) return;
                app.pointerId = e.pointerId; app.lastPointer = e.position; target.Focus();
                if (e.button == 1 || e.button == 2) { app.panning = true; panButton = e.button; panStart = e.position; panMoved = false; target.CapturePointer(e.pointerId); e.StopPropagation(); return; }
                if (e.button != 0) return;
                if (app.buildAreaMode)
                {
                    if (app.BeginAreaSelection(e.position, e.shiftKey)) { app.gestureRevision = app.Session.Revision; target.CapturePointer(e.pointerId); }
                    e.StopPropagation(); return;
                }
                if (app.armed != null) { var furnitureId = app.armed; if (app.BuildItem(furnitureId)?.placement == "wall") { if (app.TryWallCandidate(furnitureId, e.position, out var candidate, out var validation)) { if (validation.success) app.CompletePlacement(app.CommitWallPlacement(candidate)); else app.Message(validation.code + ": " + validation.message); } else app.Message("Click a visible wall to set the mounting height."); } else { var floor = app.Floor(e.position); if (floor.HasValue) app.CompletePlacement(app.Session.Place(furnitureId, floor.Value.x, floor.Value.z)); } return; }
                string id = app.View.Pick(target.WorldToLocal(e.position), new Rect(Vector2.zero, target.contentRect.size));
                if (!e.shiftKey && !(app.buildMoveMode && id == null) && (id == null || !app.selection.Contains(id))) app.selection.Clear(); if (id != null) app.selection.Add(id); app.RefreshInspector();
                if (id != null && app.BeginWallMove(e.position)) { target.CapturePointer(e.pointerId); e.StopPropagation(); return; }
                var p = app.Floor(e.position); if (p == null) return;
                if (id != null || (app.buildMoveMode && app.selection.Count > 0)) { app.dragging = true; app.moveStart = p.Value; app.gestureRevision = app.Session.Revision; app.gestureLayout = app.Session.Layout; target.CapturePointer(e.pointerId); }
                e.StopPropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (e.pointerId != app.pointerId) return;
                if (app.areaSelecting)
                {
                    if (app.Session.Revision != app.gestureRevision) { app.CancelGesture(); app.Message("The cafe changed during area selection. Try again."); return; }
                    app.UpdateAreaSelection(e.position); e.StopPropagation(); return;
                }
                if (app.dragging && app.Session.Revision != app.gestureRevision) { app.CancelGesture(); app.Message("REVISION_CONFLICT: the cafe changed while moving furniture."); return; }
                if (app.panning)
                {
                    if (!panMoved && Vector2.Distance(panStart, e.position) >= 6) panMoved = true;
                    if (panMoved)
                    {
                        var delta = (Vector2)e.position - app.lastPointer;
                        if (panButton == 2) app.View.Orbit(delta); else app.View.Pan(delta);
                        app.lastPointer = e.position; app.RefreshBuildTools();
                    }
                    e.StopPropagation(); return;
                }
                if (!app.dragging) return; if (app.UpdateWallMove(e.position)) return; var p = app.Floor(e.position); if (p == null) return;
                var preview = new Session(app.Pack.Catalog, app.gestureLayout); var result = preview.Move(app.selection, p.Value.x - app.moveStart.x, p.Value.z - app.moveStart.z);
                if (result.success) { app.View.Sync(preview.Layout, app.gestureRevision); app.previewing = true; app.Message("Release to move selection."); } else { app.View.Sync(app.Session.Layout, app.Session.Revision); app.previewing = false; app.Message(result.code + ": " + result.message); }
            }
            void Up(PointerUpEvent e)
            {
                if (e.pointerId != app.pointerId || (!app.dragging && !app.panning && !app.areaSelecting)) return;
                if (app.areaSelecting)
                {
                    if (e.button != 0) return;
                    if (app.Session.Revision != app.gestureRevision) { app.CancelGesture(); app.Message("The cafe changed during area selection. Try again."); return; }
                    app.UpdateAreaSelection(e.position);
                    app.CompleteAreaSelection(); target.ReleasePointer(e.pointerId); e.StopPropagation(); return;
                }
                if (app.panning && e.button != panButton || app.dragging && e.button != 0) return;
                if (app.CompleteWallMove(e.position, target.worldBound.Contains(e.position))) return;
                bool move = app.dragging && target.worldBound.Contains(e.position); var p = move ? app.Floor(e.position) : null; float dx = p == null ? 0 : p.Value.x - app.moveStart.x, dz = p == null ? 0 : p.Value.z - app.moveStart.z; long revision = app.gestureRevision;
                bool context = app.panning && !panMoved && panButton == 1 && target.worldBound.Contains(e.position);
                bool retainAreaMode = app.buildAreaMode; app.CancelGesture(); app.buildAreaMode = retainAreaMode; app.RefreshBuildTools();
                if (context) app.FurnitureMenu(e.position); if (p != null && (Mathf.Abs(dx) > .001f || Mathf.Abs(dz) > .001f)) app.Apply(app.Session.Move(app.selection, dx, dz, revision));
            }
            void Lost(PointerCaptureOutEvent e) { if (e.pointerId == app.pointerId && (app.dragging || app.panning || app.areaSelecting)) app.CancelGesture(); }
        }
        public Response Dispatch(Request request)
        {
            try { return DispatchCore(request); }
            catch (Exception e)
            {
                string code = e is DocumentException document ? document.Code : e is PackException pack ? pack.Code : e is ValidationException core ? core.code : e is IOException ? "IO_ERROR" : e is ArgumentException ? "INVALID_ARGUMENT" : "COMMAND_FAILED";
                return new Response { code = code, message = e.Message, revision = Session?.Revision ?? 0 };
            }
        }
        Response DispatchCore(Request request)
        {
            if (Session == null || Pack == null) return new Response { code = "NOT_READY", message = "Configure and verify an asset folder first." };
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (request.command == "dev-ui") return ProbeUi(request.arguments ?? new Arguments());
#endif
            if (modal != null) return new Response { code = "USER_ACTION_PENDING", revision = Session.Revision };
            var a = request.arguments ?? new Arguments(); long? expected = request.expectedRevision < 0 ? null : request.expectedRevision;
            if (expected.HasValue && expected.Value != Session.Revision) { CancelGesture(); return new Response { code = "REVISION_CONFLICT", revision = Session.Revision }; }
            CommandResult result = null;
            switch (request.command)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                case "dev-ui": return ProbeUi(a);
#endif
                case "viewWall": View.FocusWall(a.wallId); return new Response { success = true, revision = Session.Revision };
                case "status": return new Response { success = true, revision = Session.Revision, json = "{\"catalog\":" + Pack.Catalog.items.Length + ",\"available\":" + Pack.AvailableCount + ",\"renderUnavailable\":" + View.Unavailable.Count + ",\"packFingerprint\":\"" + Pack.Fingerprint + "\",\"instances\":" + Session.Layout.instances.Count + ",\"dirty\":" + (Session.IsDirty ? "true" : "false") + "}" };
                case "catalog": return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(new Catalog { gridUnit = Pack.Catalog.gridUnit, items = Session.Search(a.query ?? "", a.category ?? "", a.theme ?? "").ToArray() }) };
                case "layout": return new Response { success = true, revision = Session.Revision, json = JsonUtility.ToJson(Session.Layout, true) };
                case "place": result = Session.Place(a.furnitureId, a.x, a.z, a.quarterTurns, a.elevation, expected); break;
                case "setAreas": result = Session.SetAreas(a.areas, expected); break;
                case "translateArea": result = Session.TranslateArea(a.areaId, a.dx, a.dz, expected); break;
                case "resizeAreaCorner": result = Session.ResizeAreaCorner(a.areaId, a.vertexIndex, a.x, a.z, expected); break;
                case "setAreaVertex": result = Session.SetAreaVertex(a.areaId, a.vertexIndex, a.x, a.z, expected); break;
                case "setExtent": result = Session.SetExtent(a.centerX, a.centerZ, a.extentWidth, a.extentDepth, expected); break;
                case "setWallAttachment": result = Session.SetWallAttachment(a.name, a.wallId, a.along, a.elevation, expected); break;
                case "placeOnWall": result = Session.PlaceOnWall(a.furnitureId, a.wallId, a.along, a.elevation, expected); break;
                case "setWallOpenings": result = Session.SetWallOpenings(a.openings, expected); break;
                case "move": result = Session.Move(a.instanceIds, a.dx, a.dz, expected); break;
                case "rotate": result = Session.Rotate(a.instanceIds, a.steps, expected); break;
                case "duplicate": result = Session.Duplicate(a.instanceIds, a.dx, a.dz, expected); break;
                case "delete": result = Session.Delete(a.instanceIds, expected); break;
                case "undo": result = Session.Undo(expected); break;
                case "redo": result = Session.Redo(expected); break;
                case "import": if (Session.IsDirty) return new Response { code = "UNSAVED_CHANGES", revision = Session.Revision, message = "Save or explicitly discard before replacing the cafe." }; result = Session.OpenDocument(DocumentStore.ParseLayout(a.json ?? File.ReadAllText(a.path)), false, expected); if (result.success) { FinishReplacement(result); savedCafeId = null; } break;
                case "save": var saved = Session.Layout; if (!string.IsNullOrWhiteSpace(a.name)) saved.name = a.name; if (!string.IsNullOrEmpty(a.path)) DocumentStore.SaveLayout(a.path, saved); SaveCandidate(saved); return new Response { success = true, revision = Session.Revision, path = a.path };
                case "export": DocumentStore.SaveLayout(a.path, Session.Layout); return new Response { success = true, revision = Session.Revision, path = a.path };
                case "screenshot": UnityEngine.ScreenCapture.CaptureScreenshot(a.path); return new Response { success = true, revision = Session.Revision, path = a.path };
                case "capture": if (dragging || previewing) return new Response { code = "GESTURE_ACTIVE", revision = Session.Revision }; CancelGesture(); View.Sync(Session.Layout, Session.Revision); var path = View.Capture(a.path, a.width, a.height); return new Response { success = true, revision = Session.Revision, path = path };
                default: return new Response { code = "UNKNOWN_COMMAND", message = request.command, revision = Session.Revision };
            }
            if (result.success && request.command != "import") Apply(result, request.command != "undo" && request.command != "redo");
            return new Response { success = result.success, revision = result.revision, code = result.code, message = result.message, affectedIds = result.affectedIds, instanceIds = result.instanceIds };
        }
    }
}
